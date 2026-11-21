extends SceneTree

const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://world/world_state.gd")

func _fail(reason: String) -> void:
	push_error("PVP_ATTACK_WIRE_FAIL "+reason)
	quit(1)

func _initialize() -> void:
	var request := WireCodec.encode_pvp_attack(1,7,99)
	if request.size()!=28 or WireCodec.frame_opcode(request)!=WireCodec.OPCODE_PVP_ATTACK:
		_fail("request codec")
		return
	var result := _attack_result(1,7,0,100,99,28,false)
	var parsed := WireCodec.decode_pvp_attack_result(result)
	if result.size()!=42 or parsed.is_empty() or int(parsed.damage)!=28 or int(parsed.target_actor_id)!=99:
		_fail("attack result codec")
		return
	var skill_request:=WireCodec.encode_pvp_skill(1,8,99003,99)
	if skill_request.size()!=32 or WireCodec.frame_opcode(skill_request)!=WireCodec.OPCODE_PVP_SKILL:
		_fail("PvP skill request codec")
		return
	var skill_result:=_pvp_skill_result(1,8,0,120,99003,99,42,true)
	var parsed_skill:=WireCodec.decode_pvp_skill_result(skill_result)
	if skill_result.size()!=46 or parsed_skill.is_empty() or int(parsed_skill.skill_id)!=99003 or int(parsed_skill.damage)!=42 or not bool(parsed_skill.died):
		_fail("PvP skill result codec")
		return
	var skill_visual:=_pvp_skill_visual(1,120,11,5,8,99003,99,4,2,42,true,3,6)
	var parsed_skill_visual:=WireCodec.decode_pvp_skill_visual(skill_visual)
	if skill_visual.size()!=75 or parsed_skill_visual.is_empty() or int(parsed_skill_visual.skill_id)!=99003 or int(parsed_skill_visual.damage_sequence)!=2:
		_fail("PvP skill visual codec")
		return

	var victim := _pvp_damage(1,1,100,99,4,7,28,72,100,false)
	var parsed_damage := WireCodec.decode_pvp_damage(victim)
	if victim.size()!=61 or parsed_damage.is_empty() or int(parsed_damage.attacker_actor_id)!=99 or int(parsed_damage.current_hp)!=72:
		_fail("victim damage codec")
		return
	var visual:=_pvp_visual(1,100,99,4,7,11,5,1,28,false,3,6)
	var parsed_visual:=WireCodec.decode_pvp_combat_visual(visual)
	if visual.size()!=71 or WireCodec.frame_opcode(visual)!=WireCodec.OPCODE_PVP_COMBAT_VISUAL or parsed_visual.is_empty() or int(parsed_visual.attacker_actor_id)!=99 or int(parsed_visual.victim_actor_id)!=11 or int(parsed_visual.damage)!=28:
		_fail("PvP combat visual codec")
		return

	var world := WorldState.new()
	world.state=WorldState.STATE_IN_WORLD
	world.actor_id=11
	world.map_id=13
	world.map_epoch=1
	world.world_ready=true
	world.last_world_ready_ack_epoch=1
	world.pvp_known=true
	world.pvp_map_allows=true
	world.entities[99]={"actor_id":99,"connection_epoch":4,"entity_map_epoch":1}
	if not world.apply_frame(_combat_state(1,100,100)):
		_fail("combat baseline")
		return
	if not world.mark_pvp_attack_sent(1,99):
		_fail("attack intent gate")
		return
	if world.mark_pvp_attack_sent(2,99):
		_fail("single flight")
		return
	if not world.apply_frame(_attack_result(1,1,10,100,99,0,false)):
		_fail("attack result apply")
		return
	var snap: Dictionary=world.snapshot()
	if int(snap.last_pvp_attack_result_sequence)!=1 or int(snap.last_pvp_attack_result_status)!=10 or int(snap.pending_pvp_attack_sequence)!=0:
		_fail("rejected attack result state")
		return
	if not world.mark_pvp_attack_sent(2,99) or not world.apply_frame(_attack_result(1,2,0,115,99,28,false)):
		_fail("applied attack result")
		return
	snap=world.snapshot()
	if int(snap.last_pvp_attack_result.damage)!=28 or bool(snap.last_pvp_attack_result.died):
		_fail("applied attack receipt state")
		return
	var hp_before_visual:=int(snap.player_current_hp)
	if not world.apply_frame(visual):
		_fail("PvP visual apply")
		return
	snap=world.snapshot()
	if snap.pvp_combat_visual_events.size()!=1 or int(snap.pvp_combat_visual_events[0].attack_sequence)!=7 or int(snap.pvp_combat_visual_events[0].damage_sequence)!=1 or int(snap.player_current_hp)!=hp_before_visual:
		_fail("PvP visual mutated HP or was not retained")
		return
	if not world.apply_frame(visual) or int(world.snapshot().stale_pvp_combat_visuals)!=1:
		_fail("PvP visual replay not stale")
		return
	var skill_world:=WorldState.new()
	skill_world.state=WorldState.STATE_IN_WORLD
	skill_world.actor_id=11
	skill_world.map_id=13
	skill_world.map_epoch=1
	skill_world.world_ready=true
	skill_world.last_world_ready_ack_epoch=1
	skill_world.pvp_known=true
	skill_world.pvp_map_allows=true
	skill_world.entities[99]={"actor_id":99,"connection_epoch":4,"entity_map_epoch":1}
	if not skill_world.apply_frame(_combat_state(1,100,100)):
		_fail("PvP skill combat baseline")
		return
	if not skill_world.mark_skill_sent(8,1,99) or skill_world.mark_skill_sent(9,1,99):
		_fail("PvP skill shared singleflight")
		return
	if not skill_world.apply_frame(skill_result):
		_fail("PvP skill result apply")
		return
	var skill_snap:Dictionary=skill_world.snapshot()
	if int(skill_snap.last_skill_result_sequence)!=8 or str(skill_snap.last_skill_result.get("target_kind",""))!="player" or int(skill_snap.last_skill_result.damage)!=42 or int(skill_snap.pending_skill_sequence)!=0 or int(skill_snap.player_current_hp)!=100:
		_fail("PvP skill result mutated attacker HP or lost shared skill state")
		return
	if not skill_world.apply_frame(skill_visual):
		_fail("PvP skill visual apply")
		return
	skill_snap=skill_world.snapshot()
	if skill_snap.pvp_skill_visual_events.size()!=1 or int(skill_snap.pvp_skill_visual_events[0].skill_id)!=99003 or int(skill_snap.player_current_hp)!=100:
		_fail("PvP skill visual was not presentation-only")
		return
	if not skill_world.apply_frame(skill_visual) or int(skill_world.snapshot().stale_pvp_skill_visuals)!=1:
		_fail("PvP skill visual replay not stale")
		return

	if not world.apply_frame(victim):
		_fail("PvP victim damage apply")
		return
	snap=world.snapshot()
	if int(snap.player_current_hp)!=72 or int(snap.player_damage_sequence)!=1 or str(snap.last_player_damage.get("source_kind",""))!="pvp" or bool(snap.player_dead):
		_fail("shared HP timeline")
		return
	if not world.apply_frame(victim) or int(world.snapshot().stale_player_damage)!=1:
		_fail("duplicate PvP damage not stale")
		return
	if not world.apply_frame(_pvp_damage(1,2,116,99,4,8,72,0,100,true)):
		_fail("lethal PvP damage")
		return
	snap=world.snapshot()
	if int(snap.player_current_hp)!=0 or not bool(snap.player_dead) or int(snap.player_damage_sequence)!=2:
		_fail("PvP death state")
		return
	if world.mark_pvp_attack_sent(3,99):
		_fail("dead attacker intent escaped reducer")
		return

	var stale_result_before:=int(snap.stale_pvp_attack_results)
	if not world.apply_frame(_attack_result(1,2,0,115,99,28,false)) or int(world.snapshot().stale_pvp_attack_results)!=stale_result_before+1:
		_fail("attack result replay not stale")
		return

	var unsolicited := WorldState.new()
	unsolicited.state=WorldState.STATE_IN_WORLD
	unsolicited.actor_id=11
	unsolicited.map_id=13
	unsolicited.map_epoch=1
	unsolicited.world_ready=true
	unsolicited.last_world_ready_ack_epoch=1
	if unsolicited.apply_frame(_attack_result(1,1,0,100,99,28,false)) or int(unsolicited.state)!=WorldState.STATE_FAILED:
		_fail("unsolicited result did not fail closed")
		return

	var gap := WorldState.new()
	gap.state=WorldState.STATE_IN_WORLD
	gap.actor_id=11
	gap.map_id=13
	gap.map_epoch=1
	gap.world_ready=true
	gap.last_world_ready_ack_epoch=1
	if not gap.apply_frame(_combat_state(1,100,100)):
		_fail("gap baseline")
		return
	if gap.apply_frame(_pvp_damage(1,2,100,99,4,7,10,90,100,false)) or int(gap.state)!=WorldState.STATE_FAILED:
		_fail("shared damage sequence gap did not fail closed")
		return

	print("PVP_ATTACK_WIRE_PASS request=28 result=42 damage=61 visual=71 pvp_skill_request=32 pvp_skill_result=46 pvp_skill_visual=75 shared_skill_singleflight=true pvp_skill_presentation_only=true pvp_skill_replay_stale=true singleflight=true server_status=true shared_damage_sequence=true visual_presentation_only=true visual_replay_stale=true lethal=true replay_stale=true unsolicited_failclosed=true")
	quit(0)

