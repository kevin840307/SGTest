extends SceneTree

const Session=preload("res://session/client_session.gd")
const State=preload("res://world/world_state.gd")

func _fail(message:String,session:RefCounted=null)->void:
	if session!=null:
		session.stop()
	push_error("CLIENT_REGISTRATION_TLS_FAIL "+message)
	quit(1)

func _initialize()->void:
	call_deferred("_run")

func _run()->void:
	var args:=OS.get_cmdline_user_args()
	if args.size()!=2:
		_fail("expected port and certificate")
		return
	var cert:=X509Certificate.new()
	if cert.load(args[1])!=OK:
		_fail("certificate load")
		return
	var session=Session.new()
	var error:int=session.register_secure(
		"127.0.0.1",int(args[0]),"fixture_user",
		"Fixture-Password-123!","localhost",cert)
	if error!=OK:
		_fail("register_secure start "+str(error),session)
		return
	var deadline:=Time.get_ticks_msec()+10000
	var snapshot:Dictionary=session.snapshot()
	while (
		int(snapshot.get("state",-1))!=State.STATE_SELECTING_CHARACTER and
		int(snapshot.get("state",-1))!=State.STATE_FAILED and
		Time.get_ticks_msec()<deadline
	):
		await process_frame
		snapshot=session.snapshot()
	if (
		int(snapshot.get("state",-1))!=State.STATE_SELECTING_CHARACTER or
		int(snapshot.get("registration_status",-1))!=0 or
		int(snapshot.get("auth_status",-1))!=0 or
		not (snapshot.get("characters",[]) as Array).is_empty() or
		int(snapshot.get("actor_id",0))!=0
	):
		_fail("registration/auth/list state "+str(snapshot),session)
		return
	session.stop()
	print("CLIENT_REGISTRATION_TLS_PASS created=true authenticated=true character_list_empty=true gameplay_authority=false")
	quit(0)
