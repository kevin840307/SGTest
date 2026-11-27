extends SceneTree
## Formal main.tscn two-client gate for the original 056_Driver_INFO Mecha flow.
const Main=preload("res://app/main.gd")
const MainScene=preload("res://app/main.tscn")
const WorldState=preload("res://world/world_state.gd")
const PASSWORD:="Fixture-Password-123!"
const PROOF:="2468"
const MECHA_DEFINITION:=50018
const WEAPON_DEFINITION:=50703
const ENGINE_DEFINITION:=50702
const BATTERY_DEFINITION:=50103
const EXPECTED_CAPACITY:=25200
var _app:Main
var _role:=""
var _port:=0

func _initialize()->void:
	_run.call_deferred()

func _fail(message:String)->void:
	if _app!=null:
		_app.session_panel.disconnect_session()
	push_error("ONLINE_MECHA_MAIN_GATE_FAIL role=%s %s"%[_role,message])
	quit(1)

func _run()->void:
	root.size=Vector2i(800,600)
	_role=_arg("--role")
	_port=_arg_int("--port")
	if not _role in ["actor","observer"] or _port<=0:
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
		return _world_ready(v),15000)
	if ready.is_empty():
		_fail("initial world "+str(_app._last_snapshot))
		return
	if not await _wait_main_surface(8000):
		_fail("formal main surface not visible")
		return
	if _role=="observer":
		await _observer_flow()
	else:
		await _actor_flow()

