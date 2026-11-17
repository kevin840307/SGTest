extends Node
## UI composition and presentation input; ClientSession owns networking/reduction.
const WorldView = preload("res://rendering/world_view.gd")
const SessionPanel = preload("res://ui/session_panel.gd")
const MouseAutorunState = preload("res://app/mouse_autorun_state.gd")
var auto_start := true
var world_view := WorldView.new()
var _held := {}
var _dragging := false
var _closing := false
var _status := Label.new()
var toolbar := PanelContainer.new()
var session_panel := SessionPanel.new()
var _map_buttons: Array[Button] = []
var _last_snapshot: Dictionary = {}
var _mouse_autorun = MouseAutorunState.new()
var _autorun_left_press_cancelled := false
var _autorun_map_epoch := 0
var _autorun_waiting_sequence_floor := -1
var _autorun_last_blocked_sequence := 0
var _online_gate_role := ""
var _online_gate_phase := -1
var _online_gate_deadline := 0
var _online_gate_not_before := 0
var _online_gate_remote_id := 0
var _online_gate_remote_start := Vector2.ZERO
var _online_gate_finish_code := -1
var _online_gate_finish_reason := ""
var _online_gate_monster_seen := false


func _ready() -> void:
	get_tree().auto_accept_quit = false
	add_child(world_view)
	var layer := CanvasLayer.new()
	add_child(layer)
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
	_status.add_theme_color_override("font_color",Color("f2d995"))
	row.add_child(_status)
	var help := Label.new()
	help.text = "方向鍵／中鍵拖曳　Home 置中　長按地面 1 秒鎖定走路"
	row.add_child(help)
	world_view.status_changed.connect(_world_status)
	layer.add_child(session_panel)
	session_panel.visible = auto_start
	session_panel.position = Vector2(16,64)
	session_panel.connection_changed.connect(_connection_changed)
	session_panel.world_ready.connect(_server_world_ready)
	session_panel.world_snapshot.connect(_server_snapshot)
	var login_toggle := Button.new()
	login_toggle.text = "登入／連線"
	login_toggle.focus_mode = Control.FOCUS_NONE
	login_toggle.pressed.connect(func(): session_panel.visible = not session_panel.visible)
	row.add_child(login_toggle)
	get_viewport().size_changed.connect(_resize)
	_resize()
	if auto_start:
		var pack := ProjectSettings.globalize_path("res://../.cache/client-release/assets/assets.pck")
		var args := OS.get_cmdline_user_args()
		for i in args.size()-1:
			if args[i] == "--pack":
				pack = args[i+1]
			elif args[i] == "--host":
				session_panel.host_input.text = args[i+1]
			elif args[i] == "--port" and args[i+1].is_valid_int():
				session_panel.port_input.value = int(args[i+1])
			elif args[i] == "--tls-name":
				session_panel.tls_name = args[i+1]
			elif args[i] == "--trusted-cert":
				var cert := X509Certificate.new()
				if cert.load(args[i+1]) == OK:
					session_panel.trusted_chain = cert
				else:
					session_panel.status.text = "無法讀取指定憑證；未停用 TLS 驗證"
			elif args[i] == "--e2e-role":
				_online_gate_role = args[i+1]
		if world_view.open_pack(pack) == OK:
			world_view.request_map(15)
		else:
			_status.text = "素材包未就緒，請先依 Client README 建置"
		if not _online_gate_role.is_empty():
			call_deferred("_start_online_gate")


func _resize() -> void:
	world_view.resize_view(Vector2i(get_viewport().get_visible_rect().size))


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
		if drop_id > 0:
			# A latched click already queued an ordered stop barrier in _input;
			# the item interaction follows it without losing normal click priority.
			session_panel.submit_pickup_drop(drop_id)
		elif monster_id > 0:
			# A live visible monster takes attack priority over ground movement.
			# Never fall back to movement when the combat intent is backpressured.
			session_panel.submit_attack_monster(monster_id)
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
	if not _online_gate_role.is_empty():
		_online_gate_tick()
	if world_view.phase == "ready" and not session_panel.active:
		var direction := Vector2(int(_held.has(KEY_RIGHT))-int(_held.has(KEY_LEFT)),int(_held.has(KEY_DOWN))-int(_held.has(KEY_UP)))
		if direction != Vector2.ZERO:
			world_view.camera.pan(world_view.epoch,direction.normalized()*600.0*delta)


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
	_last_snapshot = snapshot.duplicate(true)
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


