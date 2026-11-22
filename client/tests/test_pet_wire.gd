extends SceneTree
const Codec = preload("res://network/wire_codec.gd")
const State = preload("res://world/world_state.gd")

const COMMAND_HEX := "1d00000001006f00070000000000000001000000016500000000000000"
const RESULT_HEX := "2600000001007000070000000000000001000000010065000000000000006500000000000000"
const SNAPSHOT_HEX := "4a0000000100710007000000000000006600000000000000020065000000000000008913000004000000580000000000000066000000000000008a13000007000000d204000000000000"

func _initialize() -> void:
	var command := Codec.encode_pet_command(7,1,1,101)
	if command.hex_encode() != COMMAND_HEX:
		_fail("C# summon command golden mismatch")
		return
	if not Codec.encode_pet_command(7,1,3,101).is_empty():
		_fail("refresh accepted nonzero pet uid")
		return
	var result := Codec.decode_pet_result(RESULT_HEX.hex_decode())
	if result.is_empty() or result.pet_uid != 101 or result.active_pet_uid != 101 or result.status != 0:
		_fail("pet result golden rejected")
		return
	var snapshot := Codec.decode_pet_snapshot(SNAPSHOT_HEX.hex_decode())
	if snapshot.is_empty() or snapshot.active_pet_uid != 102 or snapshot.pets.size() != 2:
		_fail("pet snapshot golden rejected")
		return

	var state := State.new()
	state.state = State.STATE_IN_WORLD
	state.world_ready = true
	state.map_epoch = 7
	state.actor_id = 1001
	state.last_world_ready_ack_epoch = 7
	if not state.apply_frame(SNAPSHOT_HEX.hex_decode()):
		_fail("pet snapshot reducer failed")
		return
	if not state.mark_pet_sent(1,1,101):
		_fail("valid pet summon request rejected")
		return
	if not state.apply_frame(RESULT_HEX.hex_decode()):
		_fail("pet result reducer failed")
		return
	var view := state.snapshot()
	if (
		view.active_pet_uid != 101 or view.pet_instances.size() != 2 or
		view.pending_pet_sequence != 0 or not view.pet_snapshot_received or
		int(view.pet_instances[101].definition_id) != 5001
	):
		_fail("pet reducer state mismatch")
		return

	var stale_snapshot := SNAPSHOT_HEX.hex_decode()
	stale_snapshot.encode_s64(8,6)
	if not state.apply_frame(stale_snapshot) or int(state.snapshot().active_pet_uid) != 101:
		_fail("stale pet snapshot changed current state")
		return

	print("PET_WIRE_PASS csharp_golden=true reducer=true pets=2")
	quit(0)

func _fail(message:String)->void:
	push_error("PET_WIRE_FAIL "+message)
	quit(1)
