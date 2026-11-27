extends SceneTree
## Formal main.tscn gate for character/equipment/dynamic paper-doll.
const Main=preload("res://app/main.gd")
const MainScene=preload("res://app/main.tscn")
const WorldState=preload("res://world/world_state.gd")
const PASSWORD:="Fixture-Password-123!"
const PROOF:="2468"
const DEFINITION:=18501
var _app:Main
var _role:=""
var _port:=0

func _initialize()->void:
	_run.call_deferred()

func _fail(message:String)->void:
	if _app!=null:_app.session_panel.disconnect_session()
	push_error("ONLINE_EQUIPMENT_MAIN_GATE_FAIL role=%s %s"%[_role,message])
	quit(1)

func _run()->void:
	root.size=Vector2i(800,600)
	_role=_arg("--role")
	_port=_arg_int("--port")
	if not _role in ["actor","observer"] or _port<=0:
		_fail("invalid args")
		return
	_app=MainScene.instantiate() as Main
	root.add_child(_app);current_scene=_app
	await process_frame
	_app.session_panel.server_host="127.0.0.1"
	_app.session_panel.server_port=_port
	if not await _login_and_choose():
		return
	var ready:=await _wait_snapshot(func(v):
		return _world_ready(v) and int(v.get("player_defense",-1))==3,15000)
	if ready.is_empty():
		_fail("initial world/profile "+str(_app._last_snapshot))
		return
	if not await _wait_main_surface(8000):
		_fail("formal main surface not visible")
		return
	if _role=="observer":
		await _observer_flow()
	else:
		await _actor_flow()

