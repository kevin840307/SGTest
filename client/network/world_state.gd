extends RefCounted

const WireCodec = preload("res://network/wire_codec.gd")

const STATE_NEW := 0
const STATE_AUTHENTICATING := 1
const STATE_WAIT_CHARACTER_LIST := 2
const STATE_SELECTING_CHARACTER := 3
const STATE_CHOOSING_CHARACTER := 4
const STATE_IN_WORLD := 5
const STATE_FAILED := 6
const STATE_CREATING_CHARACTER := 7

var state := STATE_NEW
var auth_status := -1
var choose_status := -1
var character_create_status := -1
var last_created_character_id := 0
var last_created_slot := -1
var last_created_name := ""
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
var skill_sequence := 0
var pending_skill_sequence := 0
var last_skill_result_sequence := 0
var last_skill_result_status := -1
var last_skill_server_tick := -1
var last_skill_result := {}
var stale_skill_results := 0
var player_skill_events: Array[Dictionary] = []
var player_skill_sequences := {}
var stale_player_skill_visuals := 0
var player_attack_events: Array[Dictionary] = []
var player_attack_sequences := {}
var stale_player_attack_visuals := 0
var player_damage_visual_events: Array[Dictionary] = []
var player_damage_visual_sequences := {}
var stale_player_damage_visuals := 0
var player_respawn_visual_events: Array[Dictionary] = []
var player_respawn_visual_sequences := {}
var stale_player_respawn_visuals := 0
const PLAYER_ATTACK_EVENT_TTL_TICKS := 120
const PLAYER_ATTACK_EVENT_MAX := 4096
const PLAYER_SKILL_TARGETS_PER_SEQUENCE_MAX := 1024
const PLAYER_COMBAT_VISUAL_EVENT_TTL_TICKS := 120
const PLAYER_COMBAT_VISUAL_EVENT_MAX := 4096
var combat_available := false
var player_current_hp := 0
var player_max_hp := 0
var player_current_mp := 0
var player_max_mp := 0
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
var shop_sequence := 0
var pending_shop_sequence := 0
var pending_shop_kind := 0
var pending_shop_npc := 0
var pending_shop_item_uid := 0
var pending_shop_item_definition_id := 0
var pending_shop_quantity := 0
var active_shop_npc := 0
var shop_items := {}
var shop_money := 0
var last_shop_open_status := -1
var last_shop_response_sequence := 0
var last_shop_result := {}
var stale_shop_results := 0
var storage_sequence := 0
var pending_storage_sequence := 0
var pending_storage_kind := 0
var pending_storage_npc := 0
var pending_storage_item_uid := 0
var active_storage_npc := 0
var storage_items := {}
var storage_ready := false
var storage_next_index := 0
var storage_revision := 0
var last_storage_open_status := -1
var last_storage_response_sequence := 0
var last_storage_result := {}
var stale_storage_results := 0
var equipment_sequence := 0
var pending_equipment_sequence := 0
var pending_equipment_kind := 0
var pending_equipment_item_uid := 0
var equipment_items := {}
var equipment_slots := {}
var equipment_ready := false
var equipment_next_index := 0
var equipment_revision := 0
var last_equipment_response_sequence := 0
var last_equipment_result := {}
var stale_equipment_results := 0
var bank_sequence := 0
var pending_bank_sequence := 0
var pending_bank_kind := 0
var pending_bank_npc := 0
var pending_bank_amount := 0
var active_bank_npc := 0
var bank_scope := 0
var bank_wallet_money := 0
var bank_money := 0
var last_bank_open_status := -1
var last_bank_response_sequence := 0
var last_bank_result := {}
var stale_bank_results := 0
var repair_sequence := 0
var pending_repair_sequence := 0
var pending_repair_kind := 0
var pending_repair_npc := 0
var pending_repair_item_uid := 0
var active_repair_npc := 0
var repair_items := {}
var repair_ready := false
var repair_next_index := 0
var repair_revision := 0
var repair_wallet_money := 0
var last_repair_open_status := -1
var last_repair_response_sequence := 0
var last_repair_result := {}
var stale_repair_results := 0

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

func mark_character_create_sent(slot: int, character_name: String) -> bool:
	if state != STATE_SELECTING_CHARACTER or slot < 0 or slot > 31:
		return false
	var bytes := character_name.to_utf8_buffer()
	if bytes.size() < 1 or bytes.size() > 96:
		return false
	state = STATE_CREATING_CHARACTER
	character_create_status = -1
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

func mark_skill_sent(sequence: int) -> bool:
	if state != STATE_IN_WORLD or player_dead or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_skill_sequence != 0 or sequence <= skill_sequence or sequence > 0xffffffff:
		return false
	skill_sequence = sequence
	pending_skill_sequence = sequence
	return true

func mark_recovery_use_sent(sequence: int,item_uid: int) -> bool:
	if state != STATE_IN_WORLD or player_dead or not combat_available or not inventory_ready or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or pending_shop_sequence != 0 or pending_storage_sequence != 0 or pending_equipment_sequence != 0 or pending_bank_sequence != 0 or pending_repair_sequence != 0 or sequence <= recovery_sequence or sequence > 0xffffffff or item_uid <= 0 or not inventory_items.has(item_uid):
		return false
	recovery_sequence = sequence
	pending_recovery_sequence = sequence
	pending_recovery_item_uid = item_uid
	return true


func mark_inventory_discard_sent(sequence: int,item_uid: int,quantity: int) -> bool:
	if state != STATE_IN_WORLD or not inventory_ready or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_discard_sequence != 0 or pending_recovery_sequence != 0 or pending_shop_sequence != 0 or pending_storage_sequence != 0 or pending_equipment_sequence != 0 or pending_bank_sequence != 0 or pending_repair_sequence != 0 or sequence <= discard_sequence or sequence > 0xffffffff or item_uid <= 0 or quantity < 1 or quantity > 1000000 or not inventory_items.has(item_uid) or int((inventory_items[item_uid] as Dictionary).quantity) < quantity:
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




func mark_shop_sent(sequence: int,kind: int,npc_spawn_id: int,item_definition_id: int = 0,item_uid: int = 0,quantity: int = 0) -> bool:
	if state != STATE_IN_WORLD or player_dead or not world_ready or not inventory_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_shop_sequence != 0 or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or pending_storage_sequence != 0 or pending_equipment_sequence != 0 or pending_bank_sequence != 0 or pending_repair_sequence != 0 or sequence <= shop_sequence or sequence > 0xffffffff or kind < 1 or kind > 3 or npc_spawn_id <= 0:
		return false
	if kind == 1:
		if item_definition_id != 0 or item_uid != 0 or quantity != 0:
			return false
	elif kind == 2:
		if active_shop_npc != npc_spawn_id or not shop_items.has(item_definition_id) or item_uid != 0 or quantity < 1 or quantity > 1000000:
			return false
	else:
		if active_shop_npc != npc_spawn_id or not shop_items.has(item_definition_id) or item_uid <= 0 or quantity < 1 or quantity > 1000000 or not inventory_items.has(item_uid) or int((inventory_items[item_uid] as Dictionary).item_definition_id) != item_definition_id or int((inventory_items[item_uid] as Dictionary).quantity) < quantity:
			return false
	shop_sequence = sequence
	pending_shop_sequence = sequence
	pending_shop_kind = kind
	pending_shop_npc = npc_spawn_id
	pending_shop_item_definition_id = item_definition_id
	pending_shop_item_uid = item_uid
	pending_shop_quantity = quantity
	return true


