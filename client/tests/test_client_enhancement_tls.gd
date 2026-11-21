extends SceneTree
const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_ENHANCEMENT_TLS_FAIL "+message)
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
	if session.connect_secure(
		"127.0.0.1",int(args[0]),USERNAME,PASSWORD,"localhost",cert) != OK:
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
			and bool(v.equipment_ready)
			and int(v.pending_world_ready_ack_epoch)==0
			and int(v.last_world_ready_ack_epoch)==int(v.map_epoch)
		),7000)
	if ready.is_empty() or (ready.equipment_items as Dictionary).size()!=1:
		_fail("world/equipment baseline "+str(session.snapshot()),session)
		return

	if not session.open_enhancement(99):
		_fail("open enhancement intent refused",session)
		return
	var opened := _wait(session,func(v):
		return (
			int(v.pending_enhancement_sequence)==0
			and int(v.last_enhancement_response_sequence)==1
			and int(v.active_enhancement_npc)==99
			and bool(v.enhancement_ready)
			and int(v.enhancement_wallet_money)==200
			and (v.enhancement_items as Dictionary).size()==1
		),7000)
	if opened.is_empty():
		_fail("enhancement open settlement "+str(session.snapshot()),session)
		return
	var uid := int((opened.enhancement_items as Dictionary).keys()[0])
	var preview: Dictionary = opened.enhancement_items[uid]
	if int(preview.location)!=2 or int(preview.current_level)!=0 or int(preview.target_level)!=1 or int(preview.success_ppm)!=1000000 or int(preview.money_cost)!=100 or not bool(preview.consume_money_on_failure):
		_fail("enhancement authoritative preview "+str(preview),session)
		return

	if not session.enhance_item(uid):
		_fail("enhancement item intent refused",session)
		return
	var enhanced := _wait(session,func(v):
		return (
			int(v.pending_enhancement_sequence)==0
			and int(v.last_enhancement_response_sequence)==2
			and int(v.enhancement_wallet_money)==100
			and not (v.enhancement_items as Dictionary).has(uid)
			and (v.equipment_items as Dictionary).has(uid)
			and int((v.equipment_items[uid] as Dictionary).get(
				"enhancement_level",-1))==1
		),7000)
	if enhanced.is_empty() or int(enhanced.last_enhancement_result.status)!=0 or not bool(enhanced.last_enhancement_result.succeeded) or int(enhanced.last_enhancement_result.item_uid)!=uid or int(enhanced.last_enhancement_result.previous_level)!=0 or int(enhanced.last_enhancement_result.target_level)!=1 or int(enhanced.last_enhancement_result.current_level)!=1 or int(enhanced.last_enhancement_result.money_cost)!=100 or int(enhanced.last_enhancement_result.money_charged)!=100 or int(enhanced.last_enhancement_result.wallet_money)!=100:
		_fail("enhancement authoritative result "+str(session.snapshot()),session)
		return

	session.stop()
	print("CLIENT_ENHANCEMENT_TLS_PASS npc=99 location=equipped level=0_to_1 success_ppm=1000000 cost=100 wallet=200_to_100 request=npc_uid_only local_formula=false")
	quit(0)

func _wait(session, predicate: Callable, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value: Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		if int(value.state)==WorldState.STATE_FAILED and not bool(
			value.get("reconnecting",false)):
			return {}
		OS.delay_msec(2)
	return {}
