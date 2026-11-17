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
var facing := 1
var map_epoch := 0
var movement_sequence := 0
var npc_interaction_sequence := 0
var pending_npc_sequence := 0
var pending_npc_epoch := 0
var last_npc_result_sequence := 0
var last_npc_result_status := -1
var last_npc_result_map_epoch := 0
var stale_npc_results := 0
var waiting_for_new_map := false
var last_receipt_sequence := 0
var last_receipt_status := -1
var last_server_tick := -1
var stale_receipts := 0
var protocol_error := ""
var world_ready := false
var entities := {}
var self_state := {}
var last_aoi_tick := -1
var stale_aoi_events := 0
var pending_world_ready_ack_epoch := 0
var pending_world_ready_ack_tick := -1
var last_world_ready_ack_epoch := 0
var last_world_ready_ack_tick := -1
var resync_pending := false
var resync_request_sent := false
var resync_reason := ""
var resync_requests := 0
var resync_completions := 0

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
	if state != STATE_IN_WORLD or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or sequence <= movement_sequence or sequence > 0xffffffff:
		return false
	movement_sequence = sequence
	return true

func mark_world_ready_ack_sent(epoch: int, tick: int) -> bool:
	if state != STATE_IN_WORLD or epoch != pending_world_ready_ack_epoch or tick != pending_world_ready_ack_tick:
		return false
	last_world_ready_ack_epoch = epoch
	last_world_ready_ack_tick = tick
	pending_world_ready_ack_epoch = 0
	pending_world_ready_ack_tick = -1
	return true

func mark_resync_request_sent(epoch: int) -> bool:
	if state != STATE_IN_WORLD or not resync_pending or resync_request_sent or epoch != map_epoch:
		return false
	resync_request_sent = true
	resync_requests += 1
	return true

func mark_npc_interact_sent(sequence: int) -> bool:
	if state != STATE_IN_WORLD or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_npc_sequence != 0 or sequence <= npc_interaction_sequence or sequence > 0xffffffff:
		return false
	npc_interaction_sequence = sequence
	pending_npc_sequence = sequence
	pending_npc_epoch = map_epoch
	last_npc_result_status = -1
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
		WireCodec.OPCODE_NPC_INTERACT_RESULT:
			return _apply_npc_result(frame)
		WireCodec.OPCODE_WORLD_READY:
			return _apply_world_ready(frame)
		WireCodec.OPCODE_SELF_STATE:
			return _apply_self_state(frame)
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
		"facing": facing,
		"map_epoch": map_epoch,
		"movement_sequence": movement_sequence,
		"npc_interaction_sequence": npc_interaction_sequence,
		"pending_npc_sequence": pending_npc_sequence,
		"pending_npc_epoch": pending_npc_epoch,
		"last_npc_result_sequence": last_npc_result_sequence,
		"last_npc_result_status": last_npc_result_status,
		"last_npc_result_map_epoch": last_npc_result_map_epoch,
		"stale_npc_results": stale_npc_results,
		"waiting_for_new_map": waiting_for_new_map,
		"last_receipt_sequence": last_receipt_sequence,
		"last_receipt_status": last_receipt_status,
		"last_server_tick": last_server_tick,
		"stale_receipts": stale_receipts,
		"protocol_error": protocol_error,
		"world_ready": world_ready,
		"entities": entities.duplicate(true),
		"self_state": self_state.duplicate(true),
		"last_aoi_tick": last_aoi_tick,
		"stale_aoi_events": stale_aoi_events,
		"pending_world_ready_ack_epoch": pending_world_ready_ack_epoch,
		"pending_world_ready_ack_tick": pending_world_ready_ack_tick,
		"last_world_ready_ack_epoch": last_world_ready_ack_epoch,
		"last_world_ready_ack_tick": last_world_ready_ack_tick,
		"resync_pending": resync_pending,
		"resync_request_sent": resync_request_sent,
		"resync_reason": resync_reason,
		"resync_requests": resync_requests,
		"resync_completions": resync_completions,
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
	npc_interaction_sequence = 0
	pending_npc_sequence = 0
	pending_npc_epoch = 0
	last_npc_result_sequence = 0
	last_npc_result_status = -1
	last_npc_result_map_epoch = 0
	stale_npc_results = 0
	waiting_for_new_map = false
	map_id = int(decoded.map_id)
	x = float(decoded.x)
	y = float(decoded.y)
	map_epoch = int(decoded.map_epoch)
	movement_sequence = 0
	last_receipt_sequence = 0
	last_receipt_status = -1
	last_server_tick = -1
	world_ready = false
	self_state.clear()
	entities.clear()
	last_aoi_tick = -1
	stale_aoi_events = 0
	pending_world_ready_ack_epoch = 0
	pending_world_ready_ack_tick = -1
	last_world_ready_ack_epoch = 0
	last_world_ready_ack_tick = -1
	resync_pending = false
	resync_request_sent = false
	resync_reason = ""
	resync_requests = 0
	resync_completions = 0
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

