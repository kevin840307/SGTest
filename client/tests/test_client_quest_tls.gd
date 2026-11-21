extends SceneTree

const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message:String,session=null)->void:
	if session!=null:
		session.stop()
	push_error("CLIENT_QUEST_TLS_FAIL "+message)
	quit(1)


func _initialize()->void:
	var args:=OS.get_cmdline_user_args()
	if (
		args.size()!=6 or not str(args[0]).is_valid_int() or
		str(args[2]) not in ["mutate","relogin"] or
		not str(args[3]).is_valid_int() or not str(args[4]).is_valid_int() or
		not str(args[5]).is_valid_int()
	):
		_fail("expected port/certificate/role/character/npc/quest")
		return
	var port:=int(args[0])
	var role:=str(args[2])
	var character_id:=int(args[3])
	var npc:=int(args[4])
	var quest_id:=int(args[5])
	var cert:=X509Certificate.new()
	if cert.load(str(args[1]))!=OK:
		_fail("certificate load")
		return
	var session=Session.new()
	if session.connect_secure(
		"127.0.0.1",port,USERNAME,PASSWORD,"localhost",cert)!=OK:
		_fail("connect",session)
		return
	var selecting:=_wait(session,func(v):
		return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,5000)
	if (
		selecting.is_empty() or selecting.characters.size()!=1 or
		int(selecting.characters[0].character_id)!=character_id
	):
		_fail("character list "+str(session.snapshot()),session)
		return
	if not session.choose_character(character_id):
		_fail("choose",session)
		return
	var ready:=_wait(session,func(v):
		return (
			int(v.state)==WorldState.STATE_IN_WORLD and
			bool(v.world_ready) and
			int(v.pending_world_ready_ack_epoch)==0 and
			int(v.last_world_ready_ack_epoch)==int(v.map_epoch)
		),7000)
	if ready.is_empty():
		_fail("world ready "+str(session.snapshot()),session)
		return
	if (
		int(ready.get("quest_sequence",0))!=0 or
		not (ready.get("quest_view",{}) as Dictionary).is_empty()
	):
		_fail("quest state was not generation-clean "+str(ready),session)
		return

	if not session.open_quest(npc):
		_fail("open quest intent refused",session)
		return
	var opened:=_wait(session,func(v):
		return (
			int(v.pending_quest_sequence)==0 and
			int(v.last_quest_response_sequence)==1 and
			not (v.quest_view as Dictionary).is_empty()
		),7000)
	if opened.is_empty():
		_fail("quest open response "+str(session.snapshot()),session)
		return
	var view:Dictionary=opened.quest_view
	if (
		int(view.npc_spawn_id)!=npc or int(view.quest_id)!=quest_id or
		str(view.title)!="Verified Quest Fixture"
	):
		_fail("quest identity/view mismatch "+str(view),session)
		return

	if role=="relogin":
		if (
			int(view.state)!=2 or int(view.stage)!=2 or
			int(view.progress_revision)!=2 or
			not (view.choices as Array).is_empty() or
			str(view.text)!="任務已完成。"
		):
			_fail("durable completed view missing "+str(view),session)
			return
		session.stop()
		print(
			"CLIENT_QUEST_TLS_PASS role=relogin completed=true stage=2 revision=2 choices=0")
		quit(0)
		return

	if (
		int(view.state)!=0 or int(view.stage)!=0 or
		int(view.progress_revision)!=0 or (view.choices as Array).size()!=2
	):
		_fail("initial quest view mismatch "+str(view),session)
		return
	if not _has_choice(view,1,"接受") or not _has_choice(
		view,3,"另一個合法起始選項"):
		_fail("initial Server choices mismatch "+str(view),session)
		return

	if not session.choose_quest_choice(1):
		_fail("accept quest choice refused",session)
		return
	var active:=_wait(session,func(v):
		return (
			int(v.pending_quest_sequence)==0 and
			int(v.last_quest_response_sequence)==2 and
			int((v.quest_view as Dictionary).get("state",-1))==1 and
			int((v.quest_view as Dictionary).get("stage",-1))==1 and
			int((v.quest_view as Dictionary).get("progress_revision",-1))==1
		),7000)
	if active.is_empty():
		_fail("active quest settlement "+str(session.snapshot()),session)
		return
	var active_view:Dictionary=active.quest_view
	if (
		(active_view.choices as Array).size()!=1 or
		not _has_choice(active_view,2,"完成")
	):
		_fail("active Server choice mismatch "+str(active_view),session)
		return

	if not session.choose_quest_choice(2):
		_fail("complete quest choice refused",session)
		return
	var completed:=_wait(session,func(v):
		return (
			int(v.pending_quest_sequence)==0 and
			int(v.last_quest_response_sequence)==3 and
			int((v.quest_view as Dictionary).get("state",-1))==2 and
			int((v.quest_view as Dictionary).get("stage",-1))==2 and
			int((v.quest_view as Dictionary).get("progress_revision",-1))==2
		),7000)
	if completed.is_empty():
		_fail("completed quest settlement "+str(session.snapshot()),session)
		return
	var final_view:Dictionary=completed.quest_view
	if (
		not (final_view.choices as Array).is_empty() or
		str(final_view.text)!="任務已完成。"
	):
		_fail("completed Server View mismatch "+str(final_view),session)
		return
	session.stop()
	print(
		"CLIENT_QUEST_TLS_PASS role=mutate open=true choice1=true choice2=true " +
		"completed=true stage=2 revision=2 client_stage_math=false")
	quit(0)


func _has_choice(view:Dictionary,choice_id:int,label:String)->bool:
	for raw in view.get("choices",[]):
		var row:Dictionary=raw
		if (
			int(row.get("choice_id",0))==choice_id and
			str(row.get("label",""))==label
		):
			return true
	return false


func _wait(session,predicate:Callable,timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		if (
			int(value.state)==WorldState.STATE_FAILED and
			not bool(value.get("reconnecting",false))
		):
			return {}
		OS.delay_msec(2)
	return {}
