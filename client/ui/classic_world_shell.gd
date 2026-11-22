extends Control
signal skin_requested(id:String)
## Original IE HK2 in-world presentation.
## This shell owns only presentation/hitboxes. All state and actions delegate
## to the same SGTest SessionPanel/ClientSession as every other UI profile.
const UiTextures = preload("res://ui/ui_texture_loader.gd")
const UiSkins = preload("res://ui/ui_skin_catalog.gd")
const Photos = preload("res://ui/local_item_photo_cache.gd")

const PRIVATE := "res://ui/private_ie_hk2"
const BASE_SIZE := Vector2(800,600)
const ITEM_ROW_COUNT := 17
const SKILL_ROW_COUNT := 16

var _panel
var _stats:Dictionary={}
var _active:=false
var _last_view:=Vector2.ZERO
var _photos:=Photos.new()

var stage:=Control.new()
var hud:=Control.new()
var menu:=Control.new()
var secondary_menu:=Control.new()
var chat_panel:=Control.new()
var friend_panel:=Control.new()
var inventory: Control
var character: Control
var skill_window: Control
var party_window: Control
var clan_window: Control
var shop_window: Control
var quest_window: Control
var player_shop_window: Control
var storage_window: Control
var bank_window: Control
var repair_window: Control
var trade_window: Control
var dialog_window: Control
var settings_window: Control
var _windows:Array[Control]=[]

var _hp_label: Label
var _mp_label: Label
var _level_label: Label
var _inventory_rows:=VBoxContainer.new()
var _equipment_rows:=VBoxContainer.new()
var _inventory_item_bg:TextureRect
var _inventory_equip_bg:TextureRect
var _inventory_tab:=0
var _inventory_tab_buttons:Array[TextureButton]=[]
var _character_labels:Dictionary={}
var _skill_rows:=VBoxContainer.new()
var _party_rows:=VBoxContainer.new()
var _clan_rows:=VBoxContainer.new()
var _shop_buy_rows:=VBoxContainer.new()
var _shop_sell_rows:=VBoxContainer.new()
var _quest_title: Label
var _quest_text: Label
var _quest_choices:=VBoxContainer.new()
var _chat_history:=TextEdit.new()
var _chat_input:=LineEdit.new()
var _friend_rows:=VBoxContainer.new()
var _player_shop_rows:=VBoxContainer.new()
var _player_shop_price:=SpinBox.new()
var _player_shop_buy:=Button.new()
var _storage_rows:=VBoxContainer.new()
var _storage_inventory_rows:=VBoxContainer.new()
var _bank_status: Label
var _bank_amount:=SpinBox.new()
var _repair_rows:=VBoxContainer.new()
var _trade_rows:=VBoxContainer.new()
var _trade_status: Label
var _dialog_title: Label
var _dialog_text: Label
var _dialog_choices:=VBoxContainer.new()
var _dialog_key:=""
var _settings_picker:=OptionButton.new()

func _init()->void:
	name="ClassicWorldShell"
	mouse_filter=Control.MOUSE_FILTER_IGNORE
	set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT)
	size=BASE_SIZE
	add_child(stage)
	stage.name="ClassicStage"
	stage.size=BASE_SIZE
	stage.mouse_filter=Control.MOUSE_FILTER_PASS
	stage.add_child(hud)
	hud.name="ClassicHud"
	hud.size=BASE_SIZE
	hud.mouse_filter=Control.MOUSE_FILTER_PASS
	_build_hud()
	_build_inventory()
	_build_character()
	_build_skill()
	_build_party()
	_build_clan()
	_build_shop()
	_build_quest()
	_build_player_shop()
	_build_storage()
	_build_bank()
	_build_repair()
	_build_trade()
	_build_dialog()
	_build_settings()
	_windows=[inventory,character,skill_window,party_window,clan_window,shop_window,quest_window,player_shop_window,storage_window,bank_window,repair_window,trade_window,dialog_window,settings_window]
	visible=false
	set_process(false)

func _tex(name:String)->Texture2D:
	return UiTextures.get_texture(PRIVATE.path_join(name+".png"))

func _background(parent:Control,name:String,size:Vector2)->TextureRect:
	var texture:=TextureRect.new()
	texture.name=name+"Background"
	texture.texture=_tex(name)
	texture.position=Vector2.ZERO
	texture.size=size
	texture.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	texture.stretch_mode=TextureRect.STRETCH_SCALE
	texture.texture_filter=CanvasItem.TEXTURE_FILTER_NEAREST
	texture.mouse_filter=Control.MOUSE_FILTER_IGNORE
	parent.add_child(texture)
	parent.move_child(texture,0)
	return texture

func _classic_label(parent:Control,node_name:String,rect:Rect2,font_size:=11,center:=false)->Label:
	var label:=Label.new()
	label.name=node_name
	label.position=rect.position
	label.size=rect.size
	label.mouse_filter=Control.MOUSE_FILTER_IGNORE
	label.vertical_alignment=VERTICAL_ALIGNMENT_CENTER
	label.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER if center else HORIZONTAL_ALIGNMENT_LEFT
	label.add_theme_font_size_override("font_size",font_size)
	label.add_theme_color_override("font_color",Color("#f9f2d6"))
	label.add_theme_color_override("font_shadow_color",Color(0,0,0,0.9))
	label.add_theme_constant_override("shadow_offset_x",1)
	label.add_theme_constant_override("shadow_offset_y",1)
	parent.add_child(label)
	return label

func _hit(parent:Control,node_name:String,rect:Rect2,callback:Callable,tooltip:String="")->Button:
	var button:=Button.new()
	button.name=node_name
	button.position=rect.position
	button.size=rect.size
	button.flat=true
	button.focus_mode=Control.FOCUS_NONE
	button.mouse_default_cursor_shape=Control.CURSOR_POINTING_HAND
	button.tooltip_text=tooltip
	button.pressed.connect(callback)
	parent.add_child(button)
	return button

func _texture_button(parent:Control,node_name:String,art:String,position:Vector2,size:Vector2,callback:Callable,tooltip:String="")->TextureButton:
	var button:=TextureButton.new()
	button.name=node_name
	button.position=position
	button.size=size
	button.ignore_texture_size=true
	button.stretch_mode=TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	button.focus_mode=Control.FOCUS_NONE
	button.mouse_default_cursor_shape=Control.CURSOR_POINTING_HAND
	button.tooltip_text=tooltip
	button.texture_normal=_tex(art+"_0")
	button.texture_hover=_tex(art+"_1")
	button.texture_pressed=_tex(art+"_2")
	button.texture_disabled=_tex(art+"_3")
	button.pressed.connect(callback)
	parent.add_child(button)
	return button

