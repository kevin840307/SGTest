extends SceneTree

const Session = preload("res://network/client_session.gd")
const WorldState = preload("res://network/world_state.gd")
const WireCodec = preload("res://network/wire_codec.gd")
const FakeTransport = preload("res://tests/fake_transport.gd")

func _fail(message: String) -> void:
	push_error("CLIENT_FPS_INDEPENDENCE_FAIL " + message)
	quit(1)

func _initialize() -> void:
	var cadences := [0, 30, 60, 90]
	var baseline_state: Dictionary = {}
	var baseline_wire: Array[String] = []
	for cadence in cadences:
		var result := _run_case(cadence)
		if result.is_empty():
			return
		if baseline_state.is_empty():
			baseline_state = result.state
			baseline_wire = result.wire
		elif result.state != baseline_state or result.wire != baseline_wire:
			_fail("cadence %d diverged state=%s wire=%s baseline_state=%s baseline_wire=%s" % [cadence, result.state, result.wire, baseline_state, baseline_wire])
			return
	print("CLIENT_FPS_INDEPENDENCE_PASS render_cadences=0,30,60,90 final_state_identical=true wire_order_identical=true reducer_thread=true")
	quit(0)

func _run_case(cadence: int) -> Dictionary:
	var fake = FakeTransport.new()
	var session = Session.new(fake)
	var error := session.connect_secure("fixture", 443, "user_01", "Password-123456!", "fixture", null)
	if error != OK:
		_fail("cadence %d connect error %d" % [cadence, error])
		return {}
	if not session.choose_character(11) or not session.move_target(24, 8):
		session.stop()
		_fail("cadence %d intent queue rejected" % cadence)
		return {}

	var render_polls := 0
	var started := Time.get_ticks_msec()
	var duration := 220
	if cadence == 0:
		OS.delay_msec(duration)
	else:
		var interval := maxi(1, int(round(1000.0 / cadence)))
		while Time.get_ticks_msec() - started < duration:
			session.snapshot()
			render_polls += 1
			OS.delay_msec(interval)

	var deadline := Time.get_ticks_msec() + 1500
	var final: Dictionary = session.snapshot()
	while Time.get_ticks_msec() < deadline:
		final = session.snapshot()
		if int(final.state) == WorldState.STATE_IN_WORLD and int(final.last_receipt_sequence) == 1 and not final.self_state.is_empty() and int(final.get("pvp_sequence",0))==1:
			break
		if int(final.state) == WorldState.STATE_FAILED:
			break
		OS.delay_msec(2)
	var wire := fake.sent_hex()
	session.stop()
	if int(final.state) != WorldState.STATE_IN_WORLD or int(final.actor_id) != 11 or int(final.map_id) != 15 or int(final.map_epoch) != 1 or int(final.movement_sequence) != 1 or int(final.last_receipt_sequence) != 1 or int(final.last_receipt_status) != 0 or int(final.last_server_tick) != 42:
		_fail("cadence %d final state invalid polls=%d state=%s" % [cadence, render_polls, final])
		return {}
	if final.self_state.is_empty() or float(final.x) != 24.0 or float(final.y) != 8.0 or bool(final.self_state.moving):
		_fail("cadence %d own authoritative state not reduced %s" % [cadence, final])
		return {}
	if wire.size() != 5:
		_fail("cadence %d wire count %d" % [cadence, wire.size()])
		return {}
	var ack_frame := wire[2].hex_decode()
	if WireCodec.frame_opcode(ack_frame) != WireCodec.OPCODE_WORLD_READY_ACK or not wire.any(func(frame: String): return WireCodec.frame_opcode(frame.hex_decode()) == WireCodec.OPCODE_PVP_MODE_COMMAND):
		_fail("cadence %d missing WorldReady ACK before Move %s" % [cadence, wire])
		return {}
	return {
		"state": {
			"state": final.state,
			"x": final.x,
			"y": final.y,
			"self_state": final.self_state,
			"actor_id": final.actor_id,
			"map_id": final.map_id,
			"map_epoch": final.map_epoch,
			"movement_sequence": final.movement_sequence,
			"last_receipt_sequence": final.last_receipt_sequence,
			"last_receipt_status": final.last_receipt_status,
			"last_server_tick": final.last_server_tick,
			"stale_receipts": final.stale_receipts,
		},
		"wire": wire,
	}
