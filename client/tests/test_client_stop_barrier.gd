extends SceneTree
const Session=preload("res://network/client_session.gd")
const WorldState=preload("res://network/world_state.gd")
const Fake=preload("res://tests/fake_stop_barrier_transport.gd")

func fail(message:String,session=null)->void:
	if session!=null:
		session.stop()
	push_error("CLIENT_STOP_BARRIER_FAIL "+message)
	quit(1)

func _initialize()->void:
	var fake=Fake.new()
	var session=Session.new(fake)
	if session.connect_secure("fixture",443,"user_01","Password-123456!","fixture",null)!=OK:
		fail("connect",session)
		return
	var selecting:=_wait(session,func(v):return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,1500)
	if selecting.is_empty() or selecting.characters.size()!=1:
		fail("character list",session)
		return
	if not session.choose_character(int(selecting.characters[0].character_id)):
		fail("choose",session)
		return
	var ready:=_wait(session,func(v):
		return int(v.state)==WorldState.STATE_IN_WORLD and bool(v.world_ready) and
			int(v.pending_world_ready_ack_epoch)==0 and
			int(v.last_world_ready_ack_epoch)==1 and not v.self_state.is_empty(),1800)
	if ready.is_empty():
		fail("world ready "+str(session.snapshot()),session)
		return
	var start:=Vector2i(roundi(float(ready.x)),roundi(float(ready.y)))
	if not session.stop_movement():
		fail("stop intent rejected",session)
		return
	# A later navigation intent must remain behind the stop barrier rather than
	# deleting it through the normal latest-navigation replacement rule.
	if not session.move_target(40,8):
		fail("follow-up move rejected",session)
		return
	var settled:=_wait(session,func(v):
		return int(v.movement_sequence)==2 and int(v.last_receipt_sequence)==2 and
			int(v.last_receipt_status)==0 and not bool(v.self_state.moving) and
			int(v.self_state.movement_action_sequence)==2 and
			abs(float(v.x)-40.0)<0.01 and abs(float(v.y)-8.0)<0.01,2200)
	if settled.is_empty():
		fail("ordered stop/move did not settle "+str(session.snapshot()),session)
		return
	var targets:=fake.move_targets()
	session.stop()
	if targets.size()!=2 or targets[0]!=start or targets[1]!=Vector2i(40,8):
		fail("stop barrier ordering targets="+str(targets)+" start="+str(start))
		return
	print("CLIENT_STOP_BARRIER_PASS stop_first=true authoritative_current_position=true waits_receipt_and_self_stop=true followup_navigation_preserved=true")
	quit(0)

func _wait(session,predicate:Callable,timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		if int(value.state)==WorldState.STATE_FAILED:
			return {}
		OS.delay_msec(2)
	return {}
