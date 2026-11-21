extends SceneTree

const Session=preload("res://session/client_session.gd")
const WorldState=preload("res://world/world_state.gd")
const PASSWORD:="Fixture-Password-123!"

func _fail(message:String,session=null)->void:
	if session!=null:
		session.stop()
	push_error("CLIENT_PLAYER_SHOP_TLS_FAIL "+message)
	quit(1)

func _initialize()->void:
	var args:=OS.get_cmdline_user_args()
	if (
		args.size()!=7 or
		not str(args[0]).is_valid_int() or
		str(args[2]) not in ["seller","buyer"]
	):
		_fail("expected port cert role seller buyer item_uid drop_id")
		return
	var role:=str(args[2])
	var seller_id:=int(args[3])
	var buyer_id:=int(args[4])
	var item_uid:=int(args[5])
	var drop_id:=int(args[6])
	var self_id:=seller_id if role=="seller" else buyer_id
	var username:="pshop_one" if role=="seller" else "pshop_two"
	var cert:=X509Certificate.new()
	if cert.load(str(args[1]))!=OK:
		_fail("certificate load")
		return
	var session=Session.new()
	if session.connect_secure(
		"127.0.0.1",int(args[0]),username,PASSWORD,"localhost",cert)!=OK:
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
	if not session.choose_character(self_id):
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
		if int((ready.inventory_items[item_uid] as Dictionary).get("enhancement_level",-1))!=6:
			_fail("seller enhancement baseline "+str(ready.inventory_items[item_uid]),session)
			return
		_run_seller(session,seller_id,buyer_id,item_uid)
	else:
		_run_buyer(session,seller_id,buyer_id,item_uid,drop_id)

func _run_seller(session,seller_id:int,buyer_id:int,item_uid:int)->void:
	if not session.open_player_shop(item_uid,300):
		_fail("seller open submit "+str(session.snapshot()),session)
		return
	var opened:=_wait(session,func(v):
		return (
			int(v.get("reconnect_count",0))>0 or
			(
				int(v.last_player_shop_result_sequence)>=1 and
				int(v.last_player_shop_result_status)==0 and
				int(v.player_shop_own_listing.get("item_uid",0))==item_uid
			)
		),7000)
	if not opened.is_empty() and int(opened.get("reconnect_count",0))>0:
		_fail("seller connection restarted before open receipt "+str(opened),session)
		return
	if opened.is_empty():
		_fail("seller open receipt "+str(session.snapshot()),session)
		return
	var listing:=int(opened.player_shop_own_listing.listing_id)
	if (
		listing<=0 or int(opened.player_shop_wallet)!=100 or
		int(opened.player_shop_own_listing.get("enhancement_level",-1))!=6 or
		not opened.inventory_items.has(item_uid)
	):
		_fail("seller open state "+str(opened),session)
		return
	print("CLIENT_PLAYER_SHOP_READY role=seller actor=%d listing=%d item=%d enhancement=6" %
		[seller_id,listing,item_uid])
	var sold:=_wait(session,func(v):
		return (
			int(v.last_player_shop_sold_listing_id)==listing and
			not v.inventory_items.has(item_uid) and
			int(v.player_shop_wallet)==400 and
			int(v.last_player_shop_sold.get("buyer_actor_id",0))==buyer_id
		),9000)
	if sold.is_empty():
		_fail("seller sold convergence "+str(session.snapshot()),session)
		return
	session.stop()
	print("CLIENT_PLAYER_SHOP_PASS role=seller actor=%d listing=%d uid_removed=true wallet=400 buyer=%d" %
		[seller_id,listing,buyer_id])
	quit(0)

func _run_buyer(
		session,seller_id:int,buyer_id:int,item_uid:int,drop_id:int)->void:
	var visible:=_wait(session,func(v):
		return (v.entities as Dictionary).has(seller_id),7000)
	if visible.is_empty():
		_fail("buyer never saw seller "+str(session.snapshot()),session)
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
	session.stop()
	print("CLIENT_PLAYER_SHOP_PASS role=buyer actor=%d listing=%d uid=%d quantity=2 drop=%d durability=80 enhancement=6 wallet=700" %
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
