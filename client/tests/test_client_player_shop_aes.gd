extends SceneTree

const Session=preload("res://session/client_session.gd")
const WorldState=preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")
const PASSWORD:="Fixture-Password-123!"

func _fail(message:String,session=null)->void:
	if session!=null:
		session.stop()
	push_error("CLIENT_PLAYER_SHOP_AES_FAIL "+message)
	quit(1)

func _initialize()->void:
	var args:=OS.get_cmdline_user_args()
	if (
		args.size()!=6 or
		not str(args[0]).is_valid_int() or
		str(args[1]) not in ["seller","buyer","relog"]
	):
		_fail("expected port role seller|buyer|relog seller buyer item_uid drop_id")
		return
	var role:=str(args[1])
	var seller_id:=int(args[2])
	var buyer_id:=int(args[3])
	var item_uid:=int(args[4])
	var drop_id:=int(args[5])
	var self_id:=seller_id if role in ["seller","relog"] else buyer_id
	var username:="pshop_one" if role in ["seller","relog"] else "pshop_two"
	var session=Session.new()
	if session.connect_secure(
		"127.0.0.1",int(args[0]),username,PASSWORD)!=OK:
		_fail("connect",session)
		return
	var selecting:=_wait(session,func(v):
		return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,7000)
	if (
		selecting.is_empty() or selecting.characters.size()!=1 or
		int(selecting.characters[0].character_id)!=self_id
	):
		_fail("character list "+str(session.snapshot()),session)
		return
	if not FixtureLogin.provision_and_choose(session, self_id):
		_fail("choose",session)
		return
	var ready:=_wait(session,func(v):
		return (
			int(v.state)==WorldState.STATE_IN_WORLD and bool(v.world_ready) and
			bool(v.inventory_ready)
		),7000)
	if ready.is_empty():
		_fail("world/inventory ready "+str(session.snapshot()),session)
		return
	if role=="seller":
		if not ready.inventory_items.has(item_uid):
			_fail("seller missing seed UID "+str(ready.inventory_items),session)
			return
		if (
			int((ready.inventory_items[item_uid] as Dictionary).get("enhancement_level",-1))!=6 or
			int((ready.inventory_items[item_uid] as Dictionary).get("quantity",0))!=3 or
			_inventory_definition_total(ready,6401)!=3
		):
			_fail("seller seed baseline "+str(ready.inventory_items),session)
			return
		_run_seller(session,seller_id,buyer_id,item_uid)
	elif role=="buyer":
		_run_buyer(session,seller_id,buyer_id,item_uid,drop_id)
	else:
		_run_relog(session,seller_id,item_uid)

func _inventory_definition_total(snapshot:Dictionary,definition_id:int)->int:
	var total:=0
	for raw in (snapshot.get("inventory_items",{}) as Dictionary).values():
		var item:Dictionary=raw
		if int(item.get("item_definition_id",0))==definition_id:
			total+=int(item.get("quantity",0))
	return total

func _run_relog(session,seller_id:int,item_uid:int)->void:
	var current:Dictionary=session.snapshot()
	var presentations:Dictionary=current.get("player_shop_presentations",{})
	if (
		int(current.get("player_money",-1))!=400 or
		_inventory_definition_total(current,6401)!=1 or
		(current.get("inventory_items",{}) as Dictionary).has(item_uid) or
		not (current.get("player_shop_own_listings",{}) as Dictionary).is_empty() or
		presentations.has(seller_id)
	):
		_fail("seller relog persistence "+str(current),session)
		return
	session.stop()
	print("CLIENT_PLAYER_SHOP_PASS role=relog actor=%d wallet=400 remainder=1 no_stall=true" % seller_id)
	quit(0)

