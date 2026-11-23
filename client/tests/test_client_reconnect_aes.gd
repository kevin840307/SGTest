extends SceneTree
const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_RECONNECT_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()!=1 or not str(args[0]).is_valid_int():
		_fail("expected port/certificate")
		return
	var session = Session.new()
	if session.connect_secure("127.0.0.1",int(args[0]),USERNAME,PASSWORD) != OK:
		_fail("initial connect",session)
		return
	var selecting := _wait(session,func(v): return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,5000)
	if selecting.is_empty() or selecting.characters.size()!=1:
		_fail("initial character list "+str(session.snapshot()),session)
		return
	var character_id := int(selecting.characters[0].character_id)
	if not FixtureLogin.provision_and_choose(session, character_id):
		_fail("initial choose",session)
		return
	var first := _wait(session,func(v):
		return _ready(v) and int(v.actor_id)==character_id,5000)
	if first.is_empty():
		_fail("initial world "+str(session.snapshot()),session)
		return
	var first_epoch := int(first.map_epoch)
	if int(first.get("chat_sequence",0))!=0 or \
			not (first.get("chat_messages",[]) as Array).is_empty():
		_fail("initial chat state was not empty "+str(first),session)
		return
	if not session.send_nearby_chat("重連前附近訊息",1):
		_fail("pre-reconnect nearby chat intent",session)
		return
	var first_chat:=_wait(session,func(v):
		return _has_self_chat(v,character_id,"ReconnectHero","重連前附近訊息",1),5000)
	if first_chat.is_empty():
		_fail("pre-reconnect authoritative chat echo "+str(session.snapshot()),session)
		return
	var first_chat_row:Dictionary=(first_chat.chat_messages as Array)[0]
	var first_chat_connection:=int(first_chat_row.sender_connection_epoch)
	if first_chat_connection<=0:
		_fail("pre-reconnect chat missing sender generation",session)
		return
	if not session.move_target(24,8):
		_fail("first move intent",session)
		return
	var moved := _wait(session,func(v):
		return (
			_ready(v)
			and int(v.last_receipt_sequence)==1
			and not v.self_state.is_empty()
			and not bool(v.self_state.moving)
			and absf(float(v.x)-24.0)<0.001
			and absf(float(v.y)-8.0)<0.001
		),5000)
	if moved.is_empty():
		_fail("first authoritative move "+str(session.snapshot()),session)
		return
	print("CLIENT_RECONNECT_FIRST_MOVE_DONE epoch=%d x=24 y=8" % first_epoch)

	var restored := _wait(session,func(v):
		return (
			_ready(v)
			and int(v.reconnect_count)==1
			and not bool(v.reconnecting)
			and int(v.actor_id)==character_id
			and int(v.map_epoch)>first_epoch
			and absf(float(v.x)-24.0)<0.001
			and absf(float(v.y)-8.0)<0.001
		),12000)
	if restored.is_empty():
		_fail("automatic reconnect "+str(session.snapshot()),session)
		return
	var second_epoch := int(restored.map_epoch)
	print("CLIENT_RECONNECT_RESTORED old_epoch=%d new_epoch=%d count=%d" %
		[first_epoch,second_epoch,int(restored.reconnect_count)])
	if (
		int(restored.get("chat_sequence",0))!=0 or
		not (restored.get("chat_messages",[]) as Array).is_empty() or
		int(restored.get("stale_chat_deliveries",0))!=0
	):
		_fail("old chat generation survived reconnect "+str(restored),session)
		return
	if not session.send_nearby_chat("重連後附近訊息",2):
		_fail("post-reconnect nearby chat intent",session)
		return
	var second_chat:=_wait(session,func(v):
		return _has_self_chat(v,character_id,"ReconnectHero","重連後附近訊息",1),5000)
	if second_chat.is_empty() or (second_chat.chat_messages as Array).size()!=1:
		_fail("post-reconnect authoritative chat echo "+str(session.snapshot()),session)
		return
	var second_chat_row:Dictionary=(second_chat.chat_messages as Array)[0]
	if int(second_chat_row.sender_connection_epoch)<=0 or \
			int(second_chat_row.sender_connection_epoch)==first_chat_connection:
		_fail("chat sender connection generation did not advance",session)
		return
	if not session.move_target(40,8):
		_fail("second move intent",session)
		return
	var final := _wait(session,func(v):
		return (
			_ready(v)
			and int(v.map_epoch)==second_epoch
			and int(v.movement_sequence)==1
			and int(v.last_receipt_sequence)==1
			and not v.self_state.is_empty()
			and not bool(v.self_state.moving)
			and absf(float(v.x)-40.0)<0.001
			and absf(float(v.y)-8.0)<0.001
		),5000)
	if final.is_empty():
		_fail("post-reconnect gameplay "+str(session.snapshot()),session)
		return
	session.stop()
	print("CLIENT_RECONNECT_PASS same_character=true generation_advanced=true persisted_spawn=true move_sequence_reset=true world_ready_reacked=true chat_generation_reset=true reconnect_count=1")
	quit(0)

func _has_self_chat(
		v:Dictionary,actor_id:int,sender_name:String,message:String,
		sequence:int)->bool:
	var rows:Array=v.get("chat_messages",[])
	if int(v.get("chat_sequence",0))!=sequence or rows.size()!=1:
		return false
	var row:Dictionary=rows[0]
	return (
		int(row.get("sender_actor_id",0))==actor_id and
		int(row.get("chat_sequence",0))==sequence and
		int(row.get("channel",-1))==0 and
		str(row.get("sender_name",""))==sender_name and
		str(row.get("message",""))==message
	)


func _ready(v: Dictionary) -> bool:
	return (
		int(v.state)==WorldState.STATE_IN_WORLD
		and bool(v.world_ready)
		and not bool(v.reconnecting)
		and int(v.pending_world_ready_ack_epoch)==0
		and int(v.last_world_ready_ack_epoch)==int(v.map_epoch)
	)

func _wait(session, predicate: Callable, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value: Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		if int(value.state)==WorldState.STATE_FAILED and not bool(value.get("reconnecting",false)):
			return {}
		OS.delay_msec(2)
	return {}
