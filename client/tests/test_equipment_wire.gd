extends SceneTree
const Wire = preload("res://network/wire_codec.gd")
const World = preload("res://network/world_state.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const INVENTORY := "47000000010024000B0000000000000000000000010200591B0000000000008D0A000002000000F5010000000000005A1B000000000000750E000003000000F601000000000000"
const EQUIP := "1C00000001003A00090000000000000007000000591B000000000000"
const UNEQUIP := "1C00000001003B00090000000000000007000000591B000000000000"
const EMPTY_EQUIPMENT := "17000000010039000B0000000000000000000000010000"
const ONE_EQUIPMENT := "2F000000010039000B0000000000000000000000010100591B00000000000004000000750E0000F501000000000000"
const RESULT := "2E00000001003C000100000000000000010000000100591B000000000000750E000004000000F501000000000000"

func fail(message: String) -> void:
	push_error("EQUIPMENT_WIRE_FAIL "+message)
	quit(1)

func new_world():
	var world = World.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()) or not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42):
		return null
	var bag := INVENTORY.hex_decode()
	bag.resize(47)
	bag.encode_s32(0,47)
	bag.encode_u16(21,1)
	bag.encode_s32(31,3701)
	bag.encode_s32(35,1)
	bag.encode_s64(39,501)
	bag[20] = 1
	if not world.apply_frame(bag):
		return null
	return world

func _initialize() -> void:
	if Wire.encode_equipment_equip(9,7,7001).hex_encode().to_upper() != EQUIP or Wire.encode_equipment_unequip(9,7,7001).hex_encode().to_upper() != UNEQUIP:
		fail("UID-only request golden mismatch")
		return
	var empty := Wire.decode_equipment_snapshot(EMPTY_EQUIPMENT.hex_decode())
	var one := Wire.decode_equipment_snapshot(ONE_EQUIPMENT.hex_decode())
	var result := Wire.decode_equipment_transfer_result(RESULT.hex_decode())
	if empty.is_empty() or not bool(empty.complete) or not (empty.items as Array).is_empty() or one.is_empty() or int(one.items[0].item_uid) != 7001 or int(one.items[0].slot_id) != 4 or int(one.items[0].drop_id) != 501 or result.is_empty() or int(result.item_definition_id) != 3701 or int(result.slot_id) != 4:
		fail("server equipment golden mismatch")
		return

	var world = new_world()
	if world == null or not world.apply_frame(EMPTY_EQUIPMENT.hex_decode()) or not bool(world.snapshot().equipment_ready):
		fail("empty equipment login baseline")
		return
	if not world.mark_equipment_sent(1,1,7001):
		fail("equip request state")
		return
	var applied := RESULT.hex_decode()
	if not world.apply_frame(applied):
		fail("equip receipt")
		return
	var state: Dictionary = world.snapshot()
	if (state.inventory_items as Dictionary).has(7001) or not (state.equipment_items as Dictionary).has(7001) or int(state.equipment_items[7001].slot_id) != 4 or int(state.equipment_slots[4]) != 7001 or int(state.equipment_items[7001].drop_id) != 501:
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
	if not (state.inventory_items as Dictionary).has(7001) or (state.equipment_items as Dictionary).has(7001) or (state.equipment_slots as Dictionary).has(4) or int(state.inventory_items[7001].quantity) != 1 or int(state.inventory_items[7001].drop_id) != 501:
		fail("unequip did not restore exact inventory UID")
		return

	world = new_world()
	if world == null or not world.apply_frame(EMPTY_EQUIPMENT.hex_decode()) or not world.mark_equipment_sent(1,1,7001):
		fail("AlreadyApplied fixture")
		return
	var replay_recovery := RESULT.hex_decode()
	replay_recovery[21] = 1
	if not world.apply_frame(replay_recovery) or not (world.snapshot().equipment_items as Dictionary).has(7001):
		fail("AlreadyApplied did not converge committed equip")
		return

	world = new_world()
	if world == null:
		fail("conflict fixture")
		return
	if world.apply_frame(ONE_EQUIPMENT.hex_decode()) or int(world.state) != World.STATE_FAILED:
		fail("same UID admitted in inventory and equipment")
		return

	world = new_world()
	if world == null or not world.apply_frame(EMPTY_EQUIPMENT.hex_decode()) or not world.mark_equipment_sent(1,1,7001):
		fail("wrong UID fixture")
		return
	var wrong := RESULT.hex_decode()
	wrong.encode_s64(22,7002)
	if world.apply_frame(wrong) or int(world.state) != World.STATE_FAILED:
		fail("wrong equipment receipt UID accepted")
		return

	print("EQUIPMENT_WIRE_PASS equip=28 unequip=28 snapshot=CHUNKED result=46 uid=SERVER_AUTH slot=SERVER_RULE provenance=PRESERVED duplicate=STALE transfer=PERSISTS replay_recovery=ALREADY_APPLIED_CONVERGES spoof=FAIL_CLOSED")
	quit(0)