func _run_seller(session,seller_id:int,buyer_id:int,item_uid:int)->void:
	const SHOP_NAME:="測試小商店"
	if not session.set_player_shop_name(SHOP_NAME):
		_fail("seller name submit "+str(session.snapshot()),session)
		return
	var named:=_wait(session,func(v):
		var presentations:Dictionary=v.get("player_shop_presentations",{})
		return (
			int(v.get("reconnect_count",0))>0 or
			(
				int(v.last_player_shop_result_sequence)>=1 and
				int(v.last_player_shop_result_status)==0 and
				presentations.has(seller_id) and
				str((presentations[seller_id] as Dictionary).get("shop_name",""))==SHOP_NAME
			)
		),7000)
	if named.is_empty() or int(named.get("reconnect_count",0))>0:
		_fail("seller name receipt "+str(session.snapshot()),session)
		return
	if not session.open_player_shop(item_uid,300,2):
		_fail("seller first open submit "+str(session.snapshot()),session)
		return
	var first_open:=_wait(session,func(v):
		return (
			int(v.last_player_shop_result_sequence)>=2 and
			int(v.last_player_shop_result_status)==0 and
			int(v.player_shop_own_listing.get("item_uid",0))==item_uid and
			bool(v.inventory_ready) and not v.inventory_items.has(item_uid)
		),7000)
	if first_open.is_empty():
		_fail("seller first open receipt "+str(session.snapshot()),session)
		return
	var first_listing:=int(first_open.player_shop_own_listing.listing_id)
	if (
		first_listing<=0 or int(first_open.player_shop_own_listing.get("quantity",0))!=2 or
		_inventory_definition_total(first_open,6401)!=1
	):
		_fail("seller first listing/remainder "+str(first_open),session)
		return
	if not session.close_player_shop(first_listing):
		_fail("seller cancel submit "+str(session.snapshot()),session)
		return
	var cancelled:=_wait(session,func(v):
		return (
			int(v.last_player_shop_result_sequence)>=3 and
			int(v.last_player_shop_result_status) in [0,1] and
			(v.player_shop_own_listings as Dictionary).is_empty() and
			bool(v.inventory_ready) and v.inventory_items.has(item_uid)
		),7000)
	if cancelled.is_empty() or _inventory_definition_total(cancelled,6401)!=3:
		_fail("seller cancel restore "+str(session.snapshot()),session)
		return
	if not session.open_player_shop(item_uid,300,2):
		_fail("seller reopen submit "+str(session.snapshot()),session)
		return
	var opened:=_wait(session,func(v):
		return (
			int(v.last_player_shop_result_sequence)>=4 and
			int(v.last_player_shop_result_status)==0 and
			int(v.player_shop_own_listing.get("item_uid",0))==item_uid and
			bool(v.inventory_ready) and not v.inventory_items.has(item_uid)
		),7000)
	if opened.is_empty():
		_fail("seller reopen receipt "+str(session.snapshot()),session)
		return
	var listing:=int(opened.player_shop_own_listing.listing_id)
	if (
		listing<=first_listing or int(opened.player_shop_wallet)!=100 or
		int(opened.player_shop_own_listing.get("quantity",0))!=2 or
		int(opened.player_shop_own_listing.get("enhancement_level",-1))!=6 or
		_inventory_definition_total(opened,6401)!=1
	):
		_fail("seller reopen state "+str(opened),session)
		return
	print("CLIENT_PLAYER_SHOP_READY role=seller actor=%d listing=%d item=%d seed_quantity=3 listing_quantity=2 seller_remainder=1 name=%s enhancement=6 cancel_reopen=true" %
		[seller_id,listing,item_uid,SHOP_NAME])
	var sold:=_wait(session,func(v):
		return (
			int(v.last_player_shop_sold_listing_id)==listing and
			not v.inventory_items.has(item_uid) and
			int(v.player_shop_wallet)==400 and
			_inventory_definition_total(v,6401)==1 and
			int(v.last_player_shop_sold.get("buyer_actor_id",0))==buyer_id
		),9000)
	if sold.is_empty():
		_fail("seller sold convergence "+str(session.snapshot()),session)
		return
	if not session.close_player_shop_all():
		_fail("seller close stall submit "+str(session.snapshot()),session)
		return
	var closed:=_wait(session,func(v):
		return (
			int(v.last_player_shop_result_sequence)>=5 and
			int(v.last_player_shop_result_status) in [0,1] and
			bool(v.inventory_ready) and
			_inventory_definition_total(v,6401)==1
		),7000)
	if closed.is_empty():
		_fail("seller close stall receipt "+str(session.snapshot()),session)
		return
	session.stop()
	print("CLIENT_PLAYER_SHOP_PASS role=seller actor=%d listing=%d uid_removed=true wallet=400 buyer=%d partial_stack=3_to_2_plus_1 close_all=true" %
		[seller_id,listing,buyer_id])
	quit(0)