func _transparent_line_edit(edit:LineEdit)->void:
	var empty:=StyleBoxEmpty.new()
	edit.add_theme_stylebox_override("normal",empty)
	edit.add_theme_stylebox_override("focus",StyleBoxEmpty.new())
	edit.add_theme_stylebox_override("read_only",StyleBoxEmpty.new())
	edit.add_theme_color_override("font_color",Color("#f9f2d6"))
	edit.add_theme_color_override("font_placeholder_color",Color("#bda982"))
	edit.add_theme_color_override("caret_color",Color.WHITE)
	edit.add_theme_font_size_override("font_size",11)

func _deferred_background(parent:Control,name:String,size:Vector2)->TextureRect:
	var texture:=TextureRect.new()
	texture.name=name+"Background"
	texture.position=Vector2.ZERO
	texture.size=size
	texture.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	texture.stretch_mode=TextureRect.STRETCH_SCALE
	texture.texture_filter=CanvasItem.TEXTURE_FILTER_NEAREST
	texture.mouse_filter=Control.MOUSE_FILTER_IGNORE
	texture.set_meta("classic_art_name",name)
	parent.add_child(texture)
	parent.move_child(texture,0)
	return texture

func _ensure_window_art(window:Control)->void:
	for child in window.get_children():
		if child is TextureRect and child.has_meta("classic_art_name") and child.texture==null:
			child.texture=_tex(str(child.get_meta("classic_art_name")))

func _window(name:String,background:String,size:Vector2,position:Vector2)->Control:
	var window:=Control.new()
	window.name=name
	window.position=position
	window.size=size
	window.mouse_filter=Control.MOUSE_FILTER_STOP
	stage.add_child(window)
	_deferred_background(window,background,size)
	_texture_button(window,"CloseWindow","CLOSESCREEN",Vector2(size.x-20,3),Vector2(17,17),window.hide,"關閉")
	window.hide()
	return window

func _build_hud()->void:
	var hp:=TextureRect.new()
	hp.name="HpBarArt"
	hp.texture=_tex("HPBAR")
	hp.position=Vector2(4,502)
	hp.size=Vector2(128,90)
	hp.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	hp.stretch_mode=TextureRect.STRETCH_SCALE
	hp.mouse_filter=Control.MOUSE_FILTER_IGNORE
	hud.add_child(hp)
	_level_label=_classic_label(hud,"Level",Rect2(37,505,72,15),11,false)
	_hp_label=_classic_label(hud,"HP",Rect2(34,527,89,13),10,false)
	_mp_label=_classic_label(hud,"MP",Rect2(34,543,89,13),10,false)

	var minimap:=TextureRect.new()
	minimap.name="MiniMap"
	minimap.texture=_tex("MINIMAP_L")
	minimap.position=Vector2(663,4)
	minimap.size=Vector2(133,122)
	minimap.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	minimap.stretch_mode=TextureRect.STRETCH_SCALE
	minimap.mouse_filter=Control.MOUSE_FILTER_IGNORE
	hud.add_child(minimap)

	var hotkey:=TextureRect.new()
	hotkey.name="HotkeyArt"
	hotkey.texture=_tex("HOTKEY_LY")
	hotkey.position=Vector2(2,5)
	hotkey.size=Vector2(30,358)
	hotkey.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	hotkey.stretch_mode=TextureRect.STRETCH_SCALE
	hotkey.mouse_filter=Control.MOUSE_FILTER_IGNORE
	hud.add_child(hotkey)
	for i in 12:
		var hotkey_label:=_classic_label(hud,"Hotkey%d"%i,Rect2(3,11+float(i)*27.2,27,18),9,true)
		hotkey_label.text="F%d"%(i+1)

	var command:=TextureRect.new()
	command.name="CommandArt"
	command.texture=_tex("COMMAND")
	command.position=Vector2(770,154)
	command.size=Vector2(30,249)
	command.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	command.stretch_mode=TextureRect.STRETCH_SCALE
	command.mouse_filter=Control.MOUSE_FILTER_IGNORE
	hud.add_child(command)
	secondary_menu.name="SecondaryCommandHitboxes"
	secondary_menu.position=command.position
	secondary_menu.size=command.size
	hud.add_child(secondary_menu)
	_texture_button(secondary_menu,"SkillCommand","B_BASIC_SKILL",Vector2(3,4),Vector2(24,23),func():_toggle(skill_window),"技能")
	_texture_button(secondary_menu,"PartyCommand","B_COMMAND_TEAM",Vector2(3,35),Vector2(24,23),func():_toggle(party_window),"隊伍")
	var secondary_actions=[
		["QuestCommand","任務"],
		["NpcShopCommand","NPC 商店"],
		["StorageCommand","倉庫"],
		["BankCommand","銀行"],
		["RepairCommand","修理"],
		["TradeCommand","交易"],
	]
	for i in secondary_actions.size():
		var spec:Array=secondary_actions[i]
		var hit:=_hit(secondary_menu,str(spec[0]),Rect2(0,66+float(i)*30.5,30,29),_secondary_menu_action.bind(i),str(spec[1]))
		hit.modulate=Color(1,1,1,0.01)

	chat_panel.name="ClassicChatPanel"
	chat_panel.position=Vector2(130,484)
	chat_panel.size=Vector2(478,116)
	chat_panel.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(chat_panel)
	var chat_bg:=NinePatchRect.new()
	chat_bg.name="BG_MESSAGE_INGAMEBackground"
	chat_bg.texture=_tex("BG_MESSAGE_INGAME")
	chat_bg.size=chat_panel.size
	chat_bg.patch_margin_left=6
	chat_bg.patch_margin_top=22
	chat_bg.patch_margin_right=6
	chat_bg.patch_margin_bottom=8
	chat_bg.mouse_filter=Control.MOUSE_FILTER_IGNORE
	chat_panel.add_child(chat_bg)
	_chat_history.name="ChatHistory"
	_chat_history.position=Vector2(6,8)
	_chat_history.size=Vector2(466,78)
	_chat_history.editable=false
	_chat_history.wrap_mode=TextEdit.LINE_WRAPPING_BOUNDARY
	_chat_history.mouse_filter=Control.MOUSE_FILTER_IGNORE
	_chat_history.add_theme_stylebox_override("normal",StyleBoxEmpty.new())
	_chat_history.add_theme_color_override("font_color",Color("#f9f2d6"))
	_chat_history.add_theme_font_size_override("font_size",10)
	chat_panel.add_child(_chat_history)
	_chat_input.name="ChatInput"
	_chat_input.position=Vector2(7,89)
	_chat_input.size=Vector2(410,19)
	_chat_input.placeholder_text="輸入附近聊天"
	_chat_input.max_length=256
	_transparent_line_edit(_chat_input)
	_chat_input.text_submitted.connect(func(_value):_send_chat())
	chat_panel.add_child(_chat_input)
	var send:=_hit(chat_panel,"SendChat",Rect2(421,88,50,20),_send_chat,"送出聊天")
	send.text="送出"
	send.add_theme_font_size_override("font_size",9)

	friend_panel.name="ClassicFriendPanel"
	friend_panel.position=Vector2(714,501)
	friend_panel.size=Vector2(86,99)
	friend_panel.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(friend_panel)
	_background(friend_panel,"BG_RIGHTBOX",friend_panel.size)
	_friend_rows.name="FriendRows"
	_friend_rows.position=Vector2(3,18)
	_friend_rows.size=Vector2(80,77)
	_friend_rows.add_theme_constant_override("separation",0)
	friend_panel.add_child(_friend_rows)

	menu.name="MainCommandBar"
	menu.position=Vector2(615,489)
	menu.size=Vector2(145,28)
	hud.add_child(menu)
	_texture_button(menu,"Menu0","B_BASIC_GUILD",Vector2(0,2),Vector2(24,23),_menu_action.bind(0),"戰團")
	_texture_button(menu,"Menu1","B_BASIC_PLAYER",Vector2(29,2),Vector2(24,23),_menu_action.bind(1),"人物狀態")
	_texture_button(menu,"Menu2","B_BASIC_ITEM",Vector2(58,2),Vector2(24,23),_menu_action.bind(2),"包包")
	_texture_button(menu,"Menu3","B_BASIC_PSHOP",Vector2(88,4),Vector2(24,23),_menu_action.bind(3),"小商店")
	_texture_button(menu,"Menu4","B_BASIC_SYSTEM",Vector2(117,2),Vector2(24,23),_menu_action.bind(4),"系統")

