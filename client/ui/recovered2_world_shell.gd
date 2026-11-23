extends Control
## Recovered2 world-HUD layout. Pixel positions follow the local game_hud.tscn
## 1400x900 reference; the content scales as one layer on smaller screens.
## Pure presentation + user intent. Server authority stays in ClientSession.
signal request_legacy_section(section: int)
signal skin_requested(id: String)

const LocalInventory = preload("res://ui/recovered2_inventory_window.gd")
const AuxWindow = preload("res://ui/recovered2_aux_window.gd")
const UiTextures = preload("res://ui/ui_texture_loader.gd")
const UiSkins = preload("res://ui/ui_skin_catalog.gd")
const PRIVATE := "res://ui/private_recovered2"
const SOURCE := Vector2(1400,900)
const ACTIONS := ["角色","物品","技能","組隊","社團","商店","任務","商城","設定"]
var stage := Control.new()
var menu := Control.new()
var mini_map := Control.new()
var status_hud := Control.new()
var shortcut := Control.new()
var chat_hud := PanelContainer.new()
var header_actions := Control.new()
var _stats: Dictionary = {}
var _panel
var inventory := LocalInventory.new()
var character := PanelContainer.new()
var popup := PanelContainer.new()
var skill_window := AuxWindow.new()
var party_window := AuxWindow.new()
var clan_window := AuxWindow.new()
var shop_window := AuxWindow.new()
var quest_window := AuxWindow.new()
var mall_window := AuxWindow.new()
var settings_window := AuxWindow.new()
var _aux_windows: Array = []
var _popup_body := Label.new()
var _character_fields: Dictionary = {}
var _map_label: Label
var _coord_label: Label
var _hp := ProgressBar.new()
var _mp := ProgressBar.new()
var _stamina := ProgressBar.new()
var _hp_text: Label
var _mp_text: Label
var _stamina_text: Label
var _money: Label
var _level: Label
var _chat_input := LineEdit.new()
var _chat_log := TextEdit.new()
var _popup_title: Label
var _popup_layout: VBoxContainer
var _skin_selection := OptionButton.new()
var _is_bound := false
var _profile := "recovered2_local"
var _design_size := SOURCE
var _inventory_default := Vector2(1082,130)
var _status_default := Vector2(192,135)

func _init() -> void:
	name = "Recovered2WorldShell"
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	visible = false
	stage.name = "Recovered2SourceStage"
	stage.size = SOURCE
	stage.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(stage)
	_build_minimap()
	_build_menu()
	_build_header_actions()
	_build_status()
	_build_shortcuts()
	_build_chat()
	_build_windows()
	resize_view(Vector2(1152,620))

func _picture(parent: Control, name: String, path: String, position: Vector2, dimensions: Vector2) -> TextureRect:
	var img := TextureRect.new()
	img.name = name
	img.position = position
	img.size = dimensions
	img.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	img.stretch_mode = TextureRect.STRETCH_SCALE
	img.mouse_filter = Control.MOUSE_FILTER_IGNORE
	img.texture = UiTextures.get_texture(PRIVATE.path_join(path))
	parent.add_child(img)
	return img

func set_profile(id: String) -> void:
	if id not in ["recovered2_local","original_ie_hk2"] or _profile == id:
		return
	_profile = id
	var classic := id == "original_ie_hk2"
	_design_size = Vector2(800,600) if classic else SOURCE
	stage.size = _design_size
	var root := "res://ui/private_ie_hk2" if classic else PRIVATE
	var pictures := [
		[mini_map.get_node("MiniMapBackground"),"MINIMAP_L.png" if classic else "MINIMAP.png",Vector2(133,122) if classic else Vector2(180,166)],
		[menu.get_node("OriginalCommandIcons"),"COMMAND.png",Vector2(30,249) if classic else Vector2(50,439)],
		[status_hud.get_node("OriginalStatusFrame"),"HPBAR.png",Vector2(128,90) if classic else Vector2(192,135)],
		[shortcut.get_node("OriginalShortcutFrame"),"HOTKEY_SY.png" if classic else "BH_HOTKEY.png",Vector2(30,196) if classic else Vector2(192,135)],
	]
	for item in pictures:
		var picture: TextureRect = item[0]
		picture.texture = UiTextures.get_texture(root.path_join(str(item[1])))
		picture.size = item[2]
	mini_map.size = Vector2(133,122) if classic else Vector2(180,166)
	menu.size = Vector2(30,249) if classic else Vector2(50,439)
	status_hud.size = Vector2(128,90) if classic else Vector2(192,135)
	shortcut.size = Vector2(30,196) if classic else Vector2(192,135)
	for i in ACTIONS.size():
		var button: Button = menu.get_node("Menu%d" % i)
		button.position = Vector2(2,2+i*27.0) if classic else Vector2(5,[9,55,100,145,194,240,290,338,389][i])
		button.size = Vector2(26,26) if classic else Vector2(42,42)
	var shortcut_grid: Control = shortcut.get_node("ShortcutGrid")
	shortcut_grid.visible = not classic # Classic source uses its own different layout
	for bar in [_hp,_mp,_stamina]:
		bar.size = Vector2(85,10) if classic else Vector2(140,16)
		bar.position.x = 34 if classic else 35
	for i in 3:
		([_hp,_mp,_stamina][i] as ProgressBar).position.y = float(([19,34,49][i]) if classic else ([29,51,72][i]))
	_hp_text.visible = not classic
	_mp_text.visible = not classic
	_stamina_text.visible = not classic
	_money.position = Vector2(29,68) if classic else Vector2(35,95)
	_level.position = Vector2(2,0) if classic else Vector2(9,4)
	var inv_style := StyleBoxTexture.new()
	inv_style.texture = UiTextures.get_texture(root.path_join("BG_ITEM.png")) if classic else null
	if classic and inv_style.texture != null:
		inv_style.content_margin_left = 12
		inv_style.content_margin_right = 8
		inv_style.content_margin_top = 23
		inv_style.content_margin_bottom = 8
		inventory.add_theme_stylebox_override("panel",inv_style)
	else:
		inventory.remove_theme_stylebox_override("panel")
	var char_style := StyleBoxTexture.new()
	char_style.texture = UiTextures.get_texture(root.path_join("BG_CHARACTER_INFO.png")) if classic else null
	if classic and char_style.texture != null:
		char_style.content_margin_left = 12
		char_style.content_margin_right = 8
		char_style.content_margin_top = 21
		char_style.content_margin_bottom = 8
		character.add_theme_stylebox_override("panel",char_style)
	else:
		character.remove_theme_stylebox_override("panel")
	resize_view(size)


func _build_minimap() -> void:
	mini_map.name = "MiniMapPanel"
	mini_map.size = Vector2(180,166)
	mini_map.mouse_filter = Control.MOUSE_FILTER_IGNORE
	stage.add_child(mini_map)
	_picture(mini_map,"MiniMapBackground","MINIMAP.png",Vector2.ZERO,Vector2(180,166))
	_coord_label = _label(mini_map,"X : —　Y : —",Vector2(5,1),Vector2(170,22),12)
	_map_label = _label(mini_map,"Map —",Vector2(8,28),Vector2(164,23),13)
	# Do not invent a working world-map bitmap or fake unexplored minimap:
	# navigation and the map viewport remain separate presentation owners.

func _build_menu() -> void:
	menu.name = "CommandMenu"
	menu.size = Vector2(50,439)
	menu.mouse_filter = Control.MOUSE_FILTER_IGNORE
	stage.add_child(menu)
	_picture(menu,"OriginalCommandIcons","COMMAND.png",Vector2.ZERO,Vector2(50,439))
	var offsets := [9,55,100,145,194,240,290,338,389]
	for i in ACTIONS.size():
		var button := Button.new()
		button.name = "Menu%d" % i
		button.position = Vector2(5,offsets[i])
		button.size = Vector2(42,42)
		button.text = ""
		button.flat = true
		button.focus_mode = Control.FOCUS_NONE
		button.tooltip_text = ACTIONS[i]
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		var blank := StyleBoxEmpty.new()
		button.add_theme_stylebox_override("normal",blank)
		button.add_theme_stylebox_override("focus",blank)
		var glow := StyleBoxFlat.new()
		glow.bg_color = Color(0.34,0.81,1.0,0.19)
		glow.border_color = Color(0.36,0.92,1.0,0.65)
		glow.set_border_width_all(1)
		glow.set_corner_radius_all(7)
		button.add_theme_stylebox_override("hover",glow)
		button.add_theme_stylebox_override("pressed",glow)
		button.pressed.connect(_menu_action.bind(i))
		menu.add_child(button)

