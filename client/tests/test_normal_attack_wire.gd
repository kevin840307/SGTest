extends SceneTree
const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://network/world_state.gd")

const MONSTER_ID := 1021400001

func _fail(message: String) -> void:
	push_error("NORMAL_ATTACK_GODOT_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var request := WireCodec.encode_normal_attack(1,1,MONSTER_ID)
	var decoded := WireCodec.decode_normal_attack(request)
	if request.size()!=28 or WireCodec.frame_opcode(request)!=WireCodec.OPCODE_NORMAL_ATTACK or decoded.is_empty() or int(decoded.map_epoch)!=1 or int(decoded.attack_sequence)!=1 or int(decoded.target_entity_id)!=MONSTER_ID:
		_fail("request codec")
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
	var miss := _attack_result(1,2,1,42,MONSTER_ID,0,40,100,2)
	if not world.apply_frame(miss):
		_fail("MISS result rejected")
		return
	snapshot=world.snapshot()
	if int(snapshot.last_attack_result_status)!=1 or int(snapshot.last_attack_result.damage)!=0 or int(snapshot.monsters[MONSTER_ID].current_hp)!=40:
		_fail("MISS semantics or monster authority changed")
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

	print("NORMAL_ATTACK_GODOT_PASS request_bytes=28 result_bytes=57 receipt_does_not_mutate_monster=true delta_owns_hp=true miss_preserved=true stale_idempotent=true future_sequence_fail_closed=true")
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
