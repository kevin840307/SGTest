extends SceneTree
## UI-020/UI-026: stable HK social lists across unrelated World/AOI snapshots.
const Shell = preload("res://ui/classic_world_shell.gd")
var failed := false

func _init() -> void:
	call_deferred("_run")

func _run() -> void:
	var shell := Shell.new()
	root.add_child(shell)
	shell.set_world_active(true)
	var snapshot := {
		"party_members": {10: {"name": "Leader"}, 11: {"name": "Member"}},
		"party_leader_actor_id": 10,
		"friends": {20: {"name": "Friend", "online": true}},
		"pending_friend_sequence": 0,
		"chat_messages": [{"channel": 0, "sender_name": "Leader", "message": "hello"}]
	}
	shell.apply_snapshot(snapshot)
	var party_row := shell._party_rows.get_child(0)
	var friend_row := shell._friend_rows.get_child(0)
	var chat_text: String = shell._chat_history.text
	var stable_party := true
	var stable_friends := true
	for i in 12:
		var unrelated := snapshot.duplicate(true)
		unrelated["player_current_hp"] = 50 + i
		shell.apply_snapshot(unrelated)
		stable_party = stable_party and shell._party_rows.get_child(0) == party_row
		stable_friends = stable_friends and shell._friend_rows.get_child(0) == friend_row
	_expect(stable_party, "unchanged party rows survive 12 World snapshots")
	_expect(stable_friends, "unchanged friend rows survive 12 World snapshots")
	_expect(shell._chat_history.text == chat_text, "chat text remains stable")
	var updated_party := snapshot.duplicate(true)
	updated_party["party_leader_actor_id"] = 11
	shell.apply_snapshot(updated_party)
	_expect(shell._party_rows.get_child(0) != party_row, "new party leader rebuilds roster")
	_expect((shell._party_rows.get_child(1) as Button).text.begins_with("★ "),
		"actual new leader is reflected")
	var updated_social := updated_party.duplicate(true)
	updated_social["friends"][20]["online"] = false
	updated_social["chat_messages"].append({"channel": 3, "sender_name": "Member", "message": "party"})
	shell.apply_snapshot(updated_social)
	_expect(shell._friend_rows.get_child(0) != friend_row, "friend presence rebuilds roster")
	_expect(shell._chat_history.text.contains("party"), "new server chat is rendered")
	var last_friend := shell._friend_rows.get_child(0)
	shell.set_world_active(false)
	shell.set_world_active(true)
	shell.apply_snapshot(updated_social)
	_expect(shell._friend_rows.get_child(0) != last_friend, "world re-entry resets social cache")
	shell.free()
	if not failed:
		print("HK_SOCIAL_LIST_SNAPSHOT_STABILITY_PASS unchanged=12 leader=true online=true chat=true reset=true")
	quit(1 if failed else 0)

func _expect(ok: bool, reason: String) -> void:
	if not ok:
		failed = true
		push_error("HK_SOCIAL_LIST_SNAPSHOT_STABILITY_FAIL " + reason)
