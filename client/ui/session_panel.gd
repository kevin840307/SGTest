extends PanelContainer
## UI adapter only. ClientSession owns transport/reduction on its own threads.
signal connection_changed(active: bool)
signal world_ready(snapshot: Dictionary)
signal world_snapshot(snapshot: Dictionary)

const Session = preload("res://network/client_session.gd")
const State = preload("res://network/world_state.gd")
const ItemCatalog = preload("res://ui/item_catalog.gd")
var session = Session.new()
var item_catalog = ItemCatalog.new()
var active := false
var trusted_chain: X509Certificate
var tls_name := ""
var host_input := LineEdit.new()
var port_input := SpinBox.new()
var username_input := LineEdit.new()
var password_input := LineEdit.new()
var login_button := Button.new()
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
var inventory_button := Button.new()
var inventory_preview := TextEdit.new()
var inventory_search := LineEdit.new()
var inventory_previous := Button.new()
var inventory_next := Button.new()
var inventory_page_label := Label.new()
var recovery_picker := OptionButton.new()
var recovery_button := Button.new()
var recovery_status := Label.new()
var discard_button := Button.new()
var discard_quantity := SpinBox.new()
var discard_all_button := Button.new()
var discard_confirm := ConfirmationDialog.new()
var discard_status := Label.new()
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
var equipment_inventory_picker := OptionButton.new()
var equipment_equipped_picker := OptionButton.new()
var equipment_equip_button := Button.new()
var equipment_unequip_button := Button.new()
var equipment_status := Label.new()
var _equipment_display_key := ""
var bank_npc_input := SpinBox.new()
var bank_open_button := Button.new()
var bank_scope_label := Label.new()
var bank_wallet_label := Label.new()
var bank_balance_label := Label.new()
var bank_amount_input := SpinBox.new()
var bank_deposit_button := Button.new()
var bank_withdraw_button := Button.new()
var bank_status := Label.new()
var _bank_display_key := ""
var repair_npc_input := SpinBox.new()
var repair_open_button := Button.new()
var repair_wallet_label := Label.new()
var repair_item_picker := OptionButton.new()
var repair_button := Button.new()
var repair_status := Label.new()
var _repair_display_key := ""
const INVENTORY_PAGE_SIZE := 80
var _inventory_page := 0
var _inventory_display_key := ""
var _pending_discard_confirmation := {}
var status := Label.new()
var _characters: Array = []
var _chosen := false
var _world_epoch := 0
var _poll_elapsed := 0.0
var _latest := {}
var _respawn_queued_after := -1
var _skill_queued_after := -1
var _skill_queued_at_ms := 0
const SKILL_RESULT_NAMES = ["成功","未命中","目標不存在","目標死亡","超出距離","障礙阻擋","冷卻中","無法戰鬥","地圖已變更","重複序號","伺服器未核准技能","MP 不足","範圍目標數超限"]


