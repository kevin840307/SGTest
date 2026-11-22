extends Control
## Original 800x600 IE HK2 authentication presentation.
## This shell owns only visuals/hitboxes. Login/character state remains in the
## shared SessionPanel/ClientSession so UI profile switching never reconnects.
const Layout = preload("res://ui/session_layout.gd")
const PlayerPreview = preload("res://player/character_preview.gd")
const AppearanceLabels = preload("res://ui/character_appearance_picker.gd")
const UiTextures = preload("res://ui/ui_texture_loader.gd")

const SOURCE := Vector2(800,600)
const ART := "res://ui/private_ie_hk2"
const CREATE_FIELDS := ["race","hair_index","hair_color","body_index","leg_index","sleeve_color"]

var stage := Control.new()
var login_page := Control.new()
var select_page := Control.new()
var create_page := Control.new()
var _panel
var _asset_store
var _username := LineEdit.new()
var _password := LineEdit.new()
var _name := LineEdit.new()
var _status: Label
var _create_note: Label
var _slots: Array[Button] = []
var _slot_names: Array[Label] = []
var _slot_empty_art: Array[TextureRect] = []
var _slot_play_art: Array[TextureRect] = []
var _previews: Array = []
var _values: Dictionary = {}
var _current_page := -1
var _character_key := ""

func _init() -> void:
	name = "ClassicAuthenticationUI"
	visible = false
	mouse_filter = Control.MOUSE_FILTER_STOP
	stage.size = SOURCE
	add_child(stage)
	for page in [login_page,select_page,create_page]:
		page.size = SOURCE
		stage.add_child(page)
		page.hide()
	_build_login()
	_build_select()
	_build_create()
	set_process(false)
	resize_view(Vector2(1152,620))

func _tex(name: String) -> Texture2D:
	return UiTextures.get_texture(ART.path_join(name + ".png"))

func _art(parent: Control,file: String) -> TextureRect:
	var picture := TextureRect.new()
	picture.name = file.trim_suffix(".png")
	picture.size = SOURCE
	picture.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	picture.stretch_mode = TextureRect.STRETCH_SCALE
	picture.mouse_filter = Control.MOUSE_FILTER_IGNORE
	picture.texture = UiTextures.get_texture(ART.path_join(file))
	parent.add_child(picture)
	return picture

func _input_at(parent: Control,node: Control,x: float,y: float,w: float,h: float) -> void:
	node.position = Vector2(x,y)
	node.size = Vector2(w,h)
	parent.add_child(node)

func _empty_style() -> StyleBoxEmpty:
	return StyleBoxEmpty.new()

func _style_line_edit(input: LineEdit) -> void:
	input.add_theme_stylebox_override("normal",_empty_style())
	input.add_theme_stylebox_override("focus",_empty_style())
	input.add_theme_stylebox_override("read_only",_empty_style())
	input.add_theme_color_override("font_color",Color("#fff7d8"))
	input.add_theme_color_override("font_placeholder_color",Color("#c9b889"))
	input.add_theme_color_override("caret_color",Color("#ffffff"))
	input.add_theme_font_size_override("font_size",13)
	input.select_all_on_focus = false

func _style_hit(button: Button,visible_text:=true) -> void:
	button.flat = true
	button.focus_mode = Control.FOCUS_NONE
	for state in ["normal","hover","pressed","focus","disabled"]:
		button.add_theme_stylebox_override(state,_empty_style())
	button.add_theme_color_override("font_color",Color("#fff4c6") if visible_text else Color.TRANSPARENT)
	button.add_theme_color_override("font_hover_color",Color.WHITE if visible_text else Color.TRANSPARENT)
	button.add_theme_color_override("font_pressed_color",Color("#ffe49b") if visible_text else Color.TRANSPARENT)
	button.add_theme_color_override("font_shadow_color",Color(0,0,0,0.9))
	button.add_theme_constant_override("shadow_offset_x",1)
	button.add_theme_constant_override("shadow_offset_y",1)
	button.add_theme_font_size_override("font_size",12)

