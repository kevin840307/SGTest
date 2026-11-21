extends VBoxContainer
## Owns trade controls and consumes the authoritative session snapshot.

var _session
var _item_catalog
var _latest := {}
var _active := false
var _in_world := false

var trade_target_picker := OptionButton.new()
var trade_request_button := Button.new()
var trade_peer_label := Label.new()
var trade_accept_button := Button.new()
var trade_reject_button := Button.new()
var trade_inventory_picker := OptionButton.new()
var trade_my_offer_picker := OptionButton.new()
var trade_offer_add_button := Button.new()
var trade_offer_remove_button := Button.new()
var trade_other_offer_label := Label.new()
var trade_money_input := SpinBox.new()
var trade_set_money_button := Button.new()
var trade_money_label := Label.new()
var trade_lock_button := Button.new()
var trade_confirm_button := Button.new()
var trade_cancel_button := Button.new()
var trade_status := Label.new()
var _trade_display_key := ""


func _ready() -> void:
	hide()
	trade_request_button.text = "邀請選中玩家交易"
	trade_peer_label.text = "交易對象：—"
	trade_accept_button.text = "接受交易"
	trade_reject_button.text = "拒絕交易"
	trade_offer_add_button.text = "加入選中背包物品（整組）"
	trade_offer_remove_button.text = "移除選中出價物品"
	trade_other_offer_label.text = "對方出價：—"
	trade_other_offer_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	trade_money_input.min_value = 0
	trade_money_input.max_value = 9000000000000000
	trade_money_input.step = 1
	trade_money_input.value = 0
	trade_set_money_button.text = "更新金錢出價"
	trade_money_label.text = "金錢出價：我 0｜對方 0"
	trade_lock_button.text = "鎖定我的出價"
	trade_confirm_button.text = "最終確認交易"
	trade_cancel_button.text = "取消交易"
	trade_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [trade_target_picker,trade_request_button,trade_peer_label,trade_accept_button,trade_reject_button,trade_inventory_picker,trade_my_offer_picker,trade_offer_add_button,trade_offer_remove_button,trade_other_offer_label,trade_money_input,trade_set_money_button,trade_money_label,trade_lock_button,trade_confirm_button,trade_cancel_button,trade_status]:
		add_child(control)
	trade_request_button.pressed.connect(_request_selected_trade)
	trade_accept_button.pressed.connect(_accept_trade_invite)
	trade_reject_button.pressed.connect(_reject_trade_invite)
	trade_offer_add_button.pressed.connect(_add_selected_trade_item)
	trade_offer_remove_button.pressed.connect(_remove_selected_trade_item)
	trade_set_money_button.pressed.connect(_set_trade_money)
	trade_lock_button.pressed.connect(_lock_trade)
	trade_confirm_button.pressed.connect(_confirm_trade)
	trade_cancel_button.pressed.connect(_cancel_trade)


func reset() -> void:
	hide()
	_active = false
	_in_world = false
	_session = null
	_latest = {}
	_trade_display_key = ""
	trade_status.text = ""
	trade_target_picker.clear()
	trade_inventory_picker.clear()
	trade_my_offer_picker.clear()


func apply_snapshot(snapshot: Dictionary, active: bool, in_world: bool, session, item_catalog) -> void:
	_latest = snapshot
	_active = active
	_in_world = in_world
	_session = session
	_item_catalog = item_catalog
	_update_trade_ui()


func submit_trade_request(target_actor_id: int) -> bool:
	if not _in_world or not bool(_latest.get("world_ready",false)) or not bool(_latest.get("inventory_ready",false)) or bool(_latest.get("player_dead",false)) or bool(_latest.get("reconnecting",false)) or bool(_latest.get("resync_pending",false)) or int(_latest.get("trade_id",0)) != 0:
		return false
	var entities: Dictionary = _latest.get("entities",{})
	if target_actor_id <= 0 or target_actor_id == int(_latest.get("actor_id",0)) or not entities.has(target_actor_id):
		return false
	return _session.request_trade(target_actor_id)


func submit_trade_accept() -> bool:
	return _active and int(_latest.get("trade_state",0)) == 1 and int(_latest.get("trade_id",0)) > 0 and _session.accept_trade()


func submit_trade_reject() -> bool:
	return _active and int(_latest.get("trade_state",0)) == 1 and int(_latest.get("trade_id",0)) > 0 and _session.reject_trade()


func submit_trade_add_item(item_uid: int) -> bool:
	var inventory: Dictionary = _latest.get("inventory_items",{})
	return _active and int(_latest.get("trade_state",0)) == 3 and not bool(_latest.get("trade_my_locked",false)) and item_uid > 0 and inventory.has(item_uid) and not (_latest.get("trade_my_items",{}) as Dictionary).has(item_uid) and _session.trade_add_item(item_uid)


