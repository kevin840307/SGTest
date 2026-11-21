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
const STATE_REGISTERING := 8

var state := STATE_NEW
var registration_status := -1
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
var pending_skill_target_kind := 0
var pending_skill_target_id := 0
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
var skill_book := {}
var skill_book_ready := false
var skill_book_revision := 0
var skill_book_next_index := 0
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
var enhancement_sequence := 0
var pending_enhancement_sequence := 0
var pending_enhancement_kind := 0
var pending_enhancement_npc := 0
var pending_enhancement_item_uid := 0
var active_enhancement_npc := 0
var enhancement_items := {}
var enhancement_ready := false
var enhancement_next_index := 0
var enhancement_revision := 0
var enhancement_wallet_money := 0
var last_enhancement_open_status := -1
var last_enhancement_response_sequence := 0
var last_enhancement_result := {}
var stale_enhancement_results := 0
var trade_sequence := 0
var trade_id := 0
var trade_revision := 0
var trade_state := 0
var trade_peer_actor_id := 0
var trade_my_money := 0
var trade_other_money := 0
var trade_my_locked := false
var trade_other_locked := false
var trade_my_confirmed := false
var trade_other_confirmed := false
var trade_my_items := {}
var trade_other_items := {}
var last_trade_terminal := {}
var stale_trade_frames := 0
var player_shop_sequence := 0
var pending_player_shop_sequence := 0
var pending_player_shop_action := 0
var pending_player_shop_seller_actor_id := 0
var pending_player_shop_listing_id := 0
var pending_player_shop_item_uid := 0
var pending_player_shop_price := 0
var player_shop_own_listing := {}
var player_shop_inspected := {}
var player_shop_wallet := 0
var last_player_shop_result_sequence := 0
var last_player_shop_result_status := -1
var last_player_shop_result := {}
var last_player_shop_sold := {}
var last_player_shop_sold_listing_id := 0
var stale_player_shop_frames := 0
var chat_sequence := 0
var chat_messages: Array[Dictionary] = []
var chat_sender_sequences := {}
var stale_chat_deliveries := 0
var pending_whisper_sequence := 0
var pending_whisper_target_actor_id := 0
var pending_whisper_target_name := ""
var pending_whisper_bubble_type := 0
var pending_whisper_message := ""
var last_whisper_result_sequence := 0
var last_whisper_result_status := -1
var last_whisper_result := {}
var stale_whisper_results := 0
var party_sequence := 0
var pending_party_sequence := 0
var pending_party_action := 0
var pending_party_party_id := 0
var pending_party_target_actor_id := 0
var pending_party_target_name := ""
var party_id := 0
var party_revision := 0
var party_leader_actor_id := 0
var party_members := {}
var pending_party_invite := {}
var last_party_result_sequence := 0
var last_party_result_status := -1
var last_party_result := {}
var stale_party_frames := 0
const CHAT_BACKLOG_MAX := 100
var quest_sequence := 0
var pending_quest_sequence := 0
var pending_quest_kind := 0
var pending_quest_npc := 0
var pending_quest_id := 0
var pending_quest_choice_id := 0
var quest_view := {}
var last_quest_response_sequence := 0
var last_quest_status := -1
var last_quest_result := {}
var stale_quest_results := 0
var pvp_sequence := 0
var pending_pvp_sequence := 0
var pvp_mode := -1
var pvp_known := false
var pvp_map_allows := false
var last_pvp_result_sequence := 0
var last_pvp_result := {}
var stale_pvp_results := 0
var pvp_attack_sequence := 0
var pending_pvp_attack_sequence := 0
var pending_pvp_target_actor_id := 0
var last_pvp_attack_result_sequence := 0
var last_pvp_attack_result_status := -1
var last_pvp_attack_result := {}
var stale_pvp_attack_results := 0
var pvp_combat_visual_events: Array[Dictionary] = []
var pvp_combat_attack_sequences := {}
var pvp_combat_damage_sequences := {}
var stale_pvp_combat_visuals := 0
var pvp_skill_visual_events: Array[Dictionary] = []
var pvp_skill_visual_sequences := {}
var pvp_skill_damage_sequences := {}
var stale_pvp_skill_visuals := 0
var gm_authorized := false
var gm_sequence := 0
var pending_gm_sequence := 0
var pending_gm_target_actor_id := 0
var pending_gm_name_state := -1
var last_gm_result_sequence := 0
var last_gm_result_status := -1
var last_gm_result := {}
var stale_gm_results := 0

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
var player_identities := {}
var stale_player_identities := 0
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

func mark_registration_sent()->bool:
	if state!=STATE_NEW:
		return false
	registration_status=-1
	state=STATE_REGISTERING
	return true


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

func mark_skill_sent(sequence: int,target_kind: int = 0,target_id: int = 0) -> bool:
	if state != STATE_IN_WORLD or player_dead or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_skill_sequence != 0 or sequence <= skill_sequence or sequence > 0xffffffff or target_kind not in [0,1] or (target_kind == 1 and target_id <= 0):
		return false
	skill_sequence = sequence
	pending_skill_sequence = sequence
	pending_skill_target_kind = target_kind
	pending_skill_target_id = target_id if target_kind == 1 else 0
	return true

func mark_recovery_use_sent(sequence: int,item_uid: int) -> bool:
	if state != STATE_IN_WORLD or player_dead or not combat_available or not inventory_ready or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or pending_shop_sequence != 0 or pending_storage_sequence != 0 or pending_equipment_sequence != 0 or pending_bank_sequence != 0 or pending_repair_sequence != 0 or pending_enhancement_sequence != 0 or trade_id != 0 or sequence <= recovery_sequence or sequence > 0xffffffff or item_uid <= 0 or not inventory_items.has(item_uid):
		return false
	recovery_sequence = sequence
	pending_recovery_sequence = sequence
	pending_recovery_item_uid = item_uid
	return true


func mark_inventory_discard_sent(sequence: int,item_uid: int,quantity: int) -> bool:
	if state != STATE_IN_WORLD or not inventory_ready or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_discard_sequence != 0 or pending_recovery_sequence != 0 or pending_shop_sequence != 0 or pending_storage_sequence != 0 or pending_equipment_sequence != 0 or pending_bank_sequence != 0 or pending_repair_sequence != 0 or pending_enhancement_sequence != 0 or trade_id != 0 or sequence <= discard_sequence or sequence > 0xffffffff or item_uid <= 0 or quantity < 1 or quantity > 1000000 or not inventory_items.has(item_uid) or int((inventory_items[item_uid] as Dictionary).quantity) < quantity:
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
	if state != STATE_IN_WORLD or player_dead or not world_ready or not inventory_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_shop_sequence != 0 or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or pending_storage_sequence != 0 or pending_equipment_sequence != 0 or pending_bank_sequence != 0 or pending_repair_sequence != 0 or pending_enhancement_sequence != 0 or trade_id != 0 or sequence <= shop_sequence or sequence > 0xffffffff or kind < 1 or kind > 3 or npc_spawn_id <= 0:
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
	if state != STATE_IN_WORLD or player_dead or not world_ready or not inventory_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_storage_sequence != 0 or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or pending_shop_sequence != 0 or pending_equipment_sequence != 0 or pending_bank_sequence != 0 or pending_repair_sequence != 0 or pending_enhancement_sequence != 0 or trade_id != 0 or sequence <= storage_sequence or sequence > 0xffffffff or kind < 1 or kind > 3 or npc_spawn_id <= 0:
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
	if state != STATE_IN_WORLD or not world_ready or not inventory_ready or not equipment_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_equipment_sequence != 0 or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or pending_shop_sequence != 0 or pending_storage_sequence != 0 or pending_bank_sequence != 0 or pending_repair_sequence != 0 or pending_enhancement_sequence != 0 or trade_id != 0 or sequence <= equipment_sequence or sequence > 0xffffffff or kind not in [1,2] or item_uid <= 0:
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
	if state != STATE_IN_WORLD or player_dead or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_bank_sequence != 0 or pending_shop_sequence != 0 or pending_storage_sequence != 0 or pending_equipment_sequence != 0 or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or pending_repair_sequence != 0 or pending_enhancement_sequence != 0 or trade_id != 0 or sequence <= bank_sequence or sequence > 0xffffffff or kind < 1 or kind > 3 or npc_spawn_id <= 0:
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
	if state != STATE_IN_WORLD or player_dead or not world_ready or not inventory_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_repair_sequence != 0 or pending_enhancement_sequence != 0 or pending_shop_sequence != 0 or pending_storage_sequence != 0 or pending_equipment_sequence != 0 or pending_bank_sequence != 0 or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or trade_id != 0 or sequence <= repair_sequence or sequence > 0xffffffff or kind not in [1,2] or npc_spawn_id <= 0:
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


func mark_enhancement_sent(sequence: int,kind: int,npc_spawn_id: int,item_uid: int = 0) -> bool:
	if state != STATE_IN_WORLD or player_dead or not world_ready or not inventory_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_enhancement_sequence != 0 or pending_repair_sequence != 0 or pending_shop_sequence != 0 or pending_storage_sequence != 0 or pending_equipment_sequence != 0 or pending_bank_sequence != 0 or pending_recovery_sequence != 0 or pending_discard_sequence != 0 or trade_id != 0 or sequence <= enhancement_sequence or sequence > 0xffffffff or kind not in [1,2] or npc_spawn_id <= 0:
		return false
	if kind == 1:
		if item_uid != 0:
			return false
	else:
		if active_enhancement_npc != npc_spawn_id or not enhancement_ready or item_uid <= 0 or not enhancement_items.has(item_uid):
			return false
	enhancement_sequence = sequence
	pending_enhancement_sequence = sequence
	pending_enhancement_kind = kind
	pending_enhancement_npc = npc_spawn_id
	pending_enhancement_item_uid = item_uid
	if kind == 1:
		active_enhancement_npc = 0
		enhancement_items.clear()
		enhancement_ready = false
		enhancement_next_index = 0
		enhancement_wallet_money = 0
		last_enhancement_open_status = -1
	return true


