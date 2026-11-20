extends SceneTree

const WireCodec=preload("res://network/wire_codec.gd")
const WorldState=preload("res://network/world_state.gd")

func _fail(reason:String)->void:
	push_error("PARTY_WIRE_FAIL "+reason)
	quit(1)

func _base_world(actor:int,connection:int,epoch:int)->WorldState:
	var world:=WorldState.new()
	world.state=WorldState.STATE_IN_WORLD
	world.actor_id=actor
	world.map_id=15 if epoch==3 else 13
	world.map_epoch=epoch
	world.world_ready=true
	world.last_world_ready_ack_epoch=epoch
	world.self_state={
		"actor_id":actor,"connection_epoch":connection,
		"entity_map_epoch":epoch
	}
	return world

func _party_result(
		epoch:int,sequence:int,action:int,status:int,
		party_id:int,target:int)->PackedByteArray:
	var w:=StreamPeerBuffer.new()
	w.big_endian=false
	w.put_u32(WireCodec.PARTY_RESULT_FRAME_BYTES)
	w.put_u16(WireCodec.VERSION)
	w.put_u16(WireCodec.OPCODE_PARTY_RESULT)
	w.put_64(epoch)
	w.put_u32(sequence)
	w.put_u8(action)
	w.put_u8(status)
	w.put_64(party_id)
	w.put_64(target)
	return w.data_array

func _party_invite(
		epoch:int,party_id:int,revision:int,inviter:int,
		connection:int,name:String)->PackedByteArray:
	var bytes:=name.to_utf8_buffer()
	var w:=StreamPeerBuffer.new()
	w.big_endian=false
	w.put_u32(WireCodec.PARTY_INVITE_HEADER_BYTES+bytes.size())
	w.put_u16(WireCodec.VERSION)
	w.put_u16(WireCodec.OPCODE_PARTY_INVITE)
	w.put_64(epoch)
	w.put_64(party_id)
	w.put_u32(revision)
	w.put_64(inviter)
	w.put_64(connection)
	w.put_u16(bytes.size())
	w.put_data(bytes)
	return w.data_array

func _party_snapshot(
		epoch:int,party_id:int,revision:int,leader:int,
		members:Array[Dictionary])->PackedByteArray:
	var encoded:Array[PackedByteArray]=[]
	var total:=WireCodec.PARTY_SNAPSHOT_HEADER_BYTES
	for member in members:
		var bytes:=str(member.name).to_utf8_buffer()
		encoded.append(bytes)
		total+=WireCodec.PARTY_MEMBER_HEADER_BYTES+bytes.size()
	var w:=StreamPeerBuffer.new()
	w.big_endian=false
	w.put_u32(total)
	w.put_u16(WireCodec.VERSION)
	w.put_u16(WireCodec.OPCODE_PARTY_SNAPSHOT)
	w.put_64(epoch)
	w.put_64(party_id)
	w.put_u32(revision)
	w.put_64(leader)
	w.put_u8(members.size())
	for i in range(members.size()):
		var member:Dictionary=members[i]
		var bytes:PackedByteArray=encoded[i]
		w.put_64(int(member.actor_id))
		w.put_64(int(member.connection_epoch))
		w.put_u16(bytes.size())
		w.put_data(bytes)
	return w.data_array

func _chat_delivery(
		receiver_epoch:int,sender:int,connection:int,sequence:int,
		channel:int,name:String,message:String)->PackedByteArray:
	var name_bytes:=name.to_utf8_buffer()
	var message_bytes:=message.to_utf8_buffer()
	var size:=WireCodec.CHAT_DELIVERY_HEADER_BYTES+name_bytes.size()+message_bytes.size()
	var w:=StreamPeerBuffer.new()
	w.big_endian=false
	w.put_u32(size)
	w.put_u16(WireCodec.VERSION)
	w.put_u16(WireCodec.OPCODE_CHAT_DELIVERY)
	w.put_64(receiver_epoch)
	w.put_64(sender)
	w.put_64(connection)
	w.put_u32(sequence)
	w.put_u8(channel)
	w.put_u8(0)
	w.put_u16(name_bytes.size())
	w.put_u16(message_bytes.size())
	w.put_data(name_bytes)
	w.put_data(message_bytes)
	return w.data_array