func _build_header_actions() -> void:
	header_actions.name = "HeaderActions"
	header_actions.size = Vector2(120,59)
	header_actions.mouse_filter = Control.MOUSE_FILTER_IGNORE
	stage.add_child(header_actions)
	for entry in [
		{"name":"CraftDismantle","icon":"craft_dismantle_icon.png","tip":"合成／分解","x":0.0,"title":"合成／分解"},
		{"name":"GameInfo","icon":"game_info_icon.png","tip":"遊戲資訊","x":60.0,"title":"遊戲資訊"},
	]:
		var button := TextureButton.new()
		button.name = str(entry.name)
		button.position = Vector2(float(entry.x),0)
		button.size = Vector2(60,59)
		button.texture_normal = UiTextures.get_texture(PRIVATE.path_join(str(entry.icon)))
		button.ignore_texture_size = true
		button.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
		button.focus_mode = Control.FOCUS_NONE
		button.tooltip_text = str(entry.tip)
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		button.pressed.connect(func(): _popup(str(entry.title),"此介面已對齊 Recovered2；功能會沿用 SGTest 現有伺服器能力，不顯示虛構資料。"))
		header_actions.add_child(button)

func _build_status() -> void:
	status_hud.name = "StatusPanel"
	status_hud.size = Vector2(192,135)
	status_hud.mouse_filter = Control.MOUSE_FILTER_IGNORE
	stage.add_child(status_hud)
	_picture(status_hud,"OriginalStatusFrame","HPBAR.png",Vector2.ZERO,Vector2(192,135))
	_level = _label(status_hud,"Lv.1",Vector2(9,4),Vector2(90,19),13)
	for pair in [[_hp,29,Color("#e03d4d")],[_mp,51,Color("#4789eb")],[_stamina,72,Color("#38cd80")]]:
		var bar: ProgressBar = pair[0]
		bar.position = Vector2(35,float(pair[1]))
		bar.size = Vector2(140,16 if int(pair[1]) != 72 else 17)
		bar.show_percentage = false
		bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
		bar.max_value = 100
		var track := StyleBoxFlat.new()
		track.bg_color = Color(0.06,0.12,0.17,0.8)
		var fill := StyleBoxFlat.new()
		fill.bg_color = pair[2]
		fill.set_corner_radius_all(2)
		bar.add_theme_stylebox_override("background",track)
		bar.add_theme_stylebox_override("fill",fill)
		status_hud.add_child(bar)
	_hp_text = _label(status_hud,"— / —",Vector2(34,28),Vector2(141,18),12)
	_mp_text = _label(status_hud,"— / —",Vector2(34,50),Vector2(141,18),12)
	_stamina_text = _label(status_hud,"— / —",Vector2(35,72),Vector2(140,18),12)
	_money = _label(status_hud,"—",Vector2(35,95),Vector2(140,18),12)

func _build_shortcuts() -> void:
	shortcut.name = "ShortcutBar"
	shortcut.size = Vector2(192,135)
	shortcut.mouse_filter = Control.MOUSE_FILTER_IGNORE
	stage.add_child(shortcut)
	_picture(shortcut,"OriginalShortcutFrame","BH_HOTKEY.png",Vector2.ZERO,Vector2(192,135))
	var shortcuts := GridContainer.new()
	shortcuts.name = "ShortcutGrid"
	shortcuts.columns = 4
	shortcuts.position = Vector2(11,12)
	shortcuts.size = Vector2(169,112)
	shortcuts.mouse_filter = Control.MOUSE_FILTER_IGNORE
	shortcuts.add_theme_constant_override("h_separation",8)
	shortcuts.add_theme_constant_override("v_separation",2)
	shortcut.add_child(shortcuts)
	for i in 12:
		var cell := PanelContainer.new()
		cell.name = "Shortcut%d" % i
		cell.custom_minimum_size = Vector2(36,34)
		cell.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var dark := StyleBoxFlat.new()
		dark.bg_color = Color(0.015,0.04,0.075,0.38)
		dark.border_color = Color(0.43,0.66,0.8,0.25)
		dark.set_border_width_all(1)
		cell.add_theme_stylebox_override("panel",dark)
		shortcuts.add_child(cell)
		var key := _label(cell,"F%d" % (i+1),Vector2.ZERO,Vector2(35,33),9)
		key.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# Shortcuts are intentionally not wired to fabricated skill IDs.

func _build_chat() -> void:
	chat_hud.name = "ChatPanel"
	chat_hud.mouse_filter = Control.MOUSE_FILTER_STOP
	stage.add_child(chat_hud)
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.018,0.046,0.082,0.78)
	style.border_color = Color(0.30,0.76,0.89,0.45)
	style.set_border_width_all(1)
	style.set_corner_radius_all(3)
	chat_hud.add_theme_stylebox_override("panel",style)
	var layout := Control.new()
	layout.name = "Recovered2ChatLayout"
	layout.mouse_filter = Control.MOUSE_FILTER_IGNORE
	chat_hud.add_child(layout)
	for entry in [
		["附近",8.0],["陣營",75.0],["戰團",142.0],["隊伍",209.0],["系統",276.0],
	]:
		var filter := Button.new()
		filter.text = "✓ " + str(entry[0])
		filter.position = Vector2(float(entry[1]),4)
		filter.size = Vector2(65,23)
		filter.toggle_mode = true
		filter.button_pressed = true
		filter.focus_mode = Control.FOCUS_NONE
		filter.add_theme_font_size_override("font_size",12)
		layout.add_child(filter)
	var system_message := Label.new()
	system_message.name = "SystemMessage"
	system_message.position = Vector2(350,4)
	system_message.size = Vector2(308,23)
	system_message.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	system_message.add_theme_font_size_override("font_size",13)
	layout.add_child(system_message)
	_chat_log.name = "MessageHistory"
	_chat_log.position = Vector2(10,31)
	_chat_log.size = Vector2(680,68)
	_chat_log.editable = false
	_chat_log.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_chat_log.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	_chat_log.add_theme_font_size_override("font_size",13)
	_chat_log.add_theme_stylebox_override("normal",StyleBoxEmpty.new())
	layout.add_child(_chat_log)
	_chat_input.name = "ChatInput"
	_chat_input.position = Vector2(43,101)
	_chat_input.size = Vector2(569,29)
	_chat_input.placeholder_text = "開始聊天！"
	_chat_input.max_length = 100
	_chat_input.add_theme_font_size_override("font_size",13)
	_chat_input.text_submitted.connect(func(_ignored): _submit_chat())
	layout.add_child(_chat_input)
	_chat_icon_button(layout,"ChannelButton","ICON_CHANNEL1.png",Vector2(8,101),Vector2(29,29))
	_chat_icon_button(layout,"EmotionButton","B_EMOTION.spr.0000.png",Vector2(618,101),Vector2(30,30))
	_chat_icon_button(layout,"BubbleButton","ICON_BUBBLE1.png",Vector2(654,101),Vector2(30,30))
	_chat_icon_button(layout,"LayoutToggle","ICON_CHAT_EXPAND.png",Vector2(665,3),Vector2(29,29))

func _chat_icon_button(parent: Control, node_name: String, file_name: String, at: Vector2, dimensions: Vector2) -> TextureButton:
	var button := TextureButton.new()
	button.name = node_name
	button.position = at
	button.size = dimensions
	button.texture_normal = UiTextures.get_texture(PRIVATE.path_join(file_name))
	button.ignore_texture_size = true
	button.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	button.focus_mode = Control.FOCUS_NONE
	button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	parent.add_child(button)
	return button