func mark_trade_sent(sequence: int,action: int,command_trade_id: int = 0,
		target_actor_id: int = 0,item_uid: int = 0,money: int = 0) -> bool:
	if state != STATE_IN_WORLD or player_dead or not world_ready or not inventory_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_repair_sequence != 0 or pending_enhancement_sequence != 0 or sequence <= trade_sequence or sequence > 0xffffffff or action < 1 or action > 9:
		return false
	var valid := false
	match action:
		1:
			valid = trade_id == 0 and command_trade_id == 0 and target_actor_id > 0 and target_actor_id != actor_id and item_uid == 0 and money == 0
		2,3:
			valid = trade_state == 1 and command_trade_id == trade_id and target_actor_id == 0 and item_uid == 0 and money == 0
		4:
			valid = trade_state == 3 and command_trade_id == trade_id and not trade_my_locked and item_uid > 0 and inventory_items.has(item_uid) and not trade_my_items.has(item_uid) and money == 0
		5:
			valid = trade_state == 3 and command_trade_id == trade_id and not trade_my_locked and item_uid > 0 and trade_my_items.has(item_uid) and money == 0
		6:
			valid = trade_state == 3 and command_trade_id == trade_id and not trade_my_locked and item_uid == 0 and money >= 0 and money <= WireCodec.MAX_MONEY
		7:
			valid = trade_state == 3 and command_trade_id == trade_id and not trade_my_locked and item_uid == 0 and money == 0
		8:
			valid = trade_state == 4 and command_trade_id == trade_id and trade_my_locked and trade_other_locked and not trade_my_confirmed and item_uid == 0 and money == 0
		9:
			valid = trade_id > 0 and command_trade_id == trade_id and target_actor_id == 0 and item_uid == 0 and money == 0
	if not valid:
		return false
	trade_sequence = sequence
	return true


func mark_player_shop_sent(
		sequence:int,action:int,seller_actor_id:int=0,
		listing_id:int=0,item_uid:int=0,price:int=0)->bool:
	if (
		state!=STATE_IN_WORLD or player_dead or not world_ready or
		not inventory_ready or resync_pending or waiting_for_new_map or
		pending_world_ready_ack_epoch!=0 or last_world_ready_ack_epoch!=map_epoch or
		pending_player_shop_sequence!=0 or sequence<=player_shop_sequence or
		sequence>0xffffffff or action<1 or action>4
	):
		return false
	var valid:=false
	match action:
		1:
			valid=(
				seller_actor_id==0 and listing_id==0 and item_uid>0 and
				inventory_items.has(item_uid) and price>0 and price<=WireCodec.MAX_MONEY
			)
		2:
			valid=(
				seller_actor_id==0 and listing_id>0 and
				item_uid==0 and price==0
			)
		3:
			valid=(
				seller_actor_id>0 and seller_actor_id!=actor_id and
				entities.has(seller_actor_id) and listing_id==0 and
				item_uid==0 and price==0
			)
		4:
			valid=(
				seller_actor_id>0 and seller_actor_id!=actor_id and
				entities.has(seller_actor_id) and listing_id>0 and
				item_uid==0 and price==0
			)
	if not valid:
		return false
	player_shop_sequence=sequence
	pending_player_shop_sequence=sequence
	pending_player_shop_action=action
	pending_player_shop_seller_actor_id=seller_actor_id
	pending_player_shop_listing_id=listing_id
	pending_player_shop_item_uid=item_uid
	pending_player_shop_price=price
	return true


func mark_chat_sent(sequence:int,channel:int,bubble_type:int,message:String)->bool:
	var bytes:=message.to_utf8_buffer()
	if (
		state!=STATE_IN_WORLD or not world_ready or resync_pending or waiting_for_new_map or
		pending_world_ready_ack_epoch!=0 or last_world_ready_ack_epoch!=map_epoch or
		pending_whisper_sequence!=0 or
		sequence<=chat_sequence or sequence>0xffffffff or channel not in [0,3] or
		(channel==3 and party_id<=0) or
		bubble_type<0 or bubble_type>5 or bytes.size()<1 or
		bytes.size()>WireCodec.CHAT_MAX_MESSAGE_BYTES or message.strip_edges().is_empty()
	):
		return false
	chat_sequence=sequence
	return true

func mark_whisper_sent(
		sequence:int,target_actor_id:int,bubble_type:int,message:String,
		target_name:String="")->bool:
	var bytes:=message.to_utf8_buffer()
	var clean_name:=target_name.strip_edges()
	var name_bytes:=clean_name.to_utf8_buffer()
	var by_name:=not clean_name.is_empty()
	if (
		state!=STATE_IN_WORLD or not world_ready or resync_pending or
		waiting_for_new_map or pending_world_ready_ack_epoch!=0 or
		last_world_ready_ack_epoch!=map_epoch or pending_whisper_sequence!=0 or
		sequence<=chat_sequence or sequence>0xffffffff or target_actor_id<0 or
		(target_actor_id>0)==by_name or target_actor_id==actor_id or
		clean_name!=target_name or name_bytes.size()>WireCodec.CHAT_MAX_SENDER_NAME_BYTES or
		bubble_type<0 or bubble_type>5 or
		bytes.size()<1 or bytes.size()>WireCodec.CHAT_MAX_MESSAGE_BYTES or
		message.strip_edges().is_empty()
	):
		return false
	chat_sequence=sequence
	pending_whisper_sequence=sequence
	pending_whisper_target_actor_id=target_actor_id
	pending_whisper_target_name=clean_name
	pending_whisper_bubble_type=bubble_type
	pending_whisper_message=message
	return true


func _clear_pending_whisper()->void:
	pending_whisper_sequence=0
	pending_whisper_target_actor_id=0
	pending_whisper_target_name=""
	pending_whisper_bubble_type=0
	pending_whisper_message=""


func mark_party_sent(
		sequence:int,action:int,request_party_id:int,target_actor_id:int,
		target_name:String="")->bool:
	if (
		state!=STATE_IN_WORLD or not world_ready or resync_pending or
		waiting_for_new_map or pending_world_ready_ack_epoch!=0 or
		last_world_ready_ack_epoch!=map_epoch or pending_party_sequence!=0 or
		sequence<=party_sequence or sequence>0xffffffff or
		action<1 or action>6 or request_party_id<0 or target_actor_id<0
	):
		return false
	var clean_name:=target_name.strip_edges()
	if clean_name!=target_name or clean_name.to_utf8_buffer().size()>WireCodec.CHAT_MAX_SENDER_NAME_BYTES:
		return false
	match action:
		1:
			var name_target:=not clean_name.is_empty()
			if (target_actor_id>0)==name_target or target_actor_id==actor_id:
				return false
			if party_id>0:
				if request_party_id!=party_id or party_leader_actor_id!=actor_id:
					return false
			elif request_party_id!=0:
				return false
		2,3:
			if not clean_name.is_empty():
				return false
			if (
				party_id!=0 or pending_party_invite.is_empty() or
				int(pending_party_invite.get("party_id",0))!=request_party_id or
				int(pending_party_invite.get("inviter_actor_id",0))!=target_actor_id
			):
				return false
		4:
			if not clean_name.is_empty() or party_id<=0 or request_party_id!=party_id or target_actor_id!=0:
				return false
		5,6:
			if not clean_name.is_empty():
				return false
			if (
				party_id<=0 or request_party_id!=party_id or
				party_leader_actor_id!=actor_id or target_actor_id<=0 or
				target_actor_id==actor_id or not party_members.has(target_actor_id)
			):
				return false
		_:
			return false
	party_sequence=sequence
	pending_party_sequence=sequence
	pending_party_action=action
	pending_party_party_id=request_party_id
	pending_party_target_actor_id=target_actor_id
	pending_party_target_name=clean_name
	return true


func _clear_pending_party()->void:
	pending_party_sequence=0
	pending_party_action=0
	pending_party_party_id=0
	pending_party_target_actor_id=0
	pending_party_target_name=""


func mark_quest_sent(
		sequence:int,kind:int,npc_spawn_id:int,quest_id:int=0,
		choice_id:int=0)->bool:
	if (
		state!=STATE_IN_WORLD or player_dead or not world_ready or
		resync_pending or waiting_for_new_map or
		pending_world_ready_ack_epoch!=0 or last_world_ready_ack_epoch!=map_epoch or
		pending_quest_sequence!=0 or sequence<=quest_sequence or
		sequence>0xffffffff or kind not in [1,2] or npc_spawn_id<=0
	):
		return false
	if kind==1:
		if quest_id!=0 or choice_id!=0:
			return false
	else:
		if (
			quest_id<=0 or choice_id<=0 or quest_view.is_empty() or
			int(quest_view.get("npc_spawn_id",0))!=npc_spawn_id or
			int(quest_view.get("quest_id",0))!=quest_id or
			not (quest_view.get("choices",[]) as Array).any(
				func(row:Dictionary)->bool:
					return int(row.get("choice_id",0))==choice_id)
		):
			return false
	quest_sequence=sequence
	pending_quest_sequence=sequence
	pending_quest_kind=kind
	pending_quest_npc=npc_spawn_id
	pending_quest_id=quest_id
	pending_quest_choice_id=choice_id
	return true


func mark_pvp_sent(sequence: int,action: int) -> bool:
	if state != STATE_IN_WORLD or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or pending_pvp_sequence != 0 or sequence <= pvp_sequence or sequence > 0xffffffff or action not in [0,1,2] or (player_dead and action != 0):
		return false
	if action == 2 and pvp_known and not pvp_map_allows:
		return false
	pvp_sequence = sequence
	pending_pvp_sequence = sequence
	return true


func mark_pvp_attack_sent(sequence: int,target_actor_id: int) -> bool:
	if state != STATE_IN_WORLD or player_dead or not combat_available or not world_ready or resync_pending or waiting_for_new_map or pending_world_ready_ack_epoch != 0 or last_world_ready_ack_epoch != map_epoch or not pvp_known or not pvp_map_allows or pending_pvp_attack_sequence != 0 or sequence <= pvp_attack_sequence or sequence > 0xffffffff or target_actor_id <= 0 or target_actor_id == actor_id:
		return false
	pvp_attack_sequence = sequence
	pending_pvp_attack_sequence = sequence
	pending_pvp_target_actor_id = target_actor_id
	return true


