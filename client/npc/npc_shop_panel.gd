extends VBoxContainer
## NPC merchant controls. Server owns prices, wallet settlement and item UID.
const State = preload("res://world/world_state.gd")

var active := false
var _latest: Dictionary = {}
var item_catalog
var _open_action: Callable
var _buy_action: Callable
var _sell_action: Callable
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

func _ready() -> void:
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
		add_child(control)
	shop_open_button.pressed.connect(_open_shop_from_input)
	shop_buy_button.pressed.connect(_buy_selected_shop_item)
	shop_sell_button.pressed.connect(_sell_selected_shop_item)
	shop_sell_picker.item_selected.connect(func(_index): _update_shop_sell_quantity())
	reset()

func set_actions(open_action: Callable, buy_action: Callable, sell_action: Callable, catalog_ref) -> void:
	_open_action = open_action
	_buy_action = buy_action
	_sell_action = sell_action
	item_catalog = catalog_ref

func reset() -> void:
	active = false
	_latest = {}
	_shop_display_key = ""
	shop_status.text = ""
	shop_item_picker.clear()
	shop_sell_picker.clear()

func apply_snapshot(snapshot: Dictionary, connection_active: bool) -> void:
	_latest = snapshot
	active = connection_active
	_update_shop_ui()

func _open_shop_from_input() -> void:
	if _open_action.call(int(shop_npc_input.value)):
		shop_status.text = "正在向伺服器確認 NPC 商店…"
		shop_open_button.disabled = true
	else:
		shop_status.text = "目前無法開啟商店"


func _buy_selected_shop_item() -> void:
	if shop_buy_button.disabled or shop_item_picker.selected < 0:
		return
	var item_id := int(shop_item_picker.get_item_metadata(shop_item_picker.selected))
	var quantity := int(shop_quantity.value)
	if _buy_action.call(item_id,quantity):
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
	if _sell_action.call(uid,quantity):
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