func _actor_flow()->void:
	var peer_ready:=await _wait_snapshot(func(v):
		return (v.get("entities",{}) as Dictionary).size()>=1
	,15000)
	if peer_ready.is_empty():
		_fail("dual-client peer never entered AOI before equip");return
	var snap:Dictionary=_app._last_snapshot
	var items:Dictionary=snap.get("inventory_items",{})
	if items.size()!=1:
		_fail("actor seed inventory "+str(items));return
	var uid:=int(items.keys()[0])
	if int((items[uid] as Dictionary).get("item_definition_id",0))!=DEFINITION:
		_fail("actor seed definition "+str(items));return
	var shell=_app.classic_world
	var item_command:=shell.secondary_menu.get_node_or_null("ItemCommand") as TextureButton
	if item_command==null:
		_fail("021 ItemCommand missing");return
	item_command.pressed.emit();await process_frame
	var equip_tab:=shell.inventory.get_node_or_null("InventoryTab1") as TextureButton
	if equip_tab==null:
		_fail("022 equipment tab missing");return
	equip_tab.pressed.emit();await process_frame
	if not shell.inventory.visible or shell._inventory_rows.get_child_count()<1:
		_fail("022 equipment candidate list empty");return
	var row:=shell._inventory_rows.get_child(0) as Button
	if row==null:
		_fail("022 equipment row not operable");return
	row.pressed.emit()
	var equipped:=await _wait_snapshot(func(v):
		var eq:Dictionary=v.get("equipment_items",{})
		if eq.size()!=1 or int(v.get("player_defense",-1))!=9:return false
		var e:Dictionary=eq.values()[0]
		var pres:Dictionary=v.get("player_equipment_presentations",{})
		var self_id:=int(v.get("actor_id",0))
		return (
			int(e.get("item_definition_id",0))==DEFINITION and int(e.get("slot_id",0))==3 and
			pres.has(self_id) and int((pres[self_id] as Dictionary).get("slot_definitions",{}).get(3,0))==DEFINITION
		),15000)
	if equipped.is_empty():
		_fail("server-authoritative equip/profile/presentation "+str(_app._last_snapshot));return
	await process_frame
	var slot:=shell._equipment_slot_buttons.get(3) as TextureButton
	var marker:=slot.get_node_or_null("EquippedMarker") as TextureRect if slot!=null else null
	var preview_app:Dictionary=shell._equipment_preview._appearance
	if (
		slot==null or int(slot.get_meta("item_uid",0))!=uid or marker==null or marker.texture==null or
		not marker.visible or int(preview_app.get("_equipment_body_color",-9))!=0
	):
		_fail("022 slot/paper-doll did not render committed equipment");return
	var player_command:=shell.secondary_menu.get_node_or_null("PlayerCommand") as TextureButton
	if player_command==null:
		_fail("013 PlayerCommand missing");return
	player_command.pressed.emit();await process_frame
	if not shell.character.visible or not shell._character_labels.has("防禦") or 			str(shell._character_labels["防禦"].text)!="9":
		_fail("013 Character_INFO did not render verified Defense=9");return
	print("ONLINE_EQUIPMENT_MAIN_GATE_EQUIP_PASS formal_main=true slot3=true paper_doll=true defense=3_to_9 uid=",uid)
	# Give observer a visible interval on the same formal Host.
	await create_timer(2.0).timeout
	_app.session_panel.disconnect_session();await process_frame
	if not await _login_and_choose():return
	var relog:=await _wait_snapshot(func(v):
		var eq:Dictionary=v.get("equipment_items",{})
		return _world_ready(v) and int(v.get("player_defense",-1))==9 and eq.size()==1 and int((eq.values()[0] as Dictionary).get("slot_id",0))==3,15000)
	if relog.is_empty():
		_fail("equipped relog persistence "+str(_app._last_snapshot));return
	var panel_relog:=await _wait_panel(func(v):
		var eq:Dictionary=v.get("equipment_items",{})
		return _world_ready(v) and int(v.get("player_defense",-1))==9 and eq.has(uid),3000)
	if panel_relog.is_empty():
		_fail("relog operation panel did not catch up to authoritative equipment");return
	if not shell.inventory.visible:
		item_command=shell.secondary_menu.get_node_or_null("ItemCommand") as TextureButton
		item_command.pressed.emit();await process_frame
	equip_tab=shell.inventory.get_node_or_null("InventoryTab1") as TextureButton
	equip_tab.pressed.emit();await process_frame
	slot=shell._equipment_slot_buttons.get(3) as TextureButton
	if slot==null or int(slot.get_meta("item_uid",0))!=uid:
		_fail("relog 022 slot not restored");return
	slot.pressed.emit()
	var unequipped:=await _wait_snapshot(func(v):
		return (v.get("equipment_items",{}) as Dictionary).is_empty() and int(v.get("player_defense",-1))==3,15000)
	if unequipped.is_empty():
		_fail("authoritative unequip/profile restore "+str(_app._last_snapshot));return
	await process_frame
	if int(shell._equipment_preview._appearance.get("_equipment_body_color",-9))!=-1:
		_fail("paper-doll body overlay did not clear");return
	print("ONLINE_EQUIPMENT_MAIN_GATE_UNEQUIP_PASS defense=9_to_3 paper_doll_clear=true")
	await create_timer(2.0).timeout
	_app.session_panel.disconnect_session();await process_frame
	if not await _login_and_choose():return
	var final_relog:=await _wait_snapshot(func(v):
		return (
			_world_ready(v) and
			int(v.get("player_defense",-1))==3 and
			(v.get("equipment_items",{}) as Dictionary).is_empty() and
			(v.get("inventory_items",{}) as Dictionary).has(uid)
		),15000)
	if final_relog.is_empty():
		_fail("unequipped relog persistence "+str(_app._last_snapshot));return
	print("ONLINE_EQUIPMENT_MAIN_GATE_ACTOR_PASS sqlite_relog=true formal_main=true native_visible=",
		not DisplayServer.get_name().contains("headless"))
	_app.session_panel.disconnect_session();quit(0)