func _build_inventory()->void:
	inventory=_window("InventoryWindow","BG_ITEM",Vector2(259,506),Vector2(531,71))
	_inventory_item_bg=inventory.get_node_or_null("BG_ITEMBackground") as TextureRect
	_inventory_equip_bg=_deferred_background(inventory,"BG_Equip",inventory.size)
	_inventory_equip_bg.hide()
	var tab_specs=[
		["B_ITEM_ITEM","道具"],
		["B_ITEM_EQUIP","裝備"],
		["B_ITEM_ARMOR","機甲／裝甲"],
		["B_ITEM_PET","寵物"],
		["B_ITEM_RIDE","騎乘"],
	]
	for i in tab_specs.size():
		var spec:Array=tab_specs[i]
		var button:=_texture_button(
			inventory,"InventoryTab%d"%i,str(spec[0]),Vector2(1,43+float(i)*39.0),
			Vector2(36,37),_change_inventory_tab.bind(i),str(spec[1]))
		_inventory_tab_buttons.append(button)
	# The original art exposes 17 visible rows beginning around y=61.
	_inventory_rows.name="InventoryRows"
	_inventory_rows.position=Vector2(39,59)
	_inventory_rows.size=Vector2(166,425)
	_inventory_rows.add_theme_constant_override("separation",0)
	inventory.add_child(_inventory_rows)
	_equipment_rows.name="EquippedRows"
	_equipment_rows.position=Vector2(70,61)
	_equipment_rows.size=Vector2(153,235)
	_equipment_rows.add_theme_constant_override("separation",0)
	_equipment_rows.hide()
	inventory.add_child(_equipment_rows)

func _build_character()->void:
	character=_window("CharacterWindow","BG_CHARACTER_INFO",Vector2(263,397),Vector2(269,95))
	for pair in [
		["名稱",Rect2(52,34,116,19)],["等級",Rect2(52,55,116,19)],
		["職業",Rect2(52,76,116,19)],["力量",Rect2(45,104,57,17)],
		["敏捷",Rect2(45,122,57,17)],["體質",Rect2(45,140,57,17)],
		["智力",Rect2(45,158,57,17)],["攻擊",Rect2(161,103,91,17)],
		["防禦",Rect2(161,122,91,17)],["命中",Rect2(161,141,91,17)],
		["迴避",Rect2(161,160,91,17)],["移動速度",Rect2(161,179,91,17)],
		["攻擊速度",Rect2(161,198,91,17)],["HP",Rect2(9,235,115,17)],
		["MP",Rect2(9,255,115,17)],["金錢",Rect2(137,235,119,17)],
		["地圖",Rect2(137,255,119,17)]
	]:
		_character_labels[pair[0]]=_classic_label(character,"Field"+str(pair[0]),pair[1],10,false)

func _build_skill()->void:
	skill_window=_window("SkillWindow","BG_SKILL",Vector2(223,506),Vector2(542,63))
	_skill_rows.name="SkillRows"
	_skill_rows.position=Vector2(34,37)
	_skill_rows.size=Vector2(165,442)
	_skill_rows.add_theme_constant_override("separation",1)
	skill_window.add_child(_skill_rows)

func _build_party()->void:
	party_window=_window("PartyWindow","BG_TEAM",Vector2(139,144),Vector2(626,424))
	_party_rows.name="PartyRows"
	_party_rows.position=Vector2(7,5)
	_party_rows.size=Vector2(124,132)
	_party_rows.add_theme_constant_override("separation",1)
	party_window.add_child(_party_rows)

func _build_clan()->void:
	clan_window=_window("ClanWindow","BG_CLUB",Vector2(263,360),Vector2(269,113))
	var header:=_classic_label(clan_window,"ClanHeader",Rect2(9,36,245,77),10,false)
	header.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART
	_clan_rows.name="ClanMemberRows"
	_clan_rows.position=Vector2(5,138)
	_clan_rows.size=Vector2(238,190)
	_clan_rows.add_theme_constant_override("separation",0)
	clan_window.add_child(_clan_rows)
	var create:=_hit(clan_window,"ClanCreateButton",Rect2(7,330,75,24),func():
		if _panel!=null:
			var requested="SGTest戰團"
			_panel.submit_clan_create(requested)
	,"建立戰團")
	create.modulate=Color(1,1,1,0.01)
	var refresh:=_hit(clan_window,"ClanRefreshButton",Rect2(92,330,75,24),func():
		if _panel!=null:_panel.submit_clan_refresh()
	,"更新")
	refresh.modulate=Color(1,1,1,0.01)
	var leave:=_hit(clan_window,"ClanLeaveButton",Rect2(177,330,75,24),func():
		if _panel!=null:_panel.submit_clan_leave()
	,"離開戰團")
	leave.modulate=Color(1,1,1,0.01)

func _build_shop()->void:
	shop_window=_window("ShopWindow","BG_SHOP",Vector2(770,506),Vector2(15,69))
	_shop_buy_rows.name="ShopBuyRows"
	_shop_buy_rows.position=Vector2(36,60)
	_shop_buy_rows.size=Vector2(410,425)
	_shop_buy_rows.add_theme_constant_override("separation",0)
	shop_window.add_child(_shop_buy_rows)
	_shop_sell_rows.name="ShopSellRows"
	_shop_sell_rows.position=Vector2(548,60)
	_shop_sell_rows.size=Vector2(164,425)
	_shop_sell_rows.add_theme_constant_override("separation",0)
	shop_window.add_child(_shop_sell_rows)

