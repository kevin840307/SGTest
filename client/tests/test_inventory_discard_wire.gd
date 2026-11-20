extends SceneTree
const Wire = preload("res://network/wire_codec.gd")
const World = preload("res://network/world_state.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const INVENTORY := "49000000010024000B0000000000000000000000010200591B0000000000008D0A000002000000F501000000000000005A1B000000000000750E000003000000F60100000000000000"
const REQUEST := "2000000001002700090000000000000007000000591B00000000000002000000"
const RESULT := "290000000100280009000000000000000700000000591B000000000000E90300000200000001000000"

func fail(message: String) -> void:
	push_error("INVENTORY_DISCARD_WIRE_FAIL "+message)
	quit(1)

func new_world():
	var world = World.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()) or not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42):
		return null
	var items := INVENTORY.hex_decode()
	items.resize(48)
	items.encode_s32(0,48)
	items.encode_u16(21,1)
	items.encode_s32(31,1001)
	items.encode_s32(35,3)
	items[20] = 1
	if not world.apply_frame(items):
		return null
	return world

func _initialize() -> void:
	if Wire.encode_inventory_discard(9,7,7001,2).hex_encode().to_upper() != REQUEST:
		fail("CSharp/Godot discard request golden changed")
		return
	var decoded := Wire.decode_inventory_discard_result(RESULT.hex_decode())
	if decoded.is_empty() or int(decoded.map_epoch) != 9 or int(decoded.discard_sequence) != 7 or int(decoded.status) != 0 or int(decoded.item_uid) != 7001 or int(decoded.item_definition_id) != 1001 or int(decoded.discarded_quantity) != 2 or int(decoded.remaining_quantity) != 1:
		fail("CSharp/Godot discard result golden mismatch")
		return
	if not Wire.encode_inventory_discard(0,7,7001,2).is_empty() or not Wire.encode_inventory_discard(9,0,7001,2).is_empty() or not Wire.encode_inventory_discard(9,7,0,2).is_empty() or not Wire.encode_inventory_discard(9,7,7001,0).is_empty():
		fail("invalid discard request was emitted")
		return
	var bad := RESULT.hex_decode()
	bad[20] = 255
	if not Wire.decode_inventory_discard_result(bad).is_empty():
		fail("unknown discard status accepted")
		return
	bad = RESULT.hex_decode()
	bad.encode_s32(33,0)
	if not Wire.decode_inventory_discard_result(bad).is_empty():
		fail("successful discard with zero removed quantity accepted")
		return

	var world = new_world()
	if world == null or not world.snapshot().inventory_ready or int(world.snapshot().inventory_items[7001].quantity) != 3:
		fail("fixture did not load authoritative inventory")
		return
	if not world.mark_inventory_discard_sent(1,7001,2) or world.mark_inventory_discard_sent(2,7001,1):
		fail("one outstanding inventory mutation requirement failed")
		return
	var committed := RESULT.hex_decode()
	committed.encode_s64(8,1)
	committed.encode_u32(16,1)
	if not world.apply_frame(committed) or int(world.snapshot().inventory_items[7001].quantity) != 1 or int(world.snapshot().inventory_revision) != 2 or int(world.snapshot().pending_discard_sequence) != 0:
		fail("committed discard did not update stack quantity once")
		return
	var stale := int(world.snapshot().stale_discard_results)
	if not world.apply_frame(committed) or int(world.snapshot().stale_discard_results) != stale+1 or int(world.snapshot().inventory_items[7001].quantity) != 1:
		fail("replayed discard reply removed another unit")
		return
	if not world.mark_inventory_discard_sent(2,7001,1):
		fail("next discard blocked after receipt")
		return
	committed.encode_u32(16,2)
	committed.encode_s32(33,1)
	committed.encode_s32(37,0)
	if not world.apply_frame(committed) or not world.snapshot().inventory_items.is_empty() or int(world.snapshot().inventory_revision) != 3:
		fail("last stack quantity did not disappear after server discard")
		return
	if world.mark_inventory_discard_sent(3,7001,1):
		fail("absent inventory UID was discardable")
		return
	if not world.apply_frame(MAP13_READY.hex_decode()) or int(world.snapshot().discard_sequence) != 0:
		fail("map transfer retained old discard sequence")
		return
	stale = int(world.snapshot().stale_discard_results)
	if not world.apply_frame(committed) or int(world.snapshot().stale_discard_results) != stale+1:
		fail("old-map discard result was not ignored")
		return

	world = new_world()
	if world == null or not world.mark_inventory_discard_sent(1,7001,2):
		fail("wrong UID response fixture")
		return
	bad = RESULT.hex_decode()
	bad.encode_s64(8,1)
	bad.encode_u32(16,1)
	bad.encode_s64(21,7002)
	if world.apply_frame(bad) or int(world.state) != World.STATE_FAILED:
		fail("server discard response for wrong ItemUID accepted")
		return
	print("INVENTORY_DISCARD_WIRE_PASS golden=32/41 auth=PASS once=PASS quantity=PASS final_delete=PASS map_epoch=PASS spoof=FAIL_CLOSED")
	quit(0)
