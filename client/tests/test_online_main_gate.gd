extends SceneTree
## Drives a regular main.tscn instance using the formal Host and synthetic assets.
const Main = preload("res://app/main.gd")
const MainScene = preload("res://app/main.tscn")
const UiSkinCatalog = preload("res://ui/ui_skin_catalog.gd")
const WorldState = preload("res://world/world_state.gd")
const TRANSFER_CYCLES := 10
const CHAT_E2E_TEXT := "主介面聊天 E2E"
const WHISPER_E2E_TEXT := "主介面私訊 E2E"

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
var _character_proof_requested := false
var _character_proof_ready := false
var _player_create_requested := false
var _walk_last_visual := Vector2.INF
var _walk_last_sample_usec := 0
var _walk_smooth_frames := 0
var _walk_max_visual_step := 0.0
var _walk_invalid_steps := 0
var _hold_targets: Array[Vector2] = [
	Vector2(1032,808),Vector2(1064,856),Vector2(1048,920),
	Vector2(1000,968),Vector2(936,984),Vector2(872,952),
	Vector2(824,904),Vector2(808,856),Vector2(840,808)
]
var _hold_index := 0
var _hold_next_msec := 0
var _hold_large_corrections_before := 0
var _transfer_cycle := 0
var _map13_move_start := Vector2.ZERO
var _chat_proof_seen := false
var _whisper_proof_seen := false
var _hud_proof_seen := false
var _manual_input_required := false
var _manual_click_seen := false
var _manual_click_screen := Vector2.ZERO
var _manual_click_world_start := Vector2.ZERO
var _real_transfer_mode := false
var _real_observer_departures := 0
var _real_observer_returns := 0


func _initialize() -> void:
	_launch.call_deferred()


func _launch() -> void:
	# Headless SceneTree defaults to 64x64, which falsely despawns visible NPCs
	# when following a player. Match the real production 1280x720 viewport.
	root.size = Vector2i(1280,720)
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
	_manual_input_required = OS.get_environment("SGTEST_E2E_REQUIRE_MANUAL_INPUT") == "1"
	_real_transfer_mode = OS.get_environment("SGTEST_E2E_REAL_TRANSFER") == "1"
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
	_online_gate_deadline = Time.get_ticks_msec() + (150000 if _real_transfer_mode else (120000 if _manual_input_required else 90000))
	_online_gate_phase = 0
	print("ONLINE_MAIN_GATE_START role=",_online_gate_role)
	_app.session_panel.login()


func _world_surface_ready(map: int) -> bool:
	if not _app.world_view.visible or _app.world_view.phase != "ready" or _app.world_view.map_id != map:
		return false
	var own_id := int(_app._last_snapshot.get("actor_id",0))
	if own_id <= 0:
		return false
	var commands: Array = _app.world_view._canvas._commands
	if not commands.any(func(c): return str(c.get("actor_id","")) == str(own_id)):
		return false
	# Map15 in this formal gate contains a rendered source NPC. Requiring its
	# draw command prevents a full-screen HK shell regression from hiding the
	# actual map/character/NPC world while protocol state still looks healthy.
	if map == 15 and not commands.any(func(c): return str(c.get("actor_id","")) == "npc:46"):
		return false
	if _app._screen_backdrop.visible or _app._photo_backdrop.visible:
		return false
	if UiSkinCatalog.valid(UiSkinCatalog.DEFAULT_SKIN) and not _app.classic_world.visible:
		return false
	return true


func _map_generation_ready(map: int) -> bool:
	if _app._last_snapshot.is_empty() or not bool(_app._last_snapshot.get("world_ready",false)):
		return false
	return int(_app._last_snapshot.get("map_id",0)) == map \
		and _app.world_view.phase == "ready" and _app.world_view.map_id == map \
		and _world_surface_ready(map)


func _online_gate_ready(map: int) -> bool:
	if not _map_generation_ready(map):
		return false
	var expected := Vector2(float(_app._last_snapshot.get("x",0.0)),float(_app._last_snapshot.get("y",0.0)))
	return _app.world_view.camera.snapshot().focus.distance_to(expected) <= 0.5