func mark_gm_pvp_name_sent(
		sequence:int,target_actor_id:int,name_state:int)->bool:
	if (
		state!=STATE_IN_WORLD or not gm_authorized or not world_ready or
		resync_pending or waiting_for_new_map or
		pending_world_ready_ack_epoch!=0 or last_world_ready_ack_epoch!=map_epoch or
		pending_gm_sequence!=0 or sequence<=gm_sequence or sequence>0xffffffff or
		target_actor_id<=0 or target_actor_id==actor_id or
		name_state<0 or name_state>4 or not entities.has(target_actor_id)
	):
		return false
	gm_sequence=sequence
	pending_gm_sequence=sequence
	pending_gm_target_actor_id=target_actor_id
	pending_gm_name_state=name_state
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
		WireCodec.OPCODE_ACCOUNT_REGISTER_RESULT:
			return _apply_registration(frame)
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
		WireCodec.OPCODE_SKILL_BOOK_SNAPSHOT:
			return _apply_skill_book_snapshot(frame)
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
		WireCodec.OPCODE_ENHANCEMENT_SNAPSHOT:
			return _apply_enhancement_snapshot(frame)
		WireCodec.OPCODE_ENHANCEMENT_RESULT:
			return _apply_enhancement_result(frame)
		WireCodec.OPCODE_TRADE_SNAPSHOT:
			return _apply_trade_snapshot(frame)
		WireCodec.OPCODE_TRADE_TERMINAL:
			return _apply_trade_terminal(frame)
		WireCodec.OPCODE_PVP_MODE_RESULT:
			return _apply_pvp_mode_result(frame)
		WireCodec.OPCODE_PVP_ATTACK_RESULT:
			return _apply_pvp_attack_result(frame)
		WireCodec.OPCODE_PVP_DAMAGE:
			return _apply_pvp_damage(frame)
		WireCodec.OPCODE_PLAYER_IDENTITY:
			return _apply_player_identity(frame)
		WireCodec.OPCODE_PVP_COMBAT_VISUAL:
			return _apply_pvp_combat_visual(frame)
		WireCodec.OPCODE_PVP_SKILL_RESULT:
			return _apply_pvp_skill_result(frame)
		WireCodec.OPCODE_PVP_SKILL_VISUAL:
			return _apply_pvp_skill_visual(frame)
		WireCodec.OPCODE_GM_CAPABILITY:
			return _apply_gm_capability(frame)
		WireCodec.OPCODE_GM_PVP_NAME_RESULT:
			return _apply_gm_pvp_name_result(frame)
		WireCodec.OPCODE_PLAYER_SHOP_RESULT:
			return _apply_player_shop_result(frame)
		WireCodec.OPCODE_PLAYER_SHOP_SOLD:
			return _apply_player_shop_sold(frame)
		WireCodec.OPCODE_CHAT_DELIVERY:
			return _apply_chat_delivery(frame)
		WireCodec.OPCODE_WHISPER_RESULT:
			return _apply_whisper_result(frame)
		WireCodec.OPCODE_PARTY_RESULT:
			return _apply_party_result(frame)
		WireCodec.OPCODE_PARTY_INVITE:
			return _apply_party_invite(frame)
		WireCodec.OPCODE_PARTY_SNAPSHOT:
			return _apply_party_snapshot(frame)
		WireCodec.OPCODE_QUEST_VIEW:
			return _apply_quest_view(frame)
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
		"registration_status": registration_status,
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
		"pending_skill_target_kind": pending_skill_target_kind,
		"pending_skill_target_id": pending_skill_target_id,
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
		"skill_book": skill_book.duplicate(true),
		"skill_book_ready": skill_book_ready,
		"skill_book_revision": skill_book_revision,
		"skill_book_next_index": skill_book_next_index,
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
		"enhancement_sequence": enhancement_sequence,
		"pending_enhancement_sequence": pending_enhancement_sequence,
		"pending_enhancement_kind": pending_enhancement_kind,
		"pending_enhancement_npc": pending_enhancement_npc,
		"pending_enhancement_item_uid": pending_enhancement_item_uid,
		"active_enhancement_npc": active_enhancement_npc,
		"enhancement_items": enhancement_items.duplicate(true),
		"enhancement_ready": enhancement_ready,
		"enhancement_next_index": enhancement_next_index,
		"enhancement_revision": enhancement_revision,
		"enhancement_wallet_money": enhancement_wallet_money,
		"last_enhancement_open_status": last_enhancement_open_status,
		"last_enhancement_response_sequence": last_enhancement_response_sequence,
		"last_enhancement_result": last_enhancement_result.duplicate(true),
		"stale_enhancement_results": stale_enhancement_results,
		"trade_sequence": trade_sequence,
		"trade_id": trade_id,
		"trade_revision": trade_revision,
		"trade_state": trade_state,
		"trade_peer_actor_id": trade_peer_actor_id,
		"trade_my_money": trade_my_money,
		"trade_other_money": trade_other_money,
		"trade_my_locked": trade_my_locked,
		"trade_other_locked": trade_other_locked,
		"trade_my_confirmed": trade_my_confirmed,
		"trade_other_confirmed": trade_other_confirmed,
		"trade_my_items": trade_my_items.duplicate(true),
		"trade_other_items": trade_other_items.duplicate(true),
		"last_trade_terminal": last_trade_terminal.duplicate(true),
		"stale_trade_frames": stale_trade_frames,
		"player_shop_sequence": player_shop_sequence,
		"pending_player_shop_sequence": pending_player_shop_sequence,
		"pending_player_shop_action": pending_player_shop_action,
		"pending_player_shop_seller_actor_id": pending_player_shop_seller_actor_id,
		"pending_player_shop_listing_id": pending_player_shop_listing_id,
		"pending_player_shop_item_uid": pending_player_shop_item_uid,
		"pending_player_shop_price": pending_player_shop_price,
		"player_shop_own_listing": player_shop_own_listing.duplicate(true),
		"player_shop_inspected": player_shop_inspected.duplicate(true),
		"player_shop_wallet": player_shop_wallet,
		"last_player_shop_result_sequence": last_player_shop_result_sequence,
		"last_player_shop_result_status": last_player_shop_result_status,
		"last_player_shop_result": last_player_shop_result.duplicate(true),
		"last_player_shop_sold": last_player_shop_sold.duplicate(true),
		"last_player_shop_sold_listing_id": last_player_shop_sold_listing_id,
		"stale_player_shop_frames": stale_player_shop_frames,
		"chat_sequence": chat_sequence,
		"chat_messages": chat_messages.duplicate(true),
		"stale_chat_deliveries": stale_chat_deliveries,
		"pending_whisper_sequence": pending_whisper_sequence,
		"pending_whisper_target_actor_id": pending_whisper_target_actor_id,
		"pending_whisper_target_name": pending_whisper_target_name,
		"last_whisper_result_sequence": last_whisper_result_sequence,
		"last_whisper_result_status": last_whisper_result_status,
		"last_whisper_result": last_whisper_result.duplicate(true),
		"stale_whisper_results": stale_whisper_results,
		"party_sequence": party_sequence,
		"pending_party_sequence": pending_party_sequence,
		"pending_party_action": pending_party_action,
		"pending_party_party_id": pending_party_party_id,
		"pending_party_target_actor_id": pending_party_target_actor_id,
		"pending_party_target_name": pending_party_target_name,
		"party_id": party_id,
		"party_revision": party_revision,
		"party_leader_actor_id": party_leader_actor_id,
		"party_members": party_members.duplicate(true),
		"pending_party_invite": pending_party_invite.duplicate(true),
		"last_party_result_sequence": last_party_result_sequence,
		"last_party_result_status": last_party_result_status,
		"last_party_result": last_party_result.duplicate(true),
		"stale_party_frames": stale_party_frames,
		"quest_sequence": quest_sequence,
		"pending_quest_sequence": pending_quest_sequence,
		"pending_quest_kind": pending_quest_kind,
		"pending_quest_npc": pending_quest_npc,
		"pending_quest_id": pending_quest_id,
		"pending_quest_choice_id": pending_quest_choice_id,
		"quest_view": quest_view.duplicate(true),
		"last_quest_response_sequence": last_quest_response_sequence,
		"last_quest_status": last_quest_status,
		"last_quest_result": last_quest_result.duplicate(true),
		"stale_quest_results": stale_quest_results,
		"pvp_sequence": pvp_sequence,
		"pending_pvp_sequence": pending_pvp_sequence,
		"pvp_mode": pvp_mode,
		"pvp_known": pvp_known,
		"pvp_map_allows": pvp_map_allows,
		"last_pvp_result_sequence": last_pvp_result_sequence,
		"last_pvp_result": last_pvp_result.duplicate(true),
		"stale_pvp_results": stale_pvp_results,
		"pvp_attack_sequence": pvp_attack_sequence,
		"pending_pvp_attack_sequence": pending_pvp_attack_sequence,
		"pending_pvp_target_actor_id": pending_pvp_target_actor_id,
		"last_pvp_attack_result_sequence": last_pvp_attack_result_sequence,
		"last_pvp_attack_result_status": last_pvp_attack_result_status,
		"last_pvp_attack_result": last_pvp_attack_result.duplicate(true),
		"stale_pvp_attack_results": stale_pvp_attack_results,
		"pvp_combat_visual_events": pvp_combat_visual_events.duplicate(true),
		"pvp_combat_attack_sequences": pvp_combat_attack_sequences.duplicate(true),
		"pvp_combat_damage_sequences": pvp_combat_damage_sequences.duplicate(true),
		"stale_pvp_combat_visuals": stale_pvp_combat_visuals,
		"pvp_skill_visual_events": pvp_skill_visual_events.duplicate(true),
		"pvp_skill_visual_sequences": pvp_skill_visual_sequences.duplicate(true),
		"pvp_skill_damage_sequences": pvp_skill_damage_sequences.duplicate(true),
		"stale_pvp_skill_visuals": stale_pvp_skill_visuals,
		"gm_authorized": gm_authorized,
		"gm_sequence": gm_sequence,
		"pending_gm_sequence": pending_gm_sequence,
		"pending_gm_target_actor_id": pending_gm_target_actor_id,
		"pending_gm_name_state": pending_gm_name_state,
		"last_gm_result_sequence": last_gm_result_sequence,
		"last_gm_result_status": last_gm_result_status,
		"last_gm_result": last_gm_result.duplicate(true),
		"stale_gm_results": stale_gm_results,
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
		"player_identities": player_identities.duplicate(true),
		"stale_player_identities": stale_player_identities,
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

func _apply_registration(frame:PackedByteArray)->bool:
	if state!=STATE_REGISTERING:
		return fail("account_registration_result_out_of_state")
	var decoded:=WireCodec.decode_account_register_result(frame)
	if decoded.is_empty():
		return fail("invalid_account_registration_result")
	registration_status=int(decoded.status)
	if registration_status==0:
		state=STATE_NEW
		return true
	state=STATE_FAILED
	return true


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
	_reset_character_actions_and_combat()
	_reset_character_economy_state()
	_reset_character_world_state(decoded)
	state = STATE_IN_WORLD
	return true

# A newly chosen character must not inherit prior Actor, UID or map generations.
# Keep all mutations in this sole reducer; command/visual processing stays FIFO.
func _reset_character_actions_and_combat() -> void:
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
	pending_skill_target_kind = 0
	pending_skill_target_id = 0
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

func _reset_character_economy_state() -> void:
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
	skill_book.clear()
	skill_book_ready = false
	skill_book_revision = 0
	skill_book_next_index = 0
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
	enhancement_sequence = 0
	pending_enhancement_sequence = 0
	pending_enhancement_kind = 0
	pending_enhancement_npc = 0
	pending_enhancement_item_uid = 0
	active_enhancement_npc = 0
	enhancement_items.clear()
	enhancement_ready = false
	enhancement_next_index = 0
	enhancement_revision = 0
	enhancement_wallet_money = 0
	last_enhancement_open_status = -1
	last_enhancement_response_sequence = 0
	last_enhancement_result.clear()
	stale_enhancement_results = 0
	_clear_trade_state(true)
	_clear_player_shop_map()

