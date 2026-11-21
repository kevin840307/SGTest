extends SceneTree
const Wire = preload("res://wire/wire_codec.gd")
const World = preload("res://world/world_state.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const INVENTORY := "49000000010024000B0000000000000000000000010200591B0000000000008D0A000002000000F501000000000000005A1B000000000000750E000003000000F60100000000000000"
const OPEN := "180000000100340009000000000000000700000063000000"
const DEPOSIT := "200000000100360009000000000000000800000063000000591B000000000000"
const WITHDRAW := "200000000100370009000000000000000900000063000000591B000000000000"
const SNAPSHOT := "3900000001003500090000000000000007000000006300000000000000010100591B0000000000008D0A000002000000F50100000000000005"
const RESULT := "3200000001003800090000000000000008000000010063000000591B0000000000008D0A000002000000F501000000000000"

func fail(message: String) -> void:
	push_error("STORAGE_WIRE_FAIL "+message)
	quit(1)

func new_world():
	var world = World.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()) or not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42):
		return null
	var bag := INVENTORY.hex_decode()
	bag.resize(48)
	bag.encode_s32(0,48)
	bag.encode_u16(21,1)
	bag.encode_s32(31,2701)
	bag.encode_s32(35,2)
	bag.encode_s64(39,501)
	bag[47] = 5
	bag[20] = 1
	if not world.apply_frame(bag):
		return null
	return world

func _initialize() -> void:
	if Wire.encode_storage_open(9,7,99).hex_encode().to_upper() != OPEN or Wire.encode_storage_deposit(9,8,99,7001).hex_encode().to_upper() != DEPOSIT or Wire.encode_storage_withdraw(9,9,99,7001).hex_encode().to_upper() != WITHDRAW:
		fail("request golden mismatch")
		return
	var snapshot := Wire.decode_storage_snapshot(SNAPSHOT.hex_decode())
	var result := Wire.decode_storage_transfer_result(RESULT.hex_decode())
	if snapshot.is_empty() or int(snapshot.items[0].item_uid) != 7001 or int(snapshot.items[0].drop_id) != 501 or int(snapshot.items[0].enhancement_level) != 5 or result.is_empty() or int(result.item_uid) != 7001 or int(result.drop_id) != 501:
		fail("server storage golden mismatch")
		return

	var world = new_world()
	if world == null or not world.mark_storage_sent(1,1,99):
		fail("open state")
		return
	var empty_open := SNAPSHOT.hex_decode()
	empty_open.resize(32)
	empty_open.encode_s32(0,32)
	empty_open.encode_s64(8,1)
	empty_open.encode_u32(16,1)
	empty_open.encode_u16(30,0)
	if not world.apply_frame(empty_open):
		fail("empty open reducer")
		return
	var state: Dictionary = world.snapshot()
	if int(state.active_storage_npc) != 99 or not bool(state.storage_ready) or not (state.storage_items as Dictionary).is_empty():
		fail("empty storage baseline")
		return

	if not world.mark_storage_sent(2,2,99,7001):
		fail("deposit state")
		return
	var deposited := RESULT.hex_decode()
	deposited.encode_s64(8,1)
	deposited.encode_u32(16,2)
	if not world.apply_frame(deposited):
		fail("deposit settlement")
		return
	state = world.snapshot()
	if (state.inventory_items as Dictionary).has(7001) or not (state.storage_items as Dictionary).has(7001) or int(state.storage_items[7001].drop_id) != 501 or int(state.storage_items[7001].enhancement_level) != 5:
		fail("deposit did not move exact UID/provenance")
		return
	var revision := int(state.storage_revision)
	var stale := int(state.stale_storage_results)
	if not world.apply_frame(deposited) or int(world.snapshot().storage_revision) != revision or int(world.snapshot().stale_storage_results) != stale+1:
		fail("duplicate deposit mutated twice")
		return

	if not world.mark_storage_sent(3,3,99,7001):
		fail("withdraw state")
		return
	var withdrawn := RESULT.hex_decode()
	withdrawn.encode_s64(8,1)
	withdrawn.encode_u32(16,3)
	withdrawn[20] = 2
	if not world.apply_frame(withdrawn):
		fail("withdraw settlement")
		return
	state = world.snapshot()
	if not (state.inventory_items as Dictionary).has(7001) or (state.storage_items as Dictionary).has(7001) or int(state.inventory_items[7001].quantity) != 2 or int(state.inventory_items[7001].drop_id) != 501 or int(state.inventory_items[7001].enhancement_level) != 5:
		fail("withdraw did not restore exact inventory UID")
		return

	if not world.mark_storage_sent(4,1,99):
		fail("chunked open state")
		return
	var first := SNAPSHOT.hex_decode()
	first.encode_s64(8,1)
	first.encode_u32(16,4)
	first.encode_s64(32,8001)
	first[29] = 0
	if not world.apply_frame(first) or bool(world.snapshot().storage_ready) or int(world.snapshot().storage_next_index) != 1:
		fail("partial storage snapshot published early")
		return
	var final_chunk := SNAPSHOT.hex_decode()
	final_chunk.encode_s64(8,1)
	final_chunk.encode_u32(16,4)
	final_chunk.encode_s32(25,1)
	final_chunk.encode_s64(32,8002)
	final_chunk.encode_s32(40,2702)
	final_chunk.encode_s32(44,1)
	final_chunk.encode_s64(48,0)
	final_chunk[56] = 7
	if not world.apply_frame(final_chunk):
		fail("final storage chunk rejected")
		return
	state = world.snapshot()
	if not bool(state.storage_ready) or (state.storage_items as Dictionary).size() != 2 or int(state.active_storage_npc) != 99:
		fail("chunked storage baseline incomplete")
		return

	if not world.apply_frame(MAP13_READY.hex_decode()):
		fail("map transfer rejected")
		return
	state = world.snapshot()
	if bool(state.storage_ready) or int(state.active_storage_npc) != 0 or not (state.storage_items as Dictionary).is_empty() or int(state.storage_sequence) != 0:
		fail("map transfer did not close storage")
		return

	world = new_world()
	if world == null or not world.mark_storage_sent(1,1,99) or not world.apply_frame(empty_open) or not world.mark_storage_sent(2,2,99,7001):
		fail("access rejection fixture")
		return
	var too_far := RESULT.hex_decode()
	too_far.encode_s64(8,1)
	too_far.encode_u32(16,2)
	too_far[21] = 6
	too_far.encode_s32(34,0)
	too_far.encode_s32(38,0)
	too_far.encode_s64(42,0)
	if not world.apply_frame(too_far) or bool(world.snapshot().storage_ready) or int(world.snapshot().active_storage_npc) != 0:
		fail("TooFar did not close stale storage")
		return

	world = new_world()
	if world == null or not world.mark_storage_sent(1,1,99) or not world.apply_frame(empty_open) or not world.mark_storage_sent(2,2,99,7001):
		fail("spoof fixture")
		return
	var wrong := RESULT.hex_decode()
	wrong.encode_s64(8,1)
	wrong.encode_u32(16,2)
	wrong.encode_s64(26,7002)
	if world.apply_frame(wrong) or int(world.state) != World.STATE_FAILED:
		fail("wrong storage UID accepted")
		return

	print("STORAGE_WIRE_PASS open=24 snapshot=CHUNKED level=PRESERVED deposit=32 withdraw=32 result=50 uid=SERVER_AUTH provenance=PRESERVED duplicate=STALE transfer=CLOSES access_failure=CLOSES spoof=FAIL_CLOSED")
	quit(0)