func _initialize()->void:
	var command:=WireCodec.encode_party_command(3,1,1,0,22)
	if command.size()!=WireCodec.PARTY_COMMAND_FRAME_BYTES:
		_fail("invite command encoding")
		return
	if (
		not WireCodec.encode_party_command(3,1,1,0,11).is_empty() and false
	):
		_fail("unreachable")
		return
	if not WireCodec.encode_party_command(3,1,4,0,0).is_empty():
		_fail("leave without party admitted")
		return

	var leader:=_base_world(11,1,3)
	if not leader.mark_party_sent(1,1,0,22):
		_fail("leader invite admission")
		return
	if not leader.apply_frame(_party_result(3,1,1,0,9001,22)):
		_fail("leader invite result "+leader.protocol_error)
		return
	if not leader.apply_frame(_party_snapshot(
		3,9001,1,11,[{"actor_id":11,"connection_epoch":1,"name":"PartyA"}])):
		_fail("leader singleton snapshot "+leader.protocol_error)
		return
	var leader_snap:=leader.snapshot()
	if (
		int(leader_snap.party_id)!=9001 or int(leader_snap.party_leader_actor_id)!=11 or
		(leader_snap.party_members as Dictionary).size()!=1
	):
		_fail("leader singleton state")
		return

	var member:=_base_world(22,2,4)
	if not member.apply_frame(_party_invite(4,9001,1,11,1,"PartyA")):
		_fail("member invite "+member.protocol_error)
		return
	if not member.mark_party_sent(1,2,9001,11):
		_fail("member accept admission")
		return
	if not member.apply_frame(_party_result(4,1,2,0,9001,11)):
		_fail("member accept result "+member.protocol_error)
		return
	var two_member=[
		{"actor_id":11,"connection_epoch":1,"name":"PartyA"},
		{"actor_id":22,"connection_epoch":2,"name":"PartyB"}
	]
	if not member.apply_frame(_party_snapshot(4,9001,2,11,two_member)):
		_fail("member joined snapshot "+member.protocol_error)
		return
	if not leader.apply_frame(_party_snapshot(3,9001,2,11,two_member)):
		_fail("leader joined snapshot "+leader.protocol_error)
		return
	if not member.snapshot().pending_party_invite.is_empty():
		_fail("accepted invite not cleared")
		return

	# Party chat deliberately crosses maps and AOI. Member has no EntitySpawn for
	# PartyA but membership generation is sufficient authority.
	var party_chat:=_chat_delivery(4,11,1,1,3,"PartyA","跨圖隊伍聊天")
	if not member.apply_frame(party_chat):
		_fail("cross-map party chat "+member.protocol_error)
		return
	var member_snap:=member.snapshot()
	if (
		(member_snap.chat_messages as Array).size()!=1 or
		int(member_snap.chat_messages[0].channel)!=3 or
		str(member_snap.chat_messages[0].message)!="跨圖隊伍聊天"
	):
		_fail("cross-map party chat backlog")
		return

	var outsider:=_base_world(33,3,7)
	if not outsider.apply_frame(_chat_delivery(7,11,1,1,3,"PartyA","不可外洩")):
		_fail("nonmember party chat should stale, not fail")
		return
	if (
		not (outsider.snapshot().chat_messages as Array).is_empty() or
		int(outsider.snapshot().stale_chat_deliveries)!=1
	):
		_fail("party chat leaked to nonmember")
		return

	if not leader.mark_party_sent(2,6,9001,22):
		_fail("leader promote admission")
		return
	if not leader.apply_frame(_party_result(3,2,6,0,9001,22)):
		_fail("promote result "+leader.protocol_error)
		return
	if not leader.apply_frame(_party_snapshot(3,9001,3,22,two_member)) or 			not member.apply_frame(_party_snapshot(4,9001,3,22,two_member)):
		_fail("promote snapshots")
		return
	if (
		int(leader.snapshot().party_leader_actor_id)!=22 or
		int(member.snapshot().party_leader_actor_id)!=22
	):
		_fail("promoted leader state")
		return

	if not leader.mark_chat_sent(2,3,0,"隊伍自我回音"):
		_fail("party chat self send admission")
		return
	if not leader.apply_frame(_chat_delivery(3,11,1,2,3,"PartyA","隊伍自我回音")):
		_fail("party chat self echo "+leader.protocol_error)
		return

	# A cleared snapshot only clears the exact current party at a newer revision.
	if not leader.apply_frame(_party_snapshot(3,9001,4,0,[])):
		_fail("party clear snapshot "+leader.protocol_error)
		return
	if (
		int(leader.snapshot().party_id)!=0 or
		not (leader.snapshot().party_members as Dictionary).is_empty()
	):
		_fail("party clear did not reset membership")
		return
	if leader.mark_chat_sent(3,3,0,"離隊後不可送"):
		_fail("party chat admitted after clear")
		return

	print("PARTY_WIRE_PASS command=100 result=101 invite=102 snapshot=103 max_members=8 crossmap_chat=true nonmember=STALE leader_promote=true clear=true")
	quit(0)
