extends VBoxContainer
## NPC repair presentation; Server owns durability and wallet settlement.
const State = preload("res://world/world_state.gd")

var active := false
var _latest: Dictionary = {}
var item_catalog
var _open_action: Callable
var _repair_action: Callable
var repair_npc_input := SpinBox.new()
var repair_open_button := Button.new()
var repair_wallet_label := Label.new()
var repair_item_picker := OptionButton.new()
var repair_button := Button.new()
var repair_status := Label.new()
var _repair_display_key := ""

func _ready() -> void:
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
		add_child(control)
	repair_open_button.pressed.connect(_open_repair_from_input)
	repair_button.pressed.connect(_repair_selected_item)
	repair_item_picker.item_selected.connect(func(_index): _update_repair_buttons())
	reset()

func set_actions(open_action: Callable, repair_action: Callable, catalog_ref) -> void:
	_open_action = open_action
	_repair_action = repair_action
	item_catalog = catalog_ref

func reset() -> void:
	active = false
	_latest = {}
	_repair_display_key = ""
	repair_status.text = ""
	repair_wallet_label.text = "角色錢包：—"
	repair_item_picker.clear()

func apply_snapshot(snapshot: Dictionary, connection_active: bool) -> void:
	_latest = snapshot
	active = connection_active
	_update_repair_ui()

func _open_repair_from_input() -> void:
	if repair_open_button.disabled:
		return
	if _open_action.call(int(repair_npc_input.value)):
		repair_status.text = "修理清單請求已送出，等待伺服器核准耐久與費用…"
		repair_open_button.disabled = true
	else:
		repair_status.text = "目前無法開啟該修理 NPC"


func _repair_selected_item() -> void:
	if repair_button.disabled or repair_item_picker.selected < 0:
		return
	var uid := int(repair_item_picker.get_item_metadata(repair_item_picker.selected))
	if _repair_action.call(uid):
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
