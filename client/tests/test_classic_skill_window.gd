extends SceneTree

const SkillWindow = preload("res://ui/classic_skill_window.gd")
var failed := false
var selected := 0


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
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
		5: 0, 6: -1, # Server snapshots can include slots that are not learned.
		10101: 1, 1103: 1, 1104: 2, 1201: 1, 2103: 1,
		2104: 1, 2201: 2, 28101: 3, 7201: 1, 8201: 1
	}
	window.set_skill_book(skills)
	_expect(window._ids.size() == 10, "only positive learned levels populate HK skill pages")
	_expect(window.get_node("SkillName0").text == "技能 1103", "sorted first skill")
	_expect(window.get_node("SkillLevel0").text == "1", "server level")
	_expect(not window.get_node("SkillProgress0").visible, "no fabricated proficiency")
	_expect(window.get_node("SkillIcon0").texture == null, "no fabricated icon")
	window.get_node("SkillRow0").pressed.emit()
	_expect(selected == 1103, "row selection")
	window.get_node("ScrollDown").pressed.emit()
	_expect(window.get_node("SkillName0").text == "技能 10101", "scroll jumps by one HK page")
	var hk_label_loaded: bool = window._labels.has("28101")
	_expect(window.get_node("SkillName1").text == ("治療術" if hk_label_loaded else "技能 28101"),
		"last HK page shows source-backed name when present, numeric fallback otherwise")
	_expect(window.get_node("SkillRow1").tooltip_text != "" if hk_label_loaded else
		window.get_node("SkillRow1").tooltip_text == "",
		"only original HK text supplies skill description tooltip")
	_expect(window.get_node("SkillName2").text == "", "last HK page does not overlap prior rows")
	_expect(not window.get_node("ScrollUp").disabled, "scroll up enabled")
	_expect(window.get_node("ScrollDown").disabled, "scroll down disabled on last page")
	window.get_node("ScrollUp").pressed.emit()
	_expect(window.get_node("SkillName0").text == "技能 1103", "scroll up returns one HK page")
	_expect(window.get_node("ScrollUp").disabled, "scroll up disabled on first page")

	# A background drag must continue across child button/row hitboxes and stop
	# on mouse release, even if the release lands over a different child.
	var down := InputEventMouseButton.new()
	down.button_index = MOUSE_BUTTON_LEFT
	down.pressed = true
	down.position = Vector2(90, 20)
	window._on_window_input(down)
	_expect(window._dragging, "background press starts drag")
	var move := InputEventMouseMotion.new()
	move.relative = Vector2(43, 68)
	window._input(move)
	_expect(window.position == Vector2(43, 68), "drag continues over interactive rows")
	var up := InputEventMouseButton.new()
	up.button_index = MOUSE_BUTTON_LEFT
	up.pressed = false
	window._input(up)
	_expect(not window._dragging, "release over child stops drag")
	window._input(move)
	_expect(window.position == Vector2(43, 68), "movement after release ignored")
	window.get_node("SkillRow0").pressed.emit()
	_expect(selected == 1103, "row button stays interactive after dragging")

	# The HK name source must never grant an unlearned skill. A book refresh
	# removes stale names/tooltip from the eight real clickable rows.
	window.set_skill_book({28101: 3})
	_expect(window.get_node("SkillLevel0").text == "3" and
		window.get_node("SkillName1").text == "" and
		window.get_node("SkillRow1").disabled and
		window.get_node("SkillRow1").tooltip_text == "",
		"client labels cannot fabricate skills missing from Server SkillBook")
	if hk_label_loaded:
		_expect(window.get_node("SkillName0").text == "治療術",
			"HK skill.dat source provides localized display without changing level")
	var last_selected := selected
	window.set_skill_book({28101: 0, 1103: -2})
	_expect(window._ids.is_empty() and window.get_node("SkillRow0").disabled and
		window.get_node("SkillName0").text == "" and
		window.get_node("SkillIcon0").texture == null,
		"zero/negative Server levels must leave no selectable row or stale art")
	window._select_row(0)
	_expect(selected == last_selected, "unlearned ability must never emit an intent")

	# Malformed local display data must be rejected as a whole.
	var invalid_path := "user://sgtest_skill_labels_invalid_%d.json" % OS.get_process_id()
	var invalid := FileAccess.open(invalid_path, FileAccess.WRITE)
	invalid.store_string('{"kind":"hk_skill_labels","version":1,"source_sha256":"' +
		"0".repeat(64) + '","entries":{"28101":{"name":"","description":"unverified"}}}')
	invalid.close()
	_expect(window._read_skill_labels(invalid_path).is_empty(),
		"invalid private label manifest failed open")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(invalid_path))

	window._on_window_input(down)
	_expect(window._dragging, "background drag restarts")
	window.hide()
	window.show()
	window._input(move)
	_expect(not window._dragging and window.position == Vector2(43, 68),
		"close and reopen must cancel an unfinished drag")

	window.queue_free()
	print("CLASSIC_SKILL_WINDOW_PASS exact_rects=PASS rows=8 skill_book=server_owned source_labels=%d drag_release=PASS no_fake_icon=true no_fake_progress=true" % window._labels.size())
	quit(1 if failed else 0)


func _expect_rect(control: Control, expected: Rect2, label: String) -> void:
	_expect(Rect2(control.position, control.size) == expected, label)


func _expect(condition: bool, label: String) -> void:
	if condition:
		return
	failed = true
	push_error("CLASSIC_SKILL_WINDOW_FAIL " + label)