func _ready() -> void:
	custom_minimum_size.x = 360
	var column := VBoxContainer.new()
	add_child(column)
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
	inventory_button.text = "背包"
	inventory_button.visible = false
	inventory_button.focus_mode = Control.FOCUS_NONE
	inventory_preview.editable = false
	inventory_preview.visible = false
	inventory_preview.custom_minimum_size = Vector2(350,160)
	inventory_preview.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	inventory_search.placeholder_text = "搜尋道具名稱／ID／類別"
	inventory_search.visible = false
	inventory_search.max_length = 64
	inventory_previous.text = "上一頁"
	inventory_next.text = "下一頁"
	inventory_page_label.text = "第 1 / 1 頁"
	recovery_button.text = "使用選中道具（Server 核實回復效果）"
	recovery_button.disabled = true
	recovery_status.text = ""
	discard_button.text = "丟棄選中數量（永久銷毀）"
	discard_button.disabled = true
	discard_quantity.min_value = 1
	discard_quantity.max_value = 1
	discard_quantity.value = 1
	discard_quantity.step = 1
	discard_all_button.text = "全部數量"
	discard_all_button.disabled = true
	discard_confirm.title = "確認銷毀背包物品"
	discard_confirm.ok_button_text = "確認銷毀"
	add_child(discard_confirm)
	discard_status.text = ""
	for control in [inventory_previous, inventory_next, inventory_page_label,recovery_picker,recovery_button,recovery_status,discard_quantity,discard_all_button,discard_button,discard_status]:
		control.visible = false
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
	for control in [storage_npc_input,storage_open_button,storage_deposit_picker,storage_withdraw_picker,storage_deposit_button,storage_withdraw_button,storage_status,equipment_inventory_picker,equipment_equipped_picker,equipment_equip_button,equipment_unequip_button,equipment_status]:
		control.visible = false
	equipment_equip_button.text = "裝上選中背包物品"
	equipment_unequip_button.text = "卸下選中裝備"
	equipment_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [equipment_inventory_picker,equipment_equipped_picker,equipment_equip_button,equipment_unequip_button,equipment_status]:
		control.visible = false
	bank_npc_input.min_value = 1
	bank_npc_input.max_value = 2147483647
	bank_npc_input.step = 1
	bank_npc_input.value = 1
	bank_open_button.text = "開啟附近 NPC 銀行"
	bank_scope_label.text = "銀行範圍：—"
	bank_wallet_label.text = "角色錢包：—"
	bank_balance_label.text = "銀行餘額：—"
	bank_amount_input.min_value = 1
	bank_amount_input.max_value = 9000000000000000
	bank_amount_input.step = 1
	bank_amount_input.value = 1
	bank_deposit_button.text = "存入金錢"
	bank_withdraw_button.text = "領出金錢"
	bank_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [bank_npc_input,bank_open_button,bank_scope_label,bank_wallet_label,bank_balance_label,bank_amount_input,bank_deposit_button,bank_withdraw_button,bank_status,repair_npc_input,repair_open_button,repair_wallet_label,repair_item_picker,repair_button,repair_status]:
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
	for control in [login_button,characters,choose_button,character_name_input,character_slot_input,create_character_button,character_create_status,respawn_button,skill_hint,skill_id_input,skill_target_mode,skill_area_button,skill_status,logout_button,status,pickup_status,inventory_button,inventory_search,inventory_preview,inventory_previous,inventory_page_label,inventory_next,recovery_picker,recovery_button,recovery_status,discard_quantity,discard_all_button,discard_button,discard_status,shop_npc_input,shop_open_button,shop_money_label,shop_item_picker,shop_sell_picker,shop_quantity,shop_sell_quantity,shop_buy_button,shop_sell_button,shop_status,storage_npc_input,storage_open_button,storage_deposit_picker,storage_withdraw_picker,storage_deposit_button,storage_withdraw_button,storage_status,equipment_inventory_picker,equipment_equipped_picker,equipment_equip_button,equipment_unequip_button,equipment_status,bank_npc_input,bank_open_button,bank_scope_label,bank_wallet_label,bank_balance_label,bank_amount_input,bank_deposit_button,bank_withdraw_button,bank_status,repair_npc_input,repair_open_button,repair_wallet_label,repair_item_picker,repair_button,repair_status]:
		column.add_child(control)
	login_button.pressed.connect(login)
	password_input.text_submitted.connect(func(_text): login())
	choose_button.pressed.connect(choose)
	create_character_button.pressed.connect(create_character)
	character_name_input.text_submitted.connect(func(_text): create_character())
	respawn_button.pressed.connect(request_respawn)
	skill_area_button.pressed.connect(_cast_area_skill)
	logout_button.pressed.connect(disconnect_session)
	inventory_button.pressed.connect(_toggle_inventory)
	inventory_search.text_changed.connect(_on_inventory_search_changed)
	inventory_previous.pressed.connect(func(): _set_inventory_page(_inventory_page-1))
	inventory_next.pressed.connect(func(): _set_inventory_page(_inventory_page+1))
	recovery_button.pressed.connect(_use_selected_recovery)
	recovery_picker.item_selected.connect(func(_index): _update_discard_selection())
	discard_all_button.pressed.connect(func(): discard_quantity.value = discard_quantity.max_value)
	discard_button.pressed.connect(_discard_selected_item)
	discard_confirm.confirmed.connect(_confirm_inventory_discard)
	discard_confirm.canceled.connect(func(): _pending_discard_confirmation.clear())
	shop_open_button.pressed.connect(_open_shop_from_input)
	shop_buy_button.pressed.connect(_buy_selected_shop_item)
	shop_sell_button.pressed.connect(_sell_selected_shop_item)
	shop_sell_picker.item_selected.connect(func(_index): _update_shop_sell_quantity())
	storage_open_button.pressed.connect(_open_storage_from_input)
	storage_deposit_button.pressed.connect(_deposit_selected_storage_item)
	storage_withdraw_button.pressed.connect(_withdraw_selected_storage_item)
	equipment_equip_button.pressed.connect(_equip_selected_inventory_item)
	equipment_unequip_button.pressed.connect(_unequip_selected_item)
	bank_open_button.pressed.connect(_open_bank_from_input)
	bank_deposit_button.pressed.connect(_deposit_bank_amount)
	bank_withdraw_button.pressed.connect(_withdraw_bank_amount)
	repair_open_button.pressed.connect(_open_repair_from_input)
	repair_button.pressed.connect(_repair_selected_item)
	repair_item_picker.item_selected.connect(func(_index): _update_repair_buttons())
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