func _build_quest()->void:
	quest_window=_window("QuestWindow","BG_QUEST_LIST",Vector2(527,350),Vector2(137,125))
	_quest_title=_classic_label(quest_window,"QuestTitle",Rect2(269,36,230,22),11,false)
	_quest_text=_classic_label(quest_window,"QuestText",Rect2(6,216,247,118),10,false)
	_quest_text.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART
	_quest_choices.name="QuestChoices"
	_quest_choices.position=Vector2(268,214)
	_quest_choices.size=Vector2(233,96)
	_quest_choices.add_theme_constant_override("separation",2)
	quest_window.add_child(_quest_choices)

func _build_player_shop()->void:
	player_shop_window=_window("PlayerShopWindow","BG_PSHOP_01",Vector2(770,506),Vector2(15,69))
	_player_shop_price.name="PlayerShopPrice"
	_player_shop_price.position=Vector2(565,32)
	_player_shop_price.size=Vector2(135,24)
	_player_shop_price.min_value=1
	_player_shop_price.max_value=9000000000000000
	_player_shop_price.step=1
	_player_shop_price.value=100
	player_shop_window.add_child(_player_shop_price)
	_player_shop_rows.name="PlayerShopRows"
	_player_shop_rows.position=Vector2(36,62)
	_player_shop_rows.size=Vector2(670,385)
	_player_shop_rows.add_theme_constant_override("separation",1)
	player_shop_window.add_child(_player_shop_rows)
	_player_shop_buy.text="購買目前查看中的商品"
	_player_shop_buy.position=Vector2(525,461)
	_player_shop_buy.size=Vector2(180,26)
	_player_shop_buy.flat=true
	_player_shop_buy.pressed.connect(_buy_inspected_player_shop)
	player_shop_window.add_child(_player_shop_buy)

func _build_storage()->void:
	storage_window=_window("StorageWindow","BG_BANK",Vector2(770,506),Vector2(15,69))
	_storage_rows.name="StorageRows"
	_storage_rows.position=Vector2(35,59)
	_storage_rows.size=Vector2(165,425)
	_storage_rows.add_theme_constant_override("separation",0)
	storage_window.add_child(_storage_rows)
	_storage_inventory_rows.name="StorageInventoryRows"
	_storage_inventory_rows.position=Vector2(548,59)
	_storage_inventory_rows.size=Vector2(165,425)
	_storage_inventory_rows.add_theme_constant_override("separation",0)
	storage_window.add_child(_storage_inventory_rows)

func _build_bank()->void:
	bank_window=_window("BankWindow","BG_NPC_MESSAGE",Vector2(399,172),Vector2(201,214))
	_bank_status=_classic_label(bank_window,"BankStatus",Rect2(28,35,343,45),11,false)
	_bank_status.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART
	_bank_amount.name="BankAmount"
	_bank_amount.position=Vector2(30,88)
	_bank_amount.size=Vector2(140,24)
	_bank_amount.min_value=1
	_bank_amount.max_value=9000000000000000
	_bank_amount.step=1
	_bank_amount.value=1
	bank_window.add_child(_bank_amount)
	var deposit:=_hit(bank_window,"BankDeposit",Rect2(180,88,82,24),func():
		if _panel!=null:_panel.submit_bank_deposit(int(_bank_amount.value)),"存入金錢")
	deposit.text="存入"
	var withdraw:=_hit(bank_window,"BankWithdraw",Rect2(272,88,82,24),func():
		if _panel!=null:_panel.submit_bank_withdraw(int(_bank_amount.value)),"領出金錢")
	withdraw.text="領出"

func _build_repair()->void:
	repair_window=_window("RepairWindow","BG_REPAIR",Vector2(223,506),Vector2(542,63))
	_repair_rows.name="RepairRows"
	_repair_rows.position=Vector2(30,45)
	_repair_rows.size=Vector2(170,420)
	_repair_rows.add_theme_constant_override("separation",1)
	repair_window.add_child(_repair_rows)

func _build_trade()->void:
	trade_window=_window("TradeWindow","BG_TRADE",Vector2(770,506),Vector2(15,69))
	_trade_status=_classic_label(trade_window,"TradeStatus",Rect2(35,35,680,44),10,false)
	_trade_status.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART
	_trade_rows.name="TradeRows"
	_trade_rows.position=Vector2(36,88)
	_trade_rows.size=Vector2(670,315)
	_trade_rows.add_theme_constant_override("separation",1)
	trade_window.add_child(_trade_rows)
	var accept:=_hit(trade_window,"TradeAccept",Rect2(36,430,80,24),func():
		if _panel!=null and _panel.session!=null:_panel.session.accept_trade(),"接受交易")
	accept.text="接受"
	var reject:=_hit(trade_window,"TradeReject",Rect2(126,430,80,24),func():
		if _panel!=null and _panel.session!=null:_panel.session.reject_trade(),"拒絕交易")
	reject.text="拒絕"
	var lock:=_hit(trade_window,"TradeLock",Rect2(216,430,80,24),func():
		if _panel!=null and _panel.session!=null:_panel.session.trade_lock(),"鎖定交易")
	lock.text="鎖定"
	var confirm:=_hit(trade_window,"TradeConfirm",Rect2(306,430,80,24),func():
		if _panel!=null and _panel.session!=null:_panel.session.trade_final_confirm(),"確認交易")
	confirm.text="確認"
	var cancel:=_hit(trade_window,"TradeCancel",Rect2(396,430,80,24),func():
		if _panel!=null and _panel.session!=null:_panel.session.cancel_trade(),"取消交易")
	cancel.text="取消"

func _build_dialog()->void:
	dialog_window=_window("NpcDialogWindow","BG_NPC_MESSAGE_06",Vector2(570,428),Vector2(115,86))
	_dialog_title=_classic_label(dialog_window,"DialogTitle",Rect2(40,34,490,28),13,true)
	_dialog_title.add_theme_color_override("font_color",Color("#5b3514"))
	_dialog_text=_classic_label(dialog_window,"DialogText",Rect2(48,72,474,126),11,false)
	_dialog_text.add_theme_color_override("font_color",Color("#5b3514"))
	_dialog_text.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART
	_dialog_choices.name="DialogChoices"
	_dialog_choices.position=Vector2(48,232)
	_dialog_choices.size=Vector2(474,150)
	_dialog_choices.add_theme_constant_override("separation",4)
	dialog_window.add_child(_dialog_choices)

