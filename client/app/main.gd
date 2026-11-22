extends Node
## UI composition and presentation input; ClientSession owns networking/reduction.
const WorldView = preload("res://world/world_view.gd")
const SessionPanel = preload("res://session/session_panel.gd")
const MouseAutorunState = preload("res://movement/mouse_autorun_state.gd")
const SessionLayout = preload("res://ui/session_layout.gd")
const UiSkinCatalog = preload("res://ui/ui_skin_catalog.gd")
const BackdropShader = preload("res://ui/skin_backdrop.gdshader")
const TerminalWorldHud = preload("res://ui/terminal_world_hud.gd")
const UiTextureLoader = preload("res://ui/ui_texture_loader.gd")
const SharedGameCursor = preload("res://ui/shared_game_cursor.gd")
const Recovered2AuthShell = preload("res://ui/recovered2_auth_shell.gd")
const Recovered2WorldShell = preload("res://ui/recovered2_world_shell.gd")
const ClassicAuthShell = preload("res://ui/classic_auth_shell.gd")
const WorldLoadingOverlay = preload("res://ui/world_loading_overlay.gd")
var auto_start := true
var world_view := WorldView.new()
var _held := {}
var _dragging := false
var _closing := false
var _status := Label.new()
var toolbar := PanelContainer.new()
var _screen_backdrop := ColorRect.new()
var _photo_backdrop := TextureRect.new()
var _lobby_skin_pickers: Array[OptionButton] = []
var _backdrop_material := ShaderMaterial.new()
var _ui_stage_key := ""
var _current_skin := "retro_terminal"
var _help_label := Label.new()
var cursor_manager := SharedGameCursor.new()
var _cursor_sample_elapsed := 0.0
var terminal_hud := TerminalWorldHud.new()
var session_panel := SessionPanel.new()
var recovered2_auth := Recovered2AuthShell.new()
var recovered2_world := Recovered2WorldShell.new()
var classic_auth := ClassicAuthShell.new()
var _r2_legacy_group := -1
var _r2_close := Button.new()
var loading_overlay := WorldLoadingOverlay.new()
var _loading_ui_elapsed := 0.0
var _recovered2_local_ready := false
var preview_auth_offline := false
var offline_map_only := false
var dev_overlay_enabled := false
var skin_picker: OptionButton
var _map_buttons: Array[Button] = []
var _last_snapshot: Dictionary = {}
var _mouse_autorun = MouseAutorunState.new()
var _autorun_left_press_cancelled := false
var _autorun_map_epoch := 0
var _autorun_waiting_sequence_floor := -1
var _autorun_last_blocked_sequence := 0
# Pending complex path: never send a MoveTarget before the local path is
# complete. This prevents the Server beginning the route before the Client.
var _pending_path_target := Vector2.INF
var _pending_path_epoch := 0
var _pending_path_sequence_floor := -1


## Fit an oversized initial window into the available desktop area without
## changing the game viewport contract or forcing a fixed game resolution.
func _exit_tree() -> void:
	if offline_map_only and is_instance_valid(cursor_manager):
		cursor_manager.free() # unmounted Node has no scene-tree owner
	# Child cursors remove native pointer overrides first. Then release
	# static UI image references so a closed scene retains no GPU textures.
	UiTextureLoader.clear_cache()


func _fit_initial_window_to_desktop() -> void:
	if DisplayServer.get_name() == "headless":
		return
	var window := get_window()
	if window == null or window.mode != Window.MODE_WINDOWED:
		return
	var usable := DisplayServer.screen_get_usable_rect()
	if usable.size.x < 800 or usable.size.y < 550:
		return
	var limit := Vector2i(usable.size.x - 40,usable.size.y - 60)
	if window.size.x <= limit.x and window.size.y <= limit.y:
		return
	window.size = Vector2i(mini(window.size.x,limit.x),mini(window.size.y,limit.y))
	window.position = usable.position + (usable.size - window.size) / 2