func _live_hud_authority_ready() -> bool:
	if _hud_proof_seen:
		return true
	var snap:Dictionary=_app._last_snapshot
	if not bool(snap.get("player_profile_ready",false)):
		return false
	var level:=int(snap.get("player_level",0))
	var hp:=int(snap.get("player_current_hp",-1));var hpmax:=int(snap.get("player_max_hp",0))
	var mp:=int(snap.get("player_current_mp",-1));var mpmax:=int(snap.get("player_max_mp",0))
	var wp:=int(snap.get("player_current_wp",-1));var wpmax:=int(snap.get("player_max_wp",0))
	var money:=int(snap.get("player_money",-1))
	var exp_percent:=int(snap.get("player_experience_percent",-1))
	var require_exp:=OS.get_environment("SGTEST_E2E_REQUIRE_HUD_EXP")=="1"
	if level<=0 or hp<0 or hpmax<=0 or mp<0 or mpmax<=0 or wp<0 or wpmax<=0 or money<0:
		return false
	if require_exp and exp_percent<0:
		return false
	var shell=_app.classic_world
	if shell._level_label.text!=str(level) or shell._hp_label.text!="%d/%d"%[hp,hpmax] or \
			shell._mp_label.text!="%d/%d"%[mp,mpmax] or shell._wp_label.text!="%d/%d"%[wp,wpmax] or \
			shell._status_value_label.text!=str(money):
		return false
	if require_exp and shell._exp_percent_label.text!="%d%%"%exp_percent:
		return false
	if not is_equal_approx(shell._hud_hp_clip.size.x,92.0*float(hp)/float(hpmax)) or \
			not is_equal_approx(shell._hud_mp_clip.size.x,92.0*float(mp)/float(mpmax)) or \
			not is_equal_approx(shell._hud_wp_clip.size.x,92.0*float(wp)/float(wpmax)):
		return false
	if require_exp and not is_equal_approx(shell._hud_exp_clip.size.x,92.0*float(exp_percent)/100.0):
		return false
	_hud_proof_seen=true
	print("ONLINE_MAIN_GATE_HUD_AUTHORITY role=",_online_gate_role,
		" level=",level," hp=",hp,"/",hpmax," mp=",mp,"/",mpmax,
		" wp=",wp,"/",wpmax," money=",money," exp_percent=",exp_percent,
		" source_server=true ui_match=true")
	return true


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
	if _online_gate_role == "actor" and _online_gate_phase in [1,3,31,32,33]:
		_online_gate_watch_walk()
	if _app.world_view.phase == "failed":
		_online_gate_fail("world_view_failed_"+_app._status.text.replace(" ","_"))
		return
	if not _app.session_panel.active:
		_online_gate_fail("session_inactive_"+_app.session_panel.status.text)
		return
	var player_create_name:=OS.get_environment("SGTEST_E2E_PLAYER_CREATE_NAME").strip_edges()
	if int(_app.session_panel._latest.get("state",-1))==WorldState.STATE_SELECTING_CHARACTER and \
			_app.session_panel.characters.item_count==0 and not player_create_name.is_empty():
		if not _player_create_requested:
			var panel=_app.session_panel
			panel.character_name_input.text=player_create_name
			panel.character_slot_input.value=0
			panel.character_strength_input.value=2
			panel.character_dexterity_input.value=2
			panel.character_constitution_input.value=2
			panel.character_wisdom_input.value=2
			panel.character_password_input.text="2468"
			panel.character_password_confirm_input.text="2468"
			panel.create_character()
			if panel.character_create_status.text!="建立角色中…":
				_online_gate_fail("player_character_create_not_queued_"+panel.character_create_status.text)
				return
			_player_create_requested=true
		return
	if _app.session_panel.characters.visible and not _app.session_panel.choose_button.disabled and _app.session_panel.characters.item_count > 0:
		_app.session_panel.characters.select(0)
		if _player_create_requested and not _character_proof_ready:
			_character_proof_ready=true
			print("ONLINE_MAIN_GATE_PLAYER_CREATE role=",_online_gate_role," authority=PASS")
		# CLI-provisioned synthetic characters intentionally bypass normal
		# creation UI, so migrate their secondary proof through SessionPanel once
		# before exercising the real choose-password field/button path.
		if not _character_proof_ready:
			if not _character_proof_requested:
				var listed: Array = _app.session_panel._latest.get("characters",[])
				if listed.is_empty():
					return
				if not _app.session_panel.change_character_password(int(listed[0].character_id),"","2468"):
					_online_gate_fail("character_proof_migration_rejected")
					return
				_character_proof_requested = true
				return
			var manage_revision := int(_app.session_panel._latest.get("character_manage_result_revision",0))
			if manage_revision <= 0:
				return
			var manage_status := int(_app.session_panel._latest.get("character_manage_status",-1))
			if manage_status not in [0,2]:
				_online_gate_fail("character_proof_migration_status_%d" % manage_status)
				return
			_character_proof_ready = true
		_app.session_panel.character_choose_password_input.text = "2468"
		_app.session_panel.choose()
	if _real_transfer_mode:
		if _online_gate_role == "observer":
			_real_transfer_observer()
		else:
			_real_transfer_actor()
		return
	if _online_gate_role == "observer":
		_online_gate_observer()
	else:
		_online_gate_actor()