func _actor_flow()->void:
	var seeded:=await _wait_snapshot(func(v):
		var mechas:Dictionary=v.get("mecha_instances",{})
		var items:Dictionary=v.get("inventory_items",{})
		return mechas.size()==1 and items.size()==3
	,15000)
	if seeded.is_empty():
		_fail("source-backed mecha/inventory seed not visible "+str(_app._last_snapshot))
		return
	var peer_ready:=await _wait_snapshot(func(v):
		return (v.get("entities",{}) as Dictionary).size()>=1
	,15000)
	if peer_ready.is_empty():
		_fail("dual-client peer never entered AOI")
		return

	var mechas:Dictionary=_app._last_snapshot.get("mecha_instances",{})
	var mecha_uid:=int(mechas.keys()[0])
	var weapon_uid:=_inventory_uid_for_definition(WEAPON_DEFINITION)
	var engine_uid:=_inventory_uid_for_definition(ENGINE_DEFINITION)
	var battery_uid:=_inventory_uid_for_definition(BATTERY_DEFINITION)
	if mecha_uid<=0 or weapon_uid<=0 or engine_uid<=0 or battery_uid<=0:
		_fail("seed UIDs missing "+str(_app._last_snapshot))
		return

	var shell=_app.classic_world
	var item_command:=shell.secondary_menu.get_node_or_null("ItemCommand") as TextureButton
	if item_command==null:
		_fail("021 ItemCommand missing")
		return
	item_command.pressed.emit()
	await process_frame
	var mecha_tab:=shell.inventory.get_node_or_null("InventoryTab4") as TextureButton
	if mecha_tab==null:
		_fail("023 Mecha tab missing")
		return
	mecha_tab.pressed.emit()
	await process_frame
	var mecha_row:=_find_row(shell._inventory_rows,func(button:Button):
		return "機甲 %d"%MECHA_DEFINITION in button.text)
	if mecha_row==null:
		_fail("source-backed mecha row not operable")
		return
	mecha_row.pressed.emit()
	await process_frame
	if (
		not shell._mecha_window.visible or
		shell._mecha_window.size!=Vector2(523,393) or
		shell._mecha_drive_button.position!=Vector2(1,330) or
		shell._mecha_exit_button.position!=Vector2(1,360) or
		str(shell._mecha_info_labels["type"].text)!="MS"
	):
		_fail("056_Driver_INFO geometry/type mismatch")
		return

	if shell._mecha_battery_rows.get_child_count()<1:
		_fail("056 battery action missing")
		return
	var battery_row:=shell._mecha_battery_rows.get_child(0) as Button
	if battery_row==null:
		_fail("056 battery row not operable")
		return
	battery_row.pressed.emit()
	var charged:=await _wait_snapshot(func(v):
		var current:Dictionary=(v.get("mecha_instances",{}) as Dictionary).get(mecha_uid,{})
		var items:Dictionary=v.get("inventory_items",{})
		return int(current.get("battery_charge",0))==EXPECTED_CAPACITY and not items.has(battery_uid)
	,15000)
	if charged.is_empty():
		_fail("server-authoritative battery charge/consume "+str(_app._last_snapshot))
		return
	await process_frame
	if str(shell._mecha_info_labels["energy"].text)!="%d/%d"%[EXPECTED_CAPACITY,EXPECTED_CAPACITY]:
		_fail("056 energy display did not follow server")
		return

	if not await _press_mecha_inventory_uid(weapon_uid):
		_fail("056/023 weapon candidate not operable")
		return
	var weapon_equipped:=await _wait_snapshot(func(v):
		return _mecha_slot(v,1)==WEAPON_DEFINITION
	,15000)
	if weapon_equipped.is_empty():
		_fail("server-authoritative weapon equip "+str(_app._last_snapshot))
		return
	if not await _press_mecha_inventory_uid(engine_uid):
		_fail("056/023 engine candidate not operable")
		return
	var engine_equipped:=await _wait_snapshot(func(v):
		return _mecha_slot(v,1)==WEAPON_DEFINITION and _mecha_slot(v,2)==ENGINE_DEFINITION
	,15000)
	if engine_equipped.is_empty():
		_fail("server-authoritative engine equip "+str(_app._last_snapshot))
		return
	await process_frame
	if (
		int((shell._mecha_slot_buttons[1] as Button).get_meta("item_uid",0))!=weapon_uid or
		int((shell._mecha_slot_buttons[2] as Button).get_meta("item_uid",0))!=engine_uid
	):
		_fail("056 equipment slots did not render committed server state")
		return

	if not shell._mecha_drive_button.visible or shell._mecha_drive_button.disabled:
		_fail("056 drive button not available")
		return
	shell._mecha_drive_button.pressed.emit()
	var mounted:=await _wait_snapshot(func(v):
		if int(v.get("mounted_mecha_uid",0))!=mecha_uid:
			return false
		var actor_id:=int(v.get("actor_id",0))
		var presentation:Dictionary=(v.get("mecha_presentations",{}) as Dictionary).get(actor_id,{})
		return (
			bool(presentation.get("mounted",false)) and
			int(presentation.get("definition_id",0))==MECHA_DEFINITION and
			int(presentation.get("weapon_definition_id",0))==WEAPON_DEFINITION and
			int(presentation.get("engine_definition_id",0))==ENGINE_DEFINITION
		)
	,15000)
	if mounted.is_empty():
		_fail("server-authoritative mount presentation "+str(_app._last_snapshot))
		return
	var actor_id:=int(mounted.get("actor_id",0))
	if not await _wait_mecha_profile(actor_id,15000):
		_fail("mounted source-backed world visual profile did not render")
		return
	await process_frame
	if not shell._mecha_exit_button.visible:
		_fail("056 did not switch to Mecha exit")
		return
	print("ONLINE_MECHA_MAIN_GATE_MOUNT_PASS formal_main=true source_056=true battery=25200 weapon=50703 engine=50702 world_visual=true")

	await create_timer(2.0).timeout
	_app.session_panel.disconnect_session()
	await process_frame
	if not await _login_and_choose():
		return
	var relog:=await _wait_snapshot(func(v):
		return (
			_world_ready(v) and
			int(v.get("mounted_mecha_uid",0))==mecha_uid and
			_mecha_slot(v,1)==WEAPON_DEFINITION and
			_mecha_slot(v,2)==ENGINE_DEFINITION and
			_mecha_charge(v,mecha_uid)==EXPECTED_CAPACITY
		)
	,15000)
	if relog.is_empty():
		_fail("mounted/equipment/battery SQLite relog persistence "+str(_app._last_snapshot))
		return
	actor_id=int(relog.get("actor_id",0))
	if not await _wait_mecha_profile(actor_id,15000):
		_fail("relog did not restore mounted source visual")
		return

	if not shell.inventory.visible:
		item_command=shell.secondary_menu.get_node_or_null("ItemCommand") as TextureButton
		item_command.pressed.emit()
		await process_frame
	mecha_tab=shell.inventory.get_node_or_null("InventoryTab4") as TextureButton
	mecha_tab.pressed.emit()
	await process_frame
	mecha_row=_find_row(shell._inventory_rows,func(button:Button):
		return "機甲 %d"%MECHA_DEFINITION in button.text)
	if mecha_row==null:
		_fail("relog mecha row missing")
		return
	mecha_row.pressed.emit()
	await process_frame
	if not shell._mecha_exit_button.visible or shell._mecha_exit_button.disabled:
		_fail("relog 056 exit button unavailable")
		return
	shell._mecha_exit_button.pressed.emit()
	var dismounted:=await _wait_snapshot(func(v):
		var presentations:Dictionary=v.get("mecha_presentations",{})
		var self_id:=int(v.get("actor_id",0))
		return int(v.get("mounted_mecha_uid",0))==0 and not presentations.has(self_id)
	,15000)
	if dismounted.is_empty():
		_fail("server-authoritative dismount "+str(_app._last_snapshot))
		return
	var profile_cleared:=await _wait_profile_absent(actor_id,8000)
	if not profile_cleared:
		_fail("world visual did not restore ordinary player after dismount")
		return
	print("ONLINE_MECHA_MAIN_GATE_DISMOUNT_PASS formal_main=true human_visual_restored=true")

	await create_timer(2.0).timeout
	_app.session_panel.disconnect_session()
	await process_frame
	if not await _login_and_choose():
		return
	var final_relog:=await _wait_snapshot(func(v):
		var items:Dictionary=v.get("inventory_items",{})
		return (
			_world_ready(v) and
			int(v.get("mounted_mecha_uid",0))==0 and
			_mecha_slot(v,1)==WEAPON_DEFINITION and
			_mecha_slot(v,2)==ENGINE_DEFINITION and
			_mecha_charge(v,mecha_uid)==EXPECTED_CAPACITY and
			not items.has(battery_uid)
		)
	,15000)
	if final_relog.is_empty():
		_fail("final dismounted SQLite persistence "+str(_app._last_snapshot))
		return
	print("ONLINE_MECHA_MAIN_GATE_ACTOR_PASS sqlite_relog=true formal_main=true source_backed=true native_visible=",
		not DisplayServer.get_name().contains("headless"))
	_app.session_panel.disconnect_session()
	quit(0)

