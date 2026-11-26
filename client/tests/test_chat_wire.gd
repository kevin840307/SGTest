extends SceneTree

const WireCodec=preload("res://network/wire_codec.gd")
const WorldState=preload("res://world/world_state.gd")
const ClientSession=preload("res://session/client_session.gd")

class CapturingTransport:
	extends RefCounted
	var sent:Array[PackedByteArray]=[]
	func send_frame(frame:PackedByteArray)->bool:
		sent.append(frame.duplicate())
		return true
const COMMAND_HEX:="2400000001005C0003000000000000000900000000020C00E99984E8BF91E6B8ACE8A9A6"
const DELIVERY_HEX:="3A00000001005D0003000000000000000B00000000000000050000000000000009000000000204000C004865726FE99984E8BF91E6B8ACE8A9A6"
const WHISPER_COMMAND_HEX:="2B0000000100620003000000000000000A0000001600000000000000030C00E8B7A8E59C96E7A781E8A88A"
const WHISPER_NAME_COMMAND_HEX:="370000000100620003000000000000000B0000000000000000000000020C00E68C89E5908DE7A781E8A88A0A005461726765744865726F"
const WHISPER_RESULT_HEX:="330000000100630003000000000000000A00000000160000000000000007000000000000000C004D756C74694865726F4F6E65"
const WHISPER_DELIVERY_HEX:="3A00000001005D0003000000000000000B0000000000000005000000000000000A000000040304000C004865726FE8B7A8E59C96E7A781E8A88A"
const FRIEND_MESSAGE_COMMAND_HEX:="3000000001008B0003000000000000000C00000016000000000000001200E5A5BDE58F8BE58DB3E69982E8A88AE681AF"
const EMOTION_COMMAND_HEX:="150000000100890003000000000000000900000005"
const EMOTION_VISUAL_HEX:="2500000001008A0003000000000000000B0000000000000005000000000000000900000005"

func _fail(reason:String)->void:
	push_error("CHAT_WIRE_FAIL "+reason)
	quit(1)

func _base_world(actor:int,peer:int=0)->WorldState:
	var world:=WorldState.new()
	world.state=WorldState.STATE_IN_WORLD
	world.actor_id=actor
	world.map_id=15
	world.map_epoch=3
	world.world_ready=true
	world.last_world_ready_ack_epoch=3
	if peer>0:
		world.entities[peer]={
			"actor_id":peer,"connection_epoch":5,
			"entity_map_epoch":3,"map_id":15,"revision":1}
	return world

