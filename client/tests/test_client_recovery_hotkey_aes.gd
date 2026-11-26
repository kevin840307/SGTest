extends SceneTree

const SessionPanel = preload("res://session/session_panel.gd")
const ClassicWorldShell = preload("res://ui/classic_world_shell.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message:String,panel=null)->void:
	if panel!=null:
		panel.disconnect_session()
	push_error("CLIENT_RECOVERY_HOTKEY_AES_FAIL "+message)
	quit(1)

func _initialize()->void:
	var args:=OS.get_cmdline_user_args()
	if args.size()!=1 or not str(args[0]).is_valid_int():
		_fail("expected port")
		return
	var port:=int(args[0])
	var panel=SessionPanel.new()
	var shell=ClassicWorldShell.new()
	root.add_child(panel)
	root.add_child(shell)
	shell.bind_session(panel)
	panel.world_snapshot.connect(shell.apply_snapshot)
	panel.server_host="127.0.0.1"
	panel.server_port=port
	panel.username_input.text=USERNAME
	panel.password_input.text=PASSWORD
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
			_world_ready(v) and
			int(v.get("player_current_hp",0))==60 and
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
	if shell._hp_label.text!="60/100":
		_fail("initial HUD HP "+shell._hp_label.text,panel)
		return
	var initial_hotkeys:Dictionary=await _wait_panel(panel,func(v):
		return bool(v.get("hotkeys_ready",false)) and int(v.get("pending_hotkey_sequence",0))==0,7000)
	if initial_hotkeys.is_empty():
		_fail("initial hotkey refresh "+str(panel._latest),panel)
		return
	shell.apply_snapshot(initial_hotkeys)
	if not panel.submit_hotkey_bind(0,0,uid):
		_fail("hotkey bind refused",panel)
		return
	var bound:Dictionary=await _wait_panel(panel,func(v):
		var hotkeys:Dictionary=v.get("hotkeys",{})
		return (
			bool(v.get("hotkeys_ready",false)) and
			int(v.get("pending_hotkey_sequence",0))==0 and
			hotkeys.has(0) and
			int((hotkeys[0] as Dictionary).get("data_type",-1))==0 and
			int((hotkeys[0] as Dictionary).get("data_id",0))==uid
		),7000)
	if bound.is_empty():
		_fail("authoritative hotkey bind "+str(panel._latest),panel)
		return
	shell.apply_snapshot(bound)

	var f1:=InputEventKey.new()
	f1.keycode=KEY_F1
	f1.pressed=true
	Input.parse_input_event(f1)
	await process_frame
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
		_fail("F1 authoritative recovery "+str(panel._latest),panel)
		return
	await process_frame
	shell.apply_snapshot(panel._latest)
	if shell._hp_label.text!="90/100":
		_fail("HUD did not render committed HP "+shell._hp_label.text,panel)
		return
	var rendered_items:Dictionary=shell._stats.get("inventory_items",{})
	if not rendered_items.has(uid) or int((rendered_items[uid] as Dictionary).get("quantity",0))!=1:
		_fail("Classic inventory did not receive committed quantity",panel)
		return

	panel.disconnect_session()
	shell.set_world_active(false)
	await process_frame
	panel.username_input.text=USERNAME
	panel.password_input.text=PASSWORD
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
			int(v.get("player_current_hp",0))==90 and
			items.has(uid) and int((items[uid] as Dictionary).get("quantity",0))==1
		),9000)
	if relog.is_empty():
		_fail("relog persistence "+str(panel._latest),panel)
		return
	# In the real app the Classic world becomes active when the WORLD page is
	# selected; that activation is what requests the authoritative hotkey list.
	shell.set_world_active(true)
	shell.apply_snapshot(relog)
	var relog_hotkeys:Dictionary=await _wait_panel(panel,func(v):
		var hotkeys:Dictionary=v.get("hotkeys",{})
		return (
			bool(v.get("hotkeys_ready",false)) and hotkeys.has(0) and
			int((hotkeys[0] as Dictionary).get("data_type",-1))==0 and
			int((hotkeys[0] as Dictionary).get("data_id",0))==uid
		),7000)
	if relog_hotkeys.is_empty():
		_fail("relog hotkey refresh "+str(panel._latest),panel)
		return
	shell.apply_snapshot(relog_hotkeys)
	if shell._hp_label.text!="90/100":
		_fail("relog HUD HP "+shell._hp_label.text,panel)
		return

	panel.disconnect_session()
	shell.set_world_active(false)
	print("CLIENT_RECOVERY_HOTKEY_AES_PASS f1_input=true server_receipt=true hp=60_to_90 quantity=2_to_1 hud=true inventory=true relog=true hotkey_persisted=true")
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
