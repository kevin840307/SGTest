extends SceneTree
## Formal main.tscn gate for the original-style 092 player shop flow.
## Two real ClientSessions drive seller/buyer Classic controls against the formal AES Host.
const Main=preload("res://app/main.gd")
const MainScene=preload("res://app/main.tscn")
const WorldState=preload("res://world/world_state.gd")

const PASSWORD:="Fixture-Password-123!"
const PROOF:="2468"
const DEFINITION:=1001
const SHOP_NAME:="正式測試小商店"

var _app:Main
var _role:=""
var _port:=0

func _initialize()->void:
	_run.call_deferred()

func _fail(message:String)->void:
	if _app!=null:
		_app.session_panel.disconnect_session()
	push_error("ONLINE_PLAYER_SHOP_MAIN_GATE_FAIL role=%s %s"%[_role,message])
	quit(1)

func _run()->void:
	root.size=Vector2i(1280,720)
	_role=_arg("--role")
	_port=_arg_int("--port")
	if _role not in ["seller","buyer"] or _port<=0:
		_fail("invalid args")
		return
	_app=MainScene.instantiate() as Main
	root.add_child(_app)
	current_scene=_app
	await process_frame
	_app.session_panel.server_host="127.0.0.1"
	_app.session_panel.server_port=_port
	if not await _login_and_choose():
		return
	var ready:=await _wait_snapshot(func(v):
		return _world_ready(v) and int(v.get("player_money",-1))==(100 if _role=="seller" else 1000)
	,15000)
	if ready.is_empty():
		_fail("initial world/inventory/wallet "+str(_app._last_snapshot))
		return
	if not await _wait_main_surface(8000):
		_fail("formal main surface not visible")
		return
	if _role=="seller":
		await _seller_flow()
	else:
		await _buyer_flow()

