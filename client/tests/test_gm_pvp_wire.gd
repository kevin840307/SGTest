extends SceneTree

const WireCodec=preload("res://wire/wire_codec.gd")
const WorldState=preload("res://world/world_state.gd")

func _fail(reason:String)->void:
	push_error("GM_PVP_WIRE_FAIL "+reason)
	quit(1)

func _capability(authorized:bool)->PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(9)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_GM_CAPABILITY)
	writer.put_u8(1 if authorized else 0)
	return writer.data_array

func _result(epoch:int,sequence:int,status:int,target:int,name_state:int)->PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(30)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_GM_PVP_NAME_RESULT)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(status)
	writer.put_64(target)
	writer.put_u8(name_state)
	return writer.data_array

func _initialize()->void:
	var command:=WireCodec.encode_gm_pvp_name_command(3,7,99,4)
	if command.size()!=29 or WireCodec.frame_opcode(command)!=WireCodec.OPCODE_GM_PVP_NAME_COMMAND:
		_fail("GM command codec/size")
		return
	if (
		not WireCodec.encode_gm_pvp_name_command(3,7,99,5).is_empty() or
		not WireCodec.encode_gm_pvp_name_command(3,0,99,0).is_empty() or
		not WireCodec.encode_gm_pvp_name_command(3,7,0,0).is_empty()
	):
		_fail("GM command validation")
		return
	var decoded_cap:=WireCodec.decode_gm_capability(_capability(true))
	if decoded_cap.is_empty() or not bool(decoded_cap.authorized):
		_fail("GM capability codec")
		return
	var decoded_result:=WireCodec.decode_gm_pvp_name_result(
		_result(3,7,0,99,4))
	if (
		decoded_result.is_empty() or int(decoded_result.gm_sequence)!=7 or
		int(decoded_result.target_actor_id)!=99 or int(decoded_result.name_state)!=4
	):
		_fail("GM result codec")
		return

	var world:=WorldState.new()
	world.state=WorldState.STATE_IN_WORLD
	world.actor_id=11
	world.map_id=13
	world.map_epoch=3
	world.world_ready=true
	world.last_world_ready_ack_epoch=3
	world.entities[99]={"actor_id":99,"connection_epoch":4,"entity_map_epoch":3}
	world.player_identities[99]={
		"actor_id":99,"connection_epoch":4,"entity_map_epoch":3,
		"mode":1,"red_man":false,"pink_until_unix_ms":0,
		"pvp_updated_unix_ms":10,"display_name":"Target"}

	if world.mark_gm_pvp_name_sent(1,99,2):
		_fail("GM intent escaped server capability")
		return
	if (
		not world.apply_frame(_capability(true)) or
		not bool(world.snapshot().gm_authorized)
	):
		_fail("server GM capability not applied")
		return
	if (
		not world.mark_gm_pvp_name_sent(1,99,2) or
		world.mark_gm_pvp_name_sent(2,99,3)
	):
		_fail("GM command singleflight")
		return

	var identity_before:Dictionary=world.snapshot().player_identities[99]
	if not world.apply_frame(_result(3,1,0,99,2)):
		_fail("GM applied receipt")
		return
	var snap:Dictionary=world.snapshot()
	if (
		int(snap.last_gm_result_sequence)!=1 or
		int(snap.last_gm_result_status)!=0 or int(snap.pending_gm_sequence)!=0
	):
		_fail("GM receipt state")
		return
	var identity_after:Dictionary=snap.player_identities[99]
	if (
		bool(identity_after.red_man)!=bool(identity_before.red_man) or
		int(identity_after.pvp_updated_unix_ms)!=int(identity_before.pvp_updated_unix_ms)
	):
		_fail("GM receipt optimistically changed target identity")
		return

	if (
		not world.apply_frame(_result(3,1,0,99,2)) or
		int(world.snapshot().stale_gm_results)!=1
	):
		_fail("GM result replay not stale")
		return

	# A trusted map transfer keeps socket capability but resets its command
	# sequence namespace. The next connection/reconnect creates a new WorldState.
	world._clear_pvp_map()
	snap=world.snapshot()
	if (
		not bool(snap.gm_authorized) or int(snap.gm_sequence)!=0 or
		int(snap.last_gm_result_sequence)!=0
	):
		_fail("GM map reset lost capability or retained command cursor")
		return
	world.map_epoch=4
	world.last_world_ready_ack_epoch=4
	world.entities[99]={"actor_id":99,"connection_epoch":4,"entity_map_epoch":4}
	if (
		not world.mark_gm_pvp_name_sent(1,99,0) or
		not world.apply_frame(_result(4,1,5,99,0))
	):
		_fail("GM new-map offline result")
		return
	if int(world.snapshot().last_gm_result_status)!=5:
		_fail("GM target-offline result not retained")
		return

	# Server denial revokes local usability; privilege is never inferred.
	if (
		not world.mark_gm_pvp_name_sent(2,99,1) or
		not world.apply_frame(_result(4,2,2,99,1))
	):
		_fail("GM unauthorized result")
		return
	if bool(world.snapshot().gm_authorized) or world.mark_gm_pvp_name_sent(3,99,1):
		_fail("GM unauthorized result did not revoke local capability")
		return

	var unsolicited:=WorldState.new()
	unsolicited.state=WorldState.STATE_IN_WORLD
	unsolicited.actor_id=11
	unsolicited.map_id=13
	unsolicited.map_epoch=1
	unsolicited.world_ready=true
	unsolicited.last_world_ready_ack_epoch=1
	unsolicited.entities[99]={"actor_id":99}
	if not unsolicited.apply_frame(_capability(true)):
		_fail("unsolicited fixture capability")
		return
	if (
		unsolicited.apply_frame(_result(1,1,0,99,2)) or
		int(unsolicited.state)!=WorldState.STATE_FAILED
	):
		_fail("unsolicited GM result did not fail closed")
		return

	print("GM_PVP_WIRE_PASS capability_server_only=true command_bytes=29 result_bytes=30 singleflight=true receipt_no_identity_mutation=true map_cursor_reset=true reconnect_requires_new_capability=true unauthorized_revokes=true replay_stale=true unsolicited_failclosed=true")
	quit(0)