func _observer_flow()->void:
	var self_id:=int(_app._last_snapshot.get("actor_id",0))
	var mounted:=await _wait_snapshot(func(v):
		var presentations:Dictionary=v.get("mecha_presentations",{})
		for raw_id in presentations:
			var id:=int(raw_id)
			if id==self_id:
				continue
			var p:Dictionary=presentations[raw_id]
			if (
				bool(p.get("mounted",false)) and
				int(p.get("definition_id",0))==MECHA_DEFINITION and
				int(p.get("weapon_definition_id",0))==WEAPON_DEFINITION and
				int(p.get("engine_definition_id",0))==ENGINE_DEFINITION
			):
				return true
		return false
	,30000)
	if mounted.is_empty():
		_fail("remote mounted Mecha presentation never became visible")
		return
	var seen_actor:=_find_remote_mounted_actor(mounted,self_id)
	if seen_actor<=0 or not await _wait_mecha_profile(seen_actor,15000):
		_fail("remote actor source-backed Mecha visual never rendered")
		return
	print("ONLINE_MECHA_MAIN_GATE_OBSERVER_MOUNT_PASS actor=",seen_actor," dual_client=true world_visual=true")

	var despawned:=await _wait_snapshot(func(v):
		return not (v.get("entities",{}) as Dictionary).has(seen_actor)
	,15000)
	if despawned.is_empty():
		_fail("remote mounted actor did not despawn for relog")
		return
	var relogged:=await _wait_snapshot(func(v):
		var entities:Dictionary=v.get("entities",{})
		var presentations:Dictionary=v.get("mecha_presentations",{})
		return (
			entities.has(seen_actor) and presentations.has(seen_actor) and
			bool((presentations[seen_actor] as Dictionary).get("mounted",false))
		)
	,20000)
	if relogged.is_empty() or not await _wait_mecha_profile(seen_actor,15000):
		_fail("remote relog did not restore mounted Mecha visual")
		return
	print("ONLINE_MECHA_MAIN_GATE_OBSERVER_RELOG_PASS actor=",seen_actor," sqlite_persistence=true")

	var cleared:=await _wait_snapshot(func(v):
		var presentations:Dictionary=v.get("mecha_presentations",{})
		return (v.get("entities",{}) as Dictionary).has(seen_actor) and not presentations.has(seen_actor)
	,30000)
	if cleared.is_empty() or not await _wait_profile_absent(seen_actor,8000):
		_fail("remote Mecha presentation/profile did not clear after dismount")
		return
	print("ONLINE_MECHA_MAIN_GATE_OBSERVER_PASS mount_relog_dismount=true dual_client=true formal_main=true")
	_app.session_panel.disconnect_session()
	quit(0)