func _ready() -> void:
	get_tree().auto_accept_quit = false
	dev_overlay_enabled = OS.get_cmdline_user_args().has("--dev-ui")
	if auto_start:
		_fit_initial_window_to_desktop()
	add_child(world_view)
	var layer := CanvasLayer.new()
	add_child(layer)
	_screen_backdrop.mouse_filter = Control.MOUSE_FILTER_STOP
	_backdrop_material.shader = BackdropShader
	_screen_backdrop.material = _backdrop_material
	_screen_backdrop.color = UiSkinCatalog.backdrop_color(_current_skin)
	_screen_backdrop.visible = false
	layer.add_child(_screen_backdrop)
	_photo_backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_photo_backdrop.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_photo_backdrop.stretch_mode = TextureRect.STRETCH_SCALE
	_photo_backdrop.visible = false
	layer.add_child(_photo_backdrop)
	layer.add_child(toolbar)
	var style := StyleBoxFlat.new()
	style.bg_color = Color("211c15")
	style.border_color = Color("c8a452")
	style.set_border_width_all(1)
	style.content_margin_left = 12
	style.content_margin_right = 12
	style.content_margin_top = 8
	style.content_margin_bottom = 8
	toolbar.add_theme_stylebox_override("panel",style)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation",12)
	toolbar.add_child(row)
	for id in [15,13]:
		var button := Button.new()
		button.name = "Map" + str(id)
		button.text = "地圖 " + str(id)
		button.focus_mode = Control.FOCUS_NONE
		button.pressed.connect(func(): _inspect_map(id))
		row.add_child(button)
		_map_buttons.append(button)
	row.add_child(_status)
	_help_label.text = "方向鍵／中鍵拖曳　Home 置中　長按地面 1 秒鎖定走路"
	row.add_child(_help_label)
	world_view.status_changed.connect(_world_status)
	layer.add_child(session_panel)
	layer.add_child(terminal_hud)
	layer.add_child(recovered2_auth)
	layer.add_child(classic_auth)
	layer.add_child(recovered2_world)
	layer.add_child(loading_overlay)
	_r2_close.text = "×"
	_r2_close.name = "Recovered2CloseLegacyPanel"
	_r2_close.pressed.connect(_close_r2_legacy_group)
	_r2_close.hide()
	layer.add_child(_r2_close)
	# Offline map tests must not load the private login movie or its GPU
	# textures; that presentation is only built for the actual client or a
	# local visual test which explicitly opts in.
	if auto_start or preview_auth_offline:
		recovered2_auth.set_asset_store(world_view.store)
		_recovered2_local_ready = recovered2_auth.attach(session_panel)
	# Only _update_ui_stage may display the local auth shell. An inactive
	# offline map preview must never have its mouse input intercepted.
	recovered2_auth.enable_for_page(SessionLayout.Page.LOGIN,false)
	recovered2_auth.skin_requested.connect(_select_recovered2_skin)
	classic_auth.bind_session(session_panel,world_view.store)
	recovered2_world.bind_session(session_panel)
	recovered2_world.request_legacy_section.connect(_show_r2_legacy_group)
	recovered2_world.skin_requested.connect(_select_recovered2_skin)
	if not offline_map_only:
		add_child(cursor_manager)
	terminal_hud.visible = false
	terminal_hud.section_requested.connect(_open_terminal_section)
	session_panel.visible = auto_start
	session_panel.position = Vector2(16,64)
	session_panel.connection_changed.connect(_connection_changed)
	session_panel.world_ready.connect(_server_world_ready)
	session_panel.world_snapshot.connect(_server_snapshot)
	if dev_overlay_enabled:
		var login_toggle := Button.new()
		login_toggle.text = "登入／連線"
		login_toggle.focus_mode = Control.FOCUS_NONE
		login_toggle.pressed.connect(func(): session_panel.visible = not session_panel.visible)
		row.add_child(login_toggle)
	if not offline_map_only:
		_install_skin_selector(row)
	toolbar.visible = dev_overlay_enabled
	get_viewport().size_changed.connect(_resize)
	_resize()
	if auto_start:
		var pack := ProjectSettings.globalize_path("res://../.cache/client-release/assets/assets.pck")
		var args := OS.get_cmdline_user_args()
		for i in args.size()-1:
			if args[i] == "--pack":
				pack = args[i+1]
			elif args[i] == "--host":
				session_panel.server_host = args[i+1]
			elif args[i] == "--port" and args[i+1].is_valid_int():
				session_panel.server_port = int(args[i+1])
		if world_view.open_pack(pack) == OK:
			world_view.request_map(15)
		else:
			_status.text = "素材包未就緒，請先依 Client README 建置"


