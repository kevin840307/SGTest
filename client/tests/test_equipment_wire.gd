extends SceneTree
const Wire = preload("res://network/wire_codec.gd")
const World = preload("res://world/world_state.gd")
const Profile = preload("res://player/character_appearance_profile.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const INVENTORY := "51000000010024000B0000000000000000000000010200591B0000000000008D0A000002000000F50100000000000002500000005A1B000000000000750E000003000000F60100000000000007FFFFFFFF"
const EQUIP := "1C00000001003A00090000000000000007000000591B000000000000"
const UNEQUIP := "1C00000001003B00090000000000000007000000591B000000000000"
const EMPTY_EQUIPMENT := "17000000010039000B0000000000000000000000010000"
const ONE_EQUIPMENT := "34000000010039000B0000000000000000000000010100591B00000000000004000000750E0000F5010000000000000550000000"
const RESULT := "2E00000001003C000100000000000000010000000100591B000000000000750E000004000000F501000000000000"

func fail(message: String) -> void:
	push_error("EQUIPMENT_WIRE_FAIL "+message)
	quit(1)

func character_with_inventory():
	var world = World.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()):
		return null
	var bag := INVENTORY.hex_decode()
	bag.resize(52)
	bag.encode_s32(0,52)
	bag.encode_u16(21,1)
	bag.encode_s32(31,3701)
	bag.encode_s32(35,1)
	bag.encode_s64(39,501)
	bag[20] = 1
	bag[47] = 5
	bag.encode_s32(48,80)
	if not world.apply_frame(bag):
		return null
	return world

func new_world():
	var world = character_with_inventory()
	if world == null or not world.apply_frame(EMPTY_EQUIPMENT.hex_decode()) or not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42):
		return null
	return world

func presentation_frame(
	receiver_map_epoch:int,server_tick:int,owner_actor_id:int,
	owner_connection_epoch:int,owner_map_epoch:int,revision:int,present:bool,
	head:int,left:int,right:int,body:int,leg:int,
	weapon:int=0,shield:int=0,back:int=0,charm:int=0
)->PackedByteArray:
	var frame:=PackedByteArray()
	frame.resize(Wire.PLAYER_EQUIPMENT_PRESENTATION_FRAME_BYTES)
	frame.encode_s32(0,Wire.PLAYER_EQUIPMENT_PRESENTATION_FRAME_BYTES)
	frame.encode_u16(4,1)
	frame.encode_u16(6,Wire.OPCODE_PLAYER_EQUIPMENT_PRESENTATION)
	frame.encode_s64(8,receiver_map_epoch)
	frame.encode_s64(16,server_tick)
	frame.encode_s64(24,owner_actor_id)
	frame.encode_s64(32,owner_connection_epoch)
	frame.encode_s64(40,owner_map_epoch)
	frame.encode_u32(48,revision)
	frame[52]=1 if present else 0
	var definitions:=[head,left,right,body,leg,weapon,shield,back,charm]
	var offset:=53
	for definition_id in definitions:
		frame.encode_s32(offset,int(definition_id))
		offset+=4
	return frame


