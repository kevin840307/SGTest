extends SceneTree
const Codec = preload("res://network/wire_codec.gd")
const State = preload("res://world/world_state.gd")

const CREATE_HEX := "2e00000001006b000700000000000000010000000100000000000000000f00e6989fe99680e58588e981a3e99a8a"
const RESULT_HEX := "1e00000001006c0007000000000000000100000001002c00000000000000"
const SNAPSHOT_HEX := "5800000001006d0007000000000000002c0000000000000003000000e9030000000000000f0002e6989fe99680e58588e981a3e99a8ae903000000000000010600e59c98e995b7ea03000000000000000600e68890e593a1"

func _initialize() -> void:
	var create := Codec.encode_clan_command(7,1,1,0,"星門先遣隊")
	if create.hex_encode() != CREATE_HEX:
		_fail("C# create golden mismatch")
		return
	if not Codec.encode_clan_command(7,1,1,0," A ").is_empty():
		_fail("clan create accepted trim-changing name")
		return
	var result := Codec.decode_clan_result(RESULT_HEX.hex_decode())
	if result.is_empty() or result.clan_id != 44 or result.action != 1 or result.status != 0:
		_fail("clan result golden rejected")
		return
	var snapshot := Codec.decode_clan_snapshot(SNAPSHOT_HEX.hex_decode())
	if snapshot.is_empty() or snapshot.clan_id != 44 or snapshot.name != "星門先遣隊" or snapshot.members.size() != 2:
		_fail("clan snapshot golden rejected")
		return
	var state := State.new()
	state.state = State.STATE_IN_WORLD
	state.world_ready = true
	state.map_epoch = 7
	state.actor_id = 1001
	state.last_world_ready_ack_epoch = 7
	if not state.mark_clan_sent(1,1,0,"星門先遣隊"):
		_fail("valid clan create request rejected")
		return
	if not state.apply_frame(RESULT_HEX.hex_decode()):
		_fail("clan result reducer failed")
		return
	if not state.apply_frame(SNAPSHOT_HEX.hex_decode()):
		_fail("clan snapshot reducer failed")
		return
	var view := state.snapshot()
	if view.clan_id != 44 or view.clan_leader_actor_id != 1001 or view.clan_members.size() != 2 or view.pending_clan_sequence != 0:
		_fail("clan reducer state mismatch")
		return
	print("CLAN_WIRE_PASS csharp_golden=true reducer=true members=2")
	quit(0)

func _fail(message: String) -> void:
	push_error("CLAN_WIRE_FAIL "+message)
	quit(1)
