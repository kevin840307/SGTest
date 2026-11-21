extends SceneTree

## Optional local-only Recovered2 appearance QA. CI skips if private assets
## are not provisioned. Functional SGTest session commands are stubbed.
const Main = preload("res://app/main.gd")
const State = preload("res://world/world_state.gd")
const UiSkinCatalog = preload("res://ui/ui_skin_catalog.gd")
const Layout = preload("res://ui/session_layout.gd")

class SessionStub:
	extends RefCounted
	var logins := 0
	var creates := 0
	var enters := 0
	var last_name := ""
	var last_slot := -1
	var last_host := ""
	var last_port := 0
	func connect_secure(host, port, _user, _password, _tls_name, _trust) -> int:
		last_host = str(host)
		last_port = int(port)
		logins += 1
		return OK
	func register_secure(_host, _port, _user, _password, _tls_name, _trust) -> int:
		logins += 1
		return OK
	func create_character(slot: int, name: String) -> bool:
		creates += 1
		last_name = name
		last_slot = slot
		return true
	func choose_character(id: int) -> bool:
		enters += 1
		return id > 0
	func stop() -> void:
		pass
	func snapshot_since(_version: int) -> Dictionary:
		return {}

func _initialize() -> void:
	# Constrained 1280x720 desktop: allow OS title bar/taskbar margins.
	root.size = Vector2i(1152,620)
	_run.call_deferred()

func _error(message: String) -> void:
	push_error("RECOVERED2_LOCAL_UI_FAIL "+message)
	quit(1)

func _capture(name: String, dir: String) -> void:
	await process_frame
	# A headless CI renderer has no GPU draw completion signal. Functional
	# assertions still run; only capture requires a real Windows renderer.
	if DisplayServer.get_name() == "headless":
		print("RECOVERED2_CAPTURE_SKIPPED_HEADLESS "+name)
		return
	await RenderingServer.frame_post_draw
	var image: Image = root.get_texture().get_image()
	if image.save_png(dir.path_join(name+".png")) != OK:
		_error("capture failed "+name)
	else:
		print("RECOVERED2_SCREEN "+name+" "+str(root.size))

