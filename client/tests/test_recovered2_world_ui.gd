extends SceneTree
## Standalone Windows screenshot and source-image/icon contract for the
## Recovered2 world HUD. This never opens a network connection.
## Private appearance files are optional on CI and are not distributed.
const App = preload("res://app/main.gd")
const SkinCatalog = preload("res://ui/ui_skin_catalog.gd")
const Layout = preload("res://ui/session_layout.gd")

func _initialize() -> void:
	root.size = Vector2i(1152,620)
	_run.call_deferred()

func _fail(reason: String) -> void:
	push_error("RECOVERED2_WORLD_UI_FAIL "+reason)
	quit(1)

func _capture(file: String) -> void:
	if OS.get_cmdline_user_args().is_empty() or DisplayServer.get_name() == "headless":
		return
	await process_frame
	await RenderingServer.frame_post_draw
	if root.get_texture().get_image().save_png(OS.get_cmdline_user_args()[0].path_join(file+".png")) != OK:
		_fail("capture failed: "+file)
	else:
		print("RECOVERED2_WORLD_CAPTURE "+file)

func _run() -> void:
	if not SkinCatalog.valid("recovered2_local"):
		print("RECOVERED2_WORLD_UI_SKIP private UI assets missing")
		quit(0)
		return
	var args := OS.get_cmdline_user_args()
	if args.size() > 0:
		DirAccess.make_dir_recursive_absolute(args[0])
	var app := App.new()
	app.auto_start = false
	app.preview_auth_offline = true
	root.add_child(app)
	# Local Windows can verify the REAL original Map15 texture pipeline beneath
	# the visual shell. CI omits the private pack and still checks controls.
	if args.size() > 1:
		app.world_view.show_players = true
		if app.world_view.open_pack(str(args[1])) != OK or app.world_view.request_map(15) != OK:
			_fail("private map assets cannot mount")
			return
		var deadline := Time.get_ticks_msec()+30000
		while app.world_view.phase in ["map","palette"] and Time.get_ticks_msec() < deadline:
			await process_frame
		if app.world_view.phase != "ready":
			_fail("Map15 original textures not ready")
			return
	# Emulate the UI phase of a successfully entered world without opening
	# a TLS connection. This exercises the real Main visibility policy,
	# rather than a manually visible shell layered over debug toolbar.
	app.set_process(false)
	app.session_panel.set_process(false)
	app.auto_start = true
	app._current_skin = "recovered2_local"
	app.session_panel.visible = true
	app.session_panel.active = true
	app.session_panel._layout.show_page(Layout.Page.WORLD)
	app._update_ui_stage(true)
	var shell = app.recovered2_world
	shell.resize_view(Vector2(root.size))
	var fixtures := {
		"world_ready":true,"actor_id":101,"map_epoch":2,"map_id":15,
		"x":6674,"y":4802,"player_current_hp":720,"player_max_hp":1000,
		"player_current_mp":210,"player_max_mp":500,"player_stamina":90,
		"player_level":22,"wallet":105500,"inventory_ready":true,"equipment_ready":true,
		"inventory_revision":12,"equipment_revision":12,
		"inventory_items":{
			10001:{"item_uid":10001,"item_definition_id":1001,"quantity":12,"enhancement_level":0},
			10002:{"item_uid":10002,"item_definition_id":2000,"quantity":3,"enhancement_level":0},
			10003:{"item_uid":10003,"item_definition_id":30001,"quantity":1,"enhancement_level":3},
			10004:{"item_uid":10004,"item_definition_id":15001,"quantity":1,"enhancement_level":0},
		},
		"equipment_items":{
			10005:{"item_uid":10005,"item_definition_id":30002,"quantity":1,"enhancement_level":2},
		},
	}
	shell.apply_snapshot(fixtures)
	if not shell.visible or shell._hp.value != 720 or shell._mp.value != 210 or not shell.stage.visible:
		_fail("basic HUD HP/MP from snapshot not presented")
		return
	if shell.menu.size.x != 50 or shell.menu.get_node_or_null("Menu0") == null:
		_fail("Recovered2 right menu layout/buttons missing")
		return
	if app.toolbar.visible or app._map_buttons.any(func(button): return button.visible):
		_fail("development toolbar Map13/Map15 still visible after world entry")
		return
	await _capture("hud")
	shell._menu_action(1)
	if not shell.inventory.visible or shell.character.visible:
		_fail("inventory menu did not open independent inventory window")
		return
	shell.inventory._change_tab(0)
	shell.inventory.apply_snapshot(fixtures)
	if shell.inventory._rows.get_child_count() != 2:
		_fail("recovery and general items missing in first inventory tab")
		return
	var first = shell.inventory._rows.get_child(0)
	var icon = first.find_child("Photo",true,false)
	if icon == null:
		_fail("inventory item image widget missing")
		return
	if FileAccess.file_exists("res://ui/private_recovered2/item_photos/Photo0398.png") and icon.texture == null:
		_fail("known recovery item should render Recovered2 Photo0398.png")
		return
	await _capture("inventory")
	# A 130-slot inventory must never instantiate 130 Control/TextureRects
	# on a single UI refresh. Only the visible 40 rows are mounted.
	var bulk := fixtures.duplicate(true)
	bulk.inventory_revision = 13
	bulk.inventory_items = {}
	for i in 130:
		var uid := 20000+i
		bulk.inventory_items[uid] = {"item_uid":uid,"item_definition_id":1001,"quantity":1}
	shell.inventory.apply_snapshot(bulk)
	if shell.inventory._rows.get_child_count() != shell.inventory.PAGE_SIZE or shell.inventory._page_next.disabled:
		_fail("unbounded inventory rows or pagination missing at 130 items")
		return
	shell.inventory._navigate_page(1)
	if shell.inventory._rows.get_child_count() != shell.inventory.PAGE_SIZE or shell.inventory._page != 1:
		_fail("inventory page two should render at most the visible page")
		return
	shell.inventory._navigate_page(10)
	if shell.inventory._rows.get_child_count() != 10 or not shell.inventory._page_next.disabled:
		_fail("inventory final page not clamped to remaining ten items")
		return
	shell.inventory.apply_snapshot(fixtures)
	shell.inventory._change_tab(1)
	if shell.inventory._rows.get_child_count() != 3:
		_fail("weapon/equipment tab should include owned and equipped items")
		return
	shell._menu_action(0)
	if not shell.character.visible or shell.inventory.visible:
		_fail("character window and inventory are not mutually exclusive")
		return
	await _capture("character")
	var aux_cases := [
		[2,"skill",shell.skill_window],
		[3,"party",shell.party_window],
		[4,"clan",shell.clan_window],
		[5,"shop",shell.shop_window],
		[6,"quest",shell.quest_window],
		[7,"mall",shell.mall_window],
		[8,"settings",shell.settings_window],
	]
	for row in aux_cases:
		shell._menu_action(int(row[0]))
		var window: Control = row[2]
		if not window.visible or shell.inventory.visible or shell.character.visible:
			_fail("%s menu did not open its independent Recovered2 window" % str(row[1]))
			return
		await _capture(str(row[1]))
	var unavailable = shell.clan_window.find_child("ClanUnavailable",true,false)
	if not unavailable is Label or not (unavailable as Label).text.contains("沒有戰團／社團協定"):
		_fail("clan window must remain honest while SGTest has no clan protocol")
		return
	var settings_picker = shell.settings_window.find_child("UiProfilePickerSettings",true,false)
	if not settings_picker is OptionButton or (settings_picker as OptionButton).item_count < 2:
		_fail("settings window must expose switchable UI profiles")
		return
	shell.set_world_active(false)
	if shell.visible or shell.inventory.visible or shell.character.visible or shell._aux_windows.any(func(window): return window.visible):
		_fail("world exit should close all persistent floating windows")
		return
	app.queue_free()
	await process_frame
	print("RECOVERED2_WORLD_UI_PASS hud=true right_menu=true inventory_icons=true equipment_tab=true aux_windows=true skins_switchable=true")
	quit(0)
