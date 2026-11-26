extends SceneTree

const Shell = preload("res://ui/classic_world_shell.gd")
var failed := false


func _init() -> void:
	call_deferred("_run")


func _run() -> void:
	var shell := Shell.new()
	root.add_child(shell)
	var rail := shell.hotkey_panel
	var art := rail.get_node("HotkeyArt") as TextureRect
	var toggle := rail.get_node("HotkeySizeToggle") as TextureButton
	var page1 := rail.get_node("HotkeyPage1") as TextureButton
	var page2 := rail.get_node("HotkeyPage2") as TextureButton
	_expect(shell._hotkey_page == 0, "first page initially selected")
	_expect(page1.texture_normal == shell._tex("OFFON_1") and
		page2.texture_normal == shell._tex("OFFON_0"), "source OFFON frames show first page active")
	for button in [page1, page2, toggle]:
		_expect(button.texture_normal != null and button.texture_hover != null and
			button.texture_pressed != null and button.texture_disabled != null,
			"two-frame HK buttons have all input-state textures")
	page2.pressed.emit()
	_expect(shell._hotkey_page == 1 and page1.texture_normal == shell._tex("OFFON_0") and
		page2.texture_normal == shell._tex("OFFON_1"), "second page actually selects original active frame")
	shell._select_hotkey_page(-1)
	shell._select_hotkey_page(2)
	_expect(shell._hotkey_page == 1, "invalid page cannot change selection")
	page1.pressed.emit()
	_expect(shell._hotkey_page == 0 and page1.texture_normal == shell._tex("OFFON_1"),
		"first page can be selected again")
	_expect(rail.size == Vector2(30, 358), "long HK rail size")
	_expect(art.texture != null and art.texture.get_size() == Vector2(30, 358), "long original texture")
	_expect(toggle.position == Vector2(3, 342), "long collapse hitbox")
	var anchored := Vector2(41, 29)
	rail.position = anchored

	for cycle in 3:
		page2.pressed.emit()
		toggle.pressed.emit()
		_expect(shell._hotkey_page == 1 and page2.texture_normal == shell._tex("OFFON_1"),
			"compact rail preserves selected page")
		_expect(toggle.texture_pressed == shell._tex("HOTKEY_YL_B_1") and
			toggle.texture_disabled == shell._tex("HOTKEY_YL_B_0"), "compact expand has real two-frame states")
		_expect(rail.position == anchored, "toggle preserves the draggable rail position")
		_expect(rail.size == Vector2(30, 196), "short HK rail size")
		_expect(art.texture != null and art.texture.get_size() == Vector2(30, 196),
			"short original texture")
		_expect(art.size == rail.size, "short background bounds")
		_expect(toggle.position == Vector2(3, 179) and toggle.size == Vector2(24, 13),
			"short expand button source rect")
		_expect(toggle.texture_normal != null, "short original expand image")
		for i in 12:
			var label := rail.get_node("Hotkey%d" % i) as Control
			_expect(label.visible == (i < 6), "compact F%d visibility" % (i + 1))
			if i < 6:
				_expect(Rect2(label.position, label.size) ==
					Rect2(10, 17 + 27 * i, 12, 12), "compact F%d source rect" % (i + 1))
		toggle.pressed.emit()
		_expect(shell._hotkey_page == 1 and page2.texture_normal == shell._tex("OFFON_1"),
			"expanded rail preserves selected page")
		_expect(toggle.texture_pressed == shell._tex("HOTKEY_YS_B_1") and
			toggle.texture_disabled == shell._tex("HOTKEY_YS_B_0"), "expanded collapse has real two-frame states")
		page1.pressed.emit()
		_expect(shell._hotkey_page == 0 and page1.texture_normal == shell._tex("OFFON_1"),
			"page selection works after expanding")
		_expect(rail.size == Vector2(30, 358), "expanded HK rail size")
		_expect(art.texture != null and art.texture.get_size() == Vector2(30, 358),
			"expanded original texture")
		_expect(toggle.position == Vector2(3, 342) and toggle.size == Vector2(24, 13),
			"expanded collapse button source rect")
		_expect(toggle.texture_normal != null, "expanded original collapse image")
		var long_ys := [18, 47, 72, 99, 126, 153, 180, 207, 234, 261, 288, 315]
		for i in 12:
			var label := rail.get_node("Hotkey%d" % i) as Control
			_expect(label.visible and Rect2(label.position, label.size) ==
				Rect2(4, long_ys[i], 18 if i >= 9 else 12, 12),
				"expanded F%d source rect" % (i + 1))
			_expect((label as RichTextLabel).autowrap_mode == TextServer.AUTOWRAP_OFF,
				"F%d remains a single-line source caption" % (i + 1))

	shell.free()
	if not failed:
		print("HK_HOTKEY_TOGGLE_PASS original_long_short_art=true source_rects=true cycles=3 drag_anchor=true")
	quit(1 if failed else 0)


func _expect(condition: bool, label: String) -> void:
	if not condition:
		failed = true
		push_error("HK_HOTKEY_TOGGLE_FAIL " + label)
