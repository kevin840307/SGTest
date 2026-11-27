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
	push_error("CLIENT_HOTKEY_SKILL_AES_FAIL "+message)
	quit(1)

func _initialize()->void:
	var args:=OS.get_cmdline_user_args()
	var native_os_f1:=args.size()==2 and str(args[1])=="--native-os-f1"
	if (args.size()!=1 and not native_os_f1) or not str(args[0]).is_valid_int():
		_fail("expected port and optional --native-os-f1")
		return
	var port:=int(args[0])
	if native_os_f1:
		root.size=Vector2i(800,600)
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
		var book:Dictionary=v.get("skill_book",{})
		return (
			_world_ready(v) and bool(v.get("skill_book_ready",false)) and
			int(book.get(SKILL_ID,book.get(str(SKILL_ID),0)))==3 and
			int(v.get("player_current_mp",0))==2 and
			int(v.get("player_max_mp",0))==2 and
			(v.get("monsters",{}) as Dictionary).size()==1
		),9000)
	if ready.is_empty():
		_fail("first world/skill baseline "+str(panel._latest),panel)
		return
	var target:=int((ready.monsters as Dictionary).keys()[0])

	shell.set_world_active(true)
	shell.apply_snapshot(ready)
	var initial_hotkeys:Dictionary=await _wait_panel(panel,func(v):
		return (
			bool(v.get("hotkeys_ready",false)) and
			int(v.get("pending_hotkey_sequence",0))==0
		),7000)
	if initial_hotkeys.is_empty():
		_fail("initial hotkey refresh "+str(panel._latest),panel)
		return
	shell.apply_snapshot(initial_hotkeys)
	if not panel.submit_hotkey_bind(0,1,SKILL_ID):
		_fail("skill hotkey bind refused",panel)
		return
	var bound:Dictionary=await _wait_panel(panel,func(v):
		var hotkeys:Dictionary=v.get("hotkeys",{})
		return (
			bool(v.get("hotkeys_ready",false)) and
			int(v.get("pending_hotkey_sequence",0))==0 and
			hotkeys.has(0) and
			int((hotkeys[0] as Dictionary).get("data_type",-1))==1 and
			int((hotkeys[0] as Dictionary).get("data_id",0))==SKILL_ID
		),7000)
	if bound.is_empty():
		_fail("authoritative skill hotkey bind "+str(panel._latest),panel)
		return
	shell.apply_snapshot(bound)

	if native_os_f1:
		print("CLIENT_HOTKEY_SKILL_NATIVE_READY os_f1_required=true skill_id=%d" % SKILL_ID)
		var native_deadline:=Time.get_ticks_msec()+60000
		while panel._pre_use_skill_id!=SKILL_ID and Time.get_ticks_msec()<native_deadline:
			await process_frame
		if panel._pre_use_skill_id!=SKILL_ID:
			_fail("Windows OS F1 did not stage learned skill",panel)
			return
	else:
		var f1:=InputEventKey.new()
		f1.keycode=KEY_F1
		f1.pressed=true
		Input.parse_input_event(f1)
		await process_frame
		if panel._pre_use_skill_id!=SKILL_ID:
			_fail("F1 did not stage learned skill",panel)
			return
	# main.gd's live monster-click handler calls this exact action after
	# WorldView picks the visible monster entity.
	if not panel.submit_monster_action(target):
		_fail("monster click action refused staged F1 skill",panel)
		return
	if panel._pre_use_skill_id!=0:
		_fail("one-shot F1 skill remained staged after accepted target intent",panel)
		return
	var settled:Dictionary=await _wait_panel(panel,func(v):
		if (
			int(v.get("last_skill_result_sequence",0))!=1 or
			int(v.get("last_skill_result_status",-1))!=0
		):
			return false
		var result:Dictionary=v.get("last_skill_result",{})
		var monsters:Dictionary=v.get("monsters",{})
		return (
			int(result.get("skill_id",0))==SKILL_ID and
			int(result.get("target_entity_id",0))==target and
			int(result.get("damage",-1))==40 and
			int(result.get("current_hp",-1))==60 and
			int(v.get("player_current_mp",0))==1 and
			monsters.has(target) and
			int((monsters[target] as Dictionary).get("current_hp",-1))==60
		),9000)
	if settled.is_empty():
		_fail("F1 target skill authoritative settlement "+str(panel._latest),panel)
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
		var book:Dictionary=v.get("skill_book",{})
		return (
			_world_ready(v) and bool(v.get("skill_book_ready",false)) and
			int(book.get(SKILL_ID,book.get(str(SKILL_ID),0)))==3 and
			int(v.get("player_current_mp",0))==1
		),9000)
	if relog.is_empty():
		_fail("relog skill/MP persistence "+str(panel._latest),panel)
		return
	shell.set_world_active(true)
	shell.apply_snapshot(relog)
	var relog_hotkeys:Dictionary=await _wait_panel(panel,func(v):
		var hotkeys:Dictionary=v.get("hotkeys",{})
		return (
			bool(v.get("hotkeys_ready",false)) and hotkeys.has(0) and
			int((hotkeys[0] as Dictionary).get("data_type",-1))==1 and
			int((hotkeys[0] as Dictionary).get("data_id",0))==SKILL_ID
		),7000)
	if relog_hotkeys.is_empty():
		_fail("relog skill hotkey persistence "+str(panel._latest),panel)
		return

	panel.disconnect_session()
	shell.set_world_active(false)
	if native_os_f1:
		print("CLIENT_HOTKEY_SKILL_NATIVE_PASS os_f1=true staged_once=true server_skill_result=true skill_id=%d damage=40 monster_hp=100_to_60 mp=2_to_1 hotkey_persisted=true relog=true" % SKILL_ID)
	else:
		print("CLIENT_HOTKEY_SKILL_AES_PASS f1_input=true staged_once=true monster_click_handler=true server_skill_result=true skill_id=%d damage=40 monster_hp=100_to_60 mp=2_to_1 hotkey_persisted=true relog=true" % SKILL_ID)
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