func _install_skin_selector(row: HBoxContainer) -> void:
	skin_picker = OptionButton.new()
	# Only view styling changes; the existing SessionPanel and command
	# callbacks survive a runtime skin switch without reconnecting.
	var selected := UiSkinCatalog.load_selected()
	_current_skin = selected
	for skin in UiSkinCatalog.available():
		var index := skin_picker.item_count
		skin_picker.add_item(str(skin.name))
		skin_picker.set_item_metadata(index,str(skin.id))
		if str(skin.id) == selected:
			skin_picker.select(index)
	skin_picker.tooltip_text = "切換介面風格（不影響登入與遊戲狀態）"
	skin_picker.focus_mode = Control.FOCUS_NONE
	row.add_child(skin_picker)
	skin_picker.visible = dev_overlay_enabled
	skin_picker.item_selected.connect(_skin_selected)
	# Normal login/select/create screens contain no SGTest-only UI selector.
	# Runtime UI switching is exposed by the in-world System Settings window.
	UiSkinCatalog.apply(selected,toolbar,session_panel)
	terminal_hud.set_ui_theme(UiSkinCatalog.build_theme(selected))


func _select_recovered2_skin(id: String) -> void:
	for i in skin_picker.item_count:
		if str(skin_picker.get_item_metadata(i)) == id:
			_skin_selected(i)
			return


func _skin_selected(index: int) -> void:
	var skin := str(skin_picker.get_item_metadata(index))
	if UiSkinCatalog.apply(skin,toolbar,session_panel):
		UiSkinCatalog.save_selected(skin)
		_current_skin = skin
		terminal_hud.set_ui_theme(UiSkinCatalog.build_theme(skin))
		skin_picker.select(index)
		for picker in _lobby_skin_pickers:
			picker.select(index)
		_update_ui_stage(true)


func _resize() -> void:
	world_view.resize_view(Vector2i(get_viewport().get_visible_rect().size))
	_screen_backdrop.size = Vector2(get_viewport().get_visible_rect().size)
	_photo_backdrop.size = _screen_backdrop.size
	recovered2_auth.resize_view(_screen_backdrop.size)
	classic_auth.resize_view(_screen_backdrop.size)
	recovered2_world.resize_view(_screen_backdrop.size)
	loading_overlay.resize_view(_screen_backdrop.size)
	terminal_hud.resize_view(Vector2i(_screen_backdrop.size))
	_update_ui_stage(true)


