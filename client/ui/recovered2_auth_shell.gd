extends Control
signal skin_requested(id: String)
## Local Recovered2 login/select/create appearance adapter.
##
## Visual layout follows the locally supplied LoginScreen.tscn and
## character_select.tscn, using their original PNG, video and Theme.
## All authoritative commands remain in the single SGTest SessionPanel.
## This adapter is removable and contains no network/reducer/gameplay logic.
const SessionLayout = preload("res://ui/session_layout.gd")
const UiTextureLoader = preload("res://ui/ui_texture_loader.gd")
const UiSkinCatalog = preload("res://ui/ui_skin_catalog.gd")
const CharacterPreview = preload("res://player/character_preview.gd")
const AppearancePicker = preload("res://ui/character_appearance_picker.gd")
const State = preload("res://world/world_state.gd")

const LOCAL := "res://ui/private_recovered2"
const SIZE := Vector2(1200,900)
const SOURCE_VIEWPORT := Vector2(1400,900)
const EDGE_CLAMP_SHADER := """
shader_type canvas_item;
uniform vec2 viewport_size = vec2(1200.0, 900.0);
uniform vec2 source_size = vec2(1200.0, 900.0);
void fragment() {
	vec2 fitted = source_size * min(
		viewport_size.x / source_size.x,
		viewport_size.y / source_size.y);
	vec2 offset = (viewport_size - fitted) * 0.5;
	vec2 pixel = UV * viewport_size;
	vec2 source_uv = clamp((pixel - offset) / fitted, vec2(0.0), vec2(1.0));
	COLOR = texture(TEXTURE, source_uv);
}
"""

var _panel: PanelContainer
var _stage := Control.new()
var _login := Control.new()
var _selection := Control.new()
var _creation := Control.new()
var _video: VideoStreamPlayer
var _theme: Theme
var _account: LineEdit
var _secret: LineEdit
var _login_content: Control
var _login_window: Panel
var _login_button: Button
var _register_button: Button
var _login_status: Label
var _select_status: Label
var _select_prev: Button
var _select_back: Button
var _select_next: Button
var _create_status: Label
var _names: Array[Label] = []
var _slot_actions: Array[Button] = []
var _slot_password_actions: Array[Button] = []
var _slot_delete_actions: Array[Button] = []
var _slot_empty_arts: Array[AnimatedSprite2D] = []
var _slot_empty_hotspots: Array[Button] = []
var _slot_previews: Array[Label] = []
var _name_edit: LineEdit
var _slot_edit: SpinBox
var _confirm_create: Button
var _page_index := -1
var _skin_picker: OptionButton
var _selection_page := 0
var _last_character_key := ""
var _last_resize := Vector2.ZERO
var _asset_store: RefCounted
var _character_previews: Array[Control] = []
var _appearance_values: Dictionary = {}
var _appearance_buttons: Dictionary = {}
var _appearance_note: Label
var _preview_note: Label
var _stat_labels: Dictionary = {}
var _stat_values: Dictionary = {}
var _remaining_points_label: Label
var _remaining_points: Label
var _create_password_label: Label
var _create_password_confirm_label: Label
var _create_password: LineEdit
var _create_password_confirm: LineEdit
var _choose_password_box: Panel
var _choose_password_edit: LineEdit
var _choose_password_message: Label
var _selection_backdrop_fill: TextureRect
var _creation_backdrop_fill: TextureRect

func set_asset_store(store: RefCounted) -> void:
	_asset_store = store
	for preview in _character_previews:
		preview.bind_store(store)

func _init() -> void:
	name = "Recovered2AuthenticationUI"
	mouse_filter = Control.MOUSE_FILTER_STOP
	visible = false
	_stage.name = "OriginalVisualStage"
	_stage.size = SIZE
	_stage.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_stage)
	for page in [_login,_selection,_creation]:
		page.size = SIZE
		page.mouse_filter = Control.MOUSE_FILTER_PASS
		_stage.add_child(page)
		page.hide()
	set_process(false)

