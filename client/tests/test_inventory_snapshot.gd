extends SceneTree
const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://network/world_state.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const INVENTORY := "47000000010024000B0000000000000000000000010200591B0000000000008D0A000002000000F5010000000000005A1B000000000000750E000003000000F601000000000000"
const PICKUP_RESULT := "2D0000000100230009000000000000000700000000F501000000000000591B0000000000008D0A000002000000"
const DROP := "340000000100200001000000000000003700000000000000F5010000000000000F000000000040420000C0418D0A000002000000"

func fail(message: String) -> void:
	push_error("INVENTORY_SNAPSHOT_FAIL "+message)
	quit(1)

func new_world():
	var world = WorldState.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()) or not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42):
		return null
	return world

func _initialize() -> void:
	var decoded := WireCodec.decode_inventory_snapshot(INVENTORY.hex_decode())
	if decoded.is_empty() or int(decoded.character_id) != 11 or not bool(decoded.complete) or int(decoded.start_index) != 0 or decoded.items.size() != 2 or int(decoded.items[0].item_uid) != 7001 or int(decoded.items[1].item_uid) != 7002 or int(decoded.items[1].quantity) != 3:
		fail("C# to Godot inventory 71-byte golden mismatch")
		return
	var empty := INVENTORY.hex_decode()
	empty.resize(23)
	empty.encode_s32(0,23)
	empty.encode_u16(21,0)
	if WireCodec.decode_inventory_snapshot(empty).is_empty():
		fail("empty but final inventory baseline was rejected")
		return
	var bad := INVENTORY.hex_decode()
	bad[20] = 2
	if not WireCodec.decode_inventory_snapshot(bad).is_empty():
		fail("non-boolean inventory final flag accepted")
		return
	bad = INVENTORY.hex_decode()
	bad.encode_s64(23,0)
	if not WireCodec.decode_inventory_snapshot(bad).is_empty():
		fail("inventory zero UID was accepted")
		return
	bad = INVENTORY.hex_decode()
	bad.encode_s64(47,7001)
	if not WireCodec.decode_inventory_snapshot(bad).is_empty():
		fail("duplicate inventory item UID was accepted")
		return
	var world = new_world()
	if world == null:
		fail("authenticated inventory fixture failed")
		return
	var first := INVENTORY.hex_decode()
	first.resize(47)
	first.encode_s32(0,47)
	first[20] = 0
	first.encode_u16(21,1)
	var second := first.slice(0,23)
	second.encode_s32(16,1)
	second[20] = 1
	second.append_array(INVENTORY.hex_decode().slice(47,71))
	if world.apply_frame(second):
		fail("out-of-order chunk accepted")
		return
	world = new_world()
	if not world.apply_frame(first) or bool(world.snapshot().inventory_ready) or world.snapshot().inventory_items.size() != 1:
		fail("first partial chunk incorrectly marked complete")
		return
	if not world.apply_frame(second) or not bool(world.snapshot().inventory_ready) or world.snapshot().inventory_items.size() != 2 or int(world.snapshot().inventory_revision) != 1:
		fail("second chunk failed to commit complete inventory baseline")
		return
	if world.apply_frame(second):
		fail("post-complete chunk replaced durable inventory")
		return
	world = new_world()
	if not world.apply_frame(INVENTORY.hex_decode()) or not bool(world.snapshot().inventory_ready):
		fail("single-frame baseline failed")
		return
	# The next login must reconstruct from the same durable Server source, not
	# from the previous session's local received_item_grants cache.
	var next_login = new_world()
	if next_login == null or not next_login.snapshot().inventory_items.is_empty() or not next_login.apply_frame(INVENTORY.hex_decode()) or next_login.snapshot().inventory_items != world.snapshot().inventory_items:
		fail("reconnect inventory baseline not authoritative")
		return
	var drop := DROP.hex_decode()
	drop.encode_s64(24,503)
	if not world.apply_frame(drop) or not world.mark_pickup_sent(1,503):
		fail("pickup entry after inventory baseline")
		return
	var pickup := PICKUP_RESULT.hex_decode()
	pickup.encode_s64(8,1)
	pickup.encode_u32(16,1)
	pickup.encode_s64(21,503)
	pickup.encode_s64(29,7003)
	pickup.encode_s32(37,4701)
	pickup.encode_s32(41,4)
	if not world.apply_frame(pickup) or world.snapshot().inventory_items.size() != 3 or int(world.snapshot().inventory_items[7003].quantity) != 4 or int(world.snapshot().inventory_revision) != 2:
		fail("pickup did not merge one authoritative item UID")
		return
	if not world.apply_frame(pickup) or world.snapshot().inventory_items.size() != 3 or int(world.snapshot().inventory_revision) != 2:
		fail("replayed pickup minted duplicate inventory UID")
		return
	print("INVENTORY_SNAPSHOT_PASS golden=71 chunk=128/4095 reorder=FAIL_CLOSED login=PASS pickup_delta=PASS duplicate=PASS")
	quit(0)
