extends SceneTree
## Formal main.tscn gate for the Scheduler v20 right-side backpack player flow.
const Main = preload("res://app/main.gd")
const MainScene = preload("res://app/main.tscn")
const WorldState = preload("res://world/world_state.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"
const PROOF := "2468"

var _app: Main

func _initialize() -> void:
	_run.call_deferred()

func _fail(message:String) -> void:
	if _app != null:
		_app.session_panel.disconnect_session()
	push_error("ONLINE_INVENTORY_MAIN_GATE_FAIL " + message)
	quit(1)

func _run() -> void:
	root.size = Vector2i(800,600)
	var port := _arg_int("--port")
	if port <= 0:
		_fail("missing --port")
		return
	_app = MainScene.instantiate() as Main
	root.add_child(_app)
	current_scene = _app
	await process_frame
	_app.session_panel.server_host = "127.0.0.1"
	_app.session_panel.server_port = port
	if not await _login_and_choose():
		return
	var ready := await _wait_snapshot(func(v):
		return (
			_world_ready(v) and int(v.get("player_current_hp",0)) == 60 and
			int(v.get("player_max_hp",0)) == 100 and
			(v.get("inventory_items",{}) as Dictionary).size() == 1
		), 12000)
	if ready.is_empty():
		_fail("initial world/inventory " + str(_app._last_snapshot))
		return
	if not await _wait_main_surface(8000):
		_fail(
			"formal main world/classic shell not visible world_visible=%s phase=%s classic_visible=%s page=%s skin=%s" % [
				str(_app.world_view.visible), _app.world_view.phase, str(_app.classic_world.visible),
				str(_app.session_panel._layout.current_page), str(_app._current_skin)
			]
		)
		return
	var items:Dictionary = ready.get("inventory_items",{})
	var uid := int(items.keys()[0])
	var item:Dictionary = items[uid]
	if uid <= 0 or int(item.get("item_definition_id",0)) != 1001 or int(item.get("quantity",0)) != 2:
		_fail("unexpected inventory " + str(items))
		return
	var shell = _app.classic_world
	var item_command := shell.secondary_menu.get_node_or_null("ItemCommand") as TextureButton
	if item_command == null or shell.inventory.visible:
		_fail("021_COMMAND / initial 023_ITEM state")
		return
	item_command.pressed.emit()
	await process_frame
	if not shell.inventory.visible or shell._inventory_rows.get_child_count() != 1:
		_fail("021_COMMAND did not open populated 023_ITEM")
		return
	var row := shell._inventory_rows.get_child(0) as Button
	if row == null or not row.text.contains("×2"):
		_fail("023_ITEM baseline row")
		return
	row.pressed.emit()
	var used := await _wait_snapshot(func(v):
		var current:Dictionary = v.get("inventory_items",{})
		return (
			int(v.get("last_recovery_result_sequence",0)) == 1 and
			int(v.get("last_recovery_result_status",-1)) == 0 and
			int(v.get("pending_recovery_sequence",0)) == 0 and
			int(v.get("player_current_hp",0)) == 90 and current.has(uid) and
			int((current[uid] as Dictionary).get("quantity",0)) == 1
		), 12000)
	if used.is_empty():
		_fail("authoritative recovery " + str(_app._last_snapshot))
		return
	await process_frame
	if (
		shell._hp_label.text != "90/100" or shell._inventory_rows.get_child_count() != 1 or
		not str((shell._inventory_rows.get_child(0) as Button).text).contains("×1")
	):
		_fail("main scene did not render committed HP/inventory")
		return
	print("ONLINE_INVENTORY_MAIN_GATE_USE_PASS command_021=true item_023=true hp=60_to_90 quantity=2_to_1")

	_app.session_panel.disconnect_session()
	await process_frame
	if _app.session_panel.active or not _app.session_panel._latest.is_empty():
		_fail("disconnect did not settle")
		return
	if not await _login_and_choose():
		return
	var relog := await _wait_snapshot(func(v):
		var current:Dictionary = v.get("inventory_items",{})
		return (
			_world_ready(v) and int(v.get("player_current_hp",0)) == 90 and
			current.has(uid) and int((current[uid] as Dictionary).get("quantity",0)) == 1
		), 12000)
	if relog.is_empty():
		_fail("relog persistence " + str(_app._last_snapshot))
		return
	item_command = shell.secondary_menu.get_node_or_null("ItemCommand") as TextureButton
	if item_command == null:
		_fail("relog ItemCommand missing")
		return
	if not shell.inventory.visible:
		item_command.pressed.emit()
		await process_frame
	if (
		shell._hp_label.text != "90/100" or shell._inventory_rows.get_child_count() != 1 or
		not str((shell._inventory_rows.get_child(0) as Button).text).contains("×1")
	):
		_fail("relog main scene presentation")
		return
	print("ONLINE_INVENTORY_MAIN_GATE_PASS formal_main_scene=true command_021=true item_023=true server_authority=true sqlite_relog=true native_visible=", not DisplayServer.get_name().contains("headless"))
	_app.session_panel.disconnect_session()
	quit(0)

func _login_and_choose() -> bool:
	var panel = _app.session_panel
	panel.username_input.text = USERNAME
	panel.password_input.text = PASSWORD
	panel.login()
	var selecting := await _wait_panel(func(v):
		return (
			int(v.get("state",-1)) == WorldState.STATE_SELECTING_CHARACTER and
			(v.get("characters",[]) as Array).size() == 1
		), 8000)
	if selecting.is_empty():
		_fail(
			"character list latest=%s panel_latest=%s active=%s status=%s endpoint=%s:%d raw=%s" % [
				str(_app._last_snapshot), str(panel._latest), str(panel.active),
				panel.status.text, panel.server_host, panel.server_port, str(panel.session.snapshot())
			]
		)
		return false
	var character_id := int((selecting.get("characters",[]) as Array)[0].character_id)
	var before := int(selecting.get("character_manage_result_revision",0))
	if not panel.change_character_password(character_id,"",PROOF):
		_fail("character proof request refused")
		return false
	var managed := await _wait_panel(func(v):
		return (
			int(v.get("state",-1)) == WorldState.STATE_SELECTING_CHARACTER and
			int(v.get("character_manage_result_revision",0)) > before and
			int(v.get("character_manage_status",-1)) in [0,2]
		), 8000)
	if managed.is_empty():
		_fail("character proof settlement " + str(_app._last_snapshot))
		return false
	panel.characters.select(0)
	panel.character_choose_password_input.text = PROOF
	panel.choose()
	return true

func _world_ready(v:Dictionary) -> bool:
	return (
		int(v.get("state",-1)) == WorldState.STATE_IN_WORLD and
		bool(v.get("world_ready",false)) and bool(v.get("inventory_ready",false)) and
		bool(v.get("combat_available",false)) and
		int(v.get("pending_world_ready_ack_epoch",0)) == 0 and
		int(v.get("last_world_ready_ack_epoch",0)) == int(v.get("map_epoch",0))
	)

func _wait_snapshot(predicate:Callable, timeout_ms:int) -> Dictionary:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var value:Dictionary = _app._last_snapshot.duplicate(true)
		if not value.is_empty() and predicate.call(value):
			return value
		if int(value.get("state",-1)) == WorldState.STATE_FAILED and not bool(value.get("reconnecting",false)):
			return {}
		await process_frame
	return {}

func _wait_panel(predicate:Callable, timeout_ms:int) -> Dictionary:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var value:Dictionary = _app.session_panel._latest.duplicate(true)
		if not value.is_empty() and predicate.call(value):
			return value
		if not _app.session_panel.active and _app.session_panel.status.text != "連線中…":
			return {}
		await process_frame
	return {}

func _wait_main_surface(timeout_ms:int) -> bool:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		if _app.world_view.visible and _app.world_view.phase == "ready" and _app.classic_world.visible:
			return true
		await process_frame
	return false

func _arg_int(name:String) -> int:
	var args := OS.get_cmdline_user_args()
	for i in range(args.size() - 1):
		if args[i] == name and str(args[i + 1]).is_valid_int():
			return int(args[i + 1])
	return 0
