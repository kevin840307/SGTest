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
var _character_info := Label.new()
var _character_fields: Dictionary = {}
var _map_label := Label.new()
var _coord_label := Label.new()
var _hp := ProgressBar.new()
var _mp := ProgressBar.new()
var _stamina := ProgressBar.new()
var _hp_text := Label.new()
var _mp_text := Label.new()
var _stamina_text := Label.new()
var _money := Label.new()
var _level := Label.new()
var _chat_input := LineEdit.new()
var _chat_log := TextEdit.new()
var _info_title := Label.new()
var _popup_title := Label.new()
var _popup_layout := VBoxContainer.new()
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
	# All windows live under the same stage and are persistent; user clicks
	# show/hide them without reallocating game assets or reconstructing panels.
	stage.add_child(inventory)
	inventory.position = Vector2(1082,130)
	inventory.size = Vector2(278,510)
	for row in [[character,"CharacterWindow","角色資訊",_character_info,Vector2(410,123),Vector2(580,600)],
		[popup,"InformationWindow","提示",_popup_body,Vector2(480,225),Vector2(440,285)]]:
		var window: PanelContainer = row[0]
		window.name = row[1]
		window.theme_type_variation = &"ParadiseWindowFrame"
		window.position = row[4]
		window.size = row[5]
		window.mouse_filter = Control.MOUSE_FILTER_STOP
		window.visible = false
		stage.add_child(window)
		var vertical := VBoxContainer.new()
		vertical.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		window.add_child(vertical)
		var heading := HBoxContainer.new()
		vertical.add_child(heading)
		var text := Label.new()
		text.text = row[2]
		text.theme_type_variation = &"ParadiseWindowTitle"
		text.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		heading.add_child(text)
		if window == popup:
			_popup_title = text
			_popup_layout = vertical
		var close := TextureButton.new()
		close.name = "CloseWindow"
		close.custom_minimum_size = Vector2(22,22)
		close.ignore_texture_size = true
		close.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
		close.texture_normal = skin
		close.pressed.connect(window.hide)
		heading.add_child(close)
		vertical.add_child(HSeparator.new())
		var content: Label = row[3]
		content.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		content.size_flags_vertical = Control.SIZE_EXPAND_FILL
		content.add_theme_font_size_override("font_size",15)
		vertical.add_child(content)
		if window == character:
			content.hide()
			_build_character_sections(vertical)
	_info_title.text = "角色"
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
	for entry in [["接受邀請","_party_accept_invite"],["拒絕邀請","_party_reject_invite"],["離開隊伍","_party_leave"]]:
		var button := Button.new()
		button.text = str(entry[0])
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.pressed.connect(func():
			if _panel != null:
				_panel.party_panel.call(str(entry[1]))
		)
		actions.add_child(button)
	party_window.body.add_child(actions)

func _build_clan_aux() -> void:
	var panel = clan_window.section("戰團")
	var empty := Label.new()
	empty.name = "ClanUnavailable"
	empty.text = "尚未加入戰團\n\nSGTest Server 目前沒有戰團／社團協定。\n此視窗只對齊 Recovered2 外觀，不會偽造會員、資金或操作結果。"
	empty.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	empty.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	empty.size_flags_vertical = Control.SIZE_EXPAND_FILL
	empty.add_theme_font_size_override("font_size",18)
	panel.add_child(empty)

