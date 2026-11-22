extends SceneTree
## Drives a regular main.tscn instance using the formal Host and synthetic assets.
const Main = preload("res://app/main.gd")
const MainScene = preload("res://app/main.tscn")

var _app: Main
var _online_gate_role := ""
var _online_gate_phase := -1
var _online_gate_deadline := 0
var _online_gate_not_before := 0
var _online_gate_remote_id := 0
var _online_gate_remote_start := Vector2.ZERO
var _online_gate_finish_code := -1
var _online_gate_finish_reason := ""
var _online_gate_monster_seen := false
var _walk_last_visual := Vector2.INF
var _walk_last_sample_usec := 0
var _walk_smooth_frames := 0
var _walk_max_visual_step := 0.0
var _walk_invalid_steps := 0


func _initialize() -> void:
	_launch.call_deferred()


func _launch() -> void:
	var args := OS.get_cmdline_user_args()
	for i in range(args.size() - 1):
		if args[i] == "--e2e-role":
			_online_gate_role = args[i + 1]
			break
	_app = MainScene.instantiate() as Main
	root.add_child(_app)
	current_scene = _app
	process_frame.connect(_online_gate_tick)
	_start_online_gate()


func _start_online_gate() -> void:
	if not _online_gate_role in ["observer","actor"]:
		_online_gate_fail("invalid_role")
		return
	var username := OS.get_environment("SGTEST_E2E_USERNAME")
	var password := OS.get_environment("SGTEST_E2E_PASSWORD")
	OS.set_environment("SGTEST_E2E_PASSWORD","")
	if username.is_empty() or password.is_empty() or _app.world_view.phase == "closed":
		password = ""
		_online_gate_fail("missing_credentials_or_pack")
		return
	_app.session_panel.username_input.text = username
	_app.session_panel.password_input.text = password
	password = ""
	_online_gate_deadline = Time.get_ticks_msec() + 25000
	_online_gate_phase = 0
	print("ONLINE_MAIN_GATE_START role=",_online_gate_role)
	_app.session_panel.login()


func _online_gate_ready(map: int) -> bool:
	if _app._last_snapshot.is_empty() or not bool(_app._last_snapshot.get("world_ready",false)):
		return false
	if int(_app._last_snapshot.get("map_id",0)) != map or _app.world_view.phase != "ready" or _app.world_view.map_id != map:
		return false
	var expected := Vector2(float(_app._last_snapshot.get("x",0.0)),float(_app._last_snapshot.get("y",0.0)))
	return _app.world_view.camera.snapshot().focus.distance_to(expected) <= 0.5


func _online_gate_tick() -> void:
	if _online_gate_phase < 0:
		return
	if not _online_gate_monster_seen:
		var monster_state: Dictionary = _app._last_snapshot.get("monsters",{})
		if not monster_state.is_empty() and _app.world_view.monster_count() == monster_state.size():
			_online_gate_monster_seen = true
			print("ONLINE_MAIN_GATE_MONSTER role=",_online_gate_role,
				" count=",monster_state.size()," presented=",_app.world_view.monster_count())
	if _online_gate_phase == 90:
		var stats := _app.world_view.store.statistics()
		if int(stats.scopes) == 0 and int(stats.assets) == 0 and int(stats.decode_jobs) == 0 and int(stats.texture_jobs) == 0:
			if _online_gate_finish_code == 0:
				print("ONLINE_MAIN_GATE_PASS role=",_online_gate_role," resources=0")
			else:
				push_error("ONLINE_MAIN_GATE_FAIL role=%s reason=%s" % [_online_gate_role,_online_gate_finish_reason])
			quit(_online_gate_finish_code)
		return
	if Time.get_ticks_msec() > _online_gate_deadline:
		_online_gate_fail("timeout_phase_%d_state_%s_view_%s_status_%s" % [_online_gate_phase,str(_app._last_snapshot.get("state","none")),_app.world_view.phase,_app.session_panel.status.text])
		return
	if _online_gate_role == "actor" and _online_gate_phase in [1,3]:
		_online_gate_watch_walk()
	if _app.world_view.phase == "failed":
		_online_gate_fail("world_view_failed_"+_app._status.text.replace(" ","_"))
		return
	if not _app.session_panel.active:
		_online_gate_fail("session_inactive_"+_app.session_panel.status.text)
		return
	if _app.session_panel.characters.visible and not _app.session_panel.choose_button.disabled and _app.session_panel.characters.item_count > 0:
		_app.session_panel.characters.select(0)
		_app.session_panel.choose()
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
	var entities: Dictionary = _app._last_snapshot.get("entities",{})
	if _online_gate_phase == 1:
		for raw_id in entities.keys():
			var id := int(raw_id)
			if id == int(_app._last_snapshot.get("actor_id",0)):
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