func _build_windows() -> void:
	var skin = UiTextures.get_texture(PRIVATE.path_join("CLOSESCREEN.png"))
	var theme_resource = load(PRIVATE.path_join("paradise_window_theme.tres")) if FileAccess.file_exists(PRIVATE.path_join("paradise_window_theme.tres")) else null
	if theme_resource is Theme:
		stage.theme = theme_resource
	# Persistent windows are allocated once. Showing/hiding never reloads map,
	# actor, item photo, or theme resources.
	stage.add_child(inventory)
	inventory.position = Vector2(1082,130)
	inventory.size = Vector2(300,520)

	character.name = "CharacterWindow"
	character.theme_type_variation = &"ParadiseWindowFrame"
	character.position = Vector2(420,118)
	character.size = Vector2(560,610)
	character.mouse_filter = Control.MOUSE_FILTER_STOP
	character.visible = false
	stage.add_child(character)
	_build_character_source_layout(character,skin)

	popup.name = "InformationWindow"
	popup.theme_type_variation = &"ParadiseWindowFrame"
	popup.position = Vector2(480,225)
	popup.size = Vector2(440,285)
	popup.mouse_filter = Control.MOUSE_FILTER_STOP
	popup.visible = false
	stage.add_child(popup)
	var vertical := VBoxContainer.new()
	vertical.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	popup.add_child(vertical)
	var heading := HBoxContainer.new()
	vertical.add_child(heading)
	_popup_title = Label.new()
	_popup_title.text = "提示"
	_popup_title.theme_type_variation = &"ParadiseWindowTitle"
	_popup_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	heading.add_child(_popup_title)
	var close := TextureButton.new()
	close.name = "CloseWindow"
	close.custom_minimum_size = Vector2(22,22)
	close.ignore_texture_size = true
	close.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	close.texture_normal = skin
	close.pressed.connect(popup.hide)
	heading.add_child(close)
	vertical.add_child(HSeparator.new())
	_popup_body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_popup_body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_popup_body.add_theme_font_size_override("font_size",15)
	vertical.add_child(_popup_body)
	_popup_layout = vertical

	_skin_selection.name = "UiProfilePicker"
	_skin_selection.visible = false
	_skin_selection.item_selected.connect(_skin_picked)
	_popup_layout.add_child(_skin_selection)
	_build_aux_windows(skin)

func _build_aux_windows(close_texture: Texture2D) -> void:
	_aux_windows = [skill_window,party_window,clan_window,shop_window,quest_window,mall_window,settings_window]
	var specs := [
		[skill_window,"SkillWindow","技能",Vector2(642,440)],
		[party_window,"PartyWindow","隊伍",Vector2(760,620)],
		[clan_window,"ClanWindow","戰團",Vector2(900,680)],
		[shop_window,"ShopWindow","商店",Vector2(720,560)],
		[quest_window,"QuestWindow","任務",Vector2(900,600)],
		[mall_window,"MallWindow","商城",Vector2(800,600)],
		[settings_window,"SettingsWindow","系統設定",Vector2(540,440)],
	]
	for spec in specs:
		var window = spec[0]
		window.configure(str(spec[1]),str(spec[2]),spec[3],close_texture)
		stage.add_child(window)
	_build_skill_aux()
	_build_party_aux()
	_build_clan_aux()
	_build_shop_aux()
	_build_quest_aux()
	_build_mall_aux()
	_build_settings_aux()

func _build_skill_aux() -> void:
	var tabs := HBoxContainer.new()
	tabs.name = "CategoryTabs"
	for text in ["全部","攻擊","治療／復活","狀態","特殊"]:
		var button := Button.new()
		button.text = text
		button.toggle_mode = true
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.custom_minimum_size.y = 38
		tabs.add_child(button)
	(skill_window.body as VBoxContainer).add_child(tabs)
	var panel = skill_window.section("")
	var empty := Label.new()
	empty.name = "SkillSummary"
	empty.text = "目前沒有伺服器提供的已學技能清單"
	empty.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	empty.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	empty.size_flags_vertical = Control.SIZE_EXPAND_FILL
	empty.add_theme_font_size_override("font_size",18)
	panel.add_child(empty)
	var controls := HBoxContainer.new()
	controls.name = "SkillActions"
	var skill_id := SpinBox.new()
	skill_id.name = "SkillIdInput"
	skill_id.min_value = 1
	skill_id.max_value = 100000000
	skill_id.value = 99001
	skill_id.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	controls.add_child(skill_id)
	var target := CheckButton.new()
	target.name = "SkillTargetMode"
	target.text = "點怪單體"
	controls.add_child(target)
	var cast := Button.new()
	cast.text = "自身中心範圍施放"
	cast.pressed.connect(func():
		if _panel == null:
			return
		_panel.skill_id_input.value = skill_id.value
		_panel.skill_target_mode.button_pressed = target.button_pressed
		_panel._cast_area_skill()
	)
	controls.add_child(cast)
	(skill_window.body as VBoxContainer).add_child(controls)

func _build_party_aux() -> void:
	party_window.meta_label.text = "成員 0 / 6"
	var panel = party_window.section("")
	var members := Label.new()
	members.name = "PartyMembersSummary"
	members.text = "尚未加入隊伍\n\n建立隊伍或接受其他玩家的邀請後，\n隊員狀態會顯示在這裡。"
	members.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	members.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	members.size_flags_vertical = Control.SIZE_EXPAND_FILL
	members.add_theme_font_size_override("font_size",17)
	panel.add_child(members)
	var invite_row := HBoxContainer.new()
	var target := LineEdit.new()
	target.name = "PartyInviteTarget"
	target.placeholder_text = "角色名或 ActorId"
	target.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	invite_row.add_child(target)
	var invite := Button.new()
	invite.name = "PartyInviteButton"
	invite.text = "邀請加入隊伍"
	invite.pressed.connect(func():
		if _panel == null:
			return
		_panel.party_panel.target_input.text = target.text
		_panel.party_panel._party_invite_target()
	)
	invite_row.add_child(invite)
	party_window.body.add_child(invite_row)
	var actions := HBoxContainer.new()
	for entry in [["接受邀請","_party_accept_invite","PartyAcceptButton"],["拒絕邀請","_party_reject_invite","PartyRejectButton"],["離開隊伍","_party_leave","PartyLeaveButton"]]:
		var button := Button.new()
		button.name = str(entry[2])
		button.text = str(entry[0])
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.pressed.connect(func():
			if _panel != null:
				_panel.party_panel.call(str(entry[1]))
		)
		actions.add_child(button)
	party_window.body.add_child(actions)

