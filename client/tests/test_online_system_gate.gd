extends SceneTree
## Formal System-menu vertical gate against a real Host and main scene.
## The settings role changes source 029_SYSTEM controls then confirms return to
## login. A fresh exit-role process reuses the same user:// profile, proving
## persisted settings, then confirms Exit. The observer proves both disconnects
## remove the actor from authoritative AOI.

const Main = preload("res://app/main.gd")
const MainScene = preload("res://app/main.tscn")
const UiSkinCatalog = preload("res://ui/ui_skin_catalog.gd")

var _app: Main
var _role := ""
var _phase := -1
var _deadline := 0
var _character_proof_requested := false
var _character_proof_ready := false
var _finish_code := -1
var _finish_reason := ""
var _peer_id := 0
var _retry_after := 0

func _initialize() -> void:
	_launch.call_deferred()

func _launch() -> void:
	root.size = Vector2i(1280,720)
	var args := OS.get_cmdline_user_args()
	for i in range(args.size()-1):
		if args[i] == "--system-role":
			_role = args[i+1]
			break
	if _role not in ["observer","settings","exit"]:
		push_error("ONLINE_SYSTEM_GATE_FAIL invalid_role="+_role)
		quit(1)
		return
	_app = MainScene.instantiate() as Main
	root.add_child(_app)
	current_scene = _app
	process_frame.connect(_tick)
	var timeout_ms := 90000
	var configured := OS.get_environment("SGTEST_SYSTEM_GATE_TIMEOUT_MS")
	if configured.is_valid_int():
		timeout_ms = maxi(45000,int(configured))
	_deadline = Time.get_ticks_msec()+timeout_ms
	_phase = 0
	_begin_login()
	print("ONLINE_SYSTEM_GATE_START role=",_role)

func _begin_login() -> void:
	var username := OS.get_environment("SGTEST_E2E_USERNAME")
	var password := OS.get_environment("SGTEST_E2E_PASSWORD")
	if username.is_empty() or password.is_empty():
		_fail("missing_credentials")
		return
	_character_proof_requested=false
	_character_proof_ready=false
	_app.session_panel.username_input.text=username
	_app.session_panel.password_input.text=password
	_app.session_panel.login()

func _drive_character_choose() -> void:
	if not _app.session_panel.characters.visible or _app.session_panel.choose_button.disabled or _app.session_panel.characters.item_count<=0:
		return
	_app.session_panel.characters.select(0)
	if not _character_proof_ready:
		if not _character_proof_requested:
			var listed:Array=_app.session_panel._latest.get("characters",[])
			if listed.is_empty():
				return
			if not _app.session_panel.change_character_password(int(listed[0].character_id),"","2468"):
				_fail("character_proof_migration_rejected")
				return
			_character_proof_requested=true
			return
		var revision:=int(_app.session_panel._latest.get("character_manage_result_revision",0))
		if revision<=0:
			return
		var status:=int(_app.session_panel._latest.get("character_manage_status",-1))
		if status not in [0,2]:
			_fail("character_proof_migration_status_%d"%status)
			return
		_character_proof_ready=true
	_app.session_panel.character_choose_password_input.text="2468"
	_app.session_panel.choose()

func _world_ready() -> bool:
	return (
		not _app._last_snapshot.is_empty() and
		bool(_app._last_snapshot.get("world_ready",false)) and
		_app.world_view.visible and _app.world_view.phase=="ready" and
		UiSkinCatalog.valid(UiSkinCatalog.DEFAULT_SKIN) and
		_app.classic_world.visible
	)

func _find_peer() -> int:
	var own:=int(_app._last_snapshot.get("actor_id",0))
	var entities:Dictionary=_app._last_snapshot.get("entities",{})
	for raw in entities.keys():
		var id:=int(raw)
		if id>0 and id!=own:
			return id
	return 0

func _press(parent:Node,name:String)->bool:
	var button:=parent.get_node_or_null(name)
	if button==null or not button.has_signal("pressed"):
		return false
	button.emit_signal("pressed")
	return true

func _audio_bus_applied(name:String, expected_level:int)->bool:
	var index:=AudioServer.get_bus_index(name)
	if index<0:
		return false
	if expected_level<=0:
		return AudioServer.is_bus_mute(index)
	if AudioServer.is_bus_mute(index):
		return false
	var expected_db:=linear_to_db(float(expected_level)/10.0)
	return absf(AudioServer.get_bus_volume_db(index)-expected_db)<0.05