func _update_ui_stage(force: bool = false) -> void:
	if not auto_start:
		return
	var pre_world := session_panel.visible and session_panel._layout.current_page != SessionLayout.Page.WORLD
	var r2_frontend := _current_skin == "recovered2_local" and _recovered2_local_ready and pre_world
	var original_skin := _current_skin == "original_ie_hk2" and UiSkinCatalog.valid("original_ie_hk2")
	var original_frontend := original_skin and pre_world
	var r2_world := ((_current_skin == "recovered2_local" and _recovered2_local_ready) or original_skin) and session_panel.active and session_panel._layout.current_page == SessionLayout.Page.WORLD
	var viewport_size := Vector2(get_viewport().get_visible_rect().size)
	var key := "%s:%s:%d:%d:%d:%d:%d" % [str(pre_world),_current_skin,int(viewport_size.x),int(viewport_size.y),int(session_panel.visible),int(session_panel._layout.current_page),_r2_legacy_group]
	if not force and key == _ui_stage_key:
		return
	_ui_stage_key = key
	_screen_backdrop.color = UiSkinCatalog.backdrop_color(_current_skin)
	_backdrop_material.set_shader_parameter("base_color",_screen_backdrop.color)
	_backdrop_material.set_shader_parameter("motif",UiSkinCatalog.backdrop_motif(_current_skin))
	var backdrop_path := UiSkinCatalog.backdrop_path(_current_skin)
	var picture := UiTextureLoader.get_texture(backdrop_path)
	_photo_backdrop.texture = picture if picture is Texture2D else null
	_photo_backdrop.visible = pre_world and _photo_backdrop.texture != null
	_screen_backdrop.visible = pre_world and not _photo_backdrop.visible and not r2_frontend and not original_frontend
	_photo_backdrop.visible = _photo_backdrop.visible and not r2_frontend and not original_frontend
	recovered2_auth.enable_for_page(session_panel._layout.current_page,r2_frontend)
	classic_auth.enable_for_page(session_panel._layout.current_page,original_frontend)
	recovered2_world.set_profile("original_ie_hk2" if original_skin else "recovered2_local")
	toolbar.visible = dev_overlay_enabled and not pre_world and not r2_world
	recovered2_world.set_world_active(r2_world)
	_r2_close.visible = r2_world and _r2_legacy_group >= 0
	var retro_world := _current_skin == "retro_terminal" and not pre_world
	terminal_hud.visible = retro_world and session_panel.active
	for button in _map_buttons:
		button.hide()
	_help_label.hide()
	_status.hide()
	var panel_width := minf(560.0,maxf(360.0,viewport_size.x-32.0)) if pre_world else minf(420.0,maxf(300.0,viewport_size.x-32.0))
	session_panel.custom_minimum_size.x = panel_width
	if r2_frontend or original_frontend:
		# One SessionPanel stays alive but never covers the private login art.
		session_panel.position = Vector2(-12000,-12000)
		return
	if r2_world:
		# Floating windows are independent presentation. The existing live
		# SessionPanel is opened only for features not yet promoted to a
		# source-position native window; all Server callbacks stay intact.
		if _r2_legacy_group < 0:
			session_panel.position = Vector2(-12000,-12000)
		else:
			var width := minf(445.0,viewport_size.x-84.0)
			session_panel.size = Vector2(width,minf(530.0,viewport_size.y-120.0))
			session_panel.position = Vector2(maxf(20.0,viewport_size.x-width-86.0),76.0)
			_r2_close.position = session_panel.position + Vector2(width-35.0,-15.0)
			_r2_close.size = Vector2(35,31)
		return
	if pre_world:
		# Authentication screens size to visible content rather than leaving
		# an unused full-height sidebar on the title/character screen.
		var screen_page := session_panel._layout.current_page
		var retro := _current_skin == "retro_terminal"
		var minimum_height := (505.0 if screen_page == SessionLayout.Page.LOGIN else (590.0 if screen_page == SessionLayout.Page.CHARACTER_CREATE else 545.0)) if retro else (390.0 if screen_page == SessionLayout.Page.LOGIN else (350.0 if screen_page == SessionLayout.Page.CHARACTER_CREATE else 320.0))
		# Hidden world tabs must not inflate an authentication screen. A small
		# viewport can still scroll via SessionOptionsScroll when necessary.
		var desired_height := minimum_height
		var panel_height := minf(desired_height,maxf(160.0,viewport_size.y-48.0))
		session_panel.size = Vector2(panel_width,panel_height)
		session_panel.position = Vector2(maxf(16.0,(viewport_size.x-panel_width)*0.5),maxf(24.0,(viewport_size.y-panel_height)*0.5))
	else:
		var dock_right := UiSkinCatalog.dock(_current_skin) == "right"
		session_panel.size = Vector2(panel_width,maxf(220.0,viewport_size.y-96.0))
		session_panel.position = Vector2(maxf(16.0,viewport_size.x-panel_width-16.0) if dock_right else 16.0,64.0)


