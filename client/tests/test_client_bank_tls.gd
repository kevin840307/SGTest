extends SceneTree
const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_BANK_TLS_FAIL "+message)
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
			and bool(v.inventory_ready)
			and int(v.pending_world_ready_ack_epoch)==0
			and int(v.last_world_ready_ack_epoch)==int(v.map_epoch)
		),7000)
	if ready.is_empty():
		_fail("world/inventory baseline "+str(session.snapshot()),session)
		return

	if not session.open_bank(88):
		_fail("open bank intent refused",session)
		return
	var opened := _wait(session,func(v):
		return (
			int(v.pending_bank_sequence)==0
			and int(v.last_bank_response_sequence)==1
			and int(v.active_bank_npc)==88
			and int(v.bank_scope)==2
			and int(v.bank_wallet_money)==1000
			and int(v.bank_money)==0
		),7000)
	if opened.is_empty():
		_fail("bank open settlement "+str(session.snapshot()),session)
		return

	if not session.deposit_bank_money(400):
		_fail("bank deposit intent refused",session)
		return
	var deposited := _wait(session,func(v):
		return (
			int(v.pending_bank_sequence)==0
			and int(v.last_bank_response_sequence)==2
			and int(v.bank_wallet_money)==600
			and int(v.bank_money)==400
		),7000)
	if deposited.is_empty() or int(deposited.last_bank_result.status)!=0 or int(deposited.last_bank_result.kind)!=1 or int(deposited.last_bank_result.amount)!=400:
		_fail("bank deposit receipt "+str(session.snapshot()),session)
		return

	if not session.withdraw_bank_money(400):
		_fail("bank withdraw intent refused",session)
		return
	var withdrawn := _wait(session,func(v):
		return (
			int(v.pending_bank_sequence)==0
			and int(v.last_bank_response_sequence)==3
			and int(v.bank_wallet_money)==1000
			and int(v.bank_money)==0
		),7000)
	if withdrawn.is_empty() or int(withdrawn.last_bank_result.status)!=0 or int(withdrawn.last_bank_result.kind)!=2 or int(withdrawn.last_bank_result.amount)!=400:
		_fail("bank withdraw receipt "+str(session.snapshot()),session)
		return

	session.stop()
	print("CLIENT_BANK_TLS_PASS npc=88 scope=account wallet=1000_to_600_to_1000 bank=0_to_400_to_0 open=true deposit=true withdraw=true local_balance_after_receipt=true request_has_no_actor_or_scope_or_result_balance=true")
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
