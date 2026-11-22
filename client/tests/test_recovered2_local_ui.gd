extends SceneTree

## Optional local-only Recovered2 appearance QA. CI skips if private assets
## are not provisioned. Functional SGTest session commands are stubbed.
const Main = preload("res://app/main.gd")
const State = preload("res://world/world_state.gd")
const UiSkinCatalog = preload("res://ui/ui_skin_catalog.gd")
const Layout = preload("res://ui/session_layout.gd")
const AppearanceProfile = preload("res://player/character_appearance_profile.gd")

class SessionStub:
	extends RefCounted
	var logins := 0
	var creates := 0
	var enters := 0
	var last_name := ""
	var last_slot := -1
	var last_appearance: Dictionary = {}
	var last_creation: Dictionary = {}
	var last_host := ""
	var last_port := 0
	func connect_secure(host, port, _user, _password, ) -> int:
		last_host = str(host)
		last_port = int(port)
		logins += 1
		return OK
	func register_secure(_host, _port, _user, _password, ) -> int:
		logins += 1
		return OK
	func create_character(
			slot: int, name: String, appearance: Dictionary = {},
			creation: Dictionary = {}) -> bool:
		creates += 1
		last_name = name
		last_slot = slot
		last_appearance = appearance.duplicate()
		last_creation = creation.duplicate(true)
		return true
	func choose_character(id: int, _character_password: String = "") -> bool:
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

func _wait_previews(shell: Control, count: int, creation := false) -> bool:
	var deadline := Time.get_ticks_msec()+10000
	var ready := false
	while Time.get_ticks_msec() < deadline:
		await process_frame
		ready = true
		for i in count:
			var index := 3 if creation else i
			var preview: Control = shell._character_previews[index]
			if preview.status != "ready" or preview._canvas._commands.is_empty():
				ready = false
		if ready:
			break
	if not ready:
		var states: Array[String] = []
		for i in count:
			var index := 3 if creation else i
			var preview: Control = shell._character_previews[index]
			states.append(
				"%d:%s epoch=%d assets=%d commands=%d" % [
					index,preview.status,preview._epoch,
					preview._assets.size(),preview._canvas._commands.size()])
		_error(
			"actual character preview atlas/animation did not become ready: "+
			", ".join(states))
	return ready

