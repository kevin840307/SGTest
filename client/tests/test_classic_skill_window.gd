extends SceneTree

const SkillWindow = preload("res://ui/classic_skill_window.gd")
var failed := false
var selected := 0


func _init() -> void:
	var window := SkillWindow.new()
	root.add_child(window)

	_expect(window.size == Vector2(223, 288), "window size")
	_expect_rect(window.get_node("SkillNameHeader"), Rect2(74, 39, 48, 12), "name header")
	_expect_rect(window.get_node("SkillLevelHeader"), Rect2(170, 39, 24, 12), "level header")
	_expect_rect(window.get_node("CloseWindow"), Rect2(204, 1, 16, 16), "close")
	_expect_rect(window.get_node("OnlineHelp"), Rect2(187, 1, 16, 16), "help")
	_expect_rect(window.get_node("ScrollUp"), Rect2(202, 33, 17, 36), "scroll up")
	_expect_rect(window.get_node("ScrollDown"), Rect2(202, 244, 17, 36), "scroll down")

	for row in 8:
		var y := 54.0 + 27.0 * row
		_expect_rect(window.get_node("SkillRow%d" % row), Rect2(5, y, 195, 27), "row %d" % row)
		_expect_rect(window.get_node("SkillIcon%d" % row), Rect2(7, y, 24, 24), "icon %d" % row)
		_expect_rect(window.get_node("SkillProgress%d" % row), Rect2(72, y + 1, 89, 4), "progress %d" % row)
		_expect_rect(window.get_node("SkillName%d" % row), Rect2(35, y + 12, 120, 12), "name %d" % row)
		_expect_rect(window.get_node("SkillLevel%d" % row), Rect2(169, y + 12, 24, 12), "level %d" % row)

	window.skill_selected.connect(func(skill_id: int): selected = skill_id)
	var skills := {
		10101: 1, 1103: 1, 1104: 2, 1201: 1, 2103: 1,
		2104: 1, 2201: 2, 28101: 3, 7201: 1, 8201: 1
	}
	window.set_skill_book(skills)
	_expect(window.get_node("SkillName0").text == "技能 1103", "sorted first skill")
	_expect(window.get_node("SkillLevel0").text == "1", "server level")
	_expect(not window.get_node("SkillProgress0").visible, "no fabricated proficiency")
	_expect(window.get_node("SkillIcon0").texture == null, "no fabricated icon")
	window.get_node("SkillRow0").pressed.emit()
	_expect(selected == 1103, "row selection")
	window.get_node("ScrollDown").pressed.emit()
	_expect(window.get_node("SkillName0").text == "技能 10101", "scroll jumps by one HK page")
	_expect(window.get_node("SkillName1").text == "技能 28101", "last HK page keeps only remainder")
	_expect(window.get_node("SkillName2").text == "", "last HK page does not overlap prior rows")
	_expect(not window.get_node("ScrollUp").disabled, "scroll up enabled")
	_expect(window.get_node("ScrollDown").disabled, "scroll down disabled on last page")
	window.get_node("ScrollUp").pressed.emit()
	_expect(window.get_node("SkillName0").text == "技能 1103", "scroll up returns one HK page")
	_expect(window.get_node("ScrollUp").disabled, "scroll up disabled on first page")

	window.queue_free()
	print("CLASSIC_SKILL_WINDOW_PASS exact_rects=PASS rows=8 skill_book=server_owned no_fake_icon=true no_fake_progress=true")
	quit(1 if failed else 0)


func _expect_rect(control: Control, expected: Rect2, label: String) -> void:
	_expect(Rect2(control.position, control.size) == expected, label)


func _expect(condition: bool, label: String) -> void:
	if condition:
		return
	failed = true
	push_error("CLASSIC_SKILL_WINDOW_FAIL " + label)