func _run() -> void:
	if not UiSkinCatalog.valid("recovered2_local"):
		print("RECOVERED2_LOCAL_UI_SKIP private Recovered2 assets unavailable")
		quit(0)
		return
	var args := OS.get_cmdline_user_args()
	var directory := "user://recovered2_ui_captures"
	if args.size() > 0:
		directory = args[0]
	DirAccess.make_dir_recursive_absolute(directory)
	var app = Main.new()
	app.auto_start = false
	app.preview_auth_offline = true
	root.add_child(app)
	var shell = app.recovered2_auth
	if not app._recovered2_local_ready:
		_error("private source theme failed to load")
		return
	var picker_index := -1
	for i in app.skin_picker.item_count:
		if str(app.skin_picker.get_item_metadata(i)) == "recovered2_local":
			picker_index = i
	if picker_index < 0:
		_error("local skin not selectable")
		return
	app._skin_selected(picker_index)
	app.auto_start = true
	var panel = app.session_panel
	panel.show()
	panel.set_process(false)
	var stub = SessionStub.new()
	panel.session = stub
	panel.active = false
	panel.server_host = "127.0.0.1"
	panel.server_port = 7778
	panel._set_controls()
	app._update_ui_stage(true)
	# Host and port are internal deployment inputs, not editable UI.
	var credentials_grid: Node = panel.username_input.get_parent()
	if credentials_grid == null or panel.password_input.get_parent() != credentials_grid or credentials_grid.get_child_count() != 4:
		_error("deployment host/port unexpectedly rendered in base UI")
		return
	if shell._login.find_children("*","LineEdit",true,false).size() != 2 or not shell._login.find_children("*","SpinBox",true,false).is_empty():
		_error("Recovered2 login exposed more than account/password")
		return
	if shell._stage.position.x < -0.1 or shell._stage.position.y < -0.1 or shell._stage.position.x + shell._stage.size.x * shell._stage.scale.x > root.size.x + 1.0 or shell._stage.position.y + shell._stage.size.y * shell._stage.scale.y > root.size.y + 1.0:
		_error("Recovered2 1200x900 UI clipped in 1152x620 desktop client")
		return
	if not shell.visible or shell._page_index != Layout.Page.LOGIN:
		_error("login scene did not mount")
		return
	if shell._theme == null or shell._video.stream == null:
		_error("source Recovered2 theme/video missing")
		return
	await _capture("login",directory)
	# Loading-bar screenshot uses a deterministic mocked asset counter;
	# the gameplay MapEpoch/resource fixture exercises the real pipeline.
	# Freeze the app's real-world loading sampler while capturing a manual
	# preview over the login scene; otherwise it hides an intentionally
	# synthetic loading state on its next idle tick.
	app.set_process(false)
	app.loading_overlay.update_state("palette",15,12,32,2,1,true)
	if not app.loading_overlay.visible or app.loading_overlay._bar_art.texture == null:
		_error("Recovered2 loading artwork was not mounted")
		return
	await _capture("loading_preview",directory)
	app.loading_overlay.update_state("ready",15,32,32,0,0,true)
	if app.loading_overlay.visible:
		_error("loading overlay did not hide on world completion")
		return
	app.set_process(true)
	shell._account.text = "testexample"
	shell._secret.text = "password12345"
	# Invalid deployment endpoint must be rejected internally, not exposed
	# as a client UI control or sent to a transport.
	panel.server_port = 0
	shell._login_button.pressed.emit()
	if stub.logins != 0 or panel.active or not panel.status.text.contains("連線設定無效"):
		_error("invalid hidden server port was accepted")
		return
	panel.server_port = 7778
	shell._secret.text = "password12345"
	shell._login_button.pressed.emit()
	if stub.logins != 1 or panel.username_input.text != "testexample" or stub.last_host != "127.0.0.1" or stub.last_port != 7778:
		_error("login button did not delegate to SGTest SessionPanel")
		return
	panel.active = true
	panel._characters = [
		{"character_id":10,"slot":0,"name":"Demo A"},
		{"character_id":11,"slot":1,"name":"Demo B"},
	]
	panel.characters.clear()
	for character in panel._characters:
		panel.characters.add_item(str(character.name))
	panel._latest = {"state":State.STATE_SELECTING_CHARACTER}
	panel._set_controls()
	app._update_ui_stage(true)
	if not shell._selection.visible or shell._slot_actions[0].text != "進入世界":
		_error("3-column selection did not display account characters")
		return
	await _capture("select",directory)
	shell._slot_actions[1].pressed.emit()
	if stub.enters != 1:
		_error("character selection did not submit existing choose intent")
		return
	panel._chosen = false
	panel._show_create = true
	panel._set_controls()
	app._update_ui_stage(true)
	if not shell._creation.visible:
		_error("source create image not shown")
		return
	await _capture("create",directory)
	shell._name_edit.text = "NewHero"
	shell._slot_edit.value = 2
	shell._confirm_create.pressed.emit()
	if stub.creates != 1 or stub.last_name != "NewHero" or stub.last_slot != 2:
		_error("creation did not delegate to SGTest authorizing session")
		return
	if app._current_skin != "recovered2_local":
		_error("skin selection was not retained")
		return
	# In-screen selector must safely switch to another UI and back without
	# replacing SessionPanel or recreating its active ClientSession.
	var original_session = panel.session
	var target := -1
	for i in shell._skin_picker.item_count:
		if str(shell._skin_picker.get_item_metadata(i)) == "retro_terminal":
			target = i
	if target < 0:
		_error("other installed UI skin missing from Recovered2 selector")
		return
	shell._skin_picker.item_selected.emit(target)
	if app._current_skin != "retro_terminal" or panel.session != original_session:
		_error("switching skins rebuilt gameplay session")
		return
	app._select_recovered2_skin("recovered2_local")
	if app._current_skin != "recovered2_local" or panel.session != original_session:
		_error("cannot restore Recovered2 skin without losing session")
		return
	print("RECOVERED2_LOCAL_UI_PASS login_select_create=true hidden_deployment_host_port=true original_video_theme_pngs=true viewport_fit_1152x620=true single_session_delegation=true")
	app.queue_free()
	await process_frame
	quit(0)