func attach(panel: PanelContainer) -> bool:
	if _theme != null:
		return _panel == panel
	_panel = panel
	var loaded := load(LOCAL.path_join("paradise_window_theme.tres"))
	if not loaded is Theme:
		push_warning("Recovered2 source Theme missing; auth adapter unavailable")
		return false
	_theme = loaded
	theme = _theme
	_stage.theme = _theme
	_build_login()
	_build_select()
	_build_create()
	_build_skin_picker()
	_panel.creation_options.changed.connect(_refresh_appearance)
	_refresh_appearance()
	resize_view(get_viewport_rect().size)
	return true

func resize_view(view_size: Vector2) -> void:
	if view_size == _last_resize:
		return
	_last_resize = view_size
	size = view_size

	# Character select/create source art is 1200x900. Keep that safe content
	# intact and scale it from its own real dimensions instead of the login
	# screen's 1400x900 source viewport. Extra widescreen area is filled by a
	# separate cover backdrop so controls never drift or get clipped.
	var character_factor := minf(view_size.x / SIZE.x, view_size.y / SIZE.y)
	character_factor = maxf(0.05,character_factor)
	_stage.scale = Vector2.ONE * character_factor
	_stage.position = (view_size - SIZE * character_factor) * 0.5

	_login.size = view_size
	var login_factor := minf(
		view_size.x / SOURCE_VIEWPORT.x, view_size.y / SOURCE_VIEWPORT.y)
	login_factor = maxf(0.05,login_factor)
	if _login_content != null:
		_login_content.scale = Vector2.ONE*login_factor
		_login_content.position = (
			view_size-SOURCE_VIEWPORT*login_factor) * 0.5
	for backdrop in [_selection_backdrop_fill,_creation_backdrop_fill]:
		if is_instance_valid(backdrop):
			backdrop.position = Vector2.ZERO
			backdrop.size = view_size
			if backdrop.material is ShaderMaterial:
				(backdrop.material as ShaderMaterial).set_shader_parameter(
					"viewport_size",view_size)

func enable_for_page(page: int, active: bool) -> void:
	# An offline map or unavailable local theme never builds auth controls.
	active = active and _theme != null
	visible = active
	set_process(active)
	if not active:
		if _video != null:
			_video.stop()
		for art in _slot_empty_arts:
			art.stop()
		return
	if _page_index != page:
		_page_index = page
		_login.visible = page == SessionLayout.Page.LOGIN
		_selection.visible = page == SessionLayout.Page.CHARACTER_SELECT
		_creation.visible = page == SessionLayout.Page.CHARACTER_CREATE
		if not _selection.visible:
			for art in _slot_empty_arts:
				art.stop()
		if is_instance_valid(_selection_backdrop_fill):
			_selection_backdrop_fill.visible = _selection.visible
		if is_instance_valid(_creation_backdrop_fill):
			_creation_backdrop_fill.visible = _creation.visible
		if _video != null and _login.visible and _video.stream != null:
			_video.play()
		elif _video != null:
			_video.stop()
		if page == SessionLayout.Page.LOGIN and _panel != null:
			_account.text = _panel.username_input.text
		if page == SessionLayout.Page.CHARACTER_CREATE:
			_name_edit.text = _panel.character_name_input.text
			_slot_edit.value = _panel.character_slot_input.value
			_refresh_stats()
			_refresh_create_ready()
		_last_character_key = ""
	_update_from_session()

func _process(_delta: float) -> void:
	if _panel == null:
		return
	if _panel._layout.current_page != _page_index:
		enable_for_page(_panel._layout.current_page,true)
	else:
		_update_from_session()

func _build_skin_picker() -> void:
	_skin_picker = OptionButton.new()
	_skin_picker.name = "Recovered2SkinSelector"
	_skin_picker.tooltip_text = "切換介面風格（保留目前登入狀態）"
	_skin_picker.theme_type_variation = &"ParadiseSecondaryButton"
	for skin in UiSkinCatalog.available():
		var index := _skin_picker.item_count
		_skin_picker.add_item(str(skin.name))
		_skin_picker.set_item_metadata(index,str(skin.id))
		if str(skin.id) == "recovered2_local":
			_skin_picker.select(index)
	_skin_picker.item_selected.connect(func(index: int):
		skin_requested.emit(str(_skin_picker.get_item_metadata(index))))
	# Keep the selector as a non-visual test/control bridge. Recovered2 auth
	# screens do not contain this SGTest-only widget; UI switching belongs in
	# the in-world System Settings window.
	_put(_stage,_skin_picker,980,17,205,33)
	_skin_picker.hide()

