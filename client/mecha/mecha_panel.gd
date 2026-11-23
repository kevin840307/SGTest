extends VBoxContainer
## Mecha presentation/controller only. Ownership, equipment legality, slots,
## replay protection and mounted locks remain authoritative on the Server.
const State = preload("res://world/world_state.gd")

const RESULT_NAMES := [
	"已套用",
	"重播：未重複修改",
	"機甲不屬於目前角色",
	"機甲已在騎乘中",
	"機甲目前未騎乘",
	"操作序號衝突",
	"物品不屬於目前角色",
	"此物品不是伺服器核准的機甲裝備",
	"機甲等級不足",
	"騎乘中不可變更機甲裝備",
	"該物品未裝在此機甲",
	"核心部件不可直接卸下",
	"強化卡槽已滿",
	"伺服器尚無可信機甲規則",
	"此電池物品與選中機甲不相容",
	"機甲電量已滿"
]

var active := false
var _latest: Dictionary = {}
var _mount_action: Callable
var _dismount_action: Callable
var _refresh_action: Callable
var _equip_action: Callable
var _unequip_action: Callable
var _charge_action: Callable
var _item_catalog

var mecha_picker := OptionButton.new()
var mount_button := Button.new()
var dismount_button := Button.new()
var refresh_button := Button.new()
var equipment_picker := OptionButton.new()
var inventory_picker := OptionButton.new()
var equip_button := Button.new()
var unequip_button := Button.new()
var charge_button := Button.new()
var status_label := Label.new()
var _display_key := ""

func _ready() -> void:
	mecha_picker.tooltip_text = "伺服器持久化的機甲清單"
	mount_button.text = "騎乘選中機甲"
	dismount_button.text = "離開目前機甲"
	refresh_button.text = "更新機甲資料"
	equipment_picker.tooltip_text = "選中機甲目前由伺服器確認的裝備"
	inventory_picker.tooltip_text = "選擇背包 UID；最終相容性與槽位由伺服器判定"
	equip_button.text = "將背包物品裝到選中機甲"
	unequip_button.text = "卸下選中的機甲裝備"
	charge_button.text = "使用選中背包物品為機甲充能"
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [
		mecha_picker,mount_button,dismount_button,refresh_button,
		equipment_picker,inventory_picker,equip_button,unequip_button,charge_button,status_label
	]:
		control.visible = false
		add_child(control)
	mecha_picker.item_selected.connect(func(_index): _selection_changed())
	mount_button.pressed.connect(_mount_selected)
	dismount_button.pressed.connect(_dismount_current)
	refresh_button.pressed.connect(_refresh_server)
	equip_button.pressed.connect(_equip_selected)
	unequip_button.pressed.connect(_unequip_selected)
	charge_button.pressed.connect(_charge_selected)
	reset()

func set_actions(
	mount_action: Callable,
	dismount_action: Callable,
	refresh_action: Callable,
	equip_action: Callable,
	unequip_action: Callable,
	charge_action: Callable,
	item_catalog
) -> void:
	_mount_action = mount_action
	_dismount_action = dismount_action
	_refresh_action = refresh_action
	_equip_action = equip_action
	_unequip_action = unequip_action
	_charge_action = charge_action
	_item_catalog = item_catalog

func reset() -> void:
	active = false
	_latest = {}
	_display_key = ""
	mecha_picker.clear()
	equipment_picker.clear()
	inventory_picker.clear()
	status_label.text = ""

func apply_snapshot(snapshot: Dictionary, connection_active: bool) -> void:
	_latest = snapshot
	active = connection_active
	_refresh()

func selected_mecha_uid() -> int:
	if mecha_picker.selected < 0 or mecha_picker.item_count == 0:
		return 0
	return int(mecha_picker.get_item_metadata(mecha_picker.selected))

func _selection_changed() -> void:
	_rebuild_equipment_picker(selected_mecha_uid())
	_update_controls()

func _mount_selected() -> void:
	var uid := selected_mecha_uid()
	if mount_button.disabled or uid <= 0:
		return
	if _mount_action.is_valid() and bool(_mount_action.call(uid)):
		status_label.text = "騎乘請求已送出；等待伺服器權威結果…"