func _initialize() -> void:
	if Wire.encode_equipment_equip(9,7,7001).hex_encode().to_upper() != EQUIP or Wire.encode_equipment_unequip(9,7,7001).hex_encode().to_upper() != UNEQUIP:
		fail("UID-only request golden mismatch")
		return
	var empty := Wire.decode_equipment_snapshot(EMPTY_EQUIPMENT.hex_decode())
	var one := Wire.decode_equipment_snapshot(ONE_EQUIPMENT.hex_decode())
	var result := Wire.decode_equipment_transfer_result(RESULT.hex_decode())
	if empty.is_empty() or not bool(empty.complete) or not (empty.items as Array).is_empty() or one.is_empty() or int(one.items[0].item_uid) != 7001 or int(one.items[0].slot_id) != 4 or int(one.items[0].drop_id) != 501 or int(one.items[0].enhancement_level) != 5 or int(one.items[0].durability_raw) != 80 or result.is_empty() or int(result.item_definition_id) != 3701 or int(result.slot_id) != 4:
		fail("server equipment golden mismatch")
		return
	var presentation:=presentation_frame(
		1,43,11,1,1,1,true,0,18569,18605,18502,18537)
	var decoded_presentation:=Wire.decode_player_equipment_presentation(presentation)
	if (
		decoded_presentation.is_empty() or
		int(decoded_presentation.slot_definitions[1])!=18569 or
		int(decoded_presentation.slot_definitions[2])!=18605 or
		int(decoded_presentation.slot_definitions[3])!=18502 or
		int(decoded_presentation.slot_definitions[4])!=18537
	):
		fail("player equipment presentation decoder")
		return
	var base_app:={"race":0,"hair_index":0,"hair_color":1,
		"body_index":0,"leg_index":0,"sleeve_color":0}
	var presented:=Profile.presentation_appearance(
		base_app,decoded_presentation.slot_definitions)
	var overlays:Dictionary=Profile.definition(presented).get("overlays",{})
	if (
		int(presented.get("_equipment_left_color",-9))!=0 or
		int(presented.get("_equipment_right_color",-9))!=2 or
		int(presented.get("_equipment_body_color",-9))!=1 or
		int(presented.get("_equipment_leg_color",-9))!=2 or
		int((overlays.get(5,[]) as Array)[2])!=0 or
		int((overlays.get(7,[]) as Array)[2])!=2 or
		int((overlays.get(9,[]) as Array)[2])!=1 or
		int((overlays.get(12,[]) as Array)[2])!=2
	):
		fail("verified equipment presentation did not preserve independent paper-doll palettes")
		return
	if not (Profile.definition(
		Profile.presentation_appearance(base_app,{})).get(
			"overlays",{}) as Dictionary).is_empty():
		fail("authoritative empty equipment kept creation-time clothing overlays")
		return

	var world = new_world()
	if world == null or not bool(world.snapshot().equipment_ready):
		fail("empty equipment login baseline")
		return
	if not world.apply_frame(presentation):
		fail("player equipment presentation reducer")
		return
	var presented_state:Dictionary=world.snapshot()
	if (
		not (presented_state.player_equipment_presentations as Dictionary).has(11) or
		int(presented_state.player_equipment_presentations[11].slot_definitions[1])!=18569 or
		int(presented_state.player_equipment_presentations[11].revision)!=1
	):
		fail("player equipment presentation was not stored generation-safely")
		return
	var detached:=presentation_frame(
		1,44,11,1,1,0,false,0,0,0,0,0)
	if not world.apply_frame(detached) or not (
		world.snapshot().player_equipment_presentations as Dictionary).is_empty():
		fail("player equipment presentation detach did not clear reducer")
		return
	if not world.mark_equipment_sent(1,1,7001):
		fail("equip request state")
		return
	var applied := RESULT.hex_decode()
	if not world.apply_frame(applied):
		fail("equip receipt")
		return
	var state: Dictionary = world.snapshot()
	if (state.inventory_items as Dictionary).has(7001) or not (state.equipment_items as Dictionary).has(7001) or int(state.equipment_items[7001].slot_id) != 4 or int(state.equipment_slots[4]) != 7001 or int(state.equipment_items[7001].drop_id) != 501 or int(state.equipment_items[7001].enhancement_level) != 5 or int(state.equipment_items[7001].durability_raw) != 80:
		fail("equip did not move exact UID/provenance")
		return
	var revision := int(state.equipment_revision)
	var stale := int(state.stale_equipment_results)
	if not world.apply_frame(applied) or int(world.snapshot().equipment_revision) != revision or int(world.snapshot().stale_equipment_results) != stale+1:
		fail("duplicate equip mutated twice")
		return

	if not world.apply_frame(MAP13_READY.hex_decode()) or not world.mark_world_ready_ack_sent(2,50):
		fail("map transfer/ack rejected")
		return
	state = world.snapshot()
	if not bool(state.equipment_ready) or not (state.equipment_items as Dictionary).has(7001) or int(state.equipment_sequence) != 0 or int(state.pending_equipment_sequence) != 0:
		fail("map transfer lost equipment or retained old sequence")
		return
	if not world.mark_equipment_sent(1,2,7001):
		fail("unequip request after transfer")
		return
	var unequipped := RESULT.hex_decode()
	unequipped.encode_s64(8,2)
	unequipped.encode_u32(16,1)
	unequipped[20] = 2
	if not world.apply_frame(unequipped):
		fail("unequip receipt")
		return
	state = world.snapshot()
	if not (state.inventory_items as Dictionary).has(7001) or (state.equipment_items as Dictionary).has(7001) or (state.equipment_slots as Dictionary).has(4) or int(state.inventory_items[7001].quantity) != 1 or int(state.inventory_items[7001].drop_id) != 501 or int(state.inventory_items[7001].enhancement_level) != 5 or int(state.inventory_items[7001].durability_raw) != 80:
		fail("unequip did not restore exact inventory UID")
		return

	world = new_world()
	if world == null or not world.mark_equipment_sent(1,1,7001):
		fail("AlreadyApplied fixture")
		return
	var replay_recovery := RESULT.hex_decode()
	replay_recovery[21] = 1
	if not world.apply_frame(replay_recovery) or not (world.snapshot().equipment_items as Dictionary).has(7001):
		fail("AlreadyApplied did not converge committed equip")
		return

	world = character_with_inventory()
	if world == null:
		fail("conflict fixture")
		return
	if world.apply_frame(ONE_EQUIPMENT.hex_decode()) or int(world.state) != World.STATE_FAILED or world.protocol_error != "equipment_uid_or_slot_location_conflict":
		fail("same UID admitted in inventory and equipment")
		return

	world = new_world()
	if world == null or not world.mark_equipment_sent(1,1,7001):
		fail("wrong UID fixture")
		return
	var wrong := RESULT.hex_decode()
	wrong.encode_s64(22,7002)
	if world.apply_frame(wrong) or int(world.state) != World.STATE_FAILED:
		fail("wrong equipment receipt UID accepted")
		return

	print("EQUIPMENT_WIRE_PASS equip=28 unequip=28 snapshot=CHUNKED result=46 uid=SERVER_AUTH slot=SERVER_RULE provenance=PRESERVED duplicate=STALE transfer=PERSISTS replay_recovery=ALREADY_APPLIED_CONVERGES spoof=FAIL_CLOSED")
	quit(0)
