extends PanelContainer
## UI adapter only. ClientSession owns transport/reduction on its own threads.
signal connection_changed(active: bool)
signal world_ready(snapshot: Dictionary)
signal world_snapshot(snapshot: Dictionary)

const Session = preload("res://session/client_session.gd")
const State = preload("res://world/world_state.gd")
const TradePanel = preload("res://trade/trade_panel.gd")
const InventoryPanel = preload("res://inventory/inventory_panel.gd")
const PlayerShopPanel = preload("res://player_shop/player_shop_panel.gd")
const QuestPanel = preload("res://quest/quest_panel.gd")
const ChatPanel = preload("res://chat/chat_panel.gd")
const PartyPanel = preload("res://party/party_panel.gd")
const ItemCatalog = preload("res://inventory/item_catalog.gd")
const BankPanel = preload("res://bank/bank_panel.gd")
var session = Session.new()
var inventory_panel := InventoryPanel.new()
var trade_panel := TradePanel.new()
var player_shop_panel := PlayerShopPanel.new()
var quest_panel := QuestPanel.new()
var chat_panel := ChatPanel.new()
var party_panel := PartyPanel.new()
var item_catalog = ItemCatalog.new()
var bank_panel := BankPanel.new()
var active := false
var trusted_chain: X509Certificate
var tls_name := ""
var host_input := LineEdit.new()
var port_input := SpinBox.new()
var username_input := LineEdit.new()
var password_input := LineEdit.new()
var login_button := Button.new()
var register_button := Button.new()
var logout_button := Button.new()
var characters := OptionButton.new()
var choose_button := Button.new()
var character_name_input := LineEdit.new()
var character_slot_input := SpinBox.new()
var create_character_button := Button.new()
var character_create_status := Label.new()
var respawn_button := Button.new()
var skill_hint := Label.new()
var skill_id_input := SpinBox.new()
var skill_target_mode := CheckButton.new()
var skill_area_button := Button.new()
var skill_status := Label.new()
var pickup_status := Label.new()
var shop_npc_input := SpinBox.new()
var shop_open_button := Button.new()
var shop_money_label := Label.new()
var shop_item_picker := OptionButton.new()
var shop_sell_picker := OptionButton.new()
var shop_quantity := SpinBox.new()
var shop_sell_quantity := SpinBox.new()
var shop_buy_button := Button.new()
var shop_sell_button := Button.new()
var shop_status := Label.new()
var _shop_display_key := ""
var storage_npc_input := SpinBox.new()
var storage_open_button := Button.new()
var storage_deposit_picker := OptionButton.new()
var storage_withdraw_picker := OptionButton.new()
var storage_deposit_button := Button.new()
var storage_withdraw_button := Button.new()
var storage_status := Label.new()
var _storage_display_key := ""
var repair_npc_input := SpinBox.new()
var repair_open_button := Button.new()
var repair_wallet_label := Label.new()
var repair_item_picker := OptionButton.new()
var repair_button := Button.new()
var repair_status := Label.new()
var _repair_display_key := ""
var enhancement_npc_input := SpinBox.new()
var enhancement_open_button := Button.new()
var enhancement_wallet_label := Label.new()
var enhancement_item_picker := OptionButton.new()
var enhancement_button := Button.new()
var enhancement_status := Label.new()
var _enhancement_display_key := ""
var pvp_query_button := Button.new()
var pvp_blue_button := Button.new()
var pvp_white_button := Button.new()
var pvp_status := Label.new()
var pvp_target_picker := OptionButton.new()
var pvp_attack_button := Button.new()
var pvp_skill_button := Button.new()
var pvp_attack_status := Label.new()
var pvp_skill_status := Label.new()
var gm_pvp_state_picker := OptionButton.new()
var gm_pvp_apply_button := Button.new()
var gm_pvp_status := Label.new()
var _pvp_target_display_key := ""
const PVP_ATTACK_STATUS_NAMES = [
	"攻擊成功","地圖／連線世代已變更","重複／過期攻擊序號","不可攻擊自己",
	"此地圖禁止玩家 PvP","攻擊者為受保護藍名","目標為受保護藍名","目標不存在",
	"攻擊者已死亡","目標已死亡","距離過遠","障礙物阻擋","攻擊冷卻中",
	"戰鬥狀態不可用","伺服器忙碌，請稍後重試"]
var status := Label.new()
var _characters: Array = []
var _chosen := false
var _world_epoch := 0
var _poll_elapsed := 0.0
var _latest := {}
var _respawn_queued_after := -1
var _skill_queued_after := -1
var _skill_queued_at_ms := 0
const SKILL_RESULT_NAMES = ["成功","未命中","目標不存在","目標死亡","超出距離","障礙阻擋","冷卻中","無法戰鬥","地圖已變更","重複序號","伺服器未核准技能","MP 不足","範圍目標數超限","此地圖禁止玩家 PvP","攻擊者為受保護藍名","目標為受保護藍名","不可對自己施放","伺服器忙碌，請稍後重試"]