func _online_gate_watch_walk() -> void:
	var own_id := int(_app._last_snapshot.get("actor_id",0))
	var visual := _app.world_view.players.position_at(own_id,Time.get_ticks_usec())
	if not visual.is_finite():
		return
	# A moving authoritative actor far from the destination must still
	# have a live local walk. This catches the ACK-before-SelfState regression:
	# repeated 8px Server delta updates alone would otherwise look smooth.
	var target := Vector2(952,808) if _online_gate_phase == 1 else Vector2(808,808)
	var own: Dictionary = _app._last_snapshot.get("self_state",{})
	if bool(own.get("moving",false)) and visual.distance_to(target)>32.0 and \
			not _app.world_view.local_movement.active:
		_online_gate_fail("server_moving_without_local_route_at_%s" % str(visual))
		return
	var now_usec := Time.get_ticks_usec()
	if _walk_last_visual.is_finite():
		var dt := clampf(float(now_usec - _walk_last_sample_usec)/1000000.0,0.0,0.25)
		var travel := visual.distance_to(_walk_last_visual)
		_walk_max_visual_step = maxf(_walk_max_visual_step,travel)
		if travel > 0.1:
			_walk_smooth_frames += 1
		if travel > 240.0*dt+10.0:
			_walk_invalid_steps += 1
			_online_gate_fail("own_visual_teleport_displacement_%.1f_dt_ms_%.1f" % [travel,dt*1000.0])
			return
	_walk_last_sample_usec = now_usec
	_walk_last_visual = visual


func _online_gate_actor() -> void:
	if _online_gate_phase == 0:
		if _online_gate_ready(15):
			print("ONLINE_MAIN_GATE_READY role=actor")
			# Drive the same terrain-click path as gameplay: start prediction
			# synchronously and leave the Server authoritative MoveTarget queued.
			var own_id := int(_app._last_snapshot.get("actor_id",0))
			var before := _app.world_view.players.position_at(own_id,Time.get_ticks_usec())
			_app._begin_ground_navigation(Vector2(952,808),Time.get_ticks_msec())
			if not _app.world_view.local_movement.active:
				_online_gate_fail("local_prediction_not_started_ready_%s_pos_%s_phase_%s_own_%s" % [str(_app.world_view.local_movement.ready),str(_app.world_view.local_movement.position),str(_app.world_view.phase),str(_app._last_snapshot.get("self_state",{}))])
				return
			_app.world_view.local_movement.advance(1.0/60.0)
			var ahead := _app.world_view.local_movement.position
			if not ahead.is_finite() or ahead.distance_to(before)<=0.01:
				_online_gate_fail("local_move_waited_for_server")
				return
			print("ONLINE_MAIN_GATE_LOCAL_MOVE role=actor pre_server_receipt=true")
			_online_gate_phase = 1
		return
	if _online_gate_phase == 1:
		var at := Vector2(float(_app._last_snapshot.get("x",0.0)),float(_app._last_snapshot.get("y",0.0)))
		if at.distance_to(Vector2(952,808)) <= 1.0 and _online_gate_ready(15):
			print("ONLINE_MAIN_GATE_MOVE_CAMERA role=actor")
			_app._begin_ground_navigation(Vector2(808,808),Time.get_ticks_msec())
			_online_gate_phase = 3
	elif _online_gate_phase == 3:
		var at := Vector2(float(_app._last_snapshot.get("x",0.0)),float(_app._last_snapshot.get("y",0.0)))
		if at.distance_to(Vector2(808,808)) <= 1.0 and _online_gate_ready(15):
			if _walk_smooth_frames < 25 or _walk_invalid_steps != 0:
				_online_gate_fail("own_walk_not_continuous_frames_%d_invalid_%d" % [_walk_smooth_frames,_walk_invalid_steps])
				return
			print("ONLINE_MAIN_GATE_MOTION_SMOOTH role=actor samples=",_walk_smooth_frames," max_step=",snappedf(_walk_max_visual_step,0.01))
			_online_gate_not_before = Time.get_ticks_msec() + 350
			_online_gate_phase = 4
	elif _online_gate_phase == 4 and Time.get_ticks_msec() >= _online_gate_not_before:
		if not _app.session_panel.submit_npc_interact(46):
			_online_gate_fail("portal_46_enqueue")
			return
		_online_gate_phase = 5
	elif _online_gate_phase == 5 and _online_gate_ready(13):
		print("ONLINE_MAIN_GATE_MAP13 role=actor")
		_online_gate_not_before = Time.get_ticks_msec() + 350
		_online_gate_phase = 6
	elif _online_gate_phase == 6 and Time.get_ticks_msec() >= _online_gate_not_before:
		if not _app.session_panel.submit_npc_interact(1):
			_online_gate_fail("portal_1_enqueue")
			return
		_online_gate_phase = 7
	elif _online_gate_phase == 7 and _online_gate_ready(15):
		print("ONLINE_MAIN_GATE_RETURN_MAP15 role=actor")
		_online_gate_finish(true,"")


func _online_gate_finish(success: bool, reason: String) -> void:
	_online_gate_finish_code = 0 if success else 1
	_online_gate_finish_reason = reason
	_app.session_panel.disconnect_session()
	_app.world_view.clear_map()
	_online_gate_phase = 90


func _online_gate_fail(reason: String) -> void:
	if _online_gate_phase == 90:
		return
	_online_gate_finish(false,reason)