func _dismount_current() -> void:
	if dismount_button.disabled:
		return
	if _dismount_action.is_valid() and bool(_dismount_action.call()):
		status_label.text = "下機請求已送出；等待伺服器權威結果…"

func _refresh_server() -> void:
	if refresh_button.disabled:
		return
	if _refresh_action.is_valid() and bool(_refresh_action.call()):
		status_label.text = "機甲資料更新請求已送出…"

func _equip_selected() -> void:
	if equip_button.disabled or inventory_picker.selected < 0:
		return
	var mecha_uid := selected_mecha_uid()
	var item_uid := int(inventory_picker.get_item_metadata(inventory_picker.selected))
	if _equip_action.is_valid() and bool(_equip_action.call(mecha_uid,item_uid)):
		status_label.text = "裝備請求已送出；槽位與相容性由伺服器判定…"

func _unequip_selected() -> void:
	if unequip_button.disabled or equipment_picker.selected < 0:
		return
	var mecha_uid := selected_mecha_uid()
	var item_uid := int(equipment_picker.get_item_metadata(equipment_picker.selected))
	if _unequip_action.is_valid() and bool(_unequip_action.call(mecha_uid,item_uid)):
		status_label.text = "卸下請求已送出；等待伺服器原子移回背包…"

func _charge_selected() -> void:
	if charge_button.disabled or inventory_picker.selected < 0:
		return
	var mecha_uid := selected_mecha_uid()
	var item_uid := int(inventory_picker.get_item_metadata(inventory_picker.selected))
	if _charge_action.is_valid() and bool(_charge_action.call(mecha_uid,item_uid)):
		status_label.text = "充能請求已送出；電池相容性與充能量由伺服器判定…"

func _item_name(item: Dictionary) -> String:
	var definition_id := int(item.get("item_definition_id",0))
	if _item_catalog != null:
		return str(_item_catalog.name_for(definition_id))
	return "Item %d" % definition_id

func _slot_name(slot_id: int) -> String:
	match slot_id:
		1: return "武裝"
		2: return "引擎"
		3,4,5: return "強化卡 %d" % (slot_id - 2)
		6: return "守護石"
		_: return "槽位 %d" % slot_id

func _rebuild_equipment_picker(mecha_uid: int) -> void:
	equipment_picker.clear()
	var equipped: Dictionary = _latest.get("mecha_equipment_items",{})
	var entries: Array = []
	for raw_uid in equipped:
		var item: Dictionary = equipped[raw_uid]
		if int(item.get("mecha_uid",0)) == mecha_uid:
			entries.append(item)
	entries.sort_custom(func(a: Dictionary,b: Dictionary):
		var sa := int(a.get("slot_id",0))
		var sb := int(b.get("slot_id",0))
		return sa < sb if sa != sb else int(a.get("item_uid",0)) < int(b.get("item_uid",0)))
	for item in entries:
		var uid := int(item.get("item_uid",0))
		equipment_picker.add_item("%s｜%s｜UID %d" % [
			_slot_name(int(item.get("slot_id",0))),_item_name(item),uid
		])
		equipment_picker.set_item_metadata(equipment_picker.item_count-1,uid)
	if equipment_picker.item_count > 0:
		equipment_picker.select(0)

