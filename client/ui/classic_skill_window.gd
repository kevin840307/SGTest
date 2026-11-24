extends Control
## HK 027_Skill.itf presentation. Server SkillBook remains authoritative.
## Names/icons/proficiency are intentionally not invented when the current
## snapshot only provides skill_id -> learned level.
signal skill_selected(skill_id: int)

const UiTextures = preload("res://ui/ui_texture_loader.gd")
const PRIVATE := "res://ui/private_ie_hk2"
const WINDOW_SIZE := Vector2(223, 288)
const PAGE_SIZE := 8
const ROW_STEP := 27.0
const ROW_Y := 54.0
const TEXT_BASELINE := 10.0
const HK_FONT_BITMAP := PRIVATE + "/HK_SHARED_UI_FONT.fnt"

class ItfText extends Control:
	var text_font: Font
	var text := "":
		set(value):
			if text != value:
				text = value
				queue_redraw()

	func _draw() -> void:
		var font := text_font if text_font != null else get_theme_default_font()
		draw_string(font, Vector2(0, TEXT_BASELINE), text,
			HORIZONTAL_ALIGNMENT_LEFT, int(size.x), 12, Color.WHITE)

var _font: Font
var _skills: Dictionary = {}
var _ids: Array = []
var _page_start := 0
var _dragging := false
var _drag_offset := Vector2.ZERO
var _name_labels: Array[ItfText] = []
var _level_labels: Array[ItfText] = []
var _row_buttons: Array[Button] = []
var _icon_slots: Array[TextureRect] = []
var _progress_slots: Array[Control] = []
var _scroll_up: TextureButton
var _scroll_down: TextureButton


func _init() -> void:
	name = "SkillWindow"
	size = WINDOW_SIZE
	mouse_filter = Control.MOUSE_FILTER_STOP
	_font = _load_hk_font()
	_build()


func _load_hk_font() -> Font:
	if FileAccess.file_exists(HK_FONT_BITMAP):
		var bitmap := FontFile.new()
		if bitmap.load_bitmap_font(HK_FONT_BITMAP) == OK:
			return bitmap
	var fallback := SystemFont.new()
	fallback.font_names = PackedStringArray(["新細明體"])
	fallback.font_weight = 500
	return fallback


func _texture(name: String) -> Texture2D:
	return UiTextures.get_texture(PRIVATE.path_join(name + ".png"))


func _build() -> void:
	var background := TextureRect.new()
	background.name = "BG_SKILL_S"
	background.texture = _texture("BG_SKILL_S")
	background.size = WINDOW_SIZE
	background.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	background.stretch_mode = TextureRect.STRETCH_SCALE
	background.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(background)

	_add_text("SkillNameHeader", Rect2(74, 39, 48, 12), "技能名稱")
	_add_text("SkillLevelHeader", Rect2(170, 39, 24, 12), "等級")

	for row in PAGE_SIZE:
		var y := ROW_Y + ROW_STEP * row

		var hit := Button.new()
		hit.name = "SkillRow%d" % row
		hit.position = Vector2(5, y)
		hit.size = Vector2(195, 27)
		hit.flat = true
		hit.focus_mode = Control.FOCUS_NONE
		hit.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		hit.pressed.connect(_select_row.bind(row))
		add_child(hit)
		_row_buttons.append(hit)

		var icon := TextureRect.new()
		icon.name = "SkillIcon%d" % row
		icon.position = Vector2(7, y)
		icon.size = Vector2(24, 24)
		icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		icon.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
		icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(icon)
		_icon_slots.append(icon)

		var progress := Control.new()
		progress.name = "SkillProgress%d" % row
		progress.position = Vector2(72, y + 1)
		progress.size = Vector2(89, 4)
		progress.clip_contents = true
		progress.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(progress)
		_progress_slots.append(progress)

		var name_label := _add_text(
			"SkillName%d" % row, Rect2(35, y + 12, 120, 12), "")
		var level_label := _add_text(
			"SkillLevel%d" % row, Rect2(169, y + 12, 24, 12), "")
		_name_labels.append(name_label)
		_level_labels.append(level_label)

	_texture_button(
		"CloseWindow", "CLOSESCREEN", Vector2(204, 1), Vector2(16, 16),
		hide, "關閉")
	_texture_button(
		"OnlineHelp", "B_ONLINEHELP", Vector2(187, 1), Vector2(16, 16),
		_show_help, "技能說明")
	_scroll_up = _texture_button(
		"ScrollUp", "SCROLLUP_BUTTON", Vector2(202, 33), Vector2(17, 36),
		_scroll_page.bind(-1), "上一列")
	_scroll_down = _texture_button(
		"ScrollDown", "SCROLLDOWN_BUTTON", Vector2(202, 244), Vector2(17, 36),
		_scroll_page.bind(1), "下一列")

	gui_input.connect(_on_window_input)
	_refresh()


