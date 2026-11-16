extends SceneTree

const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://network/world_state.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const MAP15_READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const MAP15_RETURN := "2D00000001001400030000000000000042000000000000000B000000000000000F0000000000C0410000004101"
const REQUEST := "18000000010003000100000000000000010000002E000000"
const ACCEPTED := "1D00000001000400010000000000000001000000000200000000000000"
const TOO_FAR := "1D00000001000400020000000000000002000000040200000000000000"
const BACK_ACCEPTED := "1D00000001000400020000000000000002000000000300000000000000"

func _fail(detail: String) -> void:
	push_error("NPC_PORTAL_WIRE_FAIL " + detail)
	quit(1)

func _initialize() -> void:
	var encoded := WireCodec.encode_npc_interact(1, 1, 46)
	if encoded.hex_encode().to_upper() != REQUEST or WireCodec.decode_npc_interact(encoded).get("npc_spawn_id", -1) != 46:
		_fail("NPC client request does not match fixed C# binary contract")
		return
	if not WireCodec.encode_npc_interact(0, 1, 46).is_empty() or not WireCodec.encode_npc_interact(1, 0, 46).is_empty() or not WireCodec.encode_npc_interact(1, 1, 0).is_empty():
		_fail("invalid client NPC request accepted")
		return
	var accept := WireCodec.decode_npc_interact_result(ACCEPTED.hex_decode())
	var deny := WireCodec.decode_npc_interact_result(TOO_FAR.hex_decode())
	if accept.is_empty() or int(accept.status) != 0 or int(accept.result_map_epoch) != 2 or deny.is_empty() or int(deny.status) != 4 or int(deny.result_map_epoch) != 2:
		_fail("NPC result cross-language golden mismatch")
		return
	var malformed := TOO_FAR.hex_decode()
	malformed[20] = 255
	if not WireCodec.decode_npc_interact_result(malformed).is_empty():
		_fail("illegal NPC response status accepted")
		return
	malformed = ACCEPTED.hex_decode()
	malformed[0] = 28
	if not WireCodec.decode_npc_interact_result(malformed).is_empty():
		_fail("malformed response length accepted")
		return

	var world = _ready_map15()
	if world == null:
		_fail("setup WorldReady failed")
		return
	if not world.mark_npc_interact_sent(1) or world.mark_npc_interact_sent(2) or not world.apply_frame(ACCEPTED.hex_decode()):
		_fail("accepted NPC click not reduced or second click bypassed pending RPC")
		return
	if not bool(world.snapshot().waiting_for_new_map) or world.mark_move_sent(1):
		_fail("pending accepted NPC transfer allowed outgoing old-map movement")
		return
	if not world.apply_frame(MAP13_READY.hex_decode()) or int(world.snapshot().map_epoch) != 2 or int(world.snapshot().map_id) != 13 or bool(world.snapshot().waiting_for_new_map):
		_fail("new MapEpoch did not restore WorldReady and clear transition")
		return
	if not world.mark_npc_interact_sent(2) or not world.apply_frame(TOO_FAR.hex_decode()) or int(world.snapshot().last_npc_result_status) != 4 or int(world.snapshot().map_id) != 13:
		_fail("NPC TooFar denial unexpectedly changed world")
		return
	if not world.apply_frame(TOO_FAR.hex_decode()) or int(world.snapshot().stale_npc_results) != 1:
		_fail("duplicate NPC result not safely ignored")
		return

	var ready_first = _ready_map15()
	if ready_first == null or not ready_first.mark_npc_interact_sent(1):
		_fail("second client reducer setup")
		return
	if not ready_first.apply_frame(MAP13_READY.hex_decode()) or not ready_first.apply_frame(ACCEPTED.hex_decode()):
		_fail("WorldReady arriving BEFORE portal result failed")
		return
	if int(ready_first.snapshot().last_npc_result_map_epoch) != 2 or bool(ready_first.snapshot().waiting_for_new_map):
		_fail("newer WorldReady/older result processing incorrectly reset world")
		return
	if not ready_first.mark_npc_interact_sent(2) or not ready_first.apply_frame(BACK_ACCEPTED.hex_decode()) or not ready_first.apply_frame(MAP15_RETURN.hex_decode()):
		_fail("Map13 to Map15 portal did not rebuild state")
		return
	if int(ready_first.snapshot().map_epoch) != 3 or int(ready_first.snapshot().map_id) != 15 or bool(ready_first.snapshot().waiting_for_new_map):
		_fail("return-to-Map15 state wrong")
		return
	print("NPC_PORTAL_WIRE_GODOT_PASS request=24 result=29 denied=PASS ordered=PASS reordered=PASS map_epoch=1_2_3")
	quit(0)

func _ready_map15():
	var world = WorldState.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()) or not world.apply_frame(MAP15_READY.hex_decode()):
		return null
	return world
