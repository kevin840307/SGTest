extends SceneTree
const Codec = preload("res://network/wire_codec.gd")
const State = preload("res://world/world_state.gd")
const ClientSession = preload("res://session/client_session.gd")

const COMMAND_HEX := "2500000001007b000700000000000000010000000165000000000000000000000000000000"
const RESULT_HEX := "3700000001007c000700000000000000010000000100650000000000000065000000000000000000000000000000000000000000000000"
const SNAPSHOT_HEX := "4a00000001007d000700000000000000660000000000000002006500000000000000591b000004000000580000000000000066000000000000005a1b000007000000d204000000000000"
const EQUIPMENT_HEX := "5600000001008000070000000000000002006500000000000000292300000000000001bd1b0000f501000000000000055000000065000000000000002a2300000000000003851c0000f60100000000000002ffffffff"
const EQUIPMENT_EMPTY_HEX := "120000000100800007000000000000000000"
const PRESENTATION_HEX := "4100000001007e0007000000000000006300000000000000e9030000000000000b0000000000000007000000000000000166000000000000005a1b000007000000"
const PRESENTATION_CLEAR_HEX := "4100000001007e0007000000000000006400000000000000e9030000000000000b0000000000000007000000000000000000000000000000000000000000000000"

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
	var equip_command:=Codec.encode_mecha_command(7,2,4,101,9001)
	if equip_command.is_empty() or Codec.frame_opcode(equip_command)!=Codec.OPCODE_MECHA_COMMAND:
		_fail("valid mecha equip command rejected")
		return
	if not Codec.encode_mecha_command(7,2,4,101,0).is_empty():
		_fail("mecha equip accepted zero item uid")
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
	var equipment:=Codec.decode_mecha_equipment_snapshot(EQUIPMENT_HEX.hex_decode())
	if (
		equipment.is_empty() or equipment.items.size()!=2 or
		int(equipment.items[0].item_uid)!=9001 or
		int(equipment.items[0].slot_id)!=1 or
		int(equipment.items[1].slot_id)!=3 or
		int(equipment.items[1].durability_raw)!=-1 or
		Codec.frame_opcode(EQUIPMENT_HEX.hex_decode())!=Codec.OPCODE_MECHA_EQUIPMENT_SNAPSHOT
	):
		_fail("mecha equipment snapshot golden rejected")
		return

	var presentation:=Codec.decode_mecha_presentation(PRESENTATION_HEX.hex_decode())
	if (
		presentation.is_empty() or not bool(presentation.mounted) or
		int(presentation.owner_actor_id)!=1001 or int(presentation.owner_connection_epoch)!=11 or
		int(presentation.owner_map_epoch)!=7 or int(presentation.mecha_uid)!=102 or
		int(presentation.definition_id)!=7002 or int(presentation.level)!=7 or
		Codec.frame_opcode(PRESENTATION_HEX.hex_decode())!=Codec.OPCODE_MECHA_PRESENTATION
	):
		_fail("mecha presentation golden rejected")
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
	state.inventory_items[9001]={
		"item_uid":9001,"item_definition_id":7101,"quantity":1,
		"drop_id":501,"enhancement_level":5,"durability_raw":80
	}
	state.inventory_items[9002]={
		"item_uid":9002,"item_definition_id":7301,"quantity":1,
		"drop_id":502,"enhancement_level":2,"durability_raw":-1
	}
	if not state.apply_frame(EQUIPMENT_HEX.hex_decode()):
		_fail("mecha equipment reducer failed")
		return
	var equipped_view:=state.snapshot()
	if (
		equipped_view.mecha_equipment_items.size()!=2 or
		not equipped_view.inventory_items.is_empty() or
		not bool(equipped_view.mecha_equipment_snapshot_received)
	):
		_fail("mecha equipment snapshot did not reconcile physical inventory location")
		return
	if not state.apply_frame(EQUIPMENT_EMPTY_HEX.hex_decode()):
		_fail("mecha empty equipment reducer failed")
		return
	var unequipped_view:=state.snapshot()
	if (
		not unequipped_view.mecha_equipment_items.is_empty() or
		unequipped_view.inventory_items.size()!=2 or
		int(unequipped_view.inventory_items[9001].enhancement_level)!=5 or
		int(unequipped_view.inventory_items[9002].drop_id)!=502
	):
		_fail("mecha equipment removal did not restore exact inventory metadata")
		return
	if not state.apply_frame(PRESENTATION_HEX.hex_decode()):
		_fail("mecha presentation reducer mount failed")
		return
	var presented:Dictionary=state.snapshot().mecha_presentations.get(1001,{})
	if presented.is_empty() or int(presented.get("mecha_uid",0))!=102 or not bool(presented.get("mounted",false)):
		_fail("mecha presentation reducer did not publish mounted state")
		return
	if not state.apply_frame(PRESENTATION_CLEAR_HEX.hex_decode()) or not state.snapshot().mecha_presentations.is_empty():
		_fail("mecha presentation reducer did not clear dismount")
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
	# mount/dismount/refresh plus authoritative equip/unequip.
	var session := ClientSession.new()
	if (
		not session.has_method("mecha_mount") or
		not session.has_method("mecha_dismount") or
		not session.has_method("mecha_refresh") or
		not session.has_method("mecha_equip") or
		not session.has_method("mecha_unequip")
	):
		_fail("ClientSession mecha command surface missing")
		return

	print("MECHA_WIRE_PASS csharp_golden=true reducer=true equipment=true presentation=true session_surface=true mechas=2")
	quit(0)

func _fail(message:String)->void:
	push_error("MECHA_WIRE_FAIL "+message)
	quit(1)