func _show_r2_legacy_group(index: int) -> void:
	if not session_panel.active:
		return
	_r2_legacy_group = -1 if _r2_legacy_group == index else index
	if _r2_legacy_group >= 0 and session_panel._layout.layout_id == "tabs":
		session_panel._layout.world_tabs.current_tab = clampi(index,0,3)
	_update_ui_stage(true)


func _close_r2_legacy_group() -> void:
	_r2_legacy_group = -1
	_update_ui_stage(true)


func _open_terminal_section(index: int) -> void:
	if not session_panel.active:
		return
	session_panel.visible = true
	if session_panel._layout.layout_id == "tabs":
		session_panel._layout.world_tabs.current_tab = clampi(index,0,3)
	_update_ui_stage(true)


func _unhandled_key_input(event: InputEvent) -> void:
	if event is InputEventKey:
		var key: int = event.physical_keycode
		if session_panel.active:
			return
		if key in [KEY_LEFT,KEY_RIGHT,KEY_UP,KEY_DOWN]:
			if event.pressed:
				_held[key] = true
			else:
				_held.erase(key)
			get_viewport().set_input_as_handled()
		elif key == KEY_HOME and event.pressed and world_view.phase == "ready":
			world_view.camera.set_focus(world_view.epoch,Vector2(world_view.camera.snapshot().world_size)/2.0)


func _input(event: InputEvent) -> void:
	if not event is InputEventMouseButton:
		return
	var mouse := event as InputEventMouseButton
	if mouse.button_index == MOUSE_BUTTON_LEFT:
		if not mouse.pressed:
			_autorun_left_press_cancelled = false
			if _mouse_autorun.holding:
				_mouse_autorun.terrain_release(Time.get_ticks_msec())
		elif _mouse_autorun.latched:
			# Do not consume the event: entity/UI handlers still receive this
			# click. Only bare terrain suppresses the same-press new route below.
			_autorun_left_press_cancelled = true
			_cancel_mouse_autorun(true)
	elif mouse.button_index == MOUSE_BUTTON_RIGHT and mouse.pressed and _mouse_autorun.active():
		# Preserve all normal right-click handlers/context menus.
		_cancel_mouse_autorun(true)


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed and session_panel.active and world_view.phase == "ready":
		var point: Vector2 = world_view.camera.screen_to_world(event.position)
		var drop_id: int = world_view.pick_ground_drop_at(point)
		var monster_id: int = world_view.pick_monster_at(point) if drop_id == 0 else 0
		var npc_id: int = world_view.pick_npc_at(point) if drop_id == 0 and monster_id == 0 else 0
		if drop_id > 0:
			# A latched click already queued an ordered stop barrier in _input;
			# the item interaction follows it without losing normal click priority.
			session_panel.submit_pickup_drop(drop_id)
		elif monster_id > 0:
			# A live visible monster takes combat priority over ground movement.
			# Never fall back to movement when the combat intent is backpressured.
			session_panel.submit_monster_action(monster_id)
		elif npc_id > 0:
			# Rendered NPCs use the original NPC.dat spawn ID. Client never
			# executes source scripts or decides a teleport destination.
			var interaction := world_view.npc_interaction_for(npc_id)
			if str(interaction.get("mode","")) != "confirm_transfer" or not bool(interaction.get("dependencies_complete",false)):
				_status.text = "NPC %d：對話／功能尚未完成" % npc_id
			elif session_panel.submit_npc_interact(npc_id):
				_status.text = "NPC %d：等待伺服器確認傳點" % npc_id
			else:
				_status.text = "NPC %d：目前無法互動，請稍後重試" % npc_id
		elif not _autorun_left_press_cancelled:
			_begin_ground_navigation(point,Time.get_ticks_msec())
		# First bare-ground click after a latch only stops the old route.
		get_viewport().set_input_as_handled()
		return
	if session_panel.active:
		return
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_MIDDLE:
		_dragging = event.pressed
		get_viewport().set_input_as_handled()
	elif event is InputEventMouseMotion and _dragging:
		world_view.camera.pan(world_view.epoch,-event.relative)
		get_viewport().set_input_as_handled()


