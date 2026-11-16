extends RefCounted

const WireCodec = preload("res://network/wire_codec.gd")

const STATE_NEW := 0
const STATE_AUTHENTICATING := 1
const STATE_WAIT_CHARACTER_LIST := 2
const STATE_SELECTING_CHARACTER := 3
const STATE_CHOOSING_CHARACTER := 4
const STATE_IN_WORLD := 5
const STATE_FAILED := 6

var state := STATE_NEW
var auth_status := -1
var choose_status := -1
var characters: Array[Dictionary] = []
var actor_id := 0
var map_id := 0
var x := 0.0
var y := 0.0
var map_epoch := 0
var movement_sequence := 0
var last_receipt_sequence := 0
var last_receipt_status := -1
var last_server_tick := -1
var stale_receipts := 0
var protocol_error := ""

func mark_auth_sent() -> bool:
	if state != STATE_NEW:
		return false
	state = STATE_AUTHENTICATING
	return true

func mark_choose_sent(character_id: int) -> bool:
	if state != STATE_SELECTING_CHARACTER or character_id <= 0:
		return false
	if not characters.any(func(entry: Dictionary) -> bool: return int(entry.character_id) == character_id):
		return false
	state = STATE_CHOOSING_CHARACTER
	choose_status = -1
	return true

func mark_move_sent(sequence: int) -> bool:
	if state != STATE_IN_WORLD or sequence <= movement_sequence or sequence > 0xffffffff:
		return false
	movement_sequence = sequence
	return true

func apply_frame(frame: PackedByteArray) -> bool:
	var opcode := WireCodec.frame_opcode(frame)
	match opcode:
		WireCodec.OPCODE_AUTH_RESULT:
			return _apply_auth(frame)
		WireCodec.OPCODE_CHARACTER_LIST:
			return _apply_character_list(frame)
		WireCodec.OPCODE_CHARACTER_CHOOSE_RESULT:
			return _apply_choose(frame)
		WireCodec.OPCODE_MOVE_RECEIPT:
			return _apply_receipt(frame)
		_:
			return fail("unexpected_server_opcode_%d" % opcode)

func fail(reason: String) -> bool:
	protocol_error = reason
	state = STATE_FAILED
	return false

func snapshot() -> Dictionary:
	return {
		"state": state,
		"auth_status": auth_status,
		"choose_status": choose_status,
		"characters": characters.duplicate(true),
		"actor_id": actor_id,
		"map_id": map_id,
		"x": x,
		"y": y,
		"map_epoch": map_epoch,
		"movement_sequence": movement_sequence,
		"last_receipt_sequence": last_receipt_sequence,
		"last_receipt_status": last_receipt_status,
		"last_server_tick": last_server_tick,
		"stale_receipts": stale_receipts,
		"protocol_error": protocol_error,
	}

func _apply_auth(frame: PackedByteArray) -> bool:
	if state != STATE_AUTHENTICATING:
		return fail("auth_result_out_of_state")
	var decoded := WireCodec.decode_auth_result(frame)
	if decoded.is_empty():
		return fail("invalid_auth_result")
	auth_status = int(decoded.status)
	if auth_status == 0:
		state = STATE_WAIT_CHARACTER_LIST
		return true
	state = STATE_FAILED
	return true

func _apply_character_list(frame: PackedByteArray) -> bool:
	if state != STATE_WAIT_CHARACTER_LIST:
		return fail("character_list_out_of_state")
	var decoded := WireCodec.decode_character_list(frame)
	if decoded.is_empty():
		return fail("invalid_character_list")
	characters = decoded.characters.duplicate(true)
	state = STATE_SELECTING_CHARACTER
	return true

func _apply_choose(frame: PackedByteArray) -> bool:
	if state != STATE_CHOOSING_CHARACTER:
		return fail("choose_result_out_of_state")
	var decoded := WireCodec.decode_character_choose_result(frame)
	if decoded.is_empty():
		return fail("invalid_choose_result")
	choose_status = int(decoded.status)
	if choose_status == 1:
		state = STATE_SELECTING_CHARACTER
		return true
	if choose_status != 0:
		state = STATE_FAILED
		return true
	actor_id = int(decoded.character_id)
	map_id = int(decoded.map_id)
	x = float(decoded.x)
	y = float(decoded.y)
	map_epoch = int(decoded.map_epoch)
	movement_sequence = 0
	last_receipt_sequence = 0
	last_receipt_status = -1
	last_server_tick = -1
	state = STATE_IN_WORLD
	return true

func _apply_receipt(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("move_receipt_out_of_state")
	var decoded := WireCodec.decode_move_receipt(frame)
	if decoded.is_empty():
		return fail("invalid_move_receipt")
	var receipt_epoch := int(decoded.map_epoch)
	var sequence := int(decoded.movement_sequence)
	var server_tick := int(decoded.server_tick)
	if receipt_epoch < map_epoch:
		stale_receipts += 1
		return true
	if receipt_epoch > map_epoch:
		return fail("future_map_receipt")
	if sequence > movement_sequence:
		return fail("receipt_for_unsent_sequence")
	if sequence <= last_receipt_sequence:
		stale_receipts += 1
		return true
	if last_server_tick >= 0 and server_tick < last_server_tick:
		return fail("server_tick_regressed")
	last_receipt_sequence = sequence
	last_receipt_status = int(decoded.status)
	last_server_tick = server_tick
	return true