func _build_clan_aux() -> void:
	# Recovered2 clan window is a wide member-management surface. The client
	# never mutates membership locally; controls submit authenticated intents
	# and the member list is rebuilt only from ClanSnapshot.
	var row := HBoxContainer.new()
	row.name = "ClanSourceLayout"
	row.add_theme_constant_override("separation",14)
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.size_flags_vertical = Control.SIZE_EXPAND_FILL
	clan_window.body.add_child(row)

	var overview := PanelContainer.new()
	overview.name = "ClanOverviewPanel"
	overview.custom_minimum_size = Vector2(320,570)
	overview.theme_type_variation = &"ParadiseContentPanel"
	overview.size_flags_vertical = Control.SIZE_EXPAND_FILL
	row.add_child(overview)
	var left := VBoxContainer.new()
	left.add_theme_constant_override("separation",10)
	overview.add_child(left)

	var identity_title := Label.new()
	identity_title.text = "戰團資訊"
	identity_title.theme_type_variation = &"ParadiseSectionTitle"
	left.add_child(identity_title)
	var identity := Label.new()
	identity.name = "ClanIdentitySummary"
	identity.text = "尚未加入戰團"
	identity.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	identity.custom_minimum_size.y = 90
	left.add_child(identity)

	var create_section := VBoxContainer.new()
	create_section.name = "ClanCreateSection"
	left.add_child(create_section)
	var create_title := Label.new()
	create_title.text = "建立戰團"
	create_title.theme_type_variation = &"ParadiseSecondaryText"
	create_section.add_child(create_title)
	var create_name := LineEdit.new()
	create_name.name = "ClanCreateName"
	create_name.placeholder_text = "戰團名稱（2–24 字元）"
	create_name.max_length = 24
	create_section.add_child(create_name)
	var create := Button.new()
	create.name = "ClanCreateButton"
	create.text = "建立戰團"
	create.theme_type_variation = &"ParadisePrimaryButton"
	create.pressed.connect(func():
		if _panel != null:
			_panel.submit_clan_create(create_name.text)
	)
	create_section.add_child(create)

	var join_section := VBoxContainer.new()
	join_section.name = "ClanJoinSection"
	left.add_child(join_section)
	var join_title := Label.new()
	join_title.text = "加入戰團"
	join_title.theme_type_variation = &"ParadiseSecondaryText"
	join_section.add_child(join_title)
	var join_id := SpinBox.new()
	join_id.name = "ClanJoinId"
	join_id.min_value = 1
	join_id.max_value = 2147483647
	join_id.value = 1
	join_section.add_child(join_id)
	var join := Button.new()
	join.name = "ClanJoinButton"
	join.text = "依戰團 ID 加入"
	join.theme_type_variation = &"ParadisePrimaryButton"
	join.pressed.connect(func():
		if _panel != null:
			_panel.submit_clan_join(int(join_id.value))
	)
	join_section.add_child(join)

	var management := VBoxContainer.new()
	management.name = "ClanManagementSection"
	management.add_theme_constant_override("separation",6)
	left.add_child(management)
	var announcement_title := Label.new()
	announcement_title.text = "團長公告"
	announcement_title.theme_type_variation = &"ParadiseSecondaryText"
	management.add_child(announcement_title)
	var announcement_edit := LineEdit.new()
	announcement_edit.name = "ClanAnnouncementEdit"
	announcement_edit.placeholder_text = "輸入戰團公告"
	announcement_edit.max_length = 1024
	management.add_child(announcement_edit)
	var announcement_update := Button.new()
	announcement_update.name = "ClanAnnouncementUpdateButton"
	announcement_update.text = "更新公告"
	announcement_update.theme_type_variation = &"ParadisePrimaryButton"
	announcement_update.pressed.connect(func():
		if _panel != null:
			_panel.submit_clan_set_announcement(announcement_edit.text)
	)
	management.add_child(announcement_update)

	var spacer := Control.new()
	spacer.size_flags_vertical = Control.SIZE_EXPAND_FILL
	left.add_child(spacer)
	var actions := HBoxContainer.new()
	actions.add_theme_constant_override("separation",7)
	left.add_child(actions)
	var refresh := Button.new()
	refresh.name = "ClanRefreshButton"
	refresh.text = "更新"
	refresh.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	refresh.pressed.connect(func():
		if _panel != null:
			_panel.submit_clan_refresh()
	)
	actions.add_child(refresh)
	var leave := Button.new()
	leave.name = "ClanLeaveButton"
	leave.text = "離開戰團"
	leave.theme_type_variation = &"ParadiseDangerButton"
	leave.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	leave.pressed.connect(func():
		if _panel != null:
			_panel.submit_clan_leave()
	)
	actions.add_child(leave)

	var members_panel := PanelContainer.new()
	members_panel.name = "ClanMembersPanel"
	members_panel.custom_minimum_size = Vector2(500,570)
	members_panel.theme_type_variation = &"ParadiseContentPanel"
	members_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	members_panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	row.add_child(members_panel)
	var members_layout := VBoxContainer.new()
	members_layout.add_theme_constant_override("separation",8)
	members_panel.add_child(members_layout)
	var members_title := Label.new()
	members_title.text = "成員"
	members_title.theme_type_variation = &"ParadiseWindowTitle"
	members_layout.add_child(members_title)
	var members_meta := Label.new()
	members_meta.name = "ClanMembersMeta"
	members_meta.text = "0 / 100"
	members_meta.theme_type_variation = &"ParadiseSecondaryText"
	members_layout.add_child(members_meta)
	var member_scroll := ScrollContainer.new()
	member_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	member_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	members_layout.add_child(member_scroll)
	var member_rows := VBoxContainer.new()
	member_rows.name = "ClanMemberRows"
	member_rows.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	member_rows.add_theme_constant_override("separation",5)
	member_scroll.add_child(member_rows)
	var status := Label.new()
	status.name = "ClanOperationStatus"
	status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	status.theme_type_variation = &"ParadiseSecondaryText"
	members_layout.add_child(status)

func _build_shop_aux() -> void:
	# npc_shop_window.tscn uses a 360x520 shop panel beside a shared inventory.
	# SGTest mirrors that two-pane hierarchy without duplicating inventory state.
	var row := HBoxContainer.new()
	row.name = "NpcShopSourceRow"
	row.add_theme_constant_override("separation",16)
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.size_flags_vertical = Control.SIZE_EXPAND_FILL
	shop_window.body.add_child(row)

	var shop_panel := PanelContainer.new()
	shop_panel.name = "NpcShopPanel"
	shop_panel.custom_minimum_size = Vector2(360,470)
	shop_panel.theme_type_variation = &"ParadiseContentPanel"
	shop_panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	row.add_child(shop_panel)
	var shop_layout := VBoxContainer.new()
	shop_layout.add_theme_constant_override("separation",10)
	shop_panel.add_child(shop_layout)

	var mode := Label.new()
	mode.text = "購買商品"
	mode.theme_type_variation = &"ParadiseModeBadge"
	mode.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	shop_layout.add_child(mode)

	var summary_frame := PanelContainer.new()
	summary_frame.theme_type_variation = &"ParadiseSectionPanel"
	shop_layout.add_child(summary_frame)
	var summary_layout := VBoxContainer.new()
	summary_frame.add_child(summary_layout)
	var caption := Label.new()
	caption.text = "商店資訊"
	caption.theme_type_variation = &"ParadiseSecondaryText"
	summary_layout.add_child(caption)
	var status := Label.new()
	status.name = "ShopSummary"
	status.text = "尚未開啟 NPC 商店"
	status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	status.add_theme_font_size_override("font_size",15)
	summary_layout.add_child(status)

	var npc_row := HBoxContainer.new()
	shop_layout.add_child(npc_row)
	var npc := SpinBox.new()
	npc.name = "ShopNpcInput"
	npc.min_value = 1
	npc.max_value = 2147483647
	npc.value = 1
	npc.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	npc_row.add_child(npc)
	var open := Button.new()
	open.name = "ShopOpenButton"
	open.text = "開啟 NPC 商店"
	open.pressed.connect(func():
		if _panel == null:
			return
		_panel.npc_shop_panel.shop_npc_input.value = npc.value
		_panel.npc_shop_panel._open_shop_from_input()
	)
	npc_row.add_child(open)

	var product_heading := Label.new()
	product_heading.text = "販售商品"
	product_heading.theme_type_variation = &"ParadiseBodyText"
	shop_layout.add_child(product_heading)
	var buy_picker := OptionButton.new()
	buy_picker.name = "ShopBuyPicker"
	buy_picker.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	shop_layout.add_child(buy_picker)
	var buy_row := HBoxContainer.new()
	shop_layout.add_child(buy_row)
	var buy_qty := SpinBox.new()
	buy_qty.name = "ShopBuyQuantity"
	buy_qty.min_value = 1
	buy_qty.max_value = 1000000
	buy_qty.value = 1
	buy_qty.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	buy_row.add_child(buy_qty)
	var buy := Button.new()
	buy.name = "ShopBuyButton"
	buy.text = "購買"
	buy.theme_type_variation = &"ParadisePrimaryButton"
	buy.pressed.connect(func():
		if _panel == null or buy_picker.selected < 0:
			return
		_panel.submit_shop_buy(int(buy_picker.get_item_metadata(buy_picker.selected)),int(buy_qty.value))
	)
	buy_row.add_child(buy)
	var buy_spacer := Control.new()
	buy_spacer.size_flags_vertical = Control.SIZE_EXPAND_FILL
	shop_layout.add_child(buy_spacer)

	var inventory_panel := PanelContainer.new()
	inventory_panel.name = "ShopInventoryPanel"
	inventory_panel.custom_minimum_size = Vector2(300,470)
	inventory_panel.theme_type_variation = &"ParadiseContentPanel"
	inventory_panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	row.add_child(inventory_panel)
	var inventory_layout := VBoxContainer.new()
	inventory_layout.add_theme_constant_override("separation",10)
	inventory_panel.add_child(inventory_layout)
	var inventory_title := Label.new()
	inventory_title.text = "背包出售"
	inventory_title.theme_type_variation = &"ParadiseWindowTitle"
	inventory_layout.add_child(inventory_title)
	var inventory_hint := Label.new()
	inventory_hint.text = "只列出目前 NPC 可收購的物品"
	inventory_hint.theme_type_variation = &"ParadiseSecondaryText"
	inventory_hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	inventory_layout.add_child(inventory_hint)
	var sell_picker := OptionButton.new()
	sell_picker.name = "ShopSellPicker"
	sell_picker.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	inventory_layout.add_child(sell_picker)
	var sell_qty := SpinBox.new()
	sell_qty.name = "ShopSellQuantity"
	sell_qty.min_value = 1
	sell_qty.max_value = 1000000
	sell_qty.value = 1
	inventory_layout.add_child(sell_qty)
	var sell := Button.new()
	sell.name = "ShopSellButton"
	sell.text = "販售"
	sell.theme_type_variation = &"ParadisePrimaryButton"
	sell.pressed.connect(func():
		if _panel == null or sell_picker.selected < 0:
			return
		_panel.submit_shop_sell(int(sell_picker.get_item_metadata(sell_picker.selected)),int(sell_qty.value))
	)
	inventory_layout.add_child(sell)
	var sell_spacer := Control.new()
	sell_spacer.size_flags_vertical = Control.SIZE_EXPAND_FILL
	inventory_layout.add_child(sell_spacer)

