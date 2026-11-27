extends SceneTree

const SessionPanel = preload("res://session/session_panel.gd")
const ClassicWorldShell = preload("res://ui/classic_world_shell.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"
const SKILL_ID := 28101

func _fail(message:String,panel=null)->void:
	if panel!=null:
		panel.disconnect_session()
	push_error("CLIENT_HOTKEY_NATIVE_DRAG_FAIL "+message)
	quit(1)

func _initialize()->void:
	var args:=OS.get_cmdline_user_args()
	if args.size()!=1 or not str(args[0]).is_valid_int():
		_fail("expected port")
		return
	root.size=Vector2i(800,600)
	var panel=SessionPanel.new()
	var shell=ClassicWorldShell.new()
	root.add_child(panel)
	root.add_child(shell)
	shell.bind_session(panel)
	panel.world_snapshot.connect(shell.apply_snapshot)
	panel.server_host="127.0.0.1"
	panel.server_port=int(args[0])
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
		var book:Dictionary=v.get("skill_book",{})
		return (
			_world_ready(v) and bool(v.get("skill_book_ready",false)) and
			int(book.get(SKILL_ID,book.get(str(SKILL_ID),0)))==3
		),9000)
	if ready.is_empty():
		_fail("first world baseline "+str(panel._latest),panel)
		return
	shell.set_world_active(true)
	shell.apply_snapshot(ready)
	var initial:Dictionary=await _wait_panel(panel,func(v):
		return bool(v.get("hotkeys_ready",false)) and int(v.get("pending_hotkey_sequence",0))==0,7000)
	if initial.is_empty():
		_fail("initial hotkeys "+str(panel._latest),panel)
		return
	shell.apply_snapshot(initial)
	if not panel.submit_hotkey_bind(0,1,SKILL_ID):
		_fail("slot0 bind refused",panel)
		return
	var bound:Dictionary=await _wait_panel(panel,func(v):
		var h:Dictionary=v.get("hotkeys",{})
		return (
			int(v.get("pending_hotkey_sequence",0))==0 and h.has(0) and
			int((h[0] as Dictionary).get("data_type",-1))==1 and
			int((h[0] as Dictionary).get("data_id",0))==SKILL_ID
		),7000)
	if bound.is_empty():
		_fail("slot0 bind snapshot "+str(panel._latest),panel)
		return
	shell.apply_snapshot(bound)

	var slot0:=shell._hotkey_slots[0] as Control
	var slot1:=shell._hotkey_slots[1] as Control
	var rect0:=slot0.get_global_rect()
	var rect1:=slot1.get_global_rect()
	var source:=rect0.position+rect0.size/2.0
	var destination:=rect1.position+rect1.size/2.0
	print("CLIENT_HOTKEY_NATIVE_DRAG_READY source=%.1f,%.1f destination=%.1f,%.1f viewport=%s window=%s skill_id=%d" % [
		source.x,source.y,destination.x,destination.y,
		str(root.size),str(DisplayServer.window_get_size()),SKILL_ID])
	var moved:Dictionary=await _wait_panel(panel,func(v):
		var h:Dictionary=v.get("hotkeys",{})
		return (
			int(v.get("pending_hotkey_sequence",0))==0 and not h.has(0) and
			h.has(1) and int((h[1] as Dictionary).get("data_type",-1))==1 and
			int((h[1] as Dictionary).get("data_id",0))==SKILL_ID
		),20000)
	if moved.is_empty():
		_fail("Windows drag did not move slot0 to slot1 "+str(panel._latest),panel)
		return
	shell.apply_snapshot(moved)

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
		return _world_ready(v),9000)
	if relog.is_empty():
		_fail("relog world "+str(panel._latest),panel)
		return
	shell.set_world_active(true)
	shell.apply_snapshot(relog)
	var durable:Dictionary=await _wait_panel(panel,func(v):
		var h:Dictionary=v.get("hotkeys",{})
		return (
			bool(v.get("hotkeys_ready",false)) and not h.has(0) and h.has(1) and
			int((h[1] as Dictionary).get("data_type",-1))==1 and
			int((h[1] as Dictionary).get("data_id",0))==SKILL_ID
		),7000)
	if durable.is_empty():
		_fail("slot1 not durable after relog "+str(panel._latest),panel)
		return

	panel.disconnect_session()
	shell.set_world_active(false)
	print("CLIENT_HOTKEY_NATIVE_DRAG_PASS os_mouse=true slot0_to_slot1=true server_remove_then_save=true hotkey_persisted=true relog=true")
	quit(0)

func _world_ready(v:Dictionary)->bool:
	return (
		int(v.get("state",-1))==WorldState.STATE_IN_WORLD and
		bool(v.get("world_ready",false)) and bool(v.get("combat_available",false)) and
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
