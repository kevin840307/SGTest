extends RefCounted

const VERSION := 1
const HEADER_BYTES := 8
const MAX_FRAME_BYTES := 65536
const OPCODE_MOVE_TARGET := 1
const OPCODE_MOVE_RECEIPT := 2
const OPCODE_NPC_INTERACT := 3
const OPCODE_NPC_INTERACT_RESULT := 4
const OPCODE_NORMAL_ATTACK := 5
const OPCODE_NORMAL_ATTACK_RESULT := 6
const OPCODE_PLAYER_COMBAT_STATE := 7
const OPCODE_PLAYER_DAMAGE := 8
const OPCODE_AUTH_REQUEST := 10
const OPCODE_AUTH_RESULT := 11
const OPCODE_CHARACTER_LIST := 12
const OPCODE_CHARACTER_CHOOSE := 13
const OPCODE_CHARACTER_CHOOSE_RESULT := 14
const OPCODE_CHARACTER_CREATE := 15
const OPCODE_CHARACTER_CREATE_RESULT := 16
const OPCODE_WORLD_READY := 20
const OPCODE_ENTITY_SPAWN := 21
const OPCODE_ENTITY_DESPAWN := 22
const OPCODE_ENTITY_DELTA := 23
const OPCODE_SELF_STATE := 24
const OPCODE_WORLD_READY_ACK := 25
const OPCODE_WORLD_RESYNC_REQUEST := 26
const OPCODE_MONSTER_SPAWN := 27
const OPCODE_MONSTER_DELTA := 28
const OPCODE_MONSTER_DESPAWN := 29
const OPCODE_PLAYER_RESPAWN := 30
const OPCODE_PLAYER_RESPAWN_RESULT := 31
const OPCODE_GROUND_DROP_SPAWN := 32
const OPCODE_GROUND_DROP_DESPAWN := 33
const OPCODE_GROUND_PICKUP := 34
const OPCODE_GROUND_PICKUP_RESULT := 35
const OPCODE_INVENTORY_SNAPSHOT := 36
const OPCODE_RECOVERY_USE := 37
const OPCODE_RECOVERY_USE_RESULT := 38
const OPCODE_INVENTORY_DISCARD := 39
const OPCODE_INVENTORY_DISCARD_RESULT := 40
const OPCODE_PLAYER_ATTACK_VISUAL := 41
const OPCODE_PLAYER_DAMAGE_VISUAL := 42
const OPCODE_PLAYER_RESPAWN_VISUAL := 43
const OPCODE_SKILL_USE := 44
const OPCODE_SKILL_USE_RESULT := 45
const OPCODE_PLAYER_SKILL_VISUAL := 46
const OPCODE_SHOP_OPEN := 47
const OPCODE_SHOP_SNAPSHOT := 48
const OPCODE_SHOP_BUY := 49
const OPCODE_SHOP_SELL := 50
const OPCODE_SHOP_TRANSACTION_RESULT := 51
const OPCODE_STORAGE_OPEN := 52
const OPCODE_STORAGE_SNAPSHOT := 53
const OPCODE_STORAGE_DEPOSIT := 54
const OPCODE_STORAGE_WITHDRAW := 55
const OPCODE_STORAGE_TRANSFER_RESULT := 56
const OPCODE_EQUIPMENT_SNAPSHOT := 57
const OPCODE_EQUIPMENT_EQUIP := 58
const OPCODE_EQUIPMENT_UNEQUIP := 59
const OPCODE_EQUIPMENT_TRANSFER_RESULT := 60
const OPCODE_BANK_OPEN := 61
const OPCODE_BANK_SNAPSHOT := 62
const OPCODE_BANK_DEPOSIT := 63
const OPCODE_BANK_WITHDRAW := 64
const OPCODE_BANK_TRANSACTION_RESULT := 65
const OPCODE_REPAIR_OPEN := 66
const OPCODE_REPAIR_SNAPSHOT := 67
const OPCODE_REPAIR_ITEM := 68
const OPCODE_REPAIR_RESULT := 69
const OPCODE_TRADE_COMMAND := 70
const OPCODE_TRADE_SNAPSHOT := 71
const OPCODE_TRADE_TERMINAL := 72
const OPCODE_QUEST_OPEN := 73
const OPCODE_QUEST_VIEW := 74
const OPCODE_QUEST_CHOOSE := 75
const OPCODE_PVP_MODE_COMMAND := 76
const OPCODE_PVP_MODE_RESULT := 77
const OPCODE_PVP_ATTACK := 78
const OPCODE_PVP_ATTACK_RESULT := 79
const OPCODE_PVP_DAMAGE := 80
const OPCODE_PLAYER_IDENTITY := 81
const OPCODE_PVP_COMBAT_VISUAL := 82
const OPCODE_PVP_SKILL := 83
const OPCODE_PVP_SKILL_RESULT := 84
const OPCODE_PVP_SKILL_VISUAL := 85
const OPCODE_GM_CAPABILITY := 86
const OPCODE_GM_PVP_NAME_COMMAND := 87
const OPCODE_GM_PVP_NAME_RESULT := 88
const OPCODE_PLAYER_SHOP_COMMAND := 89
const OPCODE_PLAYER_SHOP_RESULT := 90
const OPCODE_PLAYER_SHOP_SOLD := 91
const OPCODE_CHAT_COMMAND := 92
const OPCODE_CHAT_DELIVERY := 93
const OPCODE_ENHANCEMENT_OPEN := 94
const OPCODE_ENHANCEMENT_SNAPSHOT := 95
const OPCODE_ENHANCEMENT_ITEM := 96
const OPCODE_ENHANCEMENT_RESULT := 97
const MOVE_TARGET_FRAME_BYTES := 28
const MOVE_RECEIPT_FRAME_BYTES := 30
const NPC_INTERACT_FRAME_BYTES := 24
const NPC_INTERACT_RESULT_FRAME_BYTES := 29
const NORMAL_ATTACK_FRAME_BYTES := 28
const NORMAL_ATTACK_RESULT_FRAME_BYTES := 57
const SKILL_USE_FRAME_BYTES := 32
const SKILL_USE_RESULT_FRAME_BYTES := 61
const PLAYER_SKILL_VISUAL_FRAME_BYTES := 70
const PLAYER_COMBAT_STATE_FRAME_BYTES := 32
const PLAYER_DAMAGE_FRAME_BYTES := 57
const PLAYER_RESPAWN_FRAME_BYTES := 20
const PLAYER_RESPAWN_RESULT_FRAME_BYTES := 29
const AUTH_RESULT_FRAME_BYTES := 9
const CHARACTER_CHOOSE_FRAME_BYTES := 16
const CHARACTER_CHOOSE_RESULT_FRAME_BYTES := 45
const MAX_CHARACTER_CREATE_FRAME_BYTES := 107
const MAX_CHARACTER_CREATE_RESULT_FRAME_BYTES := 116
const MAX_AUTH_REQUEST_FRAME_BYTES := 300
const MAX_CHARACTER_LIST_FRAME_BYTES := 4096
const WORLD_READY_FRAME_BYTES := 45
const ENTITY_STATE_FRAME_BYTES := 74
const ENTITY_DESPAWN_FRAME_BYTES := 40
const WORLD_READY_ACK_FRAME_BYTES := 24
const WORLD_RESYNC_REQUEST_FRAME_BYTES := 16
const MONSTER_STATE_FRAME_BYTES := 78
const MONSTER_DESPAWN_FRAME_BYTES := 40
const GROUND_DROP_SPAWN_FRAME_BYTES := 52
const GROUND_DROP_DESPAWN_FRAME_BYTES := 32
const GROUND_PICKUP_FRAME_BYTES := 28
const GROUND_PICKUP_RESULT_FRAME_BYTES := 45
const RECOVERY_USE_FRAME_BYTES := 28
const RECOVERY_USE_RESULT_FRAME_BYTES := 45
const INVENTORY_DISCARD_FRAME_BYTES := 32
const INVENTORY_DISCARD_RESULT_FRAME_BYTES := 41
const PLAYER_ATTACK_VISUAL_FRAME_BYTES := 66
const PLAYER_DAMAGE_VISUAL_FRAME_BYTES := 66
const PLAYER_RESPAWN_VISUAL_FRAME_BYTES := 45
const INVENTORY_SNAPSHOT_HEADER_BYTES := 23
const INVENTORY_ITEM_BYTES := 24
const INVENTORY_SNAPSHOT_MAX_PER_FRAME := 128
const INVENTORY_SNAPSHOT_MAX_ITEMS := 4095
const SHOP_OPEN_FRAME_BYTES := 24
const SHOP_BUY_FRAME_BYTES := 32
const SHOP_SELL_FRAME_BYTES := 40
const SHOP_TRANSACTION_RESULT_FRAME_BYTES := 54
const SHOP_SNAPSHOT_HEADER_BYTES := 35
const SHOP_ITEM_BYTES := 20
const SHOP_SNAPSHOT_MAX_ITEMS := 256
const STORAGE_OPEN_FRAME_BYTES := 24
const STORAGE_MOVE_FRAME_BYTES := 32
const STORAGE_SNAPSHOT_HEADER_BYTES := 32
const STORAGE_ITEM_BYTES := 24
const STORAGE_SNAPSHOT_MAX_PER_FRAME := 128
const STORAGE_SNAPSHOT_MAX_ITEMS := 4095
const STORAGE_TRANSFER_RESULT_FRAME_BYTES := 50
const EQUIPMENT_SNAPSHOT_HEADER_BYTES := 23
const EQUIPMENT_ITEM_BYTES := 24
const EQUIPMENT_SNAPSHOT_MAX_PER_FRAME := 128
const EQUIPMENT_SNAPSHOT_MAX_ITEMS := 255
const EQUIPMENT_MOVE_FRAME_BYTES := 28
const EQUIPMENT_TRANSFER_RESULT_FRAME_BYTES := 46
const BANK_OPEN_FRAME_BYTES := 24
const BANK_SNAPSHOT_FRAME_BYTES := 42
const BANK_MOVE_FRAME_BYTES := 32
const BANK_TRANSACTION_RESULT_FRAME_BYTES := 51
const REPAIR_OPEN_FRAME_BYTES := 24
const REPAIR_SNAPSHOT_HEADER_BYTES := 40
const REPAIR_PREVIEW_ITEM_BYTES := 29
const REPAIR_SNAPSHOT_MAX_PER_FRAME := 128
const REPAIR_SNAPSHOT_MAX_ITEMS := 4095
const REPAIR_ITEM_FRAME_BYTES := 32
const REPAIR_RESULT_FRAME_BYTES := 62
const ENHANCEMENT_OPEN_FRAME_BYTES := 24
const ENHANCEMENT_SNAPSHOT_HEADER_BYTES := 40
const ENHANCEMENT_PREVIEW_ITEM_BYTES := 28
const ENHANCEMENT_SNAPSHOT_MAX_PER_FRAME := 128
const ENHANCEMENT_SNAPSHOT_MAX_ITEMS := 4095
const ENHANCEMENT_ITEM_FRAME_BYTES := 32
const ENHANCEMENT_RESULT_FRAME_BYTES := 70
const TRADE_COMMAND_FRAME_BYTES := 53
const TRADE_SNAPSHOT_HEADER_BYTES := 59
const TRADE_OFFER_ITEM_BYTES := 24
const TRADE_MAX_ITEMS_PER_SIDE := 32
const TRADE_TERMINAL_FRAME_BYTES := 37
const QUEST_OPEN_FRAME_BYTES := 24
const QUEST_CHOOSE_FRAME_BYTES := 32
const QUEST_VIEW_HEADER_BYTES := 47
const QUEST_VIEW_MAX_TITLE_BYTES := 256
const QUEST_VIEW_MAX_TEXT_BYTES := 4096
const QUEST_VIEW_MAX_LABEL_BYTES := 512
const QUEST_VIEW_MAX_CHOICES := 16
const QUEST_VIEW_MAX_FRAME_BYTES := QUEST_VIEW_HEADER_BYTES + QUEST_VIEW_MAX_TITLE_BYTES + QUEST_VIEW_MAX_TEXT_BYTES + QUEST_VIEW_MAX_CHOICES * (6 + QUEST_VIEW_MAX_LABEL_BYTES)
const PVP_MODE_COMMAND_FRAME_BYTES := 21
const PVP_MODE_RESULT_FRAME_BYTES := 23
const PVP_ATTACK_FRAME_BYTES := 28
const PVP_ATTACK_RESULT_FRAME_BYTES := 42
const PVP_DAMAGE_FRAME_BYTES := 61
const PLAYER_IDENTITY_HEADER_BYTES := 68
const PLAYER_IDENTITY_MAX_NAME_BYTES := 96
const PLAYER_IDENTITY_MAX_FRAME_BYTES := PLAYER_IDENTITY_HEADER_BYTES + PLAYER_IDENTITY_MAX_NAME_BYTES
const PVP_COMBAT_VISUAL_FRAME_BYTES := 71
const PVP_SKILL_FRAME_BYTES := 32
const PVP_SKILL_RESULT_FRAME_BYTES := 46
const PVP_SKILL_VISUAL_FRAME_BYTES := 75
const GM_CAPABILITY_FRAME_BYTES := 9
const GM_PVP_NAME_COMMAND_FRAME_BYTES := 29
const GM_PVP_NAME_RESULT_FRAME_BYTES := 30
const PLAYER_SHOP_COMMAND_FRAME_BYTES := 53
const PLAYER_SHOP_RESULT_FRAME_BYTES := 90
const PLAYER_SHOP_SOLD_FRAME_BYTES := 48
const CHAT_COMMAND_HEADER_BYTES := 24
const CHAT_DELIVERY_HEADER_BYTES := 42
const CHAT_MAX_MESSAGE_BYTES := 256
const CHAT_MAX_SENDER_NAME_BYTES := 96
const CHAT_COMMAND_MAX_FRAME_BYTES := CHAT_COMMAND_HEADER_BYTES + CHAT_MAX_MESSAGE_BYTES
const CHAT_DELIVERY_MAX_FRAME_BYTES := CHAT_DELIVERY_HEADER_BYTES + CHAT_MAX_SENDER_NAME_BYTES + CHAT_MAX_MESSAGE_BYTES
const MAX_MONEY := 9000000000000000