func _build_background(page: Control, path: String) -> TextureRect:
	var backdrop := TextureRect.new()
	backdrop.size = SIZE
	backdrop.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	backdrop.stretch_mode = TextureRect.STRETCH_SCALE
	backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	backdrop.texture = UiTextureLoader.get_texture(LOCAL.path_join(path))
	page.add_child(backdrop)
	return backdrop

func _build_viewport_fill(path: String) -> TextureRect:
	var backdrop := TextureRect.new()
	backdrop.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	# Render the same aspect-fit transform as the 1200x900 safe stage, then
	# clamp pixels outside that fitted rectangle to the nearest source edge.
	# This preserves exact UI/frame alignment while filling ultrawide/tall
	# viewports without a second differently-cropped background or gray bars.
	backdrop.stretch_mode = TextureRect.STRETCH_SCALE
	backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	backdrop.texture = UiTextureLoader.get_texture(LOCAL.path_join(path))
	var shader := Shader.new()
	shader.code = EDGE_CLAMP_SHADER
	var material := ShaderMaterial.new()
	material.shader = shader
	material.set_shader_parameter("source_size",SIZE)
	backdrop.material = material
	backdrop.visible = false
	add_child(backdrop)
	move_child(backdrop,0)
	return backdrop

func _put(parent: Control, node: Control, x: float, y: float, w: float, h: float) -> void:
	node.position = Vector2(x,y)
	node.size = Vector2(w,h)
	parent.add_child(node)

func _label(parent: Control, value: String, x: float, y: float, w: float, h: float, font_size: int = 17) -> Label:
	var label := Label.new()
	label.text = value
	label.theme_type_variation = &"ParadiseBodyText"
	label.add_theme_font_size_override("font_size",font_size)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_put(parent,label,x,y,w,h)
	return label

func _button(parent: Control, value: String, x: float, y: float, w: float, h: float, primary: bool = false) -> Button:
	var button := Button.new()
	button.text = value
	button.focus_mode = Control.FOCUS_NONE
	button.theme_type_variation = &"ParadisePrimaryButton" if primary else &"ParadiseSecondaryButton"
	_put(parent,button,x,y,w,h)
	return button

func _set_line_edit_background_alpha(
		edit: LineEdit, normal_alpha: float, focus_alpha: float) -> void:
	for state in ["normal","focus"]:
		var source := edit.get_theme_stylebox(state)
		if source == null:
			continue
		var style := source.duplicate()
		var alpha := normal_alpha if state == "normal" else focus_alpha
		if style is StyleBoxFlat:
			var color: Color = style.bg_color
			color.a = alpha
			style.bg_color = color
		elif style is StyleBoxTexture:
			var tint: Color = style.modulate_color
			tint.a = alpha
			style.modulate_color = tint
		edit.add_theme_stylebox_override(state,style)

