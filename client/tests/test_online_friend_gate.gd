extends SceneTree
## Formal main-scene friend vertical gate: two real ClientSessions against the real AES Host.
## It drives original Classic friend controls and waits for authoritative snapshots/results.
const Main = preload("res://app/main.gd")
const MainScene = preload("res://app/main.tscn")
const UiSkinCatalog = preload("res://ui/ui_skin_catalog.gd")

var _actor_message := "好友A"
var _observer_reply := "好友B"

var _app: Main
var _role := ""
var _phase := -1
var _deadline := 0
var _peer_id := 0
var _character_proof_requested := false
var _character_proof_ready := false
var _relogin_started := false
var _relogin_world_seen := false
var _offline_seen := false
var _finish_code := -1
var _finish_reason := ""

func _initialize() -> void:
	_launch.call_deferred()

func _launch() -> void:
	root.size = Vector2i(1280, 720)
	var args := OS.get_cmdline_user_args()
	for i in range(args.size() - 1):
		if args[i] == "--friend-role":
			_role = args[i + 1]
			break
	_app = MainScene.instantiate() as Main
	root.add_child(_app)
	current_scene = _app
	process_frame.connect(_tick)
	if _role not in ["observer", "actor"]:
		_fail("invalid_role")
		return
	var token := OS.get_environment("SGTEST_FRIEND_E2E_TOKEN").strip_edges()
	if not token.is_empty():
		_actor_message += " " + token
		_observer_reply += " " + token
	_deadline = Time.get_ticks_msec() + 90000
	_phase = 0
	_begin_login()
	print("ONLINE_FRIEND_GATE_START role=", _role)

func _begin_login() -> void:
	var username := OS.get_environment("SGTEST_E2E_USERNAME")
	var password := OS.get_environment("SGTEST_E2E_PASSWORD")
	if username.is_empty() or password.is_empty():
		_fail("missing_credentials")
		return
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

func _first_friend_row_press() -> bool:
	for child in _app.classic_world._friend_rows.get_children():
		if child.has_signal("pressed"):
			child.emit_signal("pressed")
			return true
	return false

func _has_friend_message(text: String, direction: String) -> bool:
	for raw in _app._last_snapshot.get("friend_messages", []):
		if raw is Dictionary and str(raw.get("message", "")) == text and str(raw.get("direction", "")) == direction:
			return true
	return false

func _friend_online(peer: int) -> bool:
	var friends: Dictionary = _app._last_snapshot.get("friends", {})
	return friends.has(peer) and bool((friends[peer] as Dictionary).get("online", false))

func _friend_offline(peer: int) -> bool:
	var friends: Dictionary = _app._last_snapshot.get("friends", {})
	return friends.has(peer) and not bool((friends[peer] as Dictionary).get("online", false))

