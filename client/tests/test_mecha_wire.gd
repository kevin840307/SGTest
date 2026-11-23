extends SceneTree
const Codec = preload("res://network/wire_codec.gd")
const State = preload("res://world/world_state.gd")
const ClientSession = preload("res://session/client_session.gd")

const COMMAND_HEX := "1d00000001007b00070000000000000001000000016500000000000000"
const RESULT_HEX := "2600000001007c00070000000000000001000000010065000000000000006500000000000000"
const SNAPSHOT_HEX := "4a00000001007d000700000000000000660000000000000002006500000000000000591b000004000000580000000000000066000000000000005a1b000007000000d204000000000000"

func _initialize() -> void:
	var command := Codec.encode_mecha_command(7,1,1,101)
	if command.hex_encode() != COMMAND_HEX or Codec.frame_opcode(command) != Codec.OPCODE_MECHA_COMMAND:
		_fail("C# mount command golden mismatch")
		return
	if not Codec.encode_mecha_command(7,1,3,101).is_empty():
		_fail("refresh accepted nonzero mecha uid")
		return
	if not Codec.encode_mecha_command(7,1,1,0).is_empty():
		_fail("mount accepted zero mecha uid")
		return

	var result := Codec.decode_mecha_result(RESULT_HEX.hex_decode())
	if (
		result.is_empty() or int(result.mecha_uid) != 101 or
		int(result.mounted_mecha_uid) != 101 or int(result.status) != 0 or
		int(result.mecha_sequence) != 1
	):
		_fail("mecha result golden rejected")
		return

	var snapshot := Codec.decode_mecha_snapshot(SNAPSHOT_HEX.hex_decode())
	if (
		snapshot.is_empty() or int(snapshot.mounted_mecha_uid) != 102 or
		snapshot.mechas.size() != 2 or
		int(snapshot.mechas[0].definition_id) != 7001 or
		int(snapshot.mechas[0].level) != 4 or
		int(snapshot.mechas[0].experience) != 88 or
		int(snapshot.mechas[1].definition_id) != 7002 or
		int(snapshot.mechas[1].level) != 7 or
		int(snapshot.mechas[1].experience) != 1234
	):
		_fail("mecha snapshot golden rejected")
		return

	var state := State.new()
	state.state = State.STATE_IN_WORLD
	state.world_ready = true
	state.map_epoch = 7
	state.actor_id = 1001
	state.last_world_ready_ack_epoch = 7
	if not state.apply_frame(SNAPSHOT_HEX.hex_decode()):
		_fail("mecha snapshot reducer failed")
		return
	var initial := state.snapshot()
	if (
		int(initial.mounted_mecha_uid) != 102 or initial.mecha_instances.size() != 2 or
		not bool(initial.mecha_snapshot_received)
	):
		_fail("mecha authoritative roster was not published")
		return

	if not state.mark_mecha_sent(1,1,101):
		_fail("valid mecha mount request rejected")
		return
	if state.mark_mecha_sent(2,1,102):
		_fail("second mecha request admitted while one was pending")
		return
	if not state.apply_frame(RESULT_HEX.hex_decode()):
		_fail("mecha result reducer failed")
		return
	var view := state.snapshot()
	if (
		int(view.mounted_mecha_uid) != 101 or int(view.pending_mecha_sequence) != 0 or
		int(view.last_mecha_result_sequence) != 1 or int(view.last_mecha_result_status) != 0
	):
		_fail("mecha reducer authority state mismatch")
		return

	var stale_snapshot := SNAPSHOT_HEX.hex_decode()
	stale_snapshot.encode_s64(8,6)
	if not state.apply_frame(stale_snapshot) or int(state.snapshot().mounted_mecha_uid) != 101:
		_fail("stale mecha snapshot changed current state")
		return
	if int(state.snapshot().stale_mecha_frames) != 1:
		_fail("stale mecha frame was not counted")
		return

	var malformed := SNAPSHOT_HEX.hex_decode()
	malformed.encode_s64(16,999)
	if not Codec.decode_mecha_snapshot(malformed).is_empty():
		_fail("snapshot accepted mounted uid outside authoritative roster")
		return

	# Instantiation additionally parses the real ClientSession path that exposes
	# mecha_mount/mecha_dismount/mecha_refresh and shares this WorldState reducer.
	var session := ClientSession.new()
	if not session.has_method("mecha_mount") or not session.has_method("mecha_dismount") or not session.has_method("mecha_refresh"):
		_fail("ClientSession mecha command surface missing")
		return

	print("MECHA_WIRE_PASS csharp_golden=true reducer=true session_surface=true mechas=2")
	quit(0)

func _fail(message:String)->void:
	push_error("MECHA_WIRE_FAIL "+message)
	quit(1)