func _quest_panel(parent: Control,node_name: String,rect: Rect2,title: String) -> Control:
	var frame := PanelContainer.new()
	frame.name = node_name
	frame.position = rect.position
	frame.size = rect.size
	frame.theme_type_variation = &"ParadiseContentPanel"
	parent.add_child(frame)
	var layout := VBoxContainer.new()
	layout.add_theme_constant_override("separation",6)
	frame.add_child(layout)
	var heading := Label.new()
	heading.text = title
	heading.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	heading.theme_type_variation = &"ParadiseSectionTitle"
	layout.add_child(heading)
	return layout

func _build_quest_aux() -> void:
	# Recovered2 quest_window.tscn is 900x600: three state columns above,
	# flow + rewards below. AuxWindow owns only the common chrome.
	var source := Control.new()
	source.name = "QuestSourceLayout"
	source.custom_minimum_size = Vector2(860,510)
	source.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	source.size_flags_vertical = Control.SIZE_EXPAND_FILL
	quest_window.body.add_child(source)

	var available := _quest_panel(source,"AvailablePanel",Rect2(4,0,266,164),"待領取")
	var available_list := Label.new()
	available_list.name = "QuestAvailableList"
	available_list.text = "—"
	available_list.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	available_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	available.add_child(available_list)

	var progress := _quest_panel(source,"InProgressPanel",Rect2(294,0,266,164),"進行中")
	var progress_list := Label.new()
	progress_list.name = "QuestInProgressList"
	progress_list.text = "—"
	progress_list.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	progress_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	progress.add_child(progress_list)

	var completed := _quest_panel(source,"CompletedPanel",Rect2(584,0,266,164),"已完成")
	var completed_list := Label.new()
	completed_list.name = "QuestCompletedList"
	completed_list.text = "—"
	completed_list.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	completed_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	completed.add_child(completed_list)

	var steps := _quest_panel(source,"StepsPanel",Rect2(4,176,396,326),"任務流程")
	var summary := Label.new()
	summary.name = "QuestSummary"
	summary.text = "目前沒有伺服器任務檢視"
	summary.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	summary.size_flags_vertical = Control.SIZE_EXPAND_FILL
	steps.add_child(summary)
	var npc_row := HBoxContainer.new()
	steps.add_child(npc_row)
	var npc := SpinBox.new()
	npc.name = "QuestNpcInput"
	npc.min_value = 1
	npc.max_value = 2147483647
	npc.value = 1
	npc.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	npc_row.add_child(npc)
	var open := Button.new()
	open.name = "QuestOpenButton"
	open.text = "開啟 NPC 任務"
	open.pressed.connect(func():
		if _panel == null:
			return
		_panel.quest_panel.npc_input.value = npc.value
		_panel.quest_panel._open_quest_from_input()
	)
	npc_row.add_child(open)

	var rewards := _quest_panel(source,"RewardsPanel",Rect2(412,176,438,326),"任務獎勵／選項")
	var reward_summary := Label.new()
	reward_summary.name = "QuestRewardSummary"
	reward_summary.text = "由伺服器任務資料決定"
	reward_summary.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	reward_summary.size_flags_vertical = Control.SIZE_EXPAND_FILL
	rewards.add_child(reward_summary)
	var choice := OptionButton.new()
	choice.name = "QuestChoicePicker"
	choice.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	rewards.add_child(choice)
	var choose := Button.new()
	choose.name = "QuestChooseButton"
	choose.text = "確認任務選項"
	choose.theme_type_variation = &"ParadisePrimaryButton"
	choose.pressed.connect(func():
		if _panel == null or choice.selected < 0:
			return
		_panel.submit_quest_choice(int(choice.get_item_metadata(choice.selected)))
	)
	rewards.add_child(choose)

func _build_mall_aux() -> void:
	var row := HBoxContainer.new()
	var nav := VBoxContainer.new()
	for text in ["PayPal贊助","贈禮","提領"]:
		var button := Button.new()
		button.text = text
		button.custom_minimum_size = Vector2(150,42)
		nav.add_child(button)
	row.add_child(nav)
	var body := Label.new()
	body.text = "目前沒有已連線的贊助流程。"
	body.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	body.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	body.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	row.add_child(body)
	mall_window.body.add_child(row)

func _build_settings_aux() -> void:
	var general = settings_window.section("一般")
	for entry in [["背景音樂音量",["0%","10%","20%","30%","40%","50%","60%","70%","80%","90%","100%"],5],["音效音量",["0%","10%","20%","30%","40%","50%","60%","70%","80%","90%","100%"],5],["寵物戰鬥模式",["和平模式","被動戰鬥","主動戰鬥"],0],["自動聖靈",["關閉","開啟"],0]]:
		var row := HBoxContainer.new()
		var label := Label.new()
		label.text = str(entry[0])
		label.custom_minimum_size.x = 180
		row.add_child(label)
		var option := OptionButton.new()
		for value in entry[1]:
			option.add_item(str(value))
		option.select(int(entry[2]))
		option.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(option)
		general.add_child(row)
	var skin_row := HBoxContainer.new()
	var skin_label := Label.new()
	skin_label.text = "UI 風格"
	skin_label.custom_minimum_size.x = 180
	skin_row.add_child(skin_label)
	var skin_picker := OptionButton.new()
	skin_picker.name = "UiProfilePickerSettings"
	skin_picker.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	skin_picker.item_selected.connect(func(index): skin_requested.emit(str(skin_picker.get_item_metadata(index))))
	skin_row.add_child(skin_picker)
	general.add_child(skin_row)

func _character_panel(parent: Control,node_name: String,rect: Rect2) -> Panel:
	var panel := Panel.new()
	panel.name = node_name
	panel.position = rect.position
	panel.size = rect.size
	panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	panel.theme_type_variation = &"ParadiseSection"
	parent.add_child(panel)
	return panel

func _character_text(parent: Control,node_name: String,text: String,rect: Rect2,font_size := 14,center := false,accent := false) -> Label:
	var label := Label.new()
	label.name = node_name
	label.text = text
	label.position = rect.position
	label.size = rect.size
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	if center:
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_font_size_override("font_size",font_size)
	label.add_theme_color_override("font_color",Color("#50d4e5") if accent else Color("#8fa9ba"))
	parent.add_child(label)
	return label

func _character_value(parent: Control,key: String,rect: Rect2,accent := false) -> Label:
	var value := _character_text(parent,"Field"+key,"—",rect,15,true,accent)
	value.add_theme_color_override("font_color",Color("#50e9d9") if accent else Color("#e6f2ff"))
	_character_fields[key] = value
	return value

