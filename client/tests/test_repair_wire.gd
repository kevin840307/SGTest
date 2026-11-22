extends SceneTree
const Wire = preload("res://network/wire_codec.gd")
const World = preload("res://world/world_state.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const INVENTORY := "51000000010024000B0000000000000000000000010200591B0000000000008D0A000002000000F50100000000000000FFFFFFFF5A1B000000000000750E000003000000F60100000000000000FFFFFFFF"
const OPEN := "180000000100420001000000000000000100000058000000"
const ITEM := "200000000100440001000000000000000200000058000000591B000000000000"
const SNAPSHOT := "45000000010043000100000000000000010000000058000000640000000000000000000000010100591B000000000000750E00000150000000640000001400000000000000"
const RESULT := "3E000000010045000100000000000000020000000058000000591B000000000000750E000001640000006400000014000000000000005000000000000000"

func fail(message: String) -> void:
	push_error("REPAIR_WIRE_FAIL "+message)
	quit(1)

func new_world():
	var world = World.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()) or not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42):
		return null
	var bag := INVENTORY.hex_decode()
	bag.resize(52)
	bag.encode_s32(0,52)
	bag.encode_u16(21,1)
	bag.encode_s32(31,3701)
	bag.encode_s32(35,1)
	bag.encode_s64(39,501)
	bag[20] = 1
	if not world.apply_frame(bag):
		return null
	return world

func _initialize() -> void:
	if Wire.encode_repair_open(1,1,88).hex_encode().to_upper() != OPEN or Wire.encode_repair_item(1,2,88,7001).hex_encode().to_upper() != ITEM:
		fail("NPC/UID-only request golden mismatch")
		return
	var snapshot := Wire.decode_repair_snapshot(SNAPSHOT.hex_decode())
	var result := Wire.decode_repair_result(RESULT.hex_decode())
	if snapshot.is_empty() or int(snapshot.wallet_money) != 100 or int(snapshot.items[0].item_uid) != 7001 or int(snapshot.items[0].current_durability) != 80 or int(snapshot.items[0].max_durability) != 100 or int(snapshot.items[0].cost) != 20 or result.is_empty() or int(result.wallet_money) != 80 or int(result.current_durability) != 100 or int(result.cost) != 20:
		fail("CSharp repair golden mismatch")
		return

	var world = new_world()
	if world == null or not world.mark_repair_sent(1,1,88,0) or not world.apply_frame(SNAPSHOT.hex_decode()):
		fail("repair open reducer")
		return
	var state: Dictionary = world.snapshot()
	if not bool(state.repair_ready) or int(state.active_repair_npc) != 88 or int(state.repair_wallet_money) != 100 or not (state.repair_items as Dictionary).has(7001) or int(state.repair_items[7001].current_durability) != 80 or int(state.repair_items[7001].cost) != 20:
		fail("authoritative repair preview was not retained")
		return

	if not world.mark_repair_sent(2,2,88,7001) or not world.apply_frame(RESULT.hex_decode()):
		fail("repair settlement reducer")
		return
	state = world.snapshot()
	if int(state.repair_wallet_money) != 80 or int(state.repair_items[7001].current_durability) != 100 or int(state.repair_items[7001].cost) != 0 or int(state.last_repair_result.cost) != 20:
		fail("repair result did not converge server durability/wallet")
		return
	var revision := int(state.repair_revision)
	var stale := int(state.stale_repair_results)
	if not world.apply_frame(RESULT.hex_decode()) or int(world.snapshot().repair_revision) != revision or int(world.snapshot().stale_repair_results) != stale+1:
		fail("duplicate repair result mutated twice")
		return

	if not world.apply_frame(MAP13_READY.hex_decode()) or not world.mark_world_ready_ack_sent(2,50):
		fail("map transfer/ack")
		return
	state = world.snapshot()
	if bool(state.repair_ready) or int(state.active_repair_npc) != 0 or int(state.repair_sequence) != 0 or int(state.pending_repair_sequence) != 0 or not (state.repair_items as Dictionary).is_empty():
		fail("map transfer retained stale repair session")
		return

	world = new_world()
	if world == null or not world.mark_repair_sent(1,1,88,0) or not world.apply_frame(SNAPSHOT.hex_decode()) or not world.mark_repair_sent(2,2,88,7001):
		fail("wrong repair receipt fixture")
		return
	var wrong := RESULT.hex_decode()
	wrong.encode_s64(25,7002)
	if world.apply_frame(wrong) or int(world.state) != World.STATE_FAILED:
		fail("wrong repair UID result accepted")
		return

	print("REPAIR_WIRE_PASS open=24 item=32 snapshot=CHUNKED result=62 request=NPC_UID_ONLY durability=SERVER_AUTH cost=SERVER_AUTH wallet=SERVER_AUTH duplicate=STALE transfer=CLOSES spoof=FAIL_CLOSED")
	quit(0)