static func encode_move_target(map_epoch: int, movement_sequence: int, target_x: int, target_y: int) -> PackedByteArray:
	if map_epoch <= 0 or movement_sequence <= 0 or movement_sequence > 0xffffffff:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(MOVE_TARGET_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_MOVE_TARGET)
	writer.put_64(map_epoch)
	writer.put_u32(movement_sequence)
	writer.put_32(target_x)
	writer.put_32(target_y)
	return writer.data_array

static func decode_move_target(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_MOVE_TARGET, MOVE_TARGET_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var map_epoch := reader.get_64()
	var sequence := reader.get_u32()
	var target_x := reader.get_32()
	var target_y := reader.get_32()
	if map_epoch <= 0 or sequence == 0:
		return {}
	return {"map_epoch": map_epoch, "movement_sequence": sequence, "target_x": target_x, "target_y": target_y}

static func decode_move_receipt(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_MOVE_RECEIPT, MOVE_RECEIPT_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var map_epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u16()
	var server_tick := reader.get_64()
	if map_epoch <= 0 or sequence == 0 or status > 11 or server_tick < 0:
		return {}
	return {"map_epoch": map_epoch, "movement_sequence": sequence, "status": status, "server_tick": server_tick}

static func encode_npc_interact(map_epoch: int, interaction_sequence: int, npc_spawn_id: int) -> PackedByteArray:
	if map_epoch <= 0 or interaction_sequence <= 0 or interaction_sequence > 0xffffffff or npc_spawn_id <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(NPC_INTERACT_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_NPC_INTERACT)
	writer.put_64(map_epoch)
	writer.put_u32(interaction_sequence)
	writer.put_32(npc_spawn_id)
	return writer.data_array

static func decode_npc_interact(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_NPC_INTERACT, NPC_INTERACT_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var spawn_id := reader.get_32()
	if epoch <= 0 or sequence == 0 or spawn_id <= 0:
		return {}
	return {"map_epoch": epoch, "interaction_sequence": sequence, "npc_spawn_id": spawn_id}

static func decode_npc_interact_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_NPC_INTERACT_RESULT, NPC_INTERACT_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var result_epoch := reader.get_64()
	if epoch <= 0 or sequence == 0 or status > 6:
		return {}
	if status == 0 and result_epoch <= epoch:
		return {}
	if status != 0 and result_epoch != epoch:
		return {}
	return {"request_map_epoch": epoch, "interaction_sequence": sequence, "status": status, "result_map_epoch": result_epoch}

static func encode_normal_attack(map_epoch: int, attack_sequence: int, target_entity_id: int) -> PackedByteArray:
	if map_epoch <= 0 or attack_sequence <= 0 or attack_sequence > 0xffffffff or target_entity_id <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(NORMAL_ATTACK_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_NORMAL_ATTACK)
	writer.put_64(map_epoch)
	writer.put_u32(attack_sequence)
	writer.put_64(target_entity_id)
	return writer.data_array

static func decode_normal_attack(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_NORMAL_ATTACK, NORMAL_ATTACK_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var target := reader.get_64()
	if epoch <= 0 or sequence == 0 or target <= 0:
		return {}
	return {"map_epoch":epoch,"attack_sequence":sequence,"target_entity_id":target}

static func decode_normal_attack_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_NORMAL_ATTACK_RESULT, NORMAL_ATTACK_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var tick := reader.get_64()
	var target := reader.get_64()
	var damage := reader.get_32()
	var current_hp := reader.get_32()
	var max_hp := reader.get_32()
	var revision := reader.get_64()
	if epoch <= 0 or sequence == 0 or status > 9 or tick < 0 or target <= 0 or damage < 0 or current_hp < 0 or max_hp < 0 or revision < 0:
		return {}
	if max_hp > 0 and current_hp > max_hp:
		return {}
	if status == 0 and (damage <= 0 or max_hp <= 0 or revision <= 0):
		return {}
	if status == 1 and (damage != 0 or max_hp <= 0 or revision <= 0):
		return {}
	if status > 1 and damage != 0:
		return {}
	return {"map_epoch":epoch,"attack_sequence":sequence,"status":status,"server_tick":tick,
		"target_entity_id":target,"damage":damage,"current_hp":current_hp,
		"max_hp":max_hp,"target_revision":revision}

static func encode_skill_use(map_epoch: int, skill_sequence: int, skill_id: int, target_entity_id: int) -> PackedByteArray:
	if map_epoch <= 0 or skill_sequence <= 0 or skill_sequence > 0xffffffff or skill_id <= 0 or skill_id > 100000000 or target_entity_id < 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(SKILL_USE_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_SKILL_USE)
	writer.put_64(map_epoch)
	writer.put_u32(skill_sequence)
	writer.put_32(skill_id)
	writer.put_64(target_entity_id)
	return writer.data_array

static func decode_skill_use(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_SKILL_USE,SKILL_USE_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var skill_id := reader.get_32()
	var target := reader.get_64()
	if epoch <= 0 or sequence == 0 or skill_id <= 0 or skill_id > 100000000 or target < 0:
		return {}
	return {"map_epoch":epoch,"skill_sequence":sequence,"skill_id":skill_id,"target_entity_id":target}

static func decode_skill_use_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_SKILL_USE_RESULT,SKILL_USE_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var tick := reader.get_64()
	var skill_id := reader.get_32()
	var target := reader.get_64()
	var damage := reader.get_32()
	var current_hp := reader.get_32()
	var max_hp := reader.get_32()
	var revision := reader.get_64()
	if epoch <= 0 or sequence == 0 or status > 12 or tick < 0 or skill_id <= 0 or skill_id > 100000000 or target < 0 or damage < 0 or current_hp < 0 or max_hp < 0 or revision < 0:
		return {}
	if max_hp > 0 and current_hp > max_hp:
		return {}
	if status == 0:
		if target == 0:
			if damage != 0 or current_hp != 0 or max_hp != 0 or revision != 0:
				return {}
		elif damage <= 0 or max_hp <= 0 or revision <= 0:
			return {}
	elif status == 1:
		if target <= 0 or damage != 0 or max_hp <= 0 or revision <= 0:
			return {}
	elif damage != 0:
		return {}
	return {"map_epoch":epoch,"skill_sequence":sequence,"status":status,
		"server_tick":tick,"skill_id":skill_id,"target_entity_id":target,
		"damage":damage,"current_hp":current_hp,"max_hp":max_hp,
		"target_revision":revision}

static func decode_player_skill_visual(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_PLAYER_SKILL_VISUAL,PLAYER_SKILL_VISUAL_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var tick := reader.get_64()
	var attacker := reader.get_64()
	var connection := reader.get_64()
	var sequence := reader.get_u32()
	var skill_id := reader.get_32()
	var target := reader.get_64()
	var status := reader.get_u8()
	var damage := reader.get_32()
	var revision := reader.get_64()
	var skill_facing := reader.get_u8()
	if epoch <= 0 or tick < 0 or attacker <= 0 or connection <= 0 or sequence == 0 or skill_id <= 0 or skill_id > 100000000 or target <= 0 or revision <= 0 or skill_facing < 1 or skill_facing > 8:
		return {}
	if status == 0 and damage <= 0:
		return {}
	if status == 1 and damage != 0:
		return {}
	if status > 1:
		return {}
	return {"receiver_map_epoch":epoch,"server_tick":tick,
		"attacker_actor_id":attacker,"attacker_connection_epoch":connection,
		"skill_sequence":sequence,"skill_id":skill_id,"target_entity_id":target,
		"status":status,"damage":damage,"target_revision":revision,
		"facing":skill_facing}

static func decode_player_attack_visual(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_PLAYER_ATTACK_VISUAL,PLAYER_ATTACK_VISUAL_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var tick := reader.get_64()
	var attacker := reader.get_64()
	var connection := reader.get_64()
	var sequence := reader.get_u32()
	var target := reader.get_64()
	var status := reader.get_u8()
	var damage := reader.get_32()
	var revision := reader.get_64()
	var attack_facing := reader.get_u8()
	if epoch <= 0 or tick < 0 or attacker <= 0 or connection <= 0 or sequence == 0 or target <= 0 or revision <= 0 or attack_facing < 1 or attack_facing > 8 or status > 1:
		return {}
	if status == 0 and damage <= 0:
		return {}
	if status == 1 and damage != 0:
		return {}
	return {"receiver_map_epoch":epoch,"server_tick":tick,
		"attacker_actor_id":attacker,"attacker_connection_epoch":connection,
		"attack_sequence":sequence,"target_entity_id":target,"status":status,
		"damage":damage,"target_revision":revision,"facing":attack_facing}


static func decode_player_damage_visual(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_PLAYER_DAMAGE_VISUAL,PLAYER_DAMAGE_VISUAL_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var tick := reader.get_64()
	var victim := reader.get_64()
	var connection := reader.get_64()
	var sequence := reader.get_u32()
	var attacker := reader.get_64()
	var attacker_revision := reader.get_64()
	var damage := reader.get_32()
	var died := reader.get_u8()
	var hit_facing := reader.get_u8()
	if epoch <= 0 or tick < 0 or victim <= 0 or connection <= 0 or sequence == 0 or attacker <= 0 or attacker_revision <= 0 or damage <= 0 or died > 1 or hit_facing < 1 or hit_facing > 8:
		return {}
	return {"receiver_map_epoch":epoch,"server_tick":tick,
		"victim_actor_id":victim,"victim_connection_epoch":connection,
		"damage_sequence":sequence,"attacker_monster_entity_id":attacker,
		"attacker_monster_revision":attacker_revision,"damage":damage,
		"died":died==1,"facing":hit_facing}


static func decode_player_respawn_visual(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_PLAYER_RESPAWN_VISUAL,PLAYER_RESPAWN_VISUAL_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var tick := reader.get_64()
	var actor := reader.get_64()
	var connection := reader.get_64()
	var sequence := reader.get_u32()
	var respawn_facing := reader.get_u8()
	if epoch <= 0 or tick < 0 or actor <= 0 or connection <= 0 or sequence == 0 or respawn_facing < 1 or respawn_facing > 8:
		return {}
	return {"receiver_map_epoch":epoch,"server_tick":tick,
		"actor_id":actor,"connection_epoch":connection,
		"respawn_sequence":sequence,"facing":respawn_facing}


static func decode_player_combat_state(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_PLAYER_COMBAT_STATE, PLAYER_COMBAT_STATE_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var current_hp := reader.get_32()
	var max_hp := reader.get_32()
	var current_mp := reader.get_32()
	var max_mp := reader.get_32()
	if epoch <= 0 or current_hp < 0 or max_hp <= 0 or current_hp > max_hp or current_mp < 0 or max_mp < 0 or current_mp > max_mp:
		return {}
	return {"map_epoch":epoch,"current_hp":current_hp,"max_hp":max_hp,
		"current_mp":current_mp,"max_mp":max_mp}

static func decode_player_damage(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_PLAYER_DAMAGE, PLAYER_DAMAGE_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var tick := reader.get_64()
	var attacker := reader.get_64()
	var attacker_revision := reader.get_64()
	var damage := reader.get_32()
	var current_hp := reader.get_32()
	var max_hp := reader.get_32()
	var died := reader.get_u8()
	if epoch <= 0 or sequence == 0 or tick < 0 or attacker <= 0 or attacker_revision <= 0 or damage <= 0 or current_hp < 0 or max_hp <= 0 or current_hp > max_hp or died > 1:
		return {}
	if (died == 1) != (current_hp == 0):
		return {}
	return {"map_epoch":epoch,"damage_sequence":sequence,"server_tick":tick,
		"attacker_monster_entity_id":attacker,"attacker_monster_revision":attacker_revision,
		"damage":damage,"current_hp":current_hp,"max_hp":max_hp,"died":died==1}

static func encode_player_respawn(map_epoch: int, respawn_sequence: int) -> PackedByteArray:
	if map_epoch <= 0 or respawn_sequence <= 0 or respawn_sequence > 0xffffffff:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(PLAYER_RESPAWN_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_PLAYER_RESPAWN)
	writer.put_64(map_epoch)
	writer.put_u32(respawn_sequence)
	return writer.data_array

static func decode_player_respawn_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_PLAYER_RESPAWN_RESULT, PLAYER_RESPAWN_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var current_hp := reader.get_32()
	var max_hp := reader.get_32()
	if epoch <= 0 or sequence == 0 or status > 4 or current_hp < 0 or max_hp < 0 or current_hp > max_hp:
		return {}
	if status == 0 and (max_hp <= 0 or current_hp != max_hp):
		return {}
	if status == 1 and (max_hp <= 0 or current_hp <= 0):
		return {}
	return {
		"map_epoch":epoch,
		"respawn_sequence":sequence,
		"status":status,
		"current_hp":current_hp,
		"max_hp":max_hp
	}

static func encode_auth_request(username: String, password: String) -> PackedByteArray:
	var username_bytes := username.to_utf8_buffer()
	var password_bytes := password.to_utf8_buffer()
	if username_bytes.size() < 3 or username_bytes.size() > 32 or password_bytes.size() < 8 or password_bytes.size() > 256:
		return PackedByteArray()
	var frame_bytes := HEADER_BYTES + 4 + username_bytes.size() + password_bytes.size()
	if frame_bytes > MAX_AUTH_REQUEST_FRAME_BYTES:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(frame_bytes)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_AUTH_REQUEST)
	writer.put_u16(username_bytes.size())
	writer.put_u16(password_bytes.size())
	writer.put_data(username_bytes)
	writer.put_data(password_bytes)
	return writer.data_array

static func decode_auth_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_AUTH_RESULT, AUTH_RESULT_FRAME_BYTES):
		return {}
	var status := frame[8]
	if status > 2:
		return {}
	return {"status": status}

static func decode_character_list(frame: PackedByteArray) -> Dictionary:
	if not _valid_variable_header(frame, OPCODE_CHARACTER_LIST, 9, MAX_CHARACTER_LIST_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var count := reader.get_u8()
	if count > 32:
		return {}
	var characters: Array[Dictionary] = []
	var ids := {}
	var slots := {}
	for _i in range(count):
		if reader.get_available_bytes() < 11:
			return {}
		var character_id := reader.get_64()
		var slot := reader.get_u8()
		var name_bytes_count := reader.get_u16()
		if character_id <= 0 or slot > 31 or name_bytes_count < 1 or name_bytes_count > 96 or reader.get_available_bytes() < name_bytes_count:
			return {}
		if ids.has(character_id) or slots.has(slot):
			return {}
		var result := reader.get_data(name_bytes_count)
		if result[0] != OK:
			return {}
		var name_bytes: PackedByteArray = result[1]
		var name := name_bytes.get_string_from_utf8()
		if name.to_utf8_buffer() != name_bytes:
			return {}
		ids[character_id] = true
		slots[slot] = true
		characters.append({"character_id": character_id, "slot": slot, "name": name})
	if reader.get_available_bytes() != 0:
		return {}
	return {"characters": characters}


static func encode_character_create(slot: int, character_name: String) -> PackedByteArray:
	var name_bytes := character_name.to_utf8_buffer()
	if slot < 0 or slot > 31 or name_bytes.size() < 1 or name_bytes.size() > 96:
		return PackedByteArray()
	var frame_bytes := HEADER_BYTES + 3 + name_bytes.size()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(frame_bytes)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_CHARACTER_CREATE)
	writer.put_u8(slot)
	writer.put_u16(name_bytes.size())
	writer.put_data(name_bytes)
	return writer.data_array

static func decode_character_create_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_variable_header(
		frame, OPCODE_CHARACTER_CREATE_RESULT, 20, MAX_CHARACTER_CREATE_RESULT_FRAME_BYTES
	):
		return {}
	var reader := _reader(frame, 8)
	var status := reader.get_u8()
	var character_id := reader.get_64()
	var slot := reader.get_u8()
	var name_bytes_count := reader.get_u16()
	if status > 6 or slot > 31 or name_bytes_count > 96 or reader.get_available_bytes() != name_bytes_count:
		return {}
	var character_name := ""
	if name_bytes_count > 0:
		var result := reader.get_data(name_bytes_count)
		if result[0] != OK:
			return {}
		var name_bytes: PackedByteArray = result[1]
		character_name = name_bytes.get_string_from_utf8()
		if character_name.to_utf8_buffer() != name_bytes:
			return {}
	if status == 0:
		if character_id <= 0 or name_bytes_count < 1:
			return {}
	elif character_id != 0 or name_bytes_count != 0:
		return {}
	return {
		"status": status,
		"character_id": character_id,
		"slot": slot,
		"name": character_name,
	}

static func encode_character_choose(character_id: int) -> PackedByteArray:
	if character_id <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(CHARACTER_CHOOSE_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_CHARACTER_CHOOSE)
	writer.put_64(character_id)
	return writer.data_array

static func decode_character_choose_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_CHARACTER_CHOOSE_RESULT, CHARACTER_CHOOSE_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var status := reader.get_u8()
	var character_id := reader.get_64()
	var map_id := reader.get_32()
	var x := reader.get_double()
	var y := reader.get_double()
	var map_epoch := reader.get_64()
	if status > 2:
		return {}
	if status == 0:
		if character_id <= 0 or map_id <= 0 or map_epoch <= 0 or not is_finite(x) or not is_finite(y):
			return {}
	elif character_id != 0 or map_id != 0 or map_epoch != 0 or x != 0.0 or y != 0.0:
		return {}
	return {"status": status, "character_id": character_id, "map_id": map_id, "x": x, "y": y, "map_epoch": map_epoch}

static func encode_world_ready_ack(map_epoch: int, server_tick: int) -> PackedByteArray:
	if map_epoch <= 0 or server_tick < 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(WORLD_READY_ACK_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_WORLD_READY_ACK)
	writer.put_64(map_epoch)
	writer.put_64(server_tick)
	return writer.data_array

static func decode_world_ready_ack(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_WORLD_READY_ACK, WORLD_READY_ACK_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var tick := reader.get_64()
	return {"map_epoch":epoch,"server_tick":tick} if epoch > 0 and tick >= 0 else {}

static func encode_world_resync_request(map_epoch: int) -> PackedByteArray:
	if map_epoch <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(WORLD_RESYNC_REQUEST_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_WORLD_RESYNC_REQUEST)
	writer.put_64(map_epoch)
	return writer.data_array

static func decode_world_resync_request(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_WORLD_RESYNC_REQUEST, WORLD_RESYNC_REQUEST_FRAME_BYTES):
		return {}
	var epoch := _reader(frame,8).get_64()
	return {"map_epoch":epoch} if epoch > 0 else {}

static func decode_world_ready(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_WORLD_READY, WORLD_READY_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var epoch := reader.get_64()
	var tick := reader.get_64()
	var actor_id := reader.get_64()
	var map_id := reader.get_32()
	var x := reader.get_float()
	var y := reader.get_float()
	var facing := reader.get_u8()
	if facing < 1 or facing > 8 or epoch <= 0 or tick < 0 or actor_id <= 0 or map_id <= 0 or not is_finite(x) or not is_finite(y) or x < 0 or y < 0:
		return {}
	return {"map_epoch": epoch, "server_tick": tick, "actor_id": actor_id, "map_id": map_id, "x": x, "y": y, "facing": facing}

static func decode_entity_state(frame: PackedByteArray) -> Dictionary:
	if frame_opcode(frame) not in [OPCODE_ENTITY_SPAWN, OPCODE_ENTITY_DELTA, OPCODE_SELF_STATE] or frame.size() != ENTITY_STATE_FRAME_BYTES:
		return {}
	var reader := _reader(frame, 8)
	var receiver_epoch := reader.get_64()
	var tick := reader.get_64()
	var actor_id := reader.get_64()
	var connection_epoch := reader.get_64()
	var entity_map_epoch := reader.get_64()
	var map_id := reader.get_32()
	var revision := reader.get_64()
	var x := reader.get_float()
	var y := reader.get_float()
	var moving := reader.get_u8()
	var action_sequence := reader.get_u32()
	var facing := reader.get_u8()
	if facing < 1 or facing > 8 or receiver_epoch <= 0 or tick < 0 or actor_id <= 0 or connection_epoch <= 0 or entity_map_epoch <= 0 or map_id <= 0 or revision <= 0 or moving > 1 or not is_finite(x) or not is_finite(y) or x < 0 or y < 0:
		return {}
	return {"receiver_map_epoch": receiver_epoch, "server_tick": tick, "actor_id": actor_id, "connection_epoch": connection_epoch, "entity_map_epoch": entity_map_epoch, "map_id": map_id, "revision": revision, "x": x, "y": y, "moving": moving == 1, "movement_action_sequence": action_sequence, "facing": facing}

static func decode_monster_state(frame: PackedByteArray) -> Dictionary:
	var opcode := frame_opcode(frame)
	if opcode not in [OPCODE_MONSTER_SPAWN, OPCODE_MONSTER_DELTA] or frame.size() != MONSTER_STATE_FRAME_BYTES:
		return {}
	var reader := _reader(frame,8)
	var receiver_epoch := reader.get_64()
	var tick := reader.get_64()
	var entity_id := reader.get_64()
	var enemy_id := reader.get_32()
	var source_index := reader.get_32()
	var map_id := reader.get_32()
	var x := reader.get_float()
	var y := reader.get_float()
	var revision := reader.get_64()
	var current_hp := reader.get_32()
	var max_hp := reader.get_32()
	var ai_state := reader.get_u8()
	var monster_facing := reader.get_u8()
	var target_actor_id := reader.get_64()
	if receiver_epoch <= 0 or tick < 0 or entity_id <= 0 or enemy_id <= 0 or source_index < 0 or map_id <= 0 or revision <= 0 or current_hp < 0 or max_hp <= 0 or current_hp > max_hp or ai_state > 4 or monster_facing < 1 or monster_facing > 8 or target_actor_id < 0 or not is_finite(x) or not is_finite(y) or x < 0 or y < 0:
		return {}
	return {"receiver_map_epoch":receiver_epoch,"server_tick":tick,"entity_id":entity_id,
		"enemy_id":enemy_id,"source_index":source_index,"map_id":map_id,"x":x,"y":y,
		"revision":revision,"current_hp":current_hp,"max_hp":max_hp,"state":ai_state,
		"facing":monster_facing,"target_actor_id":target_actor_id}

static func encode_recovery_use(epoch: int, sequence: int, uid: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or uid <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(RECOVERY_USE_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_RECOVERY_USE)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_64(uid)
	return writer.data_array


static func decode_recovery_use_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_RECOVERY_USE_RESULT,RECOVERY_USE_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var uid := reader.get_64()
	var item_id := reader.get_32()
	var quantity := reader.get_32()
	var hp := reader.get_32()
	var max_hp := reader.get_32()
	if epoch <= 0 or sequence == 0 or status > 10 or uid <= 0:
		return {}
	if status in [0,1]:
		if item_id < 1 or item_id > 100000000 or quantity < 0 or quantity > 1000000 or hp <= 0 or max_hp < hp:
			return {}
	elif item_id != 0 or quantity != 0 or hp != 0 or max_hp != 0:
		return {}
	return {"map_epoch":epoch,"use_sequence":sequence,"status":status,
		"item_uid":uid,"item_definition_id":item_id,
		"remaining_quantity":quantity,"current_hp":hp,"max_hp":max_hp}


static func encode_inventory_discard(epoch: int, sequence: int, uid: int, quantity: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or uid <= 0 or quantity < 1 or quantity > 1000000:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(INVENTORY_DISCARD_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_INVENTORY_DISCARD)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_64(uid)
	writer.put_32(quantity)
	return writer.data_array


static func decode_inventory_discard_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_INVENTORY_DISCARD_RESULT,INVENTORY_DISCARD_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var uid := reader.get_64()
	var item_id := reader.get_32()
	var discarded := reader.get_32()
	var remaining := reader.get_32()
	if epoch <= 0 or sequence == 0 or status > 6 or uid <= 0:
		return {}
	if status in [0,1]:
		if item_id < 1 or item_id > 100000000 or discarded < 1 or discarded > 1000000 or remaining < 0 or remaining > 1000000:
			return {}
	elif item_id != 0 or discarded != 0 or remaining != 0:
		return {}
	return {"map_epoch":epoch,"discard_sequence":sequence,"status":status,
		"item_uid":uid,"item_definition_id":item_id,
		"discarded_quantity":discarded,"remaining_quantity":remaining}


static func encode_ground_pickup(epoch: int, sequence: int, drop_id: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or drop_id <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(GROUND_PICKUP_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_GROUND_PICKUP)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_64(drop_id)
	return writer.data_array


static func decode_inventory_snapshot(frame: PackedByteArray) -> Dictionary:
	if not _valid_variable_header(frame,OPCODE_INVENTORY_SNAPSHOT,INVENTORY_SNAPSHOT_HEADER_BYTES,INVENTORY_SNAPSHOT_HEADER_BYTES+INVENTORY_SNAPSHOT_MAX_PER_FRAME*INVENTORY_ITEM_BYTES):
		return {}
	var reader := _reader(frame,8)
	var character := reader.get_64()
	var start := reader.get_32()
	var final_chunk := reader.get_u8()
	var count := reader.get_u16()
	if character <= 0 or start < 0 or start > INVENTORY_SNAPSHOT_MAX_ITEMS or final_chunk > 1 or count > INVENTORY_SNAPSHOT_MAX_PER_FRAME or start+count > INVENTORY_SNAPSHOT_MAX_ITEMS or frame.size() != INVENTORY_SNAPSHOT_HEADER_BYTES+count*INVENTORY_ITEM_BYTES:
		return {}
	var items: Array[Dictionary] = []
	var ids := {}
	for _index in count:
		var uid := reader.get_64()
		var item_id := reader.get_32()
		var quantity := reader.get_32()
		var drop_id := reader.get_64()
		if uid <= 0 or item_id < 1 or item_id > 100000000 or quantity < 1 or quantity > 1000000 or drop_id < 0 or ids.has(uid):
			return {}
		ids[uid] = true
		items.append({"item_uid":uid,"item_definition_id":item_id,
			"quantity":quantity,"drop_id":drop_id})
	return {"character_id":character,"start_index":start,
		"complete":final_chunk==1,"items":items}


static func decode_ground_pickup_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_GROUND_PICKUP_RESULT,GROUND_PICKUP_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var drop_id := reader.get_64()
	var uid := reader.get_64()
	var item_id := reader.get_32()
	var quantity := reader.get_32()
	if epoch <= 0 or sequence == 0 or status > 9 or drop_id <= 0 or uid < 0 or item_id < 0 or quantity < 0:
		return {}
	if status in [0,1]:
		if uid <= 0 or item_id < 1 or item_id > 100000000 or quantity < 1 or quantity > 1000000:
			return {}
	elif uid != 0 or item_id != 0 or quantity != 0:
		return {}
	return {"map_epoch":epoch,"pickup_sequence":sequence,
		"status":status,"drop_id":drop_id,"item_uid":uid,
		"item_definition_id":item_id,"quantity":quantity}




static func encode_shop_open(epoch: int, sequence: int, npc_spawn_id: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or npc_spawn_id <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(SHOP_OPEN_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_SHOP_OPEN)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	return writer.data_array


static func decode_shop_snapshot(frame: PackedByteArray) -> Dictionary:
	if not _valid_variable_header(frame,OPCODE_SHOP_SNAPSHOT,SHOP_SNAPSHOT_HEADER_BYTES,SHOP_SNAPSHOT_HEADER_BYTES+SHOP_SNAPSHOT_MAX_ITEMS*SHOP_ITEM_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var npc_spawn_id := reader.get_32()
	var money := reader.get_64()
	var count := reader.get_u16()
	if epoch <= 0 or sequence == 0 or status > 4 or npc_spawn_id <= 0 or money < 0 or count > SHOP_SNAPSHOT_MAX_ITEMS or frame.size() != SHOP_SNAPSHOT_HEADER_BYTES+count*SHOP_ITEM_BYTES:
		return {}
	if status == 0 and count == 0:
		return {}
	if status != 0 and (money != 0 or count != 0):
		return {}
	var items: Array[Dictionary] = []
	var ids := {}
	for _index in count:
		var item_id := reader.get_32()
		var buy_price := reader.get_64()
		var sell_price := reader.get_64()
		if item_id < 1 or item_id > 100000000 or buy_price < 1 or sell_price < 0 or sell_price > buy_price or ids.has(item_id):
			return {}
		ids[item_id] = true
		items.append({"item_definition_id":item_id,"buy_price":buy_price,"sell_price":sell_price})
	return {"map_epoch":epoch,"shop_sequence":sequence,"status":status,
		"npc_spawn_id":npc_spawn_id,"money":money,"items":items}


static func encode_shop_buy(epoch: int, sequence: int, npc_spawn_id: int, item_definition_id: int, quantity: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or npc_spawn_id <= 0 or item_definition_id < 1 or item_definition_id > 100000000 or quantity < 1 or quantity > 1000000:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(SHOP_BUY_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_SHOP_BUY)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	writer.put_32(item_definition_id)
	writer.put_32(quantity)
	return writer.data_array


static func encode_shop_sell(epoch: int, sequence: int, npc_spawn_id: int, item_uid: int, item_definition_id: int, quantity: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or npc_spawn_id <= 0 or item_uid <= 0 or item_definition_id < 1 or item_definition_id > 100000000 or quantity < 1 or quantity > 1000000:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(SHOP_SELL_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_SHOP_SELL)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	writer.put_64(item_uid)
	writer.put_32(item_definition_id)
	writer.put_32(quantity)
	return writer.data_array


static func decode_shop_transaction_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_SHOP_TRANSACTION_RESULT,SHOP_TRANSACTION_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var kind := reader.get_u8()
	var status := reader.get_u8()
	var npc_spawn_id := reader.get_32()
	var money := reader.get_64()
	var item_uid := reader.get_64()
	var item_id := reader.get_32()
	var quantity := reader.get_32()
	var remaining := reader.get_32()
	if epoch <= 0 or sequence == 0 or kind < 1 or kind > 2 or status > 11 or npc_spawn_id <= 0 or money < 0 or item_uid < 0 or item_id < 1 or item_id > 100000000 or quantity < 1 or quantity > 1000000 or remaining < 0 or remaining > 1000000:
		return {}
	if status in [0,1] and item_uid <= 0:
		return {}
	return {"map_epoch":epoch,"shop_sequence":sequence,"kind":kind,"status":status,
		"npc_spawn_id":npc_spawn_id,"money":money,"item_uid":item_uid,
		"item_definition_id":item_id,"quantity":quantity,"remaining_quantity":remaining}


static func encode_storage_open(epoch: int, sequence: int, npc_spawn_id: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or npc_spawn_id <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(STORAGE_OPEN_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_STORAGE_OPEN)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	return writer.data_array


static func encode_storage_deposit(epoch: int, sequence: int, npc_spawn_id: int, item_uid: int) -> PackedByteArray:
	return _encode_storage_move(OPCODE_STORAGE_DEPOSIT,epoch,sequence,npc_spawn_id,item_uid)


static func encode_storage_withdraw(epoch: int, sequence: int, npc_spawn_id: int, item_uid: int) -> PackedByteArray:
	return _encode_storage_move(OPCODE_STORAGE_WITHDRAW,epoch,sequence,npc_spawn_id,item_uid)


static func _encode_storage_move(opcode: int, epoch: int, sequence: int, npc_spawn_id: int, item_uid: int) -> PackedByteArray:
	if opcode not in [OPCODE_STORAGE_DEPOSIT,OPCODE_STORAGE_WITHDRAW] or epoch <= 0 or sequence < 1 or sequence > 0xffffffff or npc_spawn_id <= 0 or item_uid <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(STORAGE_MOVE_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(opcode)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	writer.put_64(item_uid)
	return writer.data_array


static func decode_storage_snapshot(frame: PackedByteArray) -> Dictionary:
	if not _valid_variable_header(frame,OPCODE_STORAGE_SNAPSHOT,STORAGE_SNAPSHOT_HEADER_BYTES,STORAGE_SNAPSHOT_HEADER_BYTES+STORAGE_SNAPSHOT_MAX_PER_FRAME*STORAGE_ITEM_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var npc_spawn_id := reader.get_32()
	var start_index := reader.get_32()
	var complete_raw := reader.get_u8()
	var count := reader.get_u16()
	if epoch <= 0 or sequence == 0 or status > 4 or npc_spawn_id <= 0 or start_index < 0 or start_index > STORAGE_SNAPSHOT_MAX_ITEMS or complete_raw > 1 or count > STORAGE_SNAPSHOT_MAX_PER_FRAME or start_index+count > STORAGE_SNAPSHOT_MAX_ITEMS or frame.size() != STORAGE_SNAPSHOT_HEADER_BYTES+count*STORAGE_ITEM_BYTES:
		return {}
	if status != 0 and (start_index != 0 or complete_raw != 1 or count != 0):
		return {}
	var items: Array[Dictionary] = []
	var uids := {}
	for _index in count:
		var uid := reader.get_64()
		var item_id := reader.get_32()
		var quantity := reader.get_32()
		var source_drop_id := reader.get_64()
		if uid <= 0 or item_id < 1 or item_id > 100000000 or quantity < 1 or quantity > 1000000 or source_drop_id < 0 or uids.has(uid):
			return {}
		uids[uid] = true
		items.append({"item_uid":uid,"item_definition_id":item_id,
			"quantity":quantity,"drop_id":source_drop_id})
	return {"map_epoch":epoch,"storage_sequence":sequence,"status":status,
		"npc_spawn_id":npc_spawn_id,"start_index":start_index,
		"complete":complete_raw==1,"items":items}


static func decode_storage_transfer_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_STORAGE_TRANSFER_RESULT,STORAGE_TRANSFER_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var kind := reader.get_u8()
	var status := reader.get_u8()
	var npc_spawn_id := reader.get_32()
	var uid := reader.get_64()
	var item_id := reader.get_32()
	var quantity := reader.get_32()
	var source_drop_id := reader.get_64()
	if epoch <= 0 or sequence == 0 or kind < 1 or kind > 2 or status > 8 or npc_spawn_id <= 0 or uid <= 0 or item_id < 0 or item_id > 100000000 or quantity < 0 or quantity > 1000000 or source_drop_id < 0:
		return {}
	if status in [0,1]:
		if item_id <= 0 or quantity <= 0:
			return {}
	elif item_id != 0 or quantity != 0 or source_drop_id != 0:
		return {}
	return {"map_epoch":epoch,"storage_sequence":sequence,"kind":kind,
		"status":status,"npc_spawn_id":npc_spawn_id,"item_uid":uid,
		"item_definition_id":item_id,"quantity":quantity,
		"drop_id":source_drop_id}


static func encode_bank_open(epoch: int, sequence: int, npc_spawn_id: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or npc_spawn_id <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(BANK_OPEN_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_BANK_OPEN)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	return writer.data_array


static func decode_bank_snapshot(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_BANK_SNAPSHOT,BANK_SNAPSHOT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var npc_spawn_id := reader.get_32()
	var scope := reader.get_u8()
	var wallet_money := reader.get_64()
	var bank_money := reader.get_64()
	if epoch <= 0 or sequence == 0 or status > 4 or npc_spawn_id <= 0 or wallet_money < 0 or wallet_money > MAX_MONEY or bank_money < 0 or bank_money > MAX_MONEY:
		return {}
	if status == 0:
		if scope not in [1,2]:
			return {}
	elif scope != 0 or wallet_money != 0 or bank_money != 0:
		return {}
	return {"map_epoch":epoch,"bank_sequence":sequence,"status":status,
		"npc_spawn_id":npc_spawn_id,"scope":scope,
		"wallet_money":wallet_money,"bank_money":bank_money}


static func encode_bank_deposit(epoch: int, sequence: int, npc_spawn_id: int, amount: int) -> PackedByteArray:
	return _encode_bank_move(OPCODE_BANK_DEPOSIT,epoch,sequence,npc_spawn_id,amount)


static func encode_bank_withdraw(epoch: int, sequence: int, npc_spawn_id: int, amount: int) -> PackedByteArray:
	return _encode_bank_move(OPCODE_BANK_WITHDRAW,epoch,sequence,npc_spawn_id,amount)


static func _encode_bank_move(opcode: int, epoch: int, sequence: int, npc_spawn_id: int, amount: int) -> PackedByteArray:
	if opcode not in [OPCODE_BANK_DEPOSIT,OPCODE_BANK_WITHDRAW] or epoch <= 0 or sequence < 1 or sequence > 0xffffffff or npc_spawn_id <= 0 or amount <= 0 or amount > MAX_MONEY:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(BANK_MOVE_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(opcode)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	writer.put_64(amount)
	return writer.data_array


static func decode_bank_transaction_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_BANK_TRANSACTION_RESULT,BANK_TRANSACTION_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var kind := reader.get_u8()
	var status := reader.get_u8()
	var npc_spawn_id := reader.get_32()
	var scope := reader.get_u8()
	var amount := reader.get_64()
	var wallet_money := reader.get_64()
	var bank_money := reader.get_64()
	if epoch <= 0 or sequence == 0 or kind < 1 or kind > 2 or status > 10 or npc_spawn_id <= 0 or amount <= 0 or amount > MAX_MONEY or wallet_money < 0 or wallet_money > MAX_MONEY or bank_money < 0 or bank_money > MAX_MONEY:
		return {}
	var scoped := scope in [1,2]
	if status in [0,1,4,5,6,7,8]:
		if not scoped:
			return {}
	elif scope != 0 or wallet_money != 0 or bank_money != 0:
		return {}
	return {"map_epoch":epoch,"bank_sequence":sequence,"kind":kind,
		"status":status,"npc_spawn_id":npc_spawn_id,"scope":scope,
		"amount":amount,"wallet_money":wallet_money,"bank_money":bank_money}


static func encode_trade_command(epoch: int, sequence: int, action: int,
		trade_id: int = 0, target_actor_id: int = 0,
		item_uid: int = 0, money: int = 0) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or action < 1 or action > 9 or trade_id < 0 or target_actor_id < 0 or item_uid < 0 or money < 0 or money > MAX_MONEY:
		return PackedByteArray()
	var valid := false
	match action:
		1:
			valid = trade_id == 0 and target_actor_id > 0 and item_uid == 0 and money == 0
		2,3:
			valid = trade_id > 0 and target_actor_id == 0 and item_uid == 0 and money == 0
		4,5:
			valid = trade_id > 0 and target_actor_id == 0 and item_uid > 0 and money == 0
		6:
			valid = trade_id > 0 and target_actor_id == 0 and item_uid == 0
		7,8,9:
			valid = trade_id > 0 and target_actor_id == 0 and item_uid == 0 and money == 0
	if not valid:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(TRADE_COMMAND_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_TRADE_COMMAND)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(action)
	writer.put_64(trade_id)
	writer.put_64(target_actor_id)
	writer.put_64(item_uid)
	writer.put_64(money)
	return writer.data_array


static func decode_trade_snapshot(frame: PackedByteArray) -> Dictionary:
	if not _valid_variable_header(frame,OPCODE_TRADE_SNAPSHOT,
			TRADE_SNAPSHOT_HEADER_BYTES,
			TRADE_SNAPSHOT_HEADER_BYTES+2*TRADE_MAX_ITEMS_PER_SIDE*TRADE_OFFER_ITEM_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var trade_id := reader.get_64()
	var revision := reader.get_u32()
	var state := reader.get_u8()
	var peer_actor_id := reader.get_64()
	var my_money := reader.get_64()
	var other_money := reader.get_64()
	var my_locked_raw := reader.get_u8()
	var other_locked_raw := reader.get_u8()
	var my_confirmed_raw := reader.get_u8()
	var other_confirmed_raw := reader.get_u8()
	var my_count := reader.get_u8()
	var other_count := reader.get_u8()
	if epoch <= 0 or trade_id <= 0 or revision == 0 or state < 1 or state > 4 or peer_actor_id <= 0 or my_money < 0 or my_money > MAX_MONEY or other_money < 0 or other_money > MAX_MONEY or my_locked_raw > 1 or other_locked_raw > 1 or my_confirmed_raw > 1 or other_confirmed_raw > 1 or my_count > TRADE_MAX_ITEMS_PER_SIDE or other_count > TRADE_MAX_ITEMS_PER_SIDE or frame.size() != TRADE_SNAPSHOT_HEADER_BYTES+(my_count+other_count)*TRADE_OFFER_ITEM_BYTES:
		return {}
	var my_locked := my_locked_raw == 1
	var other_locked := other_locked_raw == 1
	var my_confirmed := my_confirmed_raw == 1
	var other_confirmed := other_confirmed_raw == 1
	if (my_confirmed and not my_locked) or (other_confirmed and not other_locked):
		return {}
	var my_items: Array[Dictionary] = []
	var other_items: Array[Dictionary] = []
	var seen := {}
	for index in my_count+other_count:
		var uid := reader.get_64()
		var definition := reader.get_32()
		var quantity := reader.get_32()
		var source_drop_id := reader.get_64()
		if uid <= 0 or definition < 1 or definition > 100000000 or quantity < 1 or quantity > 1000000 or source_drop_id < 0 or seen.has(uid):
			return {}
		seen[uid] = true
		var item := {"item_uid":uid,"item_definition_id":definition,
			"quantity":quantity,"drop_id":source_drop_id}
		if index < my_count:
			my_items.append(item)
		else:
			other_items.append(item)
	var invite := state in [1,2]
	if invite and (my_money != 0 or other_money != 0 or my_locked or other_locked or my_confirmed or other_confirmed or not my_items.is_empty() or not other_items.is_empty()):
		return {}
	if state == 4 and (not my_locked or not other_locked):
		return {}
	return {"map_epoch":epoch,"trade_id":trade_id,"revision":revision,
		"state":state,"peer_actor_id":peer_actor_id,
		"my_money":my_money,"other_money":other_money,
		"my_locked":my_locked,"other_locked":other_locked,
		"my_confirmed":my_confirmed,"other_confirmed":other_confirmed,
		"my_items":my_items,"other_items":other_items}


static func decode_trade_terminal(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_TRADE_TERMINAL,TRADE_TERMINAL_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var trade_id := reader.get_64()
	var revision := reader.get_u32()
	var status := reader.get_u8()
	var wallet_money := reader.get_64()
	if epoch <= 0 or trade_id <= 0 or revision == 0 or status > 6 or wallet_money < 0 or wallet_money > MAX_MONEY:
		return {}
	if status != 0 and wallet_money != 0:
		return {}
	return {"map_epoch":epoch,"trade_id":trade_id,"revision":revision,
		"status":status,"wallet_money":wallet_money}


static func encode_repair_open(epoch: int, sequence: int, npc_spawn_id: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or npc_spawn_id <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(REPAIR_OPEN_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_REPAIR_OPEN)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	return writer.data_array


static func decode_repair_snapshot(frame: PackedByteArray) -> Dictionary:
	if not _valid_variable_header(frame,OPCODE_REPAIR_SNAPSHOT,REPAIR_SNAPSHOT_HEADER_BYTES,REPAIR_SNAPSHOT_HEADER_BYTES+REPAIR_SNAPSHOT_MAX_PER_FRAME*REPAIR_PREVIEW_ITEM_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var npc_spawn_id := reader.get_32()
	var wallet_money := reader.get_64()
	var start_index := reader.get_32()
	var complete_raw := reader.get_u8()
	var count := reader.get_u16()
	if epoch <= 0 or sequence == 0 or status > 4 or npc_spawn_id <= 0 or wallet_money < 0 or wallet_money > MAX_MONEY or start_index < 0 or start_index > REPAIR_SNAPSHOT_MAX_ITEMS or complete_raw > 1 or count > REPAIR_SNAPSHOT_MAX_PER_FRAME or start_index+count > REPAIR_SNAPSHOT_MAX_ITEMS or frame.size() != REPAIR_SNAPSHOT_HEADER_BYTES+count*REPAIR_PREVIEW_ITEM_BYTES:
		return {}
	var items: Array[Dictionary] = []
	var uids := {}
	for _index in count:
		var uid := reader.get_64()
		var item_id := reader.get_32()
		var location := reader.get_u8()
		var current := reader.get_32()
		var maximum := reader.get_32()
		var cost := reader.get_64()
		if uid <= 0 or item_id < 1 or item_id > 100000000 or location not in [1,2] or current < 0 or maximum <= 0 or current > maximum or cost < 0 or cost > MAX_MONEY or uids.has(uid):
			return {}
		uids[uid] = true
		items.append({"item_uid":uid,"item_definition_id":item_id,
			"location":location,"current_durability":current,
			"max_durability":maximum,"cost":cost})
	if status != 0:
		if wallet_money != 0 or start_index != 0 or complete_raw != 1 or not items.is_empty():
			return {}
	return {"map_epoch":epoch,"repair_sequence":sequence,"status":status,
		"npc_spawn_id":npc_spawn_id,"wallet_money":wallet_money,
		"start_index":start_index,"complete":complete_raw==1,"items":items}


static func encode_repair_item(epoch: int, sequence: int, npc_spawn_id: int, item_uid: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or npc_spawn_id <= 0 or item_uid <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(REPAIR_ITEM_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_REPAIR_ITEM)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	writer.put_64(item_uid)
	return writer.data_array


static func decode_repair_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_REPAIR_RESULT,REPAIR_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var npc_spawn_id := reader.get_32()
	var uid := reader.get_64()
	var item_id := reader.get_32()
	var location := reader.get_u8()
	var current := reader.get_32()
	var maximum := reader.get_32()
	var cost := reader.get_64()
	var wallet_money := reader.get_64()
	if epoch <= 0 or sequence == 0 or status > 11 or npc_spawn_id <= 0 or uid <= 0 or item_id < 0 or item_id > 100000000 or current < 0 or maximum < 0 or cost < 0 or cost > MAX_MONEY or wallet_money < 0 or wallet_money > MAX_MONEY:
		return {}
	var known := item_id > 0 and location in [1,2]
	if status in [0,1]:
		if not known or maximum <= 0 or current != maximum or cost <= 0:
			return {}
	elif status == 6:
		if not known or maximum <= 0 or current != maximum or cost != 0:
			return {}
	elif status == 7:
		if not known or maximum <= 0 or current > maximum or cost <= 0:
			return {}
	elif status == 5:
		if not known or current != 0 or maximum != 0 or cost != 0:
			return {}
	elif status == 11:
		if not known or current != 0 or maximum <= 0 or cost != 0:
			return {}
	elif item_id != 0 or location != 0 or current != 0 or maximum != 0 or cost != 0:
		return {}
	return {"map_epoch":epoch,"repair_sequence":sequence,"status":status,
		"npc_spawn_id":npc_spawn_id,"item_uid":uid,
		"item_definition_id":item_id,"location":location,
		"current_durability":current,"max_durability":maximum,
		"cost":cost,"wallet_money":wallet_money}


static func encode_enhancement_open(epoch: int, sequence: int, npc_spawn_id: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or npc_spawn_id <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(ENHANCEMENT_OPEN_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_ENHANCEMENT_OPEN)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	return writer.data_array


static func decode_enhancement_snapshot(frame: PackedByteArray) -> Dictionary:
	if not _valid_variable_header(frame,OPCODE_ENHANCEMENT_SNAPSHOT,
		ENHANCEMENT_SNAPSHOT_HEADER_BYTES,
		ENHANCEMENT_SNAPSHOT_HEADER_BYTES+
			ENHANCEMENT_SNAPSHOT_MAX_PER_FRAME*ENHANCEMENT_PREVIEW_ITEM_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var npc_spawn_id := reader.get_32()
	var wallet_money := reader.get_64()
	var start_index := reader.get_32()
	var complete_raw := reader.get_u8()
	var count := reader.get_u16()
	if epoch <= 0 or sequence == 0 or status > 4 or npc_spawn_id <= 0 or wallet_money < 0 or wallet_money > MAX_MONEY or start_index < 0 or start_index > ENHANCEMENT_SNAPSHOT_MAX_ITEMS or complete_raw > 1 or count > ENHANCEMENT_SNAPSHOT_MAX_PER_FRAME or start_index+count > ENHANCEMENT_SNAPSHOT_MAX_ITEMS or frame.size() != ENHANCEMENT_SNAPSHOT_HEADER_BYTES+count*ENHANCEMENT_PREVIEW_ITEM_BYTES:
		return {}
	var items: Array[Dictionary] = []
	var uids := {}
	for _index in count:
		var uid := reader.get_64()
		var item_id := reader.get_32()
		var location := reader.get_u8()
		var current_level := reader.get_u8()
		var target_level := reader.get_u8()
		var success_ppm := reader.get_32()
		var money_cost := reader.get_64()
		var consume_raw := reader.get_u8()
		if uid <= 0 or item_id < 1 or item_id > 100000000 or location not in [1,2] or current_level < 0 or current_level > 254 or target_level < 1 or target_level > 255 or target_level <= current_level or success_ppm < 0 or success_ppm > 1000000 or money_cost < 0 or money_cost > MAX_MONEY or consume_raw > 1 or uids.has(uid):
			return {}
		uids[uid] = true
		items.append({"item_uid":uid,"item_definition_id":item_id,
			"location":location,"current_level":current_level,
			"target_level":target_level,"success_ppm":success_ppm,
			"money_cost":money_cost,
			"consume_money_on_failure":consume_raw==1})
	if status != 0:
		if wallet_money != 0 or start_index != 0 or complete_raw != 1 or not items.is_empty():
			return {}
	return {"map_epoch":epoch,"enhancement_sequence":sequence,"status":status,
		"npc_spawn_id":npc_spawn_id,"wallet_money":wallet_money,
		"start_index":start_index,"complete":complete_raw==1,"items":items}


static func encode_enhancement_item(epoch: int, sequence: int, npc_spawn_id: int, item_uid: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or npc_spawn_id <= 0 or item_uid <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(ENHANCEMENT_ITEM_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_ENHANCEMENT_ITEM)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	writer.put_64(item_uid)
	return writer.data_array


static func decode_enhancement_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_ENHANCEMENT_RESULT,ENHANCEMENT_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var npc_spawn_id := reader.get_32()
	var uid := reader.get_64()
	var item_id := reader.get_32()
	var location := reader.get_u8()
	var previous_level := reader.get_u8()
	var target_level := reader.get_u8()
	var current_level := reader.get_u8()
	var succeeded_raw := reader.get_u8()
	var money_cost := reader.get_64()
	var money_charged := reader.get_64()
	var wallet_money := reader.get_64()
	var roll_ppm := reader.get_32()
	if epoch <= 0 or sequence == 0 or status > 10 or npc_spawn_id <= 0 or uid <= 0 or item_id < 0 or item_id > 100000000 or previous_level < 0 or previous_level > 255 or target_level < 0 or target_level > 255 or current_level < 0 or current_level > 255 or succeeded_raw > 1 or money_cost < 0 or money_cost > MAX_MONEY or money_charged < 0 or money_charged > money_cost or wallet_money < 0 or wallet_money > MAX_MONEY or roll_ppm < 0 or roll_ppm >= 1000000:
		return {}
	var succeeded := succeeded_raw == 1
	var known := item_id > 0 and location in [1,2]
	if status in [0,1]:
		if not known or target_level <= previous_level:
			return {}
		if succeeded:
			if current_level != target_level or money_charged != money_cost:
				return {}
		elif current_level != previous_level:
			return {}
	elif status == 7:
		if not known or succeeded or target_level <= previous_level or current_level != previous_level or money_cost <= 0 or money_charged != 0 or roll_ppm != 0:
			return {}
	elif status in [5,6]:
		if not known or succeeded or target_level != 0 or current_level != previous_level or money_cost != 0 or money_charged != 0 or roll_ppm != 0:
			return {}
	elif item_id != 0 or location != 0 or previous_level != 0 or target_level != 0 or current_level != 0 or succeeded or money_cost != 0 or money_charged != 0 or roll_ppm != 0:
		return {}
	return {"map_epoch":epoch,"enhancement_sequence":sequence,"status":status,
		"npc_spawn_id":npc_spawn_id,"item_uid":uid,
		"item_definition_id":item_id,"location":location,
		"previous_level":previous_level,"target_level":target_level,
		"current_level":current_level,"succeeded":succeeded,
		"money_cost":money_cost,"money_charged":money_charged,
		"wallet_money":wallet_money,"roll_ppm":roll_ppm}


static func encode_equipment_equip(epoch: int, sequence: int, item_uid: int) -> PackedByteArray:
	return _encode_equipment_move(OPCODE_EQUIPMENT_EQUIP,epoch,sequence,item_uid)


static func encode_equipment_unequip(epoch: int, sequence: int, item_uid: int) -> PackedByteArray:
	return _encode_equipment_move(OPCODE_EQUIPMENT_UNEQUIP,epoch,sequence,item_uid)


static func _encode_equipment_move(opcode: int, epoch: int, sequence: int, item_uid: int) -> PackedByteArray:
	if opcode not in [OPCODE_EQUIPMENT_EQUIP,OPCODE_EQUIPMENT_UNEQUIP] or epoch <= 0 or sequence < 1 or sequence > 0xffffffff or item_uid <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(EQUIPMENT_MOVE_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(opcode)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_64(item_uid)
	return writer.data_array


static func decode_equipment_transfer_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_EQUIPMENT_TRANSFER_RESULT,EQUIPMENT_TRANSFER_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var kind := reader.get_u8()
	var status := reader.get_u8()
	var uid := reader.get_64()
	var item_id := reader.get_32()
	var slot_id := reader.get_32()
	var source_drop_id := reader.get_64()
	if epoch <= 0 or sequence == 0 or kind < 1 or kind > 2 or status > 8 or uid <= 0 or item_id < 0 or item_id > 100000000 or slot_id < 0 or slot_id > 255 or source_drop_id < 0:
		return {}
	if status in [0,1]:
		if item_id <= 0 or slot_id <= 0:
			return {}
	elif item_id != 0 or slot_id != 0 or source_drop_id != 0:
		return {}
	return {"map_epoch":epoch,"equipment_sequence":sequence,"kind":kind,"status":status,"item_uid":uid,"item_definition_id":item_id,"slot_id":slot_id,"drop_id":source_drop_id}

static func decode_equipment_snapshot(frame: PackedByteArray) -> Dictionary:
	if not _valid_variable_header(frame,OPCODE_EQUIPMENT_SNAPSHOT,EQUIPMENT_SNAPSHOT_HEADER_BYTES,EQUIPMENT_SNAPSHOT_HEADER_BYTES+EQUIPMENT_SNAPSHOT_MAX_PER_FRAME*EQUIPMENT_ITEM_BYTES):
		return {}
	var reader := _reader(frame,8)
	var character_id := reader.get_64()
	var start_index := reader.get_32()
	var complete_raw := reader.get_u8()
	var count := reader.get_u16()
	if character_id <= 0 or start_index < 0 or start_index > EQUIPMENT_SNAPSHOT_MAX_ITEMS or complete_raw > 1 or count > EQUIPMENT_SNAPSHOT_MAX_PER_FRAME or start_index+count > EQUIPMENT_SNAPSHOT_MAX_ITEMS or frame.size() != EQUIPMENT_SNAPSHOT_HEADER_BYTES+count*EQUIPMENT_ITEM_BYTES:
		return {}
	var items: Array[Dictionary] = []
	var uids := {}
	var slots := {}
	for _index in count:
		var uid := reader.get_64()
		var slot_id := reader.get_32()
		var item_id := reader.get_32()
		var source_drop_id := reader.get_64()
		if uid <= 0 or slot_id < 1 or slot_id > 255 or item_id < 1 or item_id > 100000000 or source_drop_id < 0 or uids.has(uid) or slots.has(slot_id):
			return {}
		uids[uid] = true
		slots[slot_id] = true
		items.append({"item_uid":uid,"slot_id":slot_id,"item_definition_id":item_id,"drop_id":source_drop_id})
	return {"character_id":character_id,"start_index":start_index,"complete":complete_raw==1,"items":items}

static func encode_pvp_mode_command(epoch: int, sequence: int, action: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or action < 0 or action > 2:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(PVP_MODE_COMMAND_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_PVP_MODE_COMMAND)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(action)
	return writer.data_array


static func decode_pvp_mode_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_PVP_MODE_RESULT,PVP_MODE_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var mode := reader.get_u8()
	var allowed := reader.get_u8()
	if epoch <= 0 or sequence == 0 or status > 6 or mode > 1 or allowed > 1:
		return {}
	return {"map_epoch":epoch,"pvp_sequence":sequence,"status":status,
		"mode":mode,"map_allows_pvp":allowed==1}


static func encode_pvp_attack(epoch: int, sequence: int, target_actor_id: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or target_actor_id <= 0:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(PVP_ATTACK_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_PVP_ATTACK)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_64(target_actor_id)
	return writer.data_array


static func decode_pvp_attack_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_PVP_ATTACK_RESULT,PVP_ATTACK_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u8()
	var tick := reader.get_64()
	var target := reader.get_64()
	var damage := reader.get_32()
	var died := reader.get_u8()
	if epoch <= 0 or sequence == 0 or status > 14 or tick < 0 or target <= 0 or damage < 0 or died > 1:
		return {}
	if status == 0:
		if damage <= 0:
			return {}
	elif damage != 0 or died != 0:
		return {}
	return {"map_epoch":epoch,"attack_sequence":sequence,"status":status,
		"server_tick":tick,"target_actor_id":target,"damage":damage,"died":died==1}


static func decode_pvp_damage(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_PVP_DAMAGE,PVP_DAMAGE_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var sequence := reader.get_u32()
	var tick := reader.get_64()
	var attacker := reader.get_64()
	var connection := reader.get_64()
	var attack_sequence := reader.get_u32()
	var damage := reader.get_32()
	var current_hp := reader.get_32()
	var max_hp := reader.get_32()
	var died := reader.get_u8()
	if epoch <= 0 or sequence == 0 or tick < 0 or attacker <= 0 or connection <= 0 or attack_sequence == 0 or damage <= 0 or current_hp < 0 or max_hp <= 0 or current_hp > max_hp or died > 1:
		return {}
	if (died == 1 and current_hp != 0) or (died == 0 and current_hp <= 0):
		return {}
	return {"map_epoch":epoch,"damage_sequence":sequence,"server_tick":tick,
		"attacker_actor_id":attacker,"attacker_connection_epoch":connection,
		"attacker_attack_sequence":attack_sequence,"damage":damage,
		"current_hp":current_hp,"max_hp":max_hp,"died":died==1}


static func decode_pvp_combat_visual(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_PVP_COMBAT_VISUAL,PVP_COMBAT_VISUAL_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var receiver_epoch:=reader.get_64()
	var tick:=reader.get_64()
	var attacker:=reader.get_64()
	var attacker_connection:=reader.get_64()
	var attack_sequence:=reader.get_u32()
	var victim:=reader.get_64()
	var victim_connection:=reader.get_64()
	var damage_sequence:=reader.get_u32()
	var damage:=reader.get_32()
	var died:=reader.get_u8()
	var attacker_facing:=reader.get_u8()
	var victim_facing:=reader.get_u8()
	if receiver_epoch<=0 or tick<0 or attacker<=0 or attacker_connection<=0 or attack_sequence==0 or victim<=0 or victim_connection<=0 or damage_sequence==0 or damage<=0 or died>1 or attacker==victim or attacker_facing<1 or attacker_facing>8 or victim_facing<1 or victim_facing>8:
		return {}
	return {"receiver_map_epoch":receiver_epoch,"server_tick":tick,
		"attacker_actor_id":attacker,"attacker_connection_epoch":attacker_connection,
		"attack_sequence":attack_sequence,"victim_actor_id":victim,
		"victim_connection_epoch":victim_connection,
		"damage_sequence":damage_sequence,"damage":damage,"died":died==1,
		"attacker_facing":attacker_facing,"victim_facing":victim_facing}


static func encode_pvp_skill(epoch:int,sequence:int,skill_id:int,target_actor_id:int)->PackedByteArray:
	if epoch<=0 or sequence<=0 or sequence>0xffffffff or skill_id<=0 or skill_id>100000000 or target_actor_id<=0:
		return PackedByteArray()
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(PVP_SKILL_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_PVP_SKILL)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(skill_id)
	writer.put_64(target_actor_id)
	return writer.data_array


static func decode_pvp_skill_result(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_PVP_SKILL_RESULT,PVP_SKILL_RESULT_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var sequence:=reader.get_u32()
	var status:=reader.get_u8()
	var tick:=reader.get_64()
	var skill_id:=reader.get_32()
	var target:=reader.get_64()
	var damage:=reader.get_32()
	var died:=reader.get_u8()
	if epoch<=0 or sequence==0 or status>17 or tick<0 or skill_id<=0 or skill_id>100000000 or target<=0 or damage<0 or died>1:
		return {}
	if status==0:
		if damage<=0:
			return {}
	elif damage!=0 or died!=0:
		return {}
	return {"map_epoch":epoch,"skill_sequence":sequence,"status":status,
		"server_tick":tick,"skill_id":skill_id,"target_actor_id":target,
		"damage":damage,"died":died==1}


static func decode_pvp_skill_visual(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_PVP_SKILL_VISUAL,PVP_SKILL_VISUAL_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var receiver_epoch:=reader.get_64()
	var tick:=reader.get_64()
	var attacker:=reader.get_64()
	var attacker_connection:=reader.get_64()
	var skill_sequence:=reader.get_u32()
	var skill_id:=reader.get_32()
	var victim:=reader.get_64()
	var victim_connection:=reader.get_64()
	var damage_sequence:=reader.get_u32()
	var damage:=reader.get_32()
	var died:=reader.get_u8()
	var attacker_facing:=reader.get_u8()
	var victim_facing:=reader.get_u8()
	if receiver_epoch<=0 or tick<0 or attacker<=0 or attacker_connection<=0 or skill_sequence==0 or skill_id<=0 or skill_id>100000000 or victim<=0 or victim_connection<=0 or damage_sequence==0 or damage<=0 or died>1 or attacker==victim or attacker_facing<1 or attacker_facing>8 or victim_facing<1 or victim_facing>8:
		return {}
	return {"receiver_map_epoch":receiver_epoch,"server_tick":tick,
		"attacker_actor_id":attacker,"attacker_connection_epoch":attacker_connection,
		"skill_sequence":skill_sequence,"skill_id":skill_id,
		"victim_actor_id":victim,"victim_connection_epoch":victim_connection,
		"damage_sequence":damage_sequence,"damage":damage,"died":died==1,
		"attacker_facing":attacker_facing,"victim_facing":victim_facing}


static func decode_gm_capability(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_GM_CAPABILITY,GM_CAPABILITY_FRAME_BYTES):
		return {}
	var authorized:=int(frame[8])
	if authorized not in [0,1]:
		return {}
	return {"authorized":authorized==1}


static func encode_gm_pvp_name_command(
		epoch:int,sequence:int,target_actor_id:int,state:int)->PackedByteArray:
	if epoch<=0 or sequence<=0 or sequence>0xffffffff or target_actor_id<=0 or state<0 or state>4:
		return PackedByteArray()
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(GM_PVP_NAME_COMMAND_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_GM_PVP_NAME_COMMAND)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_64(target_actor_id)
	writer.put_u8(state)
	return writer.data_array


static func decode_gm_pvp_name_result(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_GM_PVP_NAME_RESULT,GM_PVP_NAME_RESULT_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var sequence:=reader.get_u32()
	var status:=reader.get_u8()
	var target:=reader.get_64()
	var state:=reader.get_u8()
	if epoch<=0 or sequence==0 or status>5 or target<=0 or state>4:
		return {}
	return {"map_epoch":epoch,"gm_sequence":sequence,"status":status,
		"target_actor_id":target,"name_state":state}


static func encode_player_shop_command(
		epoch:int,sequence:int,action:int,seller_actor_id:int,
		listing_id:int,item_uid:int,price:int)->PackedByteArray:
	if (
		epoch<=0 or sequence<=0 or sequence>0xffffffff or action<1 or action>4 or
		seller_actor_id<0 or listing_id<0 or item_uid<0 or
		price<0 or price>MAX_MONEY
	):
		return PackedByteArray()
	var valid:=false
	match action:
		1:
			valid=seller_actor_id==0 and listing_id==0 and item_uid>0 and price>0
		2:
			valid=seller_actor_id==0 and listing_id>0 and item_uid==0 and price==0
		3:
			valid=seller_actor_id>0 and listing_id==0 and item_uid==0 and price==0
		4:
			valid=seller_actor_id>0 and listing_id>0 and item_uid==0 and price==0
	if not valid:
		return PackedByteArray()
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(PLAYER_SHOP_COMMAND_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_PLAYER_SHOP_COMMAND)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(action)
	writer.put_64(seller_actor_id)
	writer.put_64(listing_id)
	writer.put_64(item_uid)
	writer.put_64(price)
	return writer.data_array


static func decode_player_shop_result(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_PLAYER_SHOP_RESULT,PLAYER_SHOP_RESULT_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var sequence:=reader.get_u32()
	var action:=reader.get_u8()
	var status:=reader.get_u8()
	var seller:=reader.get_64()
	var listing:=reader.get_64()
	var uid:=reader.get_64()
	var item_id:=reader.get_32()
	var quantity:=reader.get_32()
	var source_drop:=reader.get_64()
	var durability_raw:=reader.get_32()
	var price:=reader.get_64()
	var seller_wallet:=reader.get_64()
	var buyer_wallet:=reader.get_64()
	if (
		epoch<=0 or sequence==0 or action<1 or action>4 or status>15 or
		seller<0 or listing<0 or uid<0 or item_id<0 or item_id>100000000 or
		quantity<0 or quantity>1000000 or source_drop<0 or durability_raw<-1 or
		price<0 or price>MAX_MONEY or seller_wallet<0 or seller_wallet>MAX_MONEY or
		buyer_wallet<0 or buyer_wallet>MAX_MONEY
	):
		return {}
	return {
		"map_epoch":epoch,"shop_sequence":sequence,"action":action,"status":status,
		"seller_actor_id":seller,"listing_id":listing,"item_uid":uid,
		"item_definition_id":item_id,"quantity":quantity,
		"source_drop_id":source_drop,"durability_raw":durability_raw,
		"price":price,"seller_wallet":seller_wallet,"buyer_wallet":buyer_wallet}


static func decode_player_shop_sold(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_PLAYER_SHOP_SOLD,PLAYER_SHOP_SOLD_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var listing:=reader.get_64()
	var buyer:=reader.get_64()
	var uid:=reader.get_64()
	var wallet:=reader.get_64()
	if epoch<=0 or listing<=0 or buyer<=0 or uid<=0 or wallet<0 or wallet>MAX_MONEY:
		return {}
	return {"receiver_map_epoch":epoch,"listing_id":listing,
		"buyer_actor_id":buyer,"item_uid":uid,"seller_wallet":wallet}


static func encode_quest_open(
		epoch:int,sequence:int,npc_spawn_id:int)->PackedByteArray:
	if epoch<=0 or sequence<=0 or sequence>0xffffffff or npc_spawn_id<=0:
		return PackedByteArray()
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(QUEST_OPEN_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_QUEST_OPEN)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	return writer.data_array


static func encode_quest_choose(
		epoch:int,sequence:int,npc_spawn_id:int,quest_id:int,
		choice_id:int)->PackedByteArray:
	if (
		epoch<=0 or sequence<=0 or sequence>0xffffffff or npc_spawn_id<=0 or
		quest_id<=0 or quest_id>100000000 or choice_id<=0 or
		choice_id>100000000
	):
		return PackedByteArray()
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(QUEST_CHOOSE_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_QUEST_CHOOSE)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	writer.put_32(quest_id)
	writer.put_32(choice_id)
	return writer.data_array


static func decode_quest_view(frame:PackedByteArray)->Dictionary:
	if not _valid_variable_header(
			frame,OPCODE_QUEST_VIEW,QUEST_VIEW_HEADER_BYTES,
			QUEST_VIEW_MAX_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var sequence:=reader.get_u32()
	var status:=reader.get_u8()
	var npc:=reader.get_32()
	var quest:=reader.get_32()
	var rule_revision:=reader.get_32()
	var state:=reader.get_u8()
	var stage:=reader.get_32()
	var progress_revision:=reader.get_32()
	var title_count:=reader.get_u16()
	var text_count:=reader.get_u16()
	var choice_count:=reader.get_u8()
	if (
		epoch<=0 or sequence==0 or status>10 or npc<=0 or
		title_count>QUEST_VIEW_MAX_TITLE_BYTES or
		text_count>QUEST_VIEW_MAX_TEXT_BYTES or
		choice_count>QUEST_VIEW_MAX_CHOICES or
		reader.get_available_bytes()<title_count+text_count
	):
		return {}
	var title_result:=reader.get_data(title_count)
	var text_result:=reader.get_data(text_count)
	if title_result[0]!=OK or text_result[0]!=OK:
		return {}
	var title_bytes:PackedByteArray=title_result[1]
	var text_bytes:PackedByteArray=text_result[1]
	var title:=title_bytes.get_string_from_utf8()
	var body:=text_bytes.get_string_from_utf8()
	if title.to_utf8_buffer()!=title_bytes or body.to_utf8_buffer()!=text_bytes:
		return {}
	var choices:Array[Dictionary]=[]
	var seen:={}
	for _index in choice_count:
		if reader.get_available_bytes()<6:
			return {}
		var choice_id:=reader.get_32()
		var label_count:=reader.get_u16()
		if (
			choice_id<=0 or choice_id>100000000 or label_count<1 or
			label_count>QUEST_VIEW_MAX_LABEL_BYTES or seen.has(choice_id) or
			reader.get_available_bytes()<label_count
		):
			return {}
		var label_result:=reader.get_data(label_count)
		if label_result[0]!=OK:
			return {}
		var label_bytes:PackedByteArray=label_result[1]
		var label:=label_bytes.get_string_from_utf8()
		if label.to_utf8_buffer()!=label_bytes or label.strip_edges().is_empty():
			return {}
		seen[choice_id]=true
		choices.append({"choice_id":choice_id,"label":label})
	if reader.get_available_bytes()!=0:
		return {}
	var has_view:=status in [0,1,2,6,7]
	if not has_view:
		if (
			quest!=0 or rule_revision!=0 or state!=0 or stage!=0 or
			progress_revision!=0 or not title.is_empty() or not body.is_empty() or
			not choices.is_empty()
		):
			return {}
	else:
		if (
			quest<=0 or quest>100000000 or rule_revision<=0 or state>2 or
			stage<0 or stage>1000000 or progress_revision<0 or
			title_bytes.size()<1 or body.to_utf8_buffer().size()<1 or
			title.strip_edges().is_empty()
		):
			return {}
		if state==0 and progress_revision!=0:
			return {}
		if state!=0 and progress_revision<=0:
			return {}
		if state==2 and not choices.is_empty():
			return {}
	return {
		"map_epoch":epoch,"quest_sequence":sequence,"status":status,
		"npc_spawn_id":npc,"quest_id":quest,"rule_revision":rule_revision,
		"state":state,"stage":stage,"progress_revision":progress_revision,
		"title":title,"text":body,"choices":choices
	}


static func encode_chat_command(epoch:int,sequence:int,channel:int,bubble_type:int,message:String)->PackedByteArray:
	var bytes:=message.to_utf8_buffer()
	if epoch<=0 or sequence<=0 or sequence>0xffffffff or channel<0 or channel>4 or bubble_type<0 or bubble_type>5 or bytes.size()<1 or bytes.size()>CHAT_MAX_MESSAGE_BYTES or message.strip_edges().is_empty():
		return PackedByteArray()
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(CHAT_COMMAND_HEADER_BYTES+bytes.size())
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_CHAT_COMMAND)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(channel)
	writer.put_u8(bubble_type)
	writer.put_u16(bytes.size())
	writer.put_data(bytes)
	return writer.data_array


static func decode_chat_delivery(frame:PackedByteArray)->Dictionary:
	if not _valid_variable_header(frame,OPCODE_CHAT_DELIVERY,CHAT_DELIVERY_HEADER_BYTES+2,CHAT_DELIVERY_MAX_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var receiver:=reader.get_64()
	var actor:=reader.get_64()
	var connection:=reader.get_64()
	var sequence:=reader.get_u32()
	var channel:=reader.get_u8()
	var bubble:=reader.get_u8()
	var name_count:=reader.get_u16()
	var message_count:=reader.get_u16()
	if receiver<=0 or actor<=0 or connection<=0 or sequence==0 or channel>4 or bubble>5 or name_count<1 or name_count>CHAT_MAX_SENDER_NAME_BYTES or message_count<1 or message_count>CHAT_MAX_MESSAGE_BYTES or reader.get_available_bytes()!=name_count+message_count:
		return {}
	var name_result:=reader.get_data(name_count)
	var message_result:=reader.get_data(message_count)
	if name_result[0]!=OK or message_result[0]!=OK:
		return {}
	var name_bytes:PackedByteArray=name_result[1]
	var message_bytes:PackedByteArray=message_result[1]
	var sender_name:=name_bytes.get_string_from_utf8()
	var message:=message_bytes.get_string_from_utf8()
	if sender_name.to_utf8_buffer()!=name_bytes or message.to_utf8_buffer()!=message_bytes or sender_name.strip_edges().is_empty() or message.strip_edges().is_empty():
		return {}
	return {"receiver_map_epoch":receiver,"sender_actor_id":actor,"sender_connection_epoch":connection,"chat_sequence":sequence,"channel":channel,"bubble_type":bubble,"sender_name":sender_name,"message":message}

static func decode_player_identity(frame: PackedByteArray) -> Dictionary:
	if not _valid_variable_header(
		frame,OPCODE_PLAYER_IDENTITY,PLAYER_IDENTITY_HEADER_BYTES+1,
		PLAYER_IDENTITY_MAX_FRAME_BYTES
	):
		return {}
	var reader:=_reader(frame,8)
	var receiver_epoch:=reader.get_64()
	var actor:=reader.get_64()
	var connection:=reader.get_64()
	var entity_map_epoch:=reader.get_64()
	var mode:=reader.get_u8()
	var red:=reader.get_u8()
	var server_unix_ms:=reader.get_64()
	var pink_until_unix_ms:=reader.get_64()
	var pvp_updated_unix_ms:=reader.get_64()
	var name_bytes_count:=reader.get_u16()
	if receiver_epoch<=0 or actor<=0 or connection<=0 or entity_map_epoch<=0 or mode>1 or red>1 or server_unix_ms<=0 or pink_until_unix_ms<0 or pvp_updated_unix_ms<0 or name_bytes_count<1 or name_bytes_count>PLAYER_IDENTITY_MAX_NAME_BYTES or reader.get_available_bytes()!=name_bytes_count:
		return {}
	var result:=reader.get_data(name_bytes_count)
	if result[0]!=OK:
		return {}
	var name_bytes:PackedByteArray=result[1]
	var display_name:=name_bytes.get_string_from_utf8()
	if display_name.to_utf8_buffer()!=name_bytes or display_name.strip_edges().is_empty():
		return {}
	return {
		"receiver_map_epoch":receiver_epoch,
		"actor_id":actor,
		"connection_epoch":connection,
		"entity_map_epoch":entity_map_epoch,
		"mode":mode,
		"red_man":red==1,
		"server_unix_ms":server_unix_ms,
		"pink_until_unix_ms":pink_until_unix_ms,
		"pvp_updated_unix_ms":pvp_updated_unix_ms,
		"display_name":display_name,
	}


static func decode_ground_drop_spawn(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_GROUND_DROP_SPAWN,GROUND_DROP_SPAWN_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var tick := reader.get_64()
	var drop_id := reader.get_64()
	var map_id := reader.get_32()
	var x := reader.get_float()
	var y := reader.get_float()
	var item_id := reader.get_32()
	var quantity := reader.get_32()
	if epoch <= 0 or tick < 0 or drop_id <= 0 or map_id < 1 or map_id > 65535 or not is_finite(x) or not is_finite(y) or x < 0 or y < 0 or item_id < 1 or item_id > 100000000 or quantity < 1 or quantity > 1000000:
		return {}
	return {"receiver_map_epoch":epoch,"server_tick":tick,"drop_id":drop_id,
		"map_id":map_id,"x":x,"y":y,"item_definition_id":item_id,"quantity":quantity}


static func decode_ground_drop_despawn(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_GROUND_DROP_DESPAWN,GROUND_DROP_DESPAWN_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var tick := reader.get_64()
	var drop_id := reader.get_64()
	if epoch <= 0 or tick < 0 or drop_id <= 0:
		return {}
	return {"receiver_map_epoch":epoch,"server_tick":tick,"drop_id":drop_id}


static func decode_monster_despawn(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame,OPCODE_MONSTER_DESPAWN,MONSTER_DESPAWN_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var tick := reader.get_64()
	var entity_id := reader.get_64()
	var revision := reader.get_64()
	if epoch <= 0 or tick < 0 or entity_id <= 0 or revision <= 0:
		return {}
	return {"receiver_map_epoch":epoch,"server_tick":tick,"entity_id":entity_id,"revision":revision}

static func decode_entity_despawn(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_ENTITY_DESPAWN, ENTITY_DESPAWN_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var epoch := reader.get_64()
	var tick := reader.get_64()
	var actor_id := reader.get_64()
	var connection_epoch := reader.get_64()
	if epoch <= 0 or tick < 0 or actor_id <= 0 or connection_epoch <= 0:
		return {}
	return {"receiver_map_epoch": epoch, "server_tick": tick, "actor_id": actor_id, "connection_epoch": connection_epoch}

static func frame_opcode(frame: PackedByteArray) -> int:
	if frame.size() < HEADER_BYTES or frame_length_prefix(frame) != frame.size():
		return -1
	var reader := _reader(frame, 0)
	reader.get_u32()
	if reader.get_u16() != VERSION:
		return -1
	var opcode := reader.get_u16()
	if opcode not in [OPCODE_MOVE_TARGET, OPCODE_MOVE_RECEIPT, OPCODE_NPC_INTERACT, OPCODE_NPC_INTERACT_RESULT, OPCODE_NORMAL_ATTACK, OPCODE_NORMAL_ATTACK_RESULT, OPCODE_PLAYER_COMBAT_STATE, OPCODE_PLAYER_DAMAGE, OPCODE_AUTH_REQUEST, OPCODE_AUTH_RESULT, OPCODE_CHARACTER_LIST, OPCODE_CHARACTER_CHOOSE, OPCODE_CHARACTER_CHOOSE_RESULT, OPCODE_CHARACTER_CREATE, OPCODE_CHARACTER_CREATE_RESULT, OPCODE_WORLD_READY, OPCODE_ENTITY_SPAWN, OPCODE_ENTITY_DESPAWN, OPCODE_ENTITY_DELTA, OPCODE_SELF_STATE, OPCODE_WORLD_READY_ACK, OPCODE_WORLD_RESYNC_REQUEST, OPCODE_MONSTER_SPAWN, OPCODE_MONSTER_DELTA, OPCODE_MONSTER_DESPAWN, OPCODE_PLAYER_RESPAWN, OPCODE_PLAYER_RESPAWN_RESULT, OPCODE_GROUND_DROP_SPAWN, OPCODE_GROUND_DROP_DESPAWN, OPCODE_GROUND_PICKUP, OPCODE_GROUND_PICKUP_RESULT, OPCODE_INVENTORY_SNAPSHOT, OPCODE_RECOVERY_USE, OPCODE_RECOVERY_USE_RESULT, OPCODE_INVENTORY_DISCARD, OPCODE_INVENTORY_DISCARD_RESULT, OPCODE_PLAYER_ATTACK_VISUAL, OPCODE_PLAYER_DAMAGE_VISUAL, OPCODE_PLAYER_RESPAWN_VISUAL, OPCODE_SKILL_USE, OPCODE_SKILL_USE_RESULT, OPCODE_PLAYER_SKILL_VISUAL, OPCODE_SHOP_OPEN, OPCODE_SHOP_SNAPSHOT, OPCODE_SHOP_BUY, OPCODE_SHOP_SELL, OPCODE_SHOP_TRANSACTION_RESULT, OPCODE_STORAGE_OPEN, OPCODE_STORAGE_SNAPSHOT, OPCODE_STORAGE_DEPOSIT, OPCODE_STORAGE_WITHDRAW, OPCODE_STORAGE_TRANSFER_RESULT, OPCODE_EQUIPMENT_SNAPSHOT, OPCODE_EQUIPMENT_EQUIP, OPCODE_EQUIPMENT_UNEQUIP, OPCODE_EQUIPMENT_TRANSFER_RESULT, OPCODE_BANK_OPEN, OPCODE_BANK_SNAPSHOT, OPCODE_BANK_DEPOSIT, OPCODE_BANK_WITHDRAW, OPCODE_BANK_TRANSACTION_RESULT, OPCODE_REPAIR_OPEN, OPCODE_REPAIR_SNAPSHOT, OPCODE_REPAIR_ITEM, OPCODE_REPAIR_RESULT, OPCODE_TRADE_COMMAND, OPCODE_TRADE_SNAPSHOT, OPCODE_TRADE_TERMINAL, OPCODE_PVP_MODE_COMMAND, OPCODE_PVP_MODE_RESULT, OPCODE_PVP_ATTACK, OPCODE_PVP_ATTACK_RESULT, OPCODE_PVP_DAMAGE, OPCODE_PLAYER_IDENTITY, OPCODE_PVP_COMBAT_VISUAL, OPCODE_PVP_SKILL, OPCODE_PVP_SKILL_RESULT, OPCODE_PVP_SKILL_VISUAL, OPCODE_GM_CAPABILITY, OPCODE_GM_PVP_NAME_COMMAND, OPCODE_GM_PVP_NAME_RESULT, OPCODE_PLAYER_SHOP_COMMAND, OPCODE_PLAYER_SHOP_RESULT, OPCODE_PLAYER_SHOP_SOLD, OPCODE_CHAT_COMMAND, OPCODE_CHAT_DELIVERY, OPCODE_QUEST_OPEN, OPCODE_QUEST_VIEW, OPCODE_QUEST_CHOOSE, OPCODE_ENHANCEMENT_OPEN, OPCODE_ENHANCEMENT_SNAPSHOT, OPCODE_ENHANCEMENT_ITEM, OPCODE_ENHANCEMENT_RESULT]:
		return -1
	return opcode

static func frame_length_prefix(buffer: PackedByteArray) -> int:
	if buffer.size() < 4:
		return -1
	var reader := StreamPeerBuffer.new()
	reader.big_endian = false
	reader.data_array = buffer.slice(0, 4)
	var size := reader.get_u32()
	if size < HEADER_BYTES or size > MAX_FRAME_BYTES:
		return -1
	return size

static func _reader(frame: PackedByteArray, offset: int) -> StreamPeerBuffer:
	var reader := StreamPeerBuffer.new()
	reader.big_endian = false
	reader.data_array = frame
	reader.seek(offset)
	return reader

static func _valid_header(frame: PackedByteArray, expected_opcode: int, expected_bytes: int) -> bool:
	return frame.size() == expected_bytes and _valid_variable_header(frame, expected_opcode, expected_bytes, expected_bytes)

static func _valid_variable_header(frame: PackedByteArray, expected_opcode: int, minimum_bytes: int, maximum_bytes: int) -> bool:
	if frame.size() < minimum_bytes or frame.size() > maximum_bytes or frame.size() < HEADER_BYTES or frame.size() > MAX_FRAME_BYTES:
		return false
	var reader := _reader(frame, 0)
	var advertised := reader.get_u32()
	var version := reader.get_u16()
	var opcode := reader.get_u16()
	return advertised == frame.size() and version == VERSION and opcode == expected_opcode
