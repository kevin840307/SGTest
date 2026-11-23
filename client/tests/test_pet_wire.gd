extends SceneTree
const Codec = preload("res://network/wire_codec.gd")
const State = preload("res://world/world_state.gd")

const COMMAND_HEX := "2500000001006f000700000000000000010000000165000000000000000000000000000000"
const RESULT_HEX := "37000000010070000700000000000000010000000100650000000000000065000000000000000000000000000000000000000000000000"
const PET_EQUIP_COMMAND_HEX := "2500000001006f000700000000000000020000000465000000000000002923000000000000"
const PET_EQUIP_RESULT_HEX := "37000000010070000700000000000000020000000400650000000000000065000000000000002923000000000000020000000000000000"
const PET_EQUIPMENT_SNAPSHOT_HEX := "3400000001008400070000000000000001006500000000000000292300000000000002a51f0000f5010000000000000450000000"
const SNAPSHOT_HEX := "52000000010071000700000000000000660000000000000002006500000000000000891300000200000004000000580000000000000066000000000000008a1300000700000007000000d204000000000000"
const PET_SPAWN_HEX := "4c0000000100720007000000000000002a00000000000000e9030000000000000b00000000000000040000000000000066000000000000008a13000007000000070000000000c04100001042"
const PET_DELTA_HEX := "400000000100740007000000000000002b00000000000000e9030000000000000b00000000000000040000000000000066000000000000000000204200001042"
const PET_DESPAWN_HEX := "380000000100730007000000000000002c00000000000000e9030000000000000b0000000000000004000000000000006600000000000000"
const PET_ATTACK_HEX := "2400000001007500070000000000000003000000660000000000000021a0ae3b00000000"
const PET_ATTACK_RESULT_HEX := "410000000100760007000000000000000300000000660000000000000021a0ae3b00000000190000004b0000006400000004000000000000003700000000000000"
const PET_ATTACK_VISUAL_HEX := "510000000100770007000000000000003700000000000000e9030000000000000b00000000000000040000000000000003000000660000000000000021a0ae3b0000000000190000000400000000000000"
const PET_SKILL_HEX := "280000000100810007000000000000000400000066000000000000002923000021a0ae3b00000000"
const PET_SKILL_RESULT_HEX := "45000000010082000700000000000000040000000066000000000000002923000021a0ae3b000000001e0000002d0000006400000005000000000000003800000000000000"
const PET_SKILL_VISUAL_HEX := "550000000100830007000000000000003800000000000000e9030000000000000b0000000000000004000000000000000400000066000000000000002923000021a0ae3b00000000001e0000000500000000000000"