func mark_storage_sent(sequence: int,kind: int,npc_spawn_id: int,item_uid: int = 0) -> bool:
	if state != STATE_IN_WORLD or player_dead or not world_ready or not inventory_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_storage_sequence != 0 or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or pending_shop_sequence != 0 or pending_equipment_sequence != 0 or pending_bank_sequence != 0 or pending_repair_sequence != 0 or sequence <= storage_sequence or sequence > 0xffffffff or kind < 1 or kind > 3 or npc_spawn_id <= 0:
		return false
	if kind == 1:
		if item_uid != 0:
			return false
	elif kind == 2:
		if active_storage_npc != npc_spawn_id or not storage_ready or item_uid <= 0 or not inventory_items.has(item_uid) or storage_items.has(item_uid):
			return false
	else:
		if active_storage_npc != npc_spawn_id or not storage_ready or item_uid <= 0 or not storage_items.has(item_uid) or inventory_items.has(item_uid):
			return false
	storage_sequence = sequence
	pending_storage_sequence = sequence
	pending_storage_kind = kind
	pending_storage_npc = npc_spawn_id
	pending_storage_item_uid = item_uid
	if kind == 1:
		active_storage_npc = 0
		storage_items.clear()
		storage_ready = false
		storage_next_index = 0
		last_storage_open_status = -1
	return true


func mark_equipment_sent(sequence: int,kind: int,item_uid: int) -> bool:
	if state != STATE_IN_WORLD or not world_ready or not inventory_ready or not equipment_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_equipment_sequence != 0 or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or pending_shop_sequence != 0 or pending_storage_sequence != 0 or pending_bank_sequence != 0 or pending_repair_sequence != 0 or sequence <= equipment_sequence or sequence > 0xffffffff or kind not in [1,2] or item_uid <= 0:
		return false
	if kind == 1:
		if not inventory_items.has(item_uid) or equipment_items.has(item_uid) or storage_items.has(item_uid):
			return false
	else:
		if not equipment_items.has(item_uid) or inventory_items.has(item_uid) or storage_items.has(item_uid):
			return false
	equipment_sequence = sequence
	pending_equipment_sequence = sequence
	pending_equipment_kind = kind
	pending_equipment_item_uid = item_uid
	return true

func mark_bank_sent(sequence: int,kind: int,npc_spawn_id: int,amount: int = 0) -> bool:
	if state != STATE_IN_WORLD or player_dead or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_bank_sequence != 0 or pending_shop_sequence != 0 or pending_storage_sequence != 0 or pending_equipment_sequence != 0 or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or pending_repair_sequence != 0 or sequence <= bank_sequence or sequence > 0xffffffff or kind < 1 or kind > 3 or npc_spawn_id <= 0:
		return false
	if kind == 1:
		if amount != 0:
			return false
	else:
		if active_bank_npc != npc_spawn_id or bank_scope not in [1,2] or amount <= 0 or amount > WireCodec.MAX_MONEY:
			return false
	bank_sequence = sequence
	pending_bank_sequence = sequence
	pending_bank_kind = kind
	pending_bank_npc = npc_spawn_id
	pending_bank_amount = amount
	if kind == 1:
		active_bank_npc = 0
		bank_scope = 0
		bank_wallet_money = 0
		bank_money = 0
		last_bank_open_status = -1
	return true


