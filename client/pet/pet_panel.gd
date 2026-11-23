extends VBoxContainer
## Pet presentation/controller only. Ownership, equipment legality, slots,
## replay protection and physical ItemUid movement remain authoritative on Server.
## Old SGOPetSys confirms Pet Equip/GetAllEquip behavior, but exact HK slot names
## remain NOT VERIFIED, so the UI deliberately displays generic "槽位 N".
const State = preload("res://world/world_state.gd")

const RESULT_NAMES := [
	"已套用",
	"重播：未重複修改",
	"寵物不屬於目前角色",
	"寵物已召喚",
	"寵物目前未召喚",
	"操作序號衝突",
	"物品不屬於目前角色",
	"此物品不是伺服器核准的寵物裝備",
	"寵物等級不足",
	"該物品未裝在此寵物",
	"伺服器尚無可信寵物裝備規則"
]

var active := false
var _latest: Dictionary = {}
var _summon_action: Callable
var _recall_action: Callable
var _refresh_action: Callable
var _equip_action: Callable
var _unequip_action: Callable
var _item_catalog

var pet_picker := OptionButton.new()
var summon_button := Button.new()
var recall_button := Button.new()
var refresh_button := Button.new()
var equipment_picker := OptionButton.new()
var inventory_picker := OptionButton.new()
var equip_button := Button.new()
var unequip_button := Button.new()
var status_label := Label.new()
var _display_key := ""

func _ready() -> void:
	pet_picker.tooltip_text = "伺服器持久化的寵物清單"
	summon_button.text = "召喚選中寵物"
	recall_button.text = "收回目前寵物"
	refresh_button.text = "更新寵物資料"
	equipment_picker.tooltip_text = "選中寵物目前由伺服器確認的裝備"
	inventory_picker.tooltip_text = "選擇背包 UID；最終相容性與槽位由伺服器判定"
	equip_button.text = "將背包物品裝到選中寵物"
	unequip_button.text = "卸下選中的寵物裝備"
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [
		pet_picker,summon_button,recall_button,refresh_button,
		equipment_picker,inventory_picker,equip_button,unequip_button,status_label
	]:
		control.visible = false
		add_child(control)
	pet_picker.item_selected.connect(func(_index): _selection_changed())
	summon_button.pressed.connect(_summon_selected)
	recall_button.pressed.connect(_recall_current)
	refresh_button.pressed.connect(_refresh_server)
	equip_button.pressed.connect(_equip_selected)
	unequip_button.pressed.connect(_unequip_selected)
	reset()

func set_actions(
	summon_action: Callable,
	recall_action: Callable,
	refresh_action: Callable,
	equip_action: Callable,
	unequip_action: Callable,
	item_catalog
) -> void:
	_summon_action = summon_action
	_recall_action = recall_action
	_refresh_action = refresh_action
	_equip_action = equip_action
	_unequip_action = unequip_action
	_item_catalog = item_catalog

func reset() -> void:
	active = false
	_latest = {}
	_display_key = ""
	pet_picker.clear()
	equipment_picker.clear()
	inventory_picker.clear()
	status_label.text = ""

func apply_snapshot(snapshot: Dictionary, connection_active: bool) -> void:
	_latest = snapshot
	active = connection_active
	_refresh()

func selected_pet_uid() -> int:
	if pet_picker.selected < 0 or pet_picker.item_count == 0:
		return 0
	return int(pet_picker.get_item_metadata(pet_picker.selected))

func _selection_changed() -> void:
	_rebuild_equipment_picker(selected_pet_uid())
	_update_controls()

func _summon_selected() -> void:
	var uid := selected_pet_uid()
	if summon_button.disabled or uid <= 0:
		return
	if _summon_action.is_valid() and bool(_summon_action.call(uid)):
		status_label.text = "召喚請求已送出；等待伺服器權威結果…"

func _recall_current() -> void:
	if recall_button.disabled:
		return
	if _recall_action.is_valid() and bool(_recall_action.call()):
		status_label.text = "收回請求已送出；等待伺服器權威結果…"

func _refresh_server() -> void:
	if refresh_button.disabled:
		return
	if _refresh_action.is_valid() and bool(_refresh_action.call()):
		status_label.text = "寵物資料更新請求已送出…"

func _equip_selected() -> void:
	if equip_button.disabled or inventory_picker.selected < 0:
		return
	var pet_uid := selected_pet_uid()
	var item_uid := int(inventory_picker.get_item_metadata(inventory_picker.selected))
	if _equip_action.is_valid() and bool(_equip_action.call(pet_uid,item_uid)):
		status_label.text = "裝備請求已送出；槽位與相容性由伺服器判定…"

