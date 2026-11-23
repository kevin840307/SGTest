extends SceneTree

const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

var _last_error := ""

func _fail(message:String, session=null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_MECHA_AES_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args:=OS.get_cmdline_user_args()
	if args.size()!=1 or not str(args[0]).is_valid_int():
		_fail("expected port")
		return
	var port:=int(args[0])

	var first=_connect_world(port)
	if first==null:
		_fail("first login "+_last_error)
		return
	var baseline:Dictionary=first.snapshot()
	var mecha_a:=_find_mecha(baseline,7001,4,88)
	var mecha_b:=_find_mecha(baseline,7002,7,1234)
	if mecha_a<=0 or mecha_b<=0 or mecha_a==mecha_b or int(baseline.mounted_mecha_uid)!=0:
		_fail("unexpected mecha roster "+str(baseline.get("mecha_instances",{})),first)
		return

	if not first.mecha_mount(mecha_a):
		_fail("mount intent refused",first)
		return
	var mounted:=_wait(first,func(v):
		return (
			int(v.get("pending_mecha_sequence",0))==0 and
			int(v.get("last_mecha_result_sequence",0))==1 and
			int(v.get("last_mecha_result_status",-1))==0 and
			int(v.get("mounted_mecha_uid",0))==mecha_a
		),7000)
	if mounted.is_empty():
		_fail("mount settlement "+str(first.snapshot()),first)
		return
	first.stop()
	OS.delay_msec(30)

	var second=_connect_world(port)
	if second==null:
		_fail("relogin "+_last_error)
		return
	var restored:=_wait(second,func(v):
		return int(v.get("mounted_mecha_uid",0))==mecha_a,7000)
	if restored.is_empty():
		_fail("mounted mecha not restored "+str(second.snapshot()),second)
		return
	if _find_mecha(restored,7001,4,88)!=mecha_a or _find_mecha(restored,7002,7,1234)!=mecha_b:
		_fail("relogin roster identity changed "+str(restored.get("mecha_instances",{})),second)
		return

	if not second.mecha_dismount():
		_fail("dismount intent refused",second)
		return
	var dismounted:=_wait(second,func(v):
		return (
			int(v.get("pending_mecha_sequence",0))==0 and
			int(v.get("last_mecha_result_sequence",0))==1 and
			int(v.get("last_mecha_result_status",-1))==0 and
			int(v.get("mounted_mecha_uid",0))==0
		),7000)
	if dismounted.is_empty():
		_fail("dismount settlement "+str(second.snapshot()),second)
		return

	second.stop()
	print("CLIENT_MECHA_AES_PASS mecha_a=%d mecha_b=%d mount=true reconnect_mounted=true dismount=true roster_identity=true" % [mecha_a,mecha_b])
	quit(0)

func _connect_world(port:int):
	_last_error=""
	var session=Session.new()
	if session.connect_secure("127.0.0.1",port,USERNAME,PASSWORD)!=OK:
		_last_error="connect"
		session.stop()
		return null
	var selecting:=_wait(session,func(v):
		return int(v.get("state",-1))==WorldState.STATE_SELECTING_CHARACTER,5000)
	if selecting.is_empty() or selecting.get("characters",[]).size()!=1:
		_last_error="character list "+str(session.snapshot())
		session.stop()
		return null
	if not FixtureLogin.provision_and_choose(
		session,int(selecting.characters[0].character_id)):
		_last_error="choose"
		session.stop()
		return null
	var ready:=_wait(session,func(v):
		return (
			int(v.get("state",-1))==WorldState.STATE_IN_WORLD and
			bool(v.get("world_ready",false)) and
			bool(v.get("mecha_snapshot_received",false)) and
			int(v.get("pending_world_ready_ack_epoch",0))==0 and
			int(v.get("last_world_ready_ack_epoch",0))==int(v.get("map_epoch",0)) and
			v.get("mecha_instances",{}).size()==2
		),7000)
	if ready.is_empty():
		_last_error="world/mecha baseline "+str(session.snapshot())
		session.stop()
		return null
	return session

func _find_mecha(snapshot:Dictionary, definition_id:int, level:int, experience:int)->int:
	var mechas:Dictionary=snapshot.get("mecha_instances",{})
	for raw_uid in mechas:
		var mecha:Dictionary=mechas[raw_uid]
		if (
			int(mecha.get("definition_id",0))==definition_id and
			int(mecha.get("level",0))==level and
			int(mecha.get("experience",-1))==experience
		):
			return int(raw_uid)
	return 0

func _wait(session, predicate:Callable, timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		if int(value.get("state",-1))==WorldState.STATE_FAILED and not bool(value.get("reconnecting",false)):
			return {}
		OS.delay_msec(2)
	return {}
