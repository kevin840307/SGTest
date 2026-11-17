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
var attack_sequence := 0
var last_attack_result_sequence := 0
var last_attack_result_status := -1
var last_attack_server_tick := -1
var last_attack_result := {}
var stale_attack_results := 0
var player_attack_events: Array[Dictionary] = []
var player_attack_sequences := {}
var stale_player_attack_visuals := 0
const PLAYER_ATTACK_EVENT_TTL_TICKS := 120
const PLAYER_ATTACK_EVENT_MAX := 4096
var combat_available := false
var player_current_hp := 0
var player_max_hp := 0
var player_dead := false
var player_damage_sequence := 0
var last_player_damage_tick := -1
var last_player_damage := {}
var stale_player_damage := 0
var recovery_sequence := 0
var pending_recovery_sequence := 0
var pending_recovery_item_uid := 0
var last_recovery_result_sequence := 0
var last_recovery_result_status := -1
var last_recovery_result := {}
var stale_recovery_results := 0
var discard_sequence := 0
var pending_discard_sequence := 0
var pending_discard_item_uid := 0
var pending_discard_quantity := 0
var last_discard_result_sequence := 0
var last_discard_result_status := -1
var last_discard_result := {}
var stale_discard_results := 0
var pickup_sequence := 0
var pending_pickup_sequence := 0
var pending_pickup_drop_id := 0
var last_pickup_result_sequence := 0
var last_pickup_result_status := -1
var last_pickup_result := {}
var stale_pickup_results := 0
var received_item_grants := {}
var inventory_items := {}
var inventory_ready := false
var inventory_revision := 0
var inventory_next_index := 0

var respawn_sequence := 0
var last_respawn_result_sequence := 0
var last_respawn_result_status := -1
var last_respawn_result := {}
var stale_respawn_results := 0
var waiting_for_new_map := false
var last_receipt_sequence := 0
var last_receipt_status := -1
var last_server_tick := -1
var stale_receipts := 0
var protocol_error := ""
var world_ready := false
var entities := {}
var monsters := {}
var ground_drops := {}
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
	if state != STATE_IN_WORLD or player_dead or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or sequence <= movement_sequence or sequence > 0xffffffff:
		return false
	movement_sequence = sequence
	return true

func mark_normal_attack_sent(sequence: int) -> bool:
	if state != STATE_IN_WORLD or player_dead or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or sequence <= attack_sequence or sequence > 0xffffffff:
		return false
	attack_sequence = sequence
	return true

func mark_recovery_use_sent(sequence: int,item_uid: int) -> bool:
	if state != STATE_IN_WORLD or player_dead or not combat_available or not inventory_ready or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or sequence <= recovery_sequence or sequence > 0xffffffff or item_uid <= 0 or not inventory_items.has(item_uid):
		return false
	recovery_sequence = sequence
	pending_recovery_sequence = sequence
	pending_recovery_item_uid = item_uid
	return true


func mark_inventory_discard_sent(sequence: int,item_uid: int,quantity: int) -> bool:
	if state != STATE_IN_WORLD or not inventory_ready or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_discard_sequence != 0 or pending_recovery_sequence != 0 or sequence <= discard_sequence or sequence > 0xffffffff or item_uid <= 0 or quantity < 1 or quantity > 1000000 or not inventory_items.has(item_uid) or int((inventory_items[item_uid] as Dictionary).quantity) < quantity:
		return false
	discard_sequence = sequence
	pending_discard_sequence = sequence
	pending_discard_item_uid = item_uid
	pending_discard_quantity = quantity
	return true


func mark_pickup_sent(sequence: int, drop_id: int) -> bool:
	if state != STATE_IN_WORLD or player_dead or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_pickup_sequence != 0 or sequence <= pickup_sequence or sequence > 0xffffffff or drop_id <= 0 or not ground_drops.has(drop_id):
		return false
	pickup_sequence = sequence
	pending_pickup_sequence = sequence
	pending_pickup_drop_id = drop_id
	return true