func mark_repair_sent(sequence: int,kind: int,npc_spawn_id: int,item_uid: int = 0) -> bool:
	if state != STATE_IN_WORLD or player_dead or not world_ready or not inventory_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_repair_sequence != 0 or pending_shop_sequence != 0 or pending_storage_sequence != 0 or pending_equipment_sequence != 0 or pending_bank_sequence != 0 or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or sequence <= repair_sequence or sequence > 0xffffffff or kind not in [1,2] or npc_spawn_id <= 0:
		return false
	if kind == 1:
		if item_uid != 0:
			return false
	else:
		if active_repair_npc != npc_spawn_id or not repair_ready or item_uid <= 0 or not repair_items.has(item_uid):
			return false
	repair_sequence = sequence
	pending_repair_sequence = sequence
	pending_repair_kind = kind
	pending_repair_npc = npc_spawn_id
	pending_repair_item_uid = item_uid
	if kind == 1:
		active_repair_npc = 0
		repair_items.clear()
		repair_ready = false
		repair_next_index = 0
		repair_wallet_money = 0
		last_repair_open_status = -1
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
		WireCodec.OPCODE_CHARACTER_CREATE_RESULT:
			return _apply_character_create_result(frame)
		WireCodec.OPCODE_CHARACTER_CHOOSE_RESULT:
			return _apply_choose(frame)
		WireCodec.OPCODE_MOVE_RECEIPT:
			return _apply_receipt(frame)
		WireCodec.OPCODE_NPC_INTERACT_RESULT:
			return _apply_npc_result(frame)
		WireCodec.OPCODE_NORMAL_ATTACK_RESULT:
			return _apply_normal_attack_result(frame)
		WireCodec.OPCODE_SKILL_USE_RESULT:
			return _apply_skill_use_result(frame)
		WireCodec.OPCODE_PLAYER_SKILL_VISUAL:
			return _apply_player_skill_visual(frame)
		WireCodec.OPCODE_PLAYER_ATTACK_VISUAL:
			return _apply_player_attack_visual(frame)
		WireCodec.OPCODE_PLAYER_DAMAGE_VISUAL:
			return _apply_player_damage_visual(frame)
		WireCodec.OPCODE_PLAYER_RESPAWN_VISUAL:
			return _apply_player_respawn_visual(frame)
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
		WireCodec.OPCODE_SHOP_SNAPSHOT:
			return _apply_shop_snapshot(frame)
		WireCodec.OPCODE_SHOP_TRANSACTION_RESULT:
			return _apply_shop_transaction_result(frame)
		WireCodec.OPCODE_STORAGE_SNAPSHOT:
			return _apply_storage_snapshot(frame)
		WireCodec.OPCODE_STORAGE_TRANSFER_RESULT:
			return _apply_storage_transfer_result(frame)
		WireCodec.OPCODE_EQUIPMENT_SNAPSHOT:
			return _apply_equipment_snapshot(frame)
		WireCodec.OPCODE_EQUIPMENT_TRANSFER_RESULT:
			return _apply_equipment_transfer_result(frame)
		WireCodec.OPCODE_BANK_SNAPSHOT:
			return _apply_bank_snapshot(frame)
		WireCodec.OPCODE_BANK_TRANSACTION_RESULT:
			return _apply_bank_transaction_result(frame)
		WireCodec.OPCODE_REPAIR_SNAPSHOT:
			return _apply_repair_snapshot(frame)
		WireCodec.OPCODE_REPAIR_RESULT:
			return _apply_repair_result(frame)
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
		"character_create_status": character_create_status,
		"last_created_character_id": last_created_character_id,
		"last_created_slot": last_created_slot,
		"last_created_name": last_created_name,
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
		"skill_sequence": skill_sequence,
		"pending_skill_sequence": pending_skill_sequence,
		"last_skill_result_sequence": last_skill_result_sequence,
		"last_skill_result_status": last_skill_result_status,
		"last_skill_server_tick": last_skill_server_tick,
		"last_skill_result": last_skill_result.duplicate(true),
		"stale_skill_results": stale_skill_results,
		"player_skill_events": player_skill_events.duplicate(true),
		"player_skill_sequences": player_skill_sequences.duplicate(true),
		"stale_player_skill_visuals": stale_player_skill_visuals,
		"player_attack_events": player_attack_events.duplicate(true),
		"player_attack_sequences": player_attack_sequences.duplicate(true),
		"stale_player_attack_visuals": stale_player_attack_visuals,
		"player_damage_visual_events": player_damage_visual_events.duplicate(true),
		"player_damage_visual_sequences": player_damage_visual_sequences.duplicate(true),
		"stale_player_damage_visuals": stale_player_damage_visuals,
		"player_respawn_visual_events": player_respawn_visual_events.duplicate(true),
		"player_respawn_visual_sequences": player_respawn_visual_sequences.duplicate(true),
		"stale_player_respawn_visuals": stale_player_respawn_visuals,
		"combat_available": combat_available,
		"player_current_hp": player_current_hp,
		"player_max_hp": player_max_hp,
		"player_current_mp": player_current_mp,
		"player_max_mp": player_max_mp,
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
		"shop_sequence": shop_sequence,
		"pending_shop_sequence": pending_shop_sequence,
		"pending_shop_kind": pending_shop_kind,
		"pending_shop_npc": pending_shop_npc,
		"pending_shop_item_uid": pending_shop_item_uid,
		"pending_shop_item_definition_id": pending_shop_item_definition_id,
		"pending_shop_quantity": pending_shop_quantity,
		"active_shop_npc": active_shop_npc,
		"shop_items": shop_items.duplicate(true),
		"shop_money": shop_money,
		"last_shop_open_status": last_shop_open_status,
		"last_shop_response_sequence": last_shop_response_sequence,
		"last_shop_result": last_shop_result.duplicate(true),
		"stale_shop_results": stale_shop_results,
		"storage_sequence": storage_sequence,
		"pending_storage_sequence": pending_storage_sequence,
		"pending_storage_kind": pending_storage_kind,
		"pending_storage_npc": pending_storage_npc,
		"pending_storage_item_uid": pending_storage_item_uid,
		"active_storage_npc": active_storage_npc,
		"storage_items": storage_items.duplicate(true),
		"storage_ready": storage_ready,
		"storage_next_index": storage_next_index,
		"storage_revision": storage_revision,
		"last_storage_open_status": last_storage_open_status,
		"last_storage_response_sequence": last_storage_response_sequence,
		"last_storage_result": last_storage_result.duplicate(true),
		"stale_storage_results": stale_storage_results,
		"equipment_sequence": equipment_sequence,
		"pending_equipment_sequence": pending_equipment_sequence,
		"pending_equipment_kind": pending_equipment_kind,
		"pending_equipment_item_uid": pending_equipment_item_uid,
		"equipment_items": equipment_items.duplicate(true),
		"equipment_slots": equipment_slots.duplicate(true),
		"equipment_ready": equipment_ready,
		"equipment_next_index": equipment_next_index,
		"equipment_revision": equipment_revision,
		"last_equipment_response_sequence": last_equipment_response_sequence,
		"last_equipment_result": last_equipment_result.duplicate(true),
		"stale_equipment_results": stale_equipment_results,
		"bank_sequence": bank_sequence,
		"pending_bank_sequence": pending_bank_sequence,
		"pending_bank_kind": pending_bank_kind,
		"pending_bank_npc": pending_bank_npc,
		"pending_bank_amount": pending_bank_amount,
		"active_bank_npc": active_bank_npc,
		"bank_scope": bank_scope,
		"bank_wallet_money": bank_wallet_money,
		"bank_money": bank_money,
		"last_bank_open_status": last_bank_open_status,
		"last_bank_response_sequence": last_bank_response_sequence,
		"last_bank_result": last_bank_result.duplicate(true),
		"stale_bank_results": stale_bank_results,
		"repair_sequence": repair_sequence,
		"pending_repair_sequence": pending_repair_sequence,
		"pending_repair_kind": pending_repair_kind,
		"pending_repair_npc": pending_repair_npc,
		"pending_repair_item_uid": pending_repair_item_uid,
		"active_repair_npc": active_repair_npc,
		"repair_items": repair_items.duplicate(true),
		"repair_ready": repair_ready,
		"repair_next_index": repair_next_index,
		"repair_revision": repair_revision,
		"repair_wallet_money": repair_wallet_money,
		"last_repair_open_status": last_repair_open_status,
		"last_repair_response_sequence": last_repair_response_sequence,
		"last_repair_result": last_repair_result.duplicate(true),
		"stale_repair_results": stale_repair_results,
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