func _build_login() -> void:
	# LoginScreen uses viewport anchors, unlike the fixed 1200x900 character page.
	# Its 280x320 panel belongs to the source 1400x900 canvas_items viewport.
	_login.reparent(self)
	move_child(_login,0)
	var background := ColorRect.new()
	background.color = Color.BLACK
	background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_login.add_child(background)
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_login_content = Control.new()
	_login_content.size = SOURCE_VIEWPORT
	_login_content.mouse_filter = Control.MOUSE_FILTER_PASS
	_login.add_child(_login_content)
	var aspect := AspectRatioContainer.new()
	aspect.ratio = 1.7778
	aspect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_login_content.add_child(aspect)
	aspect.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var stream = load(LOCAL.path_join("2.ogv"))
	if stream is VideoStream:
		_video = VideoStreamPlayer.new()
		_video.stream = stream
		_video.expand = true
		_video.loop = true
		_video.volume_db = -80.0
		_video.mouse_filter = Control.MOUSE_FILTER_IGNORE
		aspect.add_child(_video)
	_login_window = Panel.new()
	_login_window.name = "LoginPanel"
	_login_window.theme_type_variation = &"ParadiseWindowPanel"
	_put(_login_content,_login_window,560,290,280,320)
	_label(_login_window,"帳號登入",18,10,244,30,20).theme_type_variation = &"ParadiseWindowTitle"
	var separator := HSeparator.new()
	separator.theme_type_variation = &"ParadiseHeaderSeparator"
	_put(_login_window,separator,16,47,248,3)
	_account = LineEdit.new()
	_account.placeholder_text = "請輸入帳號"
	_account.max_length = 32 # Formal Server contract; appearance follows source.
	_account.alignment = HORIZONTAL_ALIGNMENT_CENTER
	_account.add_theme_font_size_override("font_size",18)
	_account.theme_type_variation = &"ParadiseLineEdit"
	_put(_login_window,_account,25,59,230,50)
	_set_line_edit_background_alpha(_account,0.72,0.84)
	_secret = LineEdit.new()
	_secret.placeholder_text = "請輸入密碼"
	_secret.secret = true
	_secret.max_length = 256
	_secret.alignment = HORIZONTAL_ALIGNMENT_CENTER
	_secret.add_theme_font_size_override("font_size",18)
	_secret.theme_type_variation = &"ParadiseLineEdit"
	_put(_login_window,_secret,25,121,230,50)
	_set_line_edit_background_alpha(_secret,0.72,0.84)
	_secret.text_submitted.connect(func(_unused): _submit_login(false))
	_login_button = _button(_login_window,"登入",77.5,188,125,45,true)
	_login_button.add_theme_font_size_override("font_size",18)
	_login_button.pressed.connect(_submit_login.bind(false))
	_register_button = _button(_login_window,"註冊帳號",77.5,254,125,45)
	_register_button.add_theme_font_size_override("font_size",18)
	_register_button.pressed.connect(_submit_login.bind(true))
	# SGTest registration still delegates to its existing authenticated path.
	# Separate source registration/email dialog remains an explicit TODO.
	_login_status = _label(_login_window,"",0,326,280,48,13)
	_login_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART

func _submit_login(create_first: bool) -> void:
	if _panel.active:
		return
	_panel.username_input.text = _account.text
	_panel.password_input.text = _secret.text
	_secret.clear()
	if create_first:
		_panel.register_account()
	else:
		_panel.login()

