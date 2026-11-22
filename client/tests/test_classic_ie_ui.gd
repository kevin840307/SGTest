extends SceneTree
## Independent classic IE HK2 source SPR verification; same authenticated
## SessionPanel and no alternative networking, inventory or gameplay state.
const Main = preload("res://app/main.gd")
const Layout = preload("res://ui/session_layout.gd")
const State = preload("res://world/world_state.gd")
const Skins = preload("res://ui/ui_skin_catalog.gd")

func _fail(reason: String) -> void:
	push_error("CLASSIC_IE_UI_FAIL "+reason)
	quit(1)

func _initialize() -> void:
	root.size = Vector2i(1152,620)
	_run.call_deferred()

func _capture(path: String) -> void:
	await process_frame
	await RenderingServer.frame_post_draw
	var error := root.get_texture().get_image().save_png(path)
	if error != OK:
		_fail("native screenshot write failed %d" % error)

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var directory := str(args[0]) if args.size() > 0 else ""
	if not Skins.valid("original_ie_hk2"):
		print("CLASSIC_IE_UI_SKIP original IE HK2 local assets unavailable")
		quit(0)
		return
	var app = Main.new()
	app.auto_start = false
	root.add_child(app)
	var shell = app.classic_auth
	app.session_panel.set_process(false)
	app.session_panel.active = true
	app.session_panel.show()
	app.session_panel._latest = {"state":State.STATE_SELECTING_CHARACTER}
	app.auto_start = true
	app._current_skin = "original_ie_hk2"
	app.session_panel._layout.show_page(Layout.Page.LOGIN)
	app._update_ui_stage(true)
	if not shell.visible or app.recovered2_auth.visible or app.toolbar.visible:
		_fail("classic login did not replace R2 source presentation")
		return
	if shell.login_page.get_child(0).texture == null:
		_fail("original 800x600 ACCOUNT_LOGIN.spr source was not imported")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_login.png"))
	app.session_panel._layout.show_page(Layout.Page.CHARACTER_SELECT)
	app.session_panel._characters = [{"character_id":42,"name":"TestA","slot":0,"appearance":{"race":0,"hair_index":0,"hair_color":0}}]
	app._update_ui_stage(true)
	if not shell.select_page.visible or shell._slots[0].text.find("TestA") < 0:
		_fail("original 3-slot chooser did not reflect source names")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_select.png"))
	app.session_panel._layout.show_page(Layout.Page.CHARACTER_CREATE)
	app._update_ui_stage(true)
	if not shell.create_page.visible or shell._name.placeholder_text != "角色名稱":
		_fail("original creation source layout missing")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_create.png"))
	app.session_panel._layout.show_page(Layout.Page.WORLD)
	app._update_ui_stage(true)
	var world = app.recovered2_world
	if not world.visible or world._profile != "original_ie_hk2" or world.menu.size != Vector2(30,249):
		_fail("original 30x249 COMMAND.spr and world layout not applied")
		return
	world.apply_snapshot({"world_ready":true,"actor_id":42,"map_id":15,"map_epoch":1,
		"x":100.0,"y":200.0,"player_current_hp":75,"player_max_hp":100,
		"player_current_mp":30,"player_max_mp":40,"inventory_ready":true,
		"inventory_revision":1,"inventory_items":{},"equipment_items":{}})
	if world._hp.value != 75 or world._mp.value != 30:
		_fail("classic status bars do not use Server values")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_world.png"))
	world.menu.get_node("Menu1").pressed.emit()
	if not world.inventory.visible:
		_fail("original inventory menu hitbox did not show UI")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_inventory.png"))
	app._recovered2_local_ready = app.recovered2_auth.attach(app.session_panel)
	app._current_skin = "recovered2_local"
	app._update_ui_stage(true)
	if world._profile != "recovered2_local" or not world.visible or app.session_panel.session == null:
		_fail("switching classic to Recovered2 discarded game UI")
		return
	app.session_panel.active = false
	app._update_ui_stage(true)
	app.queue_free()
	await process_frame
	print("CLASSIC_IE_UI_PASS 800x600_original_spr=true main_session_shared=true login_select_create_world=true inventory_hitbox=true runtime_switch=true")
	quit(0)