func _physics_process(_delta: float) -> void:
	if not _mouse_autorun.active():
		return
	var now := Time.get_ticks_msec()
	# _input normally sees release first; this is a fallback for cases where
	# another interaction consumes the release later in the input pipeline.
	if _mouse_autorun.holding and not Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT):
		_mouse_autorun.terrain_release(now)
	if not _mouse_autorun.active() or not session_panel.active or _last_snapshot.is_empty():
		return
	if bool(_last_snapshot.get("player_dead",false)) or bool(_last_snapshot.get("reconnecting",false)) or bool(_last_snapshot.get("waiting_for_new_map",false)):
		_cancel_mouse_autorun(false)
		return
	var epoch := int(_last_snapshot.get("map_epoch",0))
	if epoch <= 0 or (_autorun_map_epoch > 0 and epoch != _autorun_map_epoch):
		_cancel_mouse_autorun(false)
		return
	if not bool(_last_snapshot.get("world_ready",false)) or bool(_last_snapshot.get("resync_pending",false)):
		return

	var movement_sequence := int(_last_snapshot.get("movement_sequence",0))
	if _autorun_waiting_sequence_floor >= 0:
		if movement_sequence <= _autorun_waiting_sequence_floor:
			return
		_autorun_waiting_sequence_floor = -1
	var receipt_sequence := int(_last_snapshot.get("last_receipt_sequence",0))
	if movement_sequence > receipt_sequence:
		return
	if receipt_sequence == movement_sequence and movement_sequence > 0 and int(_last_snapshot.get("last_receipt_status",-1)) != 0:
		if _autorun_last_blocked_sequence != movement_sequence:
			_autorun_last_blocked_sequence = movement_sequence
			_mouse_autorun.defer_blocked_retry(now)

	var own: Dictionary = _last_snapshot.get("self_state",{})
	var moving := bool(own.get("moving",false)) if not own.is_empty() else false
	var actor_position := Vector2(
		float(_last_snapshot.get("x",0.0)),float(_last_snapshot.get("y",0.0)))
	var cursor_position := world_view.camera.screen_to_world(
		get_viewport().get_mouse_position())
	if _mouse_autorun.should_request(
			now,actor_position,cursor_position,moving):
		_submit_autorun_target(cursor_position,now,not moving)


func _process(delta: float) -> void:
	if _closing:
		var pending := world_view.store.statistics()
		if pending.decode_jobs + pending.texture_jobs == 0:
			get_tree().quit()
		return
	_flush_pending_path()
	_update_ui_stage()
	_loading_ui_elapsed += delta
	if _loading_ui_elapsed >= 0.10:
		_loading_ui_elapsed = 0.0
		_update_loading_ui()
	_cursor_sample_elapsed += delta
	if not offline_map_only and (_cursor_sample_elapsed >= 0.06 or _mouse_autorun.latched):
		_cursor_sample_elapsed = 0.0
		_update_cursor_mode()
	if world_view.phase == "ready" and not session_panel.active:
		var direction := Vector2(int(_held.has(KEY_RIGHT))-int(_held.has(KEY_LEFT)),int(_held.has(KEY_DOWN))-int(_held.has(KEY_UP)))
		if direction != Vector2.ZERO:
			world_view.camera.pan(world_view.epoch,direction.normalized()*600.0*delta)


