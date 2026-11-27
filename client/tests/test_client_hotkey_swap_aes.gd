extends SceneTree

const SessionPanel = preload("res://session/session_panel.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"
const SKILL_ID := 28101

func _fail(message:String,panel=null)->void:
	if panel!=null:
		panel.disconnect_session()
	push_error("CLIENT_HOTKEY_SWAP_AES_FAIL "+message)
	quit(1)

func _initialize()->void:
	var args:=OS.get_cmdline_user_args()
	if args.size()!=1 or not str(args[0]).is_valid_int():
		_fail("expected port")
		return
	var panel=SessionPanel.new()
	root.add_child(panel)
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
		var items:Dictionary=v.get("inventory_items",{})
		return (
			_world_ready(v) and bool(v.get("skill_book_ready",false)) and
			bool(v.get("inventory_ready",false)) and not items.is_empty() and
			int(book.get(SKILL_ID,book.get(str(SKILL_ID),0)))==3
		),9000)
	if ready.is_empty():
		_fail("first world baseline "+str(panel._latest),panel)
		return
	if not panel.submit_hotkey_refresh():
		_fail("initial hotkey refresh refused",panel)
		return
	ready=await _wait_panel(panel,func(v):
		return (
			bool(v.get("hotkeys_ready",false)) and
			int(v.get("pending_hotkey_sequence",0))==0
		)
		,7000)
	if ready.is_empty():
		_fail("initial hotkey refresh "+str(panel._latest),panel)
		return
	var items:Dictionary=ready.get("inventory_items",{})
	var item_uid:=int(items.keys()[0])
	if item_uid<=0:
		_fail("invalid fixture item uid "+str(items),panel)
		return

	if not panel.submit_hotkey_bind(0,1,SKILL_ID):
		_fail("skill bind refused",panel)
		return
	var first:Dictionary=await _wait_panel(panel,func(v):
		var h:Dictionary=v.get("hotkeys",{})
		return int(v.get("pending_hotkey_sequence",0))==0 and h.has(0) and 			int((h[0] as Dictionary).get("data_type",-1))==1 and 			int((h[0] as Dictionary).get("data_id",0))==SKILL_ID
		,7000)
	if first.is_empty():
		_fail("skill bind snapshot "+str(panel._latest),panel)
		return

	if not panel.submit_hotkey_bind(1,0,item_uid):
		_fail("item bind refused",panel)
		return
	var both:Dictionary=await _wait_panel(panel,func(v):
		var h:Dictionary=v.get("hotkeys",{})
		return int(v.get("pending_hotkey_sequence",0))==0 and h.has(0) and h.has(1) and 			int((h[0] as Dictionary).get("data_type",-1))==1 and 			int((h[0] as Dictionary).get("data_id",0))==SKILL_ID and 			int((h[1] as Dictionary).get("data_type",-1))==0 and 			int((h[1] as Dictionary).get("data_id",0))==item_uid
		,7000)
	if both.is_empty():
		_fail("two-slot bind snapshot "+str(panel._latest),panel)
		return

	if not panel.submit_hotkey_swap(0,1):
		_fail("swap refused",panel)
		return
	var swapped:Dictionary=await _wait_panel(panel,func(v):
		var h:Dictionary=v.get("hotkeys",{})
		return int(v.get("pending_hotkey_sequence",0))==0 and h.has(0) and h.has(1) and 			int((h[0] as Dictionary).get("data_type",-1))==0 and 			int((h[0] as Dictionary).get("data_id",0))==item_uid and 			int((h[1] as Dictionary).get("data_type",-1))==1 and 			int((h[1] as Dictionary).get("data_id",0))==SKILL_ID
		,7000)
	if swapped.is_empty():
		_fail("authoritative swap snapshot "+str(panel._latest),panel)
		return
	print("CLIENT_HOTKEY_SWAP_AES_APPLIED source=0 destination=1 slot0=item slot1=skill")

	panel.disconnect_session()
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
	var relog_ready:Dictionary=await _wait_panel(panel,func(v):
		return _world_ready(v),9000)
	if relog_ready.is_empty():
		_fail("relog world "+str(panel._latest),panel)
		return
	if not panel.submit_hotkey_refresh():
		_fail("relog hotkey refresh refused",panel)
		return
	var durable:Dictionary=await _wait_panel(panel,func(v):
		var h:Dictionary=v.get("hotkeys",{})
		return (
			bool(v.get("hotkeys_ready",false)) and
			int(v.get("pending_hotkey_sequence",0))==0 and h.has(0) and h.has(1) and
			int((h[0] as Dictionary).get("data_type",-1))==0 and
			int((h[0] as Dictionary).get("data_id",0))==item_uid and
			int((h[1] as Dictionary).get("data_type",-1))==1 and
			int((h[1] as Dictionary).get("data_id",0))==SKILL_ID
		)
		,7000)
	if durable.is_empty():
		_fail("swap not durable after relog "+str(panel._latest),panel)
		return

	panel.disconnect_session()
	print("CLIENT_HOTKEY_SWAP_AES_PASS client_intent=true aes=true server_atomic=true slot0=item slot1=skill relog=true")
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
