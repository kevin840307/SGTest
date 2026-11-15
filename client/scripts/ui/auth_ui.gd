class_name AuthUI
extends CanvasLayer

signal login_requested(username: String,password: String)
signal register_requested(username: String,password: String)
signal create_character_requested(name: String)
signal select_character_requested(character_id: int)
signal logout_requested

var login_center: CenterContainer
var character_center: CenterContainer
var username_edit: LineEdit
var password_edit: LineEdit
var login_status: Label
var character_status: Label
var character_list: ItemList
var character_name_edit: LineEdit

func _ready() -> void:
	layer = 100
	_build_login()
	_build_characters()
	show_login("連線中…")

func _full_center() -> CenterContainer:
	var center := CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(center)
	return center

func _panel(width: float = 390.0) -> VBoxContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = Vector2(width,0)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation",10)
	panel.add_child(box)
	return box

func _build_login() -> void:
	login_center = _full_center()
	var box := _panel()
	login_center.add_child(box.get_parent())
	var title := Label.new()
	title.text = "SGTest 登入"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(title)
	username_edit = LineEdit.new()
	username_edit.placeholder_text = "帳號（3-24 英數字）"
	username_edit.max_length = 24
	box.add_child(username_edit)
	password_edit = LineEdit.new()
	password_edit.placeholder_text = "密碼（8-72 字元）"
	password_edit.secret = true
	password_edit.max_length = 72
	box.add_child(password_edit)
	var buttons := HBoxContainer.new()
	var login := Button.new()
	login.text = "登入"
	login.pressed.connect(func() -> void:
		login_requested.emit(username_edit.text.strip_edges(),password_edit.text))
	buttons.add_child(login)
	var register := Button.new()
	register.text = "建立帳號"
	register.pressed.connect(func() -> void:
		register_requested.emit(username_edit.text.strip_edges(),password_edit.text))
	buttons.add_child(register)
	box.add_child(buttons)
	login_status = Label.new()
	login_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(login_status)

func _build_characters() -> void:
	character_center = _full_center()
	var box := _panel(430)
	character_center.add_child(box.get_parent())
	var title := Label.new()
	title.text = "角色選擇"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(title)
	character_list = ItemList.new()
	character_list.custom_minimum_size = Vector2(400,160)
	box.add_child(character_list)
	var create_row := HBoxContainer.new()
	character_name_edit = LineEdit.new()
	character_name_edit.placeholder_text = "新角色名稱"
	character_name_edit.max_length = 16
	character_name_edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	create_row.add_child(character_name_edit)
	var create := Button.new()
	create.text = "建立"
	create.pressed.connect(func() -> void:
		create_character_requested.emit(character_name_edit.text.strip_edges()))
	create_row.add_child(create)
	box.add_child(create_row)
	var actions := HBoxContainer.new()
	var enter := Button.new()
	enter.text = "進入遊戲"
	enter.pressed.connect(func() -> void:
		var selected := character_list.get_selected_items()
		if not selected.is_empty():
			select_character_requested.emit(int(character_list.get_item_metadata(selected[0]))))
	actions.add_child(enter)
	var logout := Button.new()
	logout.text = "登出帳號"
	logout.pressed.connect(func() -> void: logout_requested.emit())
	actions.add_child(logout)
	box.add_child(actions)
	character_status = Label.new()
	character_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(character_status)
	character_center.visible = false

func show_login(message: String = "") -> void:
	login_center.visible = true
	character_center.visible = false
	login_status.text = message
	password_edit.clear()

func show_characters(rows: Array,message: String = "") -> void:
	login_center.visible = false
	character_center.visible = true
	character_list.clear()
	for row in rows:
		if row is Array and row.size() >= 2:
			var index := character_list.add_item(str(row[1]))
			character_list.set_item_metadata(index,int(row[0]))
	if character_list.item_count > 0:
		character_list.select(0)
	character_status.text = message
	character_name_edit.clear()

func set_login_status(message: String) -> void:
	login_status.text = message

func set_character_status(message: String) -> void:
	character_status.text = message

func hide_all() -> void:
	login_center.visible = false
	character_center.visible = false