func submit_trade_remove_item(item_uid: int) -> bool:
	return _active and int(_latest.get("trade_state",0)) == 3 and not bool(_latest.get("trade_my_locked",false)) and item_uid > 0 and (_latest.get("trade_my_items",{}) as Dictionary).has(item_uid) and _session.trade_remove_item(item_uid)


func submit_trade_money(money: int) -> bool:
	return _active and int(_latest.get("trade_state",0)) == 3 and not bool(_latest.get("trade_my_locked",false)) and money >= 0 and money <= 9000000000000000 and _session.trade_set_money(money)


func submit_trade_lock() -> bool:
	return _active and int(_latest.get("trade_state",0)) == 3 and not bool(_latest.get("trade_my_locked",false)) and _session.trade_lock()


func submit_trade_confirm() -> bool:
	return _active and int(_latest.get("trade_state",0)) == 4 and bool(_latest.get("trade_my_locked",false)) and bool(_latest.get("trade_other_locked",false)) and not bool(_latest.get("trade_my_confirmed",false)) and _session.trade_final_confirm()


func submit_trade_cancel() -> bool:
	return _active and int(_latest.get("trade_id",0)) > 0 and _session.cancel_trade()

func _request_selected_trade() -> void:
	if trade_request_button.disabled or trade_target_picker.selected < 0:
		return
	var actor_id := int(trade_target_picker.get_item_metadata(trade_target_picker.selected))
	if submit_trade_request(actor_id):
		trade_status.text = "交易邀請已送出，等待對方回覆…"
		trade_request_button.disabled = true
	else:
		trade_status.text = "目前無法邀請該玩家交易"


func _accept_trade_invite() -> void:
	if not trade_accept_button.disabled and submit_trade_accept():
		trade_status.text = "已接受交易，等待伺服器建立出價狀態…"


func _reject_trade_invite() -> void:
	if not trade_reject_button.disabled and submit_trade_reject():
		trade_status.text = "已拒絕交易，等待伺服器確認…"


func _add_selected_trade_item() -> void:
	if trade_offer_add_button.disabled or trade_inventory_picker.selected < 0:
		return
	var uid := int(trade_inventory_picker.get_item_metadata(trade_inventory_picker.selected))
	if submit_trade_add_item(uid):
		trade_status.text = "物品出價已送出，等待雙方快照更新…"


func _remove_selected_trade_item() -> void:
	if trade_offer_remove_button.disabled or trade_my_offer_picker.selected < 0:
		return
	var uid := int(trade_my_offer_picker.get_item_metadata(trade_my_offer_picker.selected))
	if submit_trade_remove_item(uid):
		trade_status.text = "移除出價已送出，等待雙方快照更新…"


func _set_trade_money() -> void:
	if not trade_set_money_button.disabled and submit_trade_money(int(trade_money_input.value)):
		trade_status.text = "金錢出價已送出，等待雙方快照更新…"


func _lock_trade() -> void:
	if not trade_lock_button.disabled and submit_trade_lock():
		trade_status.text = "我的出價已鎖定；任一方修改出價時伺服器會解除雙方鎖定"


func _confirm_trade() -> void:
	if not trade_confirm_button.disabled and submit_trade_confirm():
		trade_status.text = "最終確認已送出；只有雙方都確認後伺服器才會原子結算"


func _cancel_trade() -> void:
	if not trade_cancel_button.disabled and submit_trade_cancel():
		trade_status.text = "取消請求已送出…"


func _trade_terminal_text(code: int) -> String:
	match code:
		0:
			return "交易成功"
		1:
			return "交易已被拒絕"
		2:
			return "交易已取消"
		3:
			return "交易邀請已逾時"
		4:
			return "對方已離線"
		5:
			return "伺服器結算失敗，物品與金錢未完成交換"
		6:
			return "地圖／連線世代已變更，交易已取消"
		_:
			return "交易已結束（代碼 %d）" % code


func _trade_state_text(value: int) -> String:
	match value:
		1:
			return "收到交易邀請"
		2:
			return "交易邀請已送出"
		3:
			return "交易進行中"
		4:
			return "雙方出價已鎖定，等待最終確認"
		_:
			return "未交易"