func _ready() -> void:
	custom_minimum_size.x = 360
	# The authenticated gameplay controls exceed the viewport height.
	# Clip and scroll the ONE existing form rather than letting trade/PvP
	# actions extend off-screen or constructing a parallel UI.
	var scroll := ScrollContainer.new()
	scroll.name = "SessionOptionsScroll"
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	scroll.follow_focus = true
	scroll.custom_minimum_size.y = maxf(220.0, get_viewport_rect().size.y - 96.0)
	add_child(scroll)
	get_viewport().size_changed.connect(func():
		scroll.custom_minimum_size.y = maxf(220.0, get_viewport_rect().size.y - 96.0))
	var column := VBoxContainer.new()
	column.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(column)
	var title := Label.new()
	title.text = "星夢 Online — 登入"
	column.add_child(title)
	var grid := GridContainer.new()
	grid.columns = 2
	column.add_child(grid)
	host_input.text = "localhost"
	port_input.min_value = 1
	port_input.max_value = 65535
	port_input.value = 7777
	username_input.max_length = 32
	password_input.secret = true
	password_input.max_length = 256
	for input in [host_input,port_input,username_input,password_input]:
		input.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for pair in [["伺服器",host_input],["連接埠",port_input],["帳號",username_input],["密碼",password_input]]:
		var label := Label.new()
		label.text = pair[0]
		grid.add_child(label)
		grid.add_child(pair[1])
	login_button.text = "登入"
	register_button.text = "建立帳號並登入"
	logout_button.text = "登出／取消"
	choose_button.text = "進入世界"
	character_name_input.placeholder_text = "新角色名稱"
	character_name_input.max_length = 24
	character_slot_input.min_value = 0
	character_slot_input.max_value = 7
	character_slot_input.step = 1
	character_slot_input.value = 0
	create_character_button.text = "建立角色"
	character_create_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	character_create_status.text = ""
	shop_npc_input.min_value = 1
	shop_npc_input.max_value = 2147483647
	shop_npc_input.step = 1
	shop_npc_input.value = 1
	shop_open_button.text = "開啟附近 NPC 商店"
	shop_money_label.text = "金錢：—"
	shop_quantity.min_value = 1
	shop_quantity.max_value = 1000000
	shop_quantity.value = 1
	shop_quantity.step = 1
	shop_sell_quantity.min_value = 1
	shop_sell_quantity.max_value = 1
	shop_sell_quantity.value = 1
	shop_sell_quantity.step = 1
	shop_buy_button.text = "購買選中商品"
	shop_sell_button.text = "販售選中背包物品"
	shop_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [shop_npc_input,shop_open_button,shop_money_label,shop_item_picker,shop_sell_picker,shop_quantity,shop_sell_quantity,shop_buy_button,shop_sell_button,shop_status]:
		control.visible = false
	storage_npc_input.min_value = 1
	storage_npc_input.max_value = 2147483647
	storage_npc_input.step = 1
	storage_npc_input.value = 1
	storage_open_button.text = "開啟附近 NPC 倉庫"
	storage_deposit_button.text = "存入選中背包物品（整組）"
	storage_withdraw_button.text = "取出選中倉庫物品（整組）"
	storage_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [storage_npc_input,storage_open_button,storage_deposit_picker,storage_withdraw_picker,storage_deposit_button,storage_withdraw_button,storage_status]:
		control.visible = false
	repair_npc_input.min_value = 1
	repair_npc_input.max_value = 2147483647
	repair_npc_input.step = 1
	repair_npc_input.value = 1
	repair_open_button.text = "開啟附近 NPC 修理"
	repair_wallet_label.text = "角色錢包：—"
	repair_button.text = "修理選中物品"
	repair_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [repair_npc_input,repair_open_button,repair_wallet_label,repair_item_picker,repair_button,repair_status]:
		control.visible = false
	enhancement_npc_input.min_value = 1
	enhancement_npc_input.max_value = 2147483647
	enhancement_npc_input.step = 1
	enhancement_npc_input.value = 1
	enhancement_open_button.text = "開啟附近 NPC 強化"
	enhancement_wallet_label.text = "角色錢包：—"
	enhancement_button.text = "強化選中裝備"
	enhancement_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [enhancement_npc_input,enhancement_open_button,enhancement_wallet_label,enhancement_item_picker,enhancement_button,enhancement_status]:
		control.visible = false
	respawn_button.text = "復活"
	skill_hint.text = "技能 ID（需伺服器已核准規則）"
	skill_id_input.min_value = 1
	skill_id_input.max_value = 100000000
	skill_id_input.step = 1
	skill_id_input.value = 99001
	skill_target_mode.text = "點怪施放單體技能（關閉則普攻）"
	skill_target_mode.focus_mode = Control.FOCUS_NONE
	skill_area_button.text = "施放自身中心範圍技能"
	skill_area_button.focus_mode = Control.FOCUS_NONE
	skill_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [login_button,register_button,characters,choose_button,character_name_input,character_slot_input,create_character_button,character_create_status,respawn_button,skill_hint,skill_id_input,skill_target_mode,skill_area_button,skill_status,logout_button,status,pickup_status]:
		column.add_child(control)
	column.add_child(inventory_panel)
	for control in [shop_npc_input,shop_open_button,shop_money_label,shop_item_picker,shop_sell_picker,shop_quantity,shop_sell_quantity,shop_buy_button,shop_sell_button,shop_status,storage_npc_input,storage_open_button,storage_deposit_picker,storage_withdraw_picker,storage_deposit_button,storage_withdraw_button,storage_status,repair_npc_input,repair_open_button,repair_wallet_label,repair_item_picker,repair_button,repair_status,enhancement_npc_input,enhancement_open_button,enhancement_wallet_label,enhancement_item_picker,enhancement_button,enhancement_status]:
		column.add_child(control)
	login_button.pressed.connect(login)
	register_button.pressed.connect(register_account)
	password_input.text_submitted.connect(func(_text): login())
	choose_button.pressed.connect(choose)
	create_character_button.pressed.connect(create_character)
	character_name_input.text_submitted.connect(func(_text): create_character())
	respawn_button.pressed.connect(request_respawn)
	skill_area_button.pressed.connect(_cast_area_skill)
	logout_button.pressed.connect(disconnect_session)
	shop_open_button.pressed.connect(_open_shop_from_input)
	shop_buy_button.pressed.connect(_buy_selected_shop_item)
	shop_sell_button.pressed.connect(_sell_selected_shop_item)
	shop_sell_picker.item_selected.connect(func(_index): _update_shop_sell_quantity())
	storage_open_button.pressed.connect(_open_storage_from_input)
	storage_deposit_button.pressed.connect(_deposit_selected_storage_item)
	storage_withdraw_button.pressed.connect(_withdraw_selected_storage_item)
	bank_panel.set_actions(Callable(self,"submit_bank_open"),Callable(self,"submit_bank_deposit"),Callable(self,"submit_bank_withdraw"))
	column.add_child(bank_panel)
	repair_open_button.pressed.connect(_open_repair_from_input)
	repair_button.pressed.connect(_repair_selected_item)
	repair_item_picker.item_selected.connect(func(_index): _update_repair_buttons())
	enhancement_open_button.pressed.connect(_open_enhancement_from_input)
	enhancement_button.pressed.connect(_enhance_selected_item)
	enhancement_item_picker.item_selected.connect(func(_index): _update_enhancement_buttons())
	column.add_child(trade_panel)
	column.add_child(player_shop_panel)
	column.add_child(chat_panel)
	column.add_child(party_panel)
	quest_panel.set_actions(Callable(self, "submit_quest_open"), Callable(self, "submit_quest_choice"))
	column.add_child(quest_panel)
	pvp_query_button.text = "查詢 PvP 名稱模式"
	pvp_blue_button.text = "設為藍名（保護）"
	pvp_white_button.text = "設為白名（允許 PvP 地圖）"
	pvp_attack_button.text = "攻擊選中玩家（Server 權威）"
	pvp_skill_button.text = "技能攻擊選中玩家（使用上方技能 ID）"
	gm_pvp_state_picker.add_item("GM：設為藍名",0)
	gm_pvp_state_picker.add_item("GM：設為白名",1)
	gm_pvp_state_picker.add_item("GM：設為紅名",2)
	gm_pvp_state_picker.add_item("GM：設為粉紅名",3)
	gm_pvp_state_picker.add_item("GM：設為閃爍粉紅名",4)
	gm_pvp_apply_button.text = "GM 套用選中玩家名稱狀態"
	pvp_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	pvp_attack_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	pvp_skill_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [pvp_query_button,pvp_blue_button,pvp_white_button,pvp_status,pvp_target_picker,pvp_attack_button,pvp_skill_button,pvp_attack_status,pvp_skill_status,gm_pvp_state_picker,gm_pvp_apply_button,gm_pvp_status]:
		control.visible = false
		column.add_child(control)
	pvp_query_button.pressed.connect(func(): _request_pvp_mode(0))
	pvp_blue_button.pressed.connect(func(): _request_pvp_mode(1))
	pvp_white_button.pressed.connect(func(): _request_pvp_mode(2))
	pvp_attack_button.pressed.connect(_attack_selected_player)
	pvp_skill_button.pressed.connect(_skill_attack_selected_player)
	gm_pvp_apply_button.pressed.connect(_gm_apply_selected_player)
	var style := StyleBoxFlat.new()
	style.bg_color = Color("211c15")
	style.border_color = Color("c8a452")
	style.set_border_width_all(2)
	for side in ["left","right","top","bottom"]:
		style.set("content_margin_"+side,16)
	add_theme_stylebox_override("panel",style)
	add_theme_color_override("font_color",Color("f2d995"))
	_set_controls()
	status.text = "請登入伺服器"
	pickup_status.text = ""


func login()->void:
	_begin_connection(false)


func register_account()->void:
	_begin_connection(true)


func _begin_connection(register_first:bool)->void:
	if active:
		return
	var username:=username_input.text.strip_edges()
	var password:=password_input.text
	password_input.clear()
	if (
		username.to_utf8_buffer().size()<3 or
		username.to_utf8_buffer().size()>32 or
		password.to_utf8_buffer().size()<8 or
		password.to_utf8_buffer().size()>256 or
		host_input.text.strip_edges().is_empty()
	):
		status.text="帳號需 3–32 bytes，密碼需 8–256 bytes"
		return
	var error:int=session.register_secure(
		host_input.text.strip_edges(),int(port_input.value),username,password,
		tls_name,trusted_chain) if register_first else session.connect_secure(
			host_input.text.strip_edges(),int(port_input.value),username,password,
			tls_name,trusted_chain)
	password=""
	if error != OK:
		status.text = "無法開始連線（"+str(error)+"）"
		return
	active = true
	_chosen = false
	_world_epoch = 0
	_latest = {}
	_respawn_queued_after = -1
	_skill_queued_after = -1
	_skill_queued_at_ms = 0
	skill_target_mode.button_pressed = false
	skill_status.text = ""
	pickup_status.text = ""
	_shop_display_key = ""
	_storage_display_key = ""
	_repair_display_key = ""
	_enhancement_display_key = ""
	chat_panel.reset()
	party_panel.reset()
	quest_panel.reset()
	shop_status.text = ""
	shop_item_picker.clear()
	shop_sell_picker.clear()
	storage_status.text = ""
	storage_deposit_picker.clear()
	storage_withdraw_picker.clear()
	bank_panel.reset()
	repair_status.text = ""
	repair_wallet_label.text = "角色錢包：—"
	repair_item_picker.clear()
	trade_panel.reset()
	player_shop_panel.reset()
	inventory_panel.reset()
	characters.clear()
	_characters = []
	_set_controls()
	status.text = "建立帳號並驗證中…" if register_first else "安全連線中…"
	connection_changed.emit(true)