func _reset_character_world_state(decoded: Dictionary) -> void:
	_clear_pvp_map()
	last_trade_terminal.clear()
	stale_trade_frames = 0
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
	player_identities.clear()
	stale_player_identities = 0
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
	if sequence != pending_skill_sequence or pending_skill_target_kind != 0:
		return fail("skill_result_without_matching_pending_request")
	if last_skill_server_tick >= 0 and tick < last_skill_server_tick:
		return fail("skill_server_tick_regressed")
	last_skill_result_sequence = sequence
	last_skill_result_status = int(result.status)
	last_skill_server_tick = tick
	last_skill_result = result.duplicate(true)
	pending_skill_sequence = 0
	pending_skill_target_kind = 0
	pending_skill_target_id = 0
	return true

func _apply_pvp_skill_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("pvp_skill_result_out_of_state")
	var result:=WireCodec.decode_pvp_skill_result(frame)
	if result.is_empty():
		return fail("invalid_pvp_skill_result")
	var epoch:=int(result.map_epoch)
	var sequence:=int(result.skill_sequence)
	var tick:=int(result.server_tick)
	if epoch<map_epoch:
		stale_skill_results+=1
		return true
	if epoch>map_epoch:
		return fail("future_pvp_skill_result")
	if sequence>skill_sequence:
		return fail("pvp_skill_result_for_unsent_sequence")
	if sequence<=last_skill_result_sequence:
		stale_skill_results+=1
		return true
	if sequence!=pending_skill_sequence or pending_skill_target_kind!=1 or int(result.target_actor_id)!=pending_skill_target_id:
		return fail("pvp_skill_result_without_matching_pending_request")
	if last_skill_server_tick>=0 and tick<last_skill_server_tick:
		return fail("pvp_skill_server_tick_regressed")
	last_skill_result_sequence=sequence
	last_skill_result_status=int(result.status)
	last_skill_server_tick=tick
	last_skill_result=result.duplicate(true)
	last_skill_result["target_kind"]="player"
	pending_skill_sequence=0
	pending_skill_target_kind=0
	pending_skill_target_id=0
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
	# Presentation may legitimately reference the latest coalesced monster
	# revision before its full MonsterDelta is reduced. It never mutates HP/state.
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
	# Presentation may lead the full coalesced MonsterDelta in the reliable FIFO.
	# The visual is non-authoritative and must not invent a resync requirement.
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
		_reset_map_combat_and_item_commands()
		_reset_map_npc_services()
		_reset_map_social_and_movement()
	if completing_resync:
		resync_completions += 1
		last_world_ready_ack_epoch = 0
		last_world_ready_ack_tick = -1
	resync_pending = false
	resync_request_sent = false
	resync_reason = ""
	# A zero-skill character intentionally receives no opcode104 so legacy V1
	# login ordering remains byte-for-byte unchanged. WorldReady is the barrier
	# that proves the Server finished pre-world baselines. A partial non-empty
	# skill book at this point is a protocol violation, never silently empty.
	if not skill_book_ready:
		if skill_book_next_index != 0:
			return fail("skill_book_incomplete_before_world_ready")
		skill_book_ready = true
		skill_book_revision += 1
	self_state.clear()
	x = float(ready.x)
	y = float(ready.y)
	facing = int(ready.facing)
	world_ready = true
	entities.clear()
	player_identities.clear()
	monsters.clear()
	ground_drops.clear()
	# One-shot presentation events are never replayed through a replacement
	# WorldReady baseline. Sequence memory is retained for same-epoch resync so
	# a delayed pre-resync visual cannot replay.
	player_skill_events.clear()
	player_attack_events.clear()
	player_damage_visual_events.clear()
	player_respawn_visual_events.clear()
	pvp_combat_visual_events.clear()
	pvp_skill_visual_events.clear()
	last_aoi_tick = tick
	pending_world_ready_ack_epoch = map_epoch
	pending_world_ready_ack_tick = tick
	return true

# New MapEpoch invalidates command sequences and visuals from the old map.
# These methods intentionally mutate only this WorldState: never create a
# second reducer, transport owner, or unordered replay queue.
func _reset_map_combat_and_item_commands() -> void:
	npc_interaction_sequence = 0
	attack_sequence = 0
	last_attack_result_sequence = 0
	last_attack_result_status = -1
	last_attack_server_tick = -1
	last_attack_result.clear()
	stale_attack_results = 0
	skill_sequence = 0
	pending_skill_sequence = 0
	pending_skill_target_kind = 0
	pending_skill_target_id = 0
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

func _reset_map_npc_services() -> void:
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
	enhancement_sequence = 0
	pending_enhancement_sequence = 0
	pending_enhancement_kind = 0
	pending_enhancement_npc = 0
	pending_enhancement_item_uid = 0
	active_enhancement_npc = 0
	enhancement_items.clear()
	enhancement_ready = false
	enhancement_next_index = 0
	enhancement_revision = 0
	enhancement_wallet_money = 0
	last_enhancement_open_status = -1
	last_enhancement_response_sequence = 0
	last_enhancement_result.clear()
	stale_enhancement_results = 0

func _reset_map_social_and_movement() -> void:
	_clear_trade_state(true)
	_clear_player_shop_map()
	_clear_quest_map()
	_clear_pvp_map()
	last_trade_terminal.clear()
	stale_trade_frames = 0
	respawn_sequence = 0
	last_respawn_result_sequence = 0
	last_respawn_result_status = -1
	last_respawn_result.clear()
	stale_respawn_results = 0
	_clear_pending_whisper()
	_clear_pending_party()
	movement_sequence = 0
	last_receipt_sequence = 0
	last_receipt_status = -1
	last_server_tick = -1
	last_world_ready_ack_epoch = 0
	last_world_ready_ack_tick = -1

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

