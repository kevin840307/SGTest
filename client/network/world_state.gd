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
var world_ready := false
var entities := {}
var last_aoi_tick := -1
var stale_aoi_events := 0

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
		WireCodec.OPCODE_WORLD_READY:
			return _apply_world_ready(frame)
		WireCodec.OPCODE_ENTITY_SPAWN, WireCodec.OPCODE_ENTITY_DELTA:
			return _apply_entity_state(frame, opcode)
		WireCodec.OPCODE_ENTITY_DESPAWN:
			return _apply_entity_despawn(frame)
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
		"world_ready": world_ready,
		"entities": entities.duplicate(true),
		"last_aoi_tick": last_aoi_tick,
		"stale_aoi_events": stale_aoi_events,
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
	world_ready = false
	entities.clear()
	last_aoi_tick = -1
	stale_aoi_events = 0
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

func _apply_world_ready(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("world_ready_out_of_state")
	var ready := WireCodec.decode_world_ready(frame)
	if ready.is_empty():
		return fail("invalid_world_ready")
	if int(ready.actor_id) != actor_id:
		return fail("world_ready_wrong_actor")
	var next_epoch := int(ready.map_epoch)
	var tick := int(ready.server_tick)
	if next_epoch < map_epoch or (next_epoch == map_epoch and tick < last_aoi_tick):
		stale_aoi_events += 1
		return true
	if next_epoch == map_epoch and int(ready.map_id) != map_id:
		return fail("world_ready_changed_map_without_epoch")
	if next_epoch > map_epoch:
		map_epoch = next_epoch
		map_id = int(ready.map_id)
		movement_sequence = 0
		last_receipt_sequence = 0
		last_receipt_status = -1
		last_server_tick = -1
	x = float(ready.x)
	y = float(ready.y)
	world_ready = true
	entities.clear()
	last_aoi_tick = tick
	return true

func _check_aoi_generation(receiver_epoch: int, tick: int) -> int:
	if not world_ready or state != STATE_IN_WORLD:
		fail("aoi_before_world_ready")
		return -1
	if receiver_epoch > map_epoch:
		fail("future_aoi_map_epoch")
		return -1
	if receiver_epoch < map_epoch or tick < last_aoi_tick:
		stale_aoi_events += 1
		return 0
	return 1

func _apply_entity_state(frame: PackedByteArray, opcode: int) -> bool:
	var value := WireCodec.decode_entity_state(frame)
	if value.is_empty():
		return fail("invalid_entity_state")
	var disposition := _check_aoi_generation(int(value.receiver_map_epoch), int(value.server_tick))
	if disposition <= 0:
		return disposition == 0
	if int(value.map_id) != map_id or int(value.actor_id) == actor_id:
		return fail("aoi_entity_wrong_map_or_self")
	var id := int(value.actor_id)
	var previous: Dictionary = entities.get(id, {})
	if opcode == WireCodec.OPCODE_ENTITY_SPAWN:
		if not previous.is_empty():
			if int(previous.connection_epoch) != int(value.connection_epoch) or int(previous.entity_map_epoch) != int(value.entity_map_epoch):
				return fail("entity_spawn_without_despawn")
			if int(value.revision) > int(previous.revision):
				return fail("new_spawn_without_despawn")
			stale_aoi_events += 1
			return true
		entities[id] = value
	else:
		if previous.is_empty():
			return fail("delta_without_spawn")
		if int(previous.connection_epoch) != int(value.connection_epoch) or int(previous.entity_map_epoch) != int(value.entity_map_epoch):
			return fail("delta_wrong_entity_generation")
		if int(value.revision) <= int(previous.revision):
			stale_aoi_events += 1
			return true
		entities[id] = value
	last_aoi_tick = maxi(last_aoi_tick, int(value.server_tick))
	return true

func _apply_entity_despawn(frame: PackedByteArray) -> bool:
	var value := WireCodec.decode_entity_despawn(frame)
	if value.is_empty():
		return fail("invalid_entity_despawn")
	var disposition := _check_aoi_generation(int(value.receiver_map_epoch), int(value.server_tick))
	if disposition <= 0:
		return disposition == 0
	var id := int(value.actor_id)
	var previous: Dictionary = entities.get(id, {})
	if previous.is_empty() or int(value.connection_epoch) < int(previous.connection_epoch):
		stale_aoi_events += 1
		return true
	if int(value.connection_epoch) != int(previous.connection_epoch):
		return fail("despawn_wrong_entity_generation")
	entities.erase(id)
	last_aoi_tick = maxi(last_aoi_tick, int(value.server_tick))
	return true
