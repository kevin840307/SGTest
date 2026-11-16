extends SceneTree

# Real concurrently connected Godot ClientSession workers. The C# fixture
# runs one shared 30Hz authoritative World with TLS/WorldAoiPump, not fake wire.
const Session = preload("res://network/client_session.gd")
const WorldState = preload("res://network/world_state.gd")
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_MULTI_FAIL " + message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() not in [3, 4] or not str(args[0]).is_valid_int() or str(args[2]) not in ["observer", "actor"] or (args.size() == 4 and str(args[3]) != "crossmap"):
		_fail("expected TLS port, certificate path, observer|actor")
		return
	var role := str(args[2])
	var crossmap := args.size() == 4
	var cert := X509Certificate.new()
	if cert.load(str(args[1])) != OK:
		_fail("test-only TLS certificate missing")
		return
	var username := "fixture_one" if role == "observer" else "fixture_two"
	var session = Session.new()
	var started := session.connect_secure("127.0.0.1", int(args[0]), username,
		PASSWORD, "localhost", cert)
	if started != OK:
		_fail("TLS connect rejected %d" % started, session)
		return

	var selecting := _wait_character_list(session, 7000)
	if selecting.is_empty() or selecting.characters.size() != 1:
		_fail("%s character list %s" % [role, session.snapshot()], session)
		return
	var self_id := int(selecting.characters[0].character_id)
	if not session.choose_character(self_id):
		_fail("%s character choose rejected" % role, session)
		return
	var ready := _wait_ready(session, 7000)
	if ready.is_empty() or int(ready.map_id) != 15 or not bool(ready.world_ready) or int(ready.actor_id) != self_id:
		_fail("%s WorldReady missing %s" % [role, session.snapshot()], session)
		return
	if role == "observer":
		print("CLIENT_%s_READY role=observer actor=%d epoch=%d" % ["CROSSMAP" if crossmap else "MULTI", self_id, int(ready.map_epoch)])
		_observe(session, self_id, crossmap)
	else:
		_move(session, self_id, crossmap)

func _observe(session, self_id: int, crossmap: bool = false) -> void:
	var first := _wait_remote(session, 7000)
	if first.is_empty():
		_fail("observer never received another player's Spawn %s" % session.snapshot(), session)
		return
	var remote_id := int(first.entities.keys()[0])
	if remote_id == self_id:
		_fail("observer received itself as remote entity", session)
		return
	var initial_revision := int(first.entities[remote_id].revision)
	var initial_x := float(first.entities[remote_id].x)
	var moved := {}
	var deadline := Time.get_ticks_msec() + 7000
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_FAILED:
			break
		if value.entities.has(remote_id) and int(value.entities[remote_id].revision) > initial_revision and float(value.entities[remote_id].x) > initial_x:
			moved = value
			break
		OS.delay_msec(2)
	if moved.is_empty():
		_fail("observer did not receive authoritative Delta %s" % session.snapshot(), session)
		return
	if crossmap:
		print("CLIENT_CROSSMAP_DELTA_SEEN role=observer remote=%d" % remote_id)
	var gone := false
	deadline = Time.get_ticks_msec() + 7000
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_FAILED:
			break
		if value.entities.is_empty():
			gone = true
			break
		OS.delay_msec(2)
	if not gone:
		_fail("observer still sees disconnected player / missing Despawn %s" % session.snapshot(), session)
		return
	if crossmap:
		print("CLIENT_CROSSMAP_DESPAWN_SEEN role=observer remote=%d" % remote_id)
		var returned := {}
		deadline = Time.get_ticks_msec() + 7000
		while Time.get_ticks_msec() < deadline:
			var value: Dictionary = session.snapshot()
			if int(value.state) == WorldState.STATE_FAILED:
				break
			if value.entities.has(remote_id) and int(value.entities[remote_id].entity_map_epoch) > int(first.entities[remote_id].entity_map_epoch):
				returned = value
				break
			OS.delay_msec(2)
		if returned.is_empty() or int(returned.map_id) != 15 or int(returned.map_epoch) != int(first.map_epoch):
			_fail("returning Map15 player did not respawn with a new generation %s" % session.snapshot(), session)
			return
		# Keep the observer alive briefly so the returning actor can
		# also reduce its authoritative peer Spawn, not only WorldReady.
		OS.delay_msec(650)
		session.stop()
		print("CLIENT_CROSSMAP_PASS role=observer map15=PASS delta=PASS despawn=PASS respawn=PASS remote=%d" % remote_id)
		quit(0)
		return
	session.stop()
	print("CLIENT_MULTI_PASS role=observer world_ready=PASS spawn=PASS delta=PASS despawn=PASS other_actor=%d" % remote_id)
	quit(0)