func _submit_autorun_target(point: Vector2,now_msec: int,was_stopped: bool) -> bool:
	if not point.is_finite() or not session_panel.active:
		return false
	var sequence_floor := int(_last_snapshot.get("movement_sequence",0))
	if not session_panel.submit_move(point):
		return false
	_autorun_waiting_sequence_floor = sequence_floor
	_mouse_autorun.note_request(point,now_msec,was_stopped)
	return true


func _cancel_mouse_autorun(stop_movement: bool) -> void:
	var was_active := _mouse_autorun.active()
	_mouse_autorun.cancel()
	_autorun_map_epoch = 0
	_autorun_waiting_sequence_floor = -1
	_autorun_last_blocked_sequence = 0
	if stop_movement and was_active and session_panel.active:
		session_panel.submit_stop_movement()


func _start_online_gate() -> void:
	if not _online_gate_role in ["observer","actor"]:
		_online_gate_fail("invalid_role")
		return
	var username := OS.get_environment("SGTEST_E2E_USERNAME")
	var password := OS.get_environment("SGTEST_E2E_PASSWORD")
	OS.set_environment("SGTEST_E2E_PASSWORD","")
	if username.is_empty() or password.is_empty() or world_view.phase == "closed":
		password = ""
		_online_gate_fail("missing_credentials_or_pack")
		return
	session_panel.username_input.text = username
	session_panel.password_input.text = password
	password = ""
	_online_gate_deadline = Time.get_ticks_msec() + 25000
	_online_gate_phase = 0
	print("ONLINE_MAIN_GATE_START role=",_online_gate_role)
	session_panel.login()


func _online_gate_ready(map: int) -> bool:
	if _last_snapshot.is_empty() or not bool(_last_snapshot.get("world_ready",false)):
		return false
	if int(_last_snapshot.get("map_id",0)) != map or world_view.phase != "ready" or world_view.map_id != map:
		return false
	var expected := Vector2(float(_last_snapshot.get("x",0.0)),float(_last_snapshot.get("y",0.0)))
	return world_view.camera.snapshot().focus.distance_to(expected) <= 0.5


func _online_gate_tick() -> void:
	if _online_gate_phase < 0:
		return
	if not _online_gate_monster_seen:
		var monster_state: Dictionary = _last_snapshot.get("monsters",{})
		if not monster_state.is_empty() and world_view.monster_count() == monster_state.size():
			_online_gate_monster_seen = true
			print("ONLINE_MAIN_GATE_MONSTER role=",_online_gate_role,
				" count=",monster_state.size()," presented=",world_view.monster_count())
	if _online_gate_phase == 90:
		var stats := world_view.store.statistics()
		if int(stats.scopes) == 0 and int(stats.assets) == 0 and int(stats.decode_jobs) == 0 and int(stats.texture_jobs) == 0:
			if _online_gate_finish_code == 0:
				print("ONLINE_MAIN_GATE_PASS role=",_online_gate_role," resources=0")
			else:
				push_error("ONLINE_MAIN_GATE_FAIL role=%s reason=%s" % [_online_gate_role,_online_gate_finish_reason])
			get_tree().quit(_online_gate_finish_code)
		return
	if Time.get_ticks_msec() > _online_gate_deadline:
		_online_gate_fail("timeout_phase_%d_state_%s_view_%s_status_%s" % [_online_gate_phase,str(_last_snapshot.get("state","none")),world_view.phase,session_panel.status.text])
		return
	if world_view.phase == "failed":
		_online_gate_fail("world_view_failed_"+_status.text.replace(" ","_"))
		return
	if not session_panel.active:
		_online_gate_fail("session_inactive_"+session_panel.status.text)
		return
	if session_panel.characters.visible and not session_panel.choose_button.disabled and session_panel.characters.item_count > 0:
		session_panel.characters.select(0)
		session_panel.choose()
	if _online_gate_role == "observer":
		_online_gate_observer()
	else:
		_online_gate_actor()