func _build_settings()->void:
	settings_window=_window("SettingsWindow","BG_SYSTEM",Vector2(291,207),Vector2(255,196))
	var label:=_classic_label(settings_window,"UiLabel",Rect2(9,35,102,20),10,false)
	label.text="介面風格"
	_settings_picker.name="UiProfilePickerSettings"
	_settings_picker.position=Vector2(112,34)
	_settings_picker.size=Vector2(154,23)
	_settings_picker.item_selected.connect(func(index:int):
		var id:=str(_settings_picker.get_item_metadata(index))
		if not id.is_empty():skin_requested.emit(id))
	settings_window.add_child(_settings_picker)
	var hint:=_classic_label(settings_window,"SettingsHint",Rect2(10,70,268,118),10,false)
	hint.text="畫面與介面設定共用 SGTest 系統設定。\n切換 UI 不會重新登入或重載地圖。"
	hint.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART

func bind_session(panel)->void:
	_panel=panel
	_refresh_profiles()

func _refresh_profiles()->void:
	if _settings_picker==null:return
	_settings_picker.clear()
	for profile in UiSkins.available():
		var i:=_settings_picker.item_count
		_settings_picker.add_item(str(profile.name))
		_settings_picker.set_item_metadata(i,str(profile.id))
		if str(profile.id)=="original_ie_hk2":_settings_picker.select(i)

func resize_view(view_size:Vector2)->void:
	if view_size==_last_view:return
	_last_view=view_size
	size=view_size
	var factor:=maxf(0.05,minf(view_size.x/BASE_SIZE.x,view_size.y/BASE_SIZE.y))
	stage.scale=Vector2.ONE*factor
	stage.position=(view_size-BASE_SIZE*factor)*0.5

func set_world_active(active:bool)->void:
	_active=active
	visible=active
	set_process(active)
	if not active:
		_hide_windows()
		_photos.clear()

func apply_snapshot(snapshot:Dictionary)->void:
	_stats=snapshot.duplicate(true)
	if not _active:return
	_refresh_hud()
	_refresh_inventory()
	_refresh_character()
	_refresh_skill()
	_refresh_party()
	_refresh_clan()
	_refresh_shop()
	_refresh_quest()
	_refresh_chat_and_friends()
	_refresh_player_shop()
	_refresh_storage()
	_refresh_bank()
	_refresh_repair()
	_refresh_trade()
	_refresh_dialog()

func _refresh_hud()->void:
	var hp:=int(_stats.get("player_current_hp",0));var hpmax:=maxi(1,int(_stats.get("player_max_hp",0)))
	var mp:=int(_stats.get("player_current_mp",0));var mpmax:=maxi(1,int(_stats.get("player_max_mp",0)))
	_level_label.text=str(int(_stats.get("player_level",0)))
	_hp_label.text="%d / %d"%[hp,hpmax]
	_mp_label.text="%d / %d"%[mp,mpmax]

func _clear_rows(box:Container)->void:
	for child in box.get_children():
		box.remove_child(child)
		child.queue_free()

func _classic_row(box:VBoxContainer,text:String,height:float,callback:=Callable())->Button:
	var button:=Button.new()
	button.text=text
	button.flat=true
	button.alignment=HORIZONTAL_ALIGNMENT_LEFT
	button.custom_minimum_size.y=height
	button.focus_mode=Control.FOCUS_NONE
	button.add_theme_font_size_override("font_size",10)
	button.add_theme_color_override("font_color",Color("#f9f2d6"))
	if callback.is_valid():button.pressed.connect(callback)
	box.add_child(button)
	return button

func _apply_item_grade_color(button:Button,item_id:int)->void:
	if _panel==null or not _panel.item_catalog.has_name_color_for(item_id):
		return
	var color:Color=_panel.item_catalog.name_color_for(item_id)
	for state in ["font_color","font_hover_color","font_pressed_color","font_focus_color","font_hover_pressed_color"]:
		button.add_theme_color_override(state,color)

func _change_inventory_tab(index:int)->void:
	_inventory_tab=clampi(index,0,4)
	var equip_view:=_inventory_tab==1
	if _inventory_item_bg!=null:_inventory_item_bg.visible=not equip_view
	if _inventory_equip_bg!=null:
		_inventory_equip_bg.visible=equip_view
		if equip_view and _inventory_equip_bg.texture==null:
			_inventory_equip_bg.texture=_tex("BG_Equip")
	_equipment_rows.visible=equip_view
	_inventory_rows.position=Vector2(39,325) if equip_view else Vector2(39,59)
	_inventory_rows.size=Vector2(166,165) if equip_view else Vector2(166,425)
	_refresh_inventory()

func _inventory_matches_tab(item:Dictionary,equipped:bool)->bool:
	if equipped:
		return _inventory_tab==1
	if _panel==null:
		return _inventory_tab==0
	var category:=str(_panel.item_catalog.category_for(int(item.get("item_definition_id",0))))
	match _inventory_tab:
		0:return category in ["general","recovery","booster",""]
		1:return category in ["equip","weapon","horseequip"]
		2:return category in ["mecha","armor","mechaequip"]
		3:return category in ["pet","elfin"]
		4:return category in ["horse","horsebattery","ride"]
	return false

