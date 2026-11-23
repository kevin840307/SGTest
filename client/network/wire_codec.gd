extends RefCounted

const FrameCodec = preload("res://network/frame_codec.gd")

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
const OPCODE_ACCOUNT_REGISTER_REQUEST := 17
const OPCODE_ACCOUNT_REGISTER_RESULT := 18
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
const OPCODE_WHISPER_COMMAND := 98
const OPCODE_WHISPER_RESULT := 99
const OPCODE_PARTY_COMMAND := 100
const OPCODE_PARTY_RESULT := 101
const OPCODE_PARTY_INVITE := 102
const OPCODE_PARTY_SNAPSHOT := 103
const OPCODE_SKILL_BOOK_SNAPSHOT := 104
const OPCODE_CHARACTER_MANAGE := 105
const OPCODE_CHARACTER_MANAGE_RESULT := 106
const OPCODE_CLAN_COMMAND := 107
const OPCODE_CLAN_RESULT := 108
const OPCODE_CLAN_SNAPSHOT := 109
const OPCODE_PLAYER_MOVEMENT_INTENT := 110
const OPCODE_PET_COMMAND := 111
const OPCODE_PET_RESULT := 112
const OPCODE_PET_SNAPSHOT := 113
const OPCODE_PET_ENTITY_SPAWN := 114
const OPCODE_PET_ENTITY_DESPAWN := 115
const OPCODE_PET_ENTITY_DELTA := 116
const OPCODE_PET_ATTACK := 117
const OPCODE_PET_ATTACK_RESULT := 118
const OPCODE_PET_ATTACK_VISUAL := 119
const OPCODE_FRIEND_COMMAND := 120
const OPCODE_FRIEND_RESULT := 121
const OPCODE_FRIEND_SNAPSHOT := 122
const OPCODE_MECHA_COMMAND := 123
const OPCODE_MECHA_RESULT := 124
const OPCODE_MECHA_SNAPSHOT := 125
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
const ACCOUNT_REGISTER_RESULT_FRAME_BYTES := 9
const CHARACTER_CHOOSE_FRAME_BYTES := 16
const CHARACTER_CHOOSE_RESULT_FRAME_BYTES := 45
const CHARACTER_MANAGE_RESULT_FRAME_BYTES := 18
const CLAN_MAX_NAME_BYTES := 72
const CLAN_MAX_ANNOUNCEMENT_BYTES := 1024
const CLAN_COMMAND_HEADER_BYTES := 39
const CLAN_COMMAND_MAX_FRAME_BYTES := CLAN_COMMAND_HEADER_BYTES + CLAN_MAX_ANNOUNCEMENT_BYTES
const CLAN_RESULT_FRAME_BYTES := 30
const CLAN_SNAPSHOT_HEADER_BYTES := 41
const CLAN_MEMBER_HEADER_BYTES := 11
const CLAN_MAX_MEMBERS := 100
const CLAN_SNAPSHOT_MAX_FRAME_BYTES := CLAN_SNAPSHOT_HEADER_BYTES + CLAN_MAX_NAME_BYTES + CLAN_MAX_ANNOUNCEMENT_BYTES + CLAN_MAX_MEMBERS * (CLAN_MEMBER_HEADER_BYTES + PLAYER_IDENTITY_MAX_NAME_BYTES)
const FRIEND_COMMAND_HEADER_BYTES := 31
const FRIEND_COMMAND_MAX_FRAME_BYTES := FRIEND_COMMAND_HEADER_BYTES + PLAYER_IDENTITY_MAX_NAME_BYTES
const FRIEND_RESULT_FRAME_BYTES := 30
const FRIEND_SNAPSHOT_HEADER_BYTES := 18
const FRIEND_ENTRY_HEADER_BYTES := 11
const PET_COMMAND_FRAME_BYTES := 29
const PET_RESULT_FRAME_BYTES := 38
const PET_SNAPSHOT_HEADER_BYTES := 26
const PET_SNAPSHOT_ENTRY_BYTES := 28
const PET_MAX_INSTANCES := 64
const PET_SNAPSHOT_MAX_FRAME_BYTES := PET_SNAPSHOT_HEADER_BYTES + PET_MAX_INSTANCES * PET_SNAPSHOT_ENTRY_BYTES
const MECHA_COMMAND_FRAME_BYTES := 29
const MECHA_RESULT_FRAME_BYTES := 38
const MECHA_SNAPSHOT_HEADER_BYTES := 26
const MECHA_SNAPSHOT_ENTRY_BYTES := 24
const MECHA_MAX_INSTANCES := 64
const MECHA_SNAPSHOT_MAX_FRAME_BYTES := MECHA_SNAPSHOT_HEADER_BYTES + MECHA_MAX_INSTANCES * MECHA_SNAPSHOT_ENTRY_BYTES
const PET_ENTITY_SPAWN_FRAME_BYTES := 76
const PET_ENTITY_DESPAWN_FRAME_BYTES := 56
const PET_ENTITY_DELTA_FRAME_BYTES := 64
const PET_ATTACK_FRAME_BYTES := 36
const PET_ATTACK_RESULT_FRAME_BYTES := 65
const PET_ATTACK_VISUAL_FRAME_BYTES := 81
const LEGACY_CHARACTER_APPEARANCE_BYTES := 4
const BODY_LEG_CHARACTER_APPEARANCE_BYTES := 6
const CHARACTER_APPEARANCE_BYTES := 7
const MAX_CHARACTER_CREATE_FRAME_BYTES := 137
const MAX_CHARACTER_CREATE_RESULT_FRAME_BYTES := 123
const MAX_AUTH_REQUEST_FRAME_BYTES := 300
const MAX_ACCOUNT_REGISTER_REQUEST_FRAME_BYTES := 300
const MAX_CREATION_APPEARANCE_CHOICES := 7000
const MAX_CHARACTER_LIST_FRAME_BYTES := (
	8+1+32*(11+96)+3+32*(1+CHARACTER_APPEARANCE_BYTES)+
	MAX_CREATION_APPEARANCE_CHOICES*CHARACTER_APPEARANCE_BYTES)
const WORLD_READY_FRAME_BYTES := 45
const ENTITY_STATE_FRAME_BYTES := 74
const ENTITY_DESPAWN_FRAME_BYTES := 40
const PLAYER_MOVEMENT_INTENT_FRAME_BYTES := 62
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
const INVENTORY_ITEM_BYTES := 29
const INVENTORY_SNAPSHOT_MAX_PER_FRAME := 128
const INVENTORY_SNAPSHOT_MAX_ITEMS := 4095
const SKILL_BOOK_SNAPSHOT_HEADER_BYTES := 23
const SKILL_BOOK_ENTRY_BYTES := 5
const SKILL_BOOK_SNAPSHOT_MAX_PER_FRAME := 256
const SKILL_BOOK_SNAPSHOT_MAX_SKILLS := 4095
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
const STORAGE_ITEM_BYTES := 29
const STORAGE_SNAPSHOT_MAX_PER_FRAME := 128
const STORAGE_SNAPSHOT_MAX_ITEMS := 4095
const STORAGE_TRANSFER_RESULT_FRAME_BYTES := 50
const EQUIPMENT_SNAPSHOT_HEADER_BYTES := 23
const EQUIPMENT_ITEM_BYTES := 29
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
const TRADE_OFFER_ITEM_BYTES := 29
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
const PLAYER_IDENTITY_MAX_FRAME_BYTES := PLAYER_IDENTITY_HEADER_BYTES + PLAYER_IDENTITY_MAX_NAME_BYTES + 1 + CHARACTER_APPEARANCE_BYTES
const PVP_COMBAT_VISUAL_FRAME_BYTES := 71
const PVP_SKILL_FRAME_BYTES := 32
const PVP_SKILL_RESULT_FRAME_BYTES := 46
const PVP_SKILL_VISUAL_FRAME_BYTES := 75
const GM_CAPABILITY_FRAME_BYTES := 9
const GM_PVP_NAME_COMMAND_FRAME_BYTES := 29
const GM_PVP_NAME_RESULT_FRAME_BYTES := 30
const PLAYER_SHOP_COMMAND_FRAME_BYTES := 53
const PLAYER_SHOP_RESULT_FRAME_BYTES := 91
const PLAYER_SHOP_SOLD_FRAME_BYTES := 48
const CHAT_COMMAND_HEADER_BYTES := 24
const CHAT_DELIVERY_HEADER_BYTES := 42
const CHAT_MAX_MESSAGE_BYTES := 256
const CHAT_MAX_SENDER_NAME_BYTES := 96
const CHAT_COMMAND_MAX_FRAME_BYTES := CHAT_COMMAND_HEADER_BYTES + CHAT_MAX_MESSAGE_BYTES
const CHAT_DELIVERY_MAX_FRAME_BYTES := CHAT_DELIVERY_HEADER_BYTES + CHAT_MAX_SENDER_NAME_BYTES + CHAT_MAX_MESSAGE_BYTES
const WHISPER_COMMAND_HEADER_BYTES := 31
const WHISPER_COMMAND_MAX_FRAME_BYTES := WHISPER_COMMAND_HEADER_BYTES + CHAT_MAX_MESSAGE_BYTES + 2 + CHAT_MAX_SENDER_NAME_BYTES
const WHISPER_RESULT_HEADER_BYTES := 39
const WHISPER_RESULT_MAX_FRAME_BYTES := WHISPER_RESULT_HEADER_BYTES + CHAT_MAX_SENDER_NAME_BYTES
const PARTY_COMMAND_FRAME_BYTES := 37
const PARTY_COMMAND_MAX_FRAME_BYTES := PARTY_COMMAND_FRAME_BYTES + CHAT_MAX_SENDER_NAME_BYTES
const PARTY_RESULT_FRAME_BYTES := 38
const PARTY_INVITE_HEADER_BYTES := 46
const PARTY_INVITE_MAX_FRAME_BYTES := PARTY_INVITE_HEADER_BYTES + CHAT_MAX_SENDER_NAME_BYTES
const PARTY_SNAPSHOT_HEADER_BYTES := 37
const PARTY_MEMBER_HEADER_BYTES := 18
const PARTY_MAX_MEMBERS := 8
const PARTY_SNAPSHOT_MAX_FRAME_BYTES := PARTY_SNAPSHOT_HEADER_BYTES + PARTY_MAX_MEMBERS * (PARTY_MEMBER_HEADER_BYTES + CHAT_MAX_SENDER_NAME_BYTES)
const MAX_MONEY := 9000000000000000