func _seller_flow()->void:
	var peer:=await _wait_snapshot(func(v):
		return (v.get("entities",{}) as Dictionary).size()>=1
	,15000)
	if peer.is_empty():
		_fail("buyer peer never entered AOI")
		return
	var items:Dictionary=_app._last_snapshot.get("inventory_items",{})
	if _definition_total(items,DEFINITION)!=3:
		_fail("seller source-backed seed quantity "+str(items))
		return
	var uid:=_definition_uid(items,DEFINITION)
	if uid<=0:
		_fail("seller source-backed UID missing")
		return
	var shell=_app.classic_world
	var command:=shell.secondary_menu.get_node_or_null("PlayerShopCommand") as TextureButton
	if command==null:
		_fail("021 PlayerShopCommand missing")
		return

	# 021 -> 092 -> original name dialog.
	command.pressed.emit()
	await process_frame
	if not shell.player_shop_window.visible or not shell._player_shop_name_dialog.visible:
		_fail("092/name dialog did not open from right-side command")
		return
	if shell.player_shop_window.size!=Vector2(770,506):
		_fail("092 source rect drift "+str(shell.player_shop_window.size))
		return
	shell._player_shop_name_input.text=SHOP_NAME
	shell._player_shop_name_input.emit_signal("text_changed",SHOP_NAME)
	await process_frame
	if shell._player_shop_name_confirm==null or shell._player_shop_name_confirm.disabled:
		_fail("shop name confirm not operable")
		return
	shell._player_shop_name_confirm.pressed.emit()
	var named:=await _wait_snapshot(func(v):
		var own:=int(v.get("actor_id",0))
		var presentations:Dictionary=v.get("player_shop_presentations",{})
		return (
			int(v.get("pending_player_shop_sequence",0))==0 and
			presentations.has(own) and
			str((presentations[own] as Dictionary).get("shop_name",""))==SHOP_NAME
		)
	,10000)
	if named.is_empty():
		_fail("server-authoritative shop name/presentation "+str(_app._last_snapshot))
		return
	await process_frame
	if shell._player_shop_name_dialog.visible or shell._player_shop_inventory_rows.get_child_count()<1:
		_fail("092 inventory side did not become operable after naming")
		return

	# Select the real inventory stack, enter unit price and listing quantity.
	var inv_row:=_first_button(shell._player_shop_inventory_rows)
	if inv_row==null:
		_fail("092 source item row missing")
		return
	inv_row.pressed.emit()
	await process_frame
	if not shell._player_shop_sell_price_dialog.visible:
		_fail("092_PShop_Sell_Item_01 price dialog missing")
		return
	shell._player_shop_sell_price_input.text="300"
	shell._player_shop_sell_price_input.emit_signal("text_changed","300")
	await process_frame
	var price_ok:=shell._player_shop_sell_price_dialog.get_node_or_null("PlayerShopSellPriceOk") as TextureButton
	if price_ok==null or price_ok.disabled:
		_fail("price confirm not operable")
		return
	price_ok.pressed.emit()
	await process_frame
	if not shell._player_shop_sell_quantity_dialog.visible:
		_fail("092_PShop_Sell_Item_02 quantity dialog missing")
		return
	shell._player_shop_sell_quantity_input.text="2"
	shell._player_shop_sell_quantity_input.emit_signal("text_changed","2")
	await process_frame
	var quantity_ok:=shell._player_shop_sell_quantity_dialog.get_node_or_null("PlayerShopSellQuantityOk") as TextureButton
	if quantity_ok==null or quantity_ok.disabled:
		_fail("quantity confirm not operable")
		return
	quantity_ok.pressed.emit()
	var listed:=await _wait_snapshot(func(v):
		var own_listings:Dictionary=v.get("player_shop_own_listings",{})
		var inventory:Dictionary=v.get("inventory_items",{})
		if own_listings.size()!=1:
			return false
		var listing:Dictionary=own_listings.values()[0]
		return (
			int(v.get("pending_player_shop_sequence",0))==0 and
			int(listing.get("item_definition_id",0))==DEFINITION and
			int(listing.get("quantity",0))==2 and
			int(listing.get("price",0))==300 and
			_definition_total(inventory,DEFINITION)==1
		)
	,12000)
	if listed.is_empty():
		_fail("authoritative listing/partial-stack split "+str(_app._last_snapshot))
		return
	var own_presentation:Dictionary=(listed.get("player_shop_presentations",{}) as Dictionary).get(int(listed.get("actor_id",0)),{})
	print("ONLINE_PLAYER_SHOP_MAIN_GATE_SELLER_LIST_PASS formal_092=true name=true price=300 quantity=2 remainder=1 source_item=1001 expected_hex=%s own_hex=%s" % [
		SHOP_NAME.to_utf8_buffer().hex_encode(),
		str(own_presentation.get("shop_name","")).to_utf8_buffer().hex_encode()])

	var sold:=await _wait_snapshot(func(v):
		return (
			int(v.get("player_money",-1))==400 and
			(v.get("player_shop_own_listings",{}) as Dictionary).is_empty() and
			_definition_total(v.get("inventory_items",{}),DEFINITION)==1 and
			int(v.get("last_player_shop_sold_listing_id",0))>0
		)
	,30000)
	if sold.is_empty():
		_fail("seller did not converge after buyer purchase "+str(_app._last_snapshot))
		return
	print("ONLINE_PLAYER_SHOP_MAIN_GATE_SELLER_SOLD_PASS wallet=100_to_400 remainder=1 push=true")

	# Re-open with the unsold remainder, then exercise the player-facing close-all confirmation.
	if shell.player_shop_window.visible:
		command.pressed.emit()
		await process_frame
	command.pressed.emit()
	await process_frame
	if not shell.player_shop_window.visible:
		_fail("092 did not reopen after sold-out stall")
		return
	if shell._player_shop_name_dialog.visible:
		shell._player_shop_name_input.text=SHOP_NAME
		shell._player_shop_name_input.emit_signal("text_changed",SHOP_NAME)
		await process_frame
		shell._player_shop_name_confirm.pressed.emit()
		var renamed:=await _wait_snapshot(func(v):
			var own:=int(v.get("actor_id",0))
			return (
				int(v.get("pending_player_shop_sequence",0))==0 and
				(v.get("player_shop_presentations",{}) as Dictionary).has(own)
			)
		,10000)
		if renamed.is_empty():
			_fail("stall reopen name failed")
			return
	await process_frame
	var remainder_uid:=_definition_uid(_app._last_snapshot.get("inventory_items",{}),DEFINITION)
	if remainder_uid<=0 or not await _open_current_sell_price_dialog(shell,remainder_uid,5000):
		_fail("remainder row did not open current sell dialog uid=%d" % remainder_uid)
		return
	shell._player_shop_sell_price_input.text="333"
	shell._player_shop_sell_price_input.emit_signal("text_changed","333")
	await process_frame
	price_ok=shell._player_shop_sell_price_dialog.get_node_or_null("PlayerShopSellPriceOk") as TextureButton
	if price_ok==null or price_ok.disabled:
		_fail("reopen price confirm unavailable")
		return
	price_ok.pressed.emit()
	await process_frame
	shell._player_shop_sell_quantity_input.text="1"
	shell._player_shop_sell_quantity_input.emit_signal("text_changed","1")
	await process_frame
	quantity_ok=shell._player_shop_sell_quantity_dialog.get_node_or_null("PlayerShopSellQuantityOk") as TextureButton
	if quantity_ok==null or quantity_ok.disabled:
		_fail("reopen quantity confirm unavailable sell_uid=%d price_value=%d qty_text=%s inventory=%s disabled=%s pending=%d" % [
			int(shell._player_shop_sell_uid),
			int(shell._player_shop_sell_price_value),
			str(shell._player_shop_sell_quantity_input.text),
			str(_app._last_snapshot.get("inventory_items",{})),
			str(quantity_ok.disabled if quantity_ok!=null else true),
			int(_app._last_snapshot.get("pending_player_shop_sequence",0))])
		return
	quantity_ok.pressed.emit()
	var remainder_listed:=await _wait_snapshot(func(v):
		var listings:Dictionary=v.get("player_shop_own_listings",{})
		return listings.size()==1 and _definition_total(v.get("inventory_items",{}),DEFINITION)==0
	,10000)
	if remainder_listed.is_empty():
		_fail("remainder listing did not commit")
		return
	await process_frame
	var close_all:=shell.player_shop_window.get_node_or_null("PlayerShopCloseAll") as TextureButton
	if close_all==null or close_all.disabled:
		_fail("092 close-all control unavailable")
		return
	close_all.pressed.emit()
	await process_frame
	if not shell._player_shop_close_question.visible:
		_fail("close-all confirmation did not open")
		return
	var yes:=shell._player_shop_close_question.get_node_or_null("QuestionYes") as TextureButton
	if yes==null:
		_fail("close-all confirm button missing")
		return
	yes.pressed.emit()
	var closed:=await _wait_snapshot(func(v):
		var own:=int(v.get("actor_id",0))
		return (
			int(v.get("pending_player_shop_sequence",0))==0 and
			(v.get("player_shop_own_listings",{}) as Dictionary).is_empty() and
			not (v.get("player_shop_presentations",{}) as Dictionary).has(own) and
			_definition_total(v.get("inventory_items",{}),DEFINITION)==1
		)
	,12000)
	if closed.is_empty():
		_fail("close-all did not restore unsold item/remove sign "+str(_app._last_snapshot))
		return
	print("ONLINE_PLAYER_SHOP_MAIN_GATE_CLOSE_ALL_PASS confirm=true unsold_restored=true sign_removed=true")

	# SQLite persistence after a genuine disconnect/relogin.
	_app.session_panel.disconnect_session()
	await process_frame
	if not await _login_and_choose():
		return
	var relog:=await _wait_snapshot(func(v):
		return (
			_world_ready(v) and int(v.get("player_money",-1))==400 and
			_definition_total(v.get("inventory_items",{}),DEFINITION)==1 and
			(v.get("player_shop_own_listings",{}) as Dictionary).is_empty()
		)
	,15000)
	if relog.is_empty():
		_fail("seller relog persistence "+str(_app._last_snapshot))
		return
	print("ONLINE_PLAYER_SHOP_MAIN_GATE_SELLER_PASS formal_main=true sqlite_relog=true wallet=400 remainder=1 native_visible=",
		not DisplayServer.get_name().contains("headless"))
	_app.session_panel.disconnect_session()
	quit(0)