func _real_npc_ready(spawn_id: int, expected_count: int) -> bool:
	if _app.world_view.npc_count() != expected_count:
		return false
	return _app.world_view._canvas._commands.any(
		func(c): return str(c.get("actor_id","")) == "npc:%d" % spawn_id)


func _real_controller_matches_generation() -> bool:
	var controller = _app.world_view.local_movement
	return (
		controller.ready
		and controller.actor_id == int(_app._last_snapshot.get("actor_id",0))
		and controller.map_epoch == int(_app._last_snapshot.get("map_epoch",0))
	)


func _real_transfer_actor() -> void:
	if _online_gate_phase == 0:
		if not _map_generation_ready(15) or not _real_npc_ready(46,52):
			return
		if not _live_hud_authority_ready():
			return
		if not _real_controller_matches_generation():
			_online_gate_fail("real_map15_controller_generation_mismatch")
			return
		print("ONLINE_MAIN_GATE_REAL_READY role=actor map=15 npcs=52 source_portal=46")
		_online_gate_not_before = Time.get_ticks_msec() + 600
		_online_gate_phase = 100
	elif _online_gate_phase == 100 and Time.get_ticks_msec() >= _online_gate_not_before:
		if not _app.session_panel.submit_npc_interact(46):
			_online_gate_fail("real_portal_46_enqueue_cycle_%d" % (_transfer_cycle+1))
			return
		_online_gate_phase = 101
	elif _online_gate_phase == 101:
		if not _map_generation_ready(13) or not _real_npc_ready(1,3):
			return
		if not _real_controller_matches_generation():
			_online_gate_fail("real_map13_controller_generation_mismatch_cycle_%d" % (_transfer_cycle+1))
			return
		_map13_move_start = Vector2(
			float(_app._last_snapshot.get("x",0.0)),
			float(_app._last_snapshot.get("y",0.0)))
		_app._begin_ground_navigation(Vector2(2528,2864),Time.get_ticks_msec())
		if not _app.world_view.local_movement.active:
			_online_gate_fail("real_map13_local_prediction_not_started_cycle_%d" % (_transfer_cycle+1))
			return
		_online_gate_phase = 102
	elif _online_gate_phase == 102:
		var at13 := Vector2(
			float(_app._last_snapshot.get("x",0.0)),
			float(_app._last_snapshot.get("y",0.0)))
		var seq13 := int(_app._last_snapshot.get("movement_sequence",0))
		if (
			at13.distance_to(_map13_move_start) >= 16.0
			and _map_generation_ready(13)
			and seq13 > 0
			and int(_app._last_snapshot.get("last_receipt_sequence",0)) == seq13
			and int(_app._last_snapshot.get("last_receipt_status",-1)) == 0
		):
			print("ONLINE_MAIN_GATE_REAL_MAP13_MOVE_ACK role=actor cycle=",_transfer_cycle+1,
				" from=",_map13_move_start," to=",at13)
			_online_gate_not_before = Time.get_ticks_msec() + 350
			_online_gate_phase = 103
	elif _online_gate_phase == 103 and Time.get_ticks_msec() >= _online_gate_not_before:
		if not _app.session_panel.submit_npc_interact(1):
			_online_gate_fail("real_portal_1_enqueue_cycle_%d" % (_transfer_cycle+1))
			return
		_online_gate_phase = 104
	elif _online_gate_phase == 104:
		if not _map_generation_ready(15) or not _real_npc_ready(46,52):
			return
		if not _real_controller_matches_generation():
			_online_gate_fail("real_return_map15_controller_generation_mismatch_cycle_%d" % (_transfer_cycle+1))
			return
		_map13_move_start = Vector2(
			float(_app._last_snapshot.get("x",0.0)),
			float(_app._last_snapshot.get("y",0.0)))
		_app._begin_ground_navigation(Vector2(7904,11328),Time.get_ticks_msec())
		if not _app.world_view.local_movement.active:
			_online_gate_fail("real_map15_local_prediction_not_started_cycle_%d" % (_transfer_cycle+1))
			return
		_online_gate_phase = 105
	elif _online_gate_phase == 105:
		var at15 := Vector2(
			float(_app._last_snapshot.get("x",0.0)),
			float(_app._last_snapshot.get("y",0.0)))
		var seq15 := int(_app._last_snapshot.get("movement_sequence",0))
		if (
			at15.distance_to(_map13_move_start) >= 16.0
			and _map_generation_ready(15)
			and seq15 > 0
			and int(_app._last_snapshot.get("last_receipt_sequence",0)) == seq15
			and int(_app._last_snapshot.get("last_receipt_status",-1)) == 0
		):
			print("ONLINE_MAIN_GATE_REAL_MAP15_MOVE_ACK role=actor cycle=",_transfer_cycle+1,
				" from=",_map13_move_start," to=",at15)
			_transfer_cycle += 1
			if _transfer_cycle >= TRANSFER_CYCLES:
				print("ONLINE_MAIN_GATE_REAL_TRANSFER_STRESS_PASS role=actor cycles=",_transfer_cycle)
				_online_gate_finish(true,"")
				return
			_online_gate_not_before = Time.get_ticks_msec() + 600
			_online_gate_phase = 106
	elif _online_gate_phase == 106 and Time.get_ticks_msec() >= _online_gate_not_before:
		if not _app.session_panel.submit_npc_interact(46):
			_online_gate_fail("real_portal_46_enqueue_cycle_%d" % (_transfer_cycle+1))
			return
		_online_gate_phase = 101