func _add_text(node_name: String, rect: Rect2, value: String) -> ItfText:
	var label := ItfText.new()
	label.name = node_name
	label.position = rect.position
	label.size = rect.size
	label.clip_contents = true
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.text_font = _font
	label.text = value
	add_child(label)
	return label


func _texture_button(
	node_name: String,
	art: String,
	button_position: Vector2,
	button_size: Vector2,
	callback: Callable,
	tooltip: String
) -> TextureButton:
	var button := TextureButton.new()
	button.name = node_name
	button.position = button_position
	button.size = button_size
	button.ignore_texture_size = true
	button.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	button.texture_normal = _texture(art + "_0")
	button.texture_hover = _texture(art + "_1")
	button.texture_pressed = _texture(art + "_2")
	button.texture_disabled = _texture(art + "_3")
	button.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	button.focus_mode = Control.FOCUS_NONE
	button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	button.tooltip_text = tooltip
	button.pressed.connect(callback)
	add_child(button)
	return button


func set_skill_book(skills: Dictionary) -> void:
	_skills = skills.duplicate(true)
	_ids = _skills.keys()
	_ids.sort()
	_page_start = mini(_page_start, maxi(0, _ids.size() - PAGE_SIZE))
	_refresh()


func _refresh() -> void:
	for row in PAGE_SIZE:
		var source_index := _page_start + row
		var has_skill := source_index >= 0 and source_index < _ids.size()
		_row_buttons[row].disabled = not has_skill
		_icon_slots[row].texture = null
		_progress_slots[row].visible = false
		if not has_skill:
			_name_labels[row].text = ""
			_level_labels[row].text = ""
			continue
		var skill_id := int(_ids[source_index])
		var level := int(_skills.get(_ids[source_index], 0))
		# Current authoritative snapshot has IDs/levels only. Keep the source
		# list usable without inventing localized names, icon frame IDs, or EXP.
		_name_labels[row].text = "技能 %d" % skill_id
		_level_labels[row].text = str(level)

	if _scroll_up != null:
		_scroll_up.disabled = _page_start <= 0
	if _scroll_down != null:
		_scroll_down.disabled = _page_start + PAGE_SIZE >= _ids.size()


func _scroll_page(direction: int) -> void:
	if direction < 0:
		_page_start = maxi(0, _page_start - 1)
	elif direction > 0:
		_page_start = mini(maxi(0, _ids.size() - PAGE_SIZE), _page_start + 1)
	_refresh()


func _select_row(row: int) -> void:
	var source_index := _page_start + row
	if source_index < 0 or source_index >= _ids.size():
		return
	skill_selected.emit(int(_ids[source_index]))


func _show_help() -> void:
	# The original OH01 control opens contextual help. No authoritative current
	# help payload is wired yet, so this remains a no-op rather than fake text.
	pass


func _on_window_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			_dragging = true
			_drag_offset = event.position
			var parent := get_parent()
			if parent != null:
				parent.move_child(self, parent.get_child_count() - 1)
			accept_event()
		elif _dragging:
			_dragging = false
			accept_event()
	elif event is InputEventMouseMotion and _dragging:
		var parent_control := get_parent() as Control
		var target: Vector2 = global_position + (event as InputEventMouseMotion).relative
		if parent_control != null:
			var bounds := parent_control.get_global_rect()
			target.x = clampf(target.x, bounds.position.x - size.x + 24.0,
				bounds.end.x - 24.0)
			target.y = clampf(target.y, bounds.position.y - size.y + 24.0,
				bounds.end.y - 24.0)
		global_position = target
		accept_event()