func _rebuild_pickers() -> void:
	var previous := selected_mecha_uid()
	var mechas: Dictionary = _latest.get("mecha_instances",{})
	var mounted := int(_latest.get("mounted_mecha_uid",0))
	mecha_picker.clear()
	var ids: Array = mechas.keys()
	ids.sort()
	var selected_index := -1
	for raw_uid in ids:
		var uid := int(raw_uid)
		var mecha: Dictionary = mechas[raw_uid]
		var battery_charge := int(mecha.get("battery_charge",0))
		var battery_capacity := int(mecha.get("battery_capacity",0))
		var battery_text := "電量 %d/%d" % [battery_charge,battery_capacity] if battery_capacity > 0 else "電量 %d/未驗證" % battery_charge
		mecha_picker.add_item("%s機甲 UID %d｜型號 %d｜Lv.%d｜EXP %d｜%s" % [
			"★ " if uid == mounted else "",
			uid,int(mecha.get("definition_id",0)),int(mecha.get("level",0)),
			int(mecha.get("experience",0)),battery_text
		])
		mecha_picker.set_item_metadata(mecha_picker.item_count-1,uid)
		if uid == previous:
			selected_index = mecha_picker.item_count-1
	if selected_index < 0 and mounted > 0:
		for i in mecha_picker.item_count:
			if int(mecha_picker.get_item_metadata(i)) == mounted:
				selected_index = i
				break
	if selected_index < 0 and mecha_picker.item_count > 0:
		selected_index = 0
	if selected_index >= 0:
		mecha_picker.select(selected_index)

	inventory_picker.clear()
	var inventory: Dictionary = _latest.get("inventory_items",{})
	var item_uids: Array = inventory.keys()
	item_uids.sort()
	for raw_uid in item_uids:
		var uid := int(raw_uid)
		var item: Dictionary = inventory[raw_uid]
		inventory_picker.add_item("%s｜UID %d" % [_item_name(item),uid])
		inventory_picker.set_item_metadata(inventory_picker.item_count-1,uid)
	if inventory_picker.item_count > 0:
		inventory_picker.select(0)
	_rebuild_equipment_picker(selected_mecha_uid())

func _result_text() -> String:
	var result: Dictionary = _latest.get("last_mecha_result",{})
	if result.is_empty():
		return "機甲清單與裝備只顯示伺服器已確認狀態"
	var code := int(result.get("status",-1))
	var name: String = str(RESULT_NAMES[code]) if code >= 0 and code < RESULT_NAMES.size() else "未知結果"
	return "%s（代碼 %d，動作 %d）" % [name,code,int(result.get("action",0))]

func _update_controls() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	for control in [
		mecha_picker,mount_button,dismount_button,refresh_button,
		equipment_picker,inventory_picker,equip_button,unequip_button,charge_button,status_label
	]:
		control.visible = in_world
	if not in_world:
		return
	var ready := (
		bool(_latest.get("world_ready",false)) and
		bool(_latest.get("mecha_snapshot_received",false)) and
		bool(_latest.get("mecha_equipment_snapshot_received",false)) and
		not bool(_latest.get("player_dead",false)) and
		not bool(_latest.get("reconnecting",false)) and
		not bool(_latest.get("resync_pending",false)) and
		not bool(_latest.get("waiting_for_new_map",false)) and
		int(_latest.get("pending_world_ready_ack_epoch",0)) == 0 and
		int(_latest.get("last_world_ready_ack_epoch",0)) == int(_latest.get("map_epoch",0))
	)
	var pending := int(_latest.get("pending_mecha_sequence",0)) != 0
	var selected := selected_mecha_uid()
	var mounted := int(_latest.get("mounted_mecha_uid",0))
	mecha_picker.disabled = not ready or pending or mecha_picker.item_count == 0
	refresh_button.disabled = not ready or pending
	mount_button.disabled = not ready or pending or selected <= 0 or selected == mounted
	dismount_button.disabled = not ready or pending or mounted <= 0
	var selected_locked := selected > 0 and selected == mounted
	inventory_picker.disabled = not ready or pending or selected <= 0 or inventory_picker.item_count == 0
	equipment_picker.disabled = not ready or pending or selected <= 0 or selected_locked or equipment_picker.item_count == 0
	equip_button.disabled = inventory_picker.disabled or selected_locked
	unequip_button.disabled = equipment_picker.disabled
	charge_button.disabled = inventory_picker.disabled
	if pending:
		status_label.text = "等待伺服器機甲結果；不可重複送出"
	else:
		status_label.text = _result_text()

func _refresh() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	if not in_world:
		_display_key = ""
		_update_controls()
		return
	var key := "%d/%d/%d/%d/%d/%d/%d/%d" % [
		int(_latest.get("actor_id",0)),int(_latest.get("map_epoch",0)),
		int(_latest.get("mecha_sequence",0)),int(_latest.get("pending_mecha_sequence",0)),
		int(_latest.get("mounted_mecha_uid",0)),
		(_latest.get("mecha_instances",{}) as Dictionary).size(),
		(_latest.get("mecha_equipment_items",{}) as Dictionary).size(),
		int(_latest.get("inventory_revision",0))
	]
	if key != _display_key:
		_display_key = key
		_rebuild_pickers()
	_update_controls()
