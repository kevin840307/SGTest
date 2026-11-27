extends SceneTree

const SessionPanel = preload("res://session/session_panel.gd")
const ClassicWorldShell = preload("res://ui/classic_world_shell.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _credential(name:String,fallback:String)->String:
	var value:=OS.get_environment(name)
	return value if not value.is_empty() else fallback

func _fail(message:String,panel=null)->void:
	if panel!=null:
		panel.disconnect_session()
	push_error("CLIENT_CLASSIC_INVENTORY_AES_FAIL "+message)
	quit(1)

func _initialize()->void:
	_run.call_deferred()

func _run()->void:
	var args:=OS.get_cmdline_user_args()
	if args.size()!=1 or not str(args[0]).is_valid_int():
		_fail("expected port")
		return
	var panel=SessionPanel.new()
	var shell=ClassicWorldShell.new()
	root.add_child(panel)
	root.add_child(shell)
	shell.bind_session(panel)
	panel.world_snapshot.connect(shell.apply_snapshot)
	panel.server_host="127.0.0.1"
	panel.server_port=int(args[0])
	var username:=_credential("SGTEST_E2E_USERNAME",USERNAME)
	var password:=_credential("SGTEST_E2E_PASSWORD",PASSWORD)
	panel.username_input.text=username
	panel.password_input.text=password
	panel.login()

	var selecting:Dictionary=await _wait_panel(panel,func(v):
		return int(v.get("state",-1))==WorldState.STATE_SELECTING_CHARACTER,7000)
	if selecting.is_empty() or selecting.characters.size()!=1:
		_fail("first character list "+str(panel._latest),panel)
		return
	var character_id:=int(selecting.characters[0].character_id)
	if not FixtureLogin.provision_and_choose(panel.session,character_id):
		_fail("first choose",panel)
		return
	var ready:Dictionary=await _wait_panel(panel,func(v):
		return (
			_world_ready(v) and int(v.get("player_current_hp",0))==60 and
			int(v.get("player_max_hp",0))==100 and
			(v.get("inventory_items",{}) as Dictionary).size()==1
		),9000)
	if ready.is_empty():
		_fail("first world baseline "+str(panel._latest),panel)
		return
	var uid:=int((ready.inventory_items as Dictionary).keys()[0])
	if uid<=0 or int(ready.inventory_items[uid].item_definition_id)!=1001 or int(ready.inventory_items[uid].quantity)!=2:
		_fail("unexpected recovery inventory "+str(ready.inventory_items),panel)
		return

	shell.set_world_active(true)
	shell.apply_snapshot(ready)
	if shell.inventory.visible:
		_fail("023_ITEM must start closed",panel)
		return
	var item_command:=shell.secondary_menu.get_node_or_null("ItemCommand") as TextureButton
	if item_command==null:
		_fail("021_COMMAND ItemCommand missing",panel)
		return
	item_command.pressed.emit()
	await process_frame
	if not shell.inventory.visible:
		_fail("right-side bag command did not open 023_ITEM",panel)
		return
	if shell._inventory_rows.get_child_count()!=1:
		_fail("023_ITEM did not render authoritative inventory",panel)
		return
	var row:=shell._inventory_rows.get_child(0) as Button
	if row==null or not row.text.contains("×2"):
		_fail("023_ITEM quantity baseline "+str(row.text if row!=null else "<missing>"),panel)
		return

	# Keep the existing fixture's durable hotkey assertion satisfied without
	# activating F1. The recovery action below must come from the 023_ITEM row.
	var hotkey_ready:Dictionary=await _wait_panel(panel,func(v):
		return bool(v.get("hotkeys_ready",false)) and int(v.get("pending_hotkey_sequence",0))==0,7000)
	if hotkey_ready.is_empty():
		_fail("hotkey refresh "+str(panel._latest),panel)
		return
	if not panel.submit_hotkey_bind(0,0,uid):
		_fail("fixture hotkey bind refused",panel)
		return
	var bound:Dictionary=await _wait_panel(panel,func(v):
		var hotkeys:Dictionary=v.get("hotkeys",{})
		return (
			bool(v.get("hotkeys_ready",false)) and
			int(v.get("pending_hotkey_sequence",0))==0 and hotkeys.has(0) and
			int((hotkeys[0] as Dictionary).get("data_type",-1))==0 and
			int((hotkeys[0] as Dictionary).get("data_id",0))==uid
		),7000)
	if bound.is_empty():
		_fail("fixture hotkey bind settlement "+str(panel._latest),panel)
		return
	shell.apply_snapshot(bound)
	row=shell._inventory_rows.get_child(0) as Button
	row.pressed.emit()

	var used:Dictionary=await _wait_panel(panel,func(v):
		var items:Dictionary=v.get("inventory_items",{})
		return (
			int(v.get("last_recovery_result_sequence",0))==1 and
			int(v.get("last_recovery_result_status",-1))==0 and
			int(v.get("pending_recovery_sequence",0))==0 and
			int(v.get("player_current_hp",0))==90 and
			items.has(uid) and int((items[uid] as Dictionary).get("quantity",0))==1
		),9000)
	if used.is_empty():
		_fail("023_ITEM row did not perform authoritative recovery "+str(panel._latest),panel)
		return
	shell.apply_snapshot(used)
	await process_frame
	if shell._hp_label.text!="90/100":
		_fail("HUD did not render committed HP "+shell._hp_label.text,panel)
		return
	if shell._inventory_rows.get_child_count()!=1 or not str((shell._inventory_rows.get_child(0) as Button).text).contains("×1"):
		_fail("023_ITEM did not render committed quantity",panel)
		return

	panel.disconnect_session()
	shell.set_world_active(false)
	await process_frame
	panel.username_input.text=username
	panel.password_input.text=password
	panel.login()
	var selecting2:Dictionary=await _wait_panel(panel,func(v):
		return int(v.get("state",-1))==WorldState.STATE_SELECTING_CHARACTER,7000)
	if selecting2.is_empty() or selecting2.characters.size()!=1:
		_fail("relog character list "+str(panel._latest),panel)
		return
	if not FixtureLogin.provision_and_choose(panel.session,character_id):
		_fail("relog choose",panel)
		return
	var relog:Dictionary=await _wait_panel(panel,func(v):
		if not _world_ready(v):
			return false
		var items:Dictionary=v.get("inventory_items",{})
		return (
			int(v.get("player_current_hp",0))==90 and items.has(uid) and
			int((items[uid] as Dictionary).get("quantity",0))==1
		),9000)
	if relog.is_empty():
		_fail("relog persistence "+str(panel._latest),panel)
		return
	shell.set_world_active(true)
	shell.apply_snapshot(relog)
	if shell._hp_label.text!="90/100" or shell._inventory_rows.get_child_count()!=1 or not str((shell._inventory_rows.get_child(0) as Button).text).contains("×1"):
		_fail("relog Classic inventory/HUD presentation",panel)
		return

	panel.disconnect_session()
	shell.set_world_active(false)
	print("CLIENT_CLASSIC_INVENTORY_AES_PASS command_021=true item_023=true row_click=true server_recovery=true hp=60_to_90 quantity=2_to_1 relog=true")
	quit(0)

func _world_ready(v:Dictionary)->bool:
	return (
		int(v.get("state",-1))==WorldState.STATE_IN_WORLD and
		bool(v.get("world_ready",false)) and bool(v.get("inventory_ready",false)) and
		bool(v.get("combat_available",false)) and
		int(v.get("pending_world_ready_ack_epoch",0))==0 and
		int(v.get("last_world_ready_ack_epoch",0))==int(v.get("map_epoch",0))
	)

func _wait_panel(panel,predicate:Callable,timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=panel._latest.duplicate(true)
		if not value.is_empty() and predicate.call(value):
			return value
		if int(value.get("state",-1))==WorldState.STATE_FAILED and not bool(value.get("reconnecting",false)):
			return {}
		await process_frame
	return {}