func _build_select() -> void:
	_selection_backdrop_fill = _build_viewport_fill("SELECT_CHARACTER.png")
	_build_background(_selection,"SELECT_CHARACTER.png")
	_label(_selection,"請選擇角色",405,75,390,48,25)
	# Reuse the four original Recovered2 NEW_CHARACTER frames (2 FPS), not
	# a hand-drawn substitute or the yellow IE HK2 selection art.
	var empty_frames := SpriteFrames.new()
	if not empty_frames.has_animation(&"default"):
		empty_frames.add_animation(&"default")
	empty_frames.set_animation_speed(&"default",2.0)
	for frame in 4:
		var texture := UiTextureLoader.get_texture(
			LOCAL.path_join("create_anim/NEW_CHARACTER.spr.%04d.png" % frame))
		if texture != null:
			empty_frames.add_frame(&"default",texture)
	for i in 3:
		var x := float([220,503,782][i])
		var name := _label(_selection,"",x,385,200,32,17)
		_names.append(name)
		var doll := CharacterPreview.new()
		_put(_selection,doll,x+12,422,176,200)
		doll.bind_store(_asset_store)
		_character_previews.append(doll)
		var art := AnimatedSprite2D.new()
		art.name = "EmptyCharacterArt%d" % i
		art.position = Vector2(x+100,522)
		art.sprite_frames = empty_frames
		_selection.add_child(art)
		_slot_empty_arts.append(art)
		var hotspot := Button.new()
		hotspot.name = "CreateCharacterSlot%d" % i
		hotspot.flat = true
		hotspot.focus_mode = Control.FOCUS_NONE
		hotspot.tooltip_text = "建立新角色"
		_put(_selection,hotspot,x+12,422,176,200)
		hotspot.pressed.connect(_enter_slot.bind(i))
		_slot_empty_hotspots.append(hotspot)
		var preview := _label(_selection,"",x+12,610,176,20,11)
		_slot_previews.append(preview)
		doll.status_changed.connect(func(_status): _last_character_key = "")
		var password := _button(_selection,"密碼設定",x+36,629,125,31)
		password.pressed.connect(_unsupported_slot_action.bind("密碼設定",i))
		_slot_password_actions.append(password)
		var action := _button(_selection,"進入遊戲",x+36,670,125,37,true)
		action.pressed.connect(_enter_slot.bind(i))
		_slot_actions.append(action)
		var remove := _button(_selection,"刪除角色",x+36,711,125,37)
		remove.theme_type_variation = &"ParadiseDangerButton"
		remove.pressed.connect(_unsupported_slot_action.bind("刪除角色",i))
		_slot_delete_actions.append(remove)
	# Treat the three navigation buttons as one centered group. Keeping only
	# the middle button centered made Previous/Next visibly skewed and even
	# overlapped the middle hitbox at some source scales.
	var nav_gap := 14.0
	var nav_width := 108.0 + nav_gap + 120.0 + nav_gap + 108.0
	var nav_x := (SIZE.x-nav_width)*0.5
	_select_prev = _button(_selection,"上一頁",nav_x,801,108,36)
	_select_prev.pressed.connect(_change_page.bind(-1))
	_select_back = _button(
		_selection,"返回登入",nav_x+108.0+nav_gap,801,120,36)
	_select_back.pressed.connect(func(): _panel.disconnect_session())
	_select_next = _button(
		_selection,"下一頁",nav_x+108.0+nav_gap+120.0+nav_gap,801,108,36)
	_select_next.pressed.connect(_change_page.bind(1))
	_select_status = _label(_selection,"",368,759,464,32,14)
	_choose_password_box = Panel.new()
	_choose_password_box.theme_type_variation = &"ParadiseWindowPanel"
	_put(_selection,_choose_password_box,450,646,300,104)
	_choose_password_message = _label(
		_choose_password_box,"請輸入 4 位角色密碼",15,8,270,25,13)
	_choose_password_edit = LineEdit.new()
	_choose_password_edit.secret = true
	_choose_password_edit.max_length = 4
	_choose_password_edit.alignment = HORIZONTAL_ALIGNMENT_CENTER
	_choose_password_edit.theme_type_variation = &"ParadiseLineEdit"
	_put(_choose_password_box,_choose_password_edit,25,42,165,38)
	var unlock := _button(
		_choose_password_box,"確認",204,42,72,38,true)
	unlock.pressed.connect(_submit_character_password)
	_choose_password_edit.text_submitted.connect(
		func(_unused): _submit_character_password())
	_choose_password_box.hide()

func _unsupported_slot_action(action: String, slot_on_page: int) -> void:
	var index := _selection_page*3+slot_on_page
	if not _panel.active or index >= _panel._characters.size():
		return
	# The SGTest wire protocol has no authenticated password-change/delete
	# request yet. Never modify local character state to simulate a success.
	_panel.status.text = "伺服器尚未支援%s" % ("角色密碼設定" if action == "密碼設定" else "刪除角色")

func _submit_character_password() -> void:
	if _choose_password_edit.text.length() != 4:
		_choose_password_message.text = "角色密碼必須是 4 個字元"
		return
	_panel.choose(_choose_password_edit.text)
	_choose_password_edit.clear()

func _change_page(delta: int) -> void:
	_selection_page = clampi(_selection_page+delta,0,2)
	_last_character_key = ""
	_update_characters()

func _enter_slot(slot_on_page: int) -> void:
	var index := _selection_page * 3 + slot_on_page
	if index < _panel._characters.size() and _panel.active:
		_panel.characters.select(index)
		_panel.choose()
	elif _panel.active:
		var taken := {}
		for character in _panel._characters:
			taken[int(character.get("slot",-1))] = true
		# Prefer the selected free slot, then any valid free backend slot.
		var proposed := index
		if proposed > 7 or taken.has(proposed):
			for s in range(8):
				if not taken.has(s):
					proposed = s
					break
		_panel.character_slot_input.value = proposed
		for stat in [
			_panel.character_strength_input,_panel.character_dexterity_input,
			_panel.character_constitution_input,_panel.character_wisdom_input
		]:
			stat.value = 1
		_panel.character_password_input.clear()
		_panel.character_password_confirm_input.clear()
		_panel.new_character_button.pressed.emit()
		_name_edit.clear()
		_slot_edit.value = proposed
		if _create_password != null:
			_create_password.clear()
			_create_password_confirm.clear()
		_refresh_stats()

