extends SceneTree

const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")

# Minimal logic-thread state double: snapshot() counts expensive full-state
# builds, so this test catches regressions even when output values are equal.
class CountingWorld:
	extends RefCounted

	var state := 0
	var world_ready := true
	var map_epoch := 7
	var pending_world_ready_ack_epoch := 0
	var pending_world_ready_ack_tick := -1
	var last_world_ready_ack_epoch := 7
	var resync_pending := false
	var resync_request_sent := false
	var waiting_for_new_map := false
	var pvp_known := true
	var pvp_sequence := 0
	var pending_pvp_sequence := 0
	var builds := 0

	func snapshot() -> Dictionary:
		builds += 1
		return {"state": state, "world_ready": world_ready, "map_epoch": map_epoch}


func _fail(reason: String) -> void:
	push_error("CLIENT_IDLE_PUBLICATION_FAIL " + reason)
	quit(1)


func _initialize() -> void:
	var session = Session.new()
	var world = CountingWorld.new()
	world.state = WorldState.STATE_IN_WORLD
	session._world = world
	session._published = {}
	session._maybe_finish_world_entry()
	if world.builds != 1 or not session._has_entered_world:
		_fail("first WorldReady must publish once")
		return
	var first: Dictionary = session.snapshot_since(-1)
	if first.is_empty() or int(first.get("revision",-1)) != 1 or not bool((first.get("snapshot",{}) as Dictionary).get("world_ready",false)):
		_fail("atomic initial versioned snapshot missing")
		return
	if not session.snapshot_since(1).is_empty():
		_fail("unchanged version unexpectedly returned a deep snapshot")
		return
	# Returned snapshots remain independent copies, even with version caching.
	(first.snapshot as Dictionary)["world_ready"] = false
	if not bool(session.snapshot().get("world_ready",false)):
		_fail("caller mutation leaked into the published snapshot")
		return

	for _i in range(100):
		session._flush_control_frames()
		session._maybe_query_pvp_mode()
		session._maybe_finish_world_entry()
	if world.builds != 1:
		_fail("idle control checks built %d extra snapshots" % (world.builds - 1))
		return
	if int(session._published.get("map_epoch",0)) != 7 or not bool(session._published.get("world_ready",false)):
		_fail("first snapshot contents changed")
		return

	# Reconnect completion is a true transition: publish it once, then idle.
	session._reconnecting = true
	session._reconnect_attempt = 2
	session._maybe_finish_world_entry()
	if world.builds != 2 or session._reconnecting or session._reconnect_attempt != 0 or session._reconnect_count != 1 or int(session._published.get("reconnect_count",0)) != 1:
		_fail("reconnect completion not published exactly once")
		return
	for _i in range(100):
		session._flush_control_frames()
		session._maybe_query_pvp_mode()
		session._maybe_finish_world_entry()
	if world.builds != 2 or session._reconnect_count != 1:
		_fail("reconnect idle loop copied snapshot or incremented counter")
		return
	var second: Dictionary = session.snapshot_since(1)
	if second.is_empty() or int(second.get("revision",-1)) != 2 or int((second.get("snapshot",{}) as Dictionary).get("reconnect_count",0)) != 1 or not session.snapshot_since(2).is_empty():
		_fail("reconnect publication version/one-copy gate failed")
		return
	print("CLIENT_IDLE_PUBLICATION_PASS idle_polls=200 full_snapshot_builds=2 entry_once=true reconnect_once=true versioned_ui_copy=true")
	quit(0)