static func encode_move_target(map_epoch: int, movement_sequence: int, target_x: int, target_y: int) -> PackedByteArray:
	if map_epoch <= 0 or movement_sequence <= 0 or movement_sequence > 0xffffffff:
		return PackedByteArray()
	var writer := _writer(MOVE_TARGET_FRAME_BYTES, OPCODE_MOVE_TARGET)
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
	var writer := _writer(NPC_INTERACT_FRAME_BYTES, OPCODE_NPC_INTERACT)
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
	var writer := _writer(NORMAL_ATTACK_FRAME_BYTES, OPCODE_NORMAL_ATTACK)
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
	var writer := _writer(SKILL_USE_FRAME_BYTES, OPCODE_SKILL_USE)
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
	var writer := _writer(PLAYER_RESPAWN_FRAME_BYTES, OPCODE_PLAYER_RESPAWN)
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
	var writer := _writer(frame_bytes, OPCODE_AUTH_REQUEST)
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


static func encode_account_register_request(
		username:String,password:String)->PackedByteArray:
	var username_bytes:=username.to_utf8_buffer()
	var password_bytes:=password.to_utf8_buffer()
	if (
		username_bytes.size()<3 or username_bytes.size()>32 or
		password_bytes.size()<8 or password_bytes.size()>256
	):
		return PackedByteArray()
	var frame_bytes:=HEADER_BYTES+4+username_bytes.size()+password_bytes.size()
	if frame_bytes>MAX_ACCOUNT_REGISTER_REQUEST_FRAME_BYTES:
		return PackedByteArray()
	var writer := _writer(frame_bytes, OPCODE_ACCOUNT_REGISTER_REQUEST)
	writer.put_u16(username_bytes.size())
	writer.put_u16(password_bytes.size())
	writer.put_data(username_bytes)
	writer.put_data(password_bytes)
	return writer.data_array


static func decode_account_register_result(frame:PackedByteArray)->Dictionary:
	if not _valid_header(
		frame,OPCODE_ACCOUNT_REGISTER_RESULT,ACCOUNT_REGISTER_RESULT_FRAME_BYTES
	):
		return {}
	var status:=frame[8]
	if status>4:
		return {}
	return {"status":status}


static func valid_character_appearance(value: Dictionary) -> bool:
	if value.size() not in [3,5,6] or not value.has_all(["race","hair_index","hair_color"]):
		return false
	if value.size() >= 5 and not value.has_all(["body_index","leg_index"]):
		return false
	if value.size() == 6 and not value.has("sleeve_color"):
		return false
	for key in value:
		if not value[key] is int:
			return false
	var body := int(value.get("body_index",0))
	var leg := int(value.get("leg_index",0))
	var sleeve := int(value.get("sleeve_color",0))
	return (
		value.race >= 0 and value.race <= 4 and
		value.hair_index >= 0 and value.hair_index <= 65535 and
		value.hair_color >= 0 and value.hair_color <= 7 and
		body >= 0 and body <= 255 and leg >= 0 and leg <= 255 and
		sleeve >= 0 and sleeve <= 255)

static func _read_character_appearance(
		reader: StreamPeerBuffer, appearance_bytes: int) -> Dictionary:
	if appearance_bytes not in [
			LEGACY_CHARACTER_APPEARANCE_BYTES,
			BODY_LEG_CHARACTER_APPEARANCE_BYTES,
			CHARACTER_APPEARANCE_BYTES] or reader.get_available_bytes() < appearance_bytes:
		return {}
	var value := {
		"race":reader.get_u8(),
		"hair_index":reader.get_u16(),
		"hair_color":reader.get_u8()
	}
	if appearance_bytes >= BODY_LEG_CHARACTER_APPEARANCE_BYTES:
		value.body_index = reader.get_u8()
		value.leg_index = reader.get_u8()
	if appearance_bytes == CHARACTER_APPEARANCE_BYTES:
		value.sleeve_color = reader.get_u8()
	return value if valid_character_appearance(value) else {}

static func _write_character_appearance(
		writer: StreamPeerBuffer, value: Dictionary, appearance_bytes: int) -> void:
	writer.put_u8(int(value.race))
	writer.put_u16(int(value.hair_index))
	writer.put_u8(int(value.hair_color))
	if appearance_bytes >= BODY_LEG_CHARACTER_APPEARANCE_BYTES:
		writer.put_u8(int(value.get("body_index",0)))
		writer.put_u8(int(value.get("leg_index",0)))
	if appearance_bytes == CHARACTER_APPEARANCE_BYTES:
		writer.put_u8(int(value.get("sleeve_color",0)))

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
	if reader.get_available_bytes() == 0:
		return {"characters": characters}
	if reader.get_available_bytes() < 3:
		return {}
	var appearance_version := reader.get_u8()
	var appearance_bytes := (
		LEGACY_CHARACTER_APPEARANCE_BYTES if appearance_version == 1
		else BODY_LEG_CHARACTER_APPEARANCE_BYTES if appearance_version == 2
		else CHARACTER_APPEARANCE_BYTES if appearance_version == 3
		else 0)
	if appearance_bytes == 0 or 			reader.get_available_bytes() < count*(1+appearance_bytes)+2:
		return {}
	for i in count:
		var known := reader.get_u8()
		var appearance := _read_character_appearance(reader,appearance_bytes)
		var zero_appearance := {
			"race":0,"hair_index":0,"hair_color":0
		}
		if appearance_bytes >= BODY_LEG_CHARACTER_APPEARANCE_BYTES:
			zero_appearance.body_index = 0
			zero_appearance.leg_index = 0
		if appearance_bytes == CHARACTER_APPEARANCE_BYTES:
			zero_appearance.sleeve_color = 0
		if known > 1 or appearance.is_empty() or 				(known == 0 and appearance != zero_appearance):
			return {}
		characters[i].appearance = appearance if known == 1 else {}
	var choice_count := reader.get_u16()
	if choice_count > MAX_CREATION_APPEARANCE_CHOICES or 			reader.get_available_bytes() != choice_count*appearance_bytes:
		return {}
	var choices: Array[Dictionary] = []
	var seen_choices := {}
	for _i in choice_count:
		var appearance := _read_character_appearance(reader,appearance_bytes)
		if appearance.is_empty():
			return {}
		var key := "%d:%d:%d:%d:%d:%d" % [
			int(appearance.race),int(appearance.hair_index),
			int(appearance.hair_color),int(appearance.get("body_index",0)),
			int(appearance.get("leg_index",0)),int(appearance.get("sleeve_color",0))]
		if seen_choices.has(key):
			return {}
		seen_choices[key] = true
		choices.append(appearance)
	return {"characters": characters,"creation_appearances":choices}


static func valid_character_creation_settings(value: Dictionary) -> bool:
	if value.size() != 5 or not value.has_all(["strength","dexterity","constitution","wisdom","password"]):
		return false
	for key in ["strength","dexterity","constitution","wisdom"]:
		if not value[key] is int or int(value[key]) < 1 or int(value[key]) > 5:
			return false
	if int(value.strength)+int(value.dexterity)+int(value.constitution)+int(value.wisdom) != 8:
		return false
	if not value.password is String:
		return false
	var password: String = value.password
	var bytes := password.to_utf8_buffer()
	if password.length() != 4 or password != password.strip_edges() or bytes.size() < 1 or bytes.size() > 16:
		return false
	for i in password.length():
		var code := password.unicode_at(i)
		if code < 32 or (code >= 127 and code <= 159):
			return false
	return true