func _apply_character_create_result(frame: PackedByteArray) -> bool:
	if state != STATE_CREATING_CHARACTER:
		return fail("character_create_result_out_of_state")
	var decoded := WireCodec.decode_character_create_result(frame)
	if decoded.is_empty():
		return fail("invalid_character_create_result")
	character_create_status = int(decoded.status)
	if character_create_status == 0:
		last_created_character_id = int(decoded.character_id)
		last_created_slot = int(decoded.slot)
		last_created_name = str(decoded.name)
	else:
		last_created_character_id = 0
		last_created_slot = int(decoded.slot)
		last_created_name = ""
	# Server always follows the result with an authoritative CharacterList,
	# including conflict failures. Wait for that refresh before allowing
	# another create/choose intent.
	state = STATE_WAIT_CHARACTER_LIST
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
	skill_sequence = 0
	pending_skill_sequence = 0
	last_skill_result_sequence = 0
	last_skill_result_status = -1
	last_skill_server_tick = -1
	last_skill_result.clear()
	stale_skill_results = 0
	player_skill_events.clear()
	player_skill_sequences.clear()
	stale_player_skill_visuals = 0
	player_attack_events.clear()
	player_attack_sequences.clear()
	stale_player_attack_visuals = 0
	player_damage_visual_events.clear()
	player_damage_visual_sequences.clear()
	stale_player_damage_visuals = 0
	player_respawn_visual_events.clear()
	player_respawn_visual_sequences.clear()
	stale_player_respawn_visuals = 0
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
	shop_sequence = 0
	pending_shop_sequence = 0
	pending_shop_kind = 0
	pending_shop_npc = 0
	pending_shop_item_uid = 0
	pending_shop_item_definition_id = 0
	pending_shop_quantity = 0
	active_shop_npc = 0
	shop_items.clear()
	shop_money = 0
	last_shop_open_status = -1
	last_shop_response_sequence = 0
	last_shop_result.clear()
	stale_shop_results = 0
	storage_sequence = 0
	pending_storage_sequence = 0
	pending_storage_kind = 0
	pending_storage_npc = 0
	pending_storage_item_uid = 0
	active_storage_npc = 0
	storage_items.clear()
	storage_ready = false
	storage_next_index = 0
	storage_revision = 0
	last_storage_open_status = -1
	last_storage_response_sequence = 0
	last_storage_result.clear()
	stale_storage_results = 0
	equipment_sequence = 0
	pending_equipment_sequence = 0
	pending_equipment_kind = 0
	pending_equipment_item_uid = 0
	equipment_items.clear()
	equipment_slots.clear()
	equipment_ready = false
	equipment_next_index = 0
	equipment_revision = 0
	last_equipment_response_sequence = 0
	last_equipment_result.clear()
	stale_equipment_results = 0
	bank_sequence = 0
	pending_bank_sequence = 0
	pending_bank_kind = 0
	pending_bank_npc = 0
	pending_bank_amount = 0
	active_bank_npc = 0
	bank_scope = 0
	bank_wallet_money = 0
	bank_money = 0
	last_bank_open_status = -1
	last_bank_response_sequence = 0
	last_bank_result.clear()
	stale_bank_results = 0
	repair_sequence = 0
	pending_repair_sequence = 0
	pending_repair_kind = 0
	pending_repair_npc = 0
	pending_repair_item_uid = 0
	active_repair_npc = 0
	repair_items.clear()
	repair_ready = false
	repair_next_index = 0
	repair_revision = 0
	repair_wallet_money = 0
	last_repair_open_status = -1
	last_repair_response_sequence = 0
	last_repair_result.clear()
	stale_repair_results = 0
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