func mark_player_respawn_sent(sequence: int) -> bool:
	if state != STATE_IN_WORLD or not combat_available or not player_dead or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or sequence <= respawn_sequence or sequence > 0xffffffff:
		return false
	respawn_sequence = sequence
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
	if state != STATE_IN_WORLD or player_dead or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_npc_sequence != 0 or sequence <= npc_interaction_sequence or sequence > 0xffffffff:
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
		WireCodec.OPCODE_NORMAL_ATTACK_RESULT:
			return _apply_normal_attack_result(frame)
		WireCodec.OPCODE_PLAYER_ATTACK_VISUAL:
			return _apply_player_attack_visual(frame)
		WireCodec.OPCODE_PLAYER_COMBAT_STATE:
			return _apply_player_combat_state(frame)
		WireCodec.OPCODE_PLAYER_DAMAGE:
			return _apply_player_damage(frame)
		WireCodec.OPCODE_PLAYER_RESPAWN_RESULT:
			return _apply_player_respawn_result(frame)
		WireCodec.OPCODE_GROUND_PICKUP_RESULT:
			return _apply_pickup_result(frame)
		WireCodec.OPCODE_RECOVERY_USE_RESULT:
			return _apply_recovery_use_result(frame)
		WireCodec.OPCODE_INVENTORY_DISCARD_RESULT:
			return _apply_inventory_discard_result(frame)
		WireCodec.OPCODE_INVENTORY_SNAPSHOT:
			return _apply_inventory_snapshot(frame)
		WireCodec.OPCODE_WORLD_READY:
			return _apply_world_ready(frame)
		WireCodec.OPCODE_SELF_STATE:
			return _apply_self_state(frame)
		WireCodec.OPCODE_ENTITY_SPAWN, WireCodec.OPCODE_ENTITY_DELTA:
			return _apply_entity_state(frame, opcode)
		WireCodec.OPCODE_ENTITY_DESPAWN:
			return _apply_entity_despawn(frame)
		WireCodec.OPCODE_MONSTER_SPAWN, WireCodec.OPCODE_MONSTER_DELTA:
			return _apply_monster_state(frame,opcode)
		WireCodec.OPCODE_MONSTER_DESPAWN:
			return _apply_monster_despawn(frame)
		WireCodec.OPCODE_GROUND_DROP_SPAWN:
			return _apply_ground_drop_spawn(frame)
		WireCodec.OPCODE_GROUND_DROP_DESPAWN:
			return _apply_ground_drop_despawn(frame)
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
		"attack_sequence": attack_sequence,
		"last_attack_result_sequence": last_attack_result_sequence,
		"last_attack_result_status": last_attack_result_status,
		"last_attack_server_tick": last_attack_server_tick,
		"last_attack_result": last_attack_result.duplicate(true),
		"stale_attack_results": stale_attack_results,
		"player_attack_events": player_attack_events.duplicate(true),
		"player_attack_sequences": player_attack_sequences.duplicate(true),
		"stale_player_attack_visuals": stale_player_attack_visuals,
		"combat_available": combat_available,
		"player_current_hp": player_current_hp,
		"player_max_hp": player_max_hp,
		"player_dead": player_dead,
		"player_damage_sequence": player_damage_sequence,
		"last_player_damage_tick": last_player_damage_tick,
		"last_player_damage": last_player_damage.duplicate(true),
		"stale_player_damage": stale_player_damage,
		"recovery_sequence": recovery_sequence,
		"pending_recovery_sequence": pending_recovery_sequence,
		"pending_recovery_item_uid": pending_recovery_item_uid,
		"last_recovery_result_sequence": last_recovery_result_sequence,
		"last_recovery_result_status": last_recovery_result_status,
		"last_recovery_result": last_recovery_result.duplicate(true),
		"stale_recovery_results": stale_recovery_results,
		"discard_sequence": discard_sequence,
		"pending_discard_sequence": pending_discard_sequence,
		"pending_discard_item_uid": pending_discard_item_uid,
		"pending_discard_quantity": pending_discard_quantity,
		"last_discard_result_sequence": last_discard_result_sequence,
		"last_discard_result_status": last_discard_result_status,
		"last_discard_result": last_discard_result.duplicate(true),
		"stale_discard_results": stale_discard_results,
		"pickup_sequence": pickup_sequence,
		"pending_pickup_sequence": pending_pickup_sequence,
		"pending_pickup_drop_id": pending_pickup_drop_id,
		"last_pickup_result_sequence": last_pickup_result_sequence,
		"last_pickup_result_status": last_pickup_result_status,
		"last_pickup_result": last_pickup_result.duplicate(true),
		"stale_pickup_results": stale_pickup_results,
		"received_item_grants": received_item_grants.duplicate(true),
		"inventory_items": inventory_items.duplicate(true),
		"inventory_ready": inventory_ready,
		"inventory_revision": inventory_revision,
		"inventory_next_index": inventory_next_index,
		"respawn_sequence": respawn_sequence,
		"last_respawn_result_sequence": last_respawn_result_sequence,
		"last_respawn_result_status": last_respawn_result_status,
		"last_respawn_result": last_respawn_result.duplicate(true),
		"stale_respawn_results": stale_respawn_results,
		"waiting_for_new_map": waiting_for_new_map,
		"last_receipt_sequence": last_receipt_sequence,
		"last_receipt_status": last_receipt_status,
		"last_server_tick": last_server_tick,
		"stale_receipts": stale_receipts,
		"protocol_error": protocol_error,
		"world_ready": world_ready,
		"entities": entities.duplicate(true),
		"monsters": monsters.duplicate(true),
		"ground_drops": ground_drops.duplicate(true),
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
	attack_sequence = 0
	last_attack_result_sequence = 0
	last_attack_result_status = -1
	last_attack_server_tick = -1
	last_attack_result.clear()
	stale_attack_results = 0
	player_attack_events.clear()
	player_attack_sequences.clear()
	stale_player_attack_visuals = 0
	combat_available = false
	player_current_hp = 0
	player_max_hp = 0
	player_dead = false
	player_damage_sequence = 0
	last_player_damage_tick = -1
	last_player_damage.clear()
	stale_player_damage = 0
	recovery_sequence = 0
	pending_recovery_sequence = 0
	pending_recovery_item_uid = 0
	last_recovery_result_sequence = 0
	last_recovery_result_status = -1
	last_recovery_result.clear()
	stale_recovery_results = 0
	discard_sequence = 0
	pending_discard_sequence = 0
	pending_discard_item_uid = 0
	pending_discard_quantity = 0
	last_discard_result_sequence = 0
	last_discard_result_status = -1
	last_discard_result.clear()
	stale_discard_results = 0
	pickup_sequence = 0
	pending_pickup_sequence = 0
	pending_pickup_drop_id = 0
	last_pickup_result_sequence = 0
	last_pickup_result_status = -1
	last_pickup_result.clear()
	stale_pickup_results = 0
	received_item_grants.clear()
	inventory_items.clear()
	inventory_ready = false
	inventory_revision = 0
	inventory_next_index = 0
	respawn_sequence = 0
	last_respawn_result_sequence = 0
	last_respawn_result_status = -1
	last_respawn_result.clear()
	stale_respawn_results = 0
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
	monsters.clear()
	ground_drops.clear()
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