func _buyer_flow()->void:
	var sign_ready:=await _wait_snapshot(func(v):
		var own:=int(v.get("actor_id",0))
		var presentations:Dictionary=v.get("player_shop_presentations",{})
		for raw in presentations.keys():
			var actor:=int(raw)
			if actor==own:
				continue
			if (
				str((presentations[raw] as Dictionary).get("shop_name",""))==SHOP_NAME and
				(v.get("entities",{}) as Dictionary).has(actor)
			):
				return true
		return false
	,25000)
	var seller_actor:=0
	if not sign_ready.is_empty():
		var own:=int(sign_ready.get("actor_id",0))
		var presentations:Dictionary=sign_ready.get("player_shop_presentations",{})
		for raw in presentations.keys():
			var actor:=int(raw)
			if actor!=own and str((presentations[raw] as Dictionary).get("shop_name",""))==SHOP_NAME:
				seller_actor=actor
				break
	if sign_ready.is_empty() or seller_actor<=0:
		var snap:Dictionary=_app._last_snapshot
		var got_name:=""
		var all_presentations:Dictionary=snap.get("player_shop_presentations",{})
		for raw in all_presentations.keys():
			if int(raw)!=int(snap.get("actor_id",0)):
				got_name=str((all_presentations[raw] as Dictionary).get("shop_name",""))
				break
		_fail(
			"buyer never saw seller world shop presentation expected_hex=%s got_hex=%s entities=%s presentations=%s resync=%s reason=%s protocol=%s stale_shop=%s stale_aoi=%s last_tick=%s"
			% [
				SHOP_NAME.to_utf8_buffer().hex_encode(),
				got_name.to_utf8_buffer().hex_encode(),
				str((snap.get("entities",{}) as Dictionary).keys()),
				str(snap.get("player_shop_presentations",{})),
				str(snap.get("resync_pending",false)),
				str(snap.get("resync_reason","")),
				str(snap.get("protocol_error","")),
				str(snap.get("stale_player_shop_presentation_frames",0)),
				str(snap.get("stale_aoi_events",0)),
				str(snap.get("last_aoi_tick",-1))
			])
		return
	var shell=_app.classic_world
	var sign_seen:=false
	var sign_deadline:=Time.get_ticks_msec()+8000
	while Time.get_ticks_msec()<sign_deadline:
		var sign=shell._player_shop_signs.get(seller_actor,null)
		if sign!=null and is_instance_valid(sign) and sign.visible:
			var hit:=sign.get_node_or_null("Hit") as TextureButton
			if hit!=null:
				hit.pressed.emit()
				sign_seen=true
				break
		await process_frame
	if not sign_seen:
		_fail("world shop sign was not rendered/clickable")
		return

	# Name publication can beat the listing commit by a few frames; keep using the
	# visible world sign as a player would until the authoritative browse snapshot arrives.
	var inspected:Dictionary={}
	var browse_deadline:=Time.get_ticks_msec()+12000
	while Time.get_ticks_msec()<browse_deadline:
		inspected=_app._last_snapshot
		var listings:Dictionary=inspected.get("player_shop_inspected_listings",{})
		if listings.size()==1:
			break
		if int(inspected.get("pending_player_shop_sequence",0))==0:
			var sign=shell._player_shop_signs.get(seller_actor,null)
			if sign!=null and is_instance_valid(sign):
				var hit:=sign.get_node_or_null("Hit") as TextureButton
				if hit!=null:
					hit.pressed.emit()
		await create_timer(0.15).timeout
	inspected=_app._last_snapshot
	var listings:Dictionary=inspected.get("player_shop_inspected_listings",{})
	if listings.size()!=1:
		_fail("world-sign browse did not load authoritative listing "+str(inspected))
		return
	var listing:Dictionary=listings.values()[0]
	if (
		int(listing.get("item_definition_id",0))!=DEFINITION or
		int(listing.get("quantity",0))!=2 or int(listing.get("price",0))!=300 or
		int(inspected.get("player_money",-1))!=1000
	):
		_fail("buyer listing metadata "+str(listing))
		return
	await process_frame
	if not shell.player_shop_window.visible or shell._player_shop_rows.get_child_count()<1:
		_fail("buyer 092 browse rows not visible")
		return
	var sale_row:=_first_button(shell._player_shop_rows)
	if sale_row==null:
		_fail("buyer sale row not operable")
		return
	sale_row.pressed.emit()
	await process_frame
	if (
		not shell._player_shop_buy_dialog.visible or
		shell._player_shop_buy_quantity.text!="2" or shell._player_shop_buy_total.text!="300"
	):
		_fail("092_PShop_Buy_Item_02 confirmation mismatch")
		return
	var buy_ok:=shell._player_shop_buy_dialog.get_node_or_null("PlayerShopBuyOk") as TextureButton
	if buy_ok==null or buy_ok.disabled:
		_fail("buyer confirm not operable")
		return
	buy_ok.pressed.emit()
	var purchased:=await _wait_snapshot(func(v):
		return (
			int(v.get("pending_player_shop_sequence",0))==0 and
			int(v.get("player_money",-1))==700 and
			_definition_total(v.get("inventory_items",{}),DEFINITION)==2 and
			(v.get("player_shop_inspected_listings",{}) as Dictionary).is_empty()
		)
	,15000)
	if purchased.is_empty():
		_fail("authoritative purchase/wallet/inventory "+str(_app._last_snapshot))
		return
	var bought_uid:=_definition_uid(purchased.get("inventory_items",{}),DEFINITION)
	if bought_uid<=0:
		_fail("purchased UID missing")
		return
	var sign_gone:=await _wait_snapshot(func(v):
		return not (v.get("player_shop_presentations",{}) as Dictionary).has(seller_actor)
	,10000)
	if sign_gone.is_empty():
		_fail("seller world sign did not close after sold-out listing")
		return
	print("ONLINE_PLAYER_SHOP_MAIN_GATE_BUY_PASS sign_click=true browse=true confirm=true wallet=1000_to_700 quantity=2 uid=",bought_uid)

	_app.session_panel.disconnect_session()
	await process_frame
	if not await _login_and_choose():
		return
	var relog:=await _wait_snapshot(func(v):
		return (
			_world_ready(v) and int(v.get("player_money",-1))==700 and
			_definition_total(v.get("inventory_items",{}),DEFINITION)==2
		)
	,15000)
	if relog.is_empty():
		_fail("buyer relog persistence "+str(_app._last_snapshot))
		return
	if _definition_uid(relog.get("inventory_items",{}),DEFINITION)!=bought_uid:
		_fail("buyer UID provenance changed across relog")
		return
	print("ONLINE_PLAYER_SHOP_MAIN_GATE_BUYER_PASS formal_main=true sqlite_relog=true wallet=700 quantity=2 native_visible=",
		not DisplayServer.get_name().contains("headless"))
	_app.session_panel.disconnect_session()
	quit(0)