func _button(parent: Control,text: String,x: float,y: float,w: float,h: float,callback: Callable,art_name: String="") -> Button:
	if not art_name.is_empty():
		var art := TextureRect.new()
		art.name = art_name + "Art"
		art.texture = _tex(art_name + "_0")
		art.position = Vector2(x,y)
		art.size = Vector2(w,h)
		art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		art.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		art.mouse_filter = Control.MOUSE_FILTER_IGNORE
		parent.add_child(art)
	var button := Button.new()
	button.text = text
	_style_hit(button,true)
	button.pressed.connect(callback)
	_input_at(parent,button,x,y,w,h)
	return button

func _label(parent: Control,text: String,x: float,y: float,w: float,h: float) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_color_override("font_color",Color("#5a3212"))
	l.add_theme_color_override("font_shadow_color",Color("#fff0b8"))
	l.add_theme_constant_override("shadow_offset_x",1)
	l.add_theme_constant_override("shadow_offset_y",1)
	l.add_theme_font_size_override("font_size",12)
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_input_at(parent,l,x,y,w,h)
	return l

func _build_login() -> void:
	_art(login_page,"ACCOUNT_LOGIN.png")
	_username.placeholder_text = "帳號"
	_username.max_length = 32
	_password.secret = true
	_password.max_length = 256
	_password.placeholder_text = "密碼"
	_style_line_edit(_username)
	_style_line_edit(_password)
	_input_at(login_page,_username,368,251,181,22)
	_input_at(login_page,_password,368,277,181,22)
	_password.text_submitted.connect(func(_unused): _login())
	_button(login_page,"登入",365,314,85,21,_login,"YELLOW_BUTTON")
	_button(login_page,"註冊",465,314,85,21,_register,"YELLOW_BUTTON")
	_status = _label(login_page,"",237,343,326,32)
	_status.add_theme_color_override("font_color",Color("#7a3517"))

func _build_select() -> void:
	_art(select_page,"SELECT_CHARACTER.png")
	for i in 3:
		var x := 170.0 + float(i)*188.0
		var name_label := _label(select_page,"",x-25,256,136,22)
		name_label.add_theme_color_override("font_color",Color("#fff0bd"))
		name_label.add_theme_color_override("font_shadow_color",Color.BLACK)
		_slot_names.append(name_label)
		var preview := PlayerPreview.new()
		_input_at(select_page,preview,x,286,85,128)
		preview.bind_store(_asset_store)
		_previews.append(preview)
		var empty_art := TextureRect.new()
		empty_art.texture = _tex("NEW_CHARACTER_0")
		empty_art.position = Vector2(x,284)
		empty_art.size = Vector2(85,132)
		empty_art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		empty_art.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		empty_art.mouse_filter = Control.MOUSE_FILTER_IGNORE
		select_page.add_child(empty_art)
		_slot_empty_art.append(empty_art)
		var play_art := TextureRect.new()
		play_art.texture = _tex("PLAY_GAME_0")
		play_art.position = Vector2(x+9,418)
		play_art.size = Vector2(67,23)
		play_art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		play_art.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		play_art.mouse_filter = Control.MOUSE_FILTER_IGNORE
		play_art.hide()
		select_page.add_child(play_art)
		_slot_play_art.append(play_art)
		var button := Button.new()
		button.name = "CharacterSlot%d" % i
		button.text = ""
		_style_hit(button,false)
		button.pressed.connect(_select_slot.bind(i))
		_input_at(select_page,button,x-4,280,94,164)
		_slots.append(button)
	_button(select_page,"登出",357,482,85,21,func(): _panel.disconnect_session(),"YELLOW_BUTTON")

func _build_create() -> void:
	_art(create_page,"CREATE_CHARACTER.png")
	_name.placeholder_text = "角色名稱"
	_name.max_length = 24
	_style_line_edit(_name)
	_input_at(create_page,_name,228,255,134,22)
	for i in CREATE_FIELDS.size():
		var field: String = CREATE_FIELDS[i]
		var y := 282.0 + float(i)*24.0
		_button(create_page,"",158,y,24,21,_cycle.bind(field,-1),"LEFT_BUTTON")
		var value := _label(create_page,"尚未開放",184,y-1,111,22)
		value.add_theme_color_override("font_color",Color("#fff2c7"))
		value.add_theme_color_override("font_shadow_color",Color.BLACK)
		_values[field] = value
		_button(create_page,"",299,y,24,21,_cycle.bind(field,1),"RIGHT_BUTTON")
	var doll := PlayerPreview.new()
	_input_at(create_page,doll,346,291,76,126)
	doll.bind_store(_asset_store)
	_previews.append(doll)
	var note := _label(create_page,"",147,435,267,52)
	note.add_theme_color_override("font_color",Color("#5a3212"))
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_create_note = note
	_button(create_page,"建立",510,442,85,21,_create,"YELLOW_BUTTON")
	_button(create_page,"返回",600,442,85,21,func(): _panel.back_to_character_button.pressed.emit(),"YELLOW_BUTTON")

