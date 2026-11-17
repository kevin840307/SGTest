extends SceneTree
const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://network/world_state.gd")

const MONSTER_ID := 1001500001
const DAMAGE_VISUAL_GOLDEN := "4200000001002A0001000000000000003C00000000000000160000000000000003000000000000000400000021A0AE3B000000000700000000000000120000000006"
const RESPAWN_VISUAL_GOLDEN := "2D00000001002B0001000000000000003D00000000000000160000000000000003000000000000000200000006"

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
	var damage_visual := _damage_visual(1,60,11,3,1,MONSTER_ID,7,18,false,6)
	if damage_visual.hex_encode().to_upper()!=DAMAGE_VISUAL_GOLDEN:
		_fail("damage visual CSharp/Godot golden")
		return
	var parsed_visual := WireCodec.decode_player_damage_visual(damage_visual)
	if parsed_visual.is_empty() or int(parsed_visual.victim_actor_id)!=11 or int(parsed_visual.damage)!=18 or bool(parsed_visual.died):
		_fail("damage visual codec")
		return
	if not world.apply_frame(damage_visual):
		_fail("damage visual apply")
		return
	snap=world.snapshot()
	if snap.player_damage_visual_events.size()!=1 or int(snap.player_current_hp)!=82:
		_fail("presentation visual mutated or missed HP authority")
		return
	if not world.apply_frame(damage_visual) or int(world.snapshot().stale_player_damage_visuals)!=1:
		_fail("damage visual replay not stale")
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
	var lethal_visual := _damage_visual(
		1,61,11,3,2,MONSTER_ID,8,82,true,6)
	if not world.apply_frame(lethal_visual):
		_fail("lethal damage visual apply")
		return
	snap=world.snapshot()
	if snap.player_damage_visual_events.size()!=2 or not bool(snap.player_damage_visual_events[-1].died):
		_fail("lethal presentation event missing")
		return
	if int(snap.player_current_hp)!=0 or not bool(snap.player_dead) or int(snap.player_damage_sequence)!=2:
		_fail("death state")
		return
	if world.mark_move_sent(1) or world.mark_normal_attack_sent(1) or world.mark_npc_interact_sent(1):
		_fail("dead player gameplay intent escaped reducer")
		return

	var respawn_request := WireCodec.encode_player_respawn(1,1)
	if respawn_request.size()!=20 or WireCodec.frame_opcode(respawn_request)!=WireCodec.OPCODE_PLAYER_RESPAWN:
		_fail("respawn request codec")
		return
	if not world.mark_player_respawn_sent(1):
		_fail("dead respawn intent gate")
		return
	var respawn := _respawn_result(1,1,0,100,100)
	var parsed_respawn := WireCodec.decode_player_respawn_result(respawn)
	if respawn.size()!=29 or parsed_respawn.is_empty() or int(parsed_respawn.current_hp)!=100:
		_fail("respawn result codec")
		return
	if not world.apply_frame(respawn):
		_fail("respawn result apply")
		return
	var respawn_visual := _respawn_visual(1,61,11,3,2,6)
	if respawn_visual.hex_encode().to_upper()!=RESPAWN_VISUAL_GOLDEN:
		_fail("respawn visual CSharp/Godot golden")
		return
	var parsed_respawn_visual := WireCodec.decode_player_respawn_visual(respawn_visual)
	if parsed_respawn_visual.is_empty() or int(parsed_respawn_visual.actor_id)!=11:
		_fail("respawn visual codec")
		return
	if not world.apply_frame(respawn_visual):
		_fail("respawn visual apply")
		return
	snap=world.snapshot()
	if snap.player_respawn_visual_events.size()!=1 or int(snap.player_current_hp)!=100:
		_fail("respawn presentation mutated or missed HP authority")
		return
	if int(snap.player_current_hp)!=100 or bool(snap.player_dead) or int(snap.player_damage_sequence)!=2 or int(snap.last_respawn_result_sequence)!=1:
		_fail("respawn state or damage sequence continuity")
		return
	if not world.mark_move_sent(1):
		_fail("respawn did not reopen gameplay gate")
		return
	var stale_respawn_before := int(world.snapshot().stale_respawn_results)
	if not world.apply_frame(respawn) or int(world.snapshot().stale_respawn_results)!=stale_respawn_before+1:
		_fail("duplicate respawn result not idempotent")
		return
	if not WireCodec.decode_player_respawn_result(_respawn_result(1,2,0,90,100)).is_empty():
		_fail("accepted partial-HP respawn decoded")
		return
	var malformed_visual := _damage_visual(
		1,62,11,3,3,MONSTER_ID,9,1,false,6)
	malformed_visual[64]=2
	if not WireCodec.decode_player_damage_visual(malformed_visual).is_empty():
		_fail("invalid damage visual death flag accepted")
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
	print("PLAYER_DAMAGE_GODOT_PASS baseline_bytes=24 damage_bytes=57 damage_visual_bytes=66 respawn_request_bytes=20 respawn_result_bytes=29 respawn_visual_bytes=45 durable_event=true presentation_only=true no_observer_hp_authority=true duplicate_stale=true visual_replay_stale=true sequence_gap_fail_closed=true death_gates_intents=true respawn_reopens_intents=true damage_sequence_continuous=true combat_dead_statuses=true old_map_stale=true")
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


func _respawn_result(epoch: int,sequence: int,status: int,current_hp: int,max_hp: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(29)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PLAYER_RESPAWN_RESULT)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(status)
	writer.put_32(current_hp)
	writer.put_32(max_hp)
	return writer.data_array


func _damage_visual(receiver_epoch: int,tick: int,victim: int,connection: int,
		sequence: int,attacker: int,attacker_revision: int,damage: int,
		died: bool,visual_facing: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(66)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PLAYER_DAMAGE_VISUAL)
	writer.put_64(receiver_epoch)
	writer.put_64(tick)
	writer.put_64(victim)
	writer.put_64(connection)
	writer.put_u32(sequence)
	writer.put_64(attacker)
	writer.put_64(attacker_revision)
	writer.put_32(damage)
	writer.put_u8(1 if died else 0)
	writer.put_u8(visual_facing)
	return writer.data_array

func _respawn_visual(receiver_epoch: int,tick: int,actor: int,connection: int,
		sequence: int,visual_facing: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(45)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PLAYER_RESPAWN_VISUAL)
	writer.put_64(receiver_epoch)
	writer.put_64(tick)
	writer.put_64(actor)
	writer.put_64(connection)
	writer.put_u32(sequence)
	writer.put_u8(visual_facing)
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