func _apply_player_identity(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("player_identity_out_of_state")
	var value:=WireCodec.decode_player_identity(frame)
	if value.is_empty():
		return fail("invalid_player_identity")
	var receiver_epoch:=int(value.receiver_map_epoch)
	if resync_pending:
		stale_player_identities+=1
		return true
	if not world_ready:
		return fail("player_identity_before_world_ready")
	if receiver_epoch<map_epoch:
		stale_player_identities+=1
		return true
	if receiver_epoch>map_epoch:
		return fail("future_player_identity_map_epoch")

	var id:=int(value.actor_id)
	var connection:=int(value.connection_epoch)
	var entity_epoch:=int(value.entity_map_epoch)
	if id==actor_id:
		if entity_epoch!=map_epoch:
			return _need_resync("self_identity_wrong_map_generation")
		if not self_state.is_empty() and int(self_state.get("connection_epoch",0))!=connection:
			return _need_resync("self_identity_wrong_connection")
	else:
		if not entities.has(id):
			return _need_resync("player_identity_without_spawn")
		var entity:Dictionary=entities[id]
		if int(entity.get("connection_epoch",0))!=connection or int(entity.get("entity_map_epoch",0))!=entity_epoch:
			return _need_resync("player_identity_wrong_entity_generation")

	var previous:Dictionary=player_identities.get(id,{})
	if not previous.is_empty():
		if int(previous.get("connection_epoch",0))!=connection or int(previous.get("entity_map_epoch",0))!=entity_epoch:
			return _need_resync("player_identity_generation_conflict")
		var updated:=int(value.pvp_updated_unix_ms)
		var prior_updated:=int(previous.get("pvp_updated_unix_ms",0))
		if updated<prior_updated:
			stale_player_identities+=1
			return true
		if updated==prior_updated:
			if str(previous.get("display_name",""))!=str(value.display_name) or int(previous.get("mode",-1))!=int(value.mode) or bool(previous.get("red_man",false))!=bool(value.red_man) or int(previous.get("pink_until_unix_ms",0))!=int(value.pink_until_unix_ms):
				return _need_resync("player_identity_same_revision_conflict")
	player_identities[id]=value.duplicate(true)
	return true


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
	player_identities.erase(id)
	if (
		not player_shop_inspected.is_empty() and
		int(player_shop_inspected.get("seller_actor_id",0))==id
	):
		player_shop_inspected.clear()
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
		# MonsterRuntime may make several authoritative AI transitions inside one
		# server tick and WorldAoi intentionally coalesces them to the latest
		# complete snapshot. Reliable FIFO + ServerTick + full identity therefore
		# require only a strictly newer revision here, not contiguous +1.
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


func _apply_skill_book_snapshot(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("skill_book_snapshot_out_of_state")
	var value:=WireCodec.decode_skill_book_snapshot(frame)
	if value.is_empty():
		return fail("invalid_skill_book_snapshot")
	if (
		skill_book_ready or int(value.character_id)!=actor_id or
		int(value.start_index)!=skill_book_next_index
	):
		return fail("skill_book_snapshot_wrong_character_or_sequence")
	var skills:Array=value.skills
	if skills.is_empty() and not bool(value.complete):
		return fail("skill_book_empty_nonfinal_chunk")
	for raw in skills:
		var skill:Dictionary=raw
		var skill_id:=int(skill.skill_id)
		if skill_book.has(skill_id):
			return fail("skill_book_duplicate_skill_id")
		skill_book[skill_id]=int(skill.level)
		skill_book_next_index+=1
	if bool(value.complete):
		skill_book_ready=true
		skill_book_revision+=1
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
			"drop_id":int(result.drop_id),
			"enhancement_level":0}
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
			receipt["enhancement_level"] = int(previous.get("enhancement_level",0))
			inventory_items.erase(uid)
			storage_items[uid] = receipt
		else:
			if not storage_items.has(uid) or inventory_items.has(uid):
				return fail("storage_withdraw_location_conflict")
			var previous: Dictionary = storage_items[uid]
			if int(previous.item_definition_id) != int(receipt.item_definition_id) or int(previous.quantity) != int(receipt.quantity) or int(previous.get("drop_id",0)) != int(receipt.drop_id):
				return fail("storage_withdraw_receipt_mismatch")
			receipt["enhancement_level"] = int(previous.get("enhancement_level",0))
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
			receipt["enhancement_level"] = int(previous.get("enhancement_level",0))
			inventory_items.erase(uid)
			equipment_items[uid] = receipt
			equipment_slots[slot_id] = uid
		else:
			if not equipment_items.has(uid) or inventory_items.has(uid) or storage_items.has(uid):
				return fail("equipment_unequip_location_conflict")
			var previous: Dictionary = equipment_items[uid]
			if int(previous.item_definition_id) != int(receipt.item_definition_id) or int(previous.slot_id) != slot_id or int(previous.get("drop_id",0)) != int(receipt.drop_id) or int(equipment_slots.get(slot_id,0)) != uid:
				return fail("equipment_unequip_receipt_mismatch")
			var level := int(previous.get("enhancement_level",0))
			equipment_items.erase(uid)
			equipment_slots.erase(slot_id)
			inventory_items[uid] = {"item_uid":uid,
				"item_definition_id":int(receipt.item_definition_id),
				"quantity":1,"drop_id":int(receipt.drop_id),
				"enhancement_level":level}
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


func _item_matches_inventory_or_equipment(item: Dictionary) -> bool:
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
		if repair_items.has(uid) or not _item_matches_inventory_or_equipment(item):
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


func _clear_pending_enhancement() -> void:
	pending_enhancement_sequence = 0
	pending_enhancement_kind = 0
	pending_enhancement_npc = 0
	pending_enhancement_item_uid = 0


func _close_enhancement() -> void:
	active_enhancement_npc = 0
	enhancement_items.clear()
	enhancement_ready = false
	enhancement_next_index = 0
	enhancement_wallet_money = 0


func _apply_enhancement_snapshot(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("enhancement_snapshot_out_of_state")
	var value := WireCodec.decode_enhancement_snapshot(frame)
	if value.is_empty():
		return fail("invalid_enhancement_snapshot")
	var epoch := int(value.map_epoch)
	var sequence := int(value.enhancement_sequence)
	if epoch < map_epoch:
		stale_enhancement_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_enhancement_snapshot")
	if sequence > enhancement_sequence:
		return fail("unsent_enhancement_snapshot")
	if sequence <= last_enhancement_response_sequence:
		stale_enhancement_results += 1
		return true
	if sequence != pending_enhancement_sequence or pending_enhancement_kind != 1 or int(value.npc_spawn_id) != pending_enhancement_npc:
		return fail("enhancement_snapshot_wrong_request")
	last_enhancement_open_status = int(value.status)
	if int(value.status) != 0:
		_close_enhancement()
		last_enhancement_response_sequence = sequence
		_clear_pending_enhancement()
		return true
	if int(value.start_index) != enhancement_next_index:
		return fail("enhancement_snapshot_wrong_chunk")
	if enhancement_next_index == 0:
		enhancement_wallet_money = int(value.wallet_money)
	elif enhancement_wallet_money != int(value.wallet_money):
		return fail("enhancement_snapshot_wallet_changed_midstream")
	var items: Array = value.items
	if items.is_empty() and not bool(value.complete):
		return fail("enhancement_empty_nonfinal_chunk")
	for item in items:
		var uid := int(item.item_uid)
		if enhancement_items.has(uid) or not _item_matches_inventory_or_equipment(item):
			return fail("enhancement_preview_uid_or_location_conflict")
		enhancement_items[uid] = item.duplicate(true)
		enhancement_next_index += 1
	if bool(value.complete):
		active_enhancement_npc = int(value.npc_spawn_id)
		enhancement_ready = true
		enhancement_revision += 1
		last_enhancement_response_sequence = sequence
		if active_shop_npc != 0:
			shop_money = enhancement_wallet_money
		if active_bank_npc != 0:
			bank_wallet_money = enhancement_wallet_money
		if active_repair_npc != 0:
			repair_wallet_money = enhancement_wallet_money
		_clear_pending_enhancement()
	return true


func _apply_enhancement_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("enhancement_result_out_of_state")
	var result := WireCodec.decode_enhancement_result(frame)
	if result.is_empty():
		return fail("invalid_enhancement_result")
	var epoch := int(result.map_epoch)
	var sequence := int(result.enhancement_sequence)
	if epoch < map_epoch:
		stale_enhancement_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_enhancement_result")
	if sequence > enhancement_sequence:
		return fail("unsent_enhancement_result")
	if sequence <= last_enhancement_response_sequence:
		stale_enhancement_results += 1
		return true
	if sequence != pending_enhancement_sequence or pending_enhancement_kind != 2 or int(result.npc_spawn_id) != pending_enhancement_npc or int(result.item_uid) != pending_enhancement_item_uid:
		return fail("enhancement_result_wrong_request")
	var status := int(result.status)
	if status in [0,1,7]:
		if not enhancement_ready or active_enhancement_npc != pending_enhancement_npc or not enhancement_items.has(pending_enhancement_item_uid):
			return fail("enhancement_result_without_preview")
		var previous: Dictionary = enhancement_items[pending_enhancement_item_uid]
		if int(previous.item_definition_id) != int(result.item_definition_id) or int(previous.location) != int(result.location) or int(previous.current_level) != int(result.previous_level) or int(previous.target_level) != int(result.target_level) or int(previous.money_cost) != int(result.money_cost):
			return fail("enhancement_result_metadata_mismatch")
		enhancement_wallet_money = int(result.wallet_money)
		if status in [0,1]:
			if bool(result.succeeded):
				var location := int(result.location)
				var owned: Dictionary = {}
				if location == 1:
					owned = (inventory_items[pending_enhancement_item_uid] as Dictionary).duplicate(true)
					owned["enhancement_level"] = int(result.current_level)
					inventory_items[pending_enhancement_item_uid] = owned
					inventory_revision += 1
				elif location == 2:
					owned = (equipment_items[pending_enhancement_item_uid] as Dictionary).duplicate(true)
					owned["enhancement_level"] = int(result.current_level)
					equipment_items[pending_enhancement_item_uid] = owned
					equipment_revision += 1
				enhancement_items.erase(pending_enhancement_item_uid)
			elif int(result.current_level) != int(previous.current_level):
				return fail("enhancement_failed_level_changed")
		if active_shop_npc != 0:
			shop_money = enhancement_wallet_money
		if active_bank_npc != 0:
			bank_wallet_money = enhancement_wallet_money
		if active_repair_npc != 0:
			repair_wallet_money = enhancement_wallet_money
		enhancement_revision += 1
	elif status in [5,6]:
		enhancement_items.erase(pending_enhancement_item_uid)
		enhancement_wallet_money = int(result.wallet_money)
		enhancement_revision += 1
	elif status in [2,3,4,8,9,10]:
		_close_enhancement()
	else:
		return fail("enhancement_result_unhandled_status")
	last_enhancement_response_sequence = sequence
	last_enhancement_result = result.duplicate(true)
	_clear_pending_enhancement()
	return true


func _clear_player_shop_pending()->void:
	pending_player_shop_sequence=0
	pending_player_shop_action=0
	pending_player_shop_seller_actor_id=0
	pending_player_shop_listing_id=0
	pending_player_shop_item_uid=0
	pending_player_shop_price=0


func _clear_player_shop_map()->void:
	player_shop_sequence=0
	_clear_player_shop_pending()
	player_shop_own_listing.clear()
	player_shop_inspected.clear()
	player_shop_wallet=0
	last_player_shop_result_sequence=0
	last_player_shop_result_status=-1
	last_player_shop_result.clear()
	last_player_shop_sold.clear()
	last_player_shop_sold_listing_id=0
	stale_player_shop_frames=0


func _player_shop_item_from_result(value:Dictionary)->Dictionary:
	return {
		"item_uid":int(value.item_uid),
		"item_definition_id":int(value.item_definition_id),
		"quantity":int(value.quantity),
		"drop_id":int(value.source_drop_id),
		"durability_raw":int(value.durability_raw),
		"enhancement_level":int(value.enhancement_level)
	}


func _apply_player_shop_result(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("player_shop_result_out_of_state")
	var value:=WireCodec.decode_player_shop_result(frame)
	if value.is_empty():
		return fail("invalid_player_shop_result")
	var epoch:=int(value.map_epoch)
	var sequence:=int(value.shop_sequence)
	var action:=int(value.action)
	var status:=int(value.status)
	if epoch<map_epoch:
		stale_player_shop_frames+=1
		return true
	if epoch>map_epoch:
		return fail("future_player_shop_result")
	if sequence<=last_player_shop_result_sequence:
		stale_player_shop_frames+=1
		return true
	if (
		sequence>player_shop_sequence or sequence!=pending_player_shop_sequence or
		action!=pending_player_shop_action
	):
		return fail("player_shop_result_without_matching_request")
	if (
		action in [3,4] and
		int(value.seller_actor_id)!=pending_player_shop_seller_actor_id
	):
		return fail("player_shop_result_wrong_seller")
	if (
		action in [2,4] and
		status not in [2,3,4,5,7,8,9,10,11,15] and
		int(value.listing_id)!=pending_player_shop_listing_id
	):
		return fail("player_shop_result_wrong_listing")
	if status==4:
		return fail("player_shop_result_stale_generation")

	if action==1:
		if status==0:
			if (
				int(value.seller_actor_id)!=actor_id or int(value.listing_id)<=0 or
				int(value.item_uid)!=pending_player_shop_item_uid or
				int(value.price)!=pending_player_shop_price or
				not inventory_items.has(int(value.item_uid))
			):
				return fail("player_shop_open_receipt_mismatch")
			var owned:Dictionary=inventory_items[int(value.item_uid)]
			if (
				int(owned.get("item_definition_id",0))!=int(value.item_definition_id) or
				int(owned.get("quantity",0))!=int(value.quantity) or
				int(owned.get("drop_id",0))!=int(value.source_drop_id) or
				int(owned.get("enhancement_level",0))!=int(value.enhancement_level)
			):
				return fail("player_shop_open_item_snapshot_mismatch")
			player_shop_own_listing=value.duplicate(true)
			player_shop_wallet=int(value.seller_wallet)
		elif status not in [6,7,8]:
			return fail("player_shop_open_unhandled_status")
	elif action==2:
		if status in [0,1,2,13]:
			player_shop_own_listing.clear()
		elif status not in [4,5,15]:
			return fail("player_shop_close_unhandled_status")
	elif action==3:
		player_shop_wallet=int(value.buyer_wallet)
		if status==0:
			if (
				int(value.listing_id)<=0 or int(value.item_uid)<=0 or
				int(value.item_definition_id)<=0 or int(value.quantity)<=0 or
				int(value.price)<=0
			):
				return fail("player_shop_inspect_empty_listing")
			player_shop_inspected=value.duplicate(true)
		elif status in [2,3,12,13,14]:
			player_shop_inspected.clear()
		elif status not in [4,5,15]:
			return fail("player_shop_inspect_unhandled_status")
	else:
		if status in [0,1]:
			var uid:=int(value.item_uid)
			if uid<=0 or int(value.listing_id)!=pending_player_shop_listing_id:
				return fail("player_shop_buy_empty_receipt")
			var purchased:=_player_shop_item_from_result(value)
			if status==0:
				if (
					not inventory_ready or inventory_items.has(uid) or
					storage_items.has(uid) or equipment_items.has(uid)
				):
					return fail("player_shop_buy_uid_conflict")
				inventory_items[uid]=purchased
				inventory_revision+=1
			elif inventory_items.has(uid):
				var existing:Dictionary=inventory_items[uid]
				if (
					int(existing.get("item_definition_id",0))!=int(purchased.item_definition_id) or
					int(existing.get("quantity",0))!=int(purchased.quantity) or
					int(existing.get("drop_id",0))!=int(purchased.drop_id) or
					int(existing.get("enhancement_level",0))!=int(purchased.enhancement_level)
				):
					return fail("player_shop_buy_replay_uid_conflict")
			player_shop_wallet=int(value.buyer_wallet)
			player_shop_inspected.clear()
		elif status in [2,3,12,13,14]:
			player_shop_inspected.clear()
		elif status not in [4,5,8,9,10,11,15]:
			return fail("player_shop_buy_unhandled_status")

	last_player_shop_result_sequence=sequence
	last_player_shop_result_status=status
	last_player_shop_result=value.duplicate(true)
	_clear_player_shop_pending()
	return true


func _apply_player_shop_sold(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("player_shop_sold_out_of_state")
	var value:=WireCodec.decode_player_shop_sold(frame)
	if value.is_empty():
		return fail("invalid_player_shop_sold")
	var epoch:=int(value.receiver_map_epoch)
	if epoch<map_epoch:
		stale_player_shop_frames+=1
		return true
	if epoch>map_epoch:
		return fail("future_player_shop_sold")
	var listing:=int(value.listing_id)
	if listing==last_player_shop_sold_listing_id:
		stale_player_shop_frames+=1
		return true
	if (
		player_shop_own_listing.is_empty() or
		int(player_shop_own_listing.get("listing_id",0))!=listing
	):
		stale_player_shop_frames+=1
		return true
	var uid:=int(value.item_uid)
	if (
		uid!=int(player_shop_own_listing.get("item_uid",0)) or
		not inventory_ready or not inventory_items.has(uid)
	):
		return fail("player_shop_sold_inventory_mismatch")
	inventory_items.erase(uid)
	inventory_revision+=1
	player_shop_wallet=int(value.seller_wallet)
	last_player_shop_sold=value.duplicate(true)
	last_player_shop_sold_listing_id=listing
	player_shop_own_listing.clear()
	return true


func _apply_chat_delivery(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("chat_delivery_out_of_state")
	var value:=WireCodec.decode_chat_delivery(frame)
	if value.is_empty():
		return fail("invalid_chat_delivery")
	var epoch:=int(value.receiver_map_epoch)
	if epoch<map_epoch:
		stale_chat_deliveries+=1
		return true
	if epoch>map_epoch:
		return fail("future_chat_delivery")
	var channel:=int(value.channel)
	if channel not in [0,3,4]:
		return fail("unsupported_chat_delivery_channel")
	var sender:=int(value.sender_actor_id)
	var connection:=int(value.sender_connection_epoch)
	var sequence:=int(value.chat_sequence)
	if channel==0:
		if sender==actor_id:
			if sequence>chat_sequence:
				return fail("chat_self_delivery_without_send")
		else:
			if not entities.has(sender):
				stale_chat_deliveries+=1
				return true
			var entity:Dictionary=entities[sender]
			if int(entity.get("connection_epoch",0))!=connection:
				stale_chat_deliveries+=1
				return true
	elif channel==3:
		if party_id<=0:
			stale_chat_deliveries+=1
			return true
		if sender==actor_id:
			if sequence>chat_sequence:
				return fail("party_chat_self_delivery_without_send")
		else:
			if not party_members.has(sender):
				stale_chat_deliveries+=1
				return true
			var member:Dictionary=party_members[sender]
			if int(member.get("connection_epoch",0))!=connection:
				stale_chat_deliveries+=1
				return true
	else:
		# Whisper is a direct online route and intentionally does not require
		# the sender to be inside AOI or on the same map. The Server-authenticated
		# sender generation in this reliable delivery is the authority.
		if sender==actor_id:
			return fail("whisper_delivery_from_self")
	var key:="%d/%d" % [sender,connection]
	var previous:=int(chat_sender_sequences.get(key,0))
	if sequence<=previous:
		stale_chat_deliveries+=1
		return true
	chat_sender_sequences[key]=sequence
	var row:Dictionary=value.duplicate(true)
	if channel==4:
		row["direction"]="incoming"
	chat_messages.append(row)
	while chat_messages.size()>CHAT_BACKLOG_MAX:
		chat_messages.pop_front()
	return true


func _apply_whisper_result(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("whisper_result_out_of_state")
	var value:=WireCodec.decode_whisper_result(frame)
	if value.is_empty():
		return fail("invalid_whisper_result")
	var epoch:=int(value.map_epoch)
	var sequence:=int(value.chat_sequence)
	if epoch<map_epoch:
		stale_whisper_results+=1
		return true
	if epoch>map_epoch:
		return fail("future_whisper_result")
	if sequence<=last_whisper_result_sequence:
		stale_whisper_results+=1
		return true
	if sequence>chat_sequence:
		return fail("unsent_whisper_result")
	var status:=int(value.status)
	var response_target:=int(value.target_actor_id)
	var target_matches:=response_target==pending_whisper_target_actor_id
	if not pending_whisper_target_name.is_empty():
		target_matches=response_target>=0
	if sequence!=pending_whisper_sequence or not target_matches:
		return fail("whisper_result_wrong_request")
	if status==0:
		if (
			int(value.target_connection_epoch)<=0 or
			str(value.target_name).strip_edges().is_empty()
		):
			return fail("whisper_delivered_without_target_identity")
		chat_messages.append({
			"receiver_map_epoch":map_epoch,
			"sender_actor_id":actor_id,
			"sender_connection_epoch":0,
			"chat_sequence":sequence,
			"channel":4,
			"bubble_type":pending_whisper_bubble_type,
			"sender_name":"",
			"message":pending_whisper_message,
			"direction":"outgoing",
			"target_actor_id":response_target,
			"target_connection_epoch":int(value.target_connection_epoch),
			"target_name":str(value.target_name)
		})
		while chat_messages.size()>CHAT_BACKLOG_MAX:
			chat_messages.pop_front()
	elif status==2:
		if response_target<=0:
			return fail("whisper_self_target_without_resolved_actor")
	elif status!=1:
		return fail("whisper_result_unknown_status")
	last_whisper_result_sequence=sequence
	last_whisper_result_status=status
	last_whisper_result=value.duplicate(true)
	_clear_pending_whisper()
	return true

func _apply_party_result(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("party_result_out_of_state")
	var value:=WireCodec.decode_party_result(frame)
	if value.is_empty():
		return fail("invalid_party_result")
	var epoch:=int(value.map_epoch)
	var sequence:=int(value.party_sequence)
	if epoch<map_epoch:
		stale_party_frames+=1
		return true
	if epoch>map_epoch:
		return fail("future_party_result")
	if sequence<=last_party_result_sequence:
		stale_party_frames+=1
		return true
	var action:=int(value.action)
	var status:=int(value.status)
	var response_target:=int(value.target_actor_id)
	var target_matches:=response_target==pending_party_target_actor_id
	if pending_party_action==1 and not pending_party_target_name.is_empty():
		target_matches=response_target>=0
	if (
		sequence>party_sequence or sequence!=pending_party_sequence or
		action!=pending_party_action or not target_matches
	):
		return fail("party_result_without_matching_request")
	var response_party:=int(value.party_id)
	if status==0:
		if action in [2,3]:
			pending_party_invite.clear()
		if action==1 and response_party<=0:
			return fail("party_invite_success_without_party")
		if action==1 and not pending_party_target_name.is_empty() and response_target<=0:
			return fail("party_name_invite_success_without_resolved_target")
	elif status<1 or status>10:
		return fail("party_result_unknown_status")
	if action in [2,3] and status in [7,8]:
		pending_party_invite.clear()
	last_party_result_sequence=sequence
	last_party_result_status=status
	last_party_result=value.duplicate(true)
	if not pending_party_target_name.is_empty():
		last_party_result["requested_target_name"]=pending_party_target_name
	_clear_pending_party()
	return true


func _apply_party_invite(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("party_invite_out_of_state")
	var value:=WireCodec.decode_party_invite(frame)
	if value.is_empty():
		return fail("invalid_party_invite")
	var epoch:=int(value.receiver_map_epoch)
	if epoch<map_epoch:
		stale_party_frames+=1
		return true
	if epoch>map_epoch:
		return fail("future_party_invite")
	if party_id!=0:
		stale_party_frames+=1
		return true
	if int(value.inviter_actor_id)==actor_id:
		return fail("party_invite_from_self")
	if not pending_party_invite.is_empty():
		if (
			int(pending_party_invite.get("party_id",0))==int(value.party_id) and
			int(pending_party_invite.get("revision",0))>=int(value.revision) and
			int(pending_party_invite.get("inviter_actor_id",0))==int(value.inviter_actor_id)
		):
			stale_party_frames+=1
			return true
		return fail("multiple_pending_party_invites")
	pending_party_invite=value.duplicate(true)
	return true


func _apply_party_snapshot(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("party_snapshot_out_of_state")
	var value:=WireCodec.decode_party_snapshot(frame)
	if value.is_empty():
		return fail("invalid_party_snapshot")
	var epoch:=int(value.receiver_map_epoch)
	var incoming_party:=int(value.party_id)
	var revision:=int(value.revision)
	if epoch<map_epoch:
		stale_party_frames+=1
		return true
	if epoch>map_epoch:
		return fail("future_party_snapshot")
	var members:Array=value.members
	if members.is_empty():
		if party_id==0 or incoming_party!=party_id or revision<=party_revision:
			stale_party_frames+=1
			return true
		party_id=0
		party_revision=revision
		party_leader_actor_id=0
		party_members.clear()
		return true
	if party_id!=0 and incoming_party!=party_id:
		return fail("party_snapshot_conflicts_with_current_party")
	if party_id==incoming_party and revision<=party_revision:
		stale_party_frames+=1
		return true
	var next_members:={}
	for raw in members:
		var member:Dictionary=raw
		var member_actor:=int(member.actor_id)
		next_members[member_actor]=member.duplicate(true)
	if not next_members.has(actor_id):
		return fail("party_snapshot_missing_self")
	if not self_state.is_empty():
		var own_connection:=int(self_state.get("connection_epoch",0))
		if (
			own_connection>0 and
			int((next_members[actor_id] as Dictionary).connection_epoch)!=own_connection
		):
			return fail("party_snapshot_wrong_self_generation")
	var leader:=int(value.leader_actor_id)
	if not next_members.has(leader):
		return fail("party_snapshot_missing_leader")
	party_id=incoming_party
	party_revision=revision
	party_leader_actor_id=leader
	party_members=next_members
	if (
		not pending_party_invite.is_empty() and
		int(pending_party_invite.get("party_id",0))==incoming_party
	):
		pending_party_invite.clear()
	return true


func _clear_pending_quest()->void:
	pending_quest_sequence=0
	pending_quest_kind=0
	pending_quest_npc=0
	pending_quest_id=0
	pending_quest_choice_id=0


func _clear_quest_map()->void:
	quest_sequence=0
	_clear_pending_quest()
	quest_view.clear()
	last_quest_response_sequence=0
	last_quest_status=-1
	last_quest_result.clear()
	stale_quest_results=0


func _apply_quest_view(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("quest_view_out_of_state")
	var value:=WireCodec.decode_quest_view(frame)
	if value.is_empty():
		return fail("invalid_quest_view")
	var epoch:=int(value.map_epoch)
	var sequence:=int(value.quest_sequence)
	var npc:=int(value.npc_spawn_id)
	var status:=int(value.status)
	if epoch<map_epoch:
		stale_quest_results+=1
		return true
	if epoch>map_epoch:
		return fail("future_quest_view")
	if sequence<=last_quest_response_sequence:
		stale_quest_results+=1
		return true
	if (
		sequence>quest_sequence or sequence!=pending_quest_sequence or
		npc!=pending_quest_npc or pending_quest_kind not in [1,2]
	):
		return fail("quest_view_without_matching_request")
	var has_view:=status in [0,1,2,6,7]
	if pending_quest_kind==2 and has_view:
		if int(value.quest_id)!=pending_quest_id:
			return fail("quest_view_wrong_quest")
		if status in [0,1,2] and pending_quest_choice_id<=0:
			return fail("quest_view_missing_choice_request")
	if has_view:
		quest_view=value.duplicate(true)
	else:
		# A no-view rejection is authoritative evidence that the previous
		# interaction view is no longer actionable for this request/generation.
		quest_view.clear()
	last_quest_response_sequence=sequence
	last_quest_status=status
	last_quest_result=value.duplicate(true)
	_clear_pending_quest()
	return true


func _clear_pvp_map() -> void:
	gm_sequence = 0
	pending_gm_sequence = 0
	pending_gm_target_actor_id = 0
	pending_gm_name_state = -1
	last_gm_result_sequence = 0
	last_gm_result_status = -1
	last_gm_result.clear()
	stale_gm_results = 0
	pvp_sequence = 0
	pending_pvp_sequence = 0
	pvp_mode = -1
	pvp_known = false
	pvp_map_allows = false
	last_pvp_result_sequence = 0
	last_pvp_result.clear()
	stale_pvp_results = 0
	pvp_attack_sequence = 0
	pending_pvp_attack_sequence = 0
	pending_pvp_target_actor_id = 0
	last_pvp_attack_result_sequence = 0
	last_pvp_attack_result_status = -1
	last_pvp_attack_result.clear()
	stale_pvp_attack_results = 0
	pvp_combat_visual_events.clear()
	pvp_combat_attack_sequences.clear()
	pvp_combat_damage_sequences.clear()
	stale_pvp_combat_visuals = 0
	pvp_skill_visual_events.clear()
	pvp_skill_visual_sequences.clear()
	pvp_skill_damage_sequences.clear()
	stale_pvp_skill_visuals = 0


func _apply_gm_capability(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("gm_capability_out_of_state")
	var value:=WireCodec.decode_gm_capability(frame)
	if value.is_empty():
		return fail("invalid_gm_capability")
	# Capability is exclusively server-issued after authenticated character bind.
	# It survives trusted map transfers on the same connection, but a reconnect
	# creates a new WorldState and must receive a fresh capability.
	gm_authorized=bool(value.authorized)
	if not gm_authorized:
		pending_gm_sequence=0
		pending_gm_target_actor_id=0
		pending_gm_name_state=-1
	return true


func _apply_gm_pvp_name_result(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("gm_pvp_name_result_out_of_state")
	var value:=WireCodec.decode_gm_pvp_name_result(frame)
	if value.is_empty():
		return fail("invalid_gm_pvp_name_result")
	var epoch:=int(value.map_epoch)
	var sequence:=int(value.gm_sequence)
	if epoch<map_epoch:
		stale_gm_results+=1
		return true
	if epoch>map_epoch:
		return fail("future_gm_pvp_name_result")
	if sequence<=last_gm_result_sequence:
		stale_gm_results+=1
		return true
	if (
		sequence!=pending_gm_sequence or sequence>gm_sequence or
		int(value.target_actor_id)!=pending_gm_target_actor_id or
		int(value.name_state)!=pending_gm_name_state
	):
		return fail("gm_pvp_name_result_without_matching_request")
	if int(value.status)==3:
		return fail("gm_pvp_name_result_stale_generation")
	last_gm_result_sequence=sequence
	last_gm_result_status=int(value.status)
	last_gm_result=value.duplicate(true)
	pending_gm_sequence=0
	pending_gm_target_actor_id=0
	pending_gm_name_state=-1
	if int(value.status)==2:
		# A server denial revokes local usability until a fresh connection gets
		# another server-issued capability; never infer privilege from username.
		gm_authorized=false
	return true


func _apply_pvp_mode_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("pvp_result_out_of_state")
	var value := WireCodec.decode_pvp_mode_result(frame)
	if value.is_empty():
		return fail("invalid_pvp_result")
	var epoch := int(value.map_epoch)
	var sequence := int(value.pvp_sequence)
	if epoch < map_epoch:
		stale_pvp_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_pvp_result")
	if sequence <= last_pvp_result_sequence:
		stale_pvp_results += 1
		return true
	if sequence != pending_pvp_sequence or sequence > pvp_sequence:
		return fail("pvp_result_without_matching_request")
	if int(value.status) == 4:
		return fail("pvp_result_stale_generation")
	# Never optimistically change PvP state; only the matching authoritative
	# response may reveal the durable character mode and current map policy.
	pvp_mode = int(value.mode)
	pvp_map_allows = bool(value.map_allows_pvp)
	pvp_known = true
	last_pvp_result_sequence = sequence
	last_pvp_result = value.duplicate(true)
	pending_pvp_sequence = 0
	return true


func _apply_pvp_attack_result(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("pvp_attack_result_out_of_state")
	var value := WireCodec.decode_pvp_attack_result(frame)
	if value.is_empty():
		return fail("invalid_pvp_attack_result")
	var epoch := int(value.map_epoch)
	var sequence := int(value.attack_sequence)
	if epoch < map_epoch:
		stale_pvp_attack_results += 1
		return true
	if epoch > map_epoch:
		return fail("future_pvp_attack_result")
	if sequence <= last_pvp_attack_result_sequence:
		stale_pvp_attack_results += 1
		return true
	if sequence != pending_pvp_attack_sequence or sequence > pvp_attack_sequence or int(value.target_actor_id) != pending_pvp_target_actor_id:
		return fail("pvp_attack_result_without_matching_request")
	if int(value.status) == 1:
		return fail("pvp_attack_result_stale_generation")
	last_pvp_attack_result_sequence = sequence
	last_pvp_attack_result_status = int(value.status)
	last_pvp_attack_result = value.duplicate(true)
	pending_pvp_attack_sequence = 0
	pending_pvp_target_actor_id = 0
	return true


func _apply_pvp_damage(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD or not combat_available:
		return fail("pvp_damage_out_of_state")
	var value := WireCodec.decode_pvp_damage(frame)
	if value.is_empty():
		return fail("invalid_pvp_damage")
	var epoch := int(value.map_epoch)
	var sequence := int(value.damage_sequence)
	var tick := int(value.server_tick)
	if epoch < map_epoch:
		stale_player_damage += 1
		return true
	if epoch > map_epoch:
		return fail("future_pvp_damage")
	if sequence <= player_damage_sequence:
		stale_player_damage += 1
		return true
	if sequence != player_damage_sequence + 1:
		return fail("pvp_damage_sequence_gap")
	if last_player_damage_tick >= 0 and tick < last_player_damage_tick:
		return fail("pvp_damage_tick_regressed")
	if int(value.max_hp) != player_max_hp:
		return fail("pvp_damage_max_hp_changed_without_baseline")
	if int(value.current_hp) > player_current_hp:
		return fail("pvp_damage_healed")
	player_damage_sequence = sequence
	last_player_damage_tick = tick
	player_current_hp = int(value.current_hp)
	player_dead = bool(value.died)
	last_player_damage = value.duplicate(true)
	last_player_damage["source_kind"] = "pvp"
	return true


func _apply_pvp_combat_visual(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("pvp_combat_visual_out_of_state")
	var value:=WireCodec.decode_pvp_combat_visual(frame)
	if value.is_empty():
		return fail("invalid_pvp_combat_visual")
	var disposition:=_check_aoi_generation(
		int(value.receiver_map_epoch),int(value.server_tick))
	if disposition<=0:
		if disposition==0:
			stale_pvp_combat_visuals+=1
		return disposition==0

	var attacker:=int(value.attacker_actor_id)
	var attacker_connection:=int(value.attacker_connection_epoch)
	var victim:=int(value.victim_actor_id)
	var victim_connection:=int(value.victim_connection_epoch)

	if attacker==actor_id:
		if not self_state.is_empty() and int(self_state.get("connection_epoch",0))!=attacker_connection:
			return _need_resync("pvp_combat_visual_attacker_self_generation")
		var own_identity:Dictionary=player_identities.get(actor_id,{})
		if self_state.is_empty() and not own_identity.is_empty() and int(own_identity.get("connection_epoch",0))!=attacker_connection:
			return _need_resync("pvp_combat_visual_attacker_identity_generation")
	else:
		if not entities.has(attacker):
			return _need_resync("pvp_combat_visual_attacker_without_spawn")
		if int((entities[attacker] as Dictionary).get("connection_epoch",0))!=attacker_connection:
			return _need_resync("pvp_combat_visual_attacker_generation")

	if victim==actor_id:
		if not self_state.is_empty() and int(self_state.get("connection_epoch",0))!=victim_connection:
			return _need_resync("pvp_combat_visual_victim_self_generation")
		var own_identity:Dictionary=player_identities.get(actor_id,{})
		if self_state.is_empty() and not own_identity.is_empty() and int(own_identity.get("connection_epoch",0))!=victim_connection:
			return _need_resync("pvp_combat_visual_victim_identity_generation")
	else:
		if not entities.has(victim):
			return _need_resync("pvp_combat_visual_victim_without_spawn")
		if int((entities[victim] as Dictionary).get("connection_epoch",0))!=victim_connection:
			return _need_resync("pvp_combat_visual_victim_generation")

	var attack_sequence:=int(value.attack_sequence)
	var damage_sequence:=int(value.damage_sequence)
	var attack_prior:Dictionary=pvp_combat_attack_sequences.get(attacker,{})
	var damage_prior:Dictionary=pvp_combat_damage_sequences.get(victim,{})
	var attack_stale:=false
	var damage_stale:=false
	if not attack_prior.is_empty():
		if int(attack_prior.get("connection_epoch",0))!=attacker_connection:
			return _need_resync("pvp_combat_visual_attack_sequence_generation")
		attack_stale=attack_sequence<=int(attack_prior.get("sequence",0))
	if not damage_prior.is_empty():
		if int(damage_prior.get("connection_epoch",0))!=victim_connection:
			return _need_resync("pvp_combat_visual_damage_sequence_generation")
		damage_stale=damage_sequence<=int(damage_prior.get("sequence",0))
	if attack_stale or damage_stale:
		if attack_stale and damage_stale:
			stale_pvp_combat_visuals+=1
			return true
		return _need_resync("pvp_combat_visual_sequence_conflict")

	pvp_combat_attack_sequences[attacker]={
		"connection_epoch":attacker_connection,"sequence":attack_sequence}
	pvp_combat_damage_sequences[victim]={
		"connection_epoch":victim_connection,"sequence":damage_sequence}
	pvp_combat_visual_events.append(value.duplicate(true))
	var cutoff:=maxi(int(value.server_tick),last_aoi_tick)-PLAYER_COMBAT_VISUAL_EVENT_TTL_TICKS
	while not pvp_combat_visual_events.is_empty() and int(pvp_combat_visual_events[0].get("server_tick",-1))<cutoff:
		pvp_combat_visual_events.pop_front()
	while pvp_combat_visual_events.size()>PLAYER_COMBAT_VISUAL_EVENT_MAX:
		pvp_combat_visual_events.pop_front()
	return true


func _apply_pvp_skill_visual(frame:PackedByteArray)->bool:
	if state!=STATE_IN_WORLD:
		return fail("pvp_skill_visual_out_of_state")
	var value:=WireCodec.decode_pvp_skill_visual(frame)
	if value.is_empty():
		return fail("invalid_pvp_skill_visual")
	var disposition:=_check_aoi_generation(
		int(value.receiver_map_epoch),int(value.server_tick))
	if disposition<=0:
		if disposition==0:
			stale_pvp_skill_visuals+=1
		return disposition==0
	var attacker:=int(value.attacker_actor_id)
	var ac:=int(value.attacker_connection_epoch)
	var victim:=int(value.victim_actor_id)
	var vc:=int(value.victim_connection_epoch)
	if attacker==actor_id:
		if not self_state.is_empty() and int(self_state.get("connection_epoch",0))!=ac:
			return _need_resync("pvp_skill_visual_attacker_self_generation")
	else:
		if not entities.has(attacker) or int((entities[attacker] as Dictionary).get("connection_epoch",0))!=ac:
			return _need_resync("pvp_skill_visual_attacker_generation")
	if victim==actor_id:
		if not self_state.is_empty() and int(self_state.get("connection_epoch",0))!=vc:
			return _need_resync("pvp_skill_visual_victim_self_generation")
	else:
		if not entities.has(victim) or int((entities[victim] as Dictionary).get("connection_epoch",0))!=vc:
			return _need_resync("pvp_skill_visual_victim_generation")
	var seq:=int(value.skill_sequence)
	var dmgseq:=int(value.damage_sequence)
	var prior:Dictionary=pvp_skill_visual_sequences.get(attacker,{})
	var dprior:Dictionary=pvp_skill_damage_sequences.get(victim,{})
	var stale1:=not prior.is_empty() and seq<=int(prior.get("sequence",0))
	var stale2:=not dprior.is_empty() and dmgseq<=int(dprior.get("sequence",0))
	if (not prior.is_empty() and int(prior.get("connection_epoch",0))!=ac) or (not dprior.is_empty() and int(dprior.get("connection_epoch",0))!=vc):
		return _need_resync("pvp_skill_visual_generation")
	if stale1 or stale2:
		if stale1 and stale2:
			stale_pvp_skill_visuals+=1
			return true
		return _need_resync("pvp_skill_visual_sequence_conflict")
	pvp_skill_visual_sequences[attacker]={"connection_epoch":ac,"sequence":seq}
	pvp_skill_damage_sequences[victim]={"connection_epoch":vc,"sequence":dmgseq}
	pvp_skill_visual_events.append(value.duplicate(true))
	var cutoff:=maxi(int(value.server_tick),last_aoi_tick)-PLAYER_COMBAT_VISUAL_EVENT_TTL_TICKS
	while not pvp_skill_visual_events.is_empty() and int(pvp_skill_visual_events[0].get("server_tick",-1))<cutoff:
		pvp_skill_visual_events.pop_front()
	while pvp_skill_visual_events.size()>PLAYER_COMBAT_VISUAL_EVENT_MAX:
		pvp_skill_visual_events.pop_front()
	last_aoi_tick=maxi(last_aoi_tick,int(value.server_tick))
	return true


func _clear_trade_state(reset_sequence: bool = false) -> void:
	if reset_sequence:
		trade_sequence = 0
	trade_id = 0
	trade_revision = 0
	trade_state = 0
	trade_peer_actor_id = 0
	trade_my_money = 0
	trade_other_money = 0
	trade_my_locked = false
	trade_other_locked = false
	trade_my_confirmed = false
	trade_other_confirmed = false
	trade_my_items.clear()
	trade_other_items.clear()


func _trade_item_matches_inventory(item: Dictionary) -> bool:
	var uid := int(item.item_uid)
	if not inventory_items.has(uid):
		return false
	var owned: Dictionary = inventory_items[uid]
	return int(owned.get("item_definition_id",0)) == int(item.item_definition_id) and int(owned.get("quantity",0)) == int(item.quantity) and int(owned.get("drop_id",0)) == int(item.drop_id) and int(owned.get("enhancement_level",0)) == int(item.get("enhancement_level",0))


func _apply_trade_snapshot(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("trade_snapshot_out_of_state")
	var value := WireCodec.decode_trade_snapshot(frame)
	if value.is_empty():
		return fail("invalid_trade_snapshot")
	var epoch := int(value.map_epoch)
	if epoch < map_epoch:
		stale_trade_frames += 1
		return true
	if epoch > map_epoch:
		return fail("future_trade_snapshot")
	var incoming_id := int(value.trade_id)
	var revision := int(value.revision)
	if trade_id == 0:
		if int(value.state) not in [1,2]:
			return fail("trade_snapshot_started_without_invite")
	elif incoming_id != trade_id:
		return fail("trade_snapshot_wrong_session")
	elif revision <= trade_revision:
		stale_trade_frames += 1
		return true
	if int(value.peer_actor_id) <= 0 or int(value.peer_actor_id) == actor_id:
		return fail("trade_snapshot_invalid_peer")
	if trade_id != 0 and trade_peer_actor_id != int(value.peer_actor_id):
		return fail("trade_snapshot_peer_changed")
	var my_next := {}
	var other_next := {}
	for raw in value.my_items:
		var item: Dictionary = raw
		var uid := int(item.item_uid)
		if not _trade_item_matches_inventory(item):
			return fail("trade_snapshot_my_item_mismatch")
		my_next[uid] = item.duplicate(true)
	for raw in value.other_items:
		var item: Dictionary = raw
		var uid := int(item.item_uid)
		if inventory_items.has(uid) or storage_items.has(uid) or equipment_items.has(uid):
			return fail("trade_snapshot_other_uid_location_conflict")
		other_next[uid] = item.duplicate(true)
	trade_id = incoming_id
	trade_revision = revision
	trade_state = int(value.state)
	trade_peer_actor_id = int(value.peer_actor_id)
	trade_my_money = int(value.my_money)
	trade_other_money = int(value.other_money)
	trade_my_locked = bool(value.my_locked)
	trade_other_locked = bool(value.other_locked)
	trade_my_confirmed = bool(value.my_confirmed)
	trade_other_confirmed = bool(value.other_confirmed)
	trade_my_items = my_next
	trade_other_items = other_next
	last_trade_terminal.clear()
	return true


func _apply_trade_terminal(frame: PackedByteArray) -> bool:
	if state != STATE_IN_WORLD:
		return fail("trade_terminal_out_of_state")
	var terminal := WireCodec.decode_trade_terminal(frame)
	if terminal.is_empty():
		return fail("invalid_trade_terminal")
	var epoch := int(terminal.map_epoch)
	if epoch < map_epoch:
		stale_trade_frames += 1
		return true
	if epoch > map_epoch:
		return fail("future_trade_terminal")
	if trade_id == 0:
		stale_trade_frames += 1
		return true
	if int(terminal.trade_id) != trade_id:
		return fail("trade_terminal_wrong_session")
	var revision := int(terminal.revision)
	if revision < trade_revision:
		stale_trade_frames += 1
		return true
	var status := int(terminal.status)
	if status == 0:
		if revision != trade_revision or trade_state != 4 or not trade_my_locked or not trade_other_locked or not trade_my_confirmed or not trade_other_confirmed:
			return fail("trade_success_without_dual_confirmation")
		for raw_uid in trade_my_items.keys():
			var uid := int(raw_uid)
			var item: Dictionary = trade_my_items[raw_uid]
			if not _trade_item_matches_inventory(item):
				return fail("trade_success_my_item_changed")
		for raw_uid in trade_other_items.keys():
			var uid := int(raw_uid)
			if inventory_items.has(uid) or storage_items.has(uid) or equipment_items.has(uid):
				return fail("trade_success_other_uid_conflict")
		for raw_uid in trade_my_items.keys():
			inventory_items.erase(int(raw_uid))
		for raw_uid in trade_other_items.keys():
			var item: Dictionary = trade_other_items[raw_uid]
			var uid := int(raw_uid)
			inventory_items[uid] = {
				"item_uid":uid,
				"item_definition_id":int(item.item_definition_id),
				"quantity":int(item.quantity),
				"drop_id":int(item.drop_id),
				"enhancement_level":int(item.get("enhancement_level",0)),
				"durability_raw":int(item.get("durability_raw",-1))
			}
		inventory_revision += 1
		var wallet := int(terminal.wallet_money)
		if active_shop_npc != 0:
			shop_money = wallet
		if active_bank_npc != 0:
			bank_wallet_money = wallet
		if active_repair_npc != 0:
			repair_wallet_money = wallet
	elif revision < trade_revision:
		return fail("trade_terminal_revision_regressed")
	last_trade_terminal = terminal.duplicate(true)
	_clear_trade_state(false)
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
		var identity_generation:Dictionary=player_identities.get(actor_id,{})
		if not identity_generation.is_empty() and int(identity_generation.get("connection_epoch",0))!=int(value.connection_epoch):
			return _need_resync("self_state_identity_generation_mismatch")
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