func _update_loading_ui() -> void:
	# No percent is inferred from network packets: the overlay measures the
	# actual local asset scope while the authenticated world is entering.
	var entering_world := session_panel.active and session_panel._layout.current_page == SessionLayout.Page.WORLD
	if not entering_world or world_view.phase not in ["map","palette"]:
		loading_overlay.update_state(world_view.phase,world_view.map_id,0,0,0,0,false)
		return
	var stats: Dictionary = world_view.store.scope_progress(&"world_view",world_view.epoch)
	loading_overlay.update_state(world_view.phase,world_view.map_id,
		int(stats.ready),int(stats.total),int(stats.decoding),int(stats.textures),true)


func _update_cursor_mode() -> void:
	if not session_panel.active or session_panel._layout.current_page != SessionLayout.Page.WORLD or world_view.phase != "ready":
		cursor_manager.set_mode("normal")
		return
	if _mouse_autorun.latched:
		cursor_manager.set_mode("move")
		return
	var hovered := get_viewport().gui_get_hovered_control()
	if hovered != null and (
		hovered == session_panel or session_panel.is_ancestor_of(hovered) or
		hovered == terminal_hud or terminal_hud.is_ancestor_of(hovered) or
		hovered == toolbar or toolbar.is_ancestor_of(hovered)
	):
		cursor_manager.set_mode("normal")
		return
	var cursor_world := world_view.camera.screen_to_world(get_viewport().get_mouse_position())
	if world_view.pick_ground_drop_at(cursor_world) > 0:
		cursor_manager.set_mode("pickup")
	elif world_view.pick_monster_at(cursor_world) > 0:
		cursor_manager.set_mode("attack")
	elif world_view.pick_npc_at(cursor_world) > 0:
		cursor_manager.set_mode("inspect")
	else:
		cursor_manager.set_mode("normal")


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_WINDOW_FOCUS_OUT:
		_held.clear()
		_dragging = false
	elif what == NOTIFICATION_WM_CLOSE_REQUEST and not _closing:
		_closing = true
		_held.clear()
		_dragging = false
		session_panel.disconnect_session()
		world_view.clear_map()


func _connection_changed(active: bool) -> void:
	if not active:
		_cancel_mouse_autorun(false)
	for button in _map_buttons:
		button.disabled = active
	_held.clear()
	_dragging = false
	terminal_hud.clear()
	_r2_legacy_group = -1
	recovered2_world.set_world_active(false)
	world_view.clear_map()
	world_view.show_players = active
	if active:
		_status.text = "連線中"
	else:
		_status.text = "已登出・可切換離線地圖檢視"


func _inspect_map(id: int) -> void:
	if not session_panel.active:
		world_view.request_map(id)


func _server_world_ready(snapshot: Dictionary) -> void:
	if world_view.request_map(int(snapshot.map_id)) == OK:
		world_view.set_player_snapshot(snapshot)
		_update_loading_ui()
	else:
		_status.text = "已連線，地圖素材未就緒"


func _server_snapshot(snapshot: Dictionary) -> void:
	var previous := _last_snapshot
	if _mouse_autorun.active() and not previous.is_empty():
		var old_epoch := int(previous.get("map_epoch",0))
		var new_epoch := int(snapshot.get("map_epoch",0))
		var new_dead := bool(snapshot.get("player_dead",false))
		var transferred := old_epoch > 0 and new_epoch > 0 and old_epoch != new_epoch
		var damaged := int(snapshot.get("player_damage_sequence",0)) > int(previous.get("player_damage_sequence",0))
		var attacked := int(snapshot.get("last_attack_result_sequence",0)) > int(previous.get("last_attack_result_sequence",0))
		if transferred or new_dead or bool(snapshot.get("waiting_for_new_map",false)) or bool(snapshot.get("reconnecting",false)):
			_cancel_mouse_autorun(false)
		elif damaged or attacked:
			_cancel_mouse_autorun(true)
	if int(snapshot.get("last_npc_result_sequence",0)) > int(previous.get("last_npc_result_sequence",0)):
		var npc_result := int(snapshot.get("last_npc_result_status",-1))
		if npc_result != 0:
			_status.text = "NPC 互動被伺服器拒絕（代碼 %d）" % npc_result
	_last_snapshot = snapshot.duplicate(true)
	terminal_hud.apply_snapshot(snapshot)
	recovered2_world.apply_snapshot(snapshot)
	world_view.set_player_snapshot(snapshot)