func _real_transfer_observer() -> void:
	var entities: Dictionary = _app._last_snapshot.get("entities",{})
	var own_id := int(_app._last_snapshot.get("actor_id",0))
	if _online_gate_phase == 0:
		if not _map_generation_ready(15) or not _real_npc_ready(46,52):
			return
		if not _live_hud_authority_ready():
			return
		print("ONLINE_MAIN_GATE_REAL_READY role=observer map=15 npcs=52 source_portal=46")
		_online_gate_phase = 1
	elif _online_gate_phase == 1:
		for raw_id in entities.keys():
			var id := int(raw_id)
			if id == own_id:
				continue
			if not _app.world_view._canvas._commands.any(
					func(c): return str(c.get("actor_id","")) == str(id)):
				continue
			_online_gate_remote_id = id
			print("ONLINE_MAIN_GATE_REAL_REMOTE_PRESENT role=observer actor=",id)
			_online_gate_phase = 2
			return
	elif _online_gate_phase == 2:
		if not entities.has(_online_gate_remote_id):
			_real_observer_departures += 1
			print("ONLINE_MAIN_GATE_REAL_DESPAWN role=observer cycle=",_real_observer_departures,
				" actor=",_online_gate_remote_id)
			_online_gate_phase = 3
	elif _online_gate_phase == 3 and entities.has(_online_gate_remote_id):
		var remote_count := 0
		for raw_id in entities.keys():
			if int(raw_id) != own_id:
				remote_count += 1
		if remote_count != 1:
			_online_gate_fail("real_return_spawn_duplicate_remote_count_%d" % remote_count)
			return
		_real_observer_returns += 1
		print("ONLINE_MAIN_GATE_REAL_RETURN_SPAWN role=observer cycle=",_real_observer_returns,
			" actor=",_online_gate_remote_id)
		if _real_observer_returns >= TRANSFER_CYCLES:
			if _real_observer_departures != TRANSFER_CYCLES:
				_online_gate_fail("real_aoi_cycle_count_%d_%d" % [
					_real_observer_departures,_real_observer_returns])
				return
			print("ONLINE_MAIN_GATE_REAL_AOI_STRESS_PASS role=observer cycles=",_real_observer_returns)
			_online_gate_finish(true,"")
			return
		_online_gate_phase = 2