func _refresh_inventory()->void:
	_clear_rows(_inventory_rows)
	_clear_rows(_equipment_rows)
	var inventory_items:Dictionary=_stats.get("inventory_items",{})
	var equipment_items:Dictionary=_stats.get("equipment_items",{})
	if _inventory_tab==1:
		var equipped_uids:Array=equipment_items.keys();equipped_uids.sort()
		for i in mini(10,equipped_uids.size()):
			var uid:=int(equipped_uids[i]);var item:Dictionary=equipment_items[equipped_uids[i]]
			var id:=int(item.get("item_definition_id",0))
			var name:=str(_panel.item_catalog.name_for(id)) if _panel!=null else "Item %d"%id
			var row:=_classic_row(_equipment_rows,name,23.0,_classic_item_action.bind(uid,id,true))
			_apply_item_grade_color(row,id)
			row.tooltip_text=(_panel.item_catalog.tooltip_text_for(item)+"\n點擊卸下") if _panel!=null else "點擊卸下｜UID %d"%uid
		if equipped_uids.is_empty():
			_classic_row(_equipment_rows,"目前沒有穿戴裝備",23.0)
		var candidates:Array=[]
		for raw_uid in inventory_items:
			var item:Dictionary=inventory_items[raw_uid]
			if _inventory_matches_tab(item,false):
				candidates.append({"uid":int(raw_uid),"item":item})
		candidates.sort_custom(func(a:Dictionary,b:Dictionary):return int(a.uid)<int(b.uid))
		for i in mini(7,candidates.size()):
			var entry:Dictionary=candidates[i];var uid:=int(entry.uid);var item:Dictionary=entry.item
			var id:=int(item.get("item_definition_id",0))
			var name:=str(_panel.item_catalog.name_for(id)) if _panel!=null else "Item %d"%id
			var row:=_classic_row(_inventory_rows,"     %s ×%d"%[name,int(item.get("quantity",1))],23.0,_classic_item_action.bind(uid,id,false))
			_apply_item_grade_color(row,id)
			if _panel!=null:
				row.tooltip_text=_panel.item_catalog.tooltip_text_for(item,equipment_items)
				var photo:=TextureRect.new();photo.texture=_photos.photo_for_item(id,_panel.item_catalog)
				photo.position=Vector2(1,1);photo.size=Vector2(21,21);photo.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
				photo.stretch_mode=TextureRect.STRETCH_KEEP_ASPECT_CENTERED;photo.mouse_filter=Control.MOUSE_FILTER_IGNORE
				row.add_child(photo)
		if candidates.is_empty():
			_classic_row(_inventory_rows,"沒有可穿戴的裝備",23.0)
		return
	var entries:Array=[]
	for raw_uid in inventory_items:
		var item:Dictionary=inventory_items[raw_uid]
		if _inventory_matches_tab(item,false):
			entries.append({"uid":int(raw_uid),"item":item})
	entries.sort_custom(func(a:Dictionary,b:Dictionary):return int(a.uid)<int(b.uid))
	for i in mini(ITEM_ROW_COUNT,entries.size()):
		var entry:Dictionary=entries[i]
		var uid:=int(entry.uid);var item:Dictionary=entry.item;var id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(id)) if _panel!=null else "Item %d"%id
		var row:=_classic_row(_inventory_rows,"     %s  ×%d"%[name,int(item.get("quantity",1))],24.4)
		_apply_item_grade_color(row,id)
		row.tooltip_text=_panel.item_catalog.tooltip_text_for(item,equipment_items) if _panel!=null else "UID %d · ID %d"%[uid,id]
		if _panel!=null:
			var photo:=TextureRect.new();photo.name="ItemPhoto";photo.texture=_photos.photo_for_item(id,_panel.item_catalog)
			photo.position=Vector2(1,1);photo.size=Vector2(22,22);photo.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
			photo.stretch_mode=TextureRect.STRETCH_KEEP_ASPECT_CENTERED;photo.mouse_filter=Control.MOUSE_FILTER_IGNORE
			row.add_child(photo)
		row.pressed.connect(_classic_item_action.bind(uid,id,false))
	if entries.is_empty():
		_classic_row(_inventory_rows,"此分頁目前沒有可用資料",24.4)

func _classic_item_action(uid:int,item_id:int,equipped:=false)->void:
	if _panel==null:return
	var category:=str(_panel.item_catalog.category_for(item_id))
	if equipped:
		_panel.inventory_panel.submit_equipment_unequip(uid)
	elif category=="recovery":
		_panel.inventory_panel.submit_recovery_use(uid)
	elif category in ["equip","weapon","horseequip"]:
		_panel.inventory_panel.submit_equipment_equip(uid)

func _refresh_character()->void:
	var values={
		"名稱":str(_stats.get("character_name",_stats.get("player_name",""))),
		"等級":str(int(_stats.get("player_level",0))),"職業":str(_stats.get("player_job","—")),
		"力量":str(int(_stats.get("player_strength",0))),"敏捷":str(int(_stats.get("player_dexterity",0))),
		"體質":str(int(_stats.get("player_constitution",0))),"智力":str(int(_stats.get("player_intelligence",0))),
		"攻擊":str(int(_stats.get("player_attack",0))),"防禦":str(int(_stats.get("player_defense",0))),
		"命中":str(int(_stats.get("player_hit",0))),"迴避":str(int(_stats.get("player_evasion",0))),
		"移動速度":str(int(_stats.get("player_move_speed",0))),"攻擊速度":str(int(_stats.get("player_attack_speed",0))),
		"HP":"%d/%d"%[int(_stats.get("player_current_hp",0)),int(_stats.get("player_max_hp",0))],
		"MP":"%d/%d"%[int(_stats.get("player_current_mp",0)),int(_stats.get("player_max_mp",0))],
		"金錢":str(int(_stats.get("wallet",_stats.get("player_money",0)))),"地圖":str(int(_stats.get("map_id",0)))
	}
	for key in _character_labels:(_character_labels[key] as Label).text=str(values.get(key,"—"))

func _refresh_skill()->void:
	_clear_rows(_skill_rows)
	var skills:Dictionary=_stats.get("learned_skills",{}) if _stats.get("learned_skills",{}) is Dictionary else {}
	var ids:Array=skills.keys()
	ids.sort()
	for i in mini(SKILL_ROW_COUNT,ids.size()):
		var id:=int(ids[i]);var level:=int(skills[ids[i]])
		_classic_row(_skill_rows,"技能 %d　Lv.%d"%[id,level],26.5,func():
			if _panel!=null:_panel.skill_input.value=id;_panel.use_skill_button.pressed.emit())
	if ids.is_empty():_classic_row(_skill_rows,"尚無已學技能",26.5)

func _refresh_party()->void:
	_clear_rows(_party_rows)
	var members:Dictionary=_stats.get("party_members",{})
	var ids:Array=members.keys();ids.sort()
	for i in mini(6,ids.size()):
		var id:=int(ids[i]);var member:Dictionary=members[ids[i]]
		_classic_row(_party_rows,"%s%s"%["★ " if id==int(_stats.get("party_leader_actor_id",0)) else "",str(member.get("name","玩家"))],21.5)
	if ids.is_empty():_classic_row(_party_rows,"尚未組隊",21.5)

func _refresh_clan()->void:
	var header=clan_window.get_node_or_null("ClanHeader")
	if header is Label:
		(header as Label).text=("%s\nID %d　團長 %d"%[str(_stats.get("clan_name","")),int(_stats.get("clan_id",0)),int(_stats.get("clan_leader_actor_id",0))]) if int(_stats.get("clan_id",0))>0 else "尚未加入戰團"
	_clear_rows(_clan_rows)
	var members:Dictionary=_stats.get("clan_members",{})
	var ids:Array=members.keys();ids.sort()
	for i in mini(9,ids.size()):
		var id:=int(ids[i]);var member:Dictionary=members[ids[i]]
		_classic_row(_clan_rows,"%s　%s"%["團長" if id==int(_stats.get("clan_leader_actor_id",0)) else "成員",str(member.get("name","玩家"))],20.5)
	if ids.is_empty():_classic_row(_clan_rows,"尚無成員資料",20.5)

func _refresh_shop()->void:
	_clear_rows(_shop_buy_rows);_clear_rows(_shop_sell_rows)
	var shop:Dictionary=_stats.get("shop_items",{})
	var ids:Array=shop.keys();ids.sort()
	for i in mini(17,ids.size()):
		var id:=int(ids[i]);var item:Dictionary=shop[ids[i]]
		var name:=str(_panel.item_catalog.name_for(id)) if _panel!=null else "Item %d"%id
		_classic_row(_shop_buy_rows,"%s　$%d"%[name,int(item.get("buy_price",0))],24.3,func():
			if _panel!=null:_panel.submit_shop_buy(id,1))
	var inv:Dictionary=_stats.get("inventory_items",{})
	var uids:Array=inv.keys();uids.sort();var count:int=0
	for raw in uids:
		if count>=17:break
		var uid:=int(raw);var owned:Dictionary=inv[raw];var id:=int(owned.get("item_definition_id",0))
		if not shop.has(id) or int((shop[id] as Dictionary).get("sell_price",0))<=0:continue
		var name:=str(_panel.item_catalog.name_for(id)) if _panel!=null else "Item %d"%id
		_classic_row(_shop_sell_rows,"%s ×%d"%[name,int(owned.get("quantity",0))],24.3,func():
			if _panel!=null:_panel.submit_shop_sell(uid,1))
		count+=1