func _tick() -> void:
	if _phase < 0:
		return
	if _phase == 90:
		var stats := _app.world_view.store.statistics()
		if int(stats.scopes) == 0 and int(stats.assets) == 0 and int(stats.decode_jobs) == 0 and int(stats.texture_jobs) == 0:
			if _finish_code == 0:
				print("ONLINE_FRIEND_GATE_PASS role=", _role, " resources=0")
			else:
				push_error("ONLINE_FRIEND_GATE_FAIL role=%s reason=%s" % [_role, _finish_reason])
			quit(_finish_code)
		return
	if Time.get_ticks_msec() > _deadline:
		_fail("timeout_phase_%d_state_%s_status_%s" % [_phase, str(_app._last_snapshot.get("state", "none")), _app.session_panel.status.text])
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
		print("ONLINE_FRIEND_GATE_READY role=actor peer=", _peer_id)
		_send_invite_via_rightbox()
		_phase = 1
		return

	if _phase == 1:
		var notice: Dictionary = _app._last_snapshot.get("last_friend_invite_notice", {})
		if int(notice.get("state", -1)) != 2 or int(notice.get("actor_id", 0)) != _peer_id:
			return
		if not (_app._last_snapshot.get("friends", {}) as Dictionary).is_empty():
			_fail("friend_exists_after_reject")
			return
		print("ONLINE_FRIEND_GATE_REJECT_SEEN role=actor authority=PASS")
		_send_invite_via_rightbox()
		_phase = 2
		return

	if _phase == 2:
		var notice: Dictionary = _app._last_snapshot.get("last_friend_invite_notice", {})
		if int(notice.get("state", -1)) != 1 or int(notice.get("actor_id", 0)) != _peer_id:
			return
		if not _friend_online(_peer_id):
			return
		if not _app.classic_world.friend_panel.visible:
			_fail("friend_panel_hidden_after_accept")
			return
		if not _press(_app.classic_world.friend_panel, "FriendExpand"):
			_fail("friend_expand_missing")
			return
		if not _app.classic_world._friend_large:
			_fail("friend_expand_ui_failed")
			return
		print("ONLINE_FRIEND_GATE_ACCEPT role=actor authority=PASS online=PASS big_window=PASS")
		# Mail buttons belong to the source-exact 020 small panel. Prove 042 big,
		# then return to 020 before opening the source mail control.
		if not _press(_app.classic_world.friend_panel, "FriendExpand") or _app.classic_world._friend_large:
			_fail("friend_collapse_ui_failed")
			return
		if _app.classic_world._friend_mail_buttons.is_empty():
			_fail("friend_mail_button_missing")
			return
		_app.classic_world._friend_mail_buttons[0].emit_signal("pressed")
		if not _app.classic_world._friend_message_panel.visible:
			_fail("friend_message_panel_not_open")
			return
		_app.classic_world._friend_message_input.text = _actor_message
		_app.classic_world._friend_message_input.text_submitted.emit(_actor_message)
		if not _app.classic_world._friend_message_input.text.is_empty():
			var current := _app.session_panel.session.snapshot()
			_fail("friend_message_submit_rejected_actor_%d_pending_%d_ready_%s_ack_%d_epoch_%d" % [
				int(_app.classic_world._friend_message_actor),
				int(current.get("pending_whisper_sequence", 0)),
				str(current.get("world_ready", false)),
				int(current.get("last_world_ready_ack_epoch", 0)),
				int(current.get("map_epoch", 0))])
			return
		_phase = 3
		return

	if _phase == 3:
		if not _has_friend_message(_actor_message, "outgoing"):
			return
		if not _has_friend_message(_observer_reply, "incoming"):
			return
		if not _app.classic_world._friend_message_history.text.contains(_observer_reply):
			return
		print("ONLINE_FRIEND_GATE_MESSAGE role=actor send=PASS receive=PASS history=PASS")
		_app.session_panel.disconnect_session()
		_phase = 4
		return

	if _phase == 4:
		if _app.session_panel.active:
			return
		if _relogin_started:
			return
		_relogin_started = true
		_begin_login()
		_phase = 5
		return

	if _phase == 5:
		if not _world_ready():
			return
		if not _relogin_world_seen:
			_relogin_world_seen = true
			var friends: Dictionary = _app._last_snapshot.get("friends", {})
			if not friends.has(_peer_id):
				_fail("friendship_missing_after_relogin")
				return
			print("ONLINE_FRIEND_GATE_RELOGIN role=actor sqlite_friendship=PASS")
			return
		if not _friend_online(_peer_id):
			return
		if not _first_friend_row_press():
			_fail("friend_row_missing_after_relogin")
			return
		if not _press(_app.classic_world.friend_panel, "FriendDelete"):
			_fail("friend_delete_missing")
			return
		_phase = 6
		return

	if _phase == 6:
		var friends: Dictionary = _app._last_snapshot.get("friends", {})
		if not friends.is_empty() or int(_app._last_snapshot.get("pending_friend_sequence", 0)) != 0:
			return
		if int(_app._last_snapshot.get("last_friend_result_status", -1)) != 0:
			_fail("friend_delete_server_rejected")
			return
		print("ONLINE_FRIEND_GATE_DELETE role=actor authority=PASS")
		_finish(true, "")

func _send_invite_via_rightbox() -> void:
	_app.classic_world.open_player_request_menu(_peer_id, Vector2(500, 300))
	if not _app.classic_world._request_menu.visible:
		_fail("rightbox_not_visible")
		return
	if not _press(_app.classic_world._request_menu, "RequestFriend"):
		_fail("rightbox_friend_button_missing")
		return
	print("ONLINE_FRIEND_GATE_INVITE_UI role=actor rightbox=PASS")

