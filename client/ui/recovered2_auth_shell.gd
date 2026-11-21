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

const LOCAL := "res://ui/private_recovered2"
const SIZE := Vector2(1200,900)

var _panel: PanelContainer
var _stage := Control.new()
var _login := Control.new()
var _selection := Control.new()
var _creation := Control.new()
var _video := VideoStreamPlayer.new()
var _theme: Theme
var _account := LineEdit.new()
var _secret := LineEdit.new()
var _address := LineEdit.new()
var _port := SpinBox.new()
var _login_button := Button.new()
var _register_button := Button.new()
var _login_status := Label.new()
var _select_status := Label.new()
var _create_status := Label.new()
var _names: Array[Label] = []
var _slot_actions: Array[Button] = []
var _slot_previews: Array[Label] = []
var _name_edit := LineEdit.new()
var _slot_edit := SpinBox.new()
var _confirm_create := Button.new()
var _page_index := -1
var _skin_picker := OptionButton.new()
var _selection_page := 0
var _last_character_key := ""
var _last_resize := Vector2.ZERO

func _init() -> void:
	name = "Recovered2AuthenticationUI"
	mouse_filter = Control.MOUSE_FILTER_STOP
	visible = false
	_stage.name = "OriginalVisualStage"
	_stage.size = SIZE
	_stage.mouse_filter = Control.MOUSE_FILTER_PASS
	add_child(_stage)
	for page in [_login,_selection,_creation]:
		page.size = SIZE
		page.mouse_filter = Control.MOUSE_FILTER_PASS
		_stage.add_child(page)
		page.hide()
	set_process(false)

func attach(panel: PanelContainer) -> bool:
	_panel = panel
	var loaded := load(LOCAL.path_join("paradise_window_theme.tres"))
	if not loaded is Theme:
		push_warning("Recovered2 source Theme missing; auth adapter unavailable")
		return false
	_theme = loaded
	_stage.theme = _theme
	_build_login()
	_build_select()
	_build_create()
	_build_skin_picker()
	resize_view(get_viewport_rect().size)
	return true

func resize_view(view_size: Vector2) -> void:
	if view_size == _last_resize:
		return
	_last_resize = view_size
	size = view_size
	var factor := minf(view_size.x / SIZE.x, view_size.y / SIZE.y)
	factor = maxf(0.05,factor)
	_stage.scale = Vector2.ONE * factor
	_stage.position = (view_size - SIZE * factor) * 0.5

func enable_for_page(page: int, active: bool) -> void:
	visible = active
	set_process(active)
	if not active:
		_video.stop()
		return
	if _page_index != page:
		_page_index = page
		_login.visible = page == SessionLayout.Page.LOGIN
		_selection.visible = page == SessionLayout.Page.CHARACTER_SELECT
		_creation.visible = page == SessionLayout.Page.CHARACTER_CREATE
		if _login.visible and _video.stream != null:
			_video.play()
		else:
			_video.stop()
		if page == SessionLayout.Page.LOGIN and _panel != null:
			_account.text = _panel.username_input.text
			_address.text = _panel.host_input.text
			_port.value = _panel.port_input.value
		if page == SessionLayout.Page.CHARACTER_CREATE:
			_name_edit.text = _panel.character_name_input.text
			_slot_edit.value = _panel.character_slot_input.value
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
	_put(_stage,_skin_picker,980,17,205,33)

func _build_background(page: Control, path: String) -> TextureRect:
	var backdrop := TextureRect.new()
	backdrop.size = SIZE
	backdrop.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	backdrop.stretch_mode = TextureRect.STRETCH_SCALE
	backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	backdrop.texture = UiTextureLoader.get_texture(LOCAL.path_join(path))
	page.add_child(backdrop)
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

func _build_login() -> void:
	_build_background(_login,"SELECT_CHARACTER.png")
	var stream = load(LOCAL.path_join("2.ogv"))
	if stream is VideoStream:
		_video.stream = stream
		_video.expand = true
		_video.loop = true
		_video.volume_db = -80.0
		_video.position = Vector2.ZERO
		_video.size = SIZE
		_video.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_login.add_child(_video)
		_login.move_child(_video,1)
	var shade := ColorRect.new()
	shade.color = Color(0.025,0.055,0.09,0.42)
	shade.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_put(_login,shade,0,0,SIZE.x,SIZE.y)
	var panel := Panel.new()
	panel.theme_type_variation = &"ParadiseWindowPanel"
	_put(_login,panel,405,205,390,490)
	_label(panel,"星夢 Online  |  帳號登入",20,14,350,40,21).theme_type_variation = &"ParadiseWindowTitle"
	var separator := HSeparator.new()
	separator.theme_type_variation = &"ParadiseHeaderSeparator"
	_put(panel,separator,25,57,340,4)
	_account.placeholder_text = "帳號"
	_account.max_length = 32
	_account.theme_type_variation = &"ParadiseLineEdit"
	_put(panel,_account,48,86,294,40)
	_secret.placeholder_text = "密碼"
	_secret.secret = true
	_secret.max_length = 256
	_secret.theme_type_variation = &"ParadiseLineEdit"
	_put(panel,_secret,48,136,294,40)
	_secret.text_submitted.connect(func(_unused): _submit_login(false))
	_login_button = _button(panel,"登入",48,199,294,43,true)
	_login_button.pressed.connect(_submit_login.bind(false))
	_register_button = _button(panel,"建立帳號並登入",48,255,294,37)
	_register_button.pressed.connect(_submit_login.bind(true))
	_label(panel,"伺服器 / PORT",48,310,294,20,13)
	_address = LineEdit.new()
	_address.theme_type_variation = &"ParadiseLineEdit"
	_address.text = "127.0.0.1"
	_put(panel,_address,48,335,194,30)
	_port.min_value = 1
	_port.max_value = 65535
	_port.value = 7778
	_put(panel,_port,254,335,88,30)
	_login_status = _label(panel,"請輸入帳號與密碼",30,386,330,63,13)
	_login_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART

func refresh_connection_defaults() -> void:
	if _panel != null and not _panel.active:
		_address.text = _panel.host_input.text
		_port.value = _panel.port_input.value

func _submit_login(create_first: bool) -> void:
	if _panel.active:
		return
	_panel.host_input.text = _address.text
	_panel.port_input.value = _port.value
	_panel.username_input.text = _account.text
	_panel.password_input.text = _secret.text
	_secret.clear()
	if create_first:
		_panel.register_account()
	else:
		_panel.login()

func _build_select() -> void:
	_build_background(_selection,"SELECT_CHARACTER.png")
	_label(_selection,"請選擇角色",405,75,390,48,25)
	for i in 3:
		var x := float([220,503,782][i])
		var name := _label(_selection,"（空欄位）",x,385,200,32,17)
		_names.append(name)
		var preview := _label(_selection,"尚未建立角色",x+12,510,176,75,13)
		preview.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		_slot_previews.append(preview)
		var action := _button(_selection,"建立角色",x+36,670,125,37,true)
		action.pressed.connect(_enter_slot.bind(i))
		_slot_actions.append(action)
	var back := _button(_selection,"返回登入",483,801,120,36)
	back.pressed.connect(func(): _panel.disconnect_session())
	var prev := _button(_selection,"上一頁",365,801,108,36)
	prev.pressed.connect(_change_page.bind(-1))
	var next := _button(_selection,"下一頁",611,801,108,36)
	next.pressed.connect(_change_page.bind(1))
	_select_status = _label(_selection,"",368,741,464,35,14)

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
		_panel.new_character_button.pressed.emit()
		_name_edit.clear()
		_slot_edit.value = proposed

func _build_create() -> void:
	_build_background(_creation,"CREATE_CHARACTER.png")
	_name_edit.theme_type_variation = &"ParadiseLineEdit"
	_name_edit.placeholder_text = "新角色名稱"
	_name_edit.max_length = 24
	_put(_creation,_name_edit,343,384,201,31)
	_name_edit.text_submitted.connect(func(_unused): _submit_create())
	_slot_edit.min_value = 0
	_slot_edit.max_value = 7
	_slot_edit.step = 1
	_put(_creation,_slot_edit,386,422,92,30)
	_label(_creation,"欄位",280,422,65,28,14)
	# These fields exist in Recovered2 but SGTest currently has no
	# authoritative race/hair/point-allocation protocol. Never invent values.
	_label(_creation,"外觀與能力分配尚未接入",318,493,235,54,14)
	_label(_creation,"人物預覽待接入",517,581,118,30,13)
	_confirm_create = _button(_creation,"建立角色",747,680,110,31,true)
	_confirm_create.pressed.connect(_submit_create)
	var cancel := _button(_creation,"返回選角",878,680,110,31)
	cancel.pressed.connect(func():
		if _panel._characters.is_empty():
			_panel.disconnect_session()
		else:
			_panel.back_to_character_button.pressed.emit())
	_create_status = _label(_creation,"",263,621,418,36,14)
	_create_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART

func _submit_create() -> void:
	_panel.character_name_input.text = _name_edit.text
	_panel.character_slot_input.value = _slot_edit.value
	_panel.create_character()

func _update_from_session() -> void:
	if _panel == null:
		return
	if _page_index == SessionLayout.Page.LOGIN:
		_login_status.text = _panel.status.text
		_login_button.disabled = _panel.active
		_register_button.disabled = _panel.active
	elif _page_index == SessionLayout.Page.CHARACTER_CREATE:
		_create_status.text = _panel.character_create_status.text
		_confirm_create.disabled = _panel.create_character_button.disabled
	elif _page_index == SessionLayout.Page.CHARACTER_SELECT:
		_select_status.text = _panel.status.text
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
		_names[i].text = str(character.get("name","未建立角色")) if valid else "空白角色欄"
		_slot_previews[i].text = "角色 ID %d\n模型預覽待接入" % int(character.get("character_id",0)) if valid else "可建立新角色"
		_slot_actions[i].text = "進入世界" if valid else "建立角色"
		_slot_actions[i].disabled = _panel._chosen
