extends SceneTree
const Wire = preload("res://network/wire_codec.gd")
const World = preload("res://world/world_state.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const INVENTORY := "51000000010024000B0000000000000000000000010200591B0000000000008D0A000002000000F50100000000000002FFFFFFFF5A1B000000000000750E000003000000F60100000000000007FFFFFFFF"
const OPEN := "1800000001005E0001000000000000000100000063000000"
const ITEM := "200000000100600001000000000000000200000063000000591B000000000000"
const SNAPSHOT := "4400000001005F000100000000000000010000000063000000C80000000000000000000000010100591B0000000000004516000001000140420F00640000000000000001"
const RESULT := "46000000010061000100000000000000020000000063000000591B00000000000045160000010001010164000000000000006400000000000000640000000000000000000000"

func fail(message: String) -> void:
	push_error("ENHANCEMENT_WIRE_FAIL "+message)
	quit(1)

func new_world():
	var world = World.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()) or not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42):
		return null
	var bag := INVENTORY.hex_decode()
	bag.resize(52)
	bag.encode_s32(0,52)
	bag.encode_u16(21,1)
	bag.encode_s32(31,5701)
	bag.encode_s32(35,1)
	bag.encode_s64(39,501)
	bag[20] = 1
	bag[47] = 0
	if not world.apply_frame(bag):
		return null
	return world

func _initialize() -> void:
	if Wire.encode_enhancement_open(1,1,99).hex_encode().to_upper() != OPEN or Wire.encode_enhancement_item(1,2,99,7001).hex_encode().to_upper() != ITEM:
		fail("NPC/UID-only request golden mismatch")
		return
	var snapshot := Wire.decode_enhancement_snapshot(SNAPSHOT.hex_decode())
	var result := Wire.decode_enhancement_result(RESULT.hex_decode())
	if snapshot.is_empty() or int(snapshot.wallet_money) != 200 or int(snapshot.items[0].item_uid) != 7001 or int(snapshot.items[0].item_definition_id) != 5701 or int(snapshot.items[0].current_level) != 0 or int(snapshot.items[0].target_level) != 1 or int(snapshot.items[0].success_ppm) != 1000000 or int(snapshot.items[0].money_cost) != 100 or not bool(snapshot.items[0].consume_money_on_failure) or result.is_empty() or int(result.wallet_money) != 100 or int(result.current_level) != 1 or int(result.money_cost) != 100 or int(result.money_charged) != 100 or not bool(result.succeeded):
		fail("CSharp enhancement golden mismatch")
		return

	var world = new_world()
	if world == null or not world.mark_enhancement_sent(1,1,99,0) or not world.apply_frame(SNAPSHOT.hex_decode()):
		fail("enhancement open reducer")
		return
	var state: Dictionary = world.snapshot()
	if not bool(state.enhancement_ready) or int(state.active_enhancement_npc) != 99 or int(state.enhancement_wallet_money) != 200 or not (state.enhancement_items as Dictionary).has(7001) or int(state.enhancement_items[7001].current_level) != 0 or int(state.enhancement_items[7001].target_level) != 1 or int(state.enhancement_items[7001].success_ppm) != 1000000 or int(state.enhancement_items[7001].money_cost) != 100:
		fail("authoritative enhancement preview was not retained")
		return

	if not world.mark_enhancement_sent(2,2,99,7001) or not world.apply_frame(RESULT.hex_decode()):
		fail("enhancement settlement reducer")
		return
	state = world.snapshot()
	if int(state.enhancement_wallet_money) != 100 or (state.enhancement_items as Dictionary).has(7001) or int((state.inventory_items[7001] as Dictionary).get("enhancement_level",-1)) != 1 or int(state.last_enhancement_result.money_charged) != 100:
		fail("enhancement result did not converge server level/wallet")
		return
	var revision := int(state.enhancement_revision)
	var stale := int(state.stale_enhancement_results)
	if not world.apply_frame(RESULT.hex_decode()) or int(world.snapshot().enhancement_revision) != revision or int(world.snapshot().stale_enhancement_results) != stale+1:
		fail("duplicate enhancement result mutated twice")
		return

	if not world.apply_frame(MAP13_READY.hex_decode()) or not world.mark_world_ready_ack_sent(2,50):
		fail("map transfer/ack")
		return
	state = world.snapshot()
	if bool(state.enhancement_ready) or int(state.active_enhancement_npc) != 0 or int(state.enhancement_sequence) != 0 or int(state.pending_enhancement_sequence) != 0 or not (state.enhancement_items as Dictionary).is_empty():
		fail("map transfer retained stale enhancement session")
		return

	world = new_world()
	if world == null or not world.mark_enhancement_sent(1,1,99,0) or not world.apply_frame(SNAPSHOT.hex_decode()) or not world.mark_enhancement_sent(2,2,99,7001):
		fail("wrong enhancement receipt fixture")
		return
	var wrong := RESULT.hex_decode()
	wrong.encode_s64(25,7002)
	if world.apply_frame(wrong) or int(world.state) != World.STATE_FAILED:
		fail("wrong enhancement UID result accepted")
		return

	print("ENHANCEMENT_WIRE_PASS open=24 item=32 snapshot=CHUNKED result=70 request=NPC_UID_ONLY probability=SERVER_AUTH cost=SERVER_AUTH wallet=SERVER_AUTH level=SERVER_AUTH duplicate=STALE transfer=CLOSES spoof=FAIL_CLOSED")
	quit(0)
