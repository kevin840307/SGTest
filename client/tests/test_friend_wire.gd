extends SceneTree
const Codec = preload("res://network/wire_codec.gd")
const State = preload("res://world/world_state.gd")

const ADD_HEX := "240000000100780007000000000000000100000001000000000000000005004275646479"
const REMOVE_HEX := "1f00000001007800070000000000000002000000022a000000000000000000"
const REFRESH_HEX := "1f000000010078000700000000000000030000000300000000000000000000"
const RESULT_HEX := "1e0000000100790007000000000000000100000001002a00000000000000"
const REMOVE_RESULT_HEX := "1e0000000100790007000000000000000200000002002a00000000000000"
const SNAPSHOT_HEX := "3400000001007a00070000000000000002002a0000000000000001050042756464792b000000000000000007004f66666c696e65"

func _initialize() -> void:
	var add := Codec.encode_friend_command(7,1,1,0,"Buddy")
	if add.hex_encode() != ADD_HEX:
		_fail("C# add-by-name golden mismatch")
		return
	if Codec.encode_friend_command(7,2,2,42).hex_encode() != REMOVE_HEX:
		_fail("C# remove golden mismatch")
		return
	if Codec.encode_friend_command(7,3,3).hex_encode() != REFRESH_HEX:
		_fail("C# refresh golden mismatch")
		return
	if not Codec.encode_friend_command(7,1,1,0," Buddy ").is_empty():
		_fail("friend add accepted trim-changing name")
		return
	var result := Codec.decode_friend_result(RESULT_HEX.hex_decode())
	if result.is_empty() or result.target_actor_id != 42 or result.action != 1 or result.status != 0:
		_fail("friend result golden rejected")
		return
	var snapshot := Codec.decode_friend_snapshot(SNAPSHOT_HEX.hex_decode())
	if snapshot.is_empty() or snapshot.friends.size() != 2 or not bool(snapshot.friends[0].online) or bool(snapshot.friends[1].online):
		_fail("friend presence snapshot golden rejected")
		return

	var state := State.new()
	state.state = State.STATE_IN_WORLD
	state.world_ready = true
	state.map_epoch = 7
	state.actor_id = 1001
	state.last_world_ready_ack_epoch = 7
	if not state.mark_friend_sent(1,1,0,"Buddy"):
		_fail("valid friend add request rejected")
		return
	if not state.apply_frame(RESULT_HEX.hex_decode()):
		_fail("friend result reducer failed")
		return
	if not state.apply_frame(SNAPSHOT_HEX.hex_decode()):
		_fail("friend snapshot reducer failed")
		return
	var view := state.snapshot()
	if view.friends.size() != 2 or not bool(view.friends[42].online) or bool(view.friends[43].online) or view.pending_friend_sequence != 0:
		_fail("friend reducer state mismatch")
		return
	if not state.mark_friend_sent(2,2,42):
		_fail("valid friend remove request rejected")
		return
	if not state.apply_frame(REMOVE_RESULT_HEX.hex_decode()):
		_fail("friend remove result reducer failed")
		return
	if state.snapshot().pending_friend_sequence != 0:
		_fail("friend remove result did not clear pending request")
		return
	print("FRIEND_WIRE_PASS csharp_golden=true reducer=true presence=true friends=2")
	quit(0)

func _fail(message: String) -> void:
	push_error("FRIEND_WIRE_FAIL "+message)
	quit(1)
