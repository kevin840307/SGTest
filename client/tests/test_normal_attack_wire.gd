extends SceneTree
const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://world/world_state.gd")

const MONSTER_ID := 1021400001
const PLAYER_ATTACK_VISUAL := "4200000001002900010000000000000032000000000000000B0000000000000002000000000000000700000021A0AE3B00000000001E000000030000000000000006"

func _fail(message: String) -> void:
	push_error("NORMAL_ATTACK_GODOT_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var request := WireCodec.encode_normal_attack(1,1,MONSTER_ID)
	var decoded := WireCodec.decode_normal_attack(request)
	if request.size()!=28 or WireCodec.frame_opcode(request)!=WireCodec.OPCODE_NORMAL_ATTACK or decoded.is_empty() or int(decoded.map_epoch)!=1 or int(decoded.attack_sequence)!=1 or int(decoded.target_entity_id)!=MONSTER_ID:
		_fail("request codec")
		return

	var visual := PLAYER_ATTACK_VISUAL.hex_decode()
	var visual_decoded := WireCodec.decode_player_attack_visual(visual)
	if visual.size()!=66 or WireCodec.frame_opcode(visual)!=WireCodec.OPCODE_PLAYER_ATTACK_VISUAL or visual_decoded.is_empty() or int(visual_decoded.attacker_actor_id)!=11 or int(visual_decoded.attacker_connection_epoch)!=2 or int(visual_decoded.attack_sequence)!=7 or int(visual_decoded.target_entity_id)!=1001300001 or int(visual_decoded.damage)!=30 or int(visual_decoded.target_revision)!=3 or int(visual_decoded.facing)!=6:
		_fail("player attack visual C#/Godot golden")
		return
	var bad_visual := visual.duplicate()
	bad_visual[52]=4
	if not WireCodec.decode_player_attack_visual(bad_visual).is_empty():
		_fail("non-visual OutOfRange event accepted")
		return
	bad_visual=visual.duplicate()
	bad_visual[65]=9
	if not WireCodec.decode_player_attack_visual(bad_visual).is_empty():
		_fail("invalid visual facing accepted")
		return

	var stationary := _world_with_monster()
	stationary.self_state.clear()
	var first_own_visual := _player_attack_visual(
		1,41,11,9,1,MONSTER_ID,0,60,2,6)
	if not stationary.apply_frame(first_own_visual):
		_fail("stationary own visual could not bind first server-only generation")
		return
	var stationary_snapshot: Dictionary = stationary.snapshot()
	if bool(stationary_snapshot.resync_pending) or stationary_snapshot.player_attack_events.size()!=1 or int(stationary_snapshot.player_attack_sequences[11].connection_epoch)!=9:
		_fail("stationary own visual incorrectly required changed-only SelfState")
		return

	var coalesced_visual_world := _world_with_monster()
	var coalesced_visual := _player_attack_visual(
		1,41,11,1,1,MONSTER_ID,0,60,5,6)
	if (
		not coalesced_visual_world.apply_frame(coalesced_visual) or
		bool(coalesced_visual_world.snapshot().resync_pending) or
		not coalesced_visual_world.snapshot().player_attack_sequences.has(11) or
		int(coalesced_visual_world.snapshot().monsters[MONSTER_ID].revision)!=1
	):
		_fail("presentation visual incorrectly required contiguous monster revision")
		return

	var world := _world_with_monster()
	if not world.mark_normal_attack_sent(1):
		_fail("attack sequence was not accepted after Ready ACK")
		return
	var result := _attack_result(1,1,0,41,MONSTER_ID,60,40,100,2)
	var parsed := WireCodec.decode_normal_attack_result(result)
	if parsed.is_empty() or int(parsed.status)!=0 or int(parsed.damage)!=60 or int(parsed.current_hp)!=40 or int(parsed.target_revision)!=2:
		_fail("result codec")
		return
	if not world.apply_frame(result):
		_fail("applied result rejected")
		return
	var snapshot: Dictionary = world.snapshot()
	if int(snapshot.last_attack_result_sequence)!=1 or int(snapshot.last_attack_result_status)!=0 or int(snapshot.last_attack_result.damage)!=60:
		_fail("reliable attack result not retained")
		return
	if int(snapshot.monsters[MONSTER_ID].current_hp)!=100 or int(snapshot.monsters[MONSTER_ID].revision)!=1:
		_fail("attack receipt illegally mutated monster authority")
		return

	var own_visual := _player_attack_visual(1,41,11,1,1,MONSTER_ID,0,60,2,6)
	if not world.apply_frame(own_visual):
		_fail("authoritative player attack visual rejected")
		return
	snapshot=world.snapshot()
	if snapshot.player_attack_events.size()!=1 or int(snapshot.player_attack_events[0].damage)!=60 or int(snapshot.player_attack_events[0].facing)!=6:
		_fail("player attack visual not retained")
		return
	if not world.apply_frame(own_visual) or int(world.snapshot().stale_player_attack_visuals)!=1 or world.snapshot().player_attack_events.size()!=1:
		_fail("duplicate attack visual replayed")
		return

	var delta := _monster_state(1,41,MONSTER_ID,2,40,100,4)
	if not world.apply_frame(delta):
		_fail("authoritative monster delta rejected")
		return
	snapshot=world.snapshot()
	if int(snapshot.monsters[MONSTER_ID].current_hp)!=40 or int(snapshot.monsters[MONSTER_ID].revision)!=2:
		_fail("monster delta did not own HP")
		return

	if not world.apply_frame(result) or int(world.snapshot().stale_attack_results)!=1:
		_fail("duplicate result was not stale/idempotent")
		return

	if not world.mark_normal_attack_sent(2):
		_fail("second attack sequence")
		return
	var miss_visual := _player_attack_visual(1,42,11,1,2,MONSTER_ID,1,0,2,6)
	if not world.apply_frame(miss_visual):
		_fail("MISS presentation visual rejected")
		return
	var miss := _attack_result(1,2,1,42,MONSTER_ID,0,40,100,2)
	if not world.apply_frame(miss):
		_fail("MISS result rejected")
		return
	snapshot=world.snapshot()
	if int(snapshot.last_attack_result_status)!=1 or int(snapshot.last_attack_result.damage)!=0 or int(snapshot.monsters[MONSTER_ID].current_hp)!=40 or snapshot.player_attack_events.size()!=2 or int(snapshot.player_attack_events[1].status)!=1:
		_fail("MISS semantics or presentation event changed monster authority")
		return

	var malformed := _attack_result(1,2,1,42,MONSTER_ID,5,40,100,2)
	if not WireCodec.decode_normal_attack_result(malformed).is_empty():
		_fail("MISS with nonzero damage accepted")
		return

	var future := _world_with_monster()
	if not future.mark_normal_attack_sent(1):
		_fail("future fixture attack sequence")
		return
	if future.apply_frame(_attack_result(1,2,0,41,MONSTER_ID,1,99,100,2)) or int(future.state)!=WorldState.STATE_FAILED:
		_fail("result for unsent attack sequence did not fail closed")
		return

	print("NORMAL_ATTACK_GODOT_PASS request_bytes=28 result_bytes=57 visual_bytes=66 receipt_does_not_mutate_monster=true visual_does_not_mutate_monster=true coalesced_revision_visual=true stationary_own_generation=true delta_owns_hp=true miss_preserved=true visual_replay_stale=true future_sequence_fail_closed=true")
	quit(0)

func _world_with_monster() -> RefCounted:
	var world := WorldState.new()
	world.state=WorldState.STATE_IN_WORLD
	world.actor_id=11
	world.map_id=15
	world.map_epoch=1
	world.world_ready=true
	world.last_world_ready_ack_epoch=1
	world.last_aoi_tick=40
	world.self_state={
		"receiver_map_epoch":1,"server_tick":40,"actor_id":11,
		"connection_epoch":1,"entity_map_epoch":1,"map_id":15,
		"revision":1,"x":24.0,"y":24.0,"moving":false,
		"movement_action_sequence":0,"facing":6
	}
	world.monsters[MONSTER_ID]={
		"receiver_map_epoch":1,"server_tick":40,"entity_id":MONSTER_ID,
		"enemy_id":1,"source_index":0,"map_id":15,"x":40.0,"y":24.0,
		"revision":1,"current_hp":100,"max_hp":100,"state":0,
		"facing":1,"target_actor_id":0
	}
	return world

func _attack_result(epoch: int, sequence: int, status: int, tick: int,
		target: int, damage: int, current_hp: int, max_hp: int, revision: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(57)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_NORMAL_ATTACK_RESULT)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(status)
	writer.put_64(tick)
	writer.put_64(target)
	writer.put_32(damage)
	writer.put_32(current_hp)
	writer.put_32(max_hp)
	writer.put_64(revision)
	return writer.data_array

func _player_attack_visual(epoch: int,tick: int,attacker: int,connection: int,
		sequence: int,target: int,status: int,damage: int,revision: int,facing: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(66)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PLAYER_ATTACK_VISUAL)
	writer.put_64(epoch)
	writer.put_64(tick)
	writer.put_64(attacker)
	writer.put_64(connection)
	writer.put_u32(sequence)
	writer.put_64(target)
	writer.put_u8(status)
	writer.put_32(damage)
	writer.put_64(revision)
	writer.put_u8(facing)
	return writer.data_array


func _monster_state(epoch: int,tick: int,entity_id: int,revision: int,
		current_hp: int,max_hp: int,state: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(78)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_MONSTER_DELTA)
	writer.put_64(epoch)
	writer.put_64(tick)
	writer.put_64(entity_id)
	writer.put_32(1)
	writer.put_32(0)
	writer.put_32(15)
	writer.put_float(40.0)
	writer.put_float(24.0)
	writer.put_64(revision)
	writer.put_32(current_hp)
	writer.put_32(max_hp)
	writer.put_u8(state)
	writer.put_u8(1)
	writer.put_64(0)
	return writer.data_array