func _login_and_choose()->bool:
	var panel=_app.session_panel
	panel.username_input.text="player_shop_"+_role
	panel.password_input.text=PASSWORD
	panel.login()
	var selecting:=await _wait_panel(func(v):
		return (
			int(v.get("state",-1))==WorldState.STATE_SELECTING_CHARACTER and
			(v.get("characters",[]) as Array).size()==1
		)
	,10000)
	if selecting.is_empty():
		_fail("character list "+str(panel._latest))
		return false
	panel.characters.select(0)
	panel.character_choose_password_input.text=PROOF
	panel.choose()
	return true

func _world_ready(v:Dictionary)->bool:
	return (
		int(v.get("state",-1))==WorldState.STATE_IN_WORLD and
		bool(v.get("world_ready",false)) and bool(v.get("inventory_ready",false)) and
		int(v.get("pending_world_ready_ack_epoch",0))==0
	)

func _definition_total(items:Dictionary,definition:int)->int:
	var total:=0
	for raw in items.values():
		var item:Dictionary=raw
		if int(item.get("item_definition_id",0))==definition:
			total+=int(item.get("quantity",0))
	return total

func _definition_uid(items:Dictionary,definition:int)->int:
	for raw_uid in items.keys():
		var item:Dictionary=items[raw_uid]
		if int(item.get("item_definition_id",0))==definition:
			return int(raw_uid)
	return 0

