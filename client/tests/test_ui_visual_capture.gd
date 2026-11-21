extends SceneTree
## Local-only visual QA. No network, asset PCK or private UI image required.
const Main = preload("res://app/main.gd")
const State = preload("res://world/world_state.gd")
const Skins = preload("res://ui/ui_skin_catalog.gd")

func _initialize() -> void:
	root.size = Vector2i(1280,720)
	_run.call_deferred()

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 1:
		quit(2)
		return
	var output := args[0]
	DirAccess.make_dir_recursive_absolute(output)
	var app = Main.new()
	app.auto_start = false
	root.add_child(app)
	app.auto_start = true
	var panel = app.session_panel
	panel.show()
	# Freeze the disconnected ClientSession during the visual-only capture.
	# Live login/UI/TLS tests exercise the real reducer separately.
	panel.set_process(false)
	var steps := [
		{"skin":"retro_terminal","page":"login"},
		{"skin":"retro_terminal","page":"select"},
		{"skin":"retro_terminal","page":"create"},
		{"skin":"retro_terminal","page":"world"},
		{"skin":"celestial","page":"login"},
		{"skin":"celestial","page":"select"},
		{"skin":"celestial","page":"create"},
		{"skin":"celestial","page":"world"},
		{"skin":"amber","page":"create"},
		{"skin":"tactical","page":"world"},
	]
	for spec in steps:
		var style := str(spec.skin)
		if not Skins.apply(style,app.toolbar,panel):
			push_error("visual skin switch failed")
			quit(1)
			return
		app._current_skin = style
		panel._characters = [{"character_id":1,"name":"Sample Hero","slot":0}]
		panel.characters.clear()
		panel.characters.add_item("Sample Hero")
		panel._show_create = false
		match str(spec.page):
			"login":
				panel.active = false
				panel._latest = {}
			"select":
				panel.active = true
				panel._latest = {"state":State.STATE_SELECTING_CHARACTER}
			"create":
				panel.active = true
				panel._latest = {"state":State.STATE_SELECTING_CHARACTER}
				panel._show_create = true
			"world":
				panel.active = true
				panel._latest = {"state":State.STATE_IN_WORLD,"world_ready":true,"actor_id":1,"map_id":15,"map_epoch":1,
					"player_max_hp":1000,"player_current_hp":720,"player_max_mp":500,"player_current_mp":420}
				panel.gameplay_hud.apply_snapshot(panel._latest)
		panel._set_controls()
		app._update_ui_stage(true)
		await process_frame
		await RenderingServer.frame_post_draw
		var file: String = output.path_join(str(spec.skin)+"_"+str(spec.page)+".png")
		if root.get_texture().get_image().save_png(file) != OK:
			push_error("visual capture failed " + file)
			quit(1)
			return
		print("UI_VISUAL_CAPTURE ",file)
	app.queue_free()
	await process_frame
	quit(0)
