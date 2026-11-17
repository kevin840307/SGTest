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
const MOVE_TARGET_FRAME_BYTES := 28
const MOVE_RECEIPT_FRAME_BYTES := 30
const NPC_INTERACT_FRAME_BYTES := 24
const NPC_INTERACT_RESULT_FRAME_BYTES := 29
const NORMAL_ATTACK_FRAME_BYTES := 28
const NORMAL_ATTACK_RESULT_FRAME_BYTES := 57
const PLAYER_COMBAT_STATE_FRAME_BYTES := 24
const PLAYER_DAMAGE_FRAME_BYTES := 57
const PLAYER_RESPAWN_FRAME_BYTES := 20
const PLAYER_RESPAWN_RESULT_FRAME_BYTES := 29
const AUTH_RESULT_FRAME_BYTES := 9
const CHARACTER_CHOOSE_FRAME_BYTES := 16
const CHARACTER_CHOOSE_RESULT_FRAME_BYTES := 45
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
const INVENTORY_SNAPSHOT_HEADER_BYTES := 23
const INVENTORY_ITEM_BYTES := 24
const INVENTORY_SNAPSHOT_MAX_PER_FRAME := 128
const INVENTORY_SNAPSHOT_MAX_ITEMS := 4095

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

static func decode_player_combat_state(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_PLAYER_COMBAT_STATE, PLAYER_COMBAT_STATE_FRAME_BYTES):
		return {}
	var reader := _reader(frame,8)
	var epoch := reader.get_64()
	var current_hp := reader.get_32()
	var max_hp := reader.get_32()
	if epoch <= 0 or current_hp < 0 or max_hp <= 0 or current_hp > max_hp:
		return {}
	return {"map_epoch":epoch,"current_hp":current_hp,"max_hp":max_hp}

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
		if uid <= 0 or item_id < 1 or item_id > 100000000 or quantity < 1 or quantity > 1000000 or drop_id <= 0 or ids.has(uid):
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
	if opcode not in [OPCODE_MOVE_TARGET, OPCODE_MOVE_RECEIPT, OPCODE_NPC_INTERACT, OPCODE_NPC_INTERACT_RESULT, OPCODE_NORMAL_ATTACK, OPCODE_NORMAL_ATTACK_RESULT, OPCODE_PLAYER_COMBAT_STATE, OPCODE_PLAYER_DAMAGE, OPCODE_AUTH_REQUEST, OPCODE_AUTH_RESULT, OPCODE_CHARACTER_LIST, OPCODE_CHARACTER_CHOOSE, OPCODE_CHARACTER_CHOOSE_RESULT, OPCODE_WORLD_READY, OPCODE_ENTITY_SPAWN, OPCODE_ENTITY_DESPAWN, OPCODE_ENTITY_DELTA, OPCODE_SELF_STATE, OPCODE_WORLD_READY_ACK, OPCODE_WORLD_RESYNC_REQUEST, OPCODE_MONSTER_SPAWN, OPCODE_MONSTER_DELTA, OPCODE_MONSTER_DESPAWN, OPCODE_PLAYER_RESPAWN, OPCODE_PLAYER_RESPAWN_RESULT, OPCODE_GROUND_DROP_SPAWN, OPCODE_GROUND_DROP_DESPAWN, OPCODE_GROUND_PICKUP, OPCODE_GROUND_PICKUP_RESULT, OPCODE_INVENTORY_SNAPSHOT]:
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
