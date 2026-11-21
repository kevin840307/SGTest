extends SceneTree

const WireCodec = preload("res://wire/wire_codec.gd")
const WorldState = preload("res://world/world_state.gd")
const AUTH_OK := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"

func _fail(reason: String) -> void:
	push_error("PVP_MODE_WIRE_FAIL " + reason)
	quit(1)

func _make_result(epoch: int, sequence: int, status: int, mode: int, allowed: bool) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(23)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PVP_MODE_RESULT)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(status)
	writer.put_u8(mode)
	writer.put_u8(1 if allowed else 0)
	return writer.data_array

func _modify_int(source: PackedByteArray, offset: int, count: int, value: int) -> PackedByteArray:
	var frame := source.duplicate()
	for index in range(count):
		frame[offset+index] = (value >> (index*8)) & 0xff
	return frame

func _initialize() -> void:
	if WireCodec.encode_pvp_mode_command(1,7,2).size() != 21 or not WireCodec.encode_pvp_mode_command(1,7,3).is_empty() or not WireCodec.encode_pvp_mode_command(1,0,0).is_empty():
		_fail("PvP request wire validation")
		return
	var world = WorldState.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH_OK.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()):
		_fail("login/choose fixture")
		return
	if world.mark_pvp_sent(1,0):
		_fail("PvP command escaped WorldReady gate")
		return
	if not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42):
		_fail("world ready/ACK")
		return
	if not world.mark_pvp_sent(1,0) or world.mark_pvp_sent(2,1):
		_fail("query single flight")
		return
	var blue := _make_result(1,1,0,0,false)
	if not world.apply_frame(blue) or not bool(world.snapshot().get("pvp_known",false)) or int(world.snapshot().get("pvp_mode",-1)) != 0:
		_fail("authoritative query mode not applied")
		return
	if world.mark_pvp_sent(2,2):
		_fail("white PvP enabled on verified NonPK map")
		return
	if not world.mark_pvp_sent(2,1) or not world.apply_frame(_make_result(1,2,2,0,false)):
		_fail("unchanged blue acknowledgement")
		return
	if not world.apply_frame(blue) or int(world.snapshot().get("stale_pvp_results",-1)) != 1:
		_fail("duplicate PvP result not stale")
		return
	var new_map := _modify_int(READY.hex_decode(),8,8,2)
	new_map = _modify_int(new_map,16,8,43)
	new_map = _modify_int(new_map,32,4,13)
	if not world.apply_frame(new_map) or not world.mark_world_ready_ack_sent(2,43):
		_fail("Map13 transfer")
		return
	if bool(world.snapshot().get("pvp_known",true)) or int(world.snapshot().get("pvp_sequence",-1)) != 0:
		_fail("PvP state from old map leaked")
		return
	if not world.mark_pvp_sent(1,0) or not world.apply_frame(_make_result(2,1,0,1,true)):
		_fail("Map13 white authority query")
		return
	if int(world.snapshot().get("pvp_mode",-1)) != 1 or not bool(world.snapshot().get("pvp_map_allows",false)):
		_fail("server white mode / eligible map not reflected")
		return
	if not world.apply_frame(blue) or int(world.snapshot().get("stale_pvp_results",-1)) != 1:
		_fail("stale old-map PvP reply not rejected")
		return
	if not world.mark_pvp_sent(2,1) or not world.apply_frame(_make_result(2,2,6,1,true)):
		_fail("stale-sequence authoritative server rejection")
		return
	if int(world.snapshot().get("pvp_mode",-1)) != 1 or int(world.snapshot().get("last_pvp_result",{}).get("status",-1)) != 6:
		_fail("stale-sequence reply corrupted existing authoritative PvP mode")
		return
	if world.apply_frame(_make_result(2,3,1,0,true)) or int(world.snapshot().get("state",-1)) != WorldState.STATE_FAILED:
		_fail("unsolicited PvP mutation did not fail closed")
		return
	print("PVP_MODE_WIRE_PASS query_blue=true no_nonpk_white=true singleflight=true replay_stale=true crossmap_clear=true white_authority=true stale_sequence=true unsolicited_failclosed=true")
	quit(0)