func _refresh_quest()->void:
	_clear_rows(_quest_choices)
	var quest:Dictionary=_stats.get("quest_view",{})
	_quest_title.text=str(quest.get("title",""))
	_quest_text.text=str(quest.get("text",""))
	for raw in quest.get("choices",[]):
		var choice:Dictionary=raw;var id:=int(choice.get("choice_id",0))
		_classic_row(_quest_choices,str(choice.get("label","選項")),22.0,func():
			if _panel!=null:_panel.submit_quest_choice(id))

func _send_chat()->void:
	if _panel==null or _panel.session==null:
		return
	var message:=_chat_input.text.strip_edges()
	if message.is_empty():
		return
	if _panel.session.send_nearby_chat(message,0):
		_chat_input.clear()

func _entity_name(actor_id:int,value:Dictionary)->String:
	var name:=str(value.get("name",value.get("character_name","")))
	return name if not name.is_empty() else "玩家 %d"%actor_id

func _refresh_chat_and_friends()->void:
	var messages:Array=_stats.get("chat_messages",[])
	var rows:=PackedStringArray()
	var start:=maxi(0,messages.size()-30)
	for i in range(start,messages.size()):
		var entry:Dictionary=messages[i]
		var channel:=int(entry.get("channel",-1))
		var sender:=str(entry.get("sender_name","玩家 %d"%int(entry.get("sender_actor_id",0))))
		match channel:
			0: rows.append("[附近] %s：%s"%[sender,str(entry.get("message",""))])
			3: rows.append("[隊伍] %s：%s"%[sender,str(entry.get("message",""))])
			4:
				if str(entry.get("direction",""))=="outgoing":
					rows.append("[私訊] 你 → %s：%s"%[str(entry.get("target_name","")),str(entry.get("message",""))])
				else:
					rows.append("[私訊] %s → 你：%s"%[sender,str(entry.get("message",""))])
	_chat_history.text="\n".join(rows)
	if not rows.is_empty():
		_chat_history.set_v_scroll(_chat_history.get_line_count())
	_clear_rows(_friend_rows)
	var entities:Dictionary=_stats.get("entities",{})
	var ids:Array=entities.keys();ids.sort()
	var shown:=0
	for raw_actor in ids:
		var actor_id:=int(raw_actor)
		if actor_id<=0 or actor_id==int(_stats.get("actor_id",0)):
			continue
		var entity:Dictionary=entities[raw_actor]
		_classic_row(_friend_rows,_entity_name(actor_id,entity),16.0,func():
			_chat_input.placeholder_text="私訊功能請由聊天面板指定玩家；此列為目前可見玩家")
		shown+=1
		if shown>=4:break
	if shown==0:
		_classic_row(_friend_rows,"目前沒有可見玩家",16.0)

func _open_player_shop_item(uid:int)->void:
	if _panel!=null and _panel.session!=null:
		_panel.session.open_player_shop(uid,int(_player_shop_price.value))

func _inspect_player_shop(actor_id:int)->void:
	if _panel!=null and _panel.session!=null:
		_panel.session.inspect_player_shop(actor_id)

func _buy_inspected_player_shop()->void:
	if _panel==null or _panel.session==null:
		return
	var listing:Dictionary=_stats.get("player_shop_inspected",{})
	if listing.is_empty():
		return
	_panel.session.buy_player_shop(int(listing.get("seller_actor_id",0)),int(listing.get("listing_id",0)))

func _refresh_player_shop()->void:
	_clear_rows(_player_shop_rows)
	var own:Dictionary=_stats.get("player_shop_own_listing",{})
	var inspected:Dictionary=_stats.get("player_shop_inspected",{})
	if not own.is_empty():
		var own_id:=int(own.get("item_definition_id",0))
		var own_name:=str(_panel.item_catalog.name_for(own_id)) if _panel!=null else "道具 %d"%own_id
		_classic_row(_player_shop_rows,"我的商店：%s ×%d　$%d"%[
			own_name,int(own.get("quantity",0)),int(own.get("price",0))],24.0,func():
				if _panel!=null and _panel.session!=null:_panel.session.close_player_shop(int(own.get("listing_id",0))))
	else:
		var inventory:Dictionary=_stats.get("inventory_items",{})
		var uids:Array=inventory.keys();uids.sort()
		var count:=0
		for raw_uid in uids:
			if count>=8:break
			var uid:=int(raw_uid);var item:Dictionary=inventory[raw_uid]
			var item_id:=int(item.get("item_definition_id",0))
			var item_name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
			_classic_row(_player_shop_rows,"開店：%s ×%d"%[item_name,int(item.get("quantity",0))],22.0,_open_player_shop_item.bind(uid))
			count+=1
	var entities:Dictionary=_stats.get("entities",{})
	var entity_ids:Array=entities.keys();entity_ids.sort()
	for raw_actor in entity_ids:
		var actor_id:=int(raw_actor)
		if actor_id<=0 or actor_id==int(_stats.get("actor_id",0)):
			continue
		_classic_row(_player_shop_rows,"查看 %s 的小商店"%_entity_name(actor_id,entities[raw_actor]),20.0,_inspect_player_shop.bind(actor_id))
	if not inspected.is_empty():
		var item_id:=int(inspected.get("item_definition_id",0))
		var item_name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
		_classic_row(_player_shop_rows,"查看中：%s ×%d　$%d"%[
			item_name,int(inspected.get("quantity",0)),int(inspected.get("price",0))],24.0)
	_player_shop_buy.disabled=inspected.is_empty() or int(_stats.get("pending_player_shop_sequence",0))!=0