func _first_button(parent:Node)->Button:
	for child in parent.get_children():
		if child is Button and not child.is_queued_for_deletion():
			return child as Button
	return null

func _open_current_sell_price_dialog(shell:Node,expected_uid:int,timeout_ms:int)->bool:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		if (
			shell._player_shop_sell_price_dialog.visible and
			int(shell._player_shop_sell_uid)==expected_uid
		):
			return true
		var row:=_first_button(shell._player_shop_inventory_rows)
		if row!=null:
			row.pressed.emit()
		await process_frame
	return false

func _wait_snapshot(predicate:Callable,timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var snap:Dictionary=_app._last_snapshot
		if predicate.call(snap):
			return snap.duplicate(true)
		await process_frame
	return {}

func _wait_panel(predicate:Callable,timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var snap:Dictionary=_app.session_panel._latest
		if predicate.call(snap):
			return snap.duplicate(true)
		await process_frame
	return {}

func _wait_main_surface(timeout_ms:int)->bool:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		if (
			_app.world_view.visible and _app.world_view.phase=="ready" and
			_app.classic_world.visible and not _app._screen_backdrop.visible and
			not _app._photo_backdrop.visible
		):
			return true
		await process_frame
	return false

func _arg(name:String)->String:
	var args:=OS.get_cmdline_user_args()
	for i in range(args.size()-1):
		if args[i]==name:
			return args[i+1]
	return ""

func _arg_int(name:String)->int:
	return int(_arg(name))