func create_character() -> void:
	if not active:
		return
	var character_name := character_name_input.text.strip_edges()
	if character_name.is_empty():
		character_create_status.text = "請輸入角色名稱"
		return
	if session.create_character(int(character_slot_input.value),character_name):
		character_create_status.text = "建立角色中…"
		_set_controls()
	else:
		character_create_status.text = "目前無法建立角色，請稍後重試"

func choose() -> void:
	if not active or _chosen or characters.selected < 0 or characters.selected >= _characters.size():
		return
	if session.choose_character(int(_characters[characters.selected].character_id)):
		_chosen = true
		choose_button.disabled = true
		status.text = "進入世界中…"
	else:
		status.text = "操作佇列忙碌，請稍後重試"


func submit_move(point: Vector2) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and point.is_finite() and session.move_target(int(floor(point.x)),int(floor(point.y)))


func submit_stop_movement() -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and not bool(_latest.get("player_dead",false)) and session.stop_movement()


func submit_attack_monster(entity_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and entity_id > 0 and session.attack_monster(entity_id)


# Client chooses only skill identity and target; the server owns rules, cost and damage.
func submit_monster_action(entity_id: int) -> bool:
	if skill_target_mode.button_pressed:
		return submit_target_skill(entity_id)
	return submit_attack_monster(entity_id)


func _skill_can_submit() -> bool:
	return active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD and bool(_latest.get("world_ready",false)) and bool(_latest.get("combat_available",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_world_ready_ack_epoch",0)) == 0 and int(_latest.get("last_world_ready_ack_epoch",0)) == int(_latest.get("map_epoch",0))


func _skill_queued() -> void:
	_skill_queued_after = int(_latest.get("skill_sequence",0))
	_skill_queued_at_ms = Time.get_ticks_msec()
	skill_status.text = "技能請求已排入佇列，等待伺服器結果"
	_set_skill_controls()


func submit_target_skill(entity_id: int) -> bool:
	if not _skill_can_submit() or _skill_queued_after >= 0 or int(_latest.get("pending_skill_sequence",0)) != 0 or entity_id <= 0:
		return false
	if not session.cast_skill(int(skill_id_input.value),entity_id):
		return false
	_skill_queued()
	return true


func submit_area_skill(skill_id: int) -> bool:
	if not _skill_can_submit() or _skill_queued_after >= 0 or int(_latest.get("pending_skill_sequence",0)) != 0 or skill_id <= 0 or skill_id > 100000000:
		return false
	if not session.cast_area_skill(skill_id):
		return false
	_skill_queued()
	return true


func _cast_area_skill() -> void:
	if not skill_area_button.disabled:
		if not submit_area_skill(int(skill_id_input.value)):
			skill_status.text = "技能未送出；請檢查角色狀態或佇列"


func submit_pickup_drop(drop_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and int(_latest.get("pending_pickup_sequence",0)) == 0 and drop_id > 0 and session.pickup_ground_drop(drop_id)


func submit_npc_interact(spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and spawn_id > 0 and session.npc_interact(spawn_id)




func submit_shop_open(npc_spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and npc_spawn_id > 0 and session.open_shop(npc_spawn_id)


func submit_shop_buy(item_definition_id: int, quantity: int = 1) -> bool:
	return active and int(_latest.get("active_shop_npc",0)) > 0 and int(_latest.get("pending_shop_sequence",0)) == 0 and item_definition_id > 0 and quantity > 0 and session.buy_shop_item(item_definition_id,quantity)


func submit_shop_sell(item_uid: int, quantity: int = 1) -> bool:
	return active and int(_latest.get("active_shop_npc",0)) > 0 and int(_latest.get("pending_shop_sequence",0)) == 0 and item_uid > 0 and quantity > 0 and quantity <= 1000000 and (_latest.get("inventory_items",{}) as Dictionary).has(item_uid) and quantity <= int(((_latest.get("inventory_items",{}) as Dictionary)[item_uid] as Dictionary).get("quantity",0)) and session.sell_shop_item(item_uid,quantity)


func submit_storage_open(npc_spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and npc_spawn_id > 0 and int(_latest.get("pending_storage_sequence",0)) == 0 and session.open_storage(npc_spawn_id)


func submit_storage_deposit(item_uid: int) -> bool:
	return active and bool(_latest.get("storage_ready",false)) and int(_latest.get("active_storage_npc",0)) > 0 and int(_latest.get("pending_storage_sequence",0)) == 0 and item_uid > 0 and (_latest.get("inventory_items",{}) as Dictionary).has(item_uid) and session.deposit_storage_item(item_uid)


func submit_storage_withdraw(item_uid: int) -> bool:
	return active and bool(_latest.get("storage_ready",false)) and int(_latest.get("active_storage_npc",0)) > 0 and int(_latest.get("pending_storage_sequence",0)) == 0 and item_uid > 0 and (_latest.get("storage_items",{}) as Dictionary).has(item_uid) and session.withdraw_storage_item(item_uid)


func submit_bank_open(npc_spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_bank_sequence",0)) == 0 and npc_spawn_id > 0 and session.open_bank(npc_spawn_id)


func submit_bank_deposit(amount: int) -> bool:
	return active and int(_latest.get("active_bank_npc",0)) > 0 and int(_latest.get("bank_scope",0)) in [1,2] and int(_latest.get("pending_bank_sequence",0)) == 0 and amount > 0 and amount <= int(_latest.get("bank_wallet_money",0)) and session.deposit_bank_money(amount)


func submit_bank_withdraw(amount: int) -> bool:
	return active and int(_latest.get("active_bank_npc",0)) > 0 and int(_latest.get("bank_scope",0)) in [1,2] and int(_latest.get("pending_bank_sequence",0)) == 0 and amount > 0 and amount <= int(_latest.get("bank_money",0)) and session.withdraw_bank_money(amount)


func submit_repair_open(npc_spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_repair_sequence",0)) == 0 and npc_spawn_id > 0 and session.open_repair(npc_spawn_id)


func submit_repair_item(item_uid: int) -> bool:
	return active and bool(_latest.get("repair_ready",false)) and int(_latest.get("active_repair_npc",0)) > 0 and int(_latest.get("pending_repair_sequence",0)) == 0 and item_uid > 0 and (_latest.get("repair_items",{}) as Dictionary).has(item_uid) and session.repair_item(item_uid)


func submit_enhancement_open(npc_spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_enhancement_sequence",0)) == 0 and int(_latest.get("pending_repair_sequence",0)) == 0 and npc_spawn_id > 0 and session.open_enhancement(npc_spawn_id)


func submit_enhancement_item(item_uid: int) -> bool:
	return active and bool(_latest.get("enhancement_ready",false)) and int(_latest.get("active_enhancement_npc",0)) > 0 and int(_latest.get("pending_enhancement_sequence",0)) == 0 and int(_latest.get("pending_repair_sequence",0)) == 0 and item_uid > 0 and (_latest.get("enhancement_items",{}) as Dictionary).has(item_uid) and session.enhance_item(item_uid)


func submit_pvp_attack(target_actor_id: int) -> bool:
	if not active or int(_latest.get("state",-1)) != State.STATE_IN_WORLD or not bool(_latest.get("world_ready",false)) or not bool(_latest.get("combat_available",false)) or bool(_latest.get("player_dead",false)) or bool(_latest.get("reconnecting",false)) or bool(_latest.get("resync_pending",false)) or bool(_latest.get("waiting_for_new_map",false)) or not bool(_latest.get("pvp_known",false)) or not bool(_latest.get("pvp_map_allows",false)) or int(_latest.get("pending_pvp_attack_sequence",0)) != 0:
		return false
	var entities: Dictionary = _latest.get("entities",{})
	if target_actor_id <= 0 or target_actor_id == int(_latest.get("actor_id",0)) or not entities.has(target_actor_id):
		return false
	return session.attack_player(target_actor_id)


func submit_pvp_skill(target_actor_id:int,skill_id:int)->bool:
	if not active or skill_id<=0 or skill_id>100000000 or int(_latest.get("state",-1))!=State.STATE_IN_WORLD or not bool(_latest.get("world_ready",false)) or not bool(_latest.get("combat_available",false)) or bool(_latest.get("player_dead",false)) or bool(_latest.get("reconnecting",false)) or bool(_latest.get("resync_pending",false)) or bool(_latest.get("waiting_for_new_map",false)) or not bool(_latest.get("pvp_known",false)) or not bool(_latest.get("pvp_map_allows",false)) or int(_latest.get("pending_skill_sequence",0))!=0:
		return false
	var entities:Dictionary=_latest.get("entities",{})
	if target_actor_id<=0 or target_actor_id==int(_latest.get("actor_id",0)) or not entities.has(target_actor_id):
		return false
	return session.cast_player_skill(skill_id,target_actor_id)


func submit_quest_open(npc_spawn_id:int)->bool:
	return (
		active and npc_spawn_id>0 and
		int(_latest.get("state",-1))==State.STATE_IN_WORLD and
		bool(_latest.get("world_ready",false)) and
		not bool(_latest.get("player_dead",false)) and
		not bool(_latest.get("reconnecting",false)) and
		not bool(_latest.get("resync_pending",false)) and
		not bool(_latest.get("waiting_for_new_map",false)) and
		int(_latest.get("pending_quest_sequence",0))==0 and
		session.open_quest(npc_spawn_id)
	)


func submit_quest_choice(choice_id:int)->bool:
	var view:Dictionary=_latest.get("quest_view",{})
	if (
		not active or choice_id<=0 or view.is_empty() or
		int(_latest.get("pending_quest_sequence",0))!=0 or
		bool(_latest.get("reconnecting",false)) or
		bool(_latest.get("resync_pending",false)) or
		bool(_latest.get("waiting_for_new_map",false)) or
		bool(_latest.get("player_dead",false))
	):
		return false
	for raw in view.get("choices",[]):
		if int((raw as Dictionary).get("choice_id",0))==choice_id:
			return session.choose_quest_choice(choice_id)
	return false


func request_respawn() -> void:
	if respawn_button.disabled or not respawn_button.visible or not active:
		return
	if session.respawn():
		# The command first enters the bounded ClientSession intent queue.
		# Guard the UI until WorldState has observed the sent sequence/result.
		_respawn_queued_after = int(_latest.get("respawn_sequence",0))
		respawn_button.disabled = true
		status.text = "復活請求已送出…"
	else:
		status.text = "復活請求尚未送出，請重試"


func disconnect_session() -> void:
	session.stop()
	var was_active := active
	active = false
	_chosen = false
	_world_epoch = 0
	_latest = {}
	_respawn_queued_after = -1
	_skill_queued_after = -1
	_skill_queued_at_ms = 0
	skill_target_mode.button_pressed = false
	skill_status.text = ""
	pickup_status.text = ""
	_shop_display_key = ""
	_storage_display_key = ""
	_repair_display_key = ""
	_enhancement_display_key = ""
	chat_panel.reset()
	party_panel.reset()
	quest_panel.reset()
	shop_status.text = ""
	shop_item_picker.clear()
	shop_sell_picker.clear()
	storage_status.text = ""
	storage_deposit_picker.clear()
	storage_withdraw_picker.clear()
	bank_panel.reset()
	repair_status.text = ""
	repair_wallet_label.text = "角色錢包：—"
	repair_item_picker.clear()
	trade_panel.reset()
	player_shop_panel.reset()
	inventory_panel.reset()
	_characters = []
	characters.clear()
	character_name_input.clear()
	character_create_status.text = ""
	password_input.clear()
	_set_controls()
	status.text = "已登出"
	if was_active:
		connection_changed.emit(false)


func _set_controls() -> void:
	host_input.editable = not active
	username_input.editable = not active
	password_input.editable = not active
	port_input.editable = not active
	login_button.disabled = active
	register_button.disabled = active
	logout_button.disabled = not active
	var state := int(_latest.get("state",-1))
	var selecting := active and state == State.STATE_SELECTING_CHARACTER
	var creating := active and state == State.STATE_CREATING_CHARACTER
	characters.visible = selecting and not _characters.is_empty()
	choose_button.visible = characters.visible
	choose_button.disabled = _chosen or not selecting or _characters.is_empty()
	for control in [character_name_input,character_slot_input,create_character_button,character_create_status]:
		control.visible = active and state in [State.STATE_SELECTING_CHARACTER,State.STATE_CREATING_CHARACTER]
	character_name_input.editable = selecting
	character_slot_input.editable = selecting
	create_character_button.disabled = not selecting
	_set_respawn_controls()
	_set_skill_controls()


func _set_respawn_controls() -> void:
	var dead: bool = active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD and bool(_latest.get("world_ready",false)) and bool(_latest.get("combat_available",false)) and bool(_latest.get("player_dead",false))
	if not dead:
		_respawn_queued_after = -1
	var sent_sequence := int(_latest.get("respawn_sequence",0))
	var result_sequence := int(_latest.get("last_respawn_result_sequence",0))
	if _respawn_queued_after >= 0 and sent_sequence > _respawn_queued_after and result_sequence >= sent_sequence:
		_respawn_queued_after = -1
	respawn_button.visible = dead
	respawn_button.disabled = not dead or bool(_latest.get("reconnecting",false)) or bool(_latest.get("resync_pending",false)) or bool(_latest.get("waiting_for_new_map",false)) or int(_latest.get("pending_world_ready_ack_epoch",0)) != 0 or int(_latest.get("last_world_ready_ack_epoch",0)) != int(_latest.get("map_epoch",0)) or sent_sequence > result_sequence or _respawn_queued_after >= 0


# Presentation-only pending guard; no gameplay timer or local skill settlement.
func _set_skill_controls() -> void:
	var in_world: bool = active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	# Only a new world/connection generation or the matching server result
	# clears an unsettled skill. Same-epoch resync does not settle MP.
	if not in_world or bool(_latest.get("reconnecting",false)) or bool(_latest.get("waiting_for_new_map",false)):
		_skill_queued_after = -1
	if _skill_queued_after >= 0:
		var sent := int(_latest.get("skill_sequence",0))
		var received := int(_latest.get("last_skill_result_sequence",0))
		if sent > _skill_queued_after and received >= sent and int(_latest.get("pending_skill_sequence",0)) == 0:
			_skill_queued_after = -1
			var code := int(_latest.get("last_skill_result_status",-1))
			skill_status.text = "伺服器技能結果：%s（代碼 %d）" % [_skill_result_name(code),code]
		elif Time.get_ticks_msec() - _skill_queued_at_ms >= 10000:
			skill_status.text = "技能結果尚未確認，請勿重複施放；等待伺服器回覆或重新連線"
	for control in [skill_hint,skill_id_input,skill_target_mode,skill_area_button,skill_status]:
		control.visible = in_world
	var enabled: bool = _skill_can_submit() and _skill_queued_after < 0 and int(_latest.get("pending_skill_sequence",0)) == 0
	skill_id_input.editable = enabled
	skill_target_mode.disabled = not enabled
	skill_area_button.disabled = not enabled


# Display-only mapping of existing Server SkillUseStatus codes.
func _skill_result_name(code: int) -> String:
	return SKILL_RESULT_NAMES[code] if code >= 0 and code < SKILL_RESULT_NAMES.size() else "未知結果"


func _combat_status_suffix() -> String:
	if not bool(_latest.get("combat_available",false)):
		return ""
	return "｜HP %d/%d｜MP %d/%d" % [int(_latest.get("player_current_hp",0)),int(_latest.get("player_max_hp",0)),int(_latest.get("player_current_mp",0)),int(_latest.get("player_max_mp",0))]


func _open_shop_from_input() -> void:
	if submit_shop_open(int(shop_npc_input.value)):
		shop_status.text = "正在向伺服器確認 NPC 商店…"
		shop_open_button.disabled = true
	else:
		shop_status.text = "目前無法開啟商店"


func _buy_selected_shop_item() -> void:
	if shop_buy_button.disabled or shop_item_picker.selected < 0:
		return
	var item_id := int(shop_item_picker.get_item_metadata(shop_item_picker.selected))
	var quantity := int(shop_quantity.value)
	if submit_shop_buy(item_id,quantity):
		shop_status.text = "購買請求已送出，等待伺服器結算…"
		shop_buy_button.disabled = true
		shop_sell_button.disabled = true
	else:
		shop_status.text = "目前無法購買該商品"


func _sell_selected_shop_item() -> void:
	if shop_sell_button.disabled or shop_sell_picker.selected < 0:
		return
	var uid := int(shop_sell_picker.get_item_metadata(shop_sell_picker.selected))
	var quantity := int(shop_sell_quantity.value)
	if submit_shop_sell(uid,quantity):
		shop_status.text = "販售請求已送出，等待伺服器結算…"
		shop_buy_button.disabled = true
		shop_sell_button.disabled = true
	else:
		shop_status.text = "目前無法販售該物品"


func _update_shop_sell_quantity() -> void:
	var uid := 0
	if shop_sell_picker.selected >= 0:
		uid = int(shop_sell_picker.get_item_metadata(shop_sell_picker.selected))
	var item: Dictionary = (_latest.get("inventory_items",{}) as Dictionary).get(uid,{})
	# Buying is independent of owned inventory. Only the sell input is
	# bounded by the selected authoritative UID stack (and wire limit).
	var owned := maxi(0,int(item.get("quantity",0)))
	shop_sell_quantity.max_value = maxi(1,mini(1000000,owned))
	shop_sell_quantity.value = mini(shop_sell_quantity.value,shop_sell_quantity.max_value)


func _update_shop_ui() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	for control in [shop_npc_input,shop_open_button,shop_money_label,shop_item_picker,shop_sell_picker,shop_quantity,shop_sell_quantity,shop_buy_button,shop_sell_button,shop_status]:
		control.visible = in_world
	if not in_world:
		_shop_display_key = ""
		return
	var pending := int(_latest.get("pending_shop_sequence",0)) != 0
	var usable := bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false))
	shop_npc_input.editable = usable and not pending
	shop_open_button.disabled = not usable or pending
	var active_npc := int(_latest.get("active_shop_npc",0))
	var shop_items: Dictionary = _latest.get("shop_items",{})
	shop_money_label.text = ("金錢：%d" % int(_latest.get("shop_money",0))) if active_npc > 0 else "金錢：—"
	var key := "%d/%d/%d/%d/%d" % [
		active_npc,int(_latest.get("shop_money",0)),
		int(_latest.get("inventory_revision",0)),
		int(_latest.get("shop_sequence",0)),shop_items.size()]
	if key != _shop_display_key:
		_shop_display_key = key
		shop_item_picker.clear()
		var ids: Array = shop_items.keys()
		ids.sort()
		for raw_id in ids:
			var item_id := int(raw_id)
			var item: Dictionary = shop_items[raw_id]
			shop_item_picker.add_item("%s｜買 %d｜賣 %d" % [
				item_catalog.name_for(item_id),
				int(item.get("buy_price",0)),int(item.get("sell_price",0))])
			shop_item_picker.set_item_metadata(shop_item_picker.item_count-1,item_id)
		shop_sell_picker.clear()
		var inventory: Dictionary = _latest.get("inventory_items",{})
		var uids: Array = inventory.keys()
		uids.sort()
		for raw_uid in uids:
			var uid := int(raw_uid)
			var owned: Dictionary = inventory[raw_uid]
			var item_id := int(owned.get("item_definition_id",0))
			if not shop_items.has(item_id) or int((shop_items[item_id] as Dictionary).get("sell_price",0)) <= 0:
				continue
			shop_sell_picker.add_item("%s ×%d｜UID %d" % [
				item_catalog.name_for(item_id),int(owned.get("quantity",0)),uid])
			shop_sell_picker.set_item_metadata(shop_sell_picker.item_count-1,uid)
		if shop_item_picker.item_count > 0:
			shop_item_picker.select(0)
		if shop_sell_picker.item_count > 0:
			shop_sell_picker.select(0)
			_update_shop_sell_quantity()
	var open_status := int(_latest.get("last_shop_open_status",-1))
	if open_status > 0 and active_npc == 0:
		shop_status.text = "商店無法開啟（伺服器代碼 %d）" % open_status
	var result: Dictionary = _latest.get("last_shop_result",{})
	if not result.is_empty():
		if int(result.get("status",-1)) in [0,1]:
			shop_status.text = "%s完成：%s ×%d｜餘額 %d" % [
				"購買" if int(result.get("kind",0)) == 1 else "販售",
				item_catalog.name_for(int(result.get("item_definition_id",0))),
				int(result.get("quantity",0)),int(result.get("money",0))]
		else:
			shop_status.text = "交易失敗（伺服器代碼 %d）" % int(result.get("status",-1))
	shop_buy_button.disabled = not usable or pending or active_npc <= 0 or shop_item_picker.item_count == 0
	shop_sell_button.disabled = not usable or pending or active_npc <= 0 or shop_sell_picker.item_count == 0
	shop_quantity.editable = usable and not pending and active_npc > 0
	shop_sell_quantity.editable = usable and not pending and active_npc > 0 and shop_sell_picker.item_count > 0


func _open_storage_from_input() -> void:
	if submit_storage_open(int(storage_npc_input.value)):
		storage_status.text = "正在向伺服器確認 NPC 倉庫…"
		storage_open_button.disabled = true
	else:
		storage_status.text = "目前無法開啟倉庫"


func _deposit_selected_storage_item() -> void:
	if storage_deposit_button.disabled or storage_deposit_picker.selected < 0:
		return
	var uid := int(storage_deposit_picker.get_item_metadata(storage_deposit_picker.selected))
	if submit_storage_deposit(uid):
		storage_status.text = "整組存入請求已送出，等待伺服器結算…"
		storage_deposit_button.disabled = true
		storage_withdraw_button.disabled = true
	else:
		storage_status.text = "目前無法存入該物品"


func _withdraw_selected_storage_item() -> void:
	if storage_withdraw_button.disabled or storage_withdraw_picker.selected < 0:
		return
	var uid := int(storage_withdraw_picker.get_item_metadata(storage_withdraw_picker.selected))
	if submit_storage_withdraw(uid):
		storage_status.text = "整組取出請求已送出，等待伺服器結算…"
		storage_deposit_button.disabled = true
		storage_withdraw_button.disabled = true
	else:
		storage_status.text = "目前無法取出該物品"


func _update_storage_ui() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	for control in [storage_npc_input,storage_open_button,storage_deposit_picker,storage_withdraw_picker,storage_deposit_button,storage_withdraw_button,storage_status]:
		control.visible = in_world
	if not in_world:
		_storage_display_key = ""
		return
	var pending := int(_latest.get("pending_storage_sequence",0)) != 0
	var usable := bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false))
	storage_npc_input.editable = usable and not pending
	storage_open_button.disabled = not usable or pending
	var active_npc := int(_latest.get("active_storage_npc",0))
	var ready := bool(_latest.get("storage_ready",false))
	var storage: Dictionary = _latest.get("storage_items",{})
	var inventory: Dictionary = _latest.get("inventory_items",{})
	var key := "%d/%d/%d/%d/%d/%d" % [
		active_npc,int(_latest.get("inventory_revision",0)),
		int(_latest.get("storage_revision",0)),
		int(_latest.get("storage_sequence",0)),
		inventory.size(),storage.size()]
	if key != _storage_display_key:
		_storage_display_key = key
		storage_deposit_picker.clear()
		storage_withdraw_picker.clear()
		if ready and active_npc > 0:
			var inventory_uids: Array = inventory.keys()
			inventory_uids.sort()
			for raw_uid in inventory_uids:
				var uid := int(raw_uid)
				var item: Dictionary = inventory[raw_uid]
				var item_id := int(item.get("item_definition_id",0))
				storage_deposit_picker.add_item("%s ×%d｜UID %d" % [
					item_catalog.name_for(item_id),int(item.get("quantity",0)),uid])
				storage_deposit_picker.set_item_metadata(storage_deposit_picker.item_count-1,uid)
			var stored_uids: Array = storage.keys()
			stored_uids.sort()
			for raw_uid in stored_uids:
				var uid := int(raw_uid)
				var item: Dictionary = storage[raw_uid]
				var item_id := int(item.get("item_definition_id",0))
				storage_withdraw_picker.add_item("%s ×%d｜UID %d" % [
					item_catalog.name_for(item_id),int(item.get("quantity",0)),uid])
				storage_withdraw_picker.set_item_metadata(storage_withdraw_picker.item_count-1,uid)
			if storage_deposit_picker.item_count > 0:
				storage_deposit_picker.select(0)
			if storage_withdraw_picker.item_count > 0:
				storage_withdraw_picker.select(0)
	var open_status := int(_latest.get("last_storage_open_status",-1))
	if open_status > 0 and not ready:
		storage_status.text = "倉庫無法開啟（伺服器代碼 %d）" % open_status
	var result: Dictionary = _latest.get("last_storage_result",{})
	if not result.is_empty():
		if int(result.get("status",-1)) in [0,1]:
			storage_status.text = "%s完成：%s ×%d｜UID %d" % [
				"存入" if int(result.get("kind",0)) == 1 else "取出",
				item_catalog.name_for(int(result.get("item_definition_id",0))),
				int(result.get("quantity",0)),int(result.get("item_uid",0))]
		else:
			storage_status.text = "倉庫操作失敗（伺服器代碼 %d）" % int(result.get("status",-1))
	storage_deposit_button.disabled = not usable or pending or not ready or active_npc <= 0 or storage_deposit_picker.item_count == 0
	storage_withdraw_button.disabled = not usable or pending or not ready or active_npc <= 0 or storage_withdraw_picker.item_count == 0


func _update_nearby_chat_ui() -> void:
	chat_panel.apply_snapshot(_latest, active, session)
	party_panel.apply_snapshot(_latest, active, session)


func _update_quest_ui() -> void:
	quest_panel.apply_snapshot(_latest, active)


func _request_pvp_mode(action: int) -> void:
	if session.request_pvp_mode(action):
		pvp_status.text = "PvP 模式請求已送出，等待伺服器權威回覆…"
	else:
		pvp_status.text = "無法送出：請確認角色、地圖及其他 PvP 請求"


func _attack_selected_player() -> void:
	if pvp_attack_button.disabled or pvp_target_picker.selected < 0:
		return
	var target := int(pvp_target_picker.get_item_metadata(pvp_target_picker.selected))
	if submit_pvp_attack(target):
		pvp_attack_status.text = "PvP 攻擊已送出，等待伺服器判定距離、保護狀態與傷害…"
		pvp_attack_button.disabled = true
	else:
		pvp_attack_status.text = "目前無法攻擊該玩家"


func _skill_attack_selected_player() -> void:
	if pvp_skill_button.disabled or pvp_target_picker.selected<0:
		return
	var target:=int(pvp_target_picker.get_item_metadata(pvp_target_picker.selected))
	var skill_id:=int(skill_id_input.value)
	if submit_pvp_skill(target,skill_id):
		pvp_skill_status.text="玩家技能已送出，等待伺服器判定規則、MP、距離與傷害…"
		pvp_skill_button.disabled=true
	else:
		pvp_skill_status.text="目前無法對該玩家施放技能"


func _gm_apply_selected_player()->void:
	if (
		gm_pvp_apply_button.disabled or pvp_target_picker.selected<0 or
		gm_pvp_state_picker.selected<0
	):
		return
	var target:=int(pvp_target_picker.get_item_metadata(pvp_target_picker.selected))
	var state:=int(gm_pvp_state_picker.get_item_id(gm_pvp_state_picker.selected))
	if submit_gm_pvp_name(target,state):
		gm_pvp_status.text="GM 指令已送出；等待伺服器 audit commit 與 AOI 名稱更新…"
		gm_pvp_apply_button.disabled=true
	else:
		gm_pvp_status.text="GM 指令未送出；權限、目標或連線狀態無效"


func submit_gm_pvp_name(target_actor_id:int,name_state:int)->bool:
	if (
		not active or not bool(_latest.get("gm_authorized",false)) or
		int(_latest.get("state",-1))!=State.STATE_IN_WORLD or
		not bool(_latest.get("world_ready",false)) or
		bool(_latest.get("reconnecting",false)) or
		bool(_latest.get("resync_pending",false)) or
		bool(_latest.get("waiting_for_new_map",false)) or
		int(_latest.get("pending_gm_sequence",0))!=0
	):
		return false
	var entities:Dictionary=_latest.get("entities",{})
	if (
		target_actor_id<=0 or target_actor_id==int(_latest.get("actor_id",0)) or
		not entities.has(target_actor_id) or name_state<0 or name_state>4
	):
		return false
	return session.gm_set_pvp_name(target_actor_id,name_state)


func _pvp_attack_status_text(code: int) -> String:
	return PVP_ATTACK_STATUS_NAMES[code] if code >= 0 and code < PVP_ATTACK_STATUS_NAMES.size() else "未知 PvP 結果"


func _update_pvp_ui() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	for control in [pvp_query_button,pvp_blue_button,pvp_white_button,pvp_status,pvp_target_picker,pvp_attack_button,pvp_skill_button,pvp_attack_status,pvp_skill_status]:
		control.visible = in_world
	var gm_visible:=in_world and bool(_latest.get("gm_authorized",false))
	for control in [gm_pvp_state_picker,gm_pvp_apply_button,gm_pvp_status]:
		control.visible=gm_visible
	if not in_world:
		_pvp_target_display_key = ""
		return
	var ready := bool(_latest.get("world_ready",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_world_ready_ack_epoch",0)) == 0 and int(_latest.get("last_world_ready_ack_epoch",0)) == int(_latest.get("map_epoch",0))
	var pending := int(_latest.get("pending_pvp_sequence",0)) != 0
	var known := bool(_latest.get("pvp_known",false))
	var dead := bool(_latest.get("player_dead",false))
	var map_allows := bool(_latest.get("pvp_map_allows",false))
	pvp_query_button.disabled = not ready or pending
	pvp_blue_button.disabled = not ready or pending or not known or dead or int(_latest.get("pvp_mode",-1)) == 0
	pvp_white_button.disabled = not ready or pending or not known or dead or not map_allows or int(_latest.get("pvp_mode",-1)) == 1
	if pending:
		pvp_status.text = "等待伺服器 PvP 模式確認；不可重複送出"
	elif known:
		var mode := "白名（PvP 開啟）" if int(_latest.get("pvp_mode",-1)) == 1 else "藍名（保護）"
		var map_text := "此圖允許玩家 PvP" if map_allows else "此圖禁止玩家 PvP"
		var result: Dictionary = _latest.get("last_pvp_result",{})
		pvp_status.text = "%s｜%s｜伺服器結果 %d" % [mode,map_text,int(result.get("status",-1))]
	else:
		pvp_status.text = "PvP 模式尚未查詢；名稱顏色由伺服器判定"

	var entity_ids: Array = (_latest.get("entities",{}) as Dictionary).keys()
	entity_ids.sort()
	var identities:Dictionary=_latest.get("player_identities",{})
	var target_key := "%d/%d/%d" % [int(_latest.get("actor_id",0)),int(_latest.get("map_epoch",0)),entity_ids.size()]
	for raw_actor in entity_ids:
		var target_id:=int(raw_actor)
		var identity:Dictionary=identities.get(target_id,{})
		target_key += ",%d:%d:%s" % [
			target_id,int(identity.get("pvp_updated_unix_ms",0)),
			str(identity.get("display_name",""))]
	if target_key != _pvp_target_display_key:
		_pvp_target_display_key = target_key
		pvp_target_picker.clear()
		for raw_actor in entity_ids:
			var target_id := int(raw_actor)
			if target_id <= 0 or target_id == int(_latest.get("actor_id",0)):
				continue
			var target_identity:Dictionary=identities.get(target_id,{})
			var target_name:=str(target_identity.get("display_name","")).strip_edges()
			pvp_target_picker.add_item(
				("%s（%d）" % [target_name,target_id])
				if not target_name.is_empty() else "可見玩家 %d" % target_id)
			pvp_target_picker.set_item_metadata(pvp_target_picker.item_count-1,target_id)
		if pvp_target_picker.item_count > 0:
			pvp_target_picker.select(0)

	var gm_pending:=int(_latest.get("pending_gm_sequence",0))!=0
	gm_pvp_apply_button.disabled=not gm_visible or not ready or gm_pending or pvp_target_picker.item_count==0
	if gm_visible:
		if gm_pending:
			gm_pvp_status.text="等待伺服器 GM audit commit；不可重複送出"
		else:
			var gm_result:Dictionary=_latest.get("last_gm_result",{})
			if gm_result.is_empty():
				gm_pvp_status.text="GM 權限由伺服器簽發；選擇 AOI 玩家與名稱狀態"
			else:
				var gm_code:=int(gm_result.get("status",-1))
				var gm_text: String = (["已套用","重播：未重複修改","無權限","世代已變更","過期序號","目標已離線"][gm_code] as String) if gm_code>=0 and gm_code<=5 else "未知結果"
				gm_pvp_status.text="%s（代碼 %d）；名稱顯示仍以 Server AOI identity 為準" % [gm_text,gm_code]

	var attack_pending := int(_latest.get("pending_pvp_attack_sequence",0)) != 0
	pvp_attack_button.disabled = not ready or not known or not map_allows or dead or not bool(_latest.get("combat_available",false)) or attack_pending or pvp_target_picker.item_count == 0
	var skill_pending:=int(_latest.get("pending_skill_sequence",0))!=0
	pvp_skill_button.disabled=not ready or not known or not map_allows or dead or not bool(_latest.get("combat_available",false)) or skill_pending or pvp_target_picker.item_count==0 or int(skill_id_input.value)<=0
	var attack_result: Dictionary = _latest.get("last_pvp_attack_result",{})
	if attack_pending:
		pvp_attack_status.text = "等待伺服器 PvP 攻擊結算；不可重複攻擊"
	elif not attack_result.is_empty():
		var code := int(attack_result.get("status",-1))
		if code == 0:
			pvp_attack_status.text = "%s｜傷害 %d%s" % [_pvp_attack_status_text(code),int(attack_result.get("damage",0)),"｜目標死亡" if bool(attack_result.get("died",false)) else ""]
		else:
			pvp_attack_status.text = "%s（代碼 %d）" % [_pvp_attack_status_text(code),code]
	elif not map_allows:
		pvp_attack_status.text = "此地圖禁止玩家 PvP"
	else:
		pvp_attack_status.text = "選擇 AOI 內玩家後可送出攻擊；傷害與罪名由伺服器判定"

	var skill_result:Dictionary=_latest.get("last_skill_result",{})
	if skill_pending and int(_latest.get("pending_skill_target_kind",0))==1:
		pvp_skill_status.text="等待伺服器玩家技能結算；不可重複施放"
	elif str(skill_result.get("target_kind",""))=="player":
		var code:=int(_latest.get("last_skill_result_status",-1))
		pvp_skill_status.text=("%s｜傷害 %d%s" % [
			SKILL_RESULT_NAMES[code] if code>=0 and code<SKILL_RESULT_NAMES.size() else "玩家技能結果",
			int(skill_result.get("damage",0)),
			"｜目標死亡" if bool(skill_result.get("died",false)) else ""]) if code==0 else "玩家技能結果（代碼 %d）" % code
	elif not map_allows:
		pvp_skill_status.text="此地圖禁止玩家 PvP"
	else:
		pvp_skill_status.text="使用既有技能 ID 對 AOI 玩家送出；MP／傷害／罪名全由伺服器判定"


func _update_player_shop_ui() -> void:
	player_shop_panel.apply_snapshot(_latest, active, session, item_catalog)


func _open_repair_from_input() -> void:
	if repair_open_button.disabled:
		return
	if submit_repair_open(int(repair_npc_input.value)):
		repair_status.text = "修理清單請求已送出，等待伺服器核准耐久與費用…"
		repair_open_button.disabled = true
	else:
		repair_status.text = "目前無法開啟該修理 NPC"


func _repair_selected_item() -> void:
	if repair_button.disabled or repair_item_picker.selected < 0:
		return
	var uid := int(repair_item_picker.get_item_metadata(repair_item_picker.selected))
	if submit_repair_item(uid):
		repair_status.text = "修理請求已送出，等待伺服器扣款與耐久結算…"
		repair_button.disabled = true
	else:
		repair_status.text = "目前無法修理該物品"


func _repair_location_text(location: int) -> String:
	return "背包" if location == 1 else "裝備" if location == 2 else "未知"


func _update_repair_buttons() -> void:
	var pending := int(_latest.get("pending_repair_sequence",0)) != 0
	var usable := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false))
	repair_open_button.disabled = not usable or pending
	var can_repair := false
	if usable and not pending and bool(_latest.get("repair_ready",false)) and int(_latest.get("active_repair_npc",0)) > 0 and repair_item_picker.selected >= 0:
		var uid := int(repair_item_picker.get_item_metadata(repair_item_picker.selected))
		var items: Dictionary = _latest.get("repair_items",{})
		if items.has(uid):
			var item: Dictionary = items[uid]
			var cost := int(item.get("cost",0))
			can_repair = cost > 0 and cost <= int(_latest.get("repair_wallet_money",0)) and int(item.get("current_durability",0)) < int(item.get("max_durability",0))
	repair_button.disabled = not can_repair


func _update_repair_ui() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	for control in [repair_npc_input,repair_open_button,repair_wallet_label,repair_item_picker,repair_button,repair_status]:
		control.visible = in_world
	if not in_world:
		_repair_display_key = ""
		return
	var active_npc := int(_latest.get("active_repair_npc",0))
	var ready := bool(_latest.get("repair_ready",false))
	var items: Dictionary = _latest.get("repair_items",{})
	var wallet := int(_latest.get("repair_wallet_money",0))
	repair_wallet_label.text = "角色錢包：%d" % wallet if active_npc > 0 else "角色錢包：—"
	var key := "%d/%d/%d/%d/%d" % [
		active_npc,int(_latest.get("repair_revision",0)),
		int(_latest.get("repair_sequence",0)),wallet,items.size()]
	if key != _repair_display_key:
		_repair_display_key = key
		repair_item_picker.clear()
		if ready and active_npc > 0:
			var uids: Array = items.keys()
			uids.sort()
			for raw_uid in uids:
				var uid := int(raw_uid)
				var item: Dictionary = items[raw_uid]
				var item_id := int(item.get("item_definition_id",0))
				repair_item_picker.add_item("%s｜%s｜耐久 %d/%d｜費用 %d｜UID %d" % [
					item_catalog.name_for(item_id),
					_repair_location_text(int(item.get("location",0))),
					int(item.get("current_durability",0)),
					int(item.get("max_durability",0)),
					int(item.get("cost",0)),uid])
				repair_item_picker.set_item_metadata(repair_item_picker.item_count-1,uid)
			if repair_item_picker.item_count > 0:
				repair_item_picker.select(0)
	var open_status := int(_latest.get("last_repair_open_status",-1))
	if open_status > 0 and not ready:
		repair_status.text = "修理 NPC 無法開啟（伺服器代碼 %d）" % open_status
	var result: Dictionary = _latest.get("last_repair_result",{})
	if not result.is_empty():
		var result_status := int(result.get("status",-1))
		if result_status in [0,1]:
			repair_status.text = "修理完成：%s｜耐久 %d/%d｜費用 %d｜錢包 %d" % [
				item_catalog.name_for(int(result.get("item_definition_id",0))),
				int(result.get("current_durability",0)),
				int(result.get("max_durability",0)),
				int(result.get("cost",0)),int(result.get("wallet_money",0))]
		elif result_status == 6:
			repair_status.text = "物品耐久已滿，不需修理"
		elif result_status == 7:
			repair_status.text = "金錢不足：需要 %d，錢包 %d" % [
				int(result.get("cost",0)),int(result.get("wallet_money",0))]
		else:
			repair_status.text = "修理失敗（伺服器代碼 %d）" % result_status
	_update_repair_buttons()


func _open_enhancement_from_input() -> void:
	if enhancement_open_button.disabled:
		return
	if submit_enhancement_open(int(enhancement_npc_input.value)):
		enhancement_status.text = "強化清單請求已送出，等待伺服器核准等級、成功率與費用…"
		enhancement_open_button.disabled = true
	else:
		enhancement_status.text = "目前無法開啟該強化 NPC"


func _enhance_selected_item() -> void:
	if enhancement_button.disabled or enhancement_item_picker.selected < 0:
		return
	var uid := int(enhancement_item_picker.get_item_metadata(
		enhancement_item_picker.selected))
	if submit_enhancement_item(uid):
		enhancement_status.text = "強化請求已送出，等待伺服器擲骰、扣款與等級結算…"
		enhancement_button.disabled = true
	else:
		enhancement_status.text = "目前無法強化該物品"


func _enhancement_location_text(location: int) -> String:
	return "背包" if location == 1 else "裝備" if location == 2 else "未知"


func _update_enhancement_buttons() -> void:
	var pending := int(_latest.get("pending_enhancement_sequence",0)) != 0
	var usable := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false))
	enhancement_open_button.disabled = not usable or pending or int(_latest.get("pending_repair_sequence",0)) != 0
	var can_enhance := false
	if usable and not pending and bool(_latest.get("enhancement_ready",false)) and int(_latest.get("active_enhancement_npc",0)) > 0 and enhancement_item_picker.selected >= 0:
		var uid := int(enhancement_item_picker.get_item_metadata(
			enhancement_item_picker.selected))
		var items: Dictionary = _latest.get("enhancement_items",{})
		if items.has(uid):
			var item: Dictionary = items[uid]
			var cost := int(item.get("money_cost",0))
			can_enhance = cost <= int(_latest.get("enhancement_wallet_money",0))
	enhancement_button.disabled = not can_enhance


func _update_enhancement_ui() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	for control in [enhancement_npc_input,enhancement_open_button,enhancement_wallet_label,enhancement_item_picker,enhancement_button,enhancement_status]:
		control.visible = in_world
	if not in_world:
		_enhancement_display_key = ""
		return
	var active_npc := int(_latest.get("active_enhancement_npc",0))
	var ready := bool(_latest.get("enhancement_ready",false))
	var items: Dictionary = _latest.get("enhancement_items",{})
	var wallet := int(_latest.get("enhancement_wallet_money",0))
	enhancement_wallet_label.text = "角色錢包：%d" % wallet if active_npc > 0 else "角色錢包：—"
	var key := "%d/%d/%d/%d/%d" % [
		active_npc,int(_latest.get("enhancement_revision",0)),
		int(_latest.get("enhancement_sequence",0)),wallet,items.size()]
	if key != _enhancement_display_key:
		_enhancement_display_key = key
		enhancement_item_picker.clear()
		if ready and active_npc > 0:
			var uids: Array = items.keys()
			uids.sort()
			for raw_uid in uids:
				var uid := int(raw_uid)
				var item: Dictionary = items[raw_uid]
				var item_id := int(item.get("item_definition_id",0))
				var chance := float(int(item.get("success_ppm",0))) / 10000.0
				var failure_fee := "失敗扣款" if bool(
					item.get("consume_money_on_failure",false)) else "失敗不扣"
				enhancement_item_picker.add_item(
					"%s｜%s｜+%d → +%d｜成功 %.2f%%｜費用 %d｜%s｜UID %d" % [
						item_catalog.name_for(item_id),
						_enhancement_location_text(int(item.get("location",0))),
						int(item.get("current_level",0)),
						int(item.get("target_level",0)),chance,
						int(item.get("money_cost",0)),failure_fee,uid])
				enhancement_item_picker.set_item_metadata(
					enhancement_item_picker.item_count-1,uid)
			if enhancement_item_picker.item_count > 0:
				enhancement_item_picker.select(0)
	var open_status := int(_latest.get("last_enhancement_open_status",-1))
	if open_status > 0 and not ready:
		enhancement_status.text = "強化 NPC 無法開啟（伺服器代碼 %d）" % open_status
	var result: Dictionary = _latest.get("last_enhancement_result",{})
	if not result.is_empty():
		var status := int(result.get("status",-1))
		if status in [0,1]:
			var outcome := "成功" if bool(result.get("succeeded",false)) else "失敗，等級不變"
			enhancement_status.text = "強化%s：%s｜+%d → +%d（目前 +%d）｜費用 %d／實扣 %d｜錢包 %d" % [
				outcome,item_catalog.name_for(
					int(result.get("item_definition_id",0))),
				int(result.get("previous_level",0)),
				int(result.get("target_level",0)),
				int(result.get("current_level",0)),
				int(result.get("money_cost",0)),
				int(result.get("money_charged",0)),
				int(result.get("wallet_money",0))]
		elif status == 6:
			enhancement_status.text = "此物品已達目前伺服器規則的最高強化等級"
		elif status == 7:
			enhancement_status.text = "金錢不足：需要 %d，錢包 %d" % [
				int(result.get("money_cost",0)),
				int(result.get("wallet_money",0))]
		elif status == 5:
			enhancement_status.text = "伺服器目前沒有此物品／等級的已驗證強化規則"
		else:
			enhancement_status.text = "強化失敗（伺服器代碼 %d）" % status
	_update_enhancement_buttons()


func _process(delta: float) -> void:
	if not active:
		return
	_poll_elapsed += delta
	if _poll_elapsed < 0.05:
		return
	_poll_elapsed = 0.0
	_latest = session.snapshot() # Reads published state; never pumps packets.
	_set_respawn_controls()
	_set_skill_controls()
	_refresh_inventory_panel()
	_update_shop_ui()
	_update_storage_ui()
	bank_panel.apply_snapshot(_latest, active)
	_update_repair_ui()
	_update_enhancement_ui()
	_refresh_trade_panel()
	_update_player_shop_ui()
	_update_nearby_chat_ui()
	_update_quest_ui()
	_update_pvp_ui()
	var pickup_result: Dictionary = _latest.get("last_pickup_result",{})
	if not pickup_result.is_empty():
		if int(pickup_result.status) in [0,1]:
			pickup_status.text = "本次拾取：道具 %d ×%d（UID %d）" % [int(pickup_result.item_definition_id),int(pickup_result.quantity),int(pickup_result.item_uid)]
		else:
			pickup_status.text = "拾取失敗，代碼 %d" % int(pickup_result.status)
	if bool(_latest.get("reconnecting",false)):
		status.text = "連線中斷，正在重新連線（%d/%d）…" % [int(_latest.reconnect_attempt),int(_latest.max_reconnect_attempts)]
		return
	var state := int(_latest.state)
	if state == State.STATE_FAILED:
		var registration_code:=int(_latest.get("registration_status",-1))
		disconnect_session()
		status.text=_registration_status_text(registration_code) if registration_code>0 else "連線或登入失敗，請確認帳號與伺服器憑證後重試"
		return
	if state == State.STATE_SELECTING_CHARACTER:
		if _chosen and int(_latest.choose_status) >= 0:
			disconnect_session()
			status.text = "角色無法使用，請重新登入"
			return
		if _latest.characters != _characters:
			_characters = _latest.characters
			characters.clear()
			var created_index := -1
			var created_id := int(_latest.get("last_created_character_id",0))
			var used_slots := {}
			for entry in _characters:
				characters.add_item(str(entry.name))
				used_slots[int(entry.slot)] = true
				if int(entry.character_id) == created_id:
					created_index = characters.item_count - 1
			if created_index >= 0:
				characters.select(created_index)
				character_name_input.clear()
			if used_slots.has(int(character_slot_input.value)):
				for slot in range(8):
					if not used_slots.has(slot):
						character_slot_input.value = slot
						break
			_set_controls()
		var create_code := int(_latest.get("character_create_status",-1))
		if create_code >= 0:
			character_create_status.text = _character_create_status_text(
				create_code,str(_latest.get("last_created_name","")))
		if not _chosen:
			status.text = "請選擇角色或建立新角色"
	elif state == State.STATE_IN_WORLD:
		characters.hide()
		choose_button.hide()
		status.text = "已連線・角色 " + str(_latest.actor_id)
		if bool(_latest.get("combat_available",false)):
			status.text += _combat_status_suffix()
			if bool(_latest.get("player_dead",false)):
				status.text += "｜復活處理中" if respawn_button.disabled else "｜已死亡，請復活"
		if bool(_latest.world_ready) and int(_latest.map_epoch) != _world_epoch:
			_world_epoch = int(_latest.map_epoch)
			world_ready.emit(_latest)
		if bool(_latest.world_ready):
			world_snapshot.emit(_latest)




func _registration_status_text(code:int)->String:
	match code:
		1:
			return "伺服器未開放公開帳號註冊"
		2:
			return "帳號名稱已被使用"
		3:
			return "帳號或密碼不符合伺服器規則"
		4:
			return "建立帳號請求過於頻繁，請稍後重試"
		_:
			return "建立帳號失敗"


func _character_create_status_text(code: int, character_name: String) -> String:
	match code:
		0:
			return "角色「%s」建立成功，可直接進入世界" % character_name
		1:
			return "這個角色欄位已被使用，請換一個欄位"
		2:
			return "角色名稱已被使用"
		3:
			return "角色數量已達上限"
		4:
			return "角色名稱格式不合法"
		5:
			return "角色欄位不合法"
		6:
			return "伺服器尚未開放遊戲內創角"
		_:
			return ""

func _exit_tree() -> void:
	session.stop()


func _refresh_trade_panel() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	trade_panel.apply_snapshot(_latest, active, in_world, session, item_catalog)


func _refresh_inventory_panel() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	inventory_panel.apply_snapshot(_latest, active, in_world, session, item_catalog)