func login() -> void:
	if active:
		return
	var username := username_input.text.strip_edges()
	var password := password_input.text
	password_input.clear()
	if username.to_utf8_buffer().size() < 3 or username.to_utf8_buffer().size() > 32 or password.to_utf8_buffer().size() < 8 or password.to_utf8_buffer().size() > 256 or host_input.text.strip_edges().is_empty():
		status.text = "帳號需 3–32 bytes，密碼需 8–256 bytes"
		return
	var error: int = session.connect_secure(host_input.text.strip_edges(),int(port_input.value),username,password,tls_name,trusted_chain)
	password = ""
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
	_pending_discard_confirmation.clear()
	discard_confirm.hide()
	pickup_status.text = ""
	discard_status.text = ""
	_inventory_display_key = ""
	_shop_display_key = ""
	_storage_display_key = ""
	_equipment_display_key = ""
	_bank_display_key = ""
	_repair_display_key = ""
	_inventory_page = 0
	inventory_search.clear()
	shop_status.text = ""
	shop_item_picker.clear()
	shop_sell_picker.clear()
	storage_status.text = ""
	storage_deposit_picker.clear()
	storage_withdraw_picker.clear()
	equipment_status.text = ""
	equipment_inventory_picker.clear()
	equipment_equipped_picker.clear()
	bank_status.text = ""
	bank_scope_label.text = "銀行範圍：—"
	bank_wallet_label.text = "角色錢包：—"
	bank_balance_label.text = "銀行餘額：—"
	repair_status.text = ""
	repair_wallet_label.text = "角色錢包：—"
	repair_item_picker.clear()
	inventory_preview.text = ""
	_show_inventory_preview(false)
	characters.clear()
	_characters = []
	_set_controls()
	status.text = "安全連線中…"
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