func _begin_ground_navigation(point: Vector2,now_msec: int) -> void:
	if not point.is_finite():
		return
	var was_latched := _mouse_autorun.terrain_press(now_msec)
	if was_latched:
		return
	_autorun_map_epoch = int(_last_snapshot.get("map_epoch",0))
	_autorun_last_blocked_sequence = 0
	_submit_autorun_target(point,now_msec,false)


func _flush_pending_path() -> void:
	if not _pending_path_target.is_finite():
		return
	var controller = world_view.local_movement
	var valid := session_panel.active and world_view.phase == "ready" and \
		int(_last_snapshot.get("map_epoch",0)) == _pending_path_epoch and \
		int(_last_snapshot.get("movement_sequence",0)) == _pending_path_sequence_floor and \
		bool(_last_snapshot.get("world_ready",false)) and \
		not bool(_last_snapshot.get("resync_pending",false)) and \
		not bool(_last_snapshot.get("player_dead",false))
	if not valid or not controller.active:
		_pending_path_target = Vector2.INF
		_pending_path_epoch = 0
		_pending_path_sequence_floor = -1
		_autorun_waiting_sequence_floor = -1
		if controller.active:
			world_view.stop_local_move()
		return
	if not controller.moving or not controller._search.is_empty():
		return
	var goal := _pending_path_target
	_pending_path_target = Vector2.INF
	_pending_path_epoch = 0
	_pending_path_sequence_floor = -1
	if not session_panel.submit_move(goal):
		_autorun_waiting_sequence_floor = -1
		world_view.stop_local_move()


func _submit_autorun_target(point: Vector2,now_msec: int,was_stopped: bool) -> bool:
	if not point.is_finite() or not session_panel.active:
		return false
	var sequence_floor := int(_last_snapshot.get("movement_sequence",0))
	if not world_view.begin_local_move(point,sequence_floor+1):
		return false
	_autorun_waiting_sequence_floor = sequence_floor
	if world_view.local_movement.moving:
		# Fast-path (direct/normal A*) is complete: send after route validation.
		if not session_panel.submit_move(point):
			world_view.stop_local_move()
			_autorun_waiting_sequence_floor = -1
			return false
		_pending_path_target = Vector2.INF
		_pending_path_epoch = 0
		_pending_path_sequence_floor = -1
	else:
		# Recovered's large-detour fallback returns only a COMPLETE path.
		# Wait for that full route before posting the authoritative MoveTarget.
		_pending_path_target = point
		_pending_path_epoch = int(_last_snapshot.get("map_epoch",0))
		_pending_path_sequence_floor = sequence_floor
	_mouse_autorun.note_request(point,now_msec,was_stopped)
	return true


func _cancel_mouse_autorun(stop_movement: bool) -> void:
	var was_active := _mouse_autorun.active()
	_mouse_autorun.cancel()
	_autorun_map_epoch = 0
	_autorun_waiting_sequence_floor = -1
	_autorun_last_blocked_sequence = 0
	_pending_path_target = Vector2.INF
	_pending_path_epoch = 0
	_pending_path_sequence_floor = -1
	if stop_movement and was_active and session_panel.active:
		world_view.stop_local_move()
		session_panel.submit_stop_movement()


func _world_status(message: String) -> void:
	_status.text = "地圖 " + str(world_view.map_id) + "｜已連線" if session_panel.active and world_view.phase == "ready" else message
