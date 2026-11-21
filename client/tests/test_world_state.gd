extends SceneTree

const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://world/world_state.gd")
const AUTH_OK := "0900000001000B0000"
const REGISTER_CREATED := "090000000100120000"
const REGISTER_RATE_LIMITED := "090000000100120004"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const RECEIPT := "1E0000000100020001000000000000000100000000002A00000000000000"
const OLD_MAP_RECEIPT := "1E0000000100020001000000000000000100000000002A00000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const EMPTY_LIST := "0900000001000C0000"
const CREATE_RESULT := "1B000000010010000021000000000000000007004E65774865726F"
const CREATED_LIST := "1B00000001000C000121000000000000000007004E65774865726F"

func _fail(message: String) -> void:
	push_error("WORLD_STATE_FAIL " + message)
	quit(1)

func _initialize() -> void:
	var registration:=WorldState.new()
	if (
		not registration.mark_registration_sent() or
		not registration.apply_frame(REGISTER_CREATED.hex_decode()) or
		int(registration.snapshot().state)!=WorldState.STATE_NEW or
		int(registration.snapshot().registration_status)!=0 or
		not registration.mark_auth_sent()
	):
		_fail("registration created did not require ordinary auth")
		return
	var limited:=WorldState.new()
	if (
		not limited.mark_registration_sent() or
		not limited.apply_frame(REGISTER_RATE_LIMITED.hex_decode()) or
		int(limited.snapshot().state)!=WorldState.STATE_FAILED or
		int(limited.snapshot().registration_status)!=4
	):
		_fail("registration rate limit did not fail closed")
		return

	var creation = WorldState.new()
	if not creation.mark_auth_sent() or not creation.apply_frame(AUTH_OK.hex_decode()) or 		not creation.apply_frame(EMPTY_LIST.hex_decode()):
		_fail("empty account auth/list transition")
		return
	if not creation.mark_character_create_sent(0,"NewHero") or 		not creation.apply_frame(CREATE_RESULT.hex_decode()):
		_fail("character create transition")
		return
	if int(creation.snapshot().state) != WorldState.STATE_WAIT_CHARACTER_LIST or 		not creation.apply_frame(CREATED_LIST.hex_decode()):
		_fail("character create refreshed list")
		return
	var created := creation.snapshot()
	if int(created.state) != WorldState.STATE_SELECTING_CHARACTER or 		int(created.last_created_character_id) != 33 or 		str(created.last_created_name) != "NewHero" or 		(created.characters as Array).size() != 1:
		_fail("created character state %s" % created)
		return

	var world = WorldState.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH_OK.hex_decode()) or not world.apply_frame(LIST.hex_decode()):
		_fail("auth/list transition")
		return
	if not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()):
		_fail("choose transition")
		return
	if int(world.snapshot().state) != WorldState.STATE_IN_WORLD or world.mark_move_sent(1):
		_fail("move escaped WorldReady gate")
		return
	if not world.apply_frame(READY.hex_decode()):
		_fail("WorldReady apply")
		return
	var ready_state := world.snapshot()
	if int(ready_state.pending_world_ready_ack_epoch) != 1 or int(ready_state.pending_world_ready_ack_tick) != 42 or not world.mark_world_ready_ack_sent(1,42):
		_fail("WorldReady ACK lifecycle")
		return
	if not world.mark_move_sent(1):
		_fail("world/move transition")
		return
	if not world.apply_frame(RECEIPT.hex_decode()):
		_fail("receipt apply")
		return
	var after := world.snapshot()
	if int(after.last_receipt_sequence) != 1 or int(after.last_server_tick) != 42 or int(after.last_receipt_status) != 0:
		_fail("receipt state %s" % after)
		return
	if not world.apply_frame(OLD_MAP_RECEIPT.hex_decode()) or int(world.snapshot().stale_receipts) != 1:
		_fail("duplicate receipt not ignored")
		return
	var future := _mutate_i64(RECEIPT.hex_decode(), 8, 2)
	if world.apply_frame(future) or int(world.snapshot().state) != WorldState.STATE_FAILED:
		_fail("future map receipt did not fail closed")
		return
	print("WORLD_STATE_PASS registration_then_auth=PASS registration_rate_limit=FAIL_CLOSED auth_create_refresh_choose_ready_ack_move=PASS duplicate_receipt=IGNORED future_map=FAIL_CLOSED")
	quit(0)

func _mutate_i64(source: PackedByteArray, offset: int, value: int) -> PackedByteArray:
	var copy := source.duplicate()
	for i in range(8):
		copy[offset + i] = (value >> (i * 8)) & 0xff
	return copy
