extends SceneTree
const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_AUTO_APPROACH_TLS_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()!=2 or not str(args[0]).is_valid_int():
		_fail("expected port/certificate")
		return
	var cert := X509Certificate.new()
	if cert.load(str(args[1])) != OK:
		_fail("certificate load")
		return
	var session = Session.new()
	if session.connect_secure("127.0.0.1",int(args[0]),USERNAME,PASSWORD,"localhost",cert) != OK:
		_fail("connect",session)
		return
	var selecting := _wait(session,func(v):
		return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,5000)
	if selecting.is_empty() or selecting.characters.size()!=1:
		_fail("character list "+str(session.snapshot()),session)
		return
	if not session.choose_character(int(selecting.characters[0].character_id)):
		_fail("choose",session)
		return

	var ready := _wait(session,func(v):
		return (
			int(v.state)==WorldState.STATE_IN_WORLD
			and bool(v.world_ready)
			and int(v.pending_world_ready_ack_epoch)==0
			and int(v.last_world_ready_ack_epoch)==int(v.map_epoch)
			and v.monsters.size()==1
		),7000)
	if ready.is_empty():
		_fail("monster baseline "+str(session.snapshot()),session)
		return
	var target := int((ready.monsters as Dictionary).keys()[0])
	if not session.attack_monster(target):
		_fail("initial attack intent refused",session)
		return

	var settled := _wait(session,func(v):
		return (
			int(v.attack_sequence)==2
			and int(v.last_attack_result_sequence)==2
			and int(v.last_attack_result_status)==0
			and int(v.movement_sequence)==1
			and int(v.last_receipt_sequence)==1
			and int(v.last_receipt_status)==0
			and not v.self_state.is_empty()
			and not bool(v.self_state.moving)
			and v.monsters.has(target)
			and int(v.monsters[target].current_hp)==40
		),12000)
	if settled.is_empty():
		_fail("server-authoritative auto approach did not settle "+str(session.snapshot()),session)
		return
	if abs(float(settled.x)-40.0)>0.01 or abs(float(settled.y)-8.0)>0.01:
		_fail("authoritative final position not at approached target "+str(settled),session)
		return

	session.stop()
	print("CLIENT_AUTO_APPROACH_TLS_PASS target=%d first_attack_out_of_range=true server_astar_move=true final_x=40 final_y=8 second_attack_applied=true monster_hp=40 client_range_guess=false" % target)
	quit(0)

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
