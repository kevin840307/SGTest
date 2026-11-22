extends Control
## Original 800x600 IE HK2 UI visuals, separate from R2. Same SessionPanel.
## Only original source assets decoded by our converter; all auth state is shared.
const Layout = preload("res://ui/session_layout.gd")
const PlayerPreview = preload("res://player/character_preview.gd")
const AppearanceLabels = preload("res://ui/character_appearance_picker.gd")
const UiTextures = preload("res://ui/ui_texture_loader.gd")
const SOURCE := Vector2(800,600)
const ART := "res://ui/private_ie_hk2"
const LABELS := ["種族","髮型","髮色"]
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

func _button(parent: Control,text: String,x: float,y: float,w: float,h: float,callback: Callable) -> Button:
	var button := Button.new()
	button.text = text
	button.add_theme_font_size_override("font_size",12)
	button.pressed.connect(callback)
	_input_at(parent,button,x,y,w,h)
	return button

func _label(parent: Control,text: String,x: float,y: float,w: float,h: float) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_color_override("font_color",Color("#633b14"))
	l.add_theme_font_size_override("font_size",12)
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_input_at(parent,l,x,y,w,h)
	return l

func _build_login() -> void:
	_art(login_page,"ACCOUNT_LOGIN.png")
	_username.placeholder_text = "帳號"
	_username.max_length = 32
	_username.add_theme_font_size_override("font_size",13)
	_password.secret = true
	_password.max_length = 256
	_password.placeholder_text = "密碼"
	_password.add_theme_font_size_override("font_size",13)
	_input_at(login_page,_username,368,248,181,25)
	_input_at(login_page,_password,368,276,181,25)
	_password.text_submitted.connect(func(_unused): _login())
	_button(login_page,"登入",365,316,84,27,_login)
	_button(login_page,"註冊",465,316,84,27,_register)
	_status = _label(login_page,"",237,350,326,32)

func _build_select() -> void:
	_art(select_page,"SELECT_CHARACTER.png")
	for i in 3:
		var x := 170.0 + float(i)*188.0
		var preview := PlayerPreview.new()
		_input_at(select_page,preview,x,293,86,122)
		preview.bind_store(_asset_store)
		_previews.append(preview)
		var button := _button(select_page,"建立角色",x-26,427,138,27,_select_slot.bind(i))
		_slots.append(button)
	_button(select_page,"登出",342,491,115,27,func(): _panel.disconnect_session())

func _build_create() -> void:
	_art(create_page,"CREATE_CHARACTER.png")
	_name.placeholder_text = "角色名稱"
	_name.max_length = 24
	_input_at(create_page,_name,227,253,134,26)
	var fields := ["race","hair_index","hair_color"]
	for i in fields.size():
		var field: String = str(fields[i])
		var y := 284.0 + float(i)*25.0
		var left := _button(create_page,"◀",158,y,25,24,_cycle.bind(field,-1))
		var value := _label(create_page,"尚未開放",185,y,107,24)
		_values[field] = value
		_button(create_page,"▶",300,y,25,24,_cycle.bind(field,1))
	var doll := PlayerPreview.new()
	_input_at(create_page,doll,346,291,76,126)
	doll.bind_store(_asset_store)
	_previews.append(doll)
	var note := _label(create_page,"",147,435,267,52)
	_create_note = note
	_button(create_page,"建立",510,442,65,29,_create)
	_button(create_page,"返回",591,442,64,29,func(): _panel.back_to_character_button.pressed.emit())

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
		_slots[i].text = ("進入 " + str(c.get("name","角色"))) if occupied else "建立角色"
		_slots[i].disabled = _panel._chosen
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
	_create_note.text = "角色外觀僅顯示來源已有的資產"

func resize_view(view: Vector2) -> void:
	size = view
	var scale_factor := minf(view.x/SOURCE.x,view.y/SOURCE.y)
	stage.scale = Vector2.ONE*scale_factor
	stage.position = (view-SOURCE*scale_factor)*0.5
