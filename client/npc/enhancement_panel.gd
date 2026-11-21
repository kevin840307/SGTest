extends VBoxContainer
## NPC enhancement presentation; Server owns chance, material policy and settlement.
const State = preload("res://world/world_state.gd")

var active := false
var _latest: Dictionary = {}
var item_catalog
var _open_action: Callable
var _enhance_action: Callable
var enhancement_npc_input := SpinBox.new()
var enhancement_open_button := Button.new()
var enhancement_wallet_label := Label.new()
var enhancement_item_picker := OptionButton.new()
var enhancement_button := Button.new()
var enhancement_status := Label.new()
var _enhancement_display_key := ""

func _ready() -> void:
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
		add_child(control)
	enhancement_open_button.pressed.connect(_open_enhancement_from_input)
	enhancement_button.pressed.connect(_enhance_selected_item)
	enhancement_item_picker.item_selected.connect(func(_index): _update_enhancement_buttons())
	reset()

func set_actions(open_action: Callable, enhance_action: Callable, catalog_ref) -> void:
	_open_action = open_action
	_enhance_action = enhance_action
	item_catalog = catalog_ref

func reset() -> void:
	active = false
	_latest = {}
	_enhancement_display_key = ""
	enhancement_status.text = ""
	enhancement_wallet_label.text = "角色錢包：—"
	enhancement_item_picker.clear()

func apply_snapshot(snapshot: Dictionary, connection_active: bool) -> void:
	_latest = snapshot
	active = connection_active
	_update_enhancement_ui()

func _open_enhancement_from_input() -> void:
	if enhancement_open_button.disabled:
		return
	if _open_action.call(int(enhancement_npc_input.value)):
		enhancement_status.text = "強化清單請求已送出，等待伺服器核准等級、成功率與費用…"
		enhancement_open_button.disabled = true
	else:
		enhancement_status.text = "目前無法開啟該強化 NPC"


func _enhance_selected_item() -> void:
	if enhancement_button.disabled or enhancement_item_picker.selected < 0:
		return
	var uid := int(enhancement_item_picker.get_item_metadata(
		enhancement_item_picker.selected))
	if _enhance_action.call(uid):
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
