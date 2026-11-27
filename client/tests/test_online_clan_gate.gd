extends SceneTree
## Formal two-main-scene clan vertical gate.
## Exercises the source-backed right-click invite, 061 create, 057 menu,
## 060 member list, 070 announcement editor and durable Server authority.

const Main = preload("res://app/main.gd")
const MainScene = preload("res://app/main.tscn")
const UiSkinCatalog = preload("res://ui/ui_skin_catalog.gd")

const CLAN_NAME := "RoyalGuild"
const ANNOUNCEMENT := "今晚九點集合"

var _app: Main
var _role := ""
var _phase := -1
var _deadline := 0
var _peer_id := 0
var _clan_id := 0
var _retry_after := 0
var _invite_result_after := 0
var _character_proof_requested := false
var _character_proof_ready := false
var _relogin_started := false
var _finish_code := -1
var _finish_reason := ""

func _initialize() -> void:
	_launch.call_deferred()

func _launch() -> void:
	root.size = Vector2i(1280, 720)
	var args := OS.get_cmdline_user_args()
	for i in range(args.size() - 1):
		if args[i] == "--clan-role":
			_role = args[i + 1]
			break
	_app = MainScene.instantiate() as Main
	root.add_child(_app)
	current_scene = _app
	process_frame.connect(_tick)
	if _role not in ["observer", "actor"]:
		_fail("invalid_role")
		return
	_deadline = Time.get_ticks_msec() + 115000
	_phase = 0
	_begin_login()
	print("ONLINE_CLAN_GATE_START role=", _role)

func _begin_login() -> void:
	var username := OS.get_environment("SGTEST_E2E_USERNAME")
	var password := OS.get_environment("SGTEST_E2E_PASSWORD")
	if username.is_empty() or password.is_empty():
		_fail("missing_credentials")
		return
	_character_proof_requested = false
	_character_proof_ready = false
	_app.session_panel.username_input.text = username
	_app.session_panel.password_input.text = password
	_app.session_panel.login()

func _drive_character_choose() -> void:
	if not _app.session_panel.characters.visible or _app.session_panel.choose_button.disabled or _app.session_panel.characters.item_count <= 0:
		return
	_app.session_panel.characters.select(0)
	if not _character_proof_ready:
		if not _character_proof_requested:
			var listed: Array = _app.session_panel._latest.get("characters", [])
			if listed.is_empty():
				return
			if not _app.session_panel.change_character_password(int(listed[0].character_id), "", "2468"):
				_fail("character_proof_migration_rejected")
				return
			_character_proof_requested = true
			return
		var revision := int(_app.session_panel._latest.get("character_manage_result_revision", 0))
		if revision <= 0:
			return
		var status := int(_app.session_panel._latest.get("character_manage_status", -1))
		if status not in [0, 2]:
			_fail("character_proof_migration_status_%d" % status)
			return
		_character_proof_ready = true
	_app.session_panel.character_choose_password_input.text = "2468"
	_app.session_panel.choose()

func _world_ready() -> bool:
	return (
		not _app._last_snapshot.is_empty() and
		bool(_app._last_snapshot.get("world_ready", false)) and
		_app.world_view.visible and _app.world_view.phase == "ready" and
		UiSkinCatalog.valid(UiSkinCatalog.DEFAULT_SKIN) and
		_app.classic_world.visible
	)

func _find_peer() -> int:
	var own := int(_app._last_snapshot.get("actor_id", 0))
	var entities: Dictionary = _app._last_snapshot.get("entities", {})
	for raw in entities.keys():
		var id := int(raw)
		if id > 0 and id != own:
			return id
	return 0

func _press(parent: Node, name: String) -> bool:
	var button := parent.get_node_or_null(name)
	if button == null or not button.has_signal("pressed"):
		return false
	button.emit_signal("pressed")
	return true

func _member(actor_id:int) -> Dictionary:
	var members:Dictionary = _app._last_snapshot.get("clan_members", {})
	return members.get(actor_id, {})

func _member_online(actor_id:int) -> bool:
	var entry:=_member(actor_id)
	return not entry.is_empty() and bool(entry.get("online", false))

func _select_member_in_source_list(actor_id:int) -> bool:
	_app.classic_world._open_clan_member_window()
	for i in range(_app.classic_world._clan_member_actor_ids.size()):
		if int(_app.classic_world._clan_member_actor_ids[i]) == actor_id:
			var hit := _app.classic_world._clan_member_window.get_node_or_null("MemberHit%d" % i)
			if hit == null:
				return false
			hit.emit_signal("pressed")
			return int(_app.classic_world._clan_selected_actor) == actor_id
	return false