func submit_recovery_use(item_uid: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and bool(_latest.get("combat_available",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and int(_latest.get("pending_recovery_sequence",0)) == 0 and int(_latest.get("pending_discard_sequence",0)) == 0 and item_uid > 0 and session.use_recovery_item(item_uid)


func submit_inventory_discard(item_uid: int, quantity: int = 1) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("resync_pending",false)) and int(_latest.get("pending_discard_sequence",0)) == 0 and int(_latest.get("pending_recovery_sequence",0)) == 0 and item_uid > 0 and quantity > 0 and quantity <= 1000000 and (_latest.get("inventory_items",{}) as Dictionary).has(item_uid) and quantity <= int(((_latest.get("inventory_items",{}) as Dictionary)[item_uid] as Dictionary).get("quantity",0)) and session.discard_inventory_item(item_uid,quantity)


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


func submit_equipment_equip(item_uid: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and bool(_latest.get("equipment_ready",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_equipment_sequence",0)) == 0 and item_uid > 0 and (_latest.get("inventory_items",{}) as Dictionary).has(item_uid) and session.equip_inventory_item(item_uid)


func submit_equipment_unequip(item_uid: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and bool(_latest.get("equipment_ready",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_equipment_sequence",0)) == 0 and item_uid > 0 and (_latest.get("equipment_items",{}) as Dictionary).has(item_uid) and session.unequip_item(item_uid)

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
	_pending_discard_confirmation.clear()
	discard_confirm.hide()
	pickup_status.text = ""
	discard_status.text = ""
	_inventory_display_key = ""
	_shop_display_key = ""
	_storage_display_key = ""
	_equipment_display_key = ""
	_bank_display_key = ""
	_repair_display_key = ""
	_inventory_page = 0
	inventory_search.clear()
	shop_status.text = ""
	shop_item_picker.clear()
	shop_sell_picker.clear()
	storage_status.text = ""
	storage_deposit_picker.clear()
	storage_withdraw_picker.clear()
	equipment_status.text = ""
	equipment_inventory_picker.clear()
	equipment_equipped_picker.clear()
	bank_status.text = ""
	bank_scope_label.text = "銀行範圍：—"
	bank_wallet_label.text = "角色錢包：—"
	bank_balance_label.text = "銀行餘額：—"
	repair_status.text = ""
	repair_wallet_label.text = "角色錢包：—"
	repair_item_picker.clear()
	inventory_preview.text = ""
	_show_inventory_preview(false)
	_characters = []
	characters.clear()
	character_name_input.clear()
	character_create_status.text = ""
	password_input.clear()
	_set_controls()
	inventory_button.visible = false
	inventory_button.disabled = true
	inventory_preview.visible = false
	status.text = "已登出"
	if was_active:
		connection_changed.emit(false)


func _set_controls() -> void:
	host_input.editable = not active
	username_input.editable = not active
	password_input.editable = not active
	port_input.editable = not active
	login_button.disabled = active
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


func _show_inventory_preview(show: bool) -> void:
	inventory_preview.visible = show
	recovery_picker.visible = show
	recovery_button.visible = show
	recovery_status.visible = show
	discard_quantity.visible = show
	discard_all_button.visible = show
	discard_button.visible = show
	discard_status.visible = show
	inventory_search.visible = show
	inventory_previous.visible = show
	inventory_next.visible = show
	inventory_page_label.visible = show


func _toggle_inventory() -> void:
	if inventory_button.disabled or not inventory_button.visible:
		return
	_show_inventory_preview(not inventory_preview.visible)
	if inventory_preview.visible:
		_update_inventory_ui()


func _on_inventory_search_changed(_query: String) -> void:
	_inventory_page = 0
	_inventory_display_key = ""
	_update_inventory_ui()


func _set_inventory_page(page: int) -> void:
	_inventory_page = maxi(0,page)
	_inventory_display_key = ""
	_update_inventory_ui()


func _use_selected_recovery() -> void:
	if recovery_button.disabled or recovery_picker.selected < 0:
		return
	var uid := int(recovery_picker.get_item_metadata(recovery_picker.selected))
	if uid <= 0:
		return
	if submit_recovery_use(uid):
		recovery_button.disabled = true
		discard_button.disabled = true
		recovery_status.text = "道具使用請求已送出…"
	else:
		recovery_status.text = "目前無法使用該物品"


func _update_discard_selection() -> void:
	var items: Dictionary = _latest.get("inventory_items",{})
	var uid := 0
	if recovery_picker.selected >= 0:
		uid = int(recovery_picker.get_item_metadata(recovery_picker.selected))
	var count := int((items.get(uid,{}) as Dictionary).get("quantity",0))
	discard_quantity.max_value = maxi(1,mini(1000000,count))
	discard_quantity.value = 1
	_update_discard_controls()


func _update_discard_controls() -> void:
	var enabled: bool = not discard_button.disabled and recovery_picker.selected >= 0
	discard_quantity.editable = enabled
	discard_all_button.disabled = not enabled


func _discard_selected_item() -> void:
	if discard_button.disabled or recovery_picker.selected < 0 or not _pending_discard_confirmation.is_empty():
		return
	var uid := int(recovery_picker.get_item_metadata(recovery_picker.selected))
	var quantity := int(discard_quantity.value)
	var items: Dictionary = _latest.get("inventory_items",{})
	if uid <= 0 or not items.has(uid) or quantity < 1 or quantity > mini(1000000,int((items[uid] as Dictionary).get("quantity",0))):
		discard_status.text = "數量或選取物品已失效"
		return
	_pending_discard_confirmation = {
		"actor_id":int(_latest.get("actor_id",0)),
		"reconnect_count":int(_latest.get("reconnect_count",0)),
		"map_epoch":int(_latest.get("map_epoch",0)),
		"inventory_revision":int(_latest.get("inventory_revision",0)),
		"uid":uid,"quantity":quantity,
	}
	discard_confirm.dialog_text = "確定永久銷毀 %s ×%d（UID %d）？\n這不是丟到地上，無法復原。" % [
		item_catalog.name_for(int((items[uid] as Dictionary).item_definition_id)),quantity,uid]
	discard_confirm.popup_centered()


func _confirm_inventory_discard() -> void:
	if _pending_discard_confirmation.is_empty():
		return
	var requested: Dictionary = _pending_discard_confirmation.duplicate()
	_pending_discard_confirmation.clear()
	# Confirmation is not authorization. Re-check the exact actor, epoch,
	# inventory revision, selected UID and stack size before queuing.
	var uid := int(requested.uid)
	var quantity := int(requested.quantity)
	var items: Dictionary = _latest.get("inventory_items",{})
	if discard_button.disabled or int(_latest.get("actor_id",0)) != int(requested.actor_id) or int(_latest.get("reconnect_count",0)) != int(requested.reconnect_count) or int(_latest.get("map_epoch",0)) != int(requested.map_epoch) or int(_latest.get("inventory_revision",0)) != int(requested.inventory_revision) or recovery_picker.selected < 0 or int(recovery_picker.get_item_metadata(recovery_picker.selected)) != uid or int(discard_quantity.value) != quantity or not items.has(uid) or quantity < 1 or quantity > int((items.get(uid,{}) as Dictionary).get("quantity",0)):
		discard_status.text = "背包或角色狀態已變更，請重新選取並確認"
		return
	if submit_inventory_discard(uid,quantity):
		discard_button.disabled = true
		recovery_button.disabled = true
		_update_discard_controls()
		discard_status.text = "丟棄 ×%d 請求已送出，等待伺服器結算…" % quantity
	else:
		discard_status.text = "目前無法丟棄該物品"




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


func _open_bank_from_input() -> void:
	if bank_open_button.disabled:
		return
	if submit_bank_open(int(bank_npc_input.value)):
		bank_status.text = "銀行請求已送出，等待伺服器核准範圍與餘額…"
		bank_open_button.disabled = true
	else:
		bank_status.text = "目前無法開啟該銀行"


func _deposit_bank_amount() -> void:
	if bank_deposit_button.disabled:
		return
	var amount := int(bank_amount_input.value)
	if submit_bank_deposit(amount):
		bank_status.text = "存入請求已送出，等待伺服器結算…"
		bank_deposit_button.disabled = true
		bank_withdraw_button.disabled = true
	else:
		bank_status.text = "目前無法存入該金額"


func _withdraw_bank_amount() -> void:
	if bank_withdraw_button.disabled:
		return
	var amount := int(bank_amount_input.value)
	if submit_bank_withdraw(amount):
		bank_status.text = "領出請求已送出，等待伺服器結算…"
		bank_deposit_button.disabled = true
		bank_withdraw_button.disabled = true
	else:
		bank_status.text = "目前無法領出該金額"


func _bank_scope_text(scope: int) -> String:
	match scope:
		1:
			return "角色專屬（伺服器規則）"
		2:
			return "帳號共用（伺服器規則）"
		_:
			return "—"


func _update_bank_ui() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	for control in [bank_npc_input,bank_open_button,bank_scope_label,bank_wallet_label,bank_balance_label,bank_amount_input,bank_deposit_button,bank_withdraw_button,bank_status]:
		control.visible = in_world
	if not in_world:
		_bank_display_key = ""
		return
	var active_npc := int(_latest.get("active_bank_npc",0))
	var scope := int(_latest.get("bank_scope",0))
	var wallet := int(_latest.get("bank_wallet_money",0))
	var balance := int(_latest.get("bank_money",0))
	var pending := int(_latest.get("pending_bank_sequence",0)) != 0
	var usable := bool(_latest.get("world_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_world_ready_ack_epoch",0)) == 0 and int(_latest.get("last_world_ready_ack_epoch",0)) == int(_latest.get("map_epoch",0))
	bank_open_button.disabled = not usable or pending
	bank_scope_label.text = "銀行範圍：%s" % _bank_scope_text(scope)
	bank_wallet_label.text = "角色錢包：%d" % wallet if active_npc > 0 else "角色錢包：—"
	bank_balance_label.text = "銀行餘額：%d" % balance if active_npc > 0 else "銀行餘額：—"
	var amount := int(bank_amount_input.value)
	bank_deposit_button.disabled = not usable or pending or active_npc <= 0 or scope not in [1,2] or amount <= 0 or amount > wallet
	bank_withdraw_button.disabled = not usable or pending or active_npc <= 0 or scope not in [1,2] or amount <= 0 or amount > balance
	var open_status := int(_latest.get("last_bank_open_status",-1))
	if open_status > 0 and active_npc == 0:
		bank_status.text = "銀行無法開啟（伺服器代碼 %d）" % open_status
	var result: Dictionary = _latest.get("last_bank_result",{})
	if not result.is_empty():
		if int(result.get("status",-1)) in [0,1]:
			bank_status.text = "%s完成：%d｜錢包 %d｜銀行 %d" % ["存入" if int(result.get("kind",0)) == 1 else "領出",int(result.get("amount",0)),int(result.get("wallet_money",0)),int(result.get("bank_money",0))]
		else:
			bank_status.text = "銀行操作失敗（伺服器代碼 %d）" % int(result.get("status",-1))


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


func _equip_selected_inventory_item() -> void:
	if equipment_equip_button.disabled or equipment_inventory_picker.selected < 0:
		return
	var uid := int(equipment_inventory_picker.get_item_metadata(equipment_inventory_picker.selected))
	if submit_equipment_equip(uid):
		equipment_status.text = "裝備請求已送出，等待伺服器核准欄位…"
		equipment_equip_button.disabled = true
		equipment_unequip_button.disabled = true
	else:
		equipment_status.text = "目前無法裝上該物品"


func _unequip_selected_item() -> void:
	if equipment_unequip_button.disabled or equipment_equipped_picker.selected < 0:
		return
	var uid := int(equipment_equipped_picker.get_item_metadata(equipment_equipped_picker.selected))
	if submit_equipment_unequip(uid):
		equipment_status.text = "卸下請求已送出，等待伺服器結算…"
		equipment_equip_button.disabled = true
		equipment_unequip_button.disabled = true
	else:
		equipment_status.text = "目前無法卸下該裝備"


func _update_equipment_ui() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	for control in [equipment_inventory_picker,equipment_equipped_picker,equipment_equip_button,equipment_unequip_button,equipment_status]:
		control.visible = in_world
	if not in_world:
		_equipment_display_key = ""
		return
	var ready := bool(_latest.get("equipment_ready",false))
	var pending := int(_latest.get("pending_equipment_sequence",0)) != 0
	var usable := ready and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false))
	var inventory: Dictionary = _latest.get("inventory_items",{})
	var equipment: Dictionary = _latest.get("equipment_items",{})
	var key := "%d/%d/%d/%d/%d" % [int(_latest.get("inventory_revision",0)),int(_latest.get("equipment_revision",0)),int(_latest.get("equipment_sequence",0)),inventory.size(),equipment.size()]
	if key != _equipment_display_key:
		_equipment_display_key = key
		equipment_inventory_picker.clear()
		equipment_equipped_picker.clear()
		if ready:
			var inventory_uids: Array = inventory.keys()
			inventory_uids.sort()
			for raw_uid in inventory_uids:
				var uid := int(raw_uid)
				var item: Dictionary = inventory[raw_uid]
				equipment_inventory_picker.add_item("%s｜UID %d" % [item_catalog.name_for(int(item.get("item_definition_id",0))),uid])
				equipment_inventory_picker.set_item_metadata(equipment_inventory_picker.item_count-1,uid)
			var equipped_uids: Array = equipment.keys()
			equipped_uids.sort()
			for raw_uid in equipped_uids:
				var uid := int(raw_uid)
				var item: Dictionary = equipment[raw_uid]
				equipment_equipped_picker.add_item("裝備欄位 %d｜%s｜UID %d" % [int(item.get("slot_id",0)),item_catalog.name_for(int(item.get("item_definition_id",0))),uid])
				equipment_equipped_picker.set_item_metadata(equipment_equipped_picker.item_count-1,uid)
			if equipment_inventory_picker.item_count > 0:
				equipment_inventory_picker.select(0)
			if equipment_equipped_picker.item_count > 0:
				equipment_equipped_picker.select(0)
	if not ready:
		equipment_status.text = "裝備規則尚未由伺服器啟用"
	var result: Dictionary = _latest.get("last_equipment_result",{})
	if not result.is_empty():
		if int(result.get("status",-1)) in [0,1]:
			equipment_status.text = "%s完成：裝備欄位 %d｜%s｜UID %d" % ["裝上" if int(result.get("kind",0)) == 1 else "卸下",int(result.get("slot_id",0)),item_catalog.name_for(int(result.get("item_definition_id",0))),int(result.get("item_uid",0))]
		else:
			equipment_status.text = "裝備操作失敗（伺服器代碼 %d）" % int(result.get("status",-1))
	equipment_equip_button.disabled = not usable or pending or equipment_inventory_picker.item_count == 0
	equipment_unequip_button.disabled = not usable or pending or equipment_equipped_picker.item_count == 0

func _update_inventory_ui() -> void:
	var in_world: bool = active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	var loaded: bool = in_world and bool(_latest.get("inventory_ready",false))
	inventory_button.visible = in_world
	inventory_button.disabled = not loaded or bool(_latest.get("reconnecting",false))
	if not in_world or bool(_latest.get("reconnecting",false)):
		_pending_discard_confirmation.clear()
		discard_confirm.hide()
		_show_inventory_preview(false)
		_inventory_display_key = ""
		return
	if not loaded:
		_pending_discard_confirmation.clear()
		discard_confirm.hide()
		inventory_button.text = "背包（讀取中）"
		_show_inventory_preview(false)
		_inventory_display_key = ""
		return
	var items: Dictionary = _latest.get("inventory_items",{})
	inventory_button.text = "背包（%d 件）" % items.size()
	recovery_button.disabled = not bool(_latest.get("combat_available",false)) or bool(_latest.get("player_dead",false)) or bool(_latest.get("resync_pending",false)) or int(_latest.get("pending_recovery_sequence",0)) != 0 or int(_latest.get("pending_discard_sequence",0)) != 0 or recovery_picker.item_count == 0
	discard_button.disabled = bool(_latest.get("resync_pending",false)) or int(_latest.get("pending_discard_sequence",0)) != 0 or int(_latest.get("pending_recovery_sequence",0)) != 0 or recovery_picker.item_count == 0
	_update_discard_controls()
	var discard_result: Dictionary = _latest.get("last_discard_result",{})
	if not discard_result.is_empty():
		if int(discard_result.status) in [0,1]:
			discard_status.text = "丟棄結算：已移除 %d；剩餘 %d" % [
				int(discard_result.discarded_quantity),
				int(discard_result.remaining_quantity)]
		else:
			discard_status.text = "丟棄失敗（伺服器代碼 %d）" % int(discard_result.status)
	var use_result: Dictionary = _latest.get("last_recovery_result",{})
	if not use_result.is_empty():
		if int(use_result.status) in [0,1]:
			recovery_status.text = "回復結算：HP %d / %d；剩餘 %d 件" % [
				int(use_result.current_hp),int(use_result.max_hp),
				int(use_result.remaining_quantity)]
		else:
			recovery_status.text = "道具無法使用（伺服器代碼 %d）" % int(use_result.status)
	var revision := int(_latest.get("inventory_revision",0))
	var query := inventory_search.text.strip_edges()
	# Most UI polls see an unchanged inventory. Do not allocate/sort/scan
	# up to 4095 items every 50ms; refresh only on visible state changes.
	var cached_key := "%d/%d/%d/%d/%s" % [
		int(_latest.get("actor_id",0)),
		int(_latest.get("reconnect_count",0)),revision,_inventory_page,query]
	if cached_key == _inventory_display_key:
		return
	var uids: Array = items.keys()
	uids.sort()
	var filtered: Array = []
	for raw_uid in uids:
		var id := int(raw_uid)
		var item: Dictionary = items[raw_uid]
		var item_id := int(item.item_definition_id)
		if query.is_empty() or item_catalog.name_for(item_id).findn(query) >= 0 or str(item_id).findn(query) >= 0 or item_catalog.category_for(item_id).findn(query) >= 0:
			filtered.append(raw_uid)
	var pages := maxi(1,ceili(float(filtered.size())/float(INVENTORY_PAGE_SIZE)))
	_inventory_page = clampi(_inventory_page,0,pages-1)
	inventory_previous.disabled = _inventory_page == 0
	inventory_next.disabled = _inventory_page >= pages-1
	inventory_page_label.text = "第 %d / %d 頁（符合 %d 件）" % [
		_inventory_page+1,pages,filtered.size()]
	# Revision can reset to 1 on reconnect/new character. Include all view
	# dimensions to avoid caching another character's or filter's item list.
	var display_key := "%d/%d/%d/%d/%s" % [
		int(_latest.get("actor_id",0)),
		int(_latest.get("reconnect_count",0)),revision,_inventory_page,query]
	if display_key == _inventory_display_key:
		return
	_inventory_display_key = display_key
	var rows := PackedStringArray()
	recovery_picker.clear()
	var start := _inventory_page*INVENTORY_PAGE_SIZE
	for index in range(start,mini(start+INVENTORY_PAGE_SIZE,filtered.size())):
		var uid := int(filtered[index])
		var item: Dictionary = items[filtered[index]]
		var display_name := item_catalog.name_for(int(item.item_definition_id))
		rows.append("%s ×%d  ·  UID %d" % [
			display_name,int(item.quantity),uid])
		# Allow selection without the optional local item catalog; the server
		# rejects all items without admitted, source-audited recovery rules.
		recovery_picker.add_item("%s ×%d (UID %d)" % [
			display_name,int(item.quantity),uid])
		recovery_picker.set_item_metadata(recovery_picker.item_count-1,uid)
	if recovery_picker.item_count > 0:
		recovery_picker.select(0)
	_update_discard_selection()
	recovery_button.disabled = not bool(_latest.get("combat_available",false)) or bool(_latest.get("player_dead",false)) or bool(_latest.get("resync_pending",false)) or int(_latest.get("pending_recovery_sequence",0)) != 0 or int(_latest.get("pending_discard_sequence",0)) != 0 or recovery_picker.item_count == 0
	discard_button.disabled = bool(_latest.get("resync_pending",false)) or int(_latest.get("pending_discard_sequence",0)) != 0 or int(_latest.get("pending_recovery_sequence",0)) != 0 or recovery_picker.item_count == 0
	_update_discard_controls()
	if rows.is_empty():
		rows.append("沒有符合條件的物品" if not query.is_empty() else "背包目前沒有物品")
	inventory_preview.text = "\n".join(rows)


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
	_update_inventory_ui()
	_update_shop_ui()
	_update_storage_ui()
	_update_equipment_ui()
	_update_bank_ui()
	_update_repair_ui()
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
		disconnect_session()
		status.text = "連線或登入失敗，請確認帳號與伺服器憑證後重試"
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
