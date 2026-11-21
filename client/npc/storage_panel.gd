extends VBoxContainer
## NPC personal storage controls; ItemUid transfers remain Server-authoritative.
const State = preload("res://world/world_state.gd")

var active := false
var _latest: Dictionary = {}
var item_catalog
var _open_action: Callable
var _deposit_action: Callable
var _withdraw_action: Callable
var storage_npc_input := SpinBox.new()
var storage_open_button := Button.new()
var storage_deposit_picker := OptionButton.new()
var storage_withdraw_picker := OptionButton.new()
var storage_deposit_button := Button.new()
var storage_withdraw_button := Button.new()
var storage_status := Label.new()
var _storage_display_key := ""

func _ready() -> void:
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
		add_child(control)
	storage_open_button.pressed.connect(_open_storage_from_input)
	storage_deposit_button.pressed.connect(_deposit_selected_storage_item)
	storage_withdraw_button.pressed.connect(_withdraw_selected_storage_item)
	reset()

func set_actions(open_action: Callable, deposit_action: Callable, withdraw_action: Callable, catalog_ref) -> void:
	_open_action = open_action
	_deposit_action = deposit_action
	_withdraw_action = withdraw_action
	item_catalog = catalog_ref

func reset() -> void:
	active = false
	_latest = {}
	_storage_display_key = ""
	storage_status.text = ""
	storage_deposit_picker.clear()
	storage_withdraw_picker.clear()

func apply_snapshot(snapshot: Dictionary, connection_active: bool) -> void:
	_latest = snapshot
	active = connection_active
	_update_storage_ui()

func _open_storage_from_input() -> void:
	if _open_action.call(int(storage_npc_input.value)):
		storage_status.text = "正在向伺服器確認 NPC 倉庫…"
		storage_open_button.disabled = true
	else:
		storage_status.text = "目前無法開啟倉庫"


func _deposit_selected_storage_item() -> void:
	if storage_deposit_button.disabled or storage_deposit_picker.selected < 0:
		return
	var uid := int(storage_deposit_picker.get_item_metadata(storage_deposit_picker.selected))
	if _deposit_action.call(uid):
		storage_status.text = "整組存入請求已送出，等待伺服器結算…"
		storage_deposit_button.disabled = true
		storage_withdraw_button.disabled = true
	else:
		storage_status.text = "目前無法存入該物品"


func _withdraw_selected_storage_item() -> void:
	if storage_withdraw_button.disabled or storage_withdraw_picker.selected < 0:
		return
	var uid := int(storage_withdraw_picker.get_item_metadata(storage_withdraw_picker.selected))
	if _withdraw_action.call(uid):
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