func _apply_player_combat_state(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("player_combat_state_out_of_state")
	var value := WireCodec.decode_player_combat_state(frame)
	if value.is_empty():
		return fail("invalid_player_combat_state")
	var epoch := int(value.map_epoch)
	if epoch < map_epoch:
		stale_player_damage += 1
		return true
	if epoch > map_epoch:
		return fail("future_player_combat_state")
	combat_available = true
	player_current_hp = int(value.current_hp)
	player_max_hp = int(value.max_hp)
	player_dead = player_current_hp == 0
	player_damage_sequence = 0
	last_player_damage_tick = -1
	last_player_damage.clear()
	return true

func _apply_player_damage(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD or not combat_available:
		return fail("player_damage_out_of_state")
	var value := WireCodec.decode_player_damage(frame)
	if value.is_empty():
		return fail("invalid_player_damage")
	var epoch := int(value.map_epoch)
	var sequence := int(value.damage_sequence)
	var tick := int(value.server_tick)
	if epoch < map_epoch:
		stale_player_damage += 1
		return true
	if epoch > map_epoch:
		return fail("future_player_damage")
	if sequence <= player_damage_sequence:
		stale_player_damage += 1
		return true
	if sequence != player_damage_sequence + 1:
		return fail("player_damage_sequence_gap")
	if last_player_damage_tick >= 0 and tick < last_player_damage_tick:
		return fail("player_damage_tick_regressed")
	if int(value.max_hp) != player_max_hp:
		return fail("player_damage_max_hp_changed_without_baseline")
	if int(value.current_hp) > player_current_hp:
		return fail("player_damage_healed")
	player_damage_sequence = sequence
	last_player_damage_tick = tick
	player_current_hp = int(value.current_hp)
	player_dead = bool(value.died)
	last_player_damage = value.duplicate(true)
	return true

func _apply_player_respawn_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD or not combat_available:
		return fail("player_respawn_result_out_of_state")
	var value := WireCodec.decode_player_respawn_result(frame)
	if value.is_empty():
		return fail("invalid_player_respawn_result")
	var epoch := int(value.map_epoch)
	var sequence := int(value.respawn_sequence)
	var status := int(value.status)
	if epoch < map_epoch:
		stale_respawn_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_player_respawn_result")
	if sequence > respawn_sequence:
		return fail("player_respawn_result_for_unsent_sequence")
	if sequence <= last_respawn_result_sequence:
		stale_respawn_results += 1
		return true
	last_respawn_result_sequence = sequence
	last_respawn_result_status = status
	last_respawn_result = value.duplicate(true)
	if status == 0 or status == 1:
		if int(value.max_hp) != player_max_hp:
			return fail("player_respawn_max_hp_changed")
		player_current_hp = int(value.current_hp)
		player_dead = false
	return true

func _apply_normal_attack_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("normal_attack_result_out_of_state")
	var result := WireCodec.decode_normal_attack_result(frame)
	if result.is_empty():
		return fail("invalid_normal_attack_result")
	var epoch := int(result.map_epoch)
	var sequence := int(result.attack_sequence)
	var tick := int(result.server_tick)
	if epoch < map_epoch:
		stale_attack_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_normal_attack_result")
	if sequence > attack_sequence:
		return fail("normal_attack_result_for_unsent_sequence")
	if sequence <= last_attack_result_sequence:
		stale_attack_results += 1
		return true
	if last_attack_server_tick >= 0 and tick < last_attack_server_tick:
		return fail("normal_attack_server_tick_regressed")
	last_attack_result_sequence = sequence
	last_attack_result_status = int(result.status)
	last_attack_server_tick = tick
	last_attack_result = result.duplicate(true)
	return true

func _apply_player_attack_visual(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("player_attack_visual_out_of_state")
	var value := WireCodec.decode_player_attack_visual(frame)
	if value.is_empty():
		return fail("invalid_player_attack_visual")
	var disposition := _check_aoi_generation(
		int(value.receiver_map_epoch),int(value.server_tick))
	if disposition <= 0:
		if disposition == 0:
			stale_player_attack_visuals += 1
		return disposition == 0
	var attacker := int(value.attacker_actor_id)
	var connection := int(value.attacker_connection_epoch)
	var target := int(value.target_entity_id)
	var target_revision := int(value.target_revision)

	if target <= 0 or not monsters.has(target):
		return _need_resync("player_attack_visual_target_without_spawn")
	var monster: Dictionary = monsters[target]
	var current_revision := int(monster.get("revision",0))
	if target_revision < current_revision:
		stale_player_attack_visuals += 1
		return true
	if target_revision > current_revision + 1:
		return _need_resync("player_attack_visual_target_revision_gap")

	if attacker == actor_id:
		if self_state.is_empty() or int(self_state.get("connection_epoch",0)) != connection:
			return _need_resync("player_attack_visual_self_generation")
	else:
		if not entities.has(attacker):
			return _need_resync("player_attack_visual_attacker_without_spawn")
		var remote: Dictionary = entities[attacker]
		if int(remote.get("connection_epoch",0)) != connection:
			return _need_resync("player_attack_visual_attacker_generation")

	var sequence := int(value.attack_sequence)
	var prior: Dictionary = player_attack_sequences.get(attacker,{})
	if not prior.is_empty():
		if int(prior.get("connection_epoch",0)) != connection:
			return _need_resync("player_attack_visual_sequence_generation")
		if sequence <= int(prior.get("sequence",0)):
			stale_player_attack_visuals += 1
			return true
	player_attack_sequences[attacker] = {
		"connection_epoch":connection,"sequence":sequence}
	player_attack_events.append(value.duplicate(true))
	var cutoff := maxi(int(value.server_tick),last_aoi_tick)-PLAYER_ATTACK_EVENT_TTL_TICKS
	while not player_attack_events.is_empty() and int(player_attack_events[0].get("server_tick",-1)) < cutoff:
		player_attack_events.pop_front()
	if player_attack_events.size() > PLAYER_ATTACK_EVENT_MAX:
		return fail("player_attack_visual_backlog_overflow")
	last_aoi_tick = maxi(last_aoi_tick,int(value.server_tick))
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
		attack_sequence = 0
		last_attack_result_sequence = 0
		last_attack_result_status = -1
		last_attack_server_tick = -1
		last_attack_result.clear()
		stale_attack_results = 0
		player_attack_events.clear()
		player_attack_sequences.clear()
		stale_player_attack_visuals = 0
		recovery_sequence = 0
		pending_recovery_sequence = 0
		pending_recovery_item_uid = 0
		last_recovery_result_sequence = 0
		last_recovery_result_status = -1
		last_recovery_result.clear()
		stale_recovery_results = 0
		discard_sequence = 0
		pending_discard_sequence = 0
		pending_discard_item_uid = 0
		pending_discard_quantity = 0
		last_discard_result_sequence = 0
		last_discard_result_status = -1
		last_discard_result.clear()
		stale_discard_results = 0
		pickup_sequence = 0
		pending_pickup_sequence = 0
		pending_pickup_drop_id = 0
		last_pickup_result_sequence = 0
		last_pickup_result_status = -1
		last_pickup_result.clear()
		stale_pickup_results = 0
		respawn_sequence = 0
		last_respawn_result_sequence = 0
		last_respawn_result_status = -1
		last_respawn_result.clear()
		stale_respawn_results = 0
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
	monsters.clear()
	ground_drops.clear()
	# One-shot presentation events are never replayed through a replacement
	# WorldReady baseline. Sequence memory is retained for same-epoch resync so
	# a delayed pre-resync visual cannot replay.
	player_attack_events.clear()
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

func _apply_monster_state(frame: PackedByteArray, opcode: int) -> bool:
	var value := WireCodec.decode_monster_state(frame)
	if value.is_empty():
		return fail("invalid_monster_state")
	var disposition := _check_aoi_generation(int(value.receiver_map_epoch),int(value.server_tick))
	if disposition <= 0:
		return disposition == 0
	if int(value.map_id) != map_id or int(value.entity_id) == actor_id:
		return fail("monster_wrong_map_or_self")
	var id := int(value.entity_id)
	var previous: Dictionary = monsters.get(id,{})
	if opcode == WireCodec.OPCODE_MONSTER_SPAWN:
		if not previous.is_empty():
			if not _same_monster_identity(previous,value):
				return _need_resync("monster_spawn_identity_conflict")
			if int(value.revision) > int(previous.revision):
				return _need_resync("monster_spawn_without_despawn")
			stale_aoi_events += 1
			return true
		monsters[id] = value
	else:
		if previous.is_empty():
			return _need_resync("monster_delta_without_spawn")
		if not _same_monster_identity(previous,value):
			return _need_resync("monster_delta_identity_conflict")
		if int(value.revision) <= int(previous.revision):
			stale_aoi_events += 1
			return true
		if int(value.revision) > int(previous.revision) + 1:
			return _need_resync("monster_revision_gap")
		monsters[id] = value
	last_aoi_tick = maxi(last_aoi_tick,int(value.server_tick))
	return true

func _apply_inventory_snapshot(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("inventory_snapshot_out_of_state")
	var value := WireCodec.decode_inventory_snapshot(frame)
	if value.is_empty():
		return fail("invalid_inventory_snapshot")
	if inventory_ready or int(value.character_id) != actor_id or int(value.start_index) != inventory_next_index:
		return fail("inventory_snapshot_wrong_character_or_sequence")
	var items: Array = value.items
	if items.is_empty() and not bool(value.complete):
		return fail("inventory_empty_nonfinal_chunk")
	for item in items:
		var uid := int(item.item_uid)
		if inventory_items.has(uid):
			return fail("inventory_duplicate_item_uid")
		inventory_items[uid] = item.duplicate(true)
		inventory_next_index += 1
	if bool(value.complete):
		inventory_ready = true
		inventory_revision += 1
	return true


func _apply_recovery_use_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("recovery_result_out_of_state")
	var result := WireCodec.decode_recovery_use_result(frame)
	if result.is_empty():
		return fail("invalid_recovery_result")
	var epoch := int(result.map_epoch)
	var sequence := int(result.use_sequence)
	var uid := int(result.item_uid)
	if epoch < map_epoch:
		stale_recovery_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_recovery_result")
	if sequence > recovery_sequence:
		return fail("unsent_recovery_result")
	if sequence <= last_recovery_result_sequence:
		stale_recovery_results += 1
		return true
	if sequence != pending_recovery_sequence or uid != pending_recovery_item_uid:
		return fail("recovery_result_wrong_request")
	if int(result.status) == 0:
		if not inventory_ready or not inventory_items.has(uid) or not combat_available:
			return fail("recovery_grant_without_inventory_or_combat")
		var previous: Dictionary = inventory_items[uid]
		if int(previous.item_definition_id) != int(result.item_definition_id) or int(previous.quantity) != int(result.remaining_quantity)+1:
			return fail("recovery_grant_inventory_quantity_mismatch")
		if int(result.max_hp) != player_max_hp or int(result.current_hp) < player_current_hp or player_dead:
			return fail("recovery_grant_inconsistent_hp")
		if int(result.remaining_quantity) == 0:
			inventory_items.erase(uid)
		else:
			var updated := previous.duplicate(true)
			updated.quantity = int(result.remaining_quantity)
			inventory_items[uid] = updated
		inventory_revision += 1
		player_current_hp = int(result.current_hp)
		player_dead = false
	# An AlreadyGranted receipt is a replay of a historical SQLite action:
	# never apply its old HP or decrement the quantity a second time.
	last_recovery_result_sequence = sequence
	last_recovery_result_status = int(result.status)
	last_recovery_result = result.duplicate(true)
	pending_recovery_sequence = 0
	pending_recovery_item_uid = 0
	return true


func _apply_inventory_discard_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("discard_result_out_of_state")
	var result := WireCodec.decode_inventory_discard_result(frame)
	if result.is_empty():
		return fail("invalid_discard_result")
	var epoch := int(result.map_epoch)
	var sequence := int(result.discard_sequence)
	var uid := int(result.item_uid)
	if epoch < map_epoch:
		stale_discard_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_discard_result")
	if sequence > discard_sequence:
		return fail("unsent_discard_result")
	if sequence <= last_discard_result_sequence:
		stale_discard_results += 1
		return true
	if sequence != pending_discard_sequence or uid != pending_discard_item_uid:
		return fail("discard_result_wrong_request")
	if int(result.status) == 0:
		if not inventory_ready or not inventory_items.has(uid):
			return fail("discard_applied_without_inventory")
		var previous: Dictionary = inventory_items[uid]
		var discarded := int(result.discarded_quantity)
		var remaining := int(result.remaining_quantity)
		if int(previous.item_definition_id) != int(result.item_definition_id) or discarded != pending_discard_quantity or int(previous.quantity) != discarded + remaining:
			return fail("discard_inventory_quantity_mismatch")
		if remaining == 0:
			inventory_items.erase(uid)
		else:
			var updated := previous.duplicate(true)
			updated.quantity = remaining
			inventory_items[uid] = updated
		inventory_revision += 1
	# AlreadyApplied is a historical durable receipt. The current session
	# never subtracts it twice; TCP loss cannot skip bytes without reconnect.
	last_discard_result_sequence = sequence
	last_discard_result_status = int(result.status)
	last_discard_result = result.duplicate(true)
	pending_discard_sequence = 0
	pending_discard_item_uid = 0
	pending_discard_quantity = 0
	return true


func _apply_pickup_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("pickup_result_out_of_state")
	var result := WireCodec.decode_ground_pickup_result(frame)
	if result.is_empty():
		return fail("invalid_pickup_result")
	var epoch := int(result.map_epoch)
	var sequence := int(result.pickup_sequence)
	if epoch < map_epoch:
		stale_pickup_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_pickup_result")
	if sequence > pickup_sequence:
		return fail("unsent_pickup_result")
	if sequence <= last_pickup_result_sequence:
		stale_pickup_results += 1
		return true
	if pending_pickup_sequence != sequence or pending_pickup_drop_id != int(result.drop_id):
		return fail("pickup_result_wrong_request")
	last_pickup_result_sequence = sequence
	last_pickup_result_status = int(result.status)
	last_pickup_result = result.duplicate(true)
	pending_pickup_sequence = 0
	pending_pickup_drop_id = 0
	# Only acknowledge inventory UIDs from authenticated Server results.
	# This list is session-local, NOT the full durable inventory baseline.
	if int(result.status) in [0,1]:
		var uid := int(result.item_uid)
		var granted := {
			"item_uid":uid,
			"item_definition_id":int(result.item_definition_id),
			"quantity":int(result.quantity),
			"drop_id":int(result.drop_id)}
		received_item_grants[uid] = granted
		if inventory_ready:
			if inventory_items.has(uid) and inventory_items[uid] != granted:
				return fail("pickup_uid_conflicts_inventory_baseline")
			if not inventory_items.has(uid):
				inventory_items[uid] = granted
				inventory_revision += 1
	return true


func _apply_ground_drop_spawn(frame: PackedByteArray) -> bool:
	var value := WireCodec.decode_ground_drop_spawn(frame)
	if value.is_empty():
		return fail("invalid_ground_drop_spawn")
	var disposition := _check_aoi_generation(int(value.receiver_map_epoch),int(value.server_tick))
	if disposition <= 0:
		return disposition == 0
	if int(value.map_id) != map_id:
		return fail("ground_drop_wrong_map")
	var id := int(value.drop_id)
	var previous: Dictionary = ground_drops.get(id,{})
	if not previous.is_empty():
		# AOI event tick is mutable metadata; the committed drop UID's
		# immutable item/map/coordinate payload is what must be identical.
		if int(previous.map_id) != int(value.map_id) or float(previous.x) != float(value.x) or float(previous.y) != float(value.y) or int(previous.item_definition_id) != int(value.item_definition_id) or int(previous.quantity) != int(value.quantity):
			return _need_resync("ground_drop_uid_conflict")
		stale_aoi_events += 1
		return true
	ground_drops[id] = value
	last_aoi_tick = maxi(last_aoi_tick,int(value.server_tick))
	return true


func _apply_ground_drop_despawn(frame: PackedByteArray) -> bool:
	var value := WireCodec.decode_ground_drop_despawn(frame)
	if value.is_empty():
		return fail("invalid_ground_drop_despawn")
	var disposition := _check_aoi_generation(int(value.receiver_map_epoch),int(value.server_tick))
	if disposition <= 0:
		return disposition == 0
	var id := int(value.drop_id)
	if not ground_drops.has(id):
		stale_aoi_events += 1
		return true
	ground_drops.erase(id)
	last_aoi_tick = maxi(last_aoi_tick,int(value.server_tick))
	return true


func _apply_monster_despawn(frame: PackedByteArray) -> bool:
	var value := WireCodec.decode_monster_despawn(frame)
	if value.is_empty():
		return fail("invalid_monster_despawn")
	var disposition := _check_aoi_generation(int(value.receiver_map_epoch),int(value.server_tick))
	if disposition <= 0:
		return disposition == 0
	var id := int(value.entity_id)
	var previous: Dictionary = monsters.get(id,{})
	if previous.is_empty() or int(value.revision) < int(previous.revision):
		stale_aoi_events += 1
		return true
	if int(value.revision) > int(previous.revision) + 1:
		return _need_resync("monster_despawn_revision_gap")
	monsters.erase(id)
	last_aoi_tick = maxi(last_aoi_tick,int(value.server_tick))
	return true

func _same_monster_identity(left: Dictionary, right: Dictionary) -> bool:
	return int(left.enemy_id) == int(right.enemy_id) and int(left.source_index) == int(right.source_index) and int(left.map_id) == int(right.map_id) and int(left.max_hp) == int(right.max_hp)

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
			return fail("self_state_wrong_connection")
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
