extends SceneTree

const WireCodec=preload("res://network/wire_codec.gd")
const WorldState=preload("res://network/world_state.gd")
const COMMAND_HEX:="2400000001005C0003000000000000000900000000020C00E99984E8BF91E6B8ACE8A9A6"
const DELIVERY_HEX:="3A00000001005D0003000000000000000B00000000000000050000000000000009000000000204000C004865726FE99984E8BF91E6B8ACE8A9A6"

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

	print("CHAT_WIRE_PASS command=92 delivery=93 utf8=true trusted_echo=true visible_only=true replay=STALE unsolicited_self=FAIL_CLOSED")
	quit(0)
