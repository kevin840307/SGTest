extends SceneTree
const Wire = preload("res://network/wire_codec.gd")
const World = preload("res://world/world_state.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const INVENTORY := "34000000010024000B0000000000000000000000010100411F0000000000000710000002000000F5010000000000000550000000"
const REQUEST := "3500000001004600010000000000000001000000010000000000000000160000000000000000000000000000000000000000000000"
const INVITE := "3B000000010047000100000000000000591B0000000000000100000002160000000000000000000000000000000000000000000000000000000000"
const ACTIVE := "3B000000010047000100000000000000591B0000000000000200000003160000000000000000000000000000000000000000000000000000000000"
const OFFER := "58000000010047000100000000000000591B0000000000000300000003160000000000000000000000000000000000000000000000000000000100411F0000000000000710000002000000F5010000000000000550000000"
const MONEY := "58000000010047000100000000000000591B00000000000004000000031600000000000000C8000000000000000000000000000000000000000100411F0000000000000710000002000000F5010000000000000550000000"
const LOCK1 := "58000000010047000100000000000000591B00000000000005000000031600000000000000C8000000000000000000000000000000010000000100411F0000000000000710000002000000F5010000000000000550000000"
const BOTH_LOCK := "75000000010047000100000000000000591B00000000000006000000041600000000000000C8000000000000003200000000000000010100000101411F0000000000000710000002000000F501000000000000055000000029230000000000000810000001000000F601000000000000073C000000"
const CONFIRM1 := "75000000010047000100000000000000591B00000000000007000000041600000000000000C8000000000000003200000000000000010101000101411F0000000000000710000002000000F501000000000000055000000029230000000000000810000001000000F601000000000000073C000000"
const BOTH_CONF := "75000000010047000100000000000000591B00000000000008000000041600000000000000C8000000000000003200000000000000010101010101411F0000000000000710000002000000F501000000000000055000000029230000000000000810000001000000F601000000000000073C000000"
const TERMINAL := "25000000010048000100000000000000591B00000000000008000000005203000000000000"
const WRONG_TERMINAL := "250000000100480001000000000000005A1B00000000000008000000005203000000000000"

func fail(message: String) -> void:
	push_error("TRADE_WIRE_FAIL "+message)
	quit(1)

func new_world():
	var world = World.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()) or not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42) or not world.apply_frame(INVENTORY.hex_decode()):
		return null
	return world

func _initialize() -> void:
	if Wire.encode_trade_command(1,1,1,0,22,0,0).hex_encode().to_upper() != REQUEST:
		fail("request golden mismatch")
		return
	var decoded := Wire.decode_trade_snapshot(BOTH_CONF.hex_decode())
	var terminal := Wire.decode_trade_terminal(TERMINAL.hex_decode())
	if decoded.is_empty() or int(decoded.trade_id) != 7001 or decoded.my_items.size() != 1 or decoded.other_items.size() != 1 or int(decoded.my_items[0].enhancement_level) != 5 or int(decoded.other_items[0].enhancement_level) != 7 or int(decoded.my_items[0].durability_raw) != 80 or int(decoded.other_items[0].durability_raw) != 60 or terminal.is_empty() or int(terminal.wallet_money) != 850:
		fail("server golden decode mismatch")
		return

	var bad_durability := BOTH_CONF.hex_decode()
	bad_durability.encode_s32(84,-2)
	if not Wire.decode_trade_snapshot(bad_durability).is_empty():
		fail("trade snapshot admitted durability below -1")
		return

	var world = new_world()
	if world == null or not world.mark_trade_sent(1,1,0,22,0,0) or not world.apply_frame(INVITE.hex_decode()):
		fail("trade invite reducer")
		return
	if not world.apply_frame(ACTIVE.hex_decode()) or not world.mark_trade_sent(2,4,7001,0,8001,0) or not world.apply_frame(OFFER.hex_decode()):
		fail("trade active/item offer reducer")
		return
	if not world.mark_trade_sent(3,6,7001,0,0,200) or not world.apply_frame(MONEY.hex_decode()) or not world.mark_trade_sent(4,7,7001,0,0,0) or not world.apply_frame(LOCK1.hex_decode()):
		fail("trade money/lock reducer")
		return
	if not world.apply_frame(BOTH_LOCK.hex_decode()) or not world.mark_trade_sent(5,8,7001,0,0,0) or not world.apply_frame(CONFIRM1.hex_decode()) or not world.apply_frame(BOTH_CONF.hex_decode()):
		fail("trade dual confirmation reducer")
		return
	if not world.apply_frame(TERMINAL.hex_decode()):
		fail("trade success terminal rejected")
		return
	var state: Dictionary = world.snapshot()
	if int(state.trade_id) != 0 or (state.inventory_items as Dictionary).has(8001) or not (state.inventory_items as Dictionary).has(9001) or int(state.inventory_items[9001].item_definition_id) != 4104 or int(state.inventory_items[9001].quantity) != 1 or int(state.inventory_items[9001].drop_id) != 502 or int(state.inventory_items[9001].enhancement_level) != 7 or int(state.inventory_items[9001].durability_raw) != 60 or int(state.last_trade_terminal.status) != 0:
		fail("trade terminal did not atomically swap authoritative UIDs")
		return
	var inventory_revision := int(state.inventory_revision)
	var stale := int(state.stale_trade_frames)
	if not world.apply_frame(TERMINAL.hex_decode()) or int(world.snapshot().inventory_revision) != inventory_revision or int(world.snapshot().stale_trade_frames) != stale+1:
		fail("trade terminal replay mutated inventory twice")
		return

	world = new_world()
	if world == null or not world.mark_trade_sent(1,1,0,22,0,0) or not world.apply_frame(INVITE.hex_decode()):
		fail("spoof fixture")
		return
	if world.apply_frame(WRONG_TERMINAL.hex_decode()) or int(world.state) != World.STATE_FAILED:
		fail("wrong trade session terminal accepted")
		return

	world = new_world()
	if world == null or not world.mark_trade_sent(1,1,0,22,0,0) or not world.apply_frame(INVITE.hex_decode()) or not world.apply_frame(MAP13_READY.hex_decode()):
		fail("map transfer fixture")
		return
	state = world.snapshot()
	if int(state.trade_id) != 0 or int(state.trade_state) != 0 or not (state.trade_my_items as Dictionary).is_empty() or not (state.trade_other_items as Dictionary).is_empty():
		fail("map transfer did not clear ephemeral trade")
		return

	print("TRADE_WIRE_PASS command=53 snapshot=SERVER_AUTH level=5_to_7 durability=80_to_60 terminal=37 uid_swap=ONCE replay=STALE spoof=FAIL_CLOSED transfer=CANCELS")
	quit(0)
