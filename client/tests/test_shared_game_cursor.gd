extends SceneTree
## Cursor images are one shared global layer, not copied per UI skin.
const App = preload("res://app/main.gd")
const Skins = preload("res://ui/ui_skin_catalog.gd")

func _fail(app: Node, reason: String) -> void:
	app.queue_free()
	push_error("SHARED_GAME_CURSOR_FAIL "+reason)
	quit(1)

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var app = App.new()
	app.auto_start = false
	root.add_child(app)
	var cursor = app.cursor_manager
	if cursor._loaded.size() != 9 or cursor._loaded.get("move.png") == null:
		_fail(app,"all nine original shared cursor icons must load without import-cache")
		return
	if app.session_panel.login_button.mouse_default_cursor_shape != Control.CURSOR_POINTING_HAND:
		_fail(app,"clickable login button lacks hand cursor")
		return
	if app.session_panel.username_input.mouse_default_cursor_shape != Control.CURSOR_IBEAM:
		_fail(app,"account input lacks caret cursor")
		return
	var original = cursor._loaded["arrow.png"]
	for skin in Skins.available():
		if not Skins.apply(str(skin.id),app.toolbar,app.session_panel):
			_fail(app,"skin failed "+str(skin.id))
			return
		if cursor._loaded["arrow.png"] != original or cursor._loaded.size() != 9:
			_fail(app,"switching skin swapped shared cursor icons")
			return
	cursor.set_mode("attack")
	if cursor._mode != "attack" or cursor._loaded.get("attack.png") == null:
		_fail(app,"monster attack cursor not selected")
		return
	cursor.set_mode("inspect")
	if cursor._mode != "inspect" or cursor._loaded.get("inspect.png") == null:
		_fail(app,"NPC inspect cursor not selected")
		return
	cursor.set_mode("move")
	if not cursor._locked_visual.visible:
		_fail(app,"1-second latched autorun icon not shown")
		return
	cursor._process(0.1)
	if cursor._locked_visual.rotation <= 0.0:
		_fail(app,"latched autorun icon failed to rotate")
		return
	cursor.restore_pointer()
	if Input.mouse_mode != Input.MOUSE_MODE_VISIBLE:
		_fail(app,"lost-focus/reset hid the native cursor")
		return
	cursor.set_mode("normal")
	if cursor._locked_visual.visible:
		_fail(app,"idle cursor still shows animation")
		return
	app.queue_free()
	await process_frame
	print("SHARED_GAME_CURSOR_PASS nine_icons=true all_four_skins=true clickable_input=true attack_npc_hover=true rotating_autorun=true mouse_restored=true")
	quit(0)
