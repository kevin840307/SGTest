extends SceneTree

const Shell = preload("res://ui/classic_world_shell.gd")
var failed := false

class PanelStub extends RefCounted:
	var skill_id_input := SpinBox.new()
	var skill_target_mode := CheckButton.new()

	func _init() -> void:
		skill_id_input.min_value = 1
		skill_id_input.max_value = 100000000


func _init() -> void:
	var shell := Shell.new()
	root.add_child(shell)
	var panel := PanelStub.new()
	shell._panel = panel
	shell._stats = {
		"skill_book_ready": true,
		"skill_book": {28101: 3, 7201: 1}
	}
	shell._refresh_skill()

	_expect(shell.skill_window.size == Vector2(223, 288), "shell uses HK 223x288 skill window")
	_expect(shell.skill_window.position == Vector2(542, 63), "shell skill window anchor")
	_expect(shell.skill_window.get_node("SkillName0").text == "技能 7201", "shell reads server skill_book")
	_expect(shell.skill_window.get_node("SkillLevel1").text == "3", "shell preserves server skill level")

	shell.skill_window.get_node("SkillRow0").pressed.emit()
	_expect(int(panel.skill_id_input.value) == 7201, "row selects skill id")
	_expect(panel.skill_target_mode.button_pressed, "row enters existing target-skill mode")

	shell._stats = {"skill_book_ready": false, "skill_book": {99001: 3}}
	shell._refresh_skill()
	_expect(shell.skill_window.get_node("SkillName0").text == "", "not-ready skill book is fail-closed")

	shell.free()
	panel.skill_id_input.free()
	panel.skill_target_mode.free()
	if not failed:
		print("CLASSIC_SKILL_SHELL_INTEGRATION_PASS skill_book_wire=true target_mode=true fail_closed=true")
	quit(1 if failed else 0)


func _expect(condition: bool, label: String) -> void:
	if condition:
		return
	failed = true
	push_error("CLASSIC_SKILL_SHELL_INTEGRATION_FAIL " + label)
