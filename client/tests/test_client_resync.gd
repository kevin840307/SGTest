extends SceneTree
const Session = preload("res://network/client_session.gd")
const WorldState = preload("res://network/world_state.gd")
const WireCodec = preload("res://network/wire_codec.gd")
const FakeResyncTransport = preload("res://tests/fake_resync_transport.gd")

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_RESYNC_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var fake = FakeResyncTransport.new()
	var session = Session.new(fake)
	if session.connect_secure("fixture",443,"user_01","Password-123456!","fixture",null) != OK:
		_fail("connect",session)
		return
	var selecting := _wait(session,func(v): return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,1500)
	if selecting.is_empty() or selecting.characters.size()!=1:
		_fail("character list "+str(session.snapshot()),session)
		return
	if not session.choose_character(int(selecting.characters[0].character_id)):
		_fail("choose intent",session)
		return

	var healed := _wait(session,func(v):
		return (
			int(v.state)==WorldState.STATE_IN_WORLD
			and bool(v.world_ready)
			and int(v.resync_requests)==1
			and int(v.resync_completions)==1
			and not bool(v.resync_pending)
			and int(v.last_world_ready_ack_epoch)==1
			and int(v.pending_world_ready_ack_epoch)==0
			and not v.self_state.is_empty()
			and v.entities.has(22)
		),2500)
	if healed.is_empty():
		_fail("same-session baseline did not heal "+str(session.snapshot()),session)
		return
	var expected := [
		WireCodec.OPCODE_AUTH_REQUEST,
		WireCodec.OPCODE_CHARACTER_CHOOSE,
		WireCodec.OPCODE_WORLD_READY_ACK,
		WireCodec.OPCODE_WORLD_RESYNC_REQUEST,
		WireCodec.OPCODE_WORLD_READY_ACK,
	]
	var before_move := fake.sent_opcodes()
	if before_move != expected:
		_fail("control order "+str(before_move),session)
		return
	if not session.move_target(40,8):
		_fail("post-resync move intent",session)
		return
	var moved := _wait(session,func(v):
		return (
			int(v.state)==WorldState.STATE_IN_WORLD
			and int(v.movement_sequence)==1
			and int(v.last_receipt_sequence)==1
			and int(v.last_receipt_status)==0
		),1800)
	if moved.is_empty():
		_fail("gameplay did not resume "+str(session.snapshot()),session)
		return
	var final_ops := fake.sent_opcodes()
	session.stop()
	if final_ops.size()!=6 or int(final_ops[5])!=WireCodec.OPCODE_MOVE_TARGET:
		_fail("Move was not gated after replacement ACK "+str(final_ops))
		return
	print("CLIENT_RESYNC_PASS ready_ack=true gap=delta_without_spawn resync_request=true replacement_ready_self_spawn=true reack=true gameplay_resumed=true render_loop_dependency=false")
	quit(0)

func _wait(session, predicate: Callable, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value: Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		if int(value.state)==WorldState.STATE_FAILED:
			return {}
		OS.delay_msec(2)
	return {}