func _apply_player_damage_visual(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("player_damage_visual_out_of_state")
	var value := WireCodec.decode_player_damage_visual(frame)
	if value.is_empty():
		return fail("invalid_player_damage_visual")
	var disposition := _check_aoi_generation(
		int(value.receiver_map_epoch),int(value.server_tick))
	if disposition <= 0:
		if disposition == 0:
			stale_player_damage_visuals += 1
		return disposition == 0

	var victim := int(value.victim_actor_id)
	var connection := int(value.victim_connection_epoch)
	if victim == actor_id:
		if not self_state.is_empty() and int(self_state.get("connection_epoch",0)) != connection:
			return _need_resync("player_damage_visual_self_generation")
	else:
		if not entities.has(victim):
			return _need_resync("player_damage_visual_victim_without_spawn")
		var remote: Dictionary = entities[victim]
		if int(remote.get("connection_epoch",0)) != connection:
			return _need_resync("player_damage_visual_victim_generation")

	var sequence := int(value.damage_sequence)
	var prior: Dictionary = player_damage_visual_sequences.get(victim,{})
	if not prior.is_empty():
		if int(prior.get("connection_epoch",0)) != connection:
			return _need_resync("player_damage_visual_sequence_generation")
		if sequence <= int(prior.get("sequence",0)):
			stale_player_damage_visuals += 1
			return true
	player_damage_visual_sequences[victim] = {
		"connection_epoch":connection,"sequence":sequence}
	player_damage_visual_events.append(value.duplicate(true))
	var cutoff := maxi(int(value.server_tick),last_aoi_tick)-PLAYER_COMBAT_VISUAL_EVENT_TTL_TICKS
	while not player_damage_visual_events.is_empty() and int(player_damage_visual_events[0].get("server_tick",-1)) < cutoff:
		player_damage_visual_events.pop_front()
	while player_damage_visual_events.size() > PLAYER_COMBAT_VISUAL_EVENT_MAX:
		player_damage_visual_events.pop_front()
	return true


func _apply_player_respawn_visual(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("player_respawn_visual_out_of_state")
	var value := WireCodec.decode_player_respawn_visual(frame)
	if value.is_empty():
		return fail("invalid_player_respawn_visual")
	var disposition := _check_aoi_generation(
		int(value.receiver_map_epoch),int(value.server_tick))
	if disposition <= 0:
		if disposition == 0:
			stale_player_respawn_visuals += 1
		return disposition == 0

	var visual_actor := int(value.actor_id)
	var connection := int(value.connection_epoch)
	if visual_actor == actor_id:
		if not self_state.is_empty() and int(self_state.get("connection_epoch",0)) != connection:
			return _need_resync("player_respawn_visual_self_generation")
	else:
		if not entities.has(visual_actor):
			return _need_resync("player_respawn_visual_actor_without_spawn")
		var remote: Dictionary = entities[visual_actor]
		if int(remote.get("connection_epoch",0)) != connection:
			return _need_resync("player_respawn_visual_actor_generation")

	var sequence := int(value.respawn_sequence)
	var prior: Dictionary = player_respawn_visual_sequences.get(visual_actor,{})
	if not prior.is_empty():
		if int(prior.get("connection_epoch",0)) != connection:
			return _need_resync("player_respawn_visual_sequence_generation")
		if sequence <= int(prior.get("sequence",0)):
			stale_player_respawn_visuals += 1
			return true
	player_respawn_visual_sequences[visual_actor] = {
		"connection_epoch":connection,"sequence":sequence}
	player_respawn_visual_events.append(value.duplicate(true))
	var cutoff := maxi(int(value.server_tick),last_aoi_tick)-PLAYER_COMBAT_VISUAL_EVENT_TTL_TICKS
	while not player_respawn_visual_events.is_empty() and int(player_respawn_visual_events[0].get("server_tick",-1)) < cutoff:
		player_respawn_visual_events.pop_front()
	while player_respawn_visual_events.size() > PLAYER_COMBAT_VISUAL_EVENT_MAX:
		player_respawn_visual_events.pop_front()
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

	var next_hp := int(value.current_hp)
	var next_max_hp := int(value.max_hp)
	var next_mp := int(value.current_mp)
	var next_max_mp := int(value.max_mp)
	if combat_available:
		# Mid-session Server resource refresh (for example durable skill MP).
		# HP authority remains the ordered PlayerDamage/Respawn timeline; a
		# combat baseline must not silently reset DamageSequence or heal/hurt.
		if next_hp != player_current_hp or next_max_hp != player_max_hp:
			return fail("player_combat_refresh_hp_changed")
		player_current_mp = next_mp
		player_max_mp = next_max_mp
		return true

	combat_available = true
	player_current_hp = next_hp
	player_max_hp = next_max_hp
	player_current_mp = next_mp
	player_max_mp = next_max_mp
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

func _apply_skill_use_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("skill_result_out_of_state")
	var result := WireCodec.decode_skill_use_result(frame)
	if result.is_empty():
		return fail("invalid_skill_result")
	var epoch := int(result.map_epoch)
	var sequence := int(result.skill_sequence)
	var tick := int(result.server_tick)
	if epoch < map_epoch:
		stale_skill_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_skill_result")
	if sequence > skill_sequence:
		return fail("skill_result_for_unsent_sequence")
	if sequence <= last_skill_result_sequence:
		stale_skill_results += 1
		return true
	if sequence != pending_skill_sequence:
		return fail("skill_result_without_matching_pending_request")
	if last_skill_server_tick >= 0 and tick < last_skill_server_tick:
		return fail("skill_server_tick_regressed")
	last_skill_result_sequence = sequence
	last_skill_result_status = int(result.status)
	last_skill_server_tick = tick
	last_skill_result = result.duplicate(true)
	pending_skill_sequence = 0
	return true

func _apply_player_skill_visual(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("player_skill_visual_out_of_state")
	var value := WireCodec.decode_player_skill_visual(frame)
	if value.is_empty():
		return fail("invalid_player_skill_visual")
	var disposition := _check_aoi_generation(
		int(value.receiver_map_epoch),int(value.server_tick))
	if disposition <= 0:
		if disposition == 0:
			stale_player_skill_visuals += 1
		return disposition == 0
	var attacker := int(value.attacker_actor_id)
	var connection := int(value.attacker_connection_epoch)
	var target := int(value.target_entity_id)
	var target_revision := int(value.target_revision)
	if not monsters.has(target):
		return _need_resync("player_skill_visual_target_without_spawn")
	var monster: Dictionary=monsters[target]
	var current_revision := int(monster.get("revision",0))
	if target_revision < current_revision:
		stale_player_skill_visuals += 1
		return true
	if target_revision > current_revision+1:
		return _need_resync("player_skill_visual_target_revision_gap")
	if attacker == actor_id:
		if not self_state.is_empty() and int(self_state.get("connection_epoch",0)) != connection:
			return _need_resync("player_skill_visual_self_generation")
	else:
		if not entities.has(attacker):
			return _need_resync("player_skill_visual_attacker_without_spawn")
		var remote: Dictionary=entities[attacker]
		if int(remote.get("connection_epoch",0)) != connection:
			return _need_resync("player_skill_visual_attacker_generation")
	var sequence := int(value.skill_sequence)
	var prior: Dictionary=player_skill_sequences.get(attacker,{})
	if not prior.is_empty():
		if int(prior.get("connection_epoch",0)) != connection:
			return _need_resync("player_skill_visual_sequence_generation")
		var prior_sequence := int(prior.get("sequence",0))
		if sequence < prior_sequence:
			stale_player_skill_visuals += 1
			return true
		if sequence == prior_sequence:
			var targets: Dictionary = prior.get("targets",{})
			if targets.has(target):
				stale_player_skill_visuals += 1
				return true
			if targets.size() >= PLAYER_SKILL_TARGETS_PER_SEQUENCE_MAX:
				return fail("player_skill_visual_target_set_overflow")
			targets = targets.duplicate(true)
			targets[target] = true
			prior = prior.duplicate(true)
			prior["targets"] = targets
			player_skill_sequences[attacker] = prior
		else:
			player_skill_sequences[attacker]={
				"connection_epoch":connection,"sequence":sequence,
				"targets":{target:true}}
	else:
		player_skill_sequences[attacker]={
			"connection_epoch":connection,"sequence":sequence,
			"targets":{target:true}}
	player_skill_events.append(value.duplicate(true))
	var cutoff := maxi(int(value.server_tick),last_aoi_tick)-PLAYER_ATTACK_EVENT_TTL_TICKS
	while not player_skill_events.is_empty() and int(player_skill_events[0].get("server_tick",-1)) < cutoff:
		player_skill_events.pop_front()
	if player_skill_events.size()>PLAYER_ATTACK_EVENT_MAX:
		return fail("player_skill_visual_backlog_overflow")
	last_aoi_tick=maxi(last_aoi_tick,int(value.server_tick))
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
		# Initial WorldReady has no ConnectionEpoch and SelfState is changed-only.
		# A server-only authenticated combat visual may therefore be the first
		# own-generation evidence for a stationary attacker. Once SelfState is
		# present, it must match exactly.
		if not self_state.is_empty() and int(self_state.get("connection_epoch",0)) != connection:
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
		skill_sequence = 0
		pending_skill_sequence = 0
		last_skill_result_sequence = 0
		last_skill_result_status = -1
		last_skill_server_tick = -1
		last_skill_result.clear()
		stale_skill_results = 0
		player_skill_events.clear()
		player_skill_sequences.clear()
		stale_player_skill_visuals = 0
		player_attack_events.clear()
		player_attack_sequences.clear()
		stale_player_attack_visuals = 0
		player_damage_visual_events.clear()
		player_damage_visual_sequences.clear()
		stale_player_damage_visuals = 0
		player_respawn_visual_events.clear()
		player_respawn_visual_sequences.clear()
		stale_player_respawn_visuals = 0
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
		shop_sequence = 0
		pending_shop_sequence = 0
		pending_shop_kind = 0
		pending_shop_npc = 0
		pending_shop_item_uid = 0
		pending_shop_item_definition_id = 0
		pending_shop_quantity = 0
		active_shop_npc = 0
		shop_items.clear()
		shop_money = 0
		last_shop_open_status = -1
		last_shop_response_sequence = 0
		last_shop_result.clear()
		stale_shop_results = 0
		storage_sequence = 0
		pending_storage_sequence = 0
		pending_storage_kind = 0
		pending_storage_npc = 0
		pending_storage_item_uid = 0
		active_storage_npc = 0
		storage_items.clear()
		storage_ready = false
		storage_next_index = 0
		storage_revision = 0
		last_storage_open_status = -1
		last_storage_response_sequence = 0
		last_storage_result.clear()
		stale_storage_results = 0
		# Equipment location persists across maps, but map-scoped request
		# sequences and any unresolved old-map mutation must not.
		equipment_sequence = 0
		pending_equipment_sequence = 0
		pending_equipment_kind = 0
		pending_equipment_item_uid = 0
		last_equipment_response_sequence = 0
		last_equipment_result.clear()
		stale_equipment_results = 0
		bank_sequence = 0
		pending_bank_sequence = 0
		pending_bank_kind = 0
		pending_bank_npc = 0
		pending_bank_amount = 0
		active_bank_npc = 0
		bank_scope = 0
		bank_wallet_money = 0
		bank_money = 0
		last_bank_open_status = -1
		last_bank_response_sequence = 0
		last_bank_result.clear()
		stale_bank_results = 0
		repair_sequence = 0
		pending_repair_sequence = 0
		pending_repair_kind = 0
		pending_repair_npc = 0
		pending_repair_item_uid = 0
		active_repair_npc = 0
		repair_items.clear()
		repair_ready = false
		repair_next_index = 0
		repair_revision = 0
		repair_wallet_money = 0
		last_repair_open_status = -1
		last_repair_response_sequence = 0
		last_repair_result.clear()
		stale_repair_results = 0
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
	player_skill_events.clear()
	player_attack_events.clear()
	player_damage_visual_events.clear()
	player_respawn_visual_events.clear()
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
		if equipment_items.has(uid) or storage_items.has(uid):
			return fail("inventory_item_uid_location_conflict")
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




func _clear_pending_shop() -> void:
	pending_shop_sequence = 0
	pending_shop_kind = 0
	pending_shop_npc = 0
	pending_shop_item_uid = 0
	pending_shop_item_definition_id = 0
	pending_shop_quantity = 0


func _apply_shop_snapshot(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("shop_snapshot_out_of_state")
	var value := WireCodec.decode_shop_snapshot(frame)
	if value.is_empty():
		return fail("invalid_shop_snapshot")
	var epoch := int(value.map_epoch)
	var sequence := int(value.shop_sequence)
	if epoch < map_epoch:
		stale_shop_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_shop_snapshot")
	if sequence > shop_sequence:
		return fail("unsent_shop_snapshot")
	if sequence <= last_shop_response_sequence:
		stale_shop_results += 1
		return true
	if sequence != pending_shop_sequence or pending_shop_kind != 1 or int(value.npc_spawn_id) != pending_shop_npc:
		return fail("shop_snapshot_wrong_request")
	last_shop_open_status = int(value.status)
	if int(value.status) == 0:
		active_shop_npc = int(value.npc_spawn_id)
		shop_money = int(value.money)
		if active_bank_npc != 0:
			bank_wallet_money = shop_money
		shop_items.clear()
		for item in value.items:
			var item_id := int(item.item_definition_id)
			if shop_items.has(item_id):
				return fail("shop_snapshot_duplicate_item")
			shop_items[item_id] = item.duplicate(true)
	else:
		active_shop_npc = 0
		shop_items.clear()
		shop_money = 0
	last_shop_response_sequence = sequence
	_clear_pending_shop()
	return true


func _apply_shop_transaction_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("shop_result_out_of_state")
	var result := WireCodec.decode_shop_transaction_result(frame)
	if result.is_empty():
		return fail("invalid_shop_result")
	var epoch := int(result.map_epoch)
	var sequence := int(result.shop_sequence)
	if epoch < map_epoch:
		stale_shop_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_shop_result")
	if sequence > shop_sequence:
		return fail("unsent_shop_result")
	if sequence <= last_shop_response_sequence:
		stale_shop_results += 1
		return true
	if sequence != pending_shop_sequence or pending_shop_kind not in [2,3] or int(result.kind) != pending_shop_kind-1 or int(result.npc_spawn_id) != pending_shop_npc or int(result.item_definition_id) != pending_shop_item_definition_id or int(result.quantity) != pending_shop_quantity:
		return fail("shop_result_wrong_request")
	if pending_shop_kind == 3 and int(result.item_uid) != pending_shop_item_uid:
		return fail("shop_sell_uid_mismatch")
	# Applied and AlreadyApplied carry the same authoritative durable receipt.
	# AlreadyApplied is required to recover when the original success response was
	# lost after the SQLite commit. Sequence replay protection below still makes
	# an already-consumed receipt stale, so this cannot mutate inventory twice.
	if int(result.status) in [0,1]:
		if not inventory_ready:
			return fail("shop_applied_without_inventory")
		var uid := int(result.item_uid)
		var item_id := int(result.item_definition_id)
		var quantity := int(result.quantity)
		var remaining := int(result.remaining_quantity)
		if pending_shop_kind == 2:
			if uid <= 0 or inventory_items.has(uid) or remaining != quantity:
				return fail("shop_buy_inventory_conflict")
			inventory_items[uid] = {
				"item_uid":uid,"item_definition_id":item_id,
				"quantity":quantity,"drop_id":0}
		else:
			if not inventory_items.has(uid):
				return fail("shop_sell_missing_inventory")
			var previous: Dictionary = inventory_items[uid]
			if int(previous.item_definition_id) != item_id or int(previous.quantity) != quantity+remaining:
				return fail("shop_sell_inventory_quantity_mismatch")
			if remaining == 0:
				inventory_items.erase(uid)
			else:
				var updated := previous.duplicate(true)
				updated.quantity = remaining
				inventory_items[uid] = updated
		inventory_revision += 1
		shop_money = int(result.money)
		if active_bank_npc != 0:
			bank_wallet_money = shop_money
	# Access failures are authoritative evidence that this shop session is no
	# longer usable. Close stale UI/catalog state immediately; economic failures
	# such as insufficient money/quantity keep the still-valid shop open.
	elif int(result.status) in [2,3,9,10]:
		active_shop_npc = 0
		shop_items.clear()
		shop_money = 0
	# Durable replay receipts never apply inventory mutations twice.
	last_shop_response_sequence = sequence
	last_shop_result = result.duplicate(true)
	_clear_pending_shop()
	return true


func _clear_pending_storage() -> void:
	pending_storage_sequence = 0
	pending_storage_kind = 0
	pending_storage_npc = 0
	pending_storage_item_uid = 0


func _close_storage() -> void:
	active_storage_npc = 0
	storage_items.clear()
	storage_ready = false
	storage_next_index = 0


func _apply_storage_snapshot(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("storage_snapshot_out_of_state")
	var value := WireCodec.decode_storage_snapshot(frame)
	if value.is_empty():
		return fail("invalid_storage_snapshot")
	var epoch := int(value.map_epoch)
	var sequence := int(value.storage_sequence)
	if epoch < map_epoch:
		stale_storage_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_storage_snapshot")
	if sequence > storage_sequence:
		return fail("unsent_storage_snapshot")
	if sequence <= last_storage_response_sequence:
		stale_storage_results += 1
		return true
	if sequence != pending_storage_sequence or pending_storage_kind != 1 or int(value.npc_spawn_id) != pending_storage_npc:
		return fail("storage_snapshot_wrong_request")
	last_storage_open_status = int(value.status)
	if int(value.status) != 0:
		if not bool(value.complete) or int(value.start_index) != 0 or not (value.items as Array).is_empty():
			return fail("storage_failure_snapshot_has_items")
		_close_storage()
		last_storage_response_sequence = sequence
		_clear_pending_storage()
		return true
	if int(value.start_index) != storage_next_index:
		return fail("storage_snapshot_wrong_chunk")
	var items: Array = value.items
	if items.is_empty() and not bool(value.complete):
		return fail("storage_empty_nonfinal_chunk")
	for item in items:
		var uid := int(item.item_uid)
		if storage_items.has(uid) or inventory_items.has(uid):
			return fail("storage_uid_location_conflict")
		storage_items[uid] = item.duplicate(true)
		storage_next_index += 1
	if bool(value.complete):
		active_storage_npc = int(value.npc_spawn_id)
		storage_ready = true
		storage_revision += 1
		last_storage_response_sequence = sequence
		_clear_pending_storage()
	return true


func _apply_storage_transfer_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("storage_result_out_of_state")
	var result := WireCodec.decode_storage_transfer_result(frame)
	if result.is_empty():
		return fail("invalid_storage_result")
	var epoch := int(result.map_epoch)
	var sequence := int(result.storage_sequence)
	if epoch < map_epoch:
		stale_storage_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_storage_result")
	if sequence > storage_sequence:
		return fail("unsent_storage_result")
	if sequence <= last_storage_response_sequence:
		stale_storage_results += 1
		return true
	if sequence != pending_storage_sequence or pending_storage_kind not in [2,3] or int(result.kind) != pending_storage_kind-1 or int(result.npc_spawn_id) != pending_storage_npc or int(result.item_uid) != pending_storage_item_uid:
		return fail("storage_result_wrong_request")
	if int(result.status) in [0,1]:
		if not inventory_ready or not storage_ready or active_storage_npc != pending_storage_npc:
			return fail("storage_applied_without_baselines")
		var uid := int(result.item_uid)
		var receipt := {
			"item_uid":uid,
			"item_definition_id":int(result.item_definition_id),
			"quantity":int(result.quantity),
			"drop_id":int(result.drop_id)}
		if pending_storage_kind == 2:
			if not inventory_items.has(uid) or storage_items.has(uid):
				return fail("storage_deposit_location_conflict")
			var previous: Dictionary = inventory_items[uid]
			if int(previous.item_definition_id) != int(receipt.item_definition_id) or int(previous.quantity) != int(receipt.quantity) or int(previous.get("drop_id",0)) != int(receipt.drop_id):
				return fail("storage_deposit_receipt_mismatch")
			inventory_items.erase(uid)
			storage_items[uid] = receipt
		else:
			if not storage_items.has(uid) or inventory_items.has(uid):
				return fail("storage_withdraw_location_conflict")
			var previous: Dictionary = storage_items[uid]
			if int(previous.item_definition_id) != int(receipt.item_definition_id) or int(previous.quantity) != int(receipt.quantity) or int(previous.get("drop_id",0)) != int(receipt.drop_id):
				return fail("storage_withdraw_receipt_mismatch")
			storage_items.erase(uid)
			inventory_items[uid] = receipt
		inventory_revision += 1
		storage_revision += 1
	elif int(result.status) in [5,6,7,8]:
		_close_storage()
	last_storage_response_sequence = sequence
	last_storage_result = result.duplicate(true)
	_clear_pending_storage()
	return true


func _clear_pending_equipment() -> void:
	pending_equipment_sequence = 0
	pending_equipment_kind = 0
	pending_equipment_item_uid = 0


func _apply_equipment_snapshot(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("equipment_snapshot_out_of_state")
	var value := WireCodec.decode_equipment_snapshot(frame)
	if value.is_empty():
		return fail("invalid_equipment_snapshot")
	if equipment_ready or int(value.character_id) != actor_id or int(value.start_index) != equipment_next_index:
		return fail("equipment_snapshot_wrong_character_or_sequence")
	var items: Array = value.items
	if items.is_empty() and not bool(value.complete):
		return fail("equipment_empty_nonfinal_chunk")
	for item in items:
		var uid := int(item.item_uid)
		var slot_id := int(item.slot_id)
		if equipment_items.has(uid) or equipment_slots.has(slot_id) or inventory_items.has(uid) or storage_items.has(uid):
			return fail("equipment_uid_or_slot_location_conflict")
		equipment_items[uid] = item.duplicate(true)
		equipment_slots[slot_id] = uid
		equipment_next_index += 1
	if bool(value.complete):
		equipment_ready = true
		equipment_revision += 1
	return true


func _apply_equipment_transfer_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("equipment_result_out_of_state")
	var result := WireCodec.decode_equipment_transfer_result(frame)
	if result.is_empty():
		return fail("invalid_equipment_result")
	var epoch := int(result.map_epoch)
	var sequence := int(result.equipment_sequence)
	if epoch < map_epoch:
		stale_equipment_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_equipment_result")
	if sequence > equipment_sequence:
		return fail("unsent_equipment_result")
	if sequence <= last_equipment_response_sequence:
		stale_equipment_results += 1
		return true
	if sequence != pending_equipment_sequence or pending_equipment_kind not in [1,2] or int(result.kind) != pending_equipment_kind or int(result.item_uid) != pending_equipment_item_uid:
		return fail("equipment_result_wrong_request")
	if int(result.status) in [0,1]:
		if not inventory_ready or not equipment_ready:
			return fail("equipment_applied_without_baselines")
		var uid := int(result.item_uid)
		var slot_id := int(result.slot_id)
		var receipt := {"item_uid":uid,"slot_id":slot_id,"item_definition_id":int(result.item_definition_id),"drop_id":int(result.drop_id)}
		if pending_equipment_kind == 1:
			if not inventory_items.has(uid) or equipment_items.has(uid) or storage_items.has(uid) or equipment_slots.has(slot_id):
				return fail("equipment_equip_location_conflict")
			var previous: Dictionary = inventory_items[uid]
			if int(previous.item_definition_id) != int(receipt.item_definition_id) or int(previous.quantity) != 1 or int(previous.get("drop_id",0)) != int(receipt.drop_id):
				return fail("equipment_equip_receipt_mismatch")
			inventory_items.erase(uid)
			equipment_items[uid] = receipt
			equipment_slots[slot_id] = uid
		else:
			if not equipment_items.has(uid) or inventory_items.has(uid) or storage_items.has(uid):
				return fail("equipment_unequip_location_conflict")
			var previous: Dictionary = equipment_items[uid]
			if int(previous.item_definition_id) != int(receipt.item_definition_id) or int(previous.slot_id) != slot_id or int(previous.get("drop_id",0)) != int(receipt.drop_id) or int(equipment_slots.get(slot_id,0)) != uid:
				return fail("equipment_unequip_receipt_mismatch")
			equipment_items.erase(uid)
			equipment_slots.erase(slot_id)
			inventory_items[uid] = {"item_uid":uid,"item_definition_id":int(receipt.item_definition_id),"quantity":1,"drop_id":int(receipt.drop_id)}
		inventory_revision += 1
		equipment_revision += 1
	last_equipment_response_sequence = sequence
	last_equipment_result = result.duplicate(true)
	_clear_pending_equipment()
	return true

func _clear_pending_bank() -> void:
	pending_bank_sequence = 0
	pending_bank_kind = 0
	pending_bank_npc = 0
	pending_bank_amount = 0


func _close_bank() -> void:
	active_bank_npc = 0
	bank_scope = 0
	bank_wallet_money = 0
	bank_money = 0


func _apply_bank_snapshot(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("bank_snapshot_out_of_state")
	var value := WireCodec.decode_bank_snapshot(frame)
	if value.is_empty():
		return fail("invalid_bank_snapshot")
	var epoch := int(value.map_epoch)
	var sequence := int(value.bank_sequence)
	if epoch < map_epoch:
		stale_bank_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_bank_snapshot")
	if sequence > bank_sequence:
		return fail("unsent_bank_snapshot")
	if sequence <= last_bank_response_sequence:
		stale_bank_results += 1
		return true
	if sequence != pending_bank_sequence or pending_bank_kind != 1 or int(value.npc_spawn_id) != pending_bank_npc:
		return fail("bank_snapshot_wrong_request")
	last_bank_open_status = int(value.status)
	if int(value.status) == 0:
		if int(value.scope) not in [1,2]:
			return fail("bank_snapshot_missing_scope")
		active_bank_npc = int(value.npc_spawn_id)
		bank_scope = int(value.scope)
		bank_wallet_money = int(value.wallet_money)
		bank_money = int(value.bank_money)
		if active_shop_npc != 0:
			shop_money = bank_wallet_money
	else:
		_close_bank()
	last_bank_response_sequence = sequence
	_clear_pending_bank()
	return true


func _apply_bank_transaction_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("bank_result_out_of_state")
	var result := WireCodec.decode_bank_transaction_result(frame)
	if result.is_empty():
		return fail("invalid_bank_result")
	var epoch := int(result.map_epoch)
	var sequence := int(result.bank_sequence)
	if epoch < map_epoch:
		stale_bank_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_bank_result")
	if sequence > bank_sequence:
		return fail("unsent_bank_result")
	if sequence <= last_bank_response_sequence:
		stale_bank_results += 1
		return true
	if sequence != pending_bank_sequence or pending_bank_kind not in [2,3] or int(result.kind) != pending_bank_kind-1 or int(result.npc_spawn_id) != pending_bank_npc or int(result.amount) != pending_bank_amount:
		return fail("bank_result_wrong_request")
	var status := int(result.status)
	if int(result.scope) in [1,2]:
		if active_bank_npc != pending_bank_npc or bank_scope != int(result.scope):
			return fail("bank_result_scope_or_session_mismatch")
		bank_wallet_money = int(result.wallet_money)
		bank_money = int(result.bank_money)
		if active_shop_npc != 0:
			shop_money = bank_wallet_money
	elif status in [2,3,9,10]:
		_close_bank()
	else:
		return fail("bank_result_missing_scope")
	last_bank_response_sequence = sequence
	last_bank_result = result.duplicate(true)
	_clear_pending_bank()
	return true


func _clear_pending_repair() -> void:
	pending_repair_sequence = 0
	pending_repair_kind = 0
	pending_repair_npc = 0
	pending_repair_item_uid = 0


func _close_repair() -> void:
	active_repair_npc = 0
	repair_items.clear()
	repair_ready = false
	repair_next_index = 0
	repair_wallet_money = 0


func _repair_item_matches_location(item: Dictionary) -> bool:
	var uid := int(item.item_uid)
	var item_id := int(item.item_definition_id)
	var location := int(item.location)
	if location == 1:
		return inventory_items.has(uid) and int((inventory_items[uid] as Dictionary).item_definition_id) == item_id
	if location == 2:
		return equipment_ready and equipment_items.has(uid) and int((equipment_items[uid] as Dictionary).item_definition_id) == item_id
	return false


func _apply_repair_snapshot(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("repair_snapshot_out_of_state")
	var value := WireCodec.decode_repair_snapshot(frame)
	if value.is_empty():
		return fail("invalid_repair_snapshot")
	var epoch := int(value.map_epoch)
	var sequence := int(value.repair_sequence)
	if epoch < map_epoch:
		stale_repair_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_repair_snapshot")
	if sequence > repair_sequence:
		return fail("unsent_repair_snapshot")
	if sequence <= last_repair_response_sequence:
		stale_repair_results += 1
		return true
	if sequence != pending_repair_sequence or pending_repair_kind != 1 or int(value.npc_spawn_id) != pending_repair_npc:
		return fail("repair_snapshot_wrong_request")
	last_repair_open_status = int(value.status)
	if int(value.status) != 0:
		_close_repair()
		last_repair_response_sequence = sequence
		_clear_pending_repair()
		return true
	if int(value.start_index) != repair_next_index:
		return fail("repair_snapshot_wrong_chunk")
	if repair_next_index == 0:
		repair_wallet_money = int(value.wallet_money)
	elif repair_wallet_money != int(value.wallet_money):
		return fail("repair_snapshot_wallet_changed_midstream")
	var items: Array = value.items
	if items.is_empty() and not bool(value.complete):
		return fail("repair_empty_nonfinal_chunk")
	for item in items:
		var uid := int(item.item_uid)
		if repair_items.has(uid) or not _repair_item_matches_location(item):
			return fail("repair_preview_uid_or_location_conflict")
		repair_items[uid] = item.duplicate(true)
		repair_next_index += 1
	if bool(value.complete):
		active_repair_npc = int(value.npc_spawn_id)
		repair_ready = true
		repair_revision += 1
		last_repair_response_sequence = sequence
		if active_shop_npc != 0:
			shop_money = repair_wallet_money
		if active_bank_npc != 0:
			bank_wallet_money = repair_wallet_money
		_clear_pending_repair()
	return true


func _apply_repair_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("repair_result_out_of_state")
	var result := WireCodec.decode_repair_result(frame)
	if result.is_empty():
		return fail("invalid_repair_result")
	var epoch := int(result.map_epoch)
	var sequence := int(result.repair_sequence)
	if epoch < map_epoch:
		stale_repair_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_repair_result")
	if sequence > repair_sequence:
		return fail("unsent_repair_result")
	if sequence <= last_repair_response_sequence:
		stale_repair_results += 1
		return true
	if sequence != pending_repair_sequence or pending_repair_kind != 2 or int(result.npc_spawn_id) != pending_repair_npc or int(result.item_uid) != pending_repair_item_uid:
		return fail("repair_result_wrong_request")
	var status := int(result.status)
	if status in [0,1,6,7]:
		if not repair_ready or active_repair_npc != pending_repair_npc or not repair_items.has(pending_repair_item_uid):
			return fail("repair_result_without_preview")
		var previous: Dictionary = repair_items[pending_repair_item_uid]
		if int(previous.item_definition_id) != int(result.item_definition_id) or int(previous.location) != int(result.location) or int(previous.max_durability) != int(result.max_durability):
			return fail("repair_result_metadata_mismatch")
		if status in [0,1,6]:
			if int(result.current_durability) != int(result.max_durability):
				return fail("repair_result_not_full")
			previous.current_durability = int(result.current_durability)
			previous.cost = 0
		else:
			previous.current_durability = int(result.current_durability)
			previous.cost = int(result.cost)
		repair_items[pending_repair_item_uid] = previous
		repair_wallet_money = int(result.wallet_money)
		if active_shop_npc != 0:
			shop_money = repair_wallet_money
		if active_bank_npc != 0:
			bank_wallet_money = repair_wallet_money
		repair_revision += 1
	elif status in [2,3,4,5,8,9,10,11]:
		_close_repair()
	else:
		return fail("repair_result_unhandled_status")
	last_repair_response_sequence = sequence
	last_repair_result = result.duplicate(true)
	_clear_pending_repair()
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
	# authenticated own Actor/MapEpoch. If a server-only combat visual arrived
	# first, its generation must agree. Later updates cannot replace either.
	if self_state.is_empty():
		var combat_generation: Dictionary = player_attack_sequences.get(actor_id,{})
		if not combat_generation.is_empty() and int(combat_generation.get("connection_epoch",0)) != int(value.connection_epoch):
			return _need_resync("self_state_attack_generation_mismatch")
	else:
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
