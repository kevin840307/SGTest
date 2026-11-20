extends SceneTree
const Session = preload("res://network/client_session.gd")
const WorldState = preload("res://network/world_state.gd")
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_TRADE_TLS_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 7 or not str(args[0]).is_valid_int() or str(args[2]) not in ["observer","actor"]:
		_fail("expected port cert role actor_a actor_b item_a item_b")
		return
	var role := str(args[2])
	var actor_a := int(args[3])
	var actor_b := int(args[4])
	var item_a := int(args[5])
	var item_b := int(args[6])
	var self_id := actor_b if role == "observer" else actor_a
	var peer_id := actor_a if role == "observer" else actor_b
	var own_item := item_b if role == "observer" else item_a
	var peer_item := item_a if role == "observer" else item_b
	var username := "trade_two" if role == "observer" else "trade_one"
	var cert := X509Certificate.new()
	if cert.load(str(args[1])) != OK:
		_fail("certificate load")
		return
	var session = Session.new()
	if session.connect_secure("127.0.0.1",int(args[0]),username,PASSWORD,"localhost",cert) != OK:
		_fail("connect",session)
		return
	var selecting := _wait(session,func(v): return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,7000)
	if selecting.is_empty() or selecting.characters.size()!=1 or int(selecting.characters[0].character_id)!=self_id:
		_fail("character list "+str(session.snapshot()),session)
		return
	if not session.choose_character(self_id):
		_fail("choose",session)
		return
	var ready := _wait(session,func(v): return int(v.state)==WorldState.STATE_IN_WORLD and bool(v.world_ready) and bool(v.inventory_ready) and v.inventory_items.has(own_item),7000)
	if ready.is_empty():
		_fail("world/inventory ready "+str(session.snapshot()),session)
		return
	var expected_own_level := 7 if role == "observer" else 5
	if int((ready.inventory_items[own_item] as Dictionary).get("enhancement_level",-1)) != expected_own_level:
		_fail("owned enhancement baseline "+str(ready.inventory_items[own_item]),session)
		return
	if role == "observer":
		print("CLIENT_TRADE_READY role=observer actor=%d item=%d" % [self_id,own_item])
		_run_observer(session,self_id,peer_id,own_item,peer_item)
	else:
		_run_actor(session,self_id,peer_id,own_item,peer_item)

func _run_observer(session, self_id: int, peer_id: int, own_item: int, peer_item: int) -> void:
	var invite := _wait(session,func(v): return int(v.trade_id)>0 and int(v.trade_state)==1 and int(v.trade_peer_actor_id)==peer_id,7000)
	if invite.is_empty() or not session.accept_trade():
		_fail("observer invite/accept "+str(session.snapshot()),session)
		return
	var active := _wait(session,func(v): return int(v.trade_state)==3 and int(v.trade_peer_actor_id)==peer_id,7000)
	if active.is_empty() or not session.trade_add_item(own_item):
		_fail("observer active/add "+str(session.snapshot()),session)
		return
	if _wait(session,func(v): return v.trade_my_items.has(own_item),7000).is_empty() or not session.trade_set_money(50):
		_fail("observer item/money "+str(session.snapshot()),session)
		return
	var terms := _wait(session,func(v): return int(v.trade_my_money)==50 and int(v.trade_other_money)==200 and v.trade_my_items.has(own_item) and v.trade_other_items.has(peer_item),7000)
	if terms.is_empty() or not session.trade_lock():
		_fail("observer terms/lock "+str(session.snapshot()),session)
		return
	var locked := _wait(session,func(v): return int(v.trade_state)==4 and bool(v.trade_my_locked) and bool(v.trade_other_locked),7000)
	if locked.is_empty() or not session.trade_final_confirm():
		_fail("observer dual lock/confirm "+str(session.snapshot()),session)
		return
	_finish(session,"observer",self_id,own_item,peer_item,4103,2,5)

func _run_actor(session, self_id: int, peer_id: int, own_item: int, peer_item: int) -> void:
	if not session.request_trade(peer_id):
		_fail("actor request",session)
		return
	var active := _wait(session,func(v): return int(v.trade_state)==3 and int(v.trade_peer_actor_id)==peer_id,7000)
	if active.is_empty() or not session.trade_add_item(own_item):
		_fail("actor active/add "+str(session.snapshot()),session)
		return
	if _wait(session,func(v): return v.trade_my_items.has(own_item),7000).is_empty() or not session.trade_set_money(200):
		_fail("actor item/money "+str(session.snapshot()),session)
		return
	var terms := _wait(session,func(v): return int(v.trade_my_money)==200 and int(v.trade_other_money)==50 and v.trade_my_items.has(own_item) and v.trade_other_items.has(peer_item),7000)
	if terms.is_empty() or not session.trade_lock():
		_fail("actor terms/lock "+str(session.snapshot()),session)
		return
	var locked := _wait(session,func(v): return int(v.trade_state)==4 and bool(v.trade_my_locked) and bool(v.trade_other_locked),7000)
	if locked.is_empty() or not session.trade_final_confirm():
		_fail("actor dual lock/confirm "+str(session.snapshot()),session)
		return
	_finish(session,"actor",self_id,own_item,peer_item,4104,1,7)

func _finish(session, role: String, self_id: int, own_item: int, peer_item: int, peer_definition: int, peer_quantity: int, peer_level: int) -> void:
	var final := _wait(session,func(v): return int(v.trade_id)==0 and not v.last_trade_terminal.is_empty() and int(v.last_trade_terminal.status)==0 and v.inventory_items.has(peer_item) and not v.inventory_items.has(own_item),9000)
	if final.is_empty():
		_fail(role+" terminal/inventory "+str(session.snapshot()),session)
		return
	var item: Dictionary = final.inventory_items[peer_item]
	if int(item.item_definition_id)!=peer_definition or int(item.quantity)!=peer_quantity or int(item.get("drop_id",0))<=0 or int(item.get("enhancement_level",-1))!=peer_level:
		_fail(role+" received item metadata/enhancement "+str(item),session)
		return
	session.stop()
	print("CLIENT_TRADE_PASS role=%s actor=%d received_uid=%d enhancement=%d wallet=%d dual_lock=true dual_confirm=true" % [role,self_id,peer_item,peer_level,int(final.last_trade_terminal.wallet_money)])
	quit(0)

func _wait(session, predicate: Callable, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value: Dictionary = session.snapshot()
		if predicate.call(value):
			return value
		if int(value.state)==WorldState.STATE_FAILED and not bool(value.get("reconnecting",false)):
			return {}
		OS.delay_msec(2)
	return {}