func bind_session(panel, store) -> void:
	_panel = panel
	_asset_store = store
	for preview in _previews:
		preview.bind_store(store)
	panel.creation_options.changed.connect(_update_appearance)

func _login() -> void:
	if _panel == null:
		return
	_panel.username_input.text = _username.text
	_panel.password_input.text = _password.text
	_password.clear()
	_panel.login()

func _register() -> void:
	if _panel == null:
		return
	_panel.username_input.text = _username.text
	_panel.password_input.text = _password.text
	_password.clear()
	_panel.register_account()

func _select_slot(index: int) -> void:
	if _panel == null or not _panel.active:
		return
	if index < _panel._characters.size():
		_panel.characters.select(index)
		_panel.choose()
	else:
		_panel.character_slot_input.value = index
		_panel.new_character_button.pressed.emit()

func _cycle(field: String,delta: int) -> void:
	if _panel != null:
		_panel.creation_options.cycle(field,delta)

func _create() -> void:
	if _panel == null:
		return
	_panel.character_name_input.text = _name.text
	_panel.create_character()

func enable_for_page(page: int,on: bool) -> void:
	visible = on and _panel != null
	set_process(visible)
	if not visible:
		return
	_current_page = page
	login_page.visible = page == Layout.Page.LOGIN
	select_page.visible = page == Layout.Page.CHARACTER_SELECT
	create_page.visible = page == Layout.Page.CHARACTER_CREATE
	if create_page.visible:
		_name.text = _panel.character_name_input.text
		_update_appearance()
	if select_page.visible:
		_character_key = ""
		_update_characters()
	if login_page.visible:
		_username.text = _panel.username_input.text

func _process(_delta: float) -> void:
	if _panel == null:
		return
	if _current_page != _panel._layout.current_page:
		enable_for_page(_panel._layout.current_page,true)
	if login_page.visible:
		_status.text = _panel.status.text
	if create_page.visible:
		_create_note.text = _panel.character_create_status.text
	if select_page.visible:
		_update_characters()

func _update_characters() -> void:
	var key := str(_panel._characters)+":"+str(_panel._chosen)
	if key == _character_key:
		return
	_character_key = key
	for i in _slots.size():
		var occupied: bool = i < _panel._characters.size()
		var c: Dictionary = _panel._characters[i] if occupied else {}
		_slots[i].text = str(c.get("name","")) if occupied else ""
		_slots[i].disabled = _panel._chosen
		_slot_names[i].text = str(c.get("name","")) if occupied else "建立角色"
		_slot_empty_art[i].visible = not occupied
		_slot_play_art[i].visible = occupied
		_previews[i].visible = occupied
		_previews[i].set_occupied(occupied)
		_previews[i].set_appearance(c.get("appearance",{}) if occupied else {})

func _update_appearance() -> void:
	if _panel == null:
		return
	var selected: Dictionary = _panel.creation_options.selection()
	for field in _values:
		_values[field].text = AppearanceLabels.value_label(field,int(selected.get(field,-1))) if not selected.is_empty() else "未開放"
	_previews[3].set_occupied(not selected.is_empty())
	_previews[3].set_appearance(selected)
	_create_note.text = "外觀選項直接使用伺服器允許清單；切換 UI 不改角色資料。"

func resize_view(view: Vector2) -> void:
	size = view
	var scale_factor := minf(view.x/SOURCE.x,view.y/SOURCE.y)
	stage.scale = Vector2.ONE*scale_factor
	stage.position = (view-SOURCE*scale_factor)*0.5