func _build_shop_aux() -> void:
	var panel = shop_window.section("NPC 商店")
	var npc_row := HBoxContainer.new()
	var npc := SpinBox.new()
	npc.name = "ShopNpcInput"
	npc.min_value = 1
	npc.max_value = 2147483647
	npc.value = 1
	npc.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	npc_row.add_child(npc)
	var open := Button.new()
	open.text = "開啟附近 NPC 商店"
	open.pressed.connect(func():
		if _panel == null:
			return
		_panel.npc_shop_panel.shop_npc_input.value = npc.value
		_panel.npc_shop_panel._open_shop_from_input()
	)
	npc_row.add_child(open)
	panel.add_child(npc_row)
	var status := Label.new()
	status.name = "ShopSummary"
	status.text = "尚未開啟 NPC 商店"
	status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	panel.add_child(status)
	var buy_row := HBoxContainer.new()
	var buy_picker := OptionButton.new()
	buy_picker.name = "ShopBuyPicker"
	buy_picker.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	buy_row.add_child(buy_picker)
	var buy_qty := SpinBox.new()
	buy_qty.name = "ShopBuyQuantity"
	buy_qty.min_value = 1
	buy_qty.max_value = 1000000
	buy_qty.value = 1
	buy_qty.custom_minimum_size.x = 90
	buy_row.add_child(buy_qty)
	var buy := Button.new()
	buy.text = "購買"
	buy.pressed.connect(func():
		if _panel == null or buy_picker.selected < 0:
			return
		_panel.submit_shop_buy(int(buy_picker.get_item_metadata(buy_picker.selected)),int(buy_qty.value))
	)
	buy_row.add_child(buy)
	panel.add_child(buy_row)
	var sell_row := HBoxContainer.new()
	var sell_picker := OptionButton.new()
	sell_picker.name = "ShopSellPicker"
	sell_picker.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	sell_row.add_child(sell_picker)
	var sell_qty := SpinBox.new()
	sell_qty.name = "ShopSellQuantity"
	sell_qty.min_value = 1
	sell_qty.max_value = 1000000
	sell_qty.value = 1
	sell_qty.custom_minimum_size.x = 90
	sell_row.add_child(sell_qty)
	var sell := Button.new()
	sell.text = "販售"
	sell.pressed.connect(func():
		if _panel == null or sell_picker.selected < 0:
			return
		_panel.submit_shop_sell(int(sell_picker.get_item_metadata(sell_picker.selected)),int(sell_qty.value))
	)
	sell_row.add_child(sell)
	panel.add_child(sell_row)
	var filler := Control.new()
	filler.size_flags_vertical = Control.SIZE_EXPAND_FILL
	panel.add_child(filler)

func _build_quest_aux() -> void:
	var columns := HBoxContainer.new()
	columns.size_flags_vertical = Control.SIZE_EXPAND_FILL
	for title in ["待領取","進行中","已完成"]:
		var column := PanelContainer.new()
		column.theme_type_variation = &"ParadiseContentPanel"
		column.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		columns.add_child(column)
		var label := Label.new()
		label.text = title
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		label.vertical_alignment = VERTICAL_ALIGNMENT_TOP
		column.add_child(label)
	quest_window.body.add_child(columns)
	var detail = quest_window.section("任務流程／任務獎勵")
	var view := Label.new()
	view.name = "QuestSummary"
	view.text = "目前沒有伺服器任務檢視"
	view.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	detail.add_child(view)
	var row := HBoxContainer.new()
	var npc := SpinBox.new()
	npc.name = "QuestNpcInput"
	npc.min_value = 1
	npc.max_value = 2147483647
	npc.value = 1
	npc.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(npc)
	var open := Button.new()
	open.text = "開啟附近 NPC 任務"
	open.pressed.connect(func():
		if _panel == null:
			return
		_panel.quest_panel.npc_input.value = npc.value
		_panel.quest_panel._open_quest_from_input()
	)
	row.add_child(open)
	quest_window.body.add_child(row)
	var choice_row := HBoxContainer.new()
	var choice := OptionButton.new()
	choice.name = "QuestChoicePicker"
	choice.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	choice_row.add_child(choice)
	var choose := Button.new()
	choose.text = "選擇任務選項"
	choose.pressed.connect(func():
		if _panel == null or choice.selected < 0:
			return
		_panel.submit_quest_choice(int(choice.get_item_metadata(choice.selected)))
	)
	choice_row.add_child(choose)
	quest_window.body.add_child(choice_row)

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
	if quest_summary is Label:
		(quest_summary as Label).text = ("任務：%s\n%s\n階段 %d" % [str(quest.get("title","")),str(quest.get("text","")),int(quest.get("stage",0))]) if not quest.is_empty() else "目前沒有伺服器任務檢視"
	var choice_picker = quest_window.find_child("QuestChoicePicker",true,false)
	if choice_picker is OptionButton:
		(choice_picker as OptionButton).clear()
		for raw_choice in quest.get("choices",[]):
			var choice: Dictionary = raw_choice
			(choice_picker as OptionButton).add_item(str(choice.get("label","選項")))
			(choice_picker as OptionButton).set_item_metadata((choice_picker as OptionButton).item_count-1,int(choice.get("choice_id",0)))

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
		inventory.size = Vector2(278,510)
		character.position = Vector2((logical.x-580.0)*0.5,(logical.y-600.0)*0.5)
		character.size = Vector2(580,600)
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