func _build_character_source_layout(window: PanelContainer,close_texture: Texture2D) -> void:
	# Source geometry from Recovered2 game_hud.tscn CharacterWindow (560x610).
	var layout := Control.new()
	layout.name = "SourceCharacterLayout"
	layout.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	window.add_child(layout)

	var title := _character_text(layout,"WindowTitle","角色資訊",Rect2(18,7,200,30),18,false,false)
	title.theme_type_variation = &"ParadiseWindowTitle"
	var close := TextureButton.new()
	close.name = "CloseWindow"
	close.position = Vector2(524,10)
	close.size = Vector2(22,22)
	close.ignore_texture_size = true
	close.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	close.texture_normal = close_texture
	close.pressed.connect(window.hide)
	layout.add_child(close)
	var separator := HSeparator.new()
	separator.position = Vector2(12,43)
	separator.size = Vector2(536,2)
	separator.theme_type_variation = &"ParadiseHeaderSeparator"
	layout.add_child(separator)

	_character_panel(layout,"IdentityPanel",Rect2(14,54,532,72))
	_character_panel(layout,"AttributesPanel",Rect2(14,136,532,86))
	_character_panel(layout,"CombatPanel",Rect2(14,230,532,220))
	_character_panel(layout,"StatusPanel",Rect2(14,458,532,138))

	_character_text(layout,"AttributesTitle","基礎屬性",Rect2(28,142,504,24),13,true,true)
	_character_text(layout,"CombatTitle","戰鬥能力",Rect2(28,237,504,24),13,true,true)
	_character_text(layout,"StatusTitle","角色狀態",Rect2(28,465,504,23),13,true,true)

	_character_text(layout,"NameCaption","名稱",Rect2(28,58,50,20))
	_character_value(layout,"名稱",Rect2(86,58,184,20))
	_character_text(layout,"LevelCaption","等級",Rect2(28,79,50,22))
	_character_value(layout,"等級",Rect2(86,79,98,22))
	_character_text(layout,"CareerCaption","職業",Rect2(28,102,50,20))
	_character_value(layout,"職業",Rect2(86,102,184,20))
	_character_text(layout,"RaceCaption","種族",Rect2(304,102,50,20),14,true)
	_character_value(layout,"種族",Rect2(370,102,156,20))

	for entry in [
		["力量",Rect2(32,171,68,20),Rect2(112,171,66,22)],
		["敏捷",Rect2(300,171,68,20),Rect2(380,171,66,22)],
		["體質",Rect2(32,196,68,20),Rect2(112,196,66,22)],
		["智力",Rect2(300,196,68,20),Rect2(380,196,66,22)]
	]:
		_character_text(layout,"Caption"+str(entry[0]),str(entry[0]),entry[1])
		_character_value(layout,str(entry[0]),entry[2])

	for entry in [
		["攻擊",Rect2(32,266,86,20),Rect2(126,266,84,20)],
		["防禦",Rect2(32,293,86,20),Rect2(126,293,84,20)],
		["命中",Rect2(300,266,96,20),Rect2(404,266,124,20)],
		["迴避",Rect2(300,293,96,20),Rect2(404,293,124,20)],
		["移動速度",Rect2(32,347,86,20),Rect2(126,347,84,20)],
		["攻擊速度",Rect2(300,347,96,20),Rect2(404,347,124,20)]
	]:
		_character_text(layout,"Caption"+str(entry[0]),str(entry[0]),entry[1])
		_character_value(layout,str(entry[0]),entry[2])

	# Recovered2 resource/status rows occupy the lower source section. SGTest
	# currently owns HP/MP, inventory/equipment counts, wallet and map.
	for entry in [
		["HP",Rect2(28,494,32,20),Rect2(64,494,210,20)],
		["MP",Rect2(28,519,32,20),Rect2(64,519,210,20)],
		["背包",Rect2(304,494,70,20),Rect2(390,494,138,20)],
		["裝備",Rect2(304,519,70,20),Rect2(390,519,138,20)],
		["金錢",Rect2(304,544,70,20),Rect2(390,544,138,20)],
		["地圖",Rect2(304,569,70,20),Rect2(390,569,138,20)]
	]:
		_character_text(layout,"Caption"+str(entry[0]),str(entry[0]),entry[1])
		_character_value(layout,str(entry[0]),entry[2],str(entry[0])=="金錢")

func _build_character_sections(container: VBoxContainer) -> void:
	# The source character window uses separate headed panels, not a long log.
	for section in [
		{"heading":"角色資料","fields":["名稱","等級","職業","種族"]},
		{"heading":"基本屬性","fields":["力量","體質","敏捷","智力"]},
		{"heading":"戰鬥能力","fields":["攻擊","防禦","命中","迴避","移動速度","攻擊速度"]},
		{"heading":"角色狀態","fields":["HP","MP","背包","裝備","金錢","地圖"]},
	]:
		var box := PanelContainer.new()
		box.name = "CharacterSection"+str(_character_fields.size())
		box.theme_type_variation = &"ParadiseContentPanel"
		box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		container.add_child(box)
		var content := VBoxContainer.new()
		content.add_theme_constant_override("separation",3)
		box.add_child(content)
		var heading := Label.new()
		heading.text = str(section.heading)
		heading.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		heading.add_theme_color_override("font_color",Color("#50d4e5"))
		heading.add_theme_font_size_override("font_size",13)
		content.add_child(heading)
		var grid := GridContainer.new()
		grid.columns = 4
		grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		grid.add_theme_constant_override("h_separation",6)
		grid.add_theme_constant_override("v_separation",2)
		content.add_child(grid)
		for key in section.fields:
			var caption := Label.new()
			caption.text = str(key)
			caption.add_theme_color_override("font_color",Color("#8aa7b7"))
			caption.add_theme_font_size_override("font_size",12)
			grid.add_child(caption)
			var value := Label.new()
			value.name = "Field"+str(key)
			value.text = "—"
			value.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			value.add_theme_font_size_override("font_size",12)
			grid.add_child(value)
			_character_fields[str(key)] = value


func _set_character_field(key: String,value: String) -> void:
	if _character_fields.has(key):
		(_character_fields[key] as Label).text = value


func _refresh_character_info(snapshot: Dictionary) -> void:
	var actor_id := int(snapshot.get("actor_id",0))
	var name := "角色 #%d" % actor_id
	for entry in snapshot.get("characters",[]):
		if entry is Dictionary and int(entry.get("character_id",-1)) == actor_id:
			name = str(entry.get("name",name))
			break
	_set_character_field("名稱",name)
	for pair in [
		["等級","player_level"],["力量","player_strength"],
		["體質","player_constitution"],["敏捷","player_dexterity"],
		["智力","player_intelligence"],["攻擊","player_attack"],
		["防禦","player_defense"],["命中","player_hit"],
		["迴避","player_evasion"],["移動速度","player_move_speed"],
		["攻擊速度","player_attack_speed"],["職業","player_job"],
		["種族","player_race"]
	]:
		_set_character_field(str(pair[0]),str(snapshot[pair[1]]) if snapshot.has(pair[1]) else "—")
	_set_character_field("HP","%d / %d" % [
		int(snapshot.get("player_current_hp",0)),
		int(snapshot.get("player_max_hp",0))])
	_set_character_field("MP","%d / %d" % [
		int(snapshot.get("player_current_mp",0)),
		int(snapshot.get("player_max_mp",0))])
	_set_character_field("背包",str((snapshot.get("inventory_items",{}) as Dictionary).size()))
	_set_character_field("裝備",str((snapshot.get("equipment_items",{}) as Dictionary).size()))
	_set_character_field("金錢",str(snapshot.get("player_money",snapshot.get("wallet","—"))))
	_set_character_field("地圖",str(snapshot.get("map_id",0)))


func bind_session(panel) -> void:
	_panel = panel
	inventory.bind_backend(panel,panel.item_catalog)
	_is_bound = true
	_refresh_skin_items()

func _refresh_skin_items() -> void:
	var pickers: Array = [_skin_selection]
	var settings_picker = settings_window.find_child("UiProfilePickerSettings",true,false)
	if settings_picker is OptionButton:
		pickers.append(settings_picker)
	for picker in pickers:
		(picker as OptionButton).clear()
		for profile in UiSkins.available():
			var index := (picker as OptionButton).item_count
			(picker as OptionButton).add_item(str(profile.name))
			(picker as OptionButton).set_item_metadata(index,str(profile.id))
			if str(profile.id) == _profile:
				(picker as OptionButton).select(index)

func _skin_picked(index: int) -> void:
	skin_requested.emit(str(_skin_selection.get_item_metadata(index)))