func _refresh_storage()->void:
	_clear_rows(_storage_rows)
	_clear_rows(_storage_inventory_rows)
	var ready:=bool(_stats.get("storage_ready",false))
	var active_npc:=int(_stats.get("active_storage_npc",0))
	if not ready or active_npc<=0:
		_classic_row(_storage_rows,"請先與倉庫 NPC 互動",24.4)
		_classic_row(_storage_inventory_rows,"倉庫尚未開啟",24.4)
		return
	var storage:Dictionary=_stats.get("storage_items",{})
	var inventory_items:Dictionary=_stats.get("inventory_items",{})
	var stored_uids:Array=storage.keys();stored_uids.sort()
	for i in mini(17,stored_uids.size()):
		var uid:=int(stored_uids[i]);var item:Dictionary=storage[stored_uids[i]]
		var item_id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
		_classic_row(_storage_rows,"%s ×%d"%[name,int(item.get("quantity",0))],24.4,func():
			if _panel!=null:_panel.submit_storage_withdraw(uid))
	if stored_uids.is_empty():_classic_row(_storage_rows,"倉庫目前沒有物品",24.4)
	var inv_uids:Array=inventory_items.keys();inv_uids.sort()
	for i in mini(17,inv_uids.size()):
		var uid:=int(inv_uids[i]);var item:Dictionary=inventory_items[inv_uids[i]]
		var item_id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
		_classic_row(_storage_inventory_rows,"%s ×%d"%[name,int(item.get("quantity",0))],24.4,func():
			if _panel!=null:_panel.submit_storage_deposit(uid))
	if inv_uids.is_empty():_classic_row(_storage_inventory_rows,"背包目前沒有物品",24.4)

func _refresh_bank()->void:
	var active_npc:=int(_stats.get("active_bank_npc",0))
	if active_npc<=0:
		_bank_status.text="請先與銀行 NPC 互動"
		_bank_amount.editable=false
		return
	var scope:=int(_stats.get("bank_scope",0))
	var wallet:=int(_stats.get("bank_wallet_money",0))
	var balance:=int(_stats.get("bank_money",0))
	_bank_status.text="錢包 %d　銀行 %d　%s"%[wallet,balance,"帳號共用" if scope==2 else "角色專屬"]
	_bank_amount.max_value=maxi(1,maxi(wallet,balance))
	_bank_amount.editable=int(_stats.get("pending_bank_sequence",0))==0

func _refresh_repair()->void:
	_clear_rows(_repair_rows)
	if not bool(_stats.get("repair_ready",false)) or int(_stats.get("active_repair_npc",0))<=0:
		_classic_row(_repair_rows,"請先與修理 NPC 互動",24.0)
		return
	var items:Dictionary=_stats.get("repair_items",{})
	var uids:Array=items.keys();uids.sort()
	for raw_uid in uids:
		var uid:=int(raw_uid);var item:Dictionary=items[raw_uid]
		var item_id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
		_classic_row(_repair_rows,"%s　%d/%d　$%d"%[
			name,int(item.get("current_durability",0)),int(item.get("max_durability",0)),int(item.get("cost",0))],
			24.0,func():if _panel!=null:_panel.submit_repair_item(uid))
	if uids.is_empty():
		_classic_row(_repair_rows,"沒有需要修理的裝備",24.0)

func _trade_state_text(state:int)->String:
	match state:
		1:return "等待接受"
		3:return "交易進行中"
		4:return "等待最終確認"
		_:return "未交易"

func _refresh_trade()->void:
	_clear_rows(_trade_rows)
	var trade_id:=int(_stats.get("trade_id",0))
	var state:=int(_stats.get("trade_state",0))
	if trade_id<=0:
		_trade_status.text="選擇目前可見玩家提出交易"
		var entities:Dictionary=_stats.get("entities",{})
		var ids:Array=entities.keys();ids.sort()
		for raw_actor in ids:
			var actor_id:=int(raw_actor)
			if actor_id<=0 or actor_id==int(_stats.get("actor_id",0)):continue
			_classic_row(_trade_rows,"邀請 %s 交易"%_entity_name(actor_id,entities[raw_actor]),23.0,func():
				if _panel!=null and _panel.session!=null:_panel.session.request_trade(actor_id))
		return
	_trade_status.text="交易 #%d　對象 %d　%s　我/對方金錢 %d/%d"%[
		trade_id,int(_stats.get("trade_peer_actor_id",0)),_trade_state_text(state),
		int(_stats.get("trade_my_money",0)),int(_stats.get("trade_other_money",0))]
	if state==3 and not bool(_stats.get("trade_my_locked",false)):
		var inventory:Dictionary=_stats.get("inventory_items",{})
		var mine:Dictionary=_stats.get("trade_my_items",{})
		var uids:Array=inventory.keys();uids.sort()
		for raw_uid in uids:
			var uid:=int(raw_uid)
			if mine.has(uid):continue
			var item:Dictionary=inventory[raw_uid]
			var item_id:=int(item.get("item_definition_id",0))
			var name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
			_classic_row(_trade_rows,"加入：%s ×%d"%[name,int(item.get("quantity",0))],22.0,func():
				if _panel!=null and _panel.session!=null:_panel.session.trade_add_item(uid))
	var my_items:Dictionary=_stats.get("trade_my_items",{})
	for raw_uid in my_items:
		var item:Dictionary=my_items[raw_uid]
		var item_id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
		_classic_row(_trade_rows,"我方：%s ×%d"%[name,int(item.get("quantity",0))],20.0)
	var other:Dictionary=_stats.get("trade_other_items",{})
	for raw_uid in other:
		var item:Dictionary=other[raw_uid]
		var item_id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
		_classic_row(_trade_rows,"對方：%s ×%d"%[name,int(item.get("quantity",0))],20.0)

func _refresh_dialog()->void:
	var quest:Dictionary=_stats.get("quest_view",{})
	var response_sequence:=int(_stats.get("last_quest_response_sequence",0))
	var key:="%d/%d/%d"%[response_sequence,int(quest.get("quest_id",0)),int(quest.get("progress_revision",0))]
	if quest.is_empty():
		_dialog_key=""
		_clear_rows(_dialog_choices)
		dialog_window.hide()
		return
	_dialog_title.text=str(quest.get("title","NPC 對話"))
	_dialog_text.text=str(quest.get("text",""))
	_clear_rows(_dialog_choices)
	for raw in quest.get("choices",[]):
		var choice:Dictionary=raw;var choice_id:=int(choice.get("choice_id",0))
		var row:=_classic_row(_dialog_choices,str(choice.get("label","選項")),30.0,func():
			if _panel!=null:_panel.submit_quest_choice(choice_id))
		row.add_theme_color_override("font_color",Color("#5b3514"))
	if key!=_dialog_key and response_sequence>0:
		_dialog_key=key
		_hide_windows()
		_ensure_window_art(dialog_window)
		dialog_window.show()

func _hide_windows()->void:
	for window in _windows:window.hide()

func _toggle(window:Control)->void:
	var show:=not window.visible
	_hide_windows()
	if show:
		_ensure_window_art(window)
		window.show()

func _secondary_menu_action(index:int)->void:
	match index:
		0:_toggle(quest_window)
		1:_toggle(shop_window)
		2:_toggle(storage_window)
		3:_toggle(bank_window)
		4:_toggle(repair_window)
		5:_toggle(trade_window)

func _menu_action(index:int)->void:
	match index:
		0:_toggle(clan_window)
		1:_toggle(character)
		2:_toggle(inventory)
		3:_toggle(player_shop_window)
		4:_toggle(settings_window)