static func encode_character_create(
		slot: int, character_name: String, appearance: Dictionary = {},
		creation: Dictionary = {}) -> PackedByteArray:
	var name_bytes := character_name.to_utf8_buffer()
	if slot < 0 or slot > 31 or name_bytes.size() < 1 or name_bytes.size() > 96:
		return PackedByteArray()
	if not appearance.is_empty() and not valid_character_appearance(appearance):
		return PackedByteArray()
	if not creation.is_empty() and not valid_character_creation_settings(creation):
		return PackedByteArray()
	var password_bytes := PackedByteArray()
	var appearance_bytes := (
		CHARACTER_APPEARANCE_BYTES
		if appearance.has_all(["body_index","leg_index","sleeve_color"])
		else BODY_LEG_CHARACTER_APPEARANCE_BYTES
		if appearance.has_all(["body_index","leg_index"])
		else LEGACY_CHARACTER_APPEARANCE_BYTES)
	var suffix_bytes := 0
	if not creation.is_empty():
		password_bytes = str(creation.password).to_utf8_buffer()
		suffix_bytes = 7 + appearance_bytes + password_bytes.size()
	elif not appearance.is_empty():
		suffix_bytes = appearance_bytes
	var frame_bytes := HEADER_BYTES + 3 + name_bytes.size() + suffix_bytes
	var writer := _writer(frame_bytes, OPCODE_CHARACTER_CREATE)
	writer.put_u8(slot)
	writer.put_u16(name_bytes.size())
	writer.put_data(name_bytes)
	if not creation.is_empty():
		writer.put_u8(
			4 if appearance_bytes == CHARACTER_APPEARANCE_BYTES
			else 3 if appearance_bytes == BODY_LEG_CHARACTER_APPEARANCE_BYTES
			else 2)
		writer.put_u8(0 if appearance.is_empty() else 1)
		if appearance.is_empty():
			for _i in appearance_bytes:
				writer.put_u8(0)
		else:
			_write_character_appearance(writer,appearance,appearance_bytes)
		writer.put_u8(int(creation.strength))
		writer.put_u8(int(creation.dexterity))
		writer.put_u8(int(creation.constitution))
		writer.put_u8(int(creation.wisdom))
		writer.put_u8(password_bytes.size())
		writer.put_data(password_bytes)
		password_bytes.fill(0)
	elif not appearance.is_empty():
		_write_character_appearance(writer,appearance,appearance_bytes)
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
	var remaining_before_name := reader.get_available_bytes()
	if status > 8 or slot > 31 or name_bytes_count > 96 or (
			remaining_before_name != name_bytes_count and
			remaining_before_name != name_bytes_count+LEGACY_CHARACTER_APPEARANCE_BYTES and
			remaining_before_name != name_bytes_count+BODY_LEG_CHARACTER_APPEARANCE_BYTES and
			remaining_before_name != name_bytes_count+CHARACTER_APPEARANCE_BYTES):
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
	var appearance := {}
	if reader.get_available_bytes() > 0:
		if status != 0:
			return {}
		var appearance_bytes := reader.get_available_bytes()
		if appearance_bytes not in [
				LEGACY_CHARACTER_APPEARANCE_BYTES,
				BODY_LEG_CHARACTER_APPEARANCE_BYTES,
				CHARACTER_APPEARANCE_BYTES]:
			return {}
		appearance = _read_character_appearance(reader,appearance_bytes)
		if appearance.is_empty():
			return {}
	var value := {
		"status": status,
		"character_id": character_id,
		"slot": slot,
		"name": character_name,
	}
	if not appearance.is_empty():
		value.appearance = appearance
	return value

static func encode_character_choose(character_id: int, character_password: String = "") -> PackedByteArray:
	if character_id <= 0:
		return PackedByteArray()
	var proof := character_password.to_utf8_buffer()
	if not character_password.is_empty():
		if character_password.length() != 4 or character_password != character_password.strip_edges() or proof.size() > 16:
			return PackedByteArray()
		for i in character_password.length():
			var code := character_password.unicode_at(i)
			if code < 32 or (code >= 127 and code <= 159):
				return PackedByteArray()
	var writer := _writer(CHARACTER_CHOOSE_FRAME_BYTES + (0 if proof.is_empty() else 2 + proof.size()), OPCODE_CHARACTER_CHOOSE)
	writer.put_64(character_id)
	if not proof.is_empty():
		writer.put_u8(1)
		writer.put_u8(proof.size())
		writer.put_data(proof)
	proof.fill(0)
	return writer.data_array

static func _valid_character_secret(password: String) -> bool:
	if password.length() != 4 or password != password.strip_edges() or password.to_utf8_buffer().size() > 16:
		return false
	for i in password.length():
		var code := password.unicode_at(i)
		if code < 32 or (code >= 127 and code <= 159):
			return false
	return true

static func encode_character_manage(action: int, character_id: int, old_password: String, new_password: String = "") -> PackedByteArray:
	if action not in [1,2] or character_id <= 0:
		return PackedByteArray()
	if (not old_password.is_empty() and not _valid_character_secret(old_password)) or (action == 1 and not _valid_character_secret(new_password)) or (action == 2 and (old_password.is_empty() or not new_password.is_empty())):
		return PackedByteArray()
	var old_bytes := old_password.to_utf8_buffer()
	var new_bytes := new_password.to_utf8_buffer()
	var writer := _writer(19 + old_bytes.size() + new_bytes.size(), OPCODE_CHARACTER_MANAGE)
	writer.put_u8(action)
	writer.put_64(character_id)
	writer.put_u8(old_bytes.size())
	writer.put_u8(new_bytes.size())
	writer.put_data(old_bytes)
	writer.put_data(new_bytes)
	old_bytes.fill(0)
	new_bytes.fill(0)
	return writer.data_array