func _initialize() -> void:
	var command := Codec.encode_pet_command(7,1,1,101)
	if command.hex_encode() != COMMAND_HEX:
		_fail("C# summon command golden mismatch")
		return
	if not Codec.encode_pet_command(7,1,3,101).is_empty():
		_fail("refresh accepted nonzero pet uid")
		return
	var equip_command:=Codec.encode_pet_command(7,2,4,101,9001)
	if equip_command.hex_encode()!=PET_EQUIP_COMMAND_HEX:
		_fail("C# pet equip command golden mismatch")
		return
	if not Codec.encode_pet_command(7,2,4,101,0).is_empty():
		_fail("pet equip accepted zero item uid")
		return
	var equip_result:=Codec.decode_pet_result(PET_EQUIP_RESULT_HEX.hex_decode())
	if (
		equip_result.is_empty() or int(equip_result.item_uid)!=9001 or
		int(equip_result.slot_id)!=2 or int(equip_result.replaced_item_uid)!=0
	):
		_fail("pet equip result golden rejected")
		return
	var equipment_snapshot:=Codec.decode_pet_equipment_snapshot(
		PET_EQUIPMENT_SNAPSHOT_HEX.hex_decode())
	if (
		equipment_snapshot.is_empty() or equipment_snapshot.items.size()!=1 or
		int(equipment_snapshot.items[0].pet_uid)!=101 or
		int(equipment_snapshot.items[0].item_uid)!=9001 or
		int(equipment_snapshot.items[0].slot_id)!=2
	):
		_fail("pet equipment snapshot golden rejected")
		return
	var result := Codec.decode_pet_result(RESULT_HEX.hex_decode())
	if result.is_empty() or result.pet_uid != 101 or result.active_pet_uid != 101 or result.status != 0:
		_fail("pet result golden rejected")
		return
	var snapshot := Codec.decode_pet_snapshot(SNAPSHOT_HEX.hex_decode())
	if snapshot.is_empty() or snapshot.active_pet_uid != 102 or snapshot.pets.size() != 2 or snapshot.pets[0].evolution_id != 2 or snapshot.pets[1].evolution_id != 7:
		_fail("pet snapshot golden rejected")
		return
	var spawn := Codec.decode_pet_entity_spawn(PET_SPAWN_HEX.hex_decode())
	if (
		spawn.is_empty() or spawn.owner_actor_id != 1001 or spawn.pet_uid != 102 or
		spawn.definition_id != 5002 or spawn.evolution_id != 7 or
		spawn.x != 24.0 or spawn.y != 36.0
	):
		_fail("pet AOI spawn golden/position rejected")
		return
	var delta := Codec.decode_pet_entity_delta(PET_DELTA_HEX.hex_decode())
	if (
		delta.is_empty() or delta.owner_actor_id != 1001 or delta.pet_uid != 102 or
		delta.x != 40.0 or delta.y != 36.0
	):
		_fail("pet AOI movement delta golden rejected")
		return
	var despawn := Codec.decode_pet_entity_despawn(PET_DESPAWN_HEX.hex_decode())
	if despawn.is_empty() or despawn.owner_actor_id != 1001 or despawn.pet_uid != 102:
		_fail("pet AOI despawn golden rejected")
		return
	var attack := Codec.encode_pet_attack(7,3,102,1001300001)
	if attack.hex_encode() != PET_ATTACK_HEX or Codec.frame_opcode(attack) != Codec.OPCODE_PET_ATTACK:
		_fail("C# pet attack golden mismatch")
		return
	var attack_result := Codec.decode_pet_attack_result(PET_ATTACK_RESULT_HEX.hex_decode())
	if (
		attack_result.is_empty() or int(attack_result.pet_uid) != 102 or
		int(attack_result.target_entity_id) != 1001300001 or
		int(attack_result.damage) != 25 or int(attack_result.current_hp) != 75 or
		int(attack_result.server_tick) != 55
	):
		_fail("pet attack result golden rejected")
		return

	var attack_visual:=Codec.decode_pet_attack_visual(PET_ATTACK_VISUAL_HEX.hex_decode())
	if (
		attack_visual.is_empty() or Codec.frame_opcode(PET_ATTACK_VISUAL_HEX.hex_decode())!=Codec.OPCODE_PET_ATTACK_VISUAL or
		int(attack_visual.owner_actor_id)!=1001 or int(attack_visual.owner_connection_epoch)!=11 or
		int(attack_visual.owner_map_epoch)!=4 or int(attack_visual.pet_uid)!=102 or
		int(attack_visual.attack_sequence)!=3 or int(attack_visual.damage)!=25
	):
		_fail("pet remote attack visual golden rejected")
		return
	var pet_skill:=Codec.encode_pet_skill(7,4,102,9001,1001300001)
	if pet_skill.hex_encode()!=PET_SKILL_HEX or Codec.frame_opcode(pet_skill)!=Codec.OPCODE_PET_SKILL:
		_fail("C# pet skill golden mismatch")
		return
	var pet_skill_result:=Codec.decode_pet_skill_result(PET_SKILL_RESULT_HEX.hex_decode())
	if (
		pet_skill_result.is_empty() or int(pet_skill_result.pet_uid)!=102 or
		int(pet_skill_result.skill_id)!=9001 or
		int(pet_skill_result.target_entity_id)!=1001300001 or
		int(pet_skill_result.damage)!=30 or int(pet_skill_result.current_hp)!=45 or
		int(pet_skill_result.server_tick)!=56
	):
		_fail("pet skill result golden rejected")
		return
	var pet_skill_visual:=Codec.decode_pet_skill_visual(PET_SKILL_VISUAL_HEX.hex_decode())
	if (
		pet_skill_visual.is_empty() or
		Codec.frame_opcode(PET_SKILL_VISUAL_HEX.hex_decode())!=Codec.OPCODE_PET_SKILL_VISUAL or
		int(pet_skill_visual.owner_actor_id)!=1001 or int(pet_skill_visual.pet_uid)!=102 or
		int(pet_skill_visual.skill_id)!=9001 or int(pet_skill_visual.skill_sequence)!=4 or
		int(pet_skill_visual.damage)!=30
	):
		_fail("pet remote skill visual golden rejected")
		return

	var remote_state:=State.new()
	remote_state.state=State.STATE_IN_WORLD
	remote_state.world_ready=true
	remote_state.map_epoch=7
	remote_state.actor_id=2002
	remote_state.last_world_ready_ack_epoch=7
	remote_state.entities[1001]={"connection_epoch":11,"entity_map_epoch":4}
	remote_state.monsters[1001300001]={"revision":3}
	if not remote_state.apply_frame(PET_SPAWN_HEX.hex_decode()):
		_fail("remote observer pet spawn reducer failed")
		return
	if not remote_state.apply_frame(PET_ATTACK_VISUAL_HEX.hex_decode()):
		_fail("remote observer pet attack visual reducer failed")
		return
	var remote_view:=remote_state.snapshot()
	if remote_view.pet_attack_visual_events.size()!=1 or int(remote_view.pet_attack_visual_events[0].attack_sequence)!=3:
		_fail("remote observer pet attack visual was not published")
		return
	if not remote_state.apply_frame(PET_ATTACK_VISUAL_HEX.hex_decode()):
		_fail("duplicate remote observer pet attack visual was not safely ignored")
		return
	remote_view=remote_state.snapshot()
	if remote_view.pet_attack_visual_events.size()!=1 or int(remote_view.stale_pet_attack_visuals)!=1:
		_fail("duplicate remote pet attack visual replayed instead of deduping")
		return
	if not remote_state.apply_frame(PET_SKILL_VISUAL_HEX.hex_decode()):
		_fail("remote observer pet skill visual reducer failed")
		return
	remote_view=remote_state.snapshot()
	if remote_view.pet_skill_visual_events.size()!=1 or int(remote_view.pet_skill_visual_events[0].skill_id)!=9001:
		_fail("remote observer pet skill visual was not published")
		return
	if not remote_state.apply_frame(PET_SKILL_VISUAL_HEX.hex_decode()):
		_fail("duplicate remote observer pet skill visual was not safely ignored")
		return
	remote_view=remote_state.snapshot()
	if remote_view.pet_skill_visual_events.size()!=1 or int(remote_view.stale_pet_skill_visuals)!=1:
		_fail("duplicate remote pet skill visual replayed instead of deduping")
		return

	var state := State.new()
	state.state = State.STATE_IN_WORLD
	state.world_ready = true
	state.map_epoch = 7
	state.actor_id = 1001
	state.last_world_ready_ack_epoch = 7
	if not state.apply_frame(SNAPSHOT_HEX.hex_decode()):
		_fail("pet snapshot reducer failed")
		return
	if not state.apply_frame(PET_SPAWN_HEX.hex_decode()):
		_fail("pet AOI spawn reducer failed")
		return
	var pet_entity:Dictionary = state.snapshot().pet_entities[1001]
	if int(pet_entity.pet_uid) != 102 or float(pet_entity.x) != 24.0 or float(pet_entity.y) != 36.0:
		_fail("pet AOI spawn state/position missing")
		return
	if not state.apply_frame(PET_DELTA_HEX.hex_decode()):
		_fail("pet AOI movement delta reducer failed")
		return
	pet_entity=state.snapshot().pet_entities[1001]
	if (
		int(pet_entity.pet_uid) != 102 or int(pet_entity.definition_id) != 5002 or
		int(pet_entity.evolution_id) != 7 or
		float(pet_entity.x) != 40.0 or float(pet_entity.y) != 36.0 or
		int(pet_entity.server_tick) != 43
	):
		_fail("pet AOI movement delta state missing or identity changed")
		return
	if not state.mark_pet_attack_sent(3,102,1001300001):
		_fail("valid pet attack request rejected")
		return
	if not state.apply_frame(PET_ATTACK_RESULT_HEX.hex_decode()):
		_fail("pet attack result reducer failed")
		return
	var combat_view:=state.snapshot()
	if (
		int(combat_view.pending_pet_attack_sequence)!=0 or
		int(combat_view.last_pet_attack_result_sequence)!=3 or
		int(combat_view.last_pet_attack_result_status)!=0 or
		int(combat_view.last_pet_attack_result.damage)!=25
	):
		_fail("pet attack reducer authority state mismatch")
		return
	if not state.mark_pet_skill_sent(4,102,9001,1001300001):
		_fail("valid pet skill request rejected")
		return
	if not state.apply_frame(PET_SKILL_RESULT_HEX.hex_decode()):
		_fail("pet skill result reducer failed")
		return
	combat_view=state.snapshot()
	if (
		int(combat_view.pending_pet_skill_sequence)!=0 or
		int(combat_view.last_pet_skill_result_sequence)!=4 or
		int(combat_view.last_pet_skill_result_status)!=0 or
		int(combat_view.last_pet_skill_result.skill_id)!=9001 or
		int(combat_view.last_pet_skill_result.damage)!=30
	):
		_fail("pet skill reducer authority state mismatch")
		return
	if not state.apply_frame(PET_DESPAWN_HEX.hex_decode()) or not state.snapshot().pet_entities.is_empty():
		_fail("pet AOI despawn reducer failed")
		return
	if not state.mark_pet_sent(1,1,101):
		_fail("valid pet summon request rejected")
		return
	if not state.apply_frame(RESULT_HEX.hex_decode()):
		_fail("pet result reducer failed")
		return
	var view := state.snapshot()
	if (
		view.active_pet_uid != 101 or view.pet_instances.size() != 2 or
		view.pending_pet_sequence != 0 or not view.pet_snapshot_received or
		int(view.pet_instances[101].definition_id) != 5001 or
		int(view.pet_instances[101].evolution_id) != 2
	):
		_fail("pet reducer state mismatch")
		return

	state.inventory_items[9001]={
		"item_uid":9001,"item_definition_id":8101,"quantity":1,
		"drop_id":501,"enhancement_level":4,"durability_raw":80
	}
	if not state.mark_pet_sent(2,4,101,9001):
		_fail("valid pet equip request rejected")
		return
	if not state.apply_frame(PET_EQUIP_RESULT_HEX.hex_decode()):
		_fail("pet equip result reducer failed")
		return
	if not state.apply_frame(PET_EQUIPMENT_SNAPSHOT_HEX.hex_decode()):
		_fail("pet equipment snapshot reducer failed")
		return
	view=state.snapshot()
	if (
		int(view.pending_pet_sequence)!=0 or int(view.pending_pet_item_uid)!=0 or
		not bool(view.pet_equipment_snapshot_received) or
		not view.pet_equipment_items.has(9001) or view.inventory_items.has(9001) or
		int(view.pet_equipment_items[9001].slot_id)!=2
	):
		_fail("pet equipment authority reconciliation mismatch")
		return

	var stale_snapshot := SNAPSHOT_HEX.hex_decode()
	stale_snapshot.encode_s64(8,6)
	if not state.apply_frame(stale_snapshot) or int(state.snapshot().active_pet_uid) != 101:
		_fail("stale pet snapshot changed current state")
		return

	print("PET_WIRE_PASS csharp_golden=true reducer=true equipment=true aoi_presence=true aoi_movement=true pet_combat=true pet_skill=true remote_attack_visual=true remote_skill_visual=true pets=2")
	quit(0)

func _fail(message:String)->void:
	push_error("PET_WIRE_FAIL "+message)
	quit(1)
