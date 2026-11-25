extends SceneTree

const WireCodec=preload("res://network/wire_codec.gd")
const WorldState=preload("res://world/world_state.gd")
const COMMAND_HEX:="2400000001005C0003000000000000000900000000020C00E99984E8BF91E6B8ACE8A9A6"
const DELIVERY_HEX:="3A00000001005D0003000000000000000B00000000000000050000000000000009000000000204000C004865726FE99984E8BF91E6B8ACE8A9A6"
const WHISPER_COMMAND_HEX:="2B0000000100620003000000000000000A0000001600000000000000030C00E8B7A8E59C96E7A781E8A88A"
const WHISPER_NAME_COMMAND_HEX:="370000000100620003000000000000000B0000000000000000000000020C00E68C89E5908DE7A781E8A88A0A005461726765744865726F"
const WHISPER_RESULT_HEX:="330000000100630003000000000000000A00000000160000000000000007000000000000000C004D756C74694865726F4F6E65"
const WHISPER_DELIVERY_HEX:="3A00000001005D0003000000000000000B0000000000000005000000000000000A000000040304000C004865726FE8B7A8E59C96E7A781E8A88A"

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

	print("CHAT_WIRE_PASS command=92 delivery=93 whisper_command=98 whisper_result=99 utf8=true trusted_echo=true world_policy=SERVER_AUTHORITY visible_only_nearby=true whisper_crossmap=true replay=STALE unsolicited_self=FAIL_CLOSED")
	quit(0)
