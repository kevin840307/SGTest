extends SceneTree
const Codec = preload("res://network/wire_codec.gd")
const State = preload("res://world/world_state.gd")

const COMMAND_HEX := "1d00000001006f00070000000000000001000000016500000000000000"
const RESULT_HEX := "2600000001007000070000000000000001000000010065000000000000006500000000000000"
const SNAPSHOT_HEX := "4a0000000100710007000000000000006600000000000000020065000000000000008913000004000000580000000000000066000000000000008a13000007000000d204000000000000"
const PET_SPAWN_HEX := "480000000100720007000000000000002a00000000000000e9030000000000000b00000000000000040000000000000066000000000000008a130000070000000000c04100001042"
const PET_DELTA_HEX := "400000000100740007000000000000002b00000000000000e9030000000000000b00000000000000040000000000000066000000000000000000204200001042"
const PET_DESPAWN_HEX := "380000000100730007000000000000002c00000000000000e9030000000000000b0000000000000004000000000000006600000000000000"

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
	var spawn := Codec.decode_pet_entity_spawn(PET_SPAWN_HEX.hex_decode())
	if (
		spawn.is_empty() or spawn.owner_actor_id != 1001 or spawn.pet_uid != 102 or
		spawn.definition_id != 5002 or spawn.x != 24.0 or spawn.y != 36.0
	):
		_fail("pet AOI spawn golden/position rejected")
		return
	var delta := Codec.decode_pet_entity_delta(PET_DELTA_HEX.hex_decode())
	if (
		delta.is_empty() or delta.owner_actor_id != 1001 or delta.pet_uid != 102 or
		delta.x != 40.0 or delta.y != 36.0
	):
		_fail("pet AOI movement delta golden rejected")
		return
	var despawn := Codec.decode_pet_entity_despawn(PET_DESPAWN_HEX.hex_decode())
	if despawn.is_empty() or despawn.owner_actor_id != 1001 or despawn.pet_uid != 102:
		_fail("pet AOI despawn golden rejected")
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
	if not state.apply_frame(PET_SPAWN_HEX.hex_decode()):
		_fail("pet AOI spawn reducer failed")
		return
	var pet_entity:Dictionary = state.snapshot().pet_entities[1001]
	if int(pet_entity.pet_uid) != 102 or float(pet_entity.x) != 24.0 or float(pet_entity.y) != 36.0:
		_fail("pet AOI spawn state/position missing")
		return
	if not state.apply_frame(PET_DELTA_HEX.hex_decode()):
		_fail("pet AOI movement delta reducer failed")
		return
	pet_entity=state.snapshot().pet_entities[1001]
	if (
		int(pet_entity.pet_uid) != 102 or int(pet_entity.definition_id) != 5002 or
		float(pet_entity.x) != 40.0 or float(pet_entity.y) != 36.0 or
		int(pet_entity.server_tick) != 43
	):
		_fail("pet AOI movement delta state missing or identity changed")
		return
	if not state.apply_frame(PET_DESPAWN_HEX.hex_decode()) or not state.snapshot().pet_entities.is_empty():
		_fail("pet AOI despawn reducer failed")
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

	print("PET_WIRE_PASS csharp_golden=true reducer=true aoi_presence=true aoi_movement=true pets=2")
	quit(0)

func _fail(message:String)->void:
	push_error("PET_WIRE_FAIL "+message)
	quit(1)
