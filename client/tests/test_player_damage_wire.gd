extends SceneTree
const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://network/world_state.gd")

const MONSTER_ID := 1001500001

func _fail(message: String) -> void:
	push_error("PLAYER_DAMAGE_GODOT_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var baseline := _combat_state(1,100,100)
	var parsed_baseline := WireCodec.decode_player_combat_state(baseline)
	if baseline.size()!=24 or parsed_baseline.is_empty() or int(parsed_baseline.current_hp)!=100:
		_fail("combat baseline codec")
		return

	var world := _world(1)
	if not world.apply_frame(baseline):
		_fail("baseline before WorldReady rejected")
		return
	var snap: Dictionary = world.snapshot()
	if not bool(snap.combat_available) or int(snap.player_current_hp)!=100 or bool(snap.player_dead):
		_fail("baseline state")
		return
	world.world_ready=true
	world.last_world_ready_ack_epoch=1

	var first := _damage(1,1,41,MONSTER_ID,2,18,82,100,false)
	var parsed := WireCodec.decode_player_damage(first)
	if first.size()!=57 or parsed.is_empty() or int(parsed.damage)!=18 or bool(parsed.died):
		_fail("damage codec")
		return
	if not world.apply_frame(first):
		_fail("first damage apply")
		return
	snap=world.snapshot()
	if int(snap.player_current_hp)!=82 or int(snap.player_damage_sequence)!=1 or bool(snap.player_dead) or int(snap.last_player_damage.damage)!=18:
		_fail("first damage state")
		return

	if not world.apply_frame(first) or int(world.snapshot().stale_player_damage)!=1:
		_fail("duplicate damage not idempotent")
		return

	var lethal := _damage(1,2,42,MONSTER_ID,3,82,0,100,true)
	if not world.apply_frame(lethal):
		_fail("lethal damage apply")
		return
	snap=world.snapshot()
	if int(snap.player_current_hp)!=0 or not bool(snap.player_dead) or int(snap.player_damage_sequence)!=2:
		_fail("death state")
		return
	if world.mark_move_sent(1) or world.mark_normal_attack_sent(1) or world.mark_npc_interact_sent(1):
		_fail("dead player gameplay intent escaped reducer")
		return

	var malformed := _damage(1,3,43,MONSTER_ID,4,1,0,100,false)
	if not WireCodec.decode_player_damage(malformed).is_empty():
		_fail("zero HP without death accepted")
		return

	world.map_epoch=2
	var stale_before := int(world.snapshot().stale_player_damage)
	if not world.apply_frame(_damage(1,3,43,MONSTER_ID,4,1,0,100,true)) or int(world.snapshot().stale_player_damage)!=stale_before+1:
		_fail("old-map damage was not stale")
		return

	var gap := _world(1)
	if not gap.apply_frame(_combat_state(1,100,100)):
		_fail("gap baseline")
		return
	if gap.apply_frame(_damage(1,2,41,MONSTER_ID,2,10,90,100,false)) or int(gap.state)!=WorldState.STATE_FAILED:
		_fail("damage sequence gap did not fail closed")
		return

	var future := _world(1)
	if not future.apply_frame(_combat_state(1,100,100)):
		_fail("future baseline")
		return
	if future.apply_frame(_damage(2,1,41,MONSTER_ID,2,10,90,100,false)) or int(future.state)!=WorldState.STATE_FAILED:
		_fail("future-map damage did not fail closed")
		return


	var dead_move := _move_receipt(1,1,11,44)
	var dead_move_parsed := WireCodec.decode_move_receipt(dead_move)
	if dead_move_parsed.is_empty() or int(dead_move_parsed.status)!=11:
		_fail("CombatDead move receipt codec")
		return
	var dead_npc := _npc_result(1,1,6,1)
	var dead_npc_parsed := WireCodec.decode_npc_interact_result(dead_npc)
	if dead_npc_parsed.is_empty() or int(dead_npc_parsed.status)!=6:
		_fail("CombatDead NPC result codec")
		return
	print("PLAYER_DAMAGE_GODOT_PASS baseline_bytes=24 damage_bytes=57 durable_event=true duplicate_stale=true sequence_gap_fail_closed=true death_gates_intents=true combat_dead_statuses=true old_map_stale=true")
	quit(0)

func _world(epoch: int) -> RefCounted:
	var world := WorldState.new()
	world.state=WorldState.STATE_IN_WORLD
	world.actor_id=11
	world.map_id=15
	world.map_epoch=epoch
	return world

func _combat_state(epoch: int,current_hp: int,max_hp: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(24)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PLAYER_COMBAT_STATE)
	writer.put_64(epoch)
	writer.put_32(current_hp)
	writer.put_32(max_hp)
	return writer.data_array

func _damage(epoch: int,sequence: int,tick: int,attacker: int,revision: int,
		damage: int,current_hp: int,max_hp: int,died: bool) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(57)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PLAYER_DAMAGE)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_64(tick)
	writer.put_64(attacker)
	writer.put_64(revision)
	writer.put_32(damage)
	writer.put_32(current_hp)
	writer.put_32(max_hp)
	writer.put_u8(1 if died else 0)
	return writer.data_array


func _move_receipt(epoch: int,sequence: int,status: int,tick: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(30)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_MOVE_RECEIPT)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u16(status)
	writer.put_64(tick)
	return writer.data_array

func _npc_result(epoch: int,sequence: int,status: int,result_epoch: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(29)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_NPC_INTERACT_RESULT)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(status)
	writer.put_64(result_epoch)
	return writer.data_array