func _observer_flow()->void:
	var self_id:=int(_app._last_snapshot.get("actor_id",0))
	var equipped:=await _wait_snapshot(func(v):
		var presentations:Dictionary=v.get("player_equipment_presentations",{})
		for raw_id in presentations:
			var id:=int(raw_id)
			if id==self_id:continue
			var slot_defs:Dictionary=(presentations[raw_id] as Dictionary).get("slot_definitions",{})
			if int(slot_defs.get(3,0))==DEFINITION:
				return true
		return false,25000)
	if equipped.is_empty():
		_fail("remote equipment presentation never became visible");return
	var seen_actor:=0
	var equipped_presentations:Dictionary=equipped.get("player_equipment_presentations",{})
	for raw_id in equipped_presentations:
		var id:=int(raw_id)
		if id==self_id:continue
		var slot_defs:Dictionary=(equipped_presentations[raw_id] as Dictionary).get("slot_definitions",{})
		if int(slot_defs.get(3,0))==DEFINITION:
			seen_actor=id
			break
	if seen_actor<=0:
		_fail("remote equipment presentation actor id was not recoverable");return
	var remote:Dictionary=_app._last_snapshot.get("entities",{}).get(seen_actor,{})
	if remote.is_empty():
		_fail("remote actor missing while equipment visible");return
	print("ONLINE_EQUIPMENT_MAIN_GATE_OBSERVER_EQUIP_PASS actor=",seen_actor,
		" slot3=18501 dual_client=true")
	var despawned:=await _wait_snapshot(func(v):
		var entities:Dictionary=v.get("entities",{})
		var presentations:Dictionary=v.get("player_equipment_presentations",{})
		return not entities.has(seen_actor) and not presentations.has(seen_actor)
	,15000)
	if despawned.is_empty():
		_fail("remote actor did not despawn between relog");return
	print("ONLINE_EQUIPMENT_MAIN_GATE_OBSERVER_DESPAWN_PASS actor=",seen_actor)
	var relogged:=await _wait_snapshot(func(v):
		var entities:Dictionary=v.get("entities",{})
		var presentations:Dictionary=v.get("player_equipment_presentations",{})
		if not entities.has(seen_actor) or not presentations.has(seen_actor):
			return false
		var slot_defs:Dictionary=(presentations[seen_actor] as Dictionary).get("slot_definitions",{})
		return int(slot_defs.get(3,0))==DEFINITION
	,20000)
	if relogged.is_empty():
		_fail("remote relog did not restore equipped presentation");return
	print("ONLINE_EQUIPMENT_MAIN_GATE_OBSERVER_RELOG_PASS actor=",seen_actor,
		" slot3=18501 sqlite_persistence=true")
	var cleared:=await _wait_snapshot(func(v):
		var presentations:Dictionary=v.get("player_equipment_presentations",{})
		if not presentations.has(seen_actor):
			return false
		return int((presentations[seen_actor] as Dictionary).get("slot_definitions",{}).get(3,0))==0
	,30000)
	if cleared.is_empty():
		_fail("remote equipment presentation did not clear after unequip");return
	print("ONLINE_EQUIPMENT_MAIN_GATE_OBSERVER_PASS equip_relog_and_clear=true dual_client=true formal_main=true")
	_app.session_panel.disconnect_session();quit(0)

func _login_and_choose()->bool:
	var panel=_app.session_panel
	panel.username_input.text="equipment_"+_role
	panel.password_input.text=PASSWORD
	panel.login()
	var selecting:=await _wait_panel(func(v):
		return (
			int(v.get("state",-1))==WorldState.STATE_SELECTING_CHARACTER and
			(v.get("characters",[]) as Array).size()==1
		),10000)
	if selecting.is_empty():
		_fail("character list "+str(panel._latest));return false
	panel.characters.select(0)
	panel.character_choose_password_input.text=PROOF
	panel.choose()
	return true

func _world_ready(v:Dictionary)->bool:
	return int(v.get("state",-1))==WorldState.STATE_IN_WORLD and 		bool(v.get("world_ready",false)) and bool(v.get("inventory_ready",false)) and 		bool(v.get("equipment_ready",false)) and bool(v.get("player_profile_ready",false)) and 		int(v.get("pending_world_ready_ack_epoch",0))==0

func _wait_snapshot(predicate:Callable,timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var snap:Dictionary=_app._last_snapshot
		if predicate.call(snap):return snap.duplicate(true)
		await process_frame
	return {}

func _wait_panel(predicate:Callable,timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var snap:Dictionary=_app.session_panel._latest
		if predicate.call(snap):return snap.duplicate(true)
		await process_frame
	return {}

func _wait_main_surface(timeout_ms:int)->bool:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		if _app.world_view.visible and _app.world_view.phase=="ready" and _app.classic_world.visible and 				not _app._screen_backdrop.visible and not _app._photo_backdrop.visible:
			return true
		await process_frame
	return false

func _arg(name:String)->String:
	var args:=OS.get_cmdline_user_args()
	for i in range(args.size()-1):
		if args[i]==name:return args[i+1]
	return ""

func _arg_int(name:String)->int:
	return int(_arg(name))