func _run_buyer(
		session,seller_id:int,buyer_id:int,item_uid:int,drop_id:int)->void:
	var visible:=_wait(session,func(v):
		var presentations:Dictionary=v.get("player_shop_presentations",{})
		return (
			(v.entities as Dictionary).has(seller_id) and
			presentations.has(seller_id) and
			str((presentations[seller_id] as Dictionary).get("shop_name",""))=="測試小商店"
		),7000)
	if visible.is_empty():
		_fail("buyer never saw seller shop sign "+str(session.snapshot()),session)
		return
	if not session.inspect_player_shop(seller_id):
		_fail("buyer inspect submit",session)
		return
	var inspected:=_wait(session,func(v):
		return (
			int(v.last_player_shop_result_sequence)>=1 and
			int(v.last_player_shop_result_status)==0 and
			int(v.player_shop_inspected.get("seller_actor_id",0))==seller_id and
			int(v.player_shop_inspected.get("item_uid",0))==item_uid
		),7000)
	if inspected.is_empty():
		_fail("buyer inspect "+str(session.snapshot()),session)
		return
	var listing:=int(inspected.player_shop_inspected.listing_id)
	if (
		listing<=0 or int(inspected.player_shop_inspected.price)!=300 or
		int(inspected.player_shop_inspected.quantity)!=2 or
		int(inspected.player_shop_inspected.durability_raw)!=80 or
		int(inspected.player_shop_inspected.enhancement_level)!=6 or
		int(inspected.player_shop_wallet)!=1000 or
		inspected.inventory_items.has(item_uid)
	):
		_fail("buyer inspect metadata "+str(inspected.player_shop_inspected),session)
		return
	if not session.buy_player_shop(seller_id,listing):
		_fail("buyer purchase submit",session)
		return
	var purchased:=_wait(session,func(v):
		return (
			int(v.last_player_shop_result_sequence)>=2 and
			int(v.last_player_shop_result_status)==0 and
			v.inventory_items.has(item_uid) and
			int(v.player_shop_wallet)==700
		),9000)
	if purchased.is_empty():
		_fail("buyer purchase result "+str(session.snapshot()),session)
		return
	var item:Dictionary=purchased.inventory_items[item_uid]
	if (
		int(item.item_definition_id)!=6401 or int(item.quantity)!=2 or
		int(item.drop_id)!=drop_id or int(item.durability_raw)!=80 or
		int(item.get("enhancement_level",-1))!=6 or
		not (purchased.player_shop_inspected as Dictionary).is_empty()
	):
		_fail("buyer UID provenance/durability/enhancement "+str(item),session)
		return
	var sign_closed:=_wait(session,func(v):
		return not (v.get("player_shop_presentations",{}) as Dictionary).has(seller_id),7000)
	if sign_closed.is_empty():
		_fail("buyer shop sign did not close "+str(session.snapshot()),session)
		return
	session.stop()
	print("CLIENT_PLAYER_SHOP_PASS role=buyer actor=%d listing=%d uid=%d quantity=2 drop=%d durability=80 enhancement=6 wallet=700 sign_open_close=true" %
		[buyer_id,listing,item_uid,drop_id])
	quit(0)

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