func _unequip_selected() -> void:
	if unequip_button.disabled or equipment_picker.selected < 0:
		return
	var pet_uid := selected_pet_uid()
	var item_uid := int(equipment_picker.get_item_metadata(equipment_picker.selected))
	if _unequip_action.is_valid() and bool(_unequip_action.call(pet_uid,item_uid)):
		status_label.text = "卸下請求已送出；等待伺服器原子移回背包…"

func _item_name(item: Dictionary) -> String:
	var definition_id := int(item.get("item_definition_id",0))
	if _item_catalog != null:
		return str(_item_catalog.name_for(definition_id))
	return "Item %d" % definition_id

func _slot_name(slot_id: int) -> String:
	return "槽位 %d" % slot_id

func _rebuild_equipment_picker(pet_uid: int) -> void:
	equipment_picker.clear()
	var equipped: Dictionary = _latest.get("pet_equipment_items",{})
	var entries: Array = []
	for raw_uid in equipped:
		var item: Dictionary = equipped[raw_uid]
		if int(item.get("pet_uid",0)) == pet_uid:
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
	var previous := selected_pet_uid()
	var pets: Dictionary = _latest.get("pet_instances",{})
	var summoned := int(_latest.get("active_pet_uid",0))
	pet_picker.clear()
	var ids: Array = pets.keys()
	ids.sort()
	var selected_index := -1
	for raw_uid in ids:
		var uid := int(raw_uid)
		var pet: Dictionary = pets[raw_uid]
		pet_picker.add_item("%s寵物 UID %d｜種類 %d｜進化 %d｜Lv.%d｜EXP %d" % [
			"★ " if uid == summoned else "",
			uid,int(pet.get("definition_id",0)),int(pet.get("evolution_id",0)),
			int(pet.get("level",0)),int(pet.get("experience",0))
		])
		pet_picker.set_item_metadata(pet_picker.item_count-1,uid)
		if uid == previous:
			selected_index = pet_picker.item_count-1
	if selected_index < 0 and summoned > 0:
		for i in pet_picker.item_count:
			if int(pet_picker.get_item_metadata(i)) == summoned:
				selected_index = i
				break
	if selected_index < 0 and pet_picker.item_count > 0:
		selected_index = 0
	if selected_index >= 0:
		pet_picker.select(selected_index)

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
	_rebuild_equipment_picker(selected_pet_uid())

func _result_text() -> String:
	var result: Dictionary = _latest.get("last_pet_result",{})
	if result.is_empty():
		return "寵物清單與裝備只顯示伺服器已確認狀態"
	var code := int(result.get("status",-1))
	var name: String = str(RESULT_NAMES[code]) if code >= 0 and code < RESULT_NAMES.size() else "未知結果"
	return "%s（代碼 %d，動作 %d）" % [name,code,int(result.get("action",0))]

func _update_controls() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	for control in [
		pet_picker,summon_button,recall_button,refresh_button,
		equipment_picker,inventory_picker,equip_button,unequip_button,status_label
	]:
		control.visible = in_world
	if not in_world:
		return
	var ready := (
		bool(_latest.get("world_ready",false)) and
		bool(_latest.get("pet_snapshot_received",false)) and
		bool(_latest.get("pet_equipment_snapshot_received",false)) and
		not bool(_latest.get("player_dead",false)) and
		not bool(_latest.get("reconnecting",false)) and
		not bool(_latest.get("resync_pending",false)) and
		not bool(_latest.get("waiting_for_new_map",false)) and
		int(_latest.get("pending_world_ready_ack_epoch",0)) == 0 and
		int(_latest.get("last_world_ready_ack_epoch",0)) == int(_latest.get("map_epoch",0))
	)
	var pending := int(_latest.get("pending_pet_sequence",0)) != 0
	var selected := selected_pet_uid()
	var summoned := int(_latest.get("active_pet_uid",0))
	pet_picker.disabled = not ready or pending or pet_picker.item_count == 0
	refresh_button.disabled = not ready or pending
	summon_button.disabled = not ready or pending or selected <= 0 or selected == summoned
	recall_button.disabled = not ready or pending or summoned <= 0
	inventory_picker.disabled = not ready or pending or selected <= 0 or inventory_picker.item_count == 0
	equipment_picker.disabled = not ready or pending or selected <= 0 or equipment_picker.item_count == 0
	equip_button.disabled = inventory_picker.disabled
	unequip_button.disabled = equipment_picker.disabled
	if pending:
		status_label.text = "等待伺服器寵物結果；不可重複送出"
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
		int(_latest.get("pet_sequence",0)),int(_latest.get("pending_pet_sequence",0)),
		int(_latest.get("active_pet_uid",0)),
		(_latest.get("pet_instances",{}) as Dictionary).size(),
		(_latest.get("pet_equipment_items",{}) as Dictionary).size(),
		int(_latest.get("inventory_revision",0))
	]
	if key != _display_key:
		_display_key = key
		_rebuild_pickers()
	_update_controls()
