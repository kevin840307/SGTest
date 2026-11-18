extends SceneTree
const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://network/world_state.gd")
const MONSTER_ID := 1001500001

func _fail(message: String) -> void:
	push_error("SKILL_COMBAT_GODOT_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var request := WireCodec.encode_skill_use(1,1,99001,MONSTER_ID)
	var decoded := WireCodec.decode_skill_use(request)
	if request.size()!=32 or WireCodec.frame_opcode(request)!=WireCodec.OPCODE_SKILL_USE or decoded.is_empty() or int(decoded.map_epoch)!=1 or int(decoded.skill_sequence)!=1 or int(decoded.skill_id)!=99001 or int(decoded.target_entity_id)!=MONSTER_ID:
		_fail("skill request codec")
		return

	var world := WorldState.new()
	world.state=WorldState.STATE_IN_WORLD
	world.actor_id=11
	world.map_id=15
	world.map_epoch=1
	world.world_ready=true
	world.last_world_ready_ack_epoch=1
	world.monsters[MONSTER_ID]={"entity_id":MONSTER_ID,"current_hp":100,"max_hp":100,"revision":1,"state":0}
	if not world.mark_skill_sent(1):
		_fail("skill send gate")
		return

	var result := _skill_result(1,1,0,42,99001,MONSTER_ID,60,40,100,2)
	var parsed := WireCodec.decode_skill_use_result(result)
	if result.size()!=61 or parsed.is_empty() or int(parsed.damage)!=60 or int(parsed.skill_id)!=99001:
		_fail("skill result codec")
		return
	if not world.apply_frame(result):
		_fail("skill result reducer")
		return
	var visual := _skill_visual(1,42,11,9,1,99001,MONSTER_ID,0,60,2,6)
	var visual_parsed := WireCodec.decode_player_skill_visual(visual)
	if visual.size()!=70 or visual_parsed.is_empty() or int(visual_parsed.skill_id)!=99001 or int(visual_parsed.damage)!=60:
		_fail("skill visual codec")
		return
	world.self_state={"connection_epoch":9}
	if not world.apply_frame(visual) or world.snapshot().player_skill_events.size()!=1:
		_fail("skill visual reducer")
		return
	var snap: Dictionary=world.snapshot()
	if int(snap.last_skill_result_sequence)!=1 or int(snap.last_skill_result_status)!=0 or int(snap.last_skill_result.damage)!=60:
		_fail("skill result not retained")
		return
	if int((snap.monsters[MONSTER_ID] as Dictionary).current_hp)!=100:
		_fail("skill receipt illegally mutated monster HP")
		return
	if not world.apply_frame(result) or int(world.snapshot().stale_skill_results)!=1:
		_fail("duplicate skill result not stale")
		return

	var malformed := _skill_result(1,2,0,43,99001,MONSTER_ID,0,40,100,2)
	if not WireCodec.decode_skill_use_result(malformed).is_empty():
		_fail("applied zero-damage result accepted")
		return

	var future := WorldState.new()
	future.state=WorldState.STATE_IN_WORLD
	future.actor_id=11
	future.map_id=15
	future.map_epoch=1
	future.world_ready=true
	future.last_world_ready_ack_epoch=1
	future.skill_sequence=1
	if future.apply_frame(_skill_result(2,1,10,44,99001,MONSTER_ID,0,0,0,0)) or int(future.state)!=WorldState.STATE_FAILED:
		_fail("future-map skill result did not fail closed")
		return

	print("SKILL_COMBAT_GODOT_PASS request_bytes=32 result_bytes=61 visual_bytes=70 client_damage_absent=true receipt_does_not_mutate_monster=true visual_is_presentation_only=true duplicate_stale=true future_map_fail_closed=true")
	quit(0)

func _skill_result(epoch: int, sequence: int, status: int, tick: int,
		skill_id: int, target: int, damage: int, current_hp: int,
		max_hp: int, revision: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(61)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_SKILL_USE_RESULT)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(status)
	writer.put_64(tick)
	writer.put_32(skill_id)
	writer.put_64(target)
	writer.put_32(damage)
	writer.put_32(current_hp)
	writer.put_32(max_hp)
	writer.put_64(revision)
	return writer.data_array

func _skill_visual(epoch: int,tick: int,attacker: int,connection: int,
		sequence: int,skill_id: int,target: int,status: int,damage: int,
		revision: int,skill_facing: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(70)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PLAYER_SKILL_VISUAL)
	writer.put_64(epoch)
	writer.put_64(tick)
	writer.put_64(attacker)
	writer.put_64(connection)
	writer.put_u32(sequence)
	writer.put_32(skill_id)
	writer.put_64(target)
	writer.put_u8(status)
	writer.put_32(damage)
	writer.put_64(revision)
	writer.put_u8(skill_facing)
	return writer.data_array
