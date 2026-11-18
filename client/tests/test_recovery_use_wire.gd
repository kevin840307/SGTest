extends SceneTree
const Wire = preload("res://network/wire_codec.gd")
const World = preload("res://network/world_state.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const INVENTORY := "47000000010024000B0000000000000000000000010200591B0000000000008D0A000002000000F5010000000000005A1B000000000000750E000003000000F601000000000000"
const REQUEST := "1C00000001002500090000000000000007000000591B000000000000"
const RESULT := "2D0000000100260009000000000000000700000000591B000000000000E9030000010000005A00000064000000"
const COMBAT := "200000000100070001000000000000003C000000640000000000000000000000"

func fail(message: String) -> void:
	push_error("RECOVERY_USE_WIRE_FAIL "+message)
	quit(1)

func new_world():
	var world = World.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()) or not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42):
		return null
	var items := INVENTORY.hex_decode()
	items.resize(47)
	items.encode_s32(0,47)
	items.encode_u16(21,1)
	items.encode_s32(31,1001)
	items[20] = 1
	if not world.apply_frame(items) or not world.apply_frame(COMBAT.hex_decode()):
		return null
	return world

func _initialize() -> void:
	if Wire.encode_recovery_use(9,7,7001).hex_encode().to_upper() != REQUEST:
		fail("CSharp/Godot recovery request golden changed")
		return
	var reply := Wire.decode_recovery_use_result(RESULT.hex_decode())
	if reply.is_empty() or int(reply.map_epoch) != 9 or int(reply.use_sequence) != 7 or int(reply.status) != 0 or int(reply.item_uid) != 7001 or int(reply.item_definition_id) != 1001 or int(reply.remaining_quantity) != 1 or int(reply.current_hp) != 90 or int(reply.max_hp) != 100:
		fail("CSharp/Godot recovery result golden mismatch")
		return
	if not Wire.encode_recovery_use(0,7,7001).is_empty() or not Wire.encode_recovery_use(9,0,7001).is_empty() or not Wire.encode_recovery_use(9,7,0).is_empty():
		fail("invalid recovery request was emitted")
		return
	var bad := RESULT.hex_decode()
	bad[20] = 255
	if not Wire.decode_recovery_use_result(bad).is_empty():
		fail("unknown recovery status accepted")
		return
	bad = RESULT.hex_decode()
	bad.encode_s32(37,0)
	if not Wire.decode_recovery_use_result(bad).is_empty():
		fail("successful recovery with zero HP accepted")
		return
	var world = new_world()
	if world == null or not world.snapshot().inventory_ready or int(world.snapshot().player_current_hp) != 60:
		fail("fixture did not load authenticated inventory and HP")
		return
	if not world.mark_recovery_use_sent(1,7001) or world.mark_recovery_use_sent(2,7001):
		fail("one outstanding item action requirement failed")
		return
	var committed := RESULT.hex_decode()
	committed.encode_s64(8,1)
	committed.encode_u32(16,1)
	if not world.apply_frame(committed) or int(world.snapshot().player_current_hp) != 90 or int(world.snapshot().inventory_items[7001].quantity) != 1 or int(world.snapshot().inventory_revision) != 2 or int(world.snapshot().pending_recovery_sequence) != 0:
		fail("committed item use did not update HP and stack quantity once")
		return
	var previous := int(world.snapshot().stale_recovery_results)
	if not world.apply_frame(committed) or int(world.snapshot().stale_recovery_results) != previous+1 or int(world.snapshot().inventory_items[7001].quantity) != 1:
		fail("replayed use reply consumed a second stack unit")
		return
	if not world.mark_recovery_use_sent(2,7001):
		fail("next use command blocked after receipt")
		return
	committed.encode_u32(16,2)
	committed.encode_s32(33,0)
	committed.encode_s32(37,100)
	if not world.apply_frame(committed) or not world.snapshot().inventory_items.is_empty() or int(world.snapshot().player_current_hp) != 100 or int(world.snapshot().inventory_revision) != 3:
		fail("last item quantity did not disappear after verified server consume")
		return
	if world.mark_recovery_use_sent(3,7001):
		fail("absent stack was usable")
		return
	if not world.apply_frame(MAP13_READY.hex_decode()) or int(world.snapshot().recovery_sequence) != 0 or int(world.snapshot().player_current_hp) != 100:
		fail("map transfer lost HP or retained old use sequence")
		return
	previous = int(world.snapshot().stale_recovery_results)
	if not world.apply_frame(committed) or int(world.snapshot().stale_recovery_results) != previous+1:
		fail("old map recovery response not ignored")
		return
	world = new_world()
	if world == null or not world.mark_recovery_use_sent(1,7001):
		fail("forged response fixture")
		return
	bad = RESULT.hex_decode()
	bad.encode_s64(8,1)
	bad.encode_u32(16,1)
	bad.encode_s64(21,7002)
	if world.apply_frame(bad) or int(world.state) != World.STATE_FAILED:
		fail("server recovery response for wrong ItemUID accepted")
		return
	print("RECOVERY_USE_WIRE_PASS golden=28/45 auth=PASS once=PASS quantity=PASS hp=PASS map_epoch=PASS spoof=FAIL_CLOSED")
	quit(0)