func _set_level(down_name:String,up_name:String,target:int)->bool:
	for i in range(12):
		if not _press(_app.classic_world.settings_window,down_name):
			return false
	for i in range(target):
		if not _press(_app.classic_world.settings_window,up_name):
			return false
	return true

func _tick() -> void:
	if _phase<0:
		return
	if _phase==90:
		var stats:=_app.world_view.store.statistics()
		if int(stats.scopes)==0 and int(stats.assets)==0 and int(stats.decode_jobs)==0 and int(stats.texture_jobs)==0:
			if _finish_code==0:
				print("ONLINE_SYSTEM_GATE_PASS role=",_role," resources=0")
			else:
				push_error("ONLINE_SYSTEM_GATE_FAIL role=%s reason=%s"%[_role,_finish_reason])
			quit(_finish_code)
		return
	if Time.get_ticks_msec()>_deadline:
		_fail("timeout_phase_%d_state_%s_status_%s"%[
			_phase,str(_app._last_snapshot.get("state","none")),_app.session_panel.status.text])
		return
	_drive_character_choose()
	match _role:
		"observer": _tick_observer()
		"settings": _tick_settings()
		"exit": _tick_exit()

func _tick_settings() -> void:
	if _phase==0:
		if not _world_ready():
			return
		if not _press(_app.classic_world.secondary_menu,"SystemCommand"):
			_fail("system_command_missing")
			return
		if not _app.classic_world.settings_window.visible:
			_fail("029_system_not_visible")
			return
		if not _set_level("BgmDown","BgmUp",4):
			_fail("bgm_controls_missing")
			return
		if not _audio_bus_applied("BGM",4):
			_fail("bgm_engine_not_applied")
			return
		if not _set_level("SfxDown","SfxUp",6):
			_fail("sfx_controls_missing")
			return
		if not _audio_bus_applied("SFX",6):
			_fail("sfx_engine_not_applied")
			return
		if not _press(_app.classic_world.settings_window,"QualityDown") or Engine.max_fps!=25:
			_fail("25fps_not_applied")
			return
		for i in range(3):
			if _app.classic_world.system_battle_mode()==2:
				break
			if not _press(_app.classic_world.settings_window,"BattleModeRight"):
				_fail("battle_mode_right_missing")
				return
		if (
			_app.classic_world.system_battle_mode()!=2 or
			_app.classic_world._settings_battle_value.text!="任意戰鬥模式" or
			_app.classic_world._settings_bgm_value.text!="4" or
			_app.classic_world._settings_sfx_value.text!="6"
		):
			_fail("settings_values_not_applied")
			return
		if not _press(_app.classic_world.settings_window,"CloseWindow"):
			_fail("system_close_missing")
			return
		if _app.classic_world.settings_window.visible:
			_fail("system_close_failed")
			return
		if not _press(_app.classic_world.secondary_menu,"SystemCommand") or not _app.classic_world.settings_window.visible:
			_fail("system_reopen_failed")
			return
		print("ONLINE_SYSTEM_GATE_SETTINGS role=settings source029=PASS bgm=4 sfx=6 fps=25 battle=2 reopen=PASS")
		if not _press(_app.classic_world.settings_window,"BackToServer"):
			_fail("back_to_server_missing")
			return
		if (
			not _app.classic_world._system_action_blocker.visible or
			_app.classic_world._system_action_question_text.text!="你要進入其他伺服器？"
		):
			_fail("back_to_server_question_wrong")
			return
		if not _press(_app.classic_world._system_action_question,"SystemActionCancel"):
			_fail("back_to_server_cancel_missing")
			return
		if _app.classic_world._system_action_blocker.visible or not _app.session_panel.active or not _world_ready():
			_fail("back_to_server_cancel_changed_session")
			return
		print("ONLINE_SYSTEM_GATE_CANCEL role=settings back_to_server=PASS session_retained=PASS")
		if not _press(_app.classic_world.settings_window,"BackToServer"):
			_fail("back_to_server_reopen_missing")
			return
		if not _press(_app.classic_world._system_action_question,"SystemActionConfirm"):
			_fail("back_to_server_confirm_missing")
			return
		_phase=1
		return
	if _phase==1:
		if _app.session_panel.active:
			return
		if not _app._last_snapshot.is_empty() or _app.world_view.phase=="ready":
			return
		print("ONLINE_SYSTEM_GATE_LOGOUT role=settings confirm=PASS session_stop=PASS world_clear=PASS")
		_finish(true,"")