func _send_clan_invite_via_rightbox() -> bool:
	_app.classic_world.open_player_request_menu(_peer_id, Vector2(500, 300))
	if not _app.classic_world._request_menu.visible:
		_fail("rightbox_not_visible")
		return false
	if not _press(_app.classic_world._request_menu, "RequestClan"):
		_fail("rightbox_clan_button_missing")
		return false
	_invite_result_after = int(_app._last_snapshot.get("last_clan_result_sequence", 0))
	return true

func _tick() -> void:
	if _phase < 0:
		return
	if _phase == 90:
		var stats := _app.world_view.store.statistics()
		if int(stats.scopes) == 0 and int(stats.assets) == 0 and int(stats.decode_jobs) == 0 and int(stats.texture_jobs) == 0:
			if _finish_code == 0:
				print("ONLINE_CLAN_GATE_PASS role=", _role, " resources=0")
			else:
				push_error("ONLINE_CLAN_GATE_FAIL role=%s reason=%s" % [_role, _finish_reason])
			quit(_finish_code)
		return
	if Time.get_ticks_msec() > _deadline:
		_fail("timeout_phase_%d_state_%s_status_%s" % [
			_phase, str(_app._last_snapshot.get("state", "none")),
			_app.session_panel.status.text])
		return
	_drive_character_choose()
	if _role == "actor":
		_tick_actor()
	else:
		_tick_observer()

func _tick_actor() -> void:
	if _phase == 0:
		if not _world_ready():
			return
		_peer_id = _find_peer()
		if _peer_id <= 0:
			return
		print("ONLINE_CLAN_GATE_READY role=actor peer=", _peer_id)
		if int(_app._last_snapshot.get("clan_id", 0)) != 0:
			_fail("actor_not_clean_clan_baseline")
			return
		if not _press(_app.classic_world.secondary_menu, "GuildCommand"):
			_fail("guild_command_missing")
			return
		if not _app.classic_world._clan_name_dialog.visible:
			_fail("061_create_dialog_not_visible")
			return
		_app.classic_world._clan_name_input.text = CLAN_NAME
		_app.classic_world._update_clan_name_action()
		if _app.classic_world._clan_name_confirm.disabled:
			_fail("061_create_confirm_disabled")
			return
		_app.classic_world._clan_name_confirm.emit_signal("pressed")
		_phase = 1
		return

	if _phase == 1:
		if int(_app._last_snapshot.get("pending_clan_sequence", 0)) != 0:
			return
		_clan_id = int(_app._last_snapshot.get("clan_id", 0))
		if _clan_id <= 0 or str(_app._last_snapshot.get("clan_name", "")) != CLAN_NAME:
			return
		if not _app.classic_world.clan_window.visible:
			_fail("057_not_visible_after_create")
			return
		print("ONLINE_CLAN_GATE_CREATE role=actor ui061=PASS ui057=PASS sqlite=PASS clan=", _clan_id)
		if not _send_clan_invite_via_rightbox():
			return
		_phase = 2
		return

	if _phase == 2:
		var result_seq := int(_app._last_snapshot.get("last_clan_result_sequence", 0))
		if result_seq <= _invite_result_after or int(_app._last_snapshot.get("pending_clan_sequence", 0)) != 0:
			return
		var result:Dictionary = _app._last_snapshot.get("last_clan_result", {})
		if int(result.get("action", 0)) != 8 or int(result.get("status", -1)) != 0:
			_fail("first_invite_server_rejected_"+str(result))
			return
		print("ONLINE_CLAN_GATE_INVITE role=actor rightbox=PASS server=PASS")
		_retry_after = Time.get_ticks_msec() + 900
		_phase = 3
		return

	if _phase == 3:
		if Time.get_ticks_msec() < _retry_after or int(_app._last_snapshot.get("pending_clan_sequence", 0)) != 0:
			return
		if not _send_clan_invite_via_rightbox():
			return
		_phase = 4
		return

	if _phase == 4:
		var result_seq := int(_app._last_snapshot.get("last_clan_result_sequence", 0))
		if result_seq <= _invite_result_after or int(_app._last_snapshot.get("pending_clan_sequence", 0)) != 0:
			return
		var result:Dictionary = _app._last_snapshot.get("last_clan_result", {})
		if int(result.get("action", 0)) != 8:
			_fail("retry_invite_wrong_action_"+str(result))
			return
		var status:=int(result.get("status", -1))
		if status == 15:
			_retry_after = Time.get_ticks_msec() + 300
			_phase = 3
			return
		if status != 0:
			_fail("retry_invite_status_%d"%status)
			return
		print("ONLINE_CLAN_GATE_REINVITE role=actor reject_cleared_pending=PASS")
		_phase = 5
		return

	if _phase == 5:
		var members:Dictionary = _app._last_snapshot.get("clan_members", {})
		if int(_app._last_snapshot.get("clan_id", 0)) != _clan_id or not members.has(_peer_id) or members.size() != 2:
			return
		if not _member_online(_peer_id):
			return
		_app.classic_world._open_clan_member_window()
		if not _app.classic_world._clan_member_window.visible:
			_fail("060_member_window_not_visible")
			return
		print("ONLINE_CLAN_GATE_JOIN_SYNC role=actor members=2 online=PASS ui060=PASS")
		_app.session_panel.disconnect_session()
		_retry_after = Time.get_ticks_msec() + 1600
		_phase = 6
		return

	if _phase == 6:
		if _app.session_panel.active or Time.get_ticks_msec() < _retry_after:
			return
		if not _relogin_started:
			_relogin_started = true
			_begin_login()
			_phase = 7
		return

	if _phase == 7:
		if not _world_ready():
			return
		var members:Dictionary = _app._last_snapshot.get("clan_members", {})
		if int(_app._last_snapshot.get("clan_id", 0)) != _clan_id or not members.has(_peer_id) or members.size()!=2:
			return
		# Native rendering can apply Main._last_snapshot one process frame before
		# ClassicWorldShell receives the same snapshot. Drive source controls only
		# after the player-visible shell has caught up to Server authority.
		if (
			int(_app.classic_world._stats.get("clan_id",0))!=_clan_id or
			int(_app.classic_world._stats.get("clan_leader_actor_id",0))!=int(_app._last_snapshot.get("actor_id",0)) or
			int(_app.classic_world._stats.get("pending_clan_sequence",0))!=0
		):
			return
		print("ONLINE_CLAN_GATE_RELOGIN role=actor sqlite_membership=PASS online_return=PASS")
		_app.classic_world._open_clan_ui()
		if not _press(_app.classic_world.clan_window, "ClanNews"):
			_fail("057_news_button_missing")
			return
		if not _app.classic_world._clan_announcement_window.visible or not _app.classic_world._clan_announcement_input.editable:
			_fail("070_leader_editor_not_open")
			return
		_app.classic_world._clan_announcement_input.text = ANNOUNCEMENT
		_app.classic_world._clan_announcement_changed()
		if _app.classic_world._clan_announcement_confirm.disabled:
			_fail("070_confirm_disabled")
			return
		_app.classic_world._clan_announcement_confirm.emit_signal("pressed")
		_phase = 8
		return

	if _phase == 8:
		if int(_app._last_snapshot.get("pending_clan_sequence", 0)) != 0 or str(_app._last_snapshot.get("clan_announcement", "")) != ANNOUNCEMENT:
			return
		print("ONLINE_CLAN_GATE_ANNOUNCEMENT role=actor ui070=PASS server=PASS sync=PASS")
		if not _select_member_in_source_list(_peer_id):
			_fail("060_select_peer_failed")
			return
		_app.classic_world._open_clan_ui()
		if not _press(_app.classic_world.clan_window, "ClanTransfer"):
			_fail("057_transfer_button_missing")
			return
		_phase = 9
		return

	if _phase == 9:
		if int(_app._last_snapshot.get("pending_clan_sequence", 0)) != 0:
			return
		if int(_app._last_snapshot.get("clan_leader_actor_id", 0)) != _peer_id:
			return
		print("ONLINE_CLAN_GATE_TRANSFER role=actor authority=PASS sync=PASS")
		_phase = 10
		return

	if _phase == 10:
		if int(_app._last_snapshot.get("clan_id", 0)) != 0:
			return
		print("ONLINE_CLAN_GATE_KICKED role=actor clear_snapshot=PASS")
		_finish(true, "")