func _online_gate_observer() -> void:
	if _online_gate_phase == 0:
		if _online_gate_ready(15):
			print("ONLINE_MAIN_GATE_READY role=observer")
			_online_gate_phase = 1
		return
	var entities: Dictionary = _last_snapshot.get("entities",{})
	if _online_gate_phase == 1:
		for raw_id in entities.keys():
			var id := int(raw_id)
			if id == int(_last_snapshot.get("actor_id",0)):
				continue
			var remote: Dictionary = entities[raw_id]
			_online_gate_remote_id = id
			_online_gate_remote_start = Vector2(float(remote.x),float(remote.y))
			print("ONLINE_MAIN_GATE_SPAWN role=observer actor=",id)
			_online_gate_phase = 2
			return
	elif _online_gate_phase == 2:
		if entities.has(_online_gate_remote_id):
			var remote: Dictionary = entities[_online_gate_remote_id]
			var at := Vector2(float(remote.x),float(remote.y))
			if at.distance_to(_online_gate_remote_start) >= 8.0:
				print("ONLINE_MAIN_GATE_DELTA role=observer actor=",_online_gate_remote_id)
				_online_gate_phase = 3
	elif _online_gate_phase == 3:
		if not entities.has(_online_gate_remote_id):
			print("ONLINE_MAIN_GATE_DESPAWN role=observer actor=",_online_gate_remote_id)
			_online_gate_phase = 4
	elif _online_gate_phase == 4 and entities.has(_online_gate_remote_id):
		print("ONLINE_MAIN_GATE_RETURN_SPAWN role=observer actor=",_online_gate_remote_id)
		_online_gate_finish(true,"")


func _online_gate_actor() -> void:
	if _online_gate_phase == 0:
		if _online_gate_ready(15):
			print("ONLINE_MAIN_GATE_READY role=actor")
			if not session_panel.submit_move(Vector2(808,808)):
				_online_gate_fail("move_enqueue")
				return
			_online_gate_phase = 1
		return
	if _online_gate_phase == 1:
		var at := Vector2(float(_last_snapshot.get("x",0.0)),float(_last_snapshot.get("y",0.0)))
		if at.distance_to(Vector2(808,808)) <= 1.0 and _online_gate_ready(15):
			print("ONLINE_MAIN_GATE_MOVE_CAMERA role=actor")
			_online_gate_not_before = Time.get_ticks_msec() + 350
			_online_gate_phase = 2
	elif _online_gate_phase == 2 and Time.get_ticks_msec() >= _online_gate_not_before:
		if not session_panel.submit_npc_interact(46):
			_online_gate_fail("portal_46_enqueue")
			return
		_online_gate_phase = 3
	elif _online_gate_phase == 3 and _online_gate_ready(13):
		print("ONLINE_MAIN_GATE_MAP13 role=actor")
		_online_gate_not_before = Time.get_ticks_msec() + 350
		_online_gate_phase = 4
	elif _online_gate_phase == 4 and Time.get_ticks_msec() >= _online_gate_not_before:
		if not session_panel.submit_npc_interact(1):
			_online_gate_fail("portal_1_enqueue")
			return
		_online_gate_phase = 5
	elif _online_gate_phase == 5 and _online_gate_ready(15):
		print("ONLINE_MAIN_GATE_RETURN_MAP15 role=actor")
		_online_gate_finish(true,"")


func _online_gate_finish(success: bool, reason: String) -> void:
	_online_gate_finish_code = 0 if success else 1
	_online_gate_finish_reason = reason
	session_panel.disconnect_session()
	world_view.clear_map()
	_online_gate_phase = 90


func _online_gate_fail(reason: String) -> void:
	if _online_gate_phase == 90:
		return
	_online_gate_finish(false,reason)


func _world_status(message: String) -> void:
	_status.text = "地圖 " + str(world_view.map_id) + "｜已連線" if session_panel.active and world_view.phase == "ready" else message