func _initialize()->void:
	var command:=WireCodec.encode_chat_command(3,9,0,2,"附近測試")
	if command.hex_encode().to_upper()!=COMMAND_HEX:
		_fail("command cross-runtime golden")
		return
	if (
		not WireCodec.encode_chat_command(3,1,0,0,"").is_empty() or
		not WireCodec.encode_chat_command(
			3,1,0,0,"x".repeat(WireCodec.CHAT_MAX_MESSAGE_BYTES+1)).is_empty()
	):
		_fail("command bounds")
		return

	var delivery:=DELIVERY_HEX.hex_decode()
	var decoded:=WireCodec.decode_chat_delivery(delivery)
	if (
		decoded.is_empty() or int(decoded.sender_actor_id)!=11 or
		int(decoded.sender_connection_epoch)!=5 or
		int(decoded.chat_sequence)!=9 or int(decoded.channel)!=0 or
		int(decoded.bubble_type)!=2 or str(decoded.sender_name)!="Hero" or
		str(decoded.message)!="附近測試"
	):
		_fail("delivery cross-runtime golden")
		return

	var observer:=_base_world(22,11)
	if not observer.apply_frame(delivery):
		_fail("visible nearby delivery "+observer.protocol_error)
		return
	var snapshot:=observer.snapshot()
	if (
		(snapshot.chat_messages as Array).size()!=1 or
		str(snapshot.chat_messages[0].message)!="附近測試"
	):
		_fail("visible nearby backlog")
		return
	if not observer.apply_frame(delivery):
		_fail("duplicate delivery should be stale, not fatal")
		return
	snapshot=observer.snapshot()
	if (
		(snapshot.chat_messages as Array).size()!=1 or
		int(snapshot.stale_chat_deliveries)!=1
	):
		_fail("duplicate delivery replay")
		return

	var invisible:=_base_world(22)
	if not invisible.apply_frame(delivery):
		_fail("despawn-raced delivery should be stale")
		return
	if (
		not (invisible.snapshot().chat_messages as Array).is_empty() or
		int(invisible.snapshot().stale_chat_deliveries)!=1
	):
		_fail("non-visible sender leaked nearby chat")
		return

	var self_world:=_base_world(11)
	if not self_world.mark_chat_sent(9,0,2,"附近測試"):
		_fail("self send admission")
		return
	if not self_world.apply_frame(delivery):
		_fail("authoritative self echo "+self_world.protocol_error)
		return
	if (self_world.snapshot().chat_messages as Array).size()!=1:
		_fail("self echo missing")
		return

	var unsolicited:=_base_world(11)
	if unsolicited.apply_frame(delivery) or unsolicited.protocol_error!="chat_self_delivery_without_send":
		_fail("unsolicited self echo was accepted")
		return

	# World-channel level policy is source-backed Server authority (Old chat.cfg
	# [WorldChannel] lvllimit=10). Client admission must still transmit a valid
	# request even before/under that profile level; the Server decides delivery.
	var low_level_world:=_base_world(12)
	low_level_world.player_profile_ready=false
	low_level_world.player_level=0
	if not low_level_world.mark_chat_sent(1,1,0,"交由伺服器驗證"):
		_fail("world channel was client-authorized instead of server-authorized")
		return

	var warband_receive:=_base_world(22)
	warband_receive.clan_id=44
	warband_receive.clan_members={
		11:{"actor_id":11,"name":"Hero"},
		22:{"actor_id":22,"name":"Receiver"}
	}
	var warband_delivery:=DELIVERY_HEX.hex_decode()
	warband_delivery[36]=2
	if not warband_receive.apply_frame(warband_delivery):
		_fail("authoritative Warband delivery "+warband_receive.protocol_error)
		return
	var warband_snapshot:=warband_receive.snapshot()
	if (
		(warband_snapshot.chat_messages as Array).size()!=1 or
		int(warband_snapshot.chat_messages[0].channel)!=2 or
		str(warband_snapshot.chat_messages[0].message)!="附近測試"
	):
		_fail("Warband delivery did not enter authoritative history")
		return
	var no_clan:=_base_world(11)
	if no_clan.mark_chat_sent(1,2,0,"不得送出"):
		_fail("Warband send admitted without clan snapshot")
		return
	var warband_self:=_base_world(11)
	warband_self.clan_id=44
	warband_self.clan_members={11:{"actor_id":11,"name":"Hero"}}
	if not warband_self.mark_chat_sent(1,2,0,"戰團送出"):
		_fail("Warband send rejected with authoritative clan snapshot")
		return

	var whisper_command:=WireCodec.encode_whisper_command(
		3,10,22,3,"跨圖私訊")
	if whisper_command.hex_encode().to_upper()!=WHISPER_COMMAND_HEX:
		_fail("whisper command cross-runtime golden")
		return
	var whisper_name_command:=WireCodec.encode_whisper_command(
		3,11,0,2,"按名私訊","TargetHero")
	if whisper_name_command.hex_encode().to_upper()!=WHISPER_NAME_COMMAND_HEX:
		_fail("whisper name command cross-runtime golden")
		return
	if not WireCodec.encode_whisper_command(
			3,12,22,0,"invalid","TargetHero").is_empty():
		_fail("whisper dual target selector admitted")
		return
	var whisper_result:=WireCodec.decode_whisper_result(
		WHISPER_RESULT_HEX.hex_decode())
	if (
		whisper_result.is_empty() or int(whisper_result.chat_sequence)!=10 or
		int(whisper_result.status)!=0 or
		int(whisper_result.target_actor_id)!=22 or
		int(whisper_result.target_connection_epoch)!=7 or
		str(whisper_result.target_name)!="MultiHeroOne"
	):
		_fail("whisper result cross-runtime golden")
		return

	# Direct whisper must not depend on AOI visibility. The observer has no
	# sender entity but still accepts a Server-authenticated channel=Whisper
	# delivery and generation-scoped replay remains stale.
	var whisper_observer:=_base_world(22)
	var whisper_delivery:=WHISPER_DELIVERY_HEX.hex_decode()
	if not whisper_observer.apply_frame(whisper_delivery):
		_fail("cross-map whisper delivery "+whisper_observer.protocol_error)
		return
	var whisper_observer_snapshot:=whisper_observer.snapshot()
	if (
		(whisper_observer_snapshot.chat_messages as Array).size()!=1 or
		int(whisper_observer_snapshot.chat_messages[0].channel)!=4 or
		str(whisper_observer_snapshot.chat_messages[0].direction)!="incoming" or
		str(whisper_observer_snapshot.chat_messages[0].message)!="跨圖私訊"
	):
		_fail("cross-map whisper backlog")
		return
	if not whisper_observer.apply_frame(whisper_delivery) or 			int(whisper_observer.snapshot().stale_chat_deliveries)!=1:
		_fail("duplicate whisper delivery replay")
		return

	# Sender history is published only after the authoritative Delivered result,
	# not when the command is queued locally.
	var whisper_sender:=_base_world(11)
	if not whisper_sender.mark_whisper_sent(10,22,3,"跨圖私訊"):
		_fail("whisper send admission")
		return
	if whisper_sender.mark_chat_sent(11,0,0,"不可越過 pending 私訊"):
		_fail("nearby bypassed pending whisper result")
		return
	if not (whisper_sender.snapshot().chat_messages as Array).is_empty():
		_fail("whisper was locally echoed before server result")
		return
	if not whisper_sender.apply_frame(WHISPER_RESULT_HEX.hex_decode()):
		_fail("whisper delivered result "+whisper_sender.protocol_error)
		return
	var sender_snapshot:=whisper_sender.snapshot()
	if (
		int(sender_snapshot.pending_whisper_sequence)!=0 or
		int(sender_snapshot.last_whisper_result_status)!=0 or
		(sender_snapshot.chat_messages as Array).size()!=1 or
		str(sender_snapshot.chat_messages[0].direction)!="outgoing" or
		str(sender_snapshot.chat_messages[0].target_name)!="MultiHeroOne" or
		str(sender_snapshot.chat_messages[0].message)!="跨圖私訊"
	):
		_fail("authoritative whisper sender history")
		return

	var name_sender:=_base_world(11)
	if not name_sender.mark_whisper_sent(11,0,2,"按名私訊","TargetHero"):
		_fail("whisper name send admission")
		return
	var unresolved_frame:="270000000100630003000000000000000B00000001000000000000000000000000000000000000".hex_decode()
	if WireCodec.decode_whisper_result(unresolved_frame).is_empty():
		_fail("unresolved name result codec")
		return
	if not name_sender.apply_frame(unresolved_frame):
		_fail("unresolved name result reducer "+name_sender.protocol_error)
		return
	var name_snapshot:=name_sender.snapshot()
	if (
		int(name_snapshot.pending_whisper_sequence)!=0 or
		int(name_snapshot.last_whisper_result_status)!=1 or
		not (name_snapshot.chat_messages as Array).is_empty()
	):
		_fail("unresolved name whisper created optimistic history")
		return

	var friend_command:=WireCodec.encode_friend_message_command(
		3,12,22,"好友即時訊息")
	if friend_command.hex_encode().to_upper()!=FRIEND_MESSAGE_COMMAND_HEX:
		_fail("friend message command cross-runtime golden")
		return
	if (
		WireCodec.encode_friend_message_command(
			3,13,22,"x".repeat(WireCodec.FRIEND_MESSAGE_MAX_MESSAGE_BYTES)).is_empty() or
		not WireCodec.encode_friend_message_command(
			3,14,22,"x".repeat(WireCodec.FRIEND_MESSAGE_MAX_MESSAGE_BYTES+1)).is_empty()
	):
		_fail("friend message original 0x65-byte bounds")
		return

	# Exercise the production ClientSession FIFO, not only the codec/state
	# helpers: UI/API input must queue, emit the dedicated wire, then wait for
	# the Server result before history is appended.
	var session_world:=_base_world(11)
	session_world.friends[22]={"actor_id":22,"name":"TargetHero","online":true}
	var capture:=CapturingTransport.new()
	var session:=ClientSession.new(capture)
	session._world=session_world
	session._published=session_world.snapshot()
	session._running=true
	if not session.send_friend_message(22," 好友即時訊息 "):
		_fail("client session friend-message enqueue")
		return
	if session._intents.size()!=1 or str(session._intents[0].get("type",""))!="friend_message":
		_fail("client session friend-message FIFO")
		return
	session._process_chat_intent(session._intents[0],"friend_message")
	var expected_session_frame:=WireCodec.encode_friend_message_command(
		3,1,22," 好友即時訊息 ")
	if (
		capture.sent.size()!=1 or capture.sent[0]!=expected_session_frame or
		not session._intents.is_empty() or
		int(session._world.snapshot().pending_whisper_sequence)!=1 or
		not (session._world.snapshot().friend_messages as Array).is_empty()
	):
		_fail("client session friend-message wire/pending authority")
		return

	# Friend-message delivery is intentionally separate from ordinary whisper
	# history. Channel 6 is accepted cross-map only for a current authoritative
	# friend and duplicate generation/sequence delivery is stale.
	var friend_observer:=_base_world(987654322)
	friend_observer.friends[11]={"actor_id":11,"name":"Hero","online":true}
	var friend_delivery:=WHISPER_DELIVERY_HEX.hex_decode()
	friend_delivery[36]=6
	friend_delivery[37]=0
	if not friend_observer.apply_frame(friend_delivery):
		_fail("friend message delivery "+friend_observer.protocol_error)
		return
	var friend_observer_snapshot:=friend_observer.snapshot()
	if (
		(friend_observer_snapshot.friend_messages as Array).size()!=1 or
		not (friend_observer_snapshot.chat_messages as Array).is_empty() or
		str(friend_observer_snapshot.friend_messages[0].direction)!="incoming" or
		int(friend_observer_snapshot.friend_messages[0].peer_actor_id)!=11
	):
		_fail("friend message polluted normal chat or missed contact history")
		return
	if (
		not friend_observer.apply_frame(friend_delivery) or
		int(friend_observer.snapshot().stale_chat_deliveries)!=1
	):
		_fail("duplicate friend message delivery replay")
		return

	# Sender history is added only after the Server-authoritative Delivered
	# result. The archive is per-character, matching Old ArchiveP2PHistory /
	# RetrieveP2PHistory behavior across a relog.
	var friend_sender:=_base_world(987654321)
	friend_sender.friends[22]={"actor_id":22,"name":"TargetHero","online":true}
	if not friend_sender.mark_friend_message_sent(10,22,"好友即時訊息"):
		_fail("friend message sender admission")
		return
	if not (friend_sender.snapshot().friend_messages as Array).is_empty():
		_fail("friend message was optimistically echoed before server result")
		return
	if not friend_sender.apply_frame(WHISPER_RESULT_HEX.hex_decode()):
		_fail("friend message sender result "+friend_sender.protocol_error)
		return
	var friend_sender_snapshot:=friend_sender.snapshot()
	if (
		(friend_sender_snapshot.friend_messages as Array).size()!=1 or
		not (friend_sender_snapshot.chat_messages as Array).is_empty() or
		str(friend_sender_snapshot.friend_messages[0].direction)!="outgoing" or
		int(friend_sender_snapshot.friend_messages[0].peer_actor_id)!=22 or
		not str(friend_sender_snapshot.friend_messages[0].local_timestamp).begins_with("[ Y")
	):
		_fail("authoritative friend message sender history")
		return
	var reloaded:=WorldState.new()
	reloaded.actor_id=987654321
	reloaded._load_friend_message_history()
	if (
		(reloaded.snapshot().friend_messages as Array).size()!=1 or
		str(reloaded.snapshot().friend_messages[0].message)!="好友即時訊息"
	):
		_fail("friend message relog archive retrieval")
		return

	# Original type-15 offline mailbox returns a successful queued state even
	# though the peer has no live connection. That result must close the pending
	# send and archive the outgoing message instead of failing as unavailable.
	var offline_sender:=_base_world(987654323)
	offline_sender.friends[22]={"actor_id":22,"name":"MultiHeroOne","online":false}
	if not offline_sender.mark_friend_message_sent(10,22,"離線信箱"):
		_fail("offline friend message sender admission")
		return
	var offline_result:=WHISPER_RESULT_HEX.hex_decode()
	offline_result[20]=3
	for i in range(29,37):
		offline_result[i]=0
	var decoded_offline:=WireCodec.decode_whisper_result(offline_result)
	if (
		decoded_offline.is_empty() or int(decoded_offline.status)!=3 or
		int(decoded_offline.target_connection_epoch)!=0 or
		str(decoded_offline.target_name)!="MultiHeroOne"
	):
		_fail("offline friend queued result codec")
		return
	if not offline_sender.apply_frame(offline_result):
		_fail("offline friend queued result reducer "+offline_sender.protocol_error)
		return
	var offline_snapshot:=offline_sender.snapshot()
	if (
		int(offline_snapshot.pending_whisper_sequence)!=0 or
		int(offline_snapshot.last_whisper_result_status)!=3 or
		(offline_snapshot.friend_messages as Array).size()!=1 or
		str(offline_snapshot.friend_messages[0].direction)!="outgoing" or
		not bool(offline_snapshot.friend_messages[0].get("offline_queued",false)) or
		int(offline_snapshot.friend_messages[0].target_connection_epoch)!=0 or
		str(offline_snapshot.friend_messages[0].message)!="離線信箱"
	):
		_fail("offline friend queued sender history")
		return

	DirAccess.remove_absolute(
		ProjectSettings.globalize_path(friend_sender._friend_message_history_path()))
	DirAccess.remove_absolute(
		ProjectSettings.globalize_path(friend_observer._friend_message_history_path()))
	DirAccess.remove_absolute(
		ProjectSettings.globalize_path(offline_sender._friend_message_history_path()))

	var emotion_command:=WireCodec.encode_emotion_command(3,9,5)
	if emotion_command.hex_encode().to_upper()!=EMOTION_COMMAND_HEX:
		_fail("emotion command cross-runtime golden")
		return
	if not WireCodec.encode_emotion_command(3,10,13).is_empty():
		_fail("emotion command admitted id outside original 1..12")
		return
	var emotion_visual:=EMOTION_VISUAL_HEX.hex_decode()
	var decoded_emotion:=WireCodec.decode_emotion_visual(emotion_visual)
	if (
		decoded_emotion.is_empty() or int(decoded_emotion.receiver_map_epoch)!=3 or
		int(decoded_emotion.actor_id)!=11 or int(decoded_emotion.connection_epoch)!=5 or
		int(decoded_emotion.emotion_sequence)!=9 or int(decoded_emotion.emotion_id)!=5
	):
		_fail("emotion visual cross-runtime golden")
		return
	var emotion_observer:=_base_world(22,11)
	if not emotion_observer.apply_frame(emotion_visual):
		_fail("visible emotion delivery "+emotion_observer.protocol_error)
		return
	var emotion_snapshot:=emotion_observer.snapshot()
	if (
		(emotion_snapshot.emotion_events as Array).size()!=1 or
		not (emotion_snapshot.chat_messages as Array).is_empty()
	):
		_fail("emotion delivery polluted chat or missed event")
		return
	if not emotion_observer.apply_frame(emotion_visual) or int(emotion_observer.snapshot().stale_emotion_visuals)!=1:
		_fail("duplicate emotion visual replay")
		return
	var emotion_self:=_base_world(11)
	if not emotion_self.mark_emotion_sent(9,5) or not emotion_self.apply_frame(emotion_visual):
		_fail("authoritative self emotion echo "+emotion_self.protocol_error)
		return
	if (emotion_self.snapshot().emotion_events as Array).size()!=1:
		_fail("self emotion event missing")
		return
	var emotion_unsolicited:=_base_world(11)
	if emotion_unsolicited.apply_frame(emotion_visual) or emotion_unsolicited.protocol_error!="future_self_emotion_visual":
		_fail("unsolicited self emotion visual was accepted")
		return

	print("CHAT_WIRE_PASS command=92 delivery=93 emotion_command=137 emotion_visual=138 whisper_command=98 whisper_result=99 friend_message_command=139 friend_message_channel=6 friend_message_max_bytes=101 friend_message_archive=true utf8=true trusted_echo=true emotion_authoritative=true world_policy=SERVER_AUTHORITY visible_only_nearby=true whisper_crossmap=true replay=STALE unsolicited_self=FAIL_CLOSED")
	quit(0)