func _press_mecha_inventory_uid(uid:int)->bool:
	var shell=_app.classic_world
	shell._refresh_inventory()
	await process_frame
	var row:=_find_row(shell._inventory_rows,func(button:Button):
		return "背包 UID %d"%uid in button.tooltip_text)
	if row==null:
		return false
	row.pressed.emit()
	return true

func _find_row(container:Control,predicate:Callable)->Button:
	for child in container.get_children():
		if child is Button and predicate.call(child):
			return child as Button
	return null

func _inventory_uid_for_definition(definition:int)->int:
	var items:Dictionary=_app._last_snapshot.get("inventory_items",{})
	for raw_uid in items:
		var item:Dictionary=items[raw_uid]
		if int(item.get("item_definition_id",0))==definition:
			return int(raw_uid)
	return 0

func _mecha_slot(snapshot:Dictionary,slot_id:int)->int:
	var items:Dictionary=snapshot.get("mecha_equipment_items",{})
	for raw_uid in items:
		var item:Dictionary=items[raw_uid]
		if int(item.get("slot_id",0))==slot_id:
			return int(item.get("item_definition_id",0))
	return 0

func _mecha_charge(snapshot:Dictionary,mecha_uid:int)->int:
	var mechas:Dictionary=snapshot.get("mecha_instances",{})
	var mecha:Variant=mechas.get(mecha_uid,mechas.get(str(mecha_uid),{}))
	return int((mecha as Dictionary).get("battery_charge",0)) if mecha is Dictionary else -1

func _find_remote_mounted_actor(snapshot:Dictionary,self_id:int)->int:
	var presentations:Dictionary=snapshot.get("mecha_presentations",{})
	for raw_id in presentations:
		var id:=int(raw_id)
		if id==self_id:
			continue
		var p:Dictionary=presentations[raw_id]
		if bool(p.get("mounted",false)) and int(p.get("definition_id",0))==MECHA_DEFINITION:
			return id
	return 0

func _wait_mecha_profile(actor_id:int,timeout_ms:int)->bool:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		if _app.world_view._mecha_profiles.has(actor_id):
			var profile=_app.world_view._mecha_profiles[actor_id]
			if not profile.parts_at("NS",1,0).is_empty():
				return true
		await process_frame
	return false

func _wait_profile_absent(actor_id:int,timeout_ms:int)->bool:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		if not _app.world_view._mecha_profiles.has(actor_id):
			return true
		await process_frame
	return false

func _login_and_choose()->bool:
	var panel=_app.session_panel
	panel.username_input.text="mecha_"+_role
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
		bool(v.get("world_ready",false)) and
		bool(v.get("inventory_ready",false)) and
		int(v.get("pending_world_ready_ack_epoch",0))==0
	)

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