func _online_gate_observer() -> void:
	if _online_gate_phase == 0:
		if _online_gate_ready(15):
			if not _live_hud_authority_ready():
				return
			print("ONLINE_MAIN_GATE_WORLD_SURFACE role=observer map=15 own=PASS npc=PASS hk_ui=",UiSkinCatalog.valid(UiSkinCatalog.DEFAULT_SKIN))
			print("ONLINE_MAIN_GATE_READY role=observer")
			if _manual_input_required:
				var manual_target_world := Vector2(952,808)
				var manual_target_screen := _app.world_view.camera.world_to_screen(manual_target_world)
				print("ONLINE_MAIN_GATE_MANUAL_INPUT_REQUIRED role=observer start=",Vector2(float(_app._last_snapshot.get("x",0.0)),float(_app._last_snapshot.get("y",0.0))))
				print("ONLINE_MAIN_GATE_MANUAL_TARGET role=observer screen=",manual_target_screen," world=",manual_target_world)
				_online_gate_phase = 10
			else:
				_online_gate_phase = 1
		return
	if _online_gate_phase == 10:
		var manual_at := Vector2(float(_app._last_snapshot.get("x",0.0)),float(_app._last_snapshot.get("y",0.0)))
		if not _manual_click_seen and Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT):
			_manual_click_seen = true
			_manual_click_screen = root.get_mouse_position()
			_manual_click_world_start = manual_at
			print("ONLINE_MAIN_GATE_MANUAL_CLICK_SEEN role=observer screen=",_manual_click_screen," world_start=",_manual_click_world_start)
		if _manual_click_seen and manual_at.distance_to(_manual_click_world_start) >= 8.0:
			print("ONLINE_MAIN_GATE_MANUAL_INPUT_PASS role=observer screen=",_manual_click_screen," start=",_manual_click_world_start," end=",manual_at)
			_online_gate_phase = 1
		return
	var entities: Dictionary = _app._last_snapshot.get("entities",{})
	if _online_gate_phase == 1:
		for raw_id in entities.keys():
			var id := int(raw_id)
			if id == int(_app._last_snapshot.get("actor_id",0)):
				continue
			if not _app.world_view._canvas._commands.any(func(c): return str(c.get("actor_id","")) == str(id)):
				continue
			var remote: Dictionary = entities[raw_id]
			_online_gate_remote_id = id
			_online_gate_remote_start = Vector2(float(remote.x),float(remote.y))
			print("ONLINE_MAIN_GATE_REMOTE_PRESENTED role=observer actor=",id)
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
		if not _chat_proof_seen and entities.has(_online_gate_remote_id):
			var messages: Array = _app._last_snapshot.get("chat_messages",[])
			var delivered := messages.any(func(entry):
				return entry is Dictionary and int(entry.get("channel",-1))==0 and \
					int(entry.get("sender_actor_id",0))==_online_gate_remote_id and \
					str(entry.get("message",""))==CHAT_E2E_TEXT)
			var history := _app.classic_world._chat_history.text.contains(CHAT_E2E_TEXT)
			var bubble := _app.world_view._chat_balloons.any(func(entry):
				return int(entry.get("actor_id",0))==_online_gate_remote_id and \
					str(entry.get("text",""))==CHAT_E2E_TEXT)
			if delivered and history and bubble:
				_chat_proof_seen = true
				print("ONLINE_MAIN_GATE_CHAT_UI_DELIVERY role=observer history=PASS bubble=PASS")
		if not _whisper_proof_seen and entities.has(_online_gate_remote_id):
			var messages: Array = _app._last_snapshot.get("chat_messages",[])
			var delivered := messages.any(func(entry):
				return entry is Dictionary and int(entry.get("channel",-1))==4 and \
					int(entry.get("sender_actor_id",0))==_online_gate_remote_id and \
					str(entry.get("message",""))==WHISPER_E2E_TEXT)
			var history := _app.classic_world._chat_history.text.contains(WHISPER_E2E_TEXT)
			if delivered and history:
				_whisper_proof_seen = true
				print("ONLINE_MAIN_GATE_WHISPER_UI_DELIVERY role=observer authority=PASS history=PASS")
		if not entities.has(_online_gate_remote_id):
			if not _chat_proof_seen:
				_online_gate_fail("chat_ui_delivery_missing_before_despawn")
				return
			if not _whisper_proof_seen:
				_online_gate_fail("whisper_ui_delivery_missing_before_despawn")
				return
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
	if _online_gate_phase in [1,3,33] and bool(own.get("moving",false)) and \
			visual.distance_to(target)>32.0 and not _app.world_view.local_movement.active:
		_online_gate_fail("server_moving_without_local_route_at_%s" % str(visual))
		return
	# This synthetic scene now includes a real rendered NPC atlas and a
	# source name. A single malformed moving player frame must never wipe
	# the NPC, our paper doll, or the name while the camera follows.
	if _walk_smooth_frames > 6 and _app.world_view.phase == "ready":
		var view = _app.world_view
		var commands: Array = view._canvas._commands
		var npc_id := "npc:46"
		var npc_sprite := commands.any(func(c):return str(c.actor_id)==npc_id)
		var own_sprite := commands.any(func(c):return str(c.actor_id)==str(own_id))
		var name_visible: bool = view._canvas._player_labels.any(
			func(l):return str(l.actor_id)==npc_id and str(l.text)=="測試傳點管理員")
		if not npc_sprite or not own_sprite or not name_visible:
			print("NPC_DRAW_DIAG rect=",view.camera.view_rect(),
				" actor=",visual," NPC_bounds=",view.npcs._entries[0].bounds if view.npcs.count()>0 else Rect2(),
				" NPC_in_rect=",view.npcs.actors_at(view.camera.view_rect()).size())
			var ids := commands.map(func(c):return str(c.get("actor_id","")))
			var names: Array = view._canvas._player_labels.map(func(l):return [str(l.get("actor_id","")),str(l.get("text",""))])
			_online_gate_fail("world_canvas_missing_npc_%s_own_%s_name_%s_phase_%s_npcs_%d_error_%d_skipped_%d_%d_ids_%s_labels_%s" %
				[str(npc_sprite),str(own_sprite),str(name_visible),view.phase,
				view.npc_count(),view.draw_list.error,view.draw_list.skipped_actors,
				view.draw_list.skipped_frames,str(ids.slice(0,28)),str(names.slice(0,8))])
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
			if not _live_hud_authority_ready():
				return
			print("ONLINE_MAIN_GATE_WORLD_SURFACE role=actor map=15 own=PASS npc=PASS hk_ui=",UiSkinCatalog.valid(UiSkinCatalog.DEFAULT_SKIN))
			print("ONLINE_MAIN_GATE_READY role=actor")
			# Drive the same terrain-click path as gameplay: start prediction
			# synchronously and leave the Server authoritative MoveTarget queued.
			var own_id := int(_app._last_snapshot.get("actor_id",0))
			var before := _app.world_view.players.position_at(own_id,Time.get_ticks_usec())
			_app._begin_ground_navigation(Vector2(952,808),Time.get_ticks_msec())
			var obstacle_route = _app.world_view.local_movement
			if not obstacle_route._search.is_empty() or obstacle_route._route.size()<2:
				_online_gate_fail("obstacle_route_not_complete_before_walk_target_%s_route_%s_search_%s" % [
					str(obstacle_route._target_cell),str(obstacle_route._route),str(obstacle_route._search)])
				return
			print("ONLINE_MAIN_GATE_OBSTACLE_ROUTE role=actor waypoints=",obstacle_route._route.size())
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
			if not _app.classic_world.visible:
				_online_gate_fail("classic_world_not_visible_for_chat_ui")
				return
			# This baseline proves nearby AOI delivery/bubbles. The production
			# Classic UI intentionally starts on original type 0 = World, so select
			# original type 4 = Nearby explicitly instead of relying on stale state.
			_app.classic_world._chat_send_channel=4
			_app.classic_world._chat_input.text=CHAT_E2E_TEXT
			_app.classic_world._chat_input.text_submitted.emit(CHAT_E2E_TEXT)
			if not _app.classic_world._chat_input.text.is_empty():
				_online_gate_fail("chat_ui_submit_not_queued")
				return
			print("ONLINE_MAIN_GATE_CHAT_UI_SUBMIT role=actor input=PASS")
			_online_gate_phase = 11
	elif _online_gate_phase == 11:
		var own_id := int(_app._last_snapshot.get("actor_id",0))
		var messages: Array = _app._last_snapshot.get("chat_messages",[])
		var echoed := messages.any(func(entry):
			return entry is Dictionary and int(entry.get("channel",-1))==0 and \
				int(entry.get("sender_actor_id",0))==own_id and \
				str(entry.get("message",""))==CHAT_E2E_TEXT)
		var history := _app.classic_world._chat_history.text.contains(CHAT_E2E_TEXT)
		var bubble := _app.world_view._chat_balloons.any(func(entry):
			return int(entry.get("actor_id",0))==own_id and str(entry.get("text",""))==CHAT_E2E_TEXT)
		if echoed and history and bubble:
			print("ONLINE_MAIN_GATE_CHAT_UI_ECHO role=actor authority=PASS history=PASS bubble=PASS")
			# EntityDelta intentionally does not expose character names. The isolated
			# runner provisions both exact characters, so pass the known peer name
			# as test environment instead of weakening production AOI identity data.
			var target_name := OS.get_environment("SGTEST_E2E_WHISPER_TARGET_NAME").strip_edges()
			if target_name.is_empty():
				_online_gate_fail("whisper_target_name_missing")
				return
			var command := "/name %s %s" % [target_name,WHISPER_E2E_TEXT]
			_app.classic_world._chat_input.text=command
			_app.classic_world._chat_input.text_submitted.emit(command)
			if not _app.classic_world._chat_input.text.is_empty():
				_online_gate_fail("whisper_ui_submit_not_queued")
				return
			print("ONLINE_MAIN_GATE_WHISPER_UI_SUBMIT role=actor input=PASS")
			_online_gate_phase = 12
	elif _online_gate_phase == 12:
		var own_id := int(_app._last_snapshot.get("actor_id",0))
		var messages: Array = _app._last_snapshot.get("chat_messages",[])
		var echoed := messages.any(func(entry):
			return entry is Dictionary and int(entry.get("channel",-1))==4 and \
				str(entry.get("direction",""))=="outgoing" and \
				str(entry.get("message",""))==WHISPER_E2E_TEXT)
		var history := _app.classic_world._chat_history.text.contains(WHISPER_E2E_TEXT)
		var authority_ok := int(_app._last_snapshot.get("pending_whisper_sequence",0))==0 and \
			int(_app._last_snapshot.get("last_whisper_result_status",-1))==0
		if echoed and history and authority_ok:
			print("ONLINE_MAIN_GATE_WHISPER_UI_ECHO role=actor authority=PASS history=PASS")
			_app._begin_ground_navigation(Vector2(808,808),Time.get_ticks_msec())
			_online_gate_phase = 3
	elif _online_gate_phase == 3:
		var at := Vector2(float(_app._last_snapshot.get("x",0.0)),float(_app._last_snapshot.get("y",0.0)))
		if at.distance_to(Vector2(808,808)) <= 1.0 and _online_gate_ready(15):
			if _walk_smooth_frames < 25 or _walk_invalid_steps != 0:
				_online_gate_fail("own_walk_not_continuous_frames_%d_invalid_%d" % [_walk_smooth_frames,_walk_invalid_steps])
				return
			print("ONLINE_MAIN_GATE_MOTION_SMOOTH role=actor samples=",_walk_smooth_frames," max_step=",snappedf(_walk_max_visual_step,0.01))
			_hold_index = 0
			_hold_next_msec = Time.get_ticks_msec()
			_hold_large_corrections_before = _app.world_view.local_movement.large_correction_events
			_online_gate_deadline = maxi(_online_gate_deadline,Time.get_ticks_msec()+15000)
			print("ONLINE_MAIN_GATE_LONG_HOLD_START role=actor")
			_online_gate_phase = 31
	elif _online_gate_phase == 31:
		var now := Time.get_ticks_msec()
		if _hold_index < _hold_targets.size() and now >= _hold_next_msec:
			var movement_sequence := int(_app._last_snapshot.get("movement_sequence",0))
			var receipt_sequence := int(_app._last_snapshot.get("last_receipt_sequence",0))
			if movement_sequence > receipt_sequence:
				return
			var receipt_status := int(_app._last_snapshot.get("last_receipt_status",-1))
			if movement_sequence > 0 and receipt_sequence == movement_sequence and receipt_status != 0:
				_online_gate_fail("long_hold_server_rejected_%d_seq_%d" % [receipt_status,movement_sequence])
				return
			var own: Dictionary = _app._last_snapshot.get("self_state",{})
			var controller = _app.world_view.local_movement
			if not _app._autorun_retarget_server_safe(
					movement_sequence,receipt_sequence,receipt_status,
					int(own.get("movement_action_sequence",0)),controller):
				return
			var target: Vector2 = _hold_targets[_hold_index]
			if not _app._submit_autorun_target(target,now,not controller.moving):
				_online_gate_fail("long_hold_retarget_enqueue_%d" % _hold_index)
				return
			_hold_index += 1
			_hold_next_msec = now + 100
			return
		if _hold_index >= _hold_targets.size():
			_online_gate_phase = 32
	elif _online_gate_phase == 32:
		var target: Vector2 = _hold_targets[_hold_targets.size()-1]
		var at := Vector2(float(_app._last_snapshot.get("x",0.0)),float(_app._last_snapshot.get("y",0.0)))
		if at.distance_to(target) <= 1.0 and _online_gate_ready(15):
			var controller = _app.world_view.local_movement
			if controller.large_correction_events != _hold_large_corrections_before:
				_online_gate_fail("long_hold_large_correction_count_%d_max_%.1f" % [
					controller.large_correction_events-_hold_large_corrections_before,
					controller.max_correction_distance])
				return
			print("ONLINE_MAIN_GATE_LONG_HOLD_PASS role=actor targets=",_hold_targets.size(),
				" corrections=",controller.correction_events," max=",snappedf(controller.max_correction_distance,0.1))
			_app._begin_ground_navigation(Vector2(808,808),Time.get_ticks_msec())
			_online_gate_phase = 33
	elif _online_gate_phase == 33:
		var at := Vector2(float(_app._last_snapshot.get("x",0.0)),float(_app._last_snapshot.get("y",0.0)))
		if at.distance_to(Vector2(808,808)) <= 1.0 and _online_gate_ready(15):
			_online_gate_not_before = Time.get_ticks_msec() + 350
			_online_gate_phase = 4
	elif _online_gate_phase == 4 and Time.get_ticks_msec() >= _online_gate_not_before:
		if not _app.session_panel.submit_npc_interact(46):
			_online_gate_fail("portal_46_enqueue")
			return
		_online_gate_phase = 5
	elif _online_gate_phase == 5 and _online_gate_ready(13):
		print("ONLINE_MAIN_GATE_MAP13 role=actor cycle=",_transfer_cycle+1)
		# A new WorldReady must immediately own a fresh movement generation on
		# both directions, not only after returning to Map15.
		_map13_move_start = Vector2(
			float(_app._last_snapshot.get("x",0.0)),
			float(_app._last_snapshot.get("y",0.0)))
		var map13_target := Vector2(840,800)
		_app._begin_ground_navigation(map13_target,Time.get_ticks_msec())
		if not _app.world_view.local_movement.active:
			var controller = _app.world_view.local_movement
			_online_gate_fail("map13_local_prediction_not_started ready_%s epoch_%d actor_%d pos_%s" % [
				str(controller.ready),controller.map_epoch,controller.actor_id,str(controller.position)
			])
			return
		_online_gate_phase = 6
	elif _online_gate_phase == 6:
		var at13 := Vector2(float(_app._last_snapshot.get("x",0.0)),float(_app._last_snapshot.get("y",0.0)))
		var movement_sequence13 := int(_app._last_snapshot.get("movement_sequence",0))
		if at13.distance_to(_map13_move_start) >= 16.0 and _map_generation_ready(13) and movement_sequence13 > 0 \
				and int(_app._last_snapshot.get("last_receipt_sequence",0)) == movement_sequence13 \
				and int(_app._last_snapshot.get("last_receipt_status",-1)) == 0:
			print("ONLINE_MAIN_GATE_MAP13_MOVE_ACK role=actor cycle=",_transfer_cycle+1,
				" from=",_map13_move_start," to=",at13)
			_online_gate_not_before = Time.get_ticks_msec() + 350
			_online_gate_phase = 61
	elif _online_gate_phase == 61 and Time.get_ticks_msec() >= _online_gate_not_before:
		if not _app.session_panel.submit_npc_interact(1):
			_online_gate_fail("portal_1_enqueue")
			return
		_online_gate_phase = 7
	elif _online_gate_phase == 7 and _online_gate_ready(15):
		print("ONLINE_MAIN_GATE_RETURN_MAP15 role=actor cycle=",_transfer_cycle+1)
		# Leaving a map clears the old LocalMovement predictor. Verify a cached
		# return still owns intact Blocking.dat and can bind the new generation by
		# walking again and requiring the authoritative Server ACK.
		var return_target := Vector2(840,808)
		_app._begin_ground_navigation(return_target,Time.get_ticks_msec())
		if not _app.world_view.local_movement.active:
			var controller = _app.world_view.local_movement
			_online_gate_fail("return_map15_local_prediction_not_started ready_%s epoch_%d actor_%d pos_%s" % [
				str(controller.ready),controller.map_epoch,controller.actor_id,str(controller.position)
			])
			return
		_online_gate_phase = 8
	elif _online_gate_phase == 8:
		var return_target := Vector2(840,808)
		var at := Vector2(float(_app._last_snapshot.get("x",0.0)),float(_app._last_snapshot.get("y",0.0)))
		var movement_sequence := int(_app._last_snapshot.get("movement_sequence",0))
		if at.distance_to(return_target) <= 1.0 and _online_gate_ready(15) and movement_sequence > 0 \
				and int(_app._last_snapshot.get("last_receipt_sequence",0)) == movement_sequence \
				and int(_app._last_snapshot.get("last_receipt_status",-1)) == 0:
			_transfer_cycle += 1
			print("ONLINE_MAIN_GATE_RETURN_MOVE_ACK role=actor cycle=",_transfer_cycle)
			if _transfer_cycle >= TRANSFER_CYCLES:
				print("ONLINE_MAIN_GATE_TRANSFER_STRESS_PASS role=actor cycles=",_transfer_cycle)
				_online_gate_finish(true,"")
			else:
				_online_gate_not_before = Time.get_ticks_msec() + 350
				_online_gate_phase = 4


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
