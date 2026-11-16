extends SceneTree

const Session = preload("res://network/client_session.gd")
const WorldState = preload("res://network/world_state.gd")
const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_TLS_LOGIN_FAIL " + message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2 or not str(args[0]).is_valid_int():
		_fail("expected port and trusted certificate path")
		return
	var port := int(args[0])
	var trusted := X509Certificate.new()
	var cert_error := trusted.load(str(args[1]))
	if cert_error != OK:
		_fail("trusted certificate load %d" % cert_error)
		return
	var session = Session.new()
	var error := session.connect_secure("127.0.0.1", port, USERNAME, PASSWORD, "localhost", trusted)
	if error != OK:
		_fail("session connect %d" % error, session)
		return
	var selecting := _wait_state(session, WorldState.STATE_SELECTING_CHARACTER, 5000)
	if selecting.is_empty() or selecting.characters.size() != 1:
		_fail("character list timeout/state %s" % session.snapshot(), session)
		return
	var character_id := int(selecting.characters[0].character_id)
	if not session.choose_character(character_id):
		_fail("choose intent rejected", session)
		return
	var in_world := _wait_state(session, WorldState.STATE_IN_WORLD, 5000)
	if in_world.is_empty() or int(in_world.actor_id) != character_id or int(in_world.map_id) != 15 or int(in_world.map_epoch) <= 0:
		_fail("world entry invalid %s" % in_world, session)
		return
	if not session.move_target(24, 8):
		_fail("move intent rejected", session)
		return
	var deadline := Time.get_ticks_msec() + 2000
	var moved: Dictionary = session.snapshot()
	while Time.get_ticks_msec() < deadline:
		moved = session.snapshot()
		if int(moved.movement_sequence) == 1:
			break
		if int(moved.state) == WorldState.STATE_FAILED:
			break
		OS.delay_msec(2)
	if int(moved.movement_sequence) != 1:
		_fail("move was not emitted %s" % moved, session)
		return
	var receipt_deadline := Time.get_ticks_msec() + 3000
	var acknowledged: Dictionary = session.snapshot()
	while Time.get_ticks_msec() < receipt_deadline:
		acknowledged = session.snapshot()
		if int(acknowledged.last_receipt_sequence) == 1 and not acknowledged.self_state.is_empty() and not bool(acknowledged.self_state.moving) and float(acknowledged.x) == 24.0 and float(acknowledged.y) == 8.0:
			break
		if int(acknowledged.state) == WorldState.STATE_FAILED:
			break
		OS.delay_msec(2)
	if int(acknowledged.state) != WorldState.STATE_IN_WORLD or not bool(acknowledged.world_ready) or int(acknowledged.last_receipt_sequence) != 1 or int(acknowledged.last_receipt_status) != 0 or int(acknowledged.last_server_tick) < 0 or int(acknowledged.map_epoch) != int(in_world.map_epoch):
		_fail("authoritative MoveReceipt was not received/reduced %s" % acknowledged, session)
		return
	if acknowledged.self_state.is_empty() or bool(acknowledged.self_state.moving) or float(acknowledged.x) != 24.0 or float(acknowledged.y) != 8.0 or int(acknowledged.facing) != 6:
		_fail("authoritative self movement/stop was not reduced %s" % acknowledged, session)
		return
	session.stop()
	print("CLIENT_TLS_LOGIN_PASS auth=PASS character=%d map_epoch=%d move_sequence=1 receipt=PASS world_ready=PASS self_movement_stop=PASS facing_east=PASS server_tick=%d transport_thread=true reducer_thread=true certificate_validation=PASS" % [character_id, int(in_world.map_epoch), int(acknowledged.last_server_tick)])
	quit(0)

func _wait_state(session, expected: int, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == expected:
			return value
		if int(value.state) == WorldState.STATE_FAILED:
			return value
		OS.delay_msec(2)
	return {}