func _tick_observer() -> void:
	if _phase == 0:
		if not _world_ready():
			return
		_peer_id = _find_peer()
		print("ONLINE_CLAN_GATE_READY role=observer peer=", _peer_id)
		_phase = 1
		return

	if _peer_id <= 0:
		_peer_id = _find_peer()

	if _phase == 1:
		if _peer_id<=0 or not _app.classic_world._clan_question.visible:
			return
		var invite:Dictionary=_app._last_snapshot.get("pending_clan_invite", {})
		if int(invite.get("inviter_actor_id",0)) != _peer_id or str(invite.get("clan_name","")) != CLAN_NAME:
			return
		if not _press(_app.classic_world._clan_question, "QuestionNo"):
			_fail("clan_question_no_missing")
			return
		_phase = 2
		return

	if _phase == 2:
		if not (_app._last_snapshot.get("pending_clan_invite", {}) as Dictionary).is_empty() or int(_app._last_snapshot.get("pending_clan_sequence", 0)) != 0:
			return
		print("ONLINE_CLAN_GATE_REJECT role=observer question=PASS server=PASS")
		_phase = 3
		return

	if _phase == 3:
		if not _app.classic_world._clan_question.visible:
			return
		var invite:Dictionary=_app._last_snapshot.get("pending_clan_invite", {})
		if invite.is_empty() or int(invite.get("inviter_actor_id",0)) != _peer_id:
			return
		_clan_id=int(invite.get("clan_id",0))
		if not _press(_app.classic_world._clan_question, "QuestionYes"):
			_fail("clan_question_yes_missing")
			return
		_phase = 4
		return

	if _phase == 4:
		var members:Dictionary=_app._last_snapshot.get("clan_members", {})
		if int(_app._last_snapshot.get("clan_id",0))!=_clan_id or members.size()!=2 or not members.has(_peer_id):
			return
		if not _member_online(_peer_id):
			return
		print("ONLINE_CLAN_GATE_ACCEPT role=observer invite=PASS authority_join=PASS members=2")
		_phase = 5
		return

	if _phase == 5:
		var peer_member:=_member(_peer_id)
		if _peer_id<=0 or peer_member.is_empty() or _member_online(_peer_id):
			return
		print("ONLINE_CLAN_GATE_PRESENCE role=observer offline=PASS")
		_phase = 6
		return

	if _phase == 6:
		if not _member_online(_peer_id):
			return
		print("ONLINE_CLAN_GATE_PRESENCE role=observer relog_online=PASS")
		_phase = 7
		return

	if _phase == 7:
		if str(_app._last_snapshot.get("clan_announcement","")) != ANNOUNCEMENT:
			return
		_app.classic_world._open_clan_ui()
		if not _press(_app.classic_world.clan_window,"ClanNews"):
			_fail("observer_news_button_missing")
			return
		if (
			not _app.classic_world._clan_announcement_window.visible or
			_app.classic_world._clan_announcement_input.editable or
			_app.classic_world._clan_current_announcement.text != ANNOUNCEMENT
		):
			_fail("070_member_read_only_failed")
			return
		print("ONLINE_CLAN_GATE_ANNOUNCEMENT role=observer read_only=PASS synced=PASS")
		_app.classic_world._cancel_clan_announcement()
		_phase = 8
		return

	if _phase == 8:
		var self_id:=int(_app._last_snapshot.get("actor_id",0))
		if int(_app._last_snapshot.get("clan_leader_actor_id",0)) != self_id:
			return
		print("ONLINE_CLAN_GATE_TRANSFER role=observer became_leader=PASS")
		if not _select_member_in_source_list(_peer_id):
			_fail("observer_060_select_actor_failed")
			return
		_app.classic_world._open_clan_ui()
		if not _press(_app.classic_world.clan_window,"ClanKick"):
			_fail("057_kick_button_missing")
			return
		_phase = 9
		return

	if _phase == 9:
		var members:Dictionary=_app._last_snapshot.get("clan_members", {})
		if int(_app._last_snapshot.get("pending_clan_sequence",0))!=0 or members.has(_peer_id) or members.size()!=1:
			return
		print("ONLINE_CLAN_GATE_KICK role=observer authority=PASS bilateral_sync=PASS")
		_app.session_panel.disconnect_session()
		_phase = 10
		return

	if _phase == 10:
		if _app.session_panel.active:
			return
		if _relogin_started:
			return
		_relogin_started=true
		_begin_login()
		_phase=11
		return

	if _phase == 11:
		if not _world_ready():
			return
		var self_id:=int(_app._last_snapshot.get("actor_id",0))
		var members:Dictionary=_app._last_snapshot.get("clan_members", {})
		if (
			int(_app._last_snapshot.get("clan_id",0))!=_clan_id or
			int(_app._last_snapshot.get("clan_leader_actor_id",0))!=self_id or
			members.size()!=1 or not members.has(self_id)
		):
			return
		print("ONLINE_CLAN_GATE_RELOGIN role=observer sqlite_transfer_kick=PASS")
		_app.classic_world._open_clan_ui()
		if not _press(_app.classic_world.clan_window,"ClanLeave"):
			_fail("057_leave_button_missing")
			return
		_phase=12
		return

	if _phase == 12:
		if int(_app._last_snapshot.get("pending_clan_sequence",0))!=0 or int(_app._last_snapshot.get("clan_id",0))!=0:
			return
		print("ONLINE_CLAN_GATE_LEAVE role=observer authority=PASS clear_snapshot=PASS")
		_finish(true, "")

func _finish(success:bool, reason:String)->void:
	if _phase==90:
		return
	_finish_code=0 if success else 1
	_finish_reason=reason
	_app.session_panel.disconnect_session()
	_app.classic_world.set_world_active(false)
	_app.world_view.clear_map()
	_phase=90

func _fail(reason:String)->void:
	if _phase<0 or _phase==90:
		return
	print("ONLINE_CLAN_GATE_IMMEDIATE_FAIL role=",_role," phase=",_phase," reason=",reason)
	_finish(false,reason)
