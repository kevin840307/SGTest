
extends SceneTree

const ClientSession = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")

class MapWorld:
	extends RefCounted
	var state := WorldState.STATE_IN_WORLD
	var actor_id := 11
	var map_epoch := 2
	var player_dead := false
	var world_ready := true
	var resync_pending := false
	var pending_world_ready_ack_epoch := 0
	var last_world_ready_ack_epoch := 2
	var waiting_for_new_map := false
	var movement_sequence := 0
	var npc_interaction_sequence := 0
	var pending_npc_sequence := 0
	var self_state: Dictionary = {}
	var monsters: Dictionary = {501: {"current_hp": 90, "state": 0, "x": 40.0, "y": 8.0}}

	func snapshot() -> Dictionary:
		return {
			"state":state, "actor_id":actor_id, "map_epoch":map_epoch,
			"player_dead":player_dead, "world_ready":world_ready,
			"resync_pending":resync_pending,
			"pending_world_ready_ack_epoch":pending_world_ready_ack_epoch,
			"last_world_ready_ack_epoch":last_world_ready_ack_epoch,
			"waiting_for_new_map":waiting_for_new_map,
			"movement_sequence":movement_sequence,
			"npc_interaction_sequence":npc_interaction_sequence,
			"pending_npc_sequence":pending_npc_sequence,
		}

func _fail(reason: String) -> void:
	push_error("MAP_LOCAL_INTENT_EPOCH_FAIL " + reason)
	quit(1)

func _assert_bound(session, kind: String, enqueue: Callable) -> bool:
	session._intents.clear()
	if not enqueue.call():
		_fail("%s enqueue rejected" % kind)
		return false
	if session._intents.size() != 1:
		_fail("%s queue size" % kind)
		return false
	var intent: Dictionary = session._intents[0]
	if int(intent.get("actor_id",0)) != 11 or int(intent.get("map_epoch",0)) != 2:
		_fail("%s missing actor/map binding: %s" % [kind,intent])
		return false
	return true

func _assert_stale_consumed(session, intent: Dictionary, processor: Callable) -> bool:
	session._intents.clear()
	intent["_token"] = 100 + session._intent_token
	session._intents.append(intent)
	processor.call(intent)
	if not session._intents.is_empty():
		_fail("%s survived MapEpoch change" % str(intent.get("type","")))
		return false
	return true

func _initialize() -> void:
	var session = ClientSession.new()
	session._running = true
	session._published = {
		"state":WorldState.STATE_IN_WORLD,
		"actor_id":11, "map_epoch":2,
		"player_dead":false, "world_ready":true,
		"resync_pending":false, "waiting_for_new_map":false,
		"pending_world_ready_ack_epoch":0, "last_world_ready_ack_epoch":2,
	}
	if not _assert_bound(session,"move",func(): return session.move_target(40,8)):
		return
	if not _assert_bound(session,"stop_move",func(): return session.stop_movement()):
		return
	if not _assert_bound(session,"attack_monster",func(): return session.attack_monster(501)):
		return
	if not _assert_bound(session,"npc",func(): return session.npc_interact(46)):
		return

	# Auto-approach cancellation rewrites attack -> stop in the same FIFO slot.
	# The rewrite must retain the generation binding too.
	session._intents.clear()
	var chase := {
		"type":"attack_monster","entity_id":501,"phase":"await_move","move_sequence":9,
		"actor_id":11,"map_epoch":2,"_token":900,
	}
	session._intents.append(chase)
	session._cancel_attack_chase(900,chase)
	if session._intents.size()!=1 or str(session._intents[0].get("type",""))!="stop_move" \
			or int(session._intents[0].get("actor_id",0))!=11 \
			or int(session._intents[0].get("map_epoch",0))!=2:
		_fail("attack chase stop rewrite lost map binding")
		return

	var world = MapWorld.new()
	session._world = world
	if not _assert_stale_consumed(session,
		{"type":"move","actor_id":11,"map_epoch":1,"x":40,"y":8},
		func(_i): session._process_first_intent()):
		return
	if not _assert_stale_consumed(session,
		{"type":"stop_move","actor_id":11,"map_epoch":1},
		func(i): session._process_stop_movement_intent(i)):
		return
	if not _assert_stale_consumed(session,
		{"type":"attack_monster","actor_id":11,"map_epoch":1,"entity_id":501},
		func(i): session._process_attack_monster_intent(i)):
		return
	if not _assert_stale_consumed(session,
		{"type":"npc","actor_id":11,"map_epoch":1,"spawn_id":46},
		func(i): session._process_npc_intent(i)):
		return

	print("MAP_LOCAL_INTENT_EPOCH_PASS bound=move_stop_attack_npc stale_map_epoch=discarded_before_send")
	quit(0)