func _build_create() -> void:
	_creation_backdrop_fill = _build_viewport_fill("CREATE_CHARACTER.png")
	_build_background(_creation,"CREATE_CHARACTER.png")
	_name_edit = LineEdit.new()
	_slot_edit = SpinBox.new()
	_name_edit.theme_type_variation = &"ParadiseLineEdit"
	_name_edit.placeholder_text = "新角色名稱"
	_name_edit.max_length = 24
	_put(_creation,_name_edit,343,384,201,31)
	_name_edit.text_submitted.connect(func(_unused): _submit_create())
	_slot_edit.min_value = 0
	_slot_edit.max_value = 7
	_slot_edit.step = 1
	_put(_creation,_slot_edit,386,422,92,30)
	# Recovered2 chooses a slot on the selection screen, not in the race row.
	_slot_edit.hide()
	var appearance_fields := [
		"race","hair_index","hair_color","body_index","leg_index","sleeve_color"]
	var appearance_labels := [
		"種族","髮型","髮色","上衣顏色","短褲顏色","手袖顏色"]
	for i in appearance_fields.size():
		var field: String = appearance_fields[i]
		var y := float(424+36*i)
		# The first three rows mirror Recovered2; the last three restore original
		# HK2 Upper_armor / Lower_armor and synchronized hand/wrist selectors.
		var source_label := _label(
			_creation,appearance_labels[i],232,y,78,24,14)
		source_label.theme_type_variation = &"ParadiseSecondaryText"
		var value := _label(_creation,"未開放",386,y-2,72,29,14)
		value.theme_type_variation = &"ParadiseAccentText"
		_appearance_values[field] = value
		_appearance_buttons[field] = [
			_appearance_arrow(field,-1,351,y,25),
			_appearance_arrow(field,1,464,y,25)
		]
	_appearance_note = _label(_creation,"",318,539,200,51,11)
	# Internal Server allowlist/stat guidance is not player-facing UI.
	_appearance_note.hide()

	var stat_fields := ["strength","dexterity","constitution","wisdom"]
	var stat_names := ["力量","速度","體質","智慧"]
	var stat_label_widths := [77.0,78.0,78.0,78.0]
	for i in 4:
		var field: String = stat_fields[i]
		var label_y := float(388+36*i)
		var button_y := float(387+36*i)
		var value_y := float(386+36*i)
		var stat_label := _label(
			_creation,stat_names[i],745,label_y,stat_label_widths[i],24,14)
		stat_label.theme_type_variation = &"ParadiseSecondaryText"
		_stat_labels[field] = stat_label
		var minus := _source_step_button(
			-1,857,button_y,_adjust_stat.bind(field,-1))
		var value := _label(_creation,"1",891,value_y,71,27,14)
		value.theme_type_variation = &"ParadiseAccentText"
		_stat_values[field] = value
		var plus := _source_step_button(
			1,967,button_y,_adjust_stat.bind(field,1))
	_remaining_points_label = _label(
		_creation,"剩餘配點",745,532,78,24,14)
	_remaining_points_label.theme_type_variation = &"ParadiseSecondaryText"
	_remaining_points = _label(_creation,"4",891,530,71,27,14)
	_remaining_points.theme_type_variation = &"ParadiseAccentText"

	_create_password_label = _label(
		_creation,"角色密碼",745,583,78,24,14)
	_create_password_label.theme_type_variation = &"ParadiseSecondaryText"
	_create_password_confirm_label = _label(
		_creation,"確認密碼",745,619,78,24,14)
	_create_password_confirm_label.theme_type_variation = &"ParadiseSecondaryText"
	_create_password = LineEdit.new()
	_create_password.secret = true
	_create_password.max_length = 4
	_create_password.alignment = HORIZONTAL_ALIGNMENT_CENTER
	_create_password.theme_type_variation = &"ParadiseLineEdit"
	_put(_creation,_create_password,897,579,83,31)
	_create_password_confirm = LineEdit.new()
	_create_password_confirm.secret = true
	_create_password_confirm.max_length = 4
	_create_password_confirm.alignment = HORIZONTAL_ALIGNMENT_CENTER
	_create_password_confirm.theme_type_variation = &"ParadiseLineEdit"
	_put(_creation,_create_password_confirm,897,615,83,31)
	_create_password.text_changed.connect(func(_text): _refresh_create_ready())
	_create_password_confirm.text_changed.connect(func(_text): _refresh_create_ready())

	var doll := CharacterPreview.new()
	_put(_creation,doll,516,444,109,177)
	doll.bind_store(_asset_store)
	doll.set_occupied(true)
	doll.tooltip_text = "預覽目前選擇的人物外觀；部分外觀暫無預覽"
	_character_previews.append(doll)
	_preview_note = _label(_creation,"",507,594,128,38,10)
	_preview_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	doll.status_changed.connect(func(_status): _refresh_appearance())
	_confirm_create = _button(_creation,"建立角色",747,680,110,31,true)
	_confirm_create.pressed.connect(_submit_create)
	var cancel := _button(_creation,"返回選角",878,680,110,31)
	cancel.pressed.connect(func():
		if _panel._characters.is_empty():
			_panel.disconnect_session()
		else:
			_panel.back_to_character_button.pressed.emit())
	_create_status = _label(_creation,"",263,642,418,28,14)
	_create_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART

func _stat_control(field: String) -> SpinBox:
	match field:
		"strength":
			return _panel.character_strength_input
		"dexterity":
			return _panel.character_dexterity_input
		"constitution":
			return _panel.character_constitution_input
		_:
			return _panel.character_wisdom_input

func _remaining_stat_points() -> int:
	return 8 - (
		int(_panel.character_strength_input.value) +
		int(_panel.character_dexterity_input.value) +
		int(_panel.character_constitution_input.value) +
		int(_panel.character_wisdom_input.value))

func _adjust_stat(field: String, delta: int) -> void:
	var stat := _stat_control(field)
	var next_value := int(stat.value) + delta
	if next_value < 1 or next_value > 5:
		return
	if delta > 0 and _remaining_stat_points() <= 0:
		return
	stat.value = next_value
	_refresh_stats()

func _refresh_stats() -> void:
	if _panel == null or _remaining_points == null:
		return
	for field in _stat_values:
		_stat_values[field].text = str(int(_stat_control(field).value))
	_remaining_points.text = str(maxi(0,_remaining_stat_points()))
	_refresh_create_ready()

func _refresh_create_ready() -> void:
	if _confirm_create == null or _panel == null:
		return
	var password_ready := (
		_create_password != null and
		_create_password.text.length() == 4 and
		_create_password.text == _create_password_confirm.text)
	_confirm_create.disabled = (
		not _panel.creation_options.editable or
		_remaining_stat_points() != 0 or
		not password_ready or
		_panel.create_character_button.disabled)

func _source_step_button(
		delta: int, x: float, y: float, callback: Callable,
		height: float = 25.0) -> BaseButton:
	var texture := UiTextureLoader.get_texture(
		LOCAL.path_join("LEFT_BUTTON.png" if delta<0 else "RIGHT_BUTTON.png"))
	var button: BaseButton
	if texture != null:
		var image_button := TextureButton.new()
		image_button.texture_normal = texture
		image_button.ignore_texture_size = true
		image_button.stretch_mode = TextureButton.STRETCH_SCALE
		button = image_button
	else:
		var fallback := Button.new()
		fallback.text = "−" if delta<0 else "+"
		fallback.theme_type_variation = &"ParadiseSecondaryButton"
		button = fallback
	button.focus_mode = Control.FOCUS_NONE
	button.pressed.connect(callback)
	_put(_creation,button,x,y,29,height)
	return button

func _appearance_arrow(
		field: String, delta: int, x: float, y: float,
		height: float = 25.0) -> BaseButton:
	return _source_step_button(
		delta,x,y,func(): _panel.creation_options.cycle(field,delta),height)