func _refresh_aux_snapshot() -> void:
	var skills = _stats.get("learned_skills",_stats.get("known_skills",{}))
	var skill_summary = skill_window.find_child("SkillSummary",true,false)
	if skill_summary is Label:
		var skill_count := (skills as Dictionary).size() if skills is Dictionary else ((skills as Array).size() if skills is Array else 0)
		skill_window.meta_label.text = "已學技能 %d 個" % skill_count
		if skill_count > 0:
			var rows := PackedStringArray()
			if skills is Dictionary:
				for key in (skills as Dictionary).keys():
					rows.append("技能 %s" % str(key))
			else:
				for value in skills:
					rows.append("技能 %s" % str(value))
			(skill_summary as Label).text = "\n".join(rows)
		else:
			(skill_summary as Label).text = "目前沒有伺服器提供的已學技能清單"
	var members_node = party_window.find_child("PartyMembersSummary",true,false)
	if members_node is Label:
		var party_id := int(_stats.get("party_id",0))
		var members: Dictionary = _stats.get("party_members",{})
		party_window.meta_label.text = "成員 %d / 6" % members.size()
		if party_id <= 0 or members.is_empty():
			(members_node as Label).text = "尚未加入隊伍\n\n建立隊伍或接受其他玩家的邀請後，\n隊員狀態會顯示在這裡。"
		else:
			var ids := members.keys()
			ids.sort()
			var rows := PackedStringArray()
			for id in ids:
				var member: Dictionary = members[id]
				rows.append("%s%s　Actor %d" % ["★ " if int(id)==int(_stats.get("party_leader_actor_id",0)) else "・ ",str(member.get("name","玩家")),int(id)])
			(members_node as Label).text = "\n".join(rows)
	var clan_id := int(_stats.get("clan_id",0))
	var clan_name := str(_stats.get("clan_name",""))
	var clan_announcement := str(_stats.get("clan_announcement",""))
	var clan_revision := int(_stats.get("clan_revision",0))
	var clan_leader := int(_stats.get("clan_leader_actor_id",0))
	var self_actor := int(_stats.get("actor_id",0))
	var clan_members: Dictionary = _stats.get("clan_members",{})
	clan_window.meta_label.text = "成員 %d / 100" % clan_members.size()
	var clan_identity = clan_window.find_child("ClanIdentitySummary",true,false)
	if clan_identity is Label:
		(clan_identity as Label).text = (
			"%s\n戰團 ID %d　Revision %d\n團長 Actor %d\n公告：%s" % [
				clan_name,clan_id,clan_revision,clan_leader,
				clan_announcement if not clan_announcement.is_empty() else "（無）"]
		) if clan_id>0 else "尚未加入戰團"
	var clan_create = clan_window.find_child("ClanCreateSection",true,false)
	var clan_join = clan_window.find_child("ClanJoinSection",true,false)
	var clan_leave = clan_window.find_child("ClanLeaveButton",true,false)
	if clan_create is Control:
		(clan_create as Control).visible = clan_id==0
	if clan_join is Control:
		(clan_join as Control).visible = clan_id==0
	if clan_leave is Button:
		(clan_leave as Button).disabled = clan_id<=0
	var clan_management = clan_window.find_child("ClanManagementSection",true,false)
	var clan_announcement_edit = clan_window.find_child("ClanAnnouncementEdit",true,false)
	if clan_management is Control:
		(clan_management as Control).visible = clan_id>0 and clan_leader==self_actor
	if clan_announcement_edit is LineEdit and not (clan_announcement_edit as LineEdit).has_focus():
		(clan_announcement_edit as LineEdit).text = clan_announcement
	var clan_rows = clan_window.find_child("ClanMemberRows",true,false)
	if clan_rows is VBoxContainer:
		for child in (clan_rows as VBoxContainer).get_children():
			(clan_rows as VBoxContainer).remove_child(child)
			child.queue_free()
		var clan_ids := clan_members.keys()
		clan_ids.sort()
		for raw_id in clan_ids:
			var actor := int(raw_id)
			var member: Dictionary = clan_members[raw_id]
			var member_row := HBoxContainer.new()
			member_row.name = "ClanMemberRow%d" % actor
			member_row.custom_minimum_size.y = 30
			(clan_rows as VBoxContainer).add_child(member_row)
			var row := Label.new()
			row.name = "ClanMember%d" % actor
			row.text = "%s　%s　Actor %d" % [
				"★ 團長" if actor==clan_leader else "・ 成員",
				str(member.get("name","玩家")),actor]
			row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			row.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
			row.add_theme_font_size_override("font_size",14)
			member_row.add_child(row)
			if self_actor==clan_leader and actor!=self_actor:
				var transfer := Button.new()
				transfer.name = "ClanTransfer%d" % actor
				transfer.text = "轉讓"
				transfer.tooltip_text = "將團長轉讓給此成員"
				transfer.pressed.connect(func():
					if _panel != null:
						_panel.submit_clan_transfer_leadership(actor)
				)
				member_row.add_child(transfer)
				var kick := Button.new()
				kick.name = "ClanKick%d" % actor
				kick.text = "踢除"
				kick.theme_type_variation = &"ParadiseDangerButton"
				kick.pressed.connect(func():
					if _panel != null:
						_panel.submit_clan_kick(actor)
				)
				member_row.add_child(kick)
		if clan_ids.is_empty():
			var empty := Label.new()
			empty.text = "尚未加入戰團" if clan_id==0 else "等待伺服器成員快照"
			empty.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
			empty.custom_minimum_size.y = 60
			(clan_rows as VBoxContainer).add_child(empty)
	var clan_meta = clan_window.find_child("ClanMembersMeta",true,false)
	if clan_meta is Label:
		(clan_meta as Label).text = "%d / 100" % clan_members.size()
	var clan_status = clan_window.find_child("ClanOperationStatus",true,false)
	if clan_status is Label:
		var clan_result_status := int(_stats.get("last_clan_result_status",-1))
		var clan_result_text := ""
		if clan_result_status>=0:
			var messages := [
				"操作成功","已經加入戰團","目前沒有戰團","找不到戰團",
				"戰團名稱已被使用","戰團名稱格式錯誤","戰團人數已滿","戰團資料已變更，請重新操作",
				"只有團長可以執行此操作","目標不是戰團成員","不能對自己執行此操作","公告格式錯誤"]
			if clan_result_status<messages.size():
				clan_result_text = messages[clan_result_status]
		(clan_status as Label).text = clan_result_text

	var shop_summary = shop_window.find_child("ShopSummary",true,false)
	var active_npc := int(_stats.get("active_shop_npc",0))
	var shop_items: Dictionary = _stats.get("shop_items",{})
	if shop_summary is Label:
		(shop_summary as Label).text = ("NPC %d　商品 %d 項　金錢 %d" % [active_npc,shop_items.size(),int(_stats.get("shop_money",_stats.get("wallet",0)))]) if active_npc > 0 else "尚未開啟 NPC 商店"
	var buy_picker = shop_window.find_child("ShopBuyPicker",true,false)
	if buy_picker is OptionButton:
		var old_buy = int((buy_picker as OptionButton).get_item_metadata((buy_picker as OptionButton).selected)) if (buy_picker as OptionButton).selected >= 0 else -1
		(buy_picker as OptionButton).clear()
		var ids := shop_items.keys()
		ids.sort()
		for raw_id in ids:
			var item_id := int(raw_id)
			var item: Dictionary = shop_items[raw_id]
			var item_name: String = str(_panel.item_catalog.name_for(item_id)) if _panel != null else "Item %d" % item_id
			(buy_picker as OptionButton).add_item("%s　買 %d" % [item_name,int(item.get("buy_price",0))])
			(buy_picker as OptionButton).set_item_metadata((buy_picker as OptionButton).item_count-1,item_id)
			if item_id == old_buy:
				(buy_picker as OptionButton).select((buy_picker as OptionButton).item_count-1)
	var sell_picker = shop_window.find_child("ShopSellPicker",true,false)
	if sell_picker is OptionButton:
		(sell_picker as OptionButton).clear()
		var inventory_items: Dictionary = _stats.get("inventory_items",{})
		var uids := inventory_items.keys()
		uids.sort()
		for raw_uid in uids:
			var uid := int(raw_uid)
			var owned: Dictionary = inventory_items[raw_uid]
			var item_id := int(owned.get("item_definition_id",0))
			if not shop_items.has(item_id) or int((shop_items[item_id] as Dictionary).get("sell_price",0)) <= 0:
				continue
			var item_name: String = str(_panel.item_catalog.name_for(item_id)) if _panel != null else "Item %d" % item_id
			(sell_picker as OptionButton).add_item("%s ×%d　賣 %d" % [item_name,int(owned.get("quantity",0)),int((shop_items[item_id] as Dictionary).get("sell_price",0))])
			(sell_picker as OptionButton).set_item_metadata((sell_picker as OptionButton).item_count-1,uid)
	var quest_summary = quest_window.find_child("QuestSummary",true,false)
	var quest: Dictionary = _stats.get("quest_view",{})
	var available_list = quest_window.find_child("QuestAvailableList",true,false)
	var progress_list = quest_window.find_child("QuestInProgressList",true,false)
	var completed_list = quest_window.find_child("QuestCompletedList",true,false)
	for node in [available_list,progress_list,completed_list]:
		if node is Label:
			(node as Label).text = "—"
	if quest_summary is Label:
		if quest.is_empty():
			(quest_summary as Label).text = "目前沒有伺服器任務檢視"
		else:
			var title := str(quest.get("title","未命名任務"))
			var state := int(quest.get("state",0))
			(quest_summary as Label).text = "%s\n\n%s\n\n階段 %d　Revision %d" % [
				title,str(quest.get("text","")),int(quest.get("stage",0)),
				int(quest.get("progress_revision",0))]
			var target_list = available_list if state == 0 else (progress_list if state == 1 else completed_list)
			if target_list is Label:
				(target_list as Label).text = title
	var reward_summary = quest_window.find_child("QuestRewardSummary",true,false)
	if reward_summary is Label:
		var reward_text := str(quest.get("reward_text",""))
		(reward_summary as Label).text = reward_text if not reward_text.is_empty() else (
			"請選擇伺服器提供的任務選項。" if not quest.is_empty() else "目前沒有任務獎勵資料")
	var choice_picker = quest_window.find_child("QuestChoicePicker",true,false)
	if choice_picker is OptionButton:
		(choice_picker as OptionButton).clear()
		for raw_choice in quest.get("choices",[]):
			var choice: Dictionary = raw_choice
			(choice_picker as OptionButton).add_item(str(choice.get("label","選項")))
			(choice_picker as OptionButton).set_item_metadata((choice_picker as OptionButton).item_count-1,int(choice.get("choice_id",0)))
		var choose = quest_window.find_child("QuestChooseButton",true,false)
		if choose is Button:
			(choose as Button).disabled = (choice_picker as OptionButton).item_count == 0