func _move(session, self_id: int, crossmap: bool = false) -> void:
	var seen := _wait_remote(session, 7000)
	if seen.is_empty():
		_fail("actor never received observer's Spawn %s" % session.snapshot(), session)
		return
	if int(seen.entities.keys()[0]) == self_id:
		_fail("actor received itself as remote entity", session)
		return
	if not session.move_target(40, 8):
		_fail("actor MoveTarget intent rejected", session)
		return
	var ack := {}
	var deadline := Time.get_ticks_msec() + 7000
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_FAILED:
			break
		if int(value.last_receipt_sequence) == 1:
			ack = value
			break
		OS.delay_msec(2)
	if ack.is_empty() or int(ack.last_receipt_status) != 0 or not bool(ack.world_ready):
		_fail("actor did not receive accepted MoveReceipt %s" % session.snapshot(), session)
		return
	if crossmap:
		var original_epoch := int(ack.map_epoch)
		var in_map13 := {}
		deadline = Time.get_ticks_msec() + 7000
		while Time.get_ticks_msec() < deadline:
			var value: Dictionary = session.snapshot()
			if int(value.state) == WorldState.STATE_FAILED:
				break
			if bool(value.world_ready) and int(value.map_id) == 13 and int(value.map_epoch) > original_epoch and value.entities.is_empty():
				in_map13 = value
				break
			OS.delay_msec(2)
		if in_map13.is_empty():
			_fail("Map13 WorldReady/old AOI clear not observed %s" % session.snapshot(), session)
			return
		print("CLIENT_CROSSMAP_MAP13_READY role=actor epoch=%d" % int(in_map13.map_epoch))
		if not session.move_target(40, 8):
			_fail("Map13 MoveTarget rejected", session)
			return
		var map13_ack := {}
		deadline = Time.get_ticks_msec() + 7000
		while Time.get_ticks_msec() < deadline:
			var value: Dictionary = session.snapshot()
			if int(value.state) == WorldState.STATE_FAILED:
				break
			if int(value.map_id) == 13 and int(value.last_receipt_sequence) == 1 and int(value.last_receipt_status) == 0:
				map13_ack = value
				break
			OS.delay_msec(2)
		if map13_ack.is_empty():
			_fail("Map13 move ACK failed after epoch reset %s" % session.snapshot(), session)
			return
		print("CLIENT_CROSSMAP_MAP13_MOVE_ACK role=actor epoch=%d" % int(map13_ack.map_epoch))
		var returned := {}
		deadline = Time.get_ticks_msec() + 7000
		while Time.get_ticks_msec() < deadline:
			var value: Dictionary = session.snapshot()
			if int(value.state) == WorldState.STATE_FAILED:
				break
			if bool(value.world_ready) and int(value.map_id) == 15 and int(value.map_epoch) > int(in_map13.map_epoch) and value.entities.size() == 1:
				returned = value
				break
			OS.delay_msec(2)
		if returned.is_empty() or int(returned.entities.keys()[0]) == self_id:
			_fail("return Map15 WorldReady and peer Spawn missing %s" % session.snapshot(), session)
			return
		# The spectator must see re-entry before this actor logs out.
		OS.delay_msec(700)
		session.stop()
		print("CLIENT_CROSSMAP_PASS role=actor map15_map13_map15=PASS new_epochs=PASS world_ready=PASS old_aoi_clear=PASS")
		quit(0)
		return
	# Allow a couple of 30Hz World ticks for authoritative movement,
	# independent of the headless test's render cadence.
	OS.delay_msec(180)
	session.stop()
	print("CLIENT_MULTI_PASS role=actor world_ready=PASS spawn=PASS move_ack=PASS")
	quit(0)

func _wait_character_list(session, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_SELECTING_CHARACTER:
			return value
		if int(value.state) == WorldState.STATE_FAILED:
			break
		OS.delay_msec(2)
	return {}

func _wait_ready(session, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_IN_WORLD and bool(value.world_ready):
			return value
		if int(value.state) == WorldState.STATE_FAILED:
			break
		OS.delay_msec(2)
	return {}

func _wait_remote(session, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_IN_WORLD and value.entities.size() == 1:
			return value
		if int(value.state) == WorldState.STATE_FAILED:
			break
		OS.delay_msec(2)
	return {}
