extends VBoxContainer
## Presents owned items and equipment from a session snapshot.

var _latest := {}
var _active := false
var _in_world := false
var _session
var _item_catalog

const INVENTORY_PAGE_SIZE := 80
var _inventory_page := 0
var _inventory_display_key := ""
var _pending_discard_confirmation := {}

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
var equipment_inventory_picker := OptionButton.new()
var equipment_equipped_picker := OptionButton.new()
var equipment_equip_button := Button.new()
var equipment_unequip_button := Button.new()
var equipment_status := Label.new()
var _equipment_display_key := ""


func _ready() -> void:
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
	equipment_equip_button.text = "裝上選中背包物品"
	equipment_unequip_button.text = "卸下選中裝備"
	equipment_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [equipment_inventory_picker,equipment_equipped_picker,equipment_equip_button,equipment_unequip_button,equipment_status]:
		control.visible = false
	for control in [inventory_button,inventory_search,inventory_preview,inventory_previous,inventory_page_label,inventory_next,recovery_picker,recovery_button,recovery_status,discard_quantity,discard_all_button,discard_button,discard_status]:
		add_child(control)
	for control in [equipment_inventory_picker,equipment_equipped_picker,equipment_equip_button,equipment_unequip_button,equipment_status]:
		add_child(control)
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
	equipment_equip_button.pressed.connect(_equip_selected_inventory_item)
	equipment_unequip_button.pressed.connect(_unequip_selected_item)


func reset() -> void:
	_latest = {}
	_active = false
	_in_world = false
	_session = null
	_pending_discard_confirmation.clear()
	discard_confirm.hide()
	_inventory_page = 0
	_inventory_display_key = ""
	_equipment_display_key = ""
	inventory_search.clear()
	inventory_preview.clear()
	recovery_picker.clear()
	recovery_status.text = ""
	discard_status.text = ""
	equipment_inventory_picker.clear()
	equipment_equipped_picker.clear()
	equipment_status.text = ""
	_update_inventory_ui()
	_update_equipment_ui()


func apply_snapshot(snapshot: Dictionary, active: bool, in_world: bool, session, item_catalog) -> void:
	_latest = snapshot
	_active = active
	_in_world = in_world
	_session = session
	_item_catalog = item_catalog
	_update_inventory_ui()
	_update_equipment_ui()


func submit_recovery_use(item_uid: int) -> bool:
	return _active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and bool(_latest.get("combat_available",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and int(_latest.get("pending_recovery_sequence",0)) == 0 and int(_latest.get("pending_discard_sequence",0)) == 0 and item_uid > 0 and _session.use_recovery_item(item_uid)


func submit_inventory_discard(item_uid: int, quantity: int = 1) -> bool:
	return _active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("resync_pending",false)) and int(_latest.get("pending_discard_sequence",0)) == 0 and int(_latest.get("pending_recovery_sequence",0)) == 0 and item_uid > 0 and quantity > 0 and quantity <= 1000000 and (_latest.get("inventory_items",{}) as Dictionary).has(item_uid) and quantity <= int(((_latest.get("inventory_items",{}) as Dictionary)[item_uid] as Dictionary).get("quantity",0)) and _session.discard_inventory_item(item_uid,quantity)


func submit_equipment_equip(item_uid: int) -> bool:
	return _active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and bool(_latest.get("equipment_ready",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_equipment_sequence",0)) == 0 and item_uid > 0 and (_latest.get("inventory_items",{}) as Dictionary).has(item_uid) and _session.equip_inventory_item(item_uid)


func submit_equipment_unequip(item_uid: int) -> bool:
	return _active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and bool(_latest.get("equipment_ready",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_equipment_sequence",0)) == 0 and item_uid > 0 and (_latest.get("equipment_items",{}) as Dictionary).has(item_uid) and _session.unequip_item(item_uid)

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
		_item_catalog.name_for(int((items[uid] as Dictionary).item_definition_id)),quantity,uid]
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
	var in_world := _in_world
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
	# Revisions may match across characters or reconnects. Include session identity.
	var key := "%d/%d/%d/%d/%d/%d/%d" % [
		int(_latest.get("actor_id",0)),int(_latest.get("reconnect_count",0)),
		int(_latest.get("inventory_revision",0)),int(_latest.get("equipment_revision",0)),
		int(_latest.get("equipment_sequence",0)),inventory.size(),equipment.size()]
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
				equipment_inventory_picker.add_item("%s｜UID %d" % [_item_catalog.name_for(int(item.get("item_definition_id",0))),uid])
				equipment_inventory_picker.set_item_metadata(equipment_inventory_picker.item_count-1,uid)
			var equipped_uids: Array = equipment.keys()
			equipped_uids.sort()
			for raw_uid in equipped_uids:
				var uid := int(raw_uid)
				var item: Dictionary = equipment[raw_uid]
				equipment_equipped_picker.add_item("裝備欄位 %d｜%s｜UID %d" % [int(item.get("slot_id",0)),_item_catalog.name_for(int(item.get("item_definition_id",0))),uid])
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
			equipment_status.text = "%s完成：裝備欄位 %d｜%s｜UID %d" % ["裝上" if int(result.get("kind",0)) == 1 else "卸下",int(result.get("slot_id",0)),_item_catalog.name_for(int(result.get("item_definition_id",0))),int(result.get("item_uid",0))]
		else:
			equipment_status.text = "裝備操作失敗（伺服器代碼 %d）" % int(result.get("status",-1))
	equipment_equip_button.disabled = not usable or pending or equipment_inventory_picker.item_count == 0
	equipment_unequip_button.disabled = not usable or pending or equipment_equipped_picker.item_count == 0

func _update_inventory_ui() -> void:
	var in_world: bool = _in_world
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
		if query.is_empty() or _item_catalog.name_for(item_id).findn(query) >= 0 or str(item_id).findn(query) >= 0 or _item_catalog.category_for(item_id).findn(query) >= 0:
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
		var display_name: String = _item_catalog.name_for(int(item.item_definition_id))
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
