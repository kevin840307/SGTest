extends SceneTree
const Codec = preload("res://network/wire_codec.gd")
const State = preload("res://world/world_state.gd")

const ADD_HEX := "240000000100780007000000000000000100000001000000000000000005004275646479"
const REMOVE_HEX := "1f00000001007800070000000000000002000000022a000000000000000000"
const REFRESH_HEX := "1f000000010078000700000000000000030000000300000000000000000000"
const ACCEPT_HEX := "1f00000001007800070000000000000002000000042a000000000000000000"
const REJECT_HEX := "1f00000001007800070000000000000003000000052a000000000000000000"
const INVITE_HEX := "280000000100880007000000000000002a0000000000000009000000000000000005004275646479"
const ACCEPT_RESULT_HEX := "1e0000000100790007000000000000000200000004002a00000000000000"
const IGNORE_RESULT_HEX := "1e0000000100790007000000000000000300000006002a00000000000000"
const RESTORE_RESULT_HEX := "1e0000000100790007000000000000000400000007002a00000000000000"
const REMOVE_RESULT_HEX := "1e0000000100790007000000000000000500000002002a00000000000000"
const SNAPSHOT_HEX := "3400000001007a00070000000000000002002a0000000000000001050042756464792b000000000000000207004f66666c696e65"
const IGNORE_SNAPSHOT_HEX := "3400000001007a00070000000000000002002a0000000000000002050042756464792b000000000000000207004f66666c696e65"

func _initialize() -> void:
	if Codec.encode_friend_command(7,1,1,0,"Buddy").hex_encode() != ADD_HEX:
		_fail("C# add-by-name golden mismatch")
		return
	if Codec.encode_friend_command(7,2,2,42).hex_encode() != REMOVE_HEX:
		_fail("C# remove golden mismatch")
		return
	if Codec.encode_friend_command(7,3,3).hex_encode() != REFRESH_HEX:
		_fail("C# refresh golden mismatch")
		return
	if Codec.encode_friend_command(7,2,4,42).hex_encode() != ACCEPT_HEX:
		_fail("C# accept golden mismatch")
		return
	if Codec.encode_friend_command(7,3,5,42).hex_encode() != REJECT_HEX:
		_fail("C# reject golden mismatch")
		return
	if Codec.encode_friend_command(7,4,6,42).is_empty() or Codec.encode_friend_command(7,5,7,42).is_empty():
		_fail("ignore/restore command encoding rejected")
		return
	if not Codec.encode_friend_command(7,1,1,0," Buddy ").is_empty():
		_fail("friend add accepted trim-changing name")
		return

	var invite := Codec.decode_friend_invite(INVITE_HEX.hex_decode())
	if invite.is_empty() or invite.actor_id != 42 or invite.actor_connection_epoch != 9 or invite.state != 0 or invite.name != "Buddy":
		_fail("C# friend invite golden rejected")
		return
	var snapshot := Codec.decode_friend_snapshot(SNAPSHOT_HEX.hex_decode())
	if snapshot.is_empty() or snapshot.friends.size() != 2 or not bool(snapshot.friends[0].online) or bool(snapshot.friends[0].ignored) or not bool(snapshot.friends[1].ignored):
		_fail("friend presence/ignore snapshot golden rejected")
		return

	var state := State.new()
	state.state = State.STATE_IN_WORLD
	state.world_ready = true
	state.map_epoch = 7
	state.actor_id = 1001
	state.last_world_ready_ack_epoch = 7
	if not state.apply_frame(INVITE_HEX.hex_decode()):
		_fail("friend invite reducer failed")
		return
	var invited := state.snapshot()
	if int(invited.pending_friend_invite.get("actor_id",0)) != 42 or str(invited.pending_friend_invite.get("name","")) != "Buddy":
		_fail("friend invite was not exposed to UI state")
		return
	if not state.mark_friend_sent(2,4,42):
		_fail("valid friend accept request rejected")
		return
	if not state.apply_frame(ACCEPT_RESULT_HEX.hex_decode()):
		_fail("friend accept result reducer failed")
		return
	if not state.snapshot().pending_friend_invite.is_empty() or state.snapshot().pending_friend_sequence != 0:
		_fail("friend accept did not clear pending invite/request")
		return
	if not state.apply_frame(SNAPSHOT_HEX.hex_decode()):
		_fail("friend snapshot reducer failed")
		return
	var view := state.snapshot()
	if view.friends.size() != 1 or view.ignored_friends.size() != 1 or not bool(view.friends[42].online) or not view.ignored_friends.has(43):
		_fail("friend reducer presence/ignore split mismatch")
		return
	if not state.mark_friend_sent(3,6,42) or not state.apply_frame(IGNORE_RESULT_HEX.hex_decode()):
		_fail("friend ignore request/result reducer failed")
		return
	if not state.apply_frame(IGNORE_SNAPSHOT_HEX.hex_decode()):
		_fail("friend ignore snapshot reducer failed")
		return
	view=state.snapshot()
	if view.friends.size()!=0 or view.ignored_friends.size()!=2 or not view.ignored_friends.has(42):
		_fail("friend ignore did not move contact into ignored view")
		return
	if not state.mark_friend_sent(4,7,42) or not state.apply_frame(RESTORE_RESULT_HEX.hex_decode()):
		_fail("friend restore request/result reducer failed")
		return
	if not state.apply_frame(SNAPSHOT_HEX.hex_decode()):
		_fail("friend restore snapshot reducer failed")
		return
	view=state.snapshot()
	if not view.friends.has(42) or view.ignored_friends.has(42):
		_fail("friend restore did not return contact to friend view")
		return
	if not state.mark_friend_sent(5,2,42):
		_fail("valid friend remove request rejected")
		return
	if not state.apply_frame(REMOVE_RESULT_HEX.hex_decode()):
		_fail("friend remove result reducer failed")
		return
	if state.snapshot().pending_friend_sequence != 0:
		_fail("friend remove result did not clear pending request")
		return
	print("FRIEND_WIRE_PASS csharp_golden=true invite=true accept=true ignore=true restore=true reducer=true")
	quit(0)

func _fail(message: String) -> void:
	push_error("FRIEND_WIRE_FAIL "+message)
	quit(1)
