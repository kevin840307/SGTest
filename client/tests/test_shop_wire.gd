extends SceneTree
const Wire = preload("res://network/wire_codec.gd")
const World = preload("res://network/world_state.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const INVENTORY := "47000000010024000B0000000000000000000000010200591B0000000000008D0A000002000000F5010000000000005A1B000000000000750E000003000000F601000000000000"
const OPEN := "1800000001002F0009000000000000000700000063000000"
const SNAPSHOT := "37000000010030000900000000000000070000000063000000D2040000000000000100E90300000A000000000000000500000000000000"
const BUY := "200000000100310009000000000000000800000063000000E903000002000000"
const SELL := "280000000100320009000000000000000900000063000000591B000000000000E903000001000000"
const RESULT := "3600000001003300090000000000000008000000010063000000BE04000000000000411F000000000000E90300000200000002000000"

func fail(message: String) -> void:
	push_error("SHOP_WIRE_FAIL "+message)
	quit(1)

func new_world():
	var world = World.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()) or not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42):
		return null
	var bag := INVENTORY.hex_decode()
	bag.resize(47)
	bag.encode_s32(0,47)
	bag.encode_u16(21,1)
	bag.encode_s32(31,1001)
	bag.encode_s32(35,2)
	bag[20] = 1
	if not world.apply_frame(bag):
		return null
	return world

func _initialize() -> void:
	if Wire.encode_shop_open(9,7,99).hex_encode().to_upper() != OPEN or Wire.encode_shop_buy(9,8,99,1001,2).hex_encode().to_upper() != BUY or Wire.encode_shop_sell(9,9,99,7001,1001,1).hex_encode().to_upper() != SELL:
		fail("request golden mismatch")
		return
	var decoded := Wire.decode_shop_snapshot(SNAPSHOT.hex_decode())
	var result := Wire.decode_shop_transaction_result(RESULT.hex_decode())
	if decoded.is_empty() or int(decoded.money) != 1234 or int(decoded.items[0].buy_price) != 10 or int(decoded.items[0].sell_price) != 5 or result.is_empty() or int(result.item_uid) != 8001 or int(result.money) != 1214:
		fail("server result golden mismatch")
		return
	var shop_inventory := INVENTORY.hex_decode()
	shop_inventory.resize(47)
	shop_inventory.encode_s32(0,47)
	shop_inventory.encode_u16(21,1)
	shop_inventory.encode_s32(31,1001)
	shop_inventory.encode_s32(35,1)
	shop_inventory.encode_s64(39,0)
	shop_inventory[20] = 1
	if Wire.decode_inventory_snapshot(shop_inventory).is_empty():
		fail("shop-origin inventory source_drop_id=0 rejected")
		return

	var world = new_world()
	if world == null or not world.mark_shop_sent(1,1,99):
		fail("open state")
		return
	var opened := SNAPSHOT.hex_decode()
	opened.encode_s64(8,1)
	opened.encode_u32(16,1)
	opened.encode_s64(25,0)
	if not world.apply_frame(opened) or int(world.snapshot().active_shop_npc) != 99:
		fail("open reducer")
		return

	if not world.mark_shop_sent(2,3,99,1001,7001,2):
		fail("sell state")
		return
	var sold := RESULT.hex_decode()
	sold.encode_s64(8,1)
	sold.encode_u32(16,2)
	sold[20] = 2
	sold.encode_s64(26,10)
	sold.encode_s64(34,7001)
	sold.encode_s32(46,2)
	sold.encode_s32(50,0)
	if not world.apply_frame(sold) or not (world.snapshot().inventory_items as Dictionary).is_empty() or int(world.snapshot().shop_money) != 10:
		fail("sell settlement")
		return
	var stale := int(world.snapshot().stale_shop_results)
	if not world.apply_frame(sold) or int(world.snapshot().stale_shop_results) != stale+1:
		fail("duplicate result")
		return

	if not world.mark_shop_sent(3,2,99,1001,0,1):
		fail("buy state")
		return
	var bought := RESULT.hex_decode()
	bought.encode_s64(8,1)
	bought.encode_u32(16,3)
	bought.encode_s64(26,0)
	bought.encode_s64(34,8001)
	bought.encode_s32(46,1)
	bought.encode_s32(50,1)
	if not world.apply_frame(bought):
		fail("buy settlement")
		return
	var state: Dictionary = world.snapshot()
	if not (state.inventory_items as Dictionary).has(8001) or int(state.inventory_items[8001].drop_id) != 0 or int(state.shop_money) != 0:
		fail("authoritative bought UID")
		return
	if not world.apply_frame(MAP13_READY.hex_decode()) or int(world.snapshot().active_shop_npc) != 0 or int(world.snapshot().shop_sequence) != 0:
		fail("transfer did not close shop")
		return

	world = new_world()
	if world == null or not world.mark_shop_sent(1,1,99) or not world.apply_frame(opened) or not world.mark_shop_sent(2,3,99,1001,7001,1):
		fail("spoof fixture")
		return
	var wrong := sold.duplicate()
	wrong.encode_s32(46,1)
	wrong.encode_s32(50,1)
	wrong.encode_s64(34,7002)
	if world.apply_frame(wrong) or int(world.state) != World.STATE_FAILED:
		fail("wrong sell UID accepted")
		return
	print("SHOP_WIRE_PASS open=24 snapshot=55 buy=32 sell=40 result=54 source_drop_zero=PASS money=SERVER_AUTH uid=SERVER_AUTH replay=STALE transfer=CLOSES spoof=FAIL_CLOSED")
	quit(0)
