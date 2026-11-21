extends SceneTree
const WireCodec = preload("res://wire/wire_codec.gd")
const State = preload("res://world/world_state.gd")

const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const DROP := "340000000100200001000000000000003700000000000000F5010000000000000F000000000040420000C0418D0A000002000000"
const GONE := "200000000100210001000000000000003800000000000000F501000000000000"
const REQUEST := "1C00000001002200090000000000000007000000F501000000000000"
const RESULT := "2D0000000100230009000000000000000700000000F501000000000000591B0000000000008D0A000002000000"

func fail(message: String) -> void:
	push_error("GROUND_PICKUP_WIRE_FAIL "+message)
	quit(1)

func new_world():
	var world = State.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()) or not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42):
		return null
	return world

func _initialize() -> void:
	var command := WireCodec.encode_ground_pickup(9,7,501)
	var parsed := WireCodec.decode_ground_pickup_result(RESULT.hex_decode())
	if command.hex_encode().to_upper() != REQUEST or parsed.is_empty() or int(parsed.pickup_sequence) != 7 or int(parsed.item_uid) != 7001 or int(parsed.item_definition_id) != 2701 or int(parsed.quantity) != 2 or int(parsed.status) != 0:
		fail("CSharp/Godot golden pickup request/result differs")
		return
	if not WireCodec.encode_ground_pickup(0,7,501).is_empty() or not WireCodec.encode_ground_pickup(9,0,501).is_empty() or not WireCodec.encode_ground_pickup(9,7,0).is_empty():
		fail("invalid pickup parameters became outbound frame")
		return
	var malformed := RESULT.hex_decode()
	malformed[20] = 255
	if not WireCodec.decode_ground_pickup_result(malformed).is_empty():
		fail("unknown pickup result status was accepted")
		return
	malformed = RESULT.hex_decode()
	malformed.encode_s64(29,0)
	if not WireCodec.decode_ground_pickup_result(malformed).is_empty():
		fail("granted pickup without item UID accepted")
		return
	var world = new_world()
	if world == null:
		fail("authenticated WorldReady fixture failed")
		return
	if not world.apply_frame(DROP.hex_decode()) or not world.snapshot().ground_drops.has(501):
		fail("authoritative visible ground UID not in snapshot")
		return
	if world.mark_pickup_sent(2,501) != true or world.mark_pickup_sent(3,501):
		fail("one outstanding pickup sequence gate")
		return
	var accepted := RESULT.hex_decode()
	accepted.encode_s64(8,1)
	accepted.encode_u32(16,2)
	if not world.apply_frame(accepted) or int(world.snapshot().pending_pickup_sequence) != 0 or int(world.snapshot().last_pickup_result_sequence) != 2 or int(world.snapshot().last_pickup_result_status) != 0 or not world.snapshot().received_item_grants.has(7001) or not world.snapshot().ground_drops.has(501):
		fail("result invented early despawn or forgot durable granted UID")
		return
	# Only the authoritative AOI event removes world presentation,
	# not an optimistic click or an accepted inventory grant alone.
	var before := int(world.snapshot().stale_pickup_results)
	if not world.apply_frame(accepted) or int(world.snapshot().stale_pickup_results) != before+1 or world.snapshot().received_item_grants.size() != 1:
		fail("duplicate pickup result minted another UI item")
		return
	if not world.apply_frame(GONE.hex_decode()) or not world.snapshot().ground_drops.is_empty():
		fail("SQLite-committed AOI removal did not clear ground UID")
		return
	if not world.apply_frame(MAP13_READY.hex_decode()) or int(world.snapshot().pickup_sequence) != 0 or not world.snapshot().ground_drops.is_empty():
		fail("map transfer retained stale pickup generation")
		return
	before = int(world.snapshot().stale_pickup_results)
	if not world.apply_frame(accepted) or int(world.snapshot().stale_pickup_results) != before+1:
		fail("stale map pickup result leaked across transfer")
		return
	var hostile = new_world()
	hostile.apply_frame(DROP.hex_decode())
	hostile.mark_pickup_sent(1,501)
	var wrong := RESULT.hex_decode()
	wrong.encode_s64(8,1)
	wrong.encode_u32(16,1)
	wrong.encode_s64(21,502)
	if hostile.apply_frame(wrong) or int(hostile.state) != State.STATE_FAILED:
		fail("pickup result for a different UID escaped request binding")
		return
	print("GROUND_PICKUP_WIRE_PASS golden=28/45 auth_state=true single_pending=true atomic_uid=true aoidespawn=true retry_stale=true transfer_stale=true forged_uid_fail_closed=true")
	quit(0)