func _tick_observer() -> void:
	if _phase == 0:
		if not _world_ready():
			return
		_peer_id = _find_peer()
		if _peer_id <= 0:
			print("ONLINE_FRIEND_GATE_READY role=observer")
			_phase = 1
			return
		print("ONLINE_FRIEND_GATE_READY role=observer peer=", _peer_id)
		_phase = 1
		return

	if _peer_id <= 0:
		_peer_id = _find_peer()

	if _phase == 1:
		if _peer_id <= 0 or not _app.classic_world._friend_question.visible:
			return
		if int(_app.classic_world._friend_question_actor) != _peer_id:
			return
		if not _press(_app.classic_world._friend_question, "QuestionNo"):
			_fail("friend_question_no_missing")
			return
		print("ONLINE_FRIEND_GATE_REJECT role=observer question044=PASS")
		_phase = 2
		return

	if _phase == 2:
		if not _app._last_snapshot.get("pending_friend_invite", {}).is_empty():
			return
		if _app.classic_world._friend_question.visible:
			return
		_phase = 3
		return

	if _phase == 3:
		if not _app.classic_world._friend_question.visible:
			return
		if int(_app.classic_world._friend_question_actor) != _peer_id:
			return
		if not _press(_app.classic_world._friend_question, "QuestionYes"):
			_fail("friend_question_yes_missing")
			return
		_phase = 4
		return

	if _phase == 4:
		if not _friend_online(_peer_id):
			return
		if not _first_friend_row_press():
			_fail("friend_row_missing_after_accept")
			return
		if not _press(_app.classic_world.friend_panel, "FriendIgnore"):
			_fail("friend_ignore_missing")
			return
		_phase = 5
		return

	if _phase == 5:
		var ignored: Dictionary = _app._last_snapshot.get("ignored_friends", {})
		if not ignored.has(_peer_id):
			return
		# Switching to the ignore tab sends an authoritative refresh. Do not
		# race Restore behind that pending FriendCommand; wait for its result.
		if not _app.classic_world._friend_show_ignored:
			if not _press(_app.classic_world.friend_panel, "IgnoreList"):
				_fail("ignore_list_button_missing")
			return
		if int(_app._last_snapshot.get("pending_friend_sequence", 0)) != 0:
			return
		if not _first_friend_row_press():
			_fail("ignored_row_missing")
			return
		if not _press(_app.classic_world.friend_panel, "FriendAdd"):
			_fail("friend_restore_button_missing")
			return
		_phase = 6
		return

	if _phase == 6:
		var ignored: Dictionary = _app._last_snapshot.get("ignored_friends", {})
		if ignored.has(_peer_id) or not _friend_online(_peer_id):
			return
		if not _press(_app.classic_world.friend_panel, "FriendList"):
			_fail("friend_list_button_missing")
			return
		print("ONLINE_FRIEND_GATE_IGNORE_RESTORE role=observer authority=PASS ui=PASS")
		if _app.classic_world._friend_mail_buttons.is_empty():
			_fail("observer_friend_mail_missing")
			return
		_app.classic_world._friend_mail_buttons[0].emit_signal("pressed")
		_phase = 7
		return

	if _phase == 7:
		if not _has_friend_message(_actor_message, "incoming"):
			return
		if not _app.classic_world._friend_message_panel.visible or not _app.classic_world._friend_message_history.text.contains(_actor_message):
			return
		_app.classic_world._friend_message_input.text = _observer_reply
		_app.classic_world._friend_message_input.text_submitted.emit(_observer_reply)
		_phase = 8
		return

	if _phase == 8:
		if not _has_friend_message(_observer_reply, "outgoing"):
			return
		if _friend_offline(_peer_id):
			_offline_seen = true
			print("ONLINE_FRIEND_GATE_PRESENCE role=observer offline=PASS")
			_phase = 9
		return

	if _phase == 9:
		if not _offline_seen or not _friend_online(_peer_id):
			return
		print("ONLINE_FRIEND_GATE_PRESENCE role=observer relog_online=PASS sqlite_friendship=PASS")
		_phase = 10
		return

	if _phase == 10:
		var friends: Dictionary = _app._last_snapshot.get("friends", {})
		if not friends.is_empty():
			return
		print("ONLINE_FRIEND_GATE_DELETE_SYNC role=observer bilateral=PASS")
		_finish(true, "")

func _finish(success: bool, reason: String) -> void:
	_finish_code = 0 if success else 1
	_finish_reason = reason
	_app.session_panel.disconnect_session()
	_app.world_view.clear_map()
	_phase = 90

func _fail(reason: String) -> void:
	if _phase == 90:
		return
	_finish(false, reason)