func _refresh_appearance() -> void:
	var options = _panel.creation_options
	var selected: Dictionary = options.selection()
	for field in _appearance_values:
		_appearance_values[field].text = AppearancePicker.value_label(field,int(selected[field])) if not selected.is_empty() else "未開放"
		for button in _appearance_buttons[field]:
			button.disabled = not options.editable or options.values(field).size()<2
			button.modulate = Color(1,1,1,0.45 if button.disabled else 1.0)
	_appearance_note.text = ""
	var preview: Control = _character_previews[3]
	preview.set_occupied(_panel.active and int(_panel._latest.get("state",-1)) in [State.STATE_SELECTING_CHARACTER,State.STATE_CREATING_CHARACTER])
	preview.set_appearance(selected)
	_preview_note.text = "此外觀暫無預覽" if preview.status == "unsupported" else ("預設人物示意" if selected.is_empty() else "基本外觀預覽")
	if preview.status in ["loading","unavailable"]:
		_preview_note.text = "人物素材載入中" if preview.status == "loading" else "人物素材未就緒"
	_refresh_create_ready()

func _submit_create() -> void:
	_panel.character_name_input.text = _name_edit.text
	_panel.character_slot_input.value = _slot_edit.value
	_panel.character_password_input.text = _create_password.text
	_panel.character_password_confirm_input.text = _create_password_confirm.text
	_panel.create_character()
	_create_password.clear()
	_create_password_confirm.clear()
	_refresh_create_ready()

func _update_from_session() -> void:
	if _panel == null:
		return
	if _page_index == SessionLayout.Page.LOGIN:
		_login_status.text = _panel.status.text
		_login_status.visible = _panel.active or not (_panel.status.text.begins_with("請登入") or _panel.status.text.begins_with("請輸入"))
		_login_button.disabled = _panel.active
		_register_button.disabled = _panel.active
	elif _page_index == SessionLayout.Page.CHARACTER_CREATE:
		_create_status.text = _panel.character_create_status.text
		_refresh_stats()
		_refresh_create_ready()
	elif _page_index == SessionLayout.Page.CHARACTER_SELECT:
		_select_status.text = _panel.status.text
		var choose_code := int(_panel._latest.get("choose_status",-1))
		_choose_password_box.visible = choose_code in [3,4,5]
		if choose_code == 3:
			_choose_password_message.text = "請輸入 4 位角色密碼"
		elif choose_code == 4:
			_choose_password_message.text = "密碼錯誤，請重新輸入"
		elif choose_code == 5:
			_choose_password_message.text = "密碼暫時鎖定，稍後再試"
		_update_characters()

func _update_characters() -> void:
	var key := str(_panel._characters) + ":" + str(_selection_page) + ":" + str(_panel._chosen)
	if key == _last_character_key:
		return
	_last_character_key = key
	for i in 3:
		var index := _selection_page * 3 + i
		var valid: bool = index < _panel._characters.size()
		var character: Dictionary = _panel._characters[index] if valid else {}
		_names[i].text = str(character.get("name","")) if valid else ""
		var appearance: Dictionary = character.get("appearance",{}) if valid else {}
		_character_previews[i].set_appearance(appearance)
		_character_previews[i].set_occupied(valid)
		_slot_previews[i].text = ""
		_slot_empty_arts[i].visible = not valid
		if not valid and _slot_empty_arts[i].sprite_frames.get_frame_count(&"default") > 0:
			_slot_empty_arts[i].play(&"default")
		else:
			_slot_empty_arts[i].stop()
		_slot_empty_hotspots[i].visible = not valid
		_slot_empty_hotspots[i].disabled = _panel._chosen or not _panel.active
		_slot_password_actions[i].visible = valid
		_slot_password_actions[i].disabled = _panel._chosen or not _panel.active
		_slot_actions[i].visible = valid
		_slot_actions[i].text = "進入遊戲"
		_slot_actions[i].disabled = _panel._chosen or not _panel.active
		_slot_delete_actions[i].visible = valid
		_slot_delete_actions[i].disabled = _panel._chosen or not _panel.active