func _apply_npc_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("npc_result_out_of_state")
	var response := WireCodec.decode_npc_interact_result(frame)
	if response.is_empty():
		return fail("invalid_npc_result")
	var request_epoch := int(response.request_map_epoch)
	var sequence := int(response.interaction_sequence)
	var status := int(response.status)
	var result_epoch := int(response.result_map_epoch)
	if pending_npc_sequence == 0:
		stale_npc_results += 1
		return true
	if pending_npc_sequence != sequence or pending_npc_epoch != request_epoch:
		if request_epoch < map_epoch:
			stale_npc_results += 1
			return true
		return fail("npc_result_wrong_request")
	if request_epoch > map_epoch or result_epoch < map_epoch:
		return fail("npc_result_invalid_epoch")
	if status == 0:
		if map_epoch != request_epoch and map_epoch != result_epoch:
			return fail("npc_result_future_epoch_mismatch")
		if map_epoch == request_epoch:
			waiting_for_new_map = true
	elif request_epoch != map_epoch:
		return fail("npc_rejection_after_map_transfer")
	last_npc_result_sequence = sequence
	last_npc_result_status = status
	last_npc_result_map_epoch = result_epoch
	pending_npc_sequence = 0
	pending_npc_epoch = 0
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
	var completing_resync := resync_pending and next_epoch == map_epoch
	if world_ready and next_epoch == map_epoch and not completing_resync:
		stale_aoi_events += 1
		return true
	if completing_resync and tick < last_aoi_tick:
		stale_aoi_events += 1
		return true
	if next_epoch > map_epoch:
		map_epoch = next_epoch
		map_id = int(ready.map_id)
		waiting_for_new_map = false
		npc_interaction_sequence = 0
		movement_sequence = 0
		last_receipt_sequence = 0
		last_receipt_status = -1
		last_server_tick = -1
		last_world_ready_ack_epoch = 0
		last_world_ready_ack_tick = -1
	if completing_resync:
		resync_completions += 1
		last_world_ready_ack_epoch = 0
		last_world_ready_ack_tick = -1
	resync_pending = false
	resync_request_sent = false
	resync_reason = ""
	self_state.clear()
	x = float(ready.x)
	y = float(ready.y)
	facing = int(ready.facing)
	world_ready = true
	entities.clear()
	last_aoi_tick = tick
	pending_world_ready_ack_epoch = map_epoch
	pending_world_ready_ack_tick = tick
	return true

func _check_aoi_generation(receiver_epoch: int, tick: int) -> int:
	if resync_pending:
		stale_aoi_events += 1
		return 0
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
				return _need_resync("entity_spawn_generation_conflict")
			if int(value.revision) > int(previous.revision):
				return _need_resync("entity_spawn_without_despawn")
			stale_aoi_events += 1
			return true
		entities[id] = value
	else:
		if previous.is_empty():
			return _need_resync("delta_without_spawn")
		if int(previous.connection_epoch) != int(value.connection_epoch) or int(previous.entity_map_epoch) != int(value.entity_map_epoch):
			return _need_resync("delta_wrong_entity_generation")
		if int(value.revision) <= int(previous.revision):
			stale_aoi_events += 1
			return true
		if int(value.revision) > int(previous.revision) + 1:
			return _need_resync("entity_revision_gap")
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
		return _need_resync("despawn_wrong_entity_generation")
	entities.erase(id)
	last_aoi_tick = maxi(last_aoi_tick, int(value.server_tick))
	return true

func _apply_self_state(frame: PackedByteArray) -> bool:
	var value := WireCodec.decode_entity_state(frame)
	if value.is_empty():
		return fail("invalid_self_state")
	var disposition := _check_aoi_generation(int(value.receiver_map_epoch), int(value.server_tick))
	if disposition <= 0:
		return disposition == 0
	if int(value.actor_id) != actor_id or int(value.map_id) != map_id or int(value.entity_map_epoch) != map_epoch:
		return fail("self_state_wrong_identity_or_map")
	# The first update binds its server-only connection generation to the already
	# authenticated own Actor/MapEpoch. Later updates cannot replace it.
	if not self_state.is_empty():
		if int(value.connection_epoch) != int(self_state.connection_epoch):
			return _need_resync("self_state_wrong_connection")
		if int(value.revision) <= int(self_state.revision):
			stale_aoi_events += 1
			return true
		if int(value.revision) > int(self_state.revision) + 1:
			return _need_resync("self_state_revision_gap")
		if int(value.movement_action_sequence) < int(self_state.movement_action_sequence):
			return _need_resync("self_state_action_regressed")
	self_state = value
	x = float(value.x)
	y = float(value.y)
	facing = int(value.facing)
	last_aoi_tick = maxi(last_aoi_tick, int(value.server_tick))
	return true

func _need_resync(reason: String) -> bool:
	if state != STATE_IN_WORLD or map_epoch <= 0:
		return fail(reason)
	if not resync_pending:
		resync_pending = true
		resync_request_sent = false
		resync_reason = reason
		world_ready = false
		pending_world_ready_ack_epoch = 0
		pending_world_ready_ack_tick = -1
		last_world_ready_ack_epoch = 0
		last_world_ready_ack_tick = -1
	return true