func _run() -> void:
	if not UiSkinCatalog.valid("recovered2_local"):
		print("RECOVERED2_LOCAL_UI_SKIP private Recovered2 assets unavailable")
		quit(0)
		return
	var args := OS.get_cmdline_user_args()
	if args.has("base-resolution"):
		root.size = Vector2i(1400,900)
	for arg in args:
		var value := str(arg)
		if value.begins_with("viewport="):
			var parts := value.trim_prefix("viewport=").split("x")
			if parts.size() == 2 and parts[0].is_valid_int() and parts[1].is_valid_int():
				root.size = Vector2i(int(parts[0]),int(parts[1]))
	var directory := "user://recovered2_ui_captures"
	var pack_path := ""
	for arg in args:
		var value := str(arg)
		if value.ends_with(".pck"):
			pack_path = value
		elif not value.begins_with("viewport=") and value not in ["base-resolution","static-login"] and directory == "user://recovered2_ui_captures":
			directory = value
	DirAccess.make_dir_recursive_absolute(directory)
	var app = Main.new()
	app.auto_start = false
	app.preview_auth_offline = true
	root.add_child(app)
	var shell = app.recovered2_auth
	if not pack_path.is_empty() and app.world_view.open_pack(pack_path) != OK:
		_error("optional character preview PCK could not be mounted")
		return
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
	if shell._login_window.size!=Vector2(280,320) or shell._login_window.position!=Vector2(560,290) or not is_equal_approx(shell._login_content.scale.x,maxf(0.05,minf(root.size.x/1400.0,root.size.y/900.0))) or shell._account.position!=Vector2(25,59) or shell._account.size!=Vector2(230,50) or shell._secret.position!=Vector2(25,121) or shell._login_button.position!=Vector2(77.5,188):
		_error("LoginScreen source anchors/geometry changed")
		return
	var account_normal = shell._account.get_theme_stylebox("normal")
	var account_focus = shell._account.get_theme_stylebox("focus")
	if not account_normal is StyleBoxFlat or not account_focus is StyleBoxFlat or 			not is_equal_approx(account_normal.bg_color.a,0.72) or 			not is_equal_approx(account_focus.bg_color.a,0.84):
		_error("login account/password background translucency changed")
		return
	if args.has("static-login"):
		shell._video.stop()
		shell._video.hide()
	await _capture("login",directory)
	if args.has("static-login"):
		shell._video.show()
		shell._video.play()
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
	for i in 2:
		var preview_label: Label = shell._slot_previews[i]
		if not preview_label.text.is_empty():
			_error("existing character card leaked internal presentation diagnostics")
			return
	var nav_left: float = shell._select_prev.position.x
	var nav_right: float = shell._select_next.position.x+shell._select_next.size.x
	var gap_left: float = shell._select_back.position.x-(
		shell._select_prev.position.x+shell._select_prev.size.x)
	var gap_right: float = shell._select_next.position.x-(
		shell._select_back.position.x+shell._select_back.size.x)
	if not is_equal_approx((nav_left+nav_right)*0.5,600.0) or 			not is_equal_approx(gap_left,gap_right):
		_error("previous/login/next navigation group is not centered symmetrically")
		return
	if not pack_path.is_empty():
		if not await _wait_previews(shell,2):
			return
		var first: Control = shell._character_previews[0]
		var second: Control = shell._character_previews[1]
		if first._assets["equipment/b/cloth/body00"].pages[0] != second._assets["equipment/b/cloth/body00"].pages[0]:
			_error("character cards duplicated a shared atlas texture")
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
	if not pack_path.is_empty():
		if not await _wait_previews(shell,1,true):
			return
		for i in 3:
			if shell._character_previews[i]._epoch > 0 or shell._character_previews[i].is_processing():
				_error("hidden selection card retained a scope or animation loop")
				return
	await _capture("create",directory)
	if not shell._confirm_create.disabled:
		_error("creation must stay disabled until Server appearance rules and full creation settings exist")
		return
	panel.character_create_status.text = ""
	panel._latest.character_create_result_revision = 1
	panel._latest.character_creation_appearances = [
		{"race":0,"hair_index":0,"hair_color":0,"body_index":0,"leg_index":0},
		{"race":0,"hair_index":1,"hair_color":2,"body_index":0,"leg_index":0},
		{"race":0,"hair_index":1,"hair_color":5,"body_index":0,"leg_index":0},
		{"race":1,"hair_index":3,"hair_color":7,"body_index":0,"leg_index":0}]
	panel._set_controls()
	if not pack_path.is_empty() and not await _wait_previews(shell,1,true):
		return
	await _capture("create_options",directory)
	# Creation controls are aligned to the baked CREATE_CHARACTER artwork,
	# so verify the source TSCN coordinates rather than generic geometric rows.
	var stat_fields := ["strength","dexterity","constitution","wisdom"]
	var stat_widths := [77.0,78.0,78.0,78.0]
	for i in 4:
		var field: String = stat_fields[i]
		var label: Label = shell._stat_labels[field]
		var value: Label = shell._stat_values[field]
		if label.position != Vector2(745,388+36*i) or 				label.size != Vector2(stat_widths[i],24) or 				value.position != Vector2(891,386+36*i) or 				value.size != Vector2(71,27):
			_error("create stat source alignment drifted: "+field)
			return
	if shell._remaining_points_label.position != Vector2(745,532) or 			shell._remaining_points_label.size != Vector2(78,24) or 			shell._remaining_points.position != Vector2(891,530) or 			shell._remaining_points.size != Vector2(71,27):
		_error("remaining-points controls drifted from source artwork")
		return
	if shell._create_password_label.position != Vector2(745,583) or 			shell._create_password.position != Vector2(897,579) or 			shell._create_password.size != Vector2(83,31) or 			shell._create_password_confirm_label.position != Vector2(745,619) or 			shell._create_password_confirm.position != Vector2(897,615) or 			shell._create_password_confirm.size != Vector2(83,31):
		_error("secondary password controls drifted from source artwork")
		return
	if shell._appearance_note.visible or not shell._appearance_note.text.is_empty():
		_error("internal appearance/server guidance is visible to player")
		return
	var appearance_fields := ["race","hair_index","hair_color","body_index","leg_index"]
	for field in appearance_fields:
		var row := appearance_fields.find(field)
		if shell._appearance_buttons[field][0].position != Vector2(351,424+36*row) or 				shell._appearance_values[field].position != Vector2(386,422+36*row) or 				shell._appearance_values[field].size != Vector2(72,29):
			_error("appearance controls drifted from source/create rows: "+field)
			return
	var creation_preview: Control = shell._character_previews[3]
	for field in ["body_index","leg_index"]:
		if not shell._appearance_buttons[field][0].disabled or 				not shell._appearance_buttons[field][1].disabled:
			_error("unverified starter clothing selector stayed interactive: "+field)
			return
	if panel.creation_options.selection().body_index != 0 or 			panel.creation_options.selection().leg_index != 0:
		_error("starter clothing did not stay on the single verified 0/0 choice")
		return
	if not pack_path.is_empty():
		if not await _wait_previews(shell,1,true):
			return
		var definition: Dictionary = creation_preview._definition
		if definition.parts[9][0] != "players/b/base/body" or 				definition.parts[12][0] != "players/b/base/leg" or 				definition.overlays[9][0] != "equipment/b/cloth/body00" or 				definition.overlays[12][0] != "equipment/b/cloth/leg00":
			_error("starter clothes did not preserve Standard body/leg plus verified overlays")
			return
		await _capture("create_clothing",directory)
	var commands_before_hair: int = creation_preview._canvas._commands.size()
	shell._appearance_buttons.hair_index[1].pressed.emit()
	if not pack_path.is_empty() and commands_before_hair > 0 and 			creation_preview._canvas._commands.is_empty():
		_error("hair-style switch blanked the previous preview before replacement loaded")
		return
	shell._appearance_buttons.hair_color[1].pressed.emit()
	if panel.creation_options.selection()!={
			"race":0,"hair_index":1,"hair_color":5,
			"body_index":0,"leg_index":0}:
		_error("hair/color arrows did not select exact allowed tuples")
		return
	var commands_before_race: int = creation_preview._canvas._commands.size()
	shell._appearance_buttons.race[1].pressed.emit()
	if not pack_path.is_empty() and commands_before_race > 0 and 			creation_preview._canvas._commands.is_empty():
		_error("race switch blanked the previous preview before replacement loaded")
		return
	var chosen := {"race":1,"hair_index":3,"hair_color":7,"body_index":0,"leg_index":0}
	if panel.creation_options.selection()!=chosen or 			AppearanceProfile.definition(chosen).is_empty() or shell._slot_edit.visible:
		_error("verified race/hair/color choice did not bind a real profile")
		return
	if not pack_path.is_empty():
		if not await _wait_previews(shell,1,true):
			return
		var colored_preview = shell._character_previews[3]
		if colored_preview._canvas._commands.is_empty() or 				not is_equal_approx(
					float(colored_preview._canvas._hair_material.get_shader_parameter(
						"hue_shift")),315.0):
			_error("hair color 8 did not render with verified 315-degree hue shift")
			return
	await _capture("create_options_pending_profile",directory)
	shell._name_edit.text = "NewHero"
	shell._slot_edit.value = 2
	# Allocate the four remaining source-verified points through the real UI.
	shell._adjust_stat("strength",1)
	shell._adjust_stat("strength",1)
	shell._adjust_stat("dexterity",1)
	shell._adjust_stat("constitution",1)
	shell._create_password.text = "2468"
	shell._create_password_confirm.text = "2468"
	shell._refresh_create_ready()
	if shell._remaining_points.text != "0" or shell._confirm_create.disabled:
		_error("full stat allocation and matching secondary password did not enable create")
		return
	shell._confirm_create.pressed.emit()
	if stub.creates!=1 or stub.last_appearance!=chosen or 			int(stub.last_creation.get("strength",0))!=3 or 			int(stub.last_creation.get("dexterity",0))!=2 or 			int(stub.last_creation.get("constitution",0))!=2 or 			int(stub.last_creation.get("wisdom",0))!=1 or 			str(stub.last_creation.get("password",""))!="2468":
		_error("appearance/stats/secondary-password UI did not submit shared choice")
		return
	if not shell._confirm_create.disabled or panel.creation_options.editable:
		_error("source create controls stayed interactive after queue admission")
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
	if panel.creation_options.selection()!=chosen or panel.appearance_picker._pickers.race.get_selected_id()!=1:
		_error("skin switch lost shared appearance selection")
		return
	# Optional real five-race atlas gate against generated client/assets.
	# These choices are test fixture values; server allowlisting remains separate.
	if args.size()>2 and args[2]=="profiles":
		panel._latest.character_create_result_revision = 2
		panel._latest.character_creation_appearances = [
			{"race":0,"hair_index":0,"hair_color":0,"body_index":0,"leg_index":0},
			{"race":1,"hair_index":0,"hair_color":0,"body_index":0,"leg_index":0},
			{"race":2,"hair_index":0,"hair_color":0,"body_index":0,"leg_index":0},
			{"race":3,"hair_index":0,"hair_color":0,"body_index":0,"leg_index":0},
			{"race":4,"hair_index":0,"hair_color":7,"body_index":0,"leg_index":0}]
		panel._set_controls()
		var prefixes := [
			"players/b/","players/g/","players/a/",
			"equipment/r/cloth/","players/y/"]
		var expected_parts := [8,8,8,5,6]
		for race in 5:
			panel.creation_options.select_value("race",race)
			if race == 4:
				panel.creation_options.select_value("hair_color",7)
			if not await _wait_previews(shell,1,true):
				return
			var preview = shell._character_previews[3]
			if preview._canvas._commands.size()!=expected_parts[race]:
				_error("race preview lost source part count: "+str(race))
				return
			var profile_parts: Array = preview._definition.parts.values()
			profile_parts.append_array(
				preview._definition.get("overlays",{}).values())
			for part in profile_parts:
				var asset_path := str(part[0])
				var valid_prefix := asset_path.begins_with(prefixes[race])
				if race in [0,1,2]:
					valid_prefix = valid_prefix or asset_path.begins_with(
						"equipment/"+["b","g","a"][race]+"/cloth/")
				if not valid_prefix:
					_error("race preview borrowed another race: "+str(race))
					return
			if race==4 and not is_equal_approx(
					float(preview._canvas._hair_material.get_shader_parameter(
						"hue_shift")),315.0):
				_error("Yas colored hair did not retain verified hue")
				return
			await _capture("create_profile_"+str(race),directory)
	var store = app.world_view.store
	app.queue_free()
	await process_frame
	var drain_deadline := Time.get_ticks_msec()+10000
	while store.statistics().decode_jobs+store.statistics().texture_jobs>0 and Time.get_ticks_msec()<drain_deadline:
		store.pump(2)
		await process_frame
	var final: Dictionary = store.statistics()
	if final.assets+final.scopes+final.rgba_bytes+final.decode_jobs+final.texture_jobs != 0:
		_error("auth preview teardown retained assets or scopes")
		return
	print("RECOVERED2_LOCAL_UI_PASS login_select_create=true hidden_deployment_host_port=true original_video_theme_pngs=true viewport_fit_1152x620=true single_session_delegation=true character_preview_pack_tested=%s all_assets_drained=true" % str(not pack_path.is_empty()))
	quit(0)