static func decode_character_manage_result(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_CHARACTER_MANAGE_RESULT, CHARACTER_MANAGE_RESULT_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var action := reader.get_u8()
	var status := reader.get_u8()
	var character_id := reader.get_64()
	if action not in [1,2] or status > 6 or character_id <= 0:
		return {}
	return {"action":action,"status":status,"character_id":character_id}

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
	if status > 5:
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
	var writer := _writer(WORLD_READY_ACK_FRAME_BYTES, OPCODE_WORLD_READY_ACK)
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
	var writer := _writer(WORLD_RESYNC_REQUEST_FRAME_BYTES, OPCODE_WORLD_RESYNC_REQUEST)
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

static func decode_player_movement_intent(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_PLAYER_MOVEMENT_INTENT, PLAYER_MOVEMENT_INTENT_FRAME_BYTES):
		return {}
	var reader := _reader(frame, 8)
	var receiver_epoch := reader.get_64()
	var tick := reader.get_64()
	var actor_id := reader.get_64()
	var connection_epoch := reader.get_64()
	var entity_map_epoch := reader.get_64()
	var action_sequence := reader.get_u32()
	var target_x := reader.get_float()
	var target_y := reader.get_float()
	var moving := reader.get_u8()
	var intent_facing := reader.get_u8()
	if receiver_epoch <= 0 or tick < 0 or actor_id <= 0 or connection_epoch <= 0 or entity_map_epoch <= 0 or action_sequence <= 0 or moving > 1 or intent_facing < 1 or intent_facing > 8 or not is_finite(target_x) or not is_finite(target_y) or target_x < 0 or target_y < 0:
		return {}
	return {"receiver_map_epoch":receiver_epoch,"server_tick":tick,"actor_id":actor_id,
		"connection_epoch":connection_epoch,"entity_map_epoch":entity_map_epoch,
		"movement_action_sequence":action_sequence,"target_x":target_x,"target_y":target_y,
		"moving":moving == 1,"facing":intent_facing}

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
	var writer := _writer(RECOVERY_USE_FRAME_BYTES, OPCODE_RECOVERY_USE)
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
	var writer := _writer(INVENTORY_DISCARD_FRAME_BYTES, OPCODE_INVENTORY_DISCARD)
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
	var writer := _writer(GROUND_PICKUP_FRAME_BYTES, OPCODE_GROUND_PICKUP)
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
		var enhancement_level := reader.get_u8()
		var durability_raw := reader.get_32()
		if uid <= 0 or item_id < 1 or item_id > 100000000 or quantity < 1 or quantity > 1000000 or drop_id < 0 or enhancement_level < 0 or enhancement_level > 255 or durability_raw < -1 or ids.has(uid):
			return {}
		ids[uid] = true
		items.append({"item_uid":uid,"item_definition_id":item_id,
			"quantity":quantity,"drop_id":drop_id,
			"enhancement_level":enhancement_level,"durability_raw":durability_raw})
	return {"character_id":character,"start_index":start,
		"complete":final_chunk==1,"items":items}


static func decode_skill_book_snapshot(frame: PackedByteArray) -> Dictionary:
	if not _valid_variable_header(
			frame,OPCODE_SKILL_BOOK_SNAPSHOT,SKILL_BOOK_SNAPSHOT_HEADER_BYTES,
			SKILL_BOOK_SNAPSHOT_HEADER_BYTES+
				SKILL_BOOK_SNAPSHOT_MAX_PER_FRAME*SKILL_BOOK_ENTRY_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var character:=reader.get_64()
	var start:=reader.get_32()
	var final_chunk:=reader.get_u8()
	var count:=reader.get_u16()
	if (
		character<=0 or start<0 or start>SKILL_BOOK_SNAPSHOT_MAX_SKILLS or
		final_chunk>1 or count>SKILL_BOOK_SNAPSHOT_MAX_PER_FRAME or
		start+count>SKILL_BOOK_SNAPSHOT_MAX_SKILLS or
		frame.size()!=SKILL_BOOK_SNAPSHOT_HEADER_BYTES+count*SKILL_BOOK_ENTRY_BYTES
	):
		return {}
	var skills:Array[Dictionary]=[]
	var ids:={}
	for _index in count:
		var skill_id:=reader.get_32()
		var level:=reader.get_u8()
		if skill_id<1 or skill_id>65535 or level<1 or level>255 or ids.has(skill_id):
			return {}
		ids[skill_id]=true
		skills.append({"skill_id":skill_id,"level":level})
	return {
		"character_id":character,"start_index":start,
		"complete":final_chunk==1,"skills":skills
	}


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
	var writer := _writer(SHOP_OPEN_FRAME_BYTES, OPCODE_SHOP_OPEN)
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
	var writer := _writer(SHOP_BUY_FRAME_BYTES, OPCODE_SHOP_BUY)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_32(npc_spawn_id)
	writer.put_32(item_definition_id)
	writer.put_32(quantity)
	return writer.data_array


static func encode_shop_sell(epoch: int, sequence: int, npc_spawn_id: int, item_uid: int, item_definition_id: int, quantity: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or npc_spawn_id <= 0 or item_uid <= 0 or item_definition_id < 1 or item_definition_id > 100000000 or quantity < 1 or quantity > 1000000:
		return PackedByteArray()
	var writer := _writer(SHOP_SELL_FRAME_BYTES, OPCODE_SHOP_SELL)
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
	var writer := _writer(STORAGE_OPEN_FRAME_BYTES, OPCODE_STORAGE_OPEN)
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
	var writer := _writer(STORAGE_MOVE_FRAME_BYTES, opcode)
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
		var enhancement_level := reader.get_u8()
		var durability_raw := reader.get_32()
		if uid <= 0 or item_id < 1 or item_id > 100000000 or quantity < 1 or quantity > 1000000 or source_drop_id < 0 or enhancement_level < 0 or enhancement_level > 255 or durability_raw < -1 or uids.has(uid):
			return {}
		uids[uid] = true
		items.append({"item_uid":uid,"item_definition_id":item_id,
			"quantity":quantity,"drop_id":source_drop_id,
			"enhancement_level":enhancement_level,"durability_raw":durability_raw})
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
	var writer := _writer(BANK_OPEN_FRAME_BYTES, OPCODE_BANK_OPEN)
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
	var writer := _writer(BANK_MOVE_FRAME_BYTES, opcode)
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
	var writer := _writer(TRADE_COMMAND_FRAME_BYTES, OPCODE_TRADE_COMMAND)
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
		var enhancement_level := reader.get_u8()
		var durability_raw := reader.get_32()
		if uid <= 0 or definition < 1 or definition > 100000000 or quantity < 1 or quantity > 1000000 or source_drop_id < 0 or enhancement_level < 0 or enhancement_level > 255 or durability_raw < -1 or seen.has(uid):
			return {}
		seen[uid] = true
		var item := {"item_uid":uid,"item_definition_id":definition,
			"quantity":quantity,"drop_id":source_drop_id,
			"enhancement_level":enhancement_level,
			"durability_raw":durability_raw}
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
	var writer := _writer(REPAIR_OPEN_FRAME_BYTES, OPCODE_REPAIR_OPEN)
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
	var writer := _writer(REPAIR_ITEM_FRAME_BYTES, OPCODE_REPAIR_ITEM)
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
	var writer := _writer(ENHANCEMENT_OPEN_FRAME_BYTES, OPCODE_ENHANCEMENT_OPEN)
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
	var writer := _writer(ENHANCEMENT_ITEM_FRAME_BYTES, OPCODE_ENHANCEMENT_ITEM)
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
	var writer := _writer(EQUIPMENT_MOVE_FRAME_BYTES, opcode)
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
		var enhancement_level := reader.get_u8()
		var durability_raw := reader.get_32()
		if uid <= 0 or slot_id < 1 or slot_id > 255 or item_id < 1 or item_id > 100000000 or source_drop_id < 0 or enhancement_level < 0 or enhancement_level > 255 or durability_raw < -1 or uids.has(uid) or slots.has(slot_id):
			return {}
		uids[uid] = true
		slots[slot_id] = true
		items.append({"item_uid":uid,"slot_id":slot_id,
			"item_definition_id":item_id,"drop_id":source_drop_id,
			"enhancement_level":enhancement_level,"durability_raw":durability_raw})
	return {"character_id":character_id,"start_index":start_index,"complete":complete_raw==1,"items":items}

static func encode_pvp_mode_command(epoch: int, sequence: int, action: int) -> PackedByteArray:
	if epoch <= 0 or sequence < 1 or sequence > 0xffffffff or action < 0 or action > 2:
		return PackedByteArray()
	var writer := _writer(PVP_MODE_COMMAND_FRAME_BYTES, OPCODE_PVP_MODE_COMMAND)
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
	var writer := _writer(PVP_ATTACK_FRAME_BYTES, OPCODE_PVP_ATTACK)
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
	var writer := _writer(PVP_SKILL_FRAME_BYTES, OPCODE_PVP_SKILL)
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
	var writer := _writer(GM_PVP_NAME_COMMAND_FRAME_BYTES, OPCODE_GM_PVP_NAME_COMMAND)
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
	var writer := _writer(PLAYER_SHOP_COMMAND_FRAME_BYTES, OPCODE_PLAYER_SHOP_COMMAND)
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
	var enhancement_level:=reader.get_u8()
	if (
		epoch<=0 or sequence==0 or action<1 or action>4 or status>15 or
		seller<0 or listing<0 or uid<0 or item_id<0 or item_id>100000000 or
		quantity<0 or quantity>1000000 or source_drop<0 or durability_raw<-1 or
		enhancement_level<0 or enhancement_level>255 or
		price<0 or price>MAX_MONEY or seller_wallet<0 or seller_wallet>MAX_MONEY or
		buyer_wallet<0 or buyer_wallet>MAX_MONEY
	):
		return {}
	return {
		"map_epoch":epoch,"shop_sequence":sequence,"action":action,"status":status,
		"seller_actor_id":seller,"listing_id":listing,"item_uid":uid,
		"item_definition_id":item_id,"quantity":quantity,
		"source_drop_id":source_drop,"durability_raw":durability_raw,
		"price":price,"seller_wallet":seller_wallet,"buyer_wallet":buyer_wallet,
		"enhancement_level":enhancement_level}


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
	var writer := _writer(QUEST_OPEN_FRAME_BYTES, OPCODE_QUEST_OPEN)
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
	var writer := _writer(QUEST_CHOOSE_FRAME_BYTES, OPCODE_QUEST_CHOOSE)
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
	var writer := _writer(CHAT_COMMAND_HEADER_BYTES+bytes.size(), OPCODE_CHAT_COMMAND)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(channel)
	writer.put_u8(bubble_type)
	writer.put_u16(bytes.size())
	writer.put_data(bytes)
	return writer.data_array


static func encode_whisper_command(
		epoch:int,sequence:int,target_actor_id:int,bubble_type:int,
		message:String,target_name:String="")->PackedByteArray:
	var bytes:=message.to_utf8_buffer()
	var name_bytes:=target_name.to_utf8_buffer()
	var by_name:=not target_name.is_empty()
	if (
		epoch<=0 or sequence<=0 or sequence>0xffffffff or target_actor_id<0 or
		(target_actor_id>0)==by_name or bubble_type<0 or bubble_type>5 or
		bytes.size()<1 or bytes.size()>CHAT_MAX_MESSAGE_BYTES or
		message.strip_edges().is_empty() or
		name_bytes.size()>CHAT_MAX_SENDER_NAME_BYTES or
		(by_name and (
			target_name!=target_name.strip_edges() or
			target_name.strip_edges().is_empty()
		))
	):
		return PackedByteArray()
	var extra:=2+name_bytes.size() if by_name else 0
	var writer := _writer(WHISPER_COMMAND_HEADER_BYTES+bytes.size()+extra, OPCODE_WHISPER_COMMAND)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_64(target_actor_id)
	writer.put_u8(bubble_type)
	writer.put_u16(bytes.size())
	writer.put_data(bytes)
	if by_name:
		writer.put_u16(name_bytes.size())
		writer.put_data(name_bytes)
	return writer.data_array


static func decode_whisper_result(frame:PackedByteArray)->Dictionary:
	if not _valid_variable_header(
		frame,OPCODE_WHISPER_RESULT,WHISPER_RESULT_HEADER_BYTES,
		WHISPER_RESULT_MAX_FRAME_BYTES
	):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var sequence:=reader.get_u32()
	var status:=reader.get_u8()
	var target:=reader.get_64()
	var target_connection:=reader.get_64()
	var name_count:=reader.get_u16()
	if (
		epoch<=0 or sequence==0 or status>2 or target<0 or
		name_count>CHAT_MAX_SENDER_NAME_BYTES or
		reader.get_available_bytes()!=name_count
	):
		return {}
	var target_name:=""
	if name_count>0:
		var result:=reader.get_data(name_count)
		if result[0]!=OK:
			return {}
		var bytes:PackedByteArray=result[1]
		target_name=bytes.get_string_from_utf8()
		if (
			target_name.to_utf8_buffer()!=bytes or
			target_name.strip_edges().is_empty()
		):
			return {}
	if status==0:
		if target<=0 or target_connection<=0 or name_count==0:
			return {}
	elif status==2:
		if target<=0 or target_connection!=0 or name_count!=0:
			return {}
	elif target_connection!=0 or name_count!=0:
		return {}
	return {
		"map_epoch":epoch,"chat_sequence":sequence,"status":status,
		"target_actor_id":target,
		"target_connection_epoch":target_connection,
		"target_name":target_name
	}


static func encode_clan_command(
		epoch:int,sequence:int,action:int,clan_id:int,name:String="",target_actor_id:int=0)->PackedByteArray:
	var payload:=name.to_utf8_buffer()
	if epoch<=0 or sequence<=0 or sequence>0xffffffff or action<1 or action>7 or clan_id<0 or target_actor_id<0:
		return PackedByteArray()
	match action:
		1:
			if clan_id!=0 or target_actor_id!=0 or name!=name.strip_edges() or name.length()<2 or name.length()>24 or payload.is_empty() or payload.size()>CLAN_MAX_NAME_BYTES:
				return PackedByteArray()
		2:
			if clan_id<=0 or target_actor_id!=0 or not name.is_empty():
				return PackedByteArray()
		3,4:
			if clan_id!=0 or target_actor_id!=0 or not name.is_empty():
				return PackedByteArray()
		5:
			if clan_id!=0 or target_actor_id!=0 or name!=name.strip_edges() or payload.size()>CLAN_MAX_ANNOUNCEMENT_BYTES:
				return PackedByteArray()
		6,7:
			if clan_id!=0 or target_actor_id<=0 or not name.is_empty():
				return PackedByteArray()
		_:
			return PackedByteArray()
	var writer:=_writer(CLAN_COMMAND_HEADER_BYTES+payload.size(),OPCODE_CLAN_COMMAND)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(action)
	writer.put_64(clan_id)
	writer.put_64(target_actor_id)
	writer.put_u16(payload.size())
	writer.put_data(payload)
	return writer.data_array

static func decode_clan_result(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_CLAN_RESULT,CLAN_RESULT_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var sequence:=reader.get_u32()
	var action:=reader.get_u8()
	var status:=reader.get_u8()
	var clan_id:=reader.get_64()
	if epoch<=0 or sequence<=0 or action<1 or action>7 or status<0 or status>11 or clan_id<0 or reader.get_available_bytes()!=0:
		return {}
	return {"map_epoch":epoch,"clan_sequence":sequence,"action":action,"status":status,"clan_id":clan_id}

static func decode_clan_snapshot(frame:PackedByteArray)->Dictionary:
	if not _valid_variable_header(frame,OPCODE_CLAN_SNAPSHOT,CLAN_SNAPSHOT_HEADER_BYTES,CLAN_SNAPSHOT_MAX_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var clan_id:=reader.get_64()
	var revision:=reader.get_u32()
	var leader:=reader.get_64()
	var name_count:=reader.get_u16()
	var announcement_count:=reader.get_u16()
	var member_count:=reader.get_u8()
	if epoch<=0 or clan_id<0 or name_count>CLAN_MAX_NAME_BYTES or announcement_count>CLAN_MAX_ANNOUNCEMENT_BYTES or member_count>CLAN_MAX_MEMBERS or reader.get_available_bytes()<name_count+announcement_count:
		return {}
	if clan_id==0:
		if revision!=0 or leader!=0 or name_count!=0 or announcement_count!=0 or member_count!=0 or reader.get_available_bytes()!=0:
			return {}
		return {"receiver_map_epoch":epoch,"clan_id":0,"revision":0,"leader_actor_id":0,"name":"","announcement":"","members":[]}
	if revision<=0 or leader<=0 or name_count<1:
		return {}
	var name_result:=reader.get_data(name_count)
	if name_result[0]!=OK:
		return {}
	var name_bytes:PackedByteArray=name_result[1]
	var clan_name:=name_bytes.get_string_from_utf8()
	if clan_name.to_utf8_buffer()!=name_bytes or clan_name.strip_edges().is_empty():
		return {}
	var announcement_result:=reader.get_data(announcement_count)
	if announcement_result[0]!=OK:
		return {}
	var announcement_bytes:PackedByteArray=announcement_result[1]
	var announcement:=announcement_bytes.get_string_from_utf8()
	if announcement.to_utf8_buffer()!=announcement_bytes or announcement!=announcement.strip_edges():
		return {}
	var members:Array[Dictionary]=[]
	var seen:={}
	var leaders:=0
	for _i in member_count:
		if reader.get_available_bytes()<CLAN_MEMBER_HEADER_BYTES:
			return {}
		var actor:=reader.get_64()
		var role:=reader.get_u8()
		var member_name_count:=reader.get_u16()
		if actor<=0 or role<0 or role>1 or member_name_count<1 or member_name_count>PLAYER_IDENTITY_MAX_NAME_BYTES or reader.get_available_bytes()<member_name_count or seen.has(actor):
			return {}
		var member_result:=reader.get_data(member_name_count)
		if member_result[0]!=OK:
			return {}
		var member_bytes:PackedByteArray=member_result[1]
		var member_name:=member_bytes.get_string_from_utf8()
		if member_name.to_utf8_buffer()!=member_bytes or member_name.strip_edges().is_empty():
			return {}
		seen[actor]=true
		if role==1:
			leaders+=1
		members.append({"actor_id":actor,"role":role,"name":member_name})
	if reader.get_available_bytes()!=0 or not seen.has(leader) or leaders!=1:
		return {}
	return {"receiver_map_epoch":epoch,"clan_id":clan_id,"revision":revision,"leader_actor_id":leader,"name":clan_name,"announcement":announcement,"members":members}

static func encode_friend_command(
		epoch:int,sequence:int,action:int,target_actor_id:int=0,target_name:String="")->PackedByteArray:
	var payload:=target_name.to_utf8_buffer()
	if epoch<=0 or sequence<=0 or sequence>0xffffffff or action<1 or action>3 or target_actor_id<0 or payload.size()>PLAYER_IDENTITY_MAX_NAME_BYTES:
		return PackedByteArray()
	match action:
		1:
			if (target_actor_id>0)==(not target_name.is_empty()) or target_name!=target_name.strip_edges():
				return PackedByteArray()
		2:
			if target_actor_id<=0 or not target_name.is_empty():
				return PackedByteArray()
		3:
			if target_actor_id!=0 or not target_name.is_empty():
				return PackedByteArray()
		_:
			return PackedByteArray()
	var writer:=_writer(FRIEND_COMMAND_HEADER_BYTES+payload.size(),OPCODE_FRIEND_COMMAND)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(action)
	writer.put_64(target_actor_id)
	writer.put_u16(payload.size())
	writer.put_data(payload)
	return writer.data_array


static func decode_friend_result(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_FRIEND_RESULT,FRIEND_RESULT_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var sequence:=reader.get_u32()
	var action:=reader.get_u8()
	var status:=reader.get_u8()
	var target:=reader.get_64()
	if epoch<=0 or sequence<=0 or action<1 or action>3 or status<0 or status>5 or target<0 or reader.get_available_bytes()!=0:
		return {}
	return {"map_epoch":epoch,"friend_sequence":sequence,"action":action,"status":status,"target_actor_id":target}


static func decode_friend_snapshot(frame:PackedByteArray)->Dictionary:
	if not _valid_variable_header(frame,OPCODE_FRIEND_SNAPSHOT,FRIEND_SNAPSHOT_HEADER_BYTES,MAX_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var count:=reader.get_u16()
	if epoch<=0:
		return {}
	var friends:Array[Dictionary]=[]
	var seen:={}
	for _i in count:
		if reader.get_available_bytes()<FRIEND_ENTRY_HEADER_BYTES:
			return {}
		var actor:=reader.get_64()
		var online:=reader.get_u8()
		var name_count:=reader.get_u16()
		if actor<=0 or online>1 or name_count<1 or name_count>PLAYER_IDENTITY_MAX_NAME_BYTES or reader.get_available_bytes()<name_count or seen.has(actor):
			return {}
		var name_result:=reader.get_data(name_count)
		if name_result[0]!=OK:
			return {}
		var name_bytes:PackedByteArray=name_result[1]
		var friend_name:=name_bytes.get_string_from_utf8()
		if friend_name.to_utf8_buffer()!=name_bytes or friend_name.strip_edges().is_empty():
			return {}
		seen[actor]=true
		friends.append({"actor_id":actor,"name":friend_name,"online":online==1})
	if reader.get_available_bytes()!=0:
		return {}
	return {"receiver_map_epoch":epoch,"friends":friends}


static func encode_pet_command(epoch:int,sequence:int,action:int,pet_uid:int)->PackedByteArray:
	if epoch<=0 or sequence<=0 or sequence>0xffffffff or action<1 or action>3:
		return PackedByteArray()
	if action==3:
		if pet_uid!=0:
			return PackedByteArray()
	elif pet_uid<=0:
		return PackedByteArray()
	var writer:=_writer(PET_COMMAND_FRAME_BYTES,OPCODE_PET_COMMAND)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(action)
	writer.put_64(pet_uid)
	return writer.data_array


static func decode_pet_result(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_PET_RESULT,PET_RESULT_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var sequence:=reader.get_u32()
	var action:=reader.get_u8()
	var status:=reader.get_u8()
	var pet_uid:=reader.get_64()
	var active_uid:=reader.get_64()
	if (
		epoch<=0 or sequence<=0 or action<1 or action>3 or
		status<0 or status>5 or active_uid<0 or reader.get_available_bytes()!=0
	):
		return {}
	if action==3:
		if pet_uid!=0:
			return {}
	elif pet_uid<=0:
		return {}
	return {
		"map_epoch":epoch,"pet_sequence":sequence,"action":action,
		"status":status,"pet_uid":pet_uid,"active_pet_uid":active_uid
	}


static func decode_pet_snapshot(frame:PackedByteArray)->Dictionary:
	if not _valid_variable_header(
		frame,OPCODE_PET_SNAPSHOT,PET_SNAPSHOT_HEADER_BYTES,
		PET_SNAPSHOT_MAX_FRAME_BYTES
	):
		return {}
	if (
		frame.size()<PET_SNAPSHOT_HEADER_BYTES or
		(frame.size()-PET_SNAPSHOT_HEADER_BYTES)%PET_SNAPSHOT_ENTRY_BYTES!=0
	):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var active_uid:=reader.get_64()
	var count:=reader.get_u16()
	if (
		epoch<=0 or active_uid<0 or count>PET_MAX_INSTANCES or
		frame.size()!=PET_SNAPSHOT_HEADER_BYTES+count*PET_SNAPSHOT_ENTRY_BYTES
	):
		return {}
	var pets:Array[Dictionary]=[]
	var seen:={}
	for _i in count:
		var uid:=reader.get_64()
		var definition_id:=reader.get_32()
		var evolution_id:=reader.get_32()
		var level:=reader.get_32()
		var experience:=reader.get_64()
		if (
			uid<=0 or definition_id<=0 or definition_id>100000000 or
			evolution_id<0 or evolution_id>65535 or level<=0 or level>1000000 or experience<0 or
			experience>9000000000000000 or seen.has(uid)
		):
			return {}
		seen[uid]=true
		pets.append({
			"pet_uid":uid,"definition_id":definition_id,"evolution_id":evolution_id,
			"level":level,"experience":experience
		})
	if reader.get_available_bytes()!=0 or (active_uid>0 and not seen.has(active_uid)):
		return {}
	return {
		"receiver_map_epoch":epoch,
		"active_pet_uid":active_uid,
		"pets":pets
	}


static func encode_mecha_command(epoch:int,sequence:int,action:int,mecha_uid:int)->PackedByteArray:
	if epoch<=0 or sequence<=0 or sequence>0xffffffff or action<1 or action>3:
		return PackedByteArray()
	if action==3:
		if mecha_uid!=0:
			return PackedByteArray()
	elif mecha_uid<=0:
		return PackedByteArray()
	var writer:=_writer(MECHA_COMMAND_FRAME_BYTES,OPCODE_MECHA_COMMAND)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(action)
	writer.put_64(mecha_uid)
	return writer.data_array


static func decode_mecha_result(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_MECHA_RESULT,MECHA_RESULT_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var sequence:=reader.get_u32()
	var action:=reader.get_u8()
	var status:=reader.get_u8()
	var mecha_uid:=reader.get_64()
	var mounted_uid:=reader.get_64()
	if (
		epoch<=0 or sequence<=0 or action<1 or action>3 or
		status<0 or status>5 or mounted_uid<0 or reader.get_available_bytes()!=0
	):
		return {}
	if action==3:
		if mecha_uid!=0:
			return {}
	elif mecha_uid<=0:
		return {}
	return {
		"map_epoch":epoch,"mecha_sequence":sequence,"action":action,
		"status":status,"mecha_uid":mecha_uid,"mounted_mecha_uid":mounted_uid
	}


static func decode_mecha_snapshot(frame:PackedByteArray)->Dictionary:
	if not _valid_variable_header(
		frame,OPCODE_MECHA_SNAPSHOT,MECHA_SNAPSHOT_HEADER_BYTES,
		MECHA_SNAPSHOT_MAX_FRAME_BYTES
	):
		return {}
	if (
		frame.size()<MECHA_SNAPSHOT_HEADER_BYTES or
		(frame.size()-MECHA_SNAPSHOT_HEADER_BYTES)%MECHA_SNAPSHOT_ENTRY_BYTES!=0
	):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var mounted_uid:=reader.get_64()
	var count:=reader.get_u16()
	if (
		epoch<=0 or mounted_uid<0 or count>MECHA_MAX_INSTANCES or
		frame.size()!=MECHA_SNAPSHOT_HEADER_BYTES+count*MECHA_SNAPSHOT_ENTRY_BYTES
	):
		return {}
	var mechas:Array[Dictionary]=[]
	var seen:={}
	for _i in count:
		var uid:=reader.get_64()
		var definition_id:=reader.get_32()
		var level:=reader.get_32()
		var experience:=reader.get_64()
		if (
			uid<=0 or definition_id<=0 or definition_id>100000000 or
			level<=0 or level>1000000 or experience<0 or
			experience>9000000000000000 or seen.has(uid)
		):
			return {}
		seen[uid]=true
		mechas.append({
			"mecha_uid":uid,"definition_id":definition_id,
			"level":level,"experience":experience
		})
	if reader.get_available_bytes()!=0 or (mounted_uid>0 and not seen.has(mounted_uid)):
		return {}
	return {
		"receiver_map_epoch":epoch,
		"mounted_mecha_uid":mounted_uid,
		"mechas":mechas
	}


static func decode_pet_entity_spawn(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_PET_ENTITY_SPAWN,PET_ENTITY_SPAWN_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var tick:=reader.get_64()
	var owner_actor:=reader.get_64()
	var owner_connection:=reader.get_64()
	var owner_map_epoch:=reader.get_64()
	var pet_uid:=reader.get_64()
	var definition_id:=reader.get_32()
	var evolution_id:=reader.get_32()
	var level:=reader.get_32()
	var x:=reader.get_float()
	var y:=reader.get_float()
	if (
		epoch<=0 or tick<0 or owner_actor<=0 or owner_connection<=0 or
		owner_map_epoch<=0 or pet_uid<=0 or definition_id<=0 or
		definition_id>100000000 or evolution_id<0 or evolution_id>65535 or
		level<=0 or level>1000000 or
		not is_finite(x) or not is_finite(y) or x<0 or y<0 or
		reader.get_available_bytes()!=0
	):
		return {}
	return {
		"receiver_map_epoch":epoch,"server_tick":tick,
		"owner_actor_id":owner_actor,"owner_connection_epoch":owner_connection,
		"owner_map_epoch":owner_map_epoch,"pet_uid":pet_uid,
		"definition_id":definition_id,"evolution_id":evolution_id,
		"level":level,"x":x,"y":y
	}


static func decode_pet_entity_delta(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_PET_ENTITY_DELTA,PET_ENTITY_DELTA_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var tick:=reader.get_64()
	var owner_actor:=reader.get_64()
	var owner_connection:=reader.get_64()
	var owner_map_epoch:=reader.get_64()
	var pet_uid:=reader.get_64()
	var x:=reader.get_float()
	var y:=reader.get_float()
	if (
		epoch<=0 or tick<0 or owner_actor<=0 or owner_connection<=0 or
		owner_map_epoch<=0 or pet_uid<=0 or not is_finite(x) or not is_finite(y) or
		x<0 or y<0 or reader.get_available_bytes()!=0
	):
		return {}
	return {
		"receiver_map_epoch":epoch,"server_tick":tick,
		"owner_actor_id":owner_actor,"owner_connection_epoch":owner_connection,
		"owner_map_epoch":owner_map_epoch,"pet_uid":pet_uid,"x":x,"y":y
	}


static func encode_pet_attack(
		epoch:int,sequence:int,pet_uid:int,target_entity_id:int)->PackedByteArray:
	if (
		epoch<=0 or sequence<=0 or sequence>0xffffffff or
		pet_uid<=0 or target_entity_id<=0
	):
		return PackedByteArray()
	var writer:=_writer(PET_ATTACK_FRAME_BYTES,OPCODE_PET_ATTACK)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_64(pet_uid)
	writer.put_64(target_entity_id)
	return writer.data_array


static func decode_pet_attack_result(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_PET_ATTACK_RESULT,PET_ATTACK_RESULT_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var sequence:=reader.get_u32()
	var status:=reader.get_u8()
	var pet_uid:=reader.get_64()
	var target_entity_id:=reader.get_64()
	var damage:=reader.get_32()
	var current_hp:=reader.get_32()
	var max_hp:=reader.get_32()
	var target_revision:=reader.get_64()
	var server_tick:=reader.get_64()
	if (
		epoch<=0 or sequence<=0 or status<0 or status>9 or pet_uid<=0 or
		target_entity_id<=0 or damage<0 or current_hp<0 or max_hp<0 or
		target_revision<0 or server_tick<0 or
		(max_hp>0 and current_hp>max_hp) or reader.get_available_bytes()!=0
	):
		return {}
	if status==0 and (damage<=0 or max_hp<=0 or target_revision<=0):
		return {}
	if status==1 and (damage!=0 or max_hp<=0 or target_revision<=0):
		return {}
	if status>1 and damage!=0:
		return {}
	return {
		"map_epoch":epoch,"attack_sequence":sequence,"status":status,
		"pet_uid":pet_uid,"target_entity_id":target_entity_id,"damage":damage,
		"current_hp":current_hp,"max_hp":max_hp,
		"target_revision":target_revision,"server_tick":server_tick
	}


static func decode_pet_attack_visual(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_PET_ATTACK_VISUAL,PET_ATTACK_VISUAL_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var tick:=reader.get_64()
	var owner_actor:=reader.get_64()
	var owner_connection:=reader.get_64()
	var owner_map_epoch:=reader.get_64()
	var sequence:=reader.get_u32()
	var pet_uid:=reader.get_64()
	var target:=reader.get_64()
	var status:=reader.get_u8()
	var damage:=reader.get_32()
	var revision:=reader.get_64()
	if (
		epoch<=0 or tick<0 or owner_actor<=0 or owner_connection<=0 or
		owner_map_epoch<=0 or sequence<=0 or pet_uid<=0 or target<=0 or
		status not in [0,1] or damage<0 or revision<=0 or
		reader.get_available_bytes()!=0
	):
		return {}
	if status==0 and damage<=0:
		return {}
	if status==1 and damage!=0:
		return {}
	return {
		"receiver_map_epoch":epoch,"server_tick":tick,
		"owner_actor_id":owner_actor,"owner_connection_epoch":owner_connection,
		"owner_map_epoch":owner_map_epoch,"attack_sequence":sequence,
		"pet_uid":pet_uid,"target_entity_id":target,"status":status,
		"damage":damage,"target_revision":revision
	}


static func decode_pet_entity_despawn(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_PET_ENTITY_DESPAWN,PET_ENTITY_DESPAWN_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var tick:=reader.get_64()
	var owner_actor:=reader.get_64()
	var owner_connection:=reader.get_64()
	var owner_map_epoch:=reader.get_64()
	var pet_uid:=reader.get_64()
	if (
		epoch<=0 or tick<0 or owner_actor<=0 or owner_connection<=0 or
		owner_map_epoch<=0 or pet_uid<=0 or reader.get_available_bytes()!=0
	):
		return {}
	return {
		"receiver_map_epoch":epoch,"server_tick":tick,
		"owner_actor_id":owner_actor,"owner_connection_epoch":owner_connection,
		"owner_map_epoch":owner_map_epoch,"pet_uid":pet_uid
	}


static func encode_party_command(
		epoch:int,sequence:int,action:int,party_id:int,target_actor_id:int,
		target_name:String=""
	)->PackedByteArray:
	var name_bytes:=target_name.to_utf8_buffer()
	if (
		epoch<=0 or sequence<=0 or sequence>0xffffffff or action<1 or action>6 or
		party_id<0 or target_actor_id<0 or
		name_bytes.size()>CHAT_MAX_SENDER_NAME_BYTES
	):
		return PackedByteArray()
	var name_target:=not target_name.is_empty()
	if name_target and (
		target_name!=target_name.strip_edges() or
		target_name.strip_edges().is_empty()
	):
		return PackedByteArray()
	if action==1:
		if (target_actor_id>0)==name_target:
			return PackedByteArray()
	elif action in [5,6]:
		if name_target or target_actor_id<=0 or party_id<=0:
			return PackedByteArray()
	elif action in [2,3]:
		if name_target or party_id<=0 or target_actor_id<=0:
			return PackedByteArray()
	elif action==4:
		if name_target or party_id<=0 or target_actor_id!=0:
			return PackedByteArray()
	else:
		return PackedByteArray()
	var writer := _writer(PARTY_COMMAND_FRAME_BYTES+name_bytes.size(), OPCODE_PARTY_COMMAND)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(action)
	writer.put_64(party_id)
	writer.put_64(target_actor_id)
	if not name_bytes.is_empty():
		writer.put_data(name_bytes)
	return writer.data_array


static func decode_party_result(frame:PackedByteArray)->Dictionary:
	if not _valid_header(frame,OPCODE_PARTY_RESULT,PARTY_RESULT_FRAME_BYTES):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var sequence:=reader.get_u32()
	var action:=reader.get_u8()
	var status:=reader.get_u8()
	var party_id:=reader.get_64()
	var target:=reader.get_64()
	if (
		epoch<=0 or sequence==0 or action<1 or action>6 or
		status<0 or status>10 or party_id<0 or target<0
	):
		return {}
	return {
		"map_epoch":epoch,"party_sequence":sequence,"action":action,
		"status":status,"party_id":party_id,"target_actor_id":target
	}


static func decode_party_invite(frame:PackedByteArray)->Dictionary:
	if not _valid_variable_header(
		frame,OPCODE_PARTY_INVITE,PARTY_INVITE_HEADER_BYTES+1,
		PARTY_INVITE_MAX_FRAME_BYTES
	):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var party_id:=reader.get_64()
	var revision:=reader.get_u32()
	var inviter:=reader.get_64()
	var inviter_connection:=reader.get_64()
	var name_count:=reader.get_u16()
	if (
		epoch<=0 or party_id<=0 or revision==0 or inviter<=0 or
		inviter_connection<=0 or name_count<1 or
		name_count>CHAT_MAX_SENDER_NAME_BYTES or
		reader.get_available_bytes()!=name_count
	):
		return {}
	var result:=reader.get_data(name_count)
	if result[0]!=OK:
		return {}
	var bytes:PackedByteArray=result[1]
	var name:=bytes.get_string_from_utf8()
	if name.to_utf8_buffer()!=bytes or name.strip_edges().is_empty():
		return {}
	return {
		"receiver_map_epoch":epoch,"party_id":party_id,"revision":revision,
		"inviter_actor_id":inviter,
		"inviter_connection_epoch":inviter_connection,
		"inviter_name":name
	}


static func decode_party_snapshot(frame:PackedByteArray)->Dictionary:
	if not _valid_variable_header(
		frame,OPCODE_PARTY_SNAPSHOT,PARTY_SNAPSHOT_HEADER_BYTES,
		PARTY_SNAPSHOT_MAX_FRAME_BYTES
	):
		return {}
	var reader:=_reader(frame,8)
	var epoch:=reader.get_64()
	var party_id:=reader.get_64()
	var revision:=reader.get_u32()
	var leader:=reader.get_64()
	var count:=reader.get_u8()
	if (
		epoch<=0 or party_id<=0 or revision==0 or count<0 or
		count>PARTY_MAX_MEMBERS or ((count==0)!=(leader==0))
	):
		return {}
	var members:Array[Dictionary]=[]
	var seen:={}
	for _i in range(count):
		if reader.get_available_bytes()<PARTY_MEMBER_HEADER_BYTES:
			return {}
		var actor:=reader.get_64()
		var connection:=reader.get_64()
		var name_count:=reader.get_u16()
		if (
			actor<=0 or connection<=0 or name_count<1 or
			name_count>CHAT_MAX_SENDER_NAME_BYTES or
			reader.get_available_bytes()<name_count or seen.has(actor)
		):
			return {}
		var result:=reader.get_data(name_count)
		if result[0]!=OK:
			return {}
		var bytes:PackedByteArray=result[1]
		var name:=bytes.get_string_from_utf8()
		if name.to_utf8_buffer()!=bytes or name.strip_edges().is_empty():
			return {}
		seen[actor]=true
		members.append({
			"actor_id":actor,"connection_epoch":connection,"name":name
		})
	if reader.get_available_bytes()!=0:
		return {}
	if count>0 and not seen.has(leader):
		return {}
	return {
		"receiver_map_epoch":epoch,"party_id":party_id,"revision":revision,
		"leader_actor_id":leader,"members":members
	}


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
	var remaining := reader.get_available_bytes()-name_bytes_count
	if receiver_epoch<=0 or actor<=0 or connection<=0 or entity_map_epoch<=0 or mode>1 or red>1 or server_unix_ms<=0 or pink_until_unix_ms<0 or pvp_updated_unix_ms<0 or name_bytes_count<1 or name_bytes_count>PLAYER_IDENTITY_MAX_NAME_BYTES or remaining not in [0,1+BODY_LEG_CHARACTER_APPEARANCE_BYTES,1+CHARACTER_APPEARANCE_BYTES]:
		return {}
	var result:=reader.get_data(name_bytes_count)
	if result[0]!=OK:
		return {}
	var name_bytes:PackedByteArray=result[1]
	var display_name:=name_bytes.get_string_from_utf8()
	if display_name.to_utf8_buffer()!=name_bytes or display_name.strip_edges().is_empty():
		return {}
	var appearance := {}
	if remaining > 0:
		if reader.get_u8() != 1:
			return {}
		appearance = _read_character_appearance(reader,remaining-1)
		if appearance.is_empty() or reader.get_available_bytes()!=0:
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
		"appearance":appearance,
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
	if opcode not in [OPCODE_MOVE_TARGET, OPCODE_MOVE_RECEIPT, OPCODE_NPC_INTERACT, OPCODE_NPC_INTERACT_RESULT, OPCODE_NORMAL_ATTACK, OPCODE_NORMAL_ATTACK_RESULT, OPCODE_PLAYER_COMBAT_STATE, OPCODE_PLAYER_DAMAGE, OPCODE_AUTH_REQUEST, OPCODE_AUTH_RESULT, OPCODE_CHARACTER_LIST, OPCODE_CHARACTER_CHOOSE, OPCODE_CHARACTER_CHOOSE_RESULT, OPCODE_CHARACTER_CREATE, OPCODE_CHARACTER_CREATE_RESULT, OPCODE_ACCOUNT_REGISTER_REQUEST, OPCODE_ACCOUNT_REGISTER_RESULT, OPCODE_WORLD_READY, OPCODE_ENTITY_SPAWN, OPCODE_ENTITY_DESPAWN, OPCODE_ENTITY_DELTA, OPCODE_SELF_STATE, OPCODE_WORLD_READY_ACK, OPCODE_WORLD_RESYNC_REQUEST, OPCODE_MONSTER_SPAWN, OPCODE_MONSTER_DELTA, OPCODE_MONSTER_DESPAWN, OPCODE_PLAYER_RESPAWN, OPCODE_PLAYER_RESPAWN_RESULT, OPCODE_GROUND_DROP_SPAWN, OPCODE_GROUND_DROP_DESPAWN, OPCODE_GROUND_PICKUP, OPCODE_GROUND_PICKUP_RESULT, OPCODE_INVENTORY_SNAPSHOT, OPCODE_RECOVERY_USE, OPCODE_RECOVERY_USE_RESULT, OPCODE_INVENTORY_DISCARD, OPCODE_INVENTORY_DISCARD_RESULT, OPCODE_PLAYER_ATTACK_VISUAL, OPCODE_PLAYER_DAMAGE_VISUAL, OPCODE_PLAYER_RESPAWN_VISUAL, OPCODE_SKILL_USE, OPCODE_SKILL_USE_RESULT, OPCODE_PLAYER_SKILL_VISUAL, OPCODE_SHOP_OPEN, OPCODE_SHOP_SNAPSHOT, OPCODE_SHOP_BUY, OPCODE_SHOP_SELL, OPCODE_SHOP_TRANSACTION_RESULT, OPCODE_STORAGE_OPEN, OPCODE_STORAGE_SNAPSHOT, OPCODE_STORAGE_DEPOSIT, OPCODE_STORAGE_WITHDRAW, OPCODE_STORAGE_TRANSFER_RESULT, OPCODE_EQUIPMENT_SNAPSHOT, OPCODE_EQUIPMENT_EQUIP, OPCODE_EQUIPMENT_UNEQUIP, OPCODE_EQUIPMENT_TRANSFER_RESULT, OPCODE_BANK_OPEN, OPCODE_BANK_SNAPSHOT, OPCODE_BANK_DEPOSIT, OPCODE_BANK_WITHDRAW, OPCODE_BANK_TRANSACTION_RESULT, OPCODE_REPAIR_OPEN, OPCODE_REPAIR_SNAPSHOT, OPCODE_REPAIR_ITEM, OPCODE_REPAIR_RESULT, OPCODE_TRADE_COMMAND, OPCODE_TRADE_SNAPSHOT, OPCODE_TRADE_TERMINAL, OPCODE_PVP_MODE_COMMAND, OPCODE_PVP_MODE_RESULT, OPCODE_PVP_ATTACK, OPCODE_PVP_ATTACK_RESULT, OPCODE_PVP_DAMAGE, OPCODE_PLAYER_IDENTITY, OPCODE_PVP_COMBAT_VISUAL, OPCODE_PVP_SKILL, OPCODE_PVP_SKILL_RESULT, OPCODE_PVP_SKILL_VISUAL, OPCODE_GM_CAPABILITY, OPCODE_GM_PVP_NAME_COMMAND, OPCODE_GM_PVP_NAME_RESULT, OPCODE_PLAYER_SHOP_COMMAND, OPCODE_PLAYER_SHOP_RESULT, OPCODE_PLAYER_SHOP_SOLD, OPCODE_CHAT_COMMAND, OPCODE_CHAT_DELIVERY, OPCODE_QUEST_OPEN, OPCODE_QUEST_VIEW, OPCODE_QUEST_CHOOSE, OPCODE_ENHANCEMENT_OPEN, OPCODE_ENHANCEMENT_SNAPSHOT, OPCODE_ENHANCEMENT_ITEM, OPCODE_ENHANCEMENT_RESULT, OPCODE_WHISPER_COMMAND, OPCODE_WHISPER_RESULT, OPCODE_PARTY_COMMAND, OPCODE_PARTY_RESULT, OPCODE_PARTY_INVITE, OPCODE_PARTY_SNAPSHOT, OPCODE_SKILL_BOOK_SNAPSHOT, OPCODE_CHARACTER_MANAGE, OPCODE_CHARACTER_MANAGE_RESULT, OPCODE_CLAN_COMMAND, OPCODE_CLAN_RESULT, OPCODE_CLAN_SNAPSHOT, OPCODE_PLAYER_MOVEMENT_INTENT, OPCODE_PET_COMMAND, OPCODE_PET_RESULT, OPCODE_PET_SNAPSHOT, OPCODE_PET_ENTITY_SPAWN, OPCODE_PET_ENTITY_DESPAWN, OPCODE_PET_ENTITY_DELTA, OPCODE_PET_ATTACK, OPCODE_PET_ATTACK_RESULT, OPCODE_PET_ATTACK_VISUAL, OPCODE_FRIEND_COMMAND, OPCODE_FRIEND_RESULT, OPCODE_FRIEND_SNAPSHOT, OPCODE_MECHA_COMMAND, OPCODE_MECHA_RESULT, OPCODE_MECHA_SNAPSHOT]:
		return -1
	return opcode

static func frame_length_prefix(buffer: PackedByteArray) -> int:
	return FrameCodec.length_prefix(buffer, HEADER_BYTES, MAX_FRAME_BYTES)

static func _reader(frame: PackedByteArray, offset: int) -> StreamPeerBuffer:
	return FrameCodec.reader(frame, offset)

# Protocol constants have exactly one owner. The shared framing writer
# supplies the length/version/opcode header; feature payloads follow in order.
static func _writer(frame_bytes: int, opcode: int) -> StreamPeerBuffer:
	return FrameCodec.writer(frame_bytes, VERSION, opcode)

static func _valid_header(frame: PackedByteArray, expected_opcode: int, expected_bytes: int) -> bool:
	return FrameCodec.valid_header(
		frame, VERSION, HEADER_BYTES, MAX_FRAME_BYTES, expected_opcode, expected_bytes)

static func _valid_variable_header(frame: PackedByteArray, expected_opcode: int, minimum_bytes: int, maximum_bytes: int) -> bool:
	return FrameCodec.valid_variable_header(
		frame, VERSION, HEADER_BYTES, MAX_FRAME_BYTES,
		expected_opcode, minimum_bytes, maximum_bytes)