func _update_trade_ui() -> void:
	var in_world := _in_world
	visible = in_world
	if not in_world:
		_trade_display_key = ""
		return
	var trade_id := int(_latest.get("trade_id",0))
	var trade_state := int(_latest.get("trade_state",0))
	var usable := bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false))
	var entity_ids: Array = (_latest.get("entities",{}) as Dictionary).keys()
	entity_ids.sort()
	var target_key := ""
	for raw_actor in entity_ids:
		target_key += "," + str(int(raw_actor))
	var key := "%d/%d/%d/%d/%d/%s" % [trade_id,trade_state,int(_latest.get("trade_revision",0)),int(_latest.get("inventory_revision",0)),int(_latest.get("trade_sequence",0)),target_key]
	if key != _trade_display_key:
		_trade_display_key = key
		trade_target_picker.clear()
		if trade_id == 0:
			for raw_actor in entity_ids:
				var actor_id := int(raw_actor)
				if actor_id <= 0 or actor_id == int(_latest.get("actor_id",0)):
					continue
				trade_target_picker.add_item("玩家 %d" % actor_id)
				trade_target_picker.set_item_metadata(trade_target_picker.item_count-1,actor_id)
			if trade_target_picker.item_count > 0:
				trade_target_picker.select(0)
		trade_inventory_picker.clear()
		trade_my_offer_picker.clear()
		var inventory: Dictionary = _latest.get("inventory_items",{})
		var mine: Dictionary = _latest.get("trade_my_items",{})
		var uids: Array = inventory.keys()
		uids.sort()
		if trade_state == 3 and not bool(_latest.get("trade_my_locked",false)):
			for raw_uid in uids:
				var uid := int(raw_uid)
				if mine.has(uid):
					continue
				var item: Dictionary = inventory[raw_uid]
				trade_inventory_picker.add_item(_item_catalog.offer_item_text(item))
				trade_inventory_picker.set_item_metadata(trade_inventory_picker.item_count-1,uid)
		var mine_uids: Array = mine.keys()
		mine_uids.sort()
		for raw_uid in mine_uids:
			var item: Dictionary = mine[raw_uid]
			trade_my_offer_picker.add_item(_item_catalog.offer_item_text(item))
			trade_my_offer_picker.set_item_metadata(trade_my_offer_picker.item_count-1,int(raw_uid))
		if trade_inventory_picker.item_count > 0:
			trade_inventory_picker.select(0)
		if trade_my_offer_picker.item_count > 0:
			trade_my_offer_picker.select(0)
		trade_money_input.value = int(_latest.get("trade_my_money",0))
	var peer := int(_latest.get("trade_peer_actor_id",0))
	trade_peer_label.text = ("交易對象：玩家 %d｜%s" % [peer,_trade_state_text(trade_state)]) if trade_id > 0 else "交易對象：—"
	trade_money_label.text = "金錢出價：我 %d｜對方 %d" % [int(_latest.get("trade_my_money",0)),int(_latest.get("trade_other_money",0))]
	var other_rows := PackedStringArray()
	var other: Dictionary = _latest.get("trade_other_items",{})
	var other_uids: Array = other.keys()
	other_uids.sort()
	for raw_uid in other_uids:
		other_rows.append(_item_catalog.offer_item_text(other[raw_uid]))
	trade_other_offer_label.text = "對方出價：" + ("—" if other_rows.is_empty() else "\n".join(other_rows))
	trade_request_button.disabled = not usable or trade_id != 0 or trade_target_picker.item_count == 0
	trade_accept_button.visible = in_world and trade_state == 1
	trade_reject_button.visible = in_world and trade_state == 1
	trade_accept_button.disabled = not usable or trade_state != 1
	trade_reject_button.disabled = not usable or trade_state != 1
	var mutable := usable and trade_state == 3 and not bool(_latest.get("trade_my_locked",false))
	trade_inventory_picker.visible = in_world and trade_state == 3
	trade_my_offer_picker.visible = in_world and trade_state == 3
	trade_offer_add_button.visible = in_world and trade_state == 3
	trade_offer_remove_button.visible = in_world and trade_state == 3
	trade_money_input.visible = in_world and trade_state == 3
	trade_set_money_button.visible = in_world and trade_state == 3
	trade_offer_add_button.disabled = not mutable or trade_inventory_picker.item_count == 0
	trade_offer_remove_button.disabled = not mutable or trade_my_offer_picker.item_count == 0
	trade_money_input.editable = mutable
	trade_set_money_button.disabled = not mutable
	trade_lock_button.visible = in_world and trade_state == 3
	trade_lock_button.disabled = not mutable
	trade_confirm_button.visible = in_world and trade_state == 4
	trade_confirm_button.disabled = not usable or trade_state != 4 or not bool(_latest.get("trade_my_locked",false)) or not bool(_latest.get("trade_other_locked",false)) or bool(_latest.get("trade_my_confirmed",false))
	trade_cancel_button.visible = in_world and trade_id > 0
	trade_cancel_button.disabled = not usable or trade_id <= 0
	var terminal: Dictionary = _latest.get("last_trade_terminal",{})
	if trade_id > 0:
		trade_status.text = "%s｜我%s鎖定／對方%s鎖定｜我%s確認／對方%s確認" % [_trade_state_text(trade_state),"已" if bool(_latest.get("trade_my_locked",false)) else "未","已" if bool(_latest.get("trade_other_locked",false)) else "未","已" if bool(_latest.get("trade_my_confirmed",false)) else "未","已" if bool(_latest.get("trade_other_confirmed",false)) else "未"]
	elif not terminal.is_empty():
		trade_status.text = "%s｜伺服器代碼 %d" % [_trade_terminal_text(int(terminal.get("status",-1))),int(terminal.get("status",-1))]