func _combat_state(epoch: int,current_hp: int,max_hp: int) -> PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(32)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PLAYER_COMBAT_STATE)
	writer.put_64(epoch)
	writer.put_32(current_hp)
	writer.put_32(max_hp)
	writer.put_32(0)
	writer.put_32(0)
	return writer.data_array

func _attack_result(epoch: int,sequence: int,status: int,tick: int,target: int,
		damage: int,died: bool) -> PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(42)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PVP_ATTACK_RESULT)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(status)
	writer.put_64(tick)
	writer.put_64(target)
	writer.put_32(damage)
	writer.put_u8(1 if died else 0)
	return writer.data_array

func _pvp_damage(epoch: int,sequence: int,tick: int,attacker: int,connection: int,
		attack_sequence: int,damage: int,current_hp: int,max_hp: int,died: bool) -> PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(61)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PVP_DAMAGE)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_64(tick)
	writer.put_64(attacker)
	writer.put_64(connection)
	writer.put_u32(attack_sequence)
	writer.put_32(damage)
	writer.put_32(current_hp)
	writer.put_32(max_hp)
	writer.put_u8(1 if died else 0)
	return writer.data_array


func _pvp_visual(receiver_epoch:int,tick:int,attacker:int,attacker_connection:int,
		attack_sequence:int,victim:int,victim_connection:int,damage_sequence:int,
		damage:int,died:bool,attacker_facing:int,victim_facing:int)->PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(71)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PVP_COMBAT_VISUAL)
	writer.put_64(receiver_epoch)
	writer.put_64(tick)
	writer.put_64(attacker)
	writer.put_64(attacker_connection)
	writer.put_u32(attack_sequence)
	writer.put_64(victim)
	writer.put_64(victim_connection)
	writer.put_u32(damage_sequence)
	writer.put_32(damage)
	writer.put_u8(1 if died else 0)
	writer.put_u8(attacker_facing)
	writer.put_u8(victim_facing)
	return writer.data_array


func _pvp_skill_result(epoch:int,sequence:int,status:int,tick:int,skill_id:int,
		target:int,damage:int,died:bool)->PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(46)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PVP_SKILL_RESULT)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(status)
	writer.put_64(tick)
	writer.put_32(skill_id)
	writer.put_64(target)
	writer.put_32(damage)
	writer.put_u8(1 if died else 0)
	return writer.data_array

func _pvp_skill_visual(epoch:int,tick:int,attacker:int,ac:int,sequence:int,
		skill_id:int,victim:int,vc:int,damage_sequence:int,damage:int,died:bool,
		attacker_facing:int,victim_facing:int)->PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(75)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PVP_SKILL_VISUAL)
	writer.put_64(epoch)
	writer.put_64(tick)
	writer.put_64(attacker)
	writer.put_64(ac)
	writer.put_u32(sequence)
	writer.put_32(skill_id)
	writer.put_64(victim)
	writer.put_64(vc)
	writer.put_u32(damage_sequence)
	writer.put_32(damage)
	writer.put_u8(1 if died else 0)
	writer.put_u8(attacker_facing)
	writer.put_u8(victim_facing)
	return writer.data_array