func _hide_floating_windows() -> void:
	inventory.hide()
	character.hide()
	popup.hide()
	for window in _aux_windows:
		window.hide()

func _toggle_aux(window: Control) -> void:
	var should_show := not window.visible
	_hide_floating_windows()
	if should_show:
		window.show()
		_refresh_aux_snapshot()

func _menu_action(index: int) -> void:
	match index:
		0:
			var should_show := not character.visible
			_hide_floating_windows()
			character.visible = should_show
		1:
			var should_show := not inventory.visible
			_hide_floating_windows()
			inventory.visible = should_show
			if inventory.visible:
				inventory.apply_snapshot(_stats)
		2: _toggle_aux(skill_window)
		3: _toggle_aux(party_window)
		4: _toggle_aux(clan_window)
		5: _toggle_aux(shop_window)
		6: _toggle_aux(quest_window)
		7: _toggle_aux(mall_window)
		8: _toggle_aux(settings_window)

func _popup(title: String,message: String) -> void:
	_hide_floating_windows()
	popup.show()
	_popup_body.text = message
	_skin_selection.hide()
	_popup_title.text = title

func _label(parent: Control,value: String,position: Vector2,dimensions: Vector2,font_size: int) -> Label:
	var label := Label.new()
	label.text = value
	label.position = position
	label.size = dimensions
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.add_theme_font_size_override("font_size",font_size)
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(label)
	return label

func resize_view(view: Vector2) -> void:
	size = view
	var factor := maxf(0.1,minf(view.x / _design_size.x,view.y / _design_size.y))
	stage.scale = Vector2(factor,factor)
	if _profile == "original_ie_hk2":
		stage.size = _design_size
		stage.position = (view - _design_size*factor)*0.5
		header_actions.hide()
		mini_map.position = Vector2.ZERO
		menu.position = Vector2(770,195)
		status_hud.position = Vector2(0,510)
		shortcut.position = Vector2(132,365)
		chat_hud.position = Vector2(195,518)
		chat_hud.size = Vector2(568,76)
		inventory.position = Vector2(530,48)
		inventory.size = Vector2(259,506)
		character.position = Vector2(273,92)
		character.size = Vector2(263,397)
	else:
		# Recovered2 uses viewport anchors, not a centered 1400x900 sprite sheet.
		# Keep the source pixel scale, but expand the logical width on wide screens
		# so top-left/right and bottom-center controls stay on their real anchors.
		stage.position = Vector2.ZERO
		var logical := view / factor
		stage.size = logical
		header_actions.show()
		mini_map.position = Vector2.ZERO
		menu.position = Vector2(logical.x-50.0, logical.y*0.5-313.5)
		header_actions.position = Vector2(logical.x-120.0,0)
		status_hud.position = Vector2(logical.x*0.5-700.0,logical.y-135.0)
		shortcut.position = Vector2(logical.x*0.5-507.0,logical.y-135.0)
		chat_hud.position = Vector2(logical.x*0.5-314.0,logical.y-134.0)
		chat_hud.size = Vector2(700,134)
		inventory.position = Vector2(logical.x-318.0,130.0)
		inventory.size = Vector2(300,520)
		character.position = Vector2((logical.x-560.0)*0.5,(logical.y-610.0)*0.5)
		character.size = Vector2(560,610)
		popup.position = Vector2((logical.x-440.0)*0.5,(logical.y-285.0)*0.5)
		# Reference screenshot/source-space positions.
		skill_window.position = Vector2(logical.x-695.0,logical.y*0.5-313.0)
		party_window.position = Vector2(logical.x*0.5-380.0,logical.y*0.5-340.0)
		clan_window.position = Vector2(logical.x*0.5-450.0,logical.y*0.5-359.0)
		shop_window.position = Vector2(logical.x*0.5-360.0,logical.y*0.5-280.0)
		quest_window.position = Vector2(logical.x*0.5-450.0,logical.y*0.5-310.0)
		mall_window.position = Vector2(maxf(0.0,logical.x*0.5-700.0),maxf(0.0,logical.y*0.5-450.0))
		settings_window.position = Vector2(logical.x*0.5-270.0,logical.y*0.5-220.0)

func set_world_active(on: bool) -> void:
	if visible == on:
		return
	visible = on
	if not on:
		_hide_floating_windows()
		_chat_input.clear()
		_chat_log.clear()
		inventory.clear()

func apply_snapshot(snapshot: Dictionary) -> void:
	_stats = snapshot
	if not bool(snapshot.get("world_ready",false)):
		return
	_map_label.text = "Map %d" % int(snapshot.get("map_id",0))
	_coord_label.text = "X %d    Y %d" % [int(snapshot.get("x",0)),int(snapshot.get("y",0))]
	var hp_max := maxi(1,int(snapshot.get("player_max_hp",0)))
	var mp_max := maxi(1,int(snapshot.get("player_max_mp",0)))
	_hp.max_value = hp_max
	_hp.value = clampi(int(snapshot.get("player_current_hp",0)),0,hp_max)
	_mp.max_value = mp_max
	_mp.value = clampi(int(snapshot.get("player_current_mp",0)),0,mp_max)
	var stamina_max := maxi(1,int(snapshot.get("player_max_stamina",100)))
	var stamina_value := clampi(int(snapshot.get("player_stamina",0)),0,stamina_max)
	_stamina.max_value = stamina_max
	_stamina.value = stamina_value
	_hp_text.text = "%d / %d" % [int(_hp.value),hp_max]
	_mp_text.text = "%d / %d" % [int(_mp.value),mp_max]
	_stamina_text.text = "%d / %d" % [stamina_value,stamina_max]
	_money.text = str(int(snapshot.get("player_money",snapshot.get("wallet",0))))
	_level.text = "Lv.%d" % int(snapshot.get("player_level",1))
	_refresh_character_info(snapshot)
	_refresh_aux_snapshot()
	if inventory.visible:
		inventory.apply_snapshot(snapshot)
	# The original chat panel receives the same snapshot and owns all wire.
	if _panel != null and _panel.chat_panel.history != null:
		var text: String = _panel.chat_panel.history.text
		if text != _chat_log.text:
			_chat_log.text = text
			_chat_log.set_v_scroll(_chat_log.get_line_count())

func _submit_chat() -> void:
	if _panel == null or _chat_input.text.strip_edges().is_empty():
		return
	if _panel.chat_panel.nearby_send_button.disabled:
		return
	_panel.chat_panel.nearby_input.text = _chat_input.text.strip_edges()
	_panel.chat_panel._send_nearby_chat()
	if _panel.chat_panel.nearby_input.text.is_empty():
		_chat_input.clear()

func all_ui_action_labels() -> Array:
	return ACTIONS.duplicate()