func _tick_exit() -> void:
	if _phase==0:
		if not _world_ready():
			return
		_peer_id=_find_peer()
		if _peer_id<=0:
			return
		if (
			_app.classic_world._settings_bgm_level!=4 or
			_app.classic_world._settings_sfx_level!=6 or
			_app.classic_world.system_battle_mode()!=2 or
			Engine.max_fps!=25 or
			not _audio_bus_applied("BGM",4) or
			not _audio_bus_applied("SFX",6)
		):
			_fail("fresh_process_settings_not_restored")
			return
		print("ONLINE_SYSTEM_GATE_RESTORE role=exit fresh_process=PASS bgm=4 sfx=6 fps=25 battle=2")
		_retry_after=Time.get_ticks_msec()+700
		_phase=1
		return
	if _phase==1:
		if Time.get_ticks_msec()<_retry_after:
			return
		if not _press(_app.classic_world.secondary_menu,"SystemCommand") or not _app.classic_world.settings_window.visible:
			_fail("exit_system_open_failed")
			return
		if not _press(_app.classic_world.settings_window,"ExitGame"):
			_fail("exit_button_missing")
			return
		if (
			not _app.classic_world._system_action_blocker.visible or
			_app.classic_world._system_action_question_text.text!="你決定離開系統？"
		):
			_fail("exit_question_wrong")
			return
		if not _press(_app.classic_world._system_action_question,"SystemActionCancel"):
			_fail("exit_cancel_missing")
			return
		if _app.classic_world._system_action_blocker.visible or not _app.session_panel.active or not _world_ready():
			_fail("exit_cancel_changed_session")
			return
		print("ONLINE_SYSTEM_GATE_CANCEL role=exit exit_game=PASS session_retained=PASS")
		if not _press(_app.classic_world.settings_window,"ExitGame"):
			_fail("exit_reopen_missing")
			return
		print("ONLINE_SYSTEM_GATE_EXIT_REQUESTED role=exit prompt=PASS")
		if not _press(_app.classic_world._system_action_question,"SystemActionConfirm"):
			_fail("exit_confirm_missing")
			return
		if _app.session_panel.active:
			_fail("exit_confirm_did_not_stop_session")
			return
		print("ONLINE_SYSTEM_GATE_EXIT_CONFIRMED role=exit session_stop=PASS")
		_phase=91

func _tick_observer() -> void:
	if _phase==0:
		if not _world_ready():
			return
		print("ONLINE_SYSTEM_GATE_READY role=observer")
		_phase=1
		return
	if _phase==1:
		_peer_id=_find_peer()
		if _peer_id<=0:
			return
		print("ONLINE_SYSTEM_GATE_PRESENT role=observer peer=",_peer_id)
		_phase=2
		return
	if _phase==2:
		if _find_peer()!=0:
			return
		print("ONLINE_SYSTEM_GATE_LOGOUT_DESPAWN role=observer authority_aoi=PASS")
		_phase=3
		return
	if _phase==3:
		_peer_id=_find_peer()
		if _peer_id<=0:
			return
		print("ONLINE_SYSTEM_GATE_RELOGIN_PRESENT role=observer authority_aoi=PASS peer=",_peer_id)
		_phase=4
		return
	if _phase==4:
		if _find_peer()!=0:
			return
		print("ONLINE_SYSTEM_GATE_EXIT_DESPAWN role=observer authority_aoi=PASS")
		_finish(true,"")

func _finish(success:bool,reason:String)->void:
	if _phase==90:
		return
	_finish_code=0 if success else 1
	_finish_reason=reason
	_app.session_panel.disconnect_session()
	_app.classic_world.set_world_active(false)
	_app.world_view.clear_map()
	_phase=90

func _fail(reason:String)->void:
	if _phase<0 or _phase in [90,91]:
		return
	print("ONLINE_SYSTEM_GATE_IMMEDIATE_FAIL role=",_role," phase=",_phase," reason=",reason)
	_finish(false,reason)
