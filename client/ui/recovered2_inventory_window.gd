extends PanelContainer
## Recovered2 inventory presentation. Visual structure follows
## shared_inventory_panel.tscn while all authoritative actions remain owned by
## SessionPanel / InventoryPanel. Large bags page their rows so hidden items do
## not keep Controls or textures alive.
const Photos = preload("res://ui/local_item_photo_cache.gd")
const UiTextures = preload("res://ui/ui_texture_loader.gd")
const PRIVATE := "res://ui/private_recovered2"
const TAB_NAMES := ["道具","裝備","寵物","機甲","其他"]
const PAGE_SIZE := 40

var _photos := Photos.new()
var _session_panel
var _catalog
var _snapshot: Dictionary = {}
var _revision_key := ""
var _tab := 0
var _page := 0
var _selected_uid := 0
var _selected_equipped := false

var _buttons: Array[Button] = []
var _rows := VBoxContainer.new()
var _capacity := Label.new()
var _status := Label.new()
var _selected_label := Label.new()
var _page_bar := HBoxContainer.new()
var _page_previous := Button.new()
var _page_next := Button.new()
var _page_label := Label.new()
var _action_bar := HBoxContainer.new()
var _use := Button.new()
var _equip := Button.new()
var _discard := Button.new()
var _search := LineEdit.new() # kept as a non-visual filter API for tests/tools
var _confirm := ConfirmationDialog.new()

func _init() -> void:
	name = "Recovered2InventoryWindow"
	custom_minimum_size = Vector2(300,520)
	mouse_filter = Control.MOUSE_FILTER_STOP
	theme_type_variation = &"ParadiseWindowFrame"
	visible = false

	var root := VBoxContainer.new()
	root.name = "Layout"
	root.add_theme_constant_override("separation",8)
	add_child(root)

	var header := HBoxContainer.new()
	header.name = "Header"
	header.custom_minimum_size.y = 30
	header.add_theme_constant_override("separation",10)
	root.add_child(header)

	var title := Label.new()
	title.name = "TitleLabel"
	title.text = "物品"
	title.theme_type_variation = &"ParadiseWindowTitle"
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	header.add_child(title)

	_capacity.name = "LblCapacity"
	_capacity.text = "剩餘空間 --"
	_capacity.theme_type_variation = &"ParadiseSecondaryText"
	_capacity.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	header.add_child(_capacity)

	var close := TextureButton.new()
	close.name = "CloseInventory"
	close.custom_minimum_size = Vector2(22,22)
	close.focus_mode = Control.FOCUS_NONE
	close.ignore_texture_size = true
	close.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	close.texture_normal = UiTextures.get_texture(PRIVATE.path_join("CLOSESCREEN.png"))
	close.pressed.connect(hide)
	header.add_child(close)

	var separator := HSeparator.new()
	separator.theme_type_variation = &"ParadiseHeaderSeparator"
	root.add_child(separator)

	var tabs := HBoxContainer.new()
	tabs.name = "TabBar"
	tabs.add_theme_constant_override("separation",5)
	root.add_child(tabs)
	for i in TAB_NAMES.size():
		var button := Button.new()
		button.name = "InventoryTab%d" % i
		button.text = TAB_NAMES[i]
		button.focus_mode = Control.FOCUS_NONE
		button.toggle_mode = true
		button.theme_type_variation = &"ParadiseTabButton"
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.pressed.connect(_change_tab.bind(i))
		tabs.add_child(button)
		_buttons.append(button)
	_buttons[0].button_pressed = true

	# Recovered2 does not expose SGTest's diagnostic search field. Keep it
	# detached from layout so automated filtering remains available without
	# changing source appearance.
	_search.name = "ItemSearch"
	_search.visible = false
	_search.text_changed.connect(func(_value): _page = 0; _revision_key = ""; _refresh())
	add_child(_search)

	var content := PanelContainer.new()
	content.name = "Content"
	content.theme_type_variation = &"ParadiseContentPanel"
	content.size_flags_vertical = Control.SIZE_EXPAND_FILL
	root.add_child(content)

	var scroll := ScrollContainer.new()
	scroll.name = "ItemScroll"
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content.add_child(scroll)
	_rows.name = "VisibleItemRows"
	_rows.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_rows.add_theme_constant_override("separation",6)
	scroll.add_child(_rows)

	# Paging exists only when needed. This keeps the visual one-page case
	# identical to Recovered2 while bounding resource use for large inventories.
	_page_bar.name = "InventoryPagination"
	_page_bar.add_theme_constant_override("separation",5)
	_page_bar.visible = false
	root.add_child(_page_bar)
	_page_previous.text = "◀"
	_page_previous.name = "PreviousPage"
	_page_previous.custom_minimum_size.x = 36
	_page_previous.pressed.connect(_navigate_page.bind(-1))
	_page_bar.add_child(_page_previous)
	_page_label.text = "1 / 1"
	_page_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_page_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_page_bar.add_child(_page_label)
	_page_next.text = "▶"
	_page_next.name = "NextPage"
	_page_next.custom_minimum_size.x = 36
	_page_next.pressed.connect(_navigate_page.bind(1))
	_page_bar.add_child(_page_next)

	# Source rows are the primary interaction. Additional SGTest actions only
	# appear after selection; they no longer permanently alter the empty bag.
	_selected_label.name = "SelectedItemDetail"
	_selected_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_selected_label.add_theme_font_size_override("font_size",11)
	_selected_label.visible = false
	root.add_child(_selected_label)

	_action_bar.name = "SelectedItemActions"
	_action_bar.add_theme_constant_override("separation",4)
	_action_bar.visible = false
	root.add_child(_action_bar)
	for pair in [["使用",_use],["裝備／卸下",_equip],["丟棄",_discard]]:
		var button: Button = pair[1]
		button.text = pair[0]
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.focus_mode = Control.FOCUS_NONE
		_action_bar.add_child(button)
	_use.pressed.connect(_use_selected)
	_equip.pressed.connect(_equip_selected)
	_discard.pressed.connect(_ask_discard)

	_status.name = "InventoryStatus"
	_status.add_theme_font_size_override("font_size",11)
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.visible = false
	root.add_child(_status)

	_confirm.title = "確認丟棄背包物品"
	_confirm.dialog_text = "將永久銷毀所選物品 1 個，確定繼續？"
	_confirm.confirmed.connect(_discard_selected)
	add_child(_confirm)
	_update_actions()

func bind_backend(panel, catalog) -> void:
	_session_panel = panel
	_catalog = catalog

func apply_snapshot(snapshot: Dictionary) -> void:
	_snapshot = snapshot
	var key := "%d:%d:%d:%d:%d:%d" % [
		int(snapshot.get("map_epoch",0)),int(snapshot.get("inventory_revision",-1)),
		int(snapshot.get("equipment_revision",-1)),int(snapshot.get("last_discard_result_sequence",0)),
		int(snapshot.get("last_recovery_result_sequence",0)),int(snapshot.get("last_equipment_result_sequence",0))]
	if key != _revision_key:
		_revision_key = key
		_refresh()

func _navigate_page(delta: int) -> void:
	_page = maxi(0,_page+delta)
	_refresh()

func _change_tab(index: int) -> void:
	_tab = clampi(index,0,TAB_NAMES.size()-1)
	_page = 0
	for i in _buttons.size():
		_buttons[i].button_pressed = i == _tab
	_selected_uid = 0
	_selected_equipped = false
	_revision_key = ""
	_refresh()

func _matches_tab(item: Dictionary, equipped: bool) -> bool:
	if equipped:
		return _tab == 1
	var category := str(_catalog.category_for(int(item.get("item_definition_id",0)))) if _catalog != null else ""
	match _tab:
		0: return category in ["general","recovery","booster",""]
		1: return category in ["equip","weapon","horseequip"]
		2,3: return false # no pet/mecha inventory authority in SGTest yet
		4: return category in ["horse","horsebattery"]
	return false

func _refresh() -> void:
	if not is_inside_tree():
		return
	for row in _rows.get_children():
		_rows.remove_child(row)
		row.queue_free()
	var inventory: Dictionary = _snapshot.get("inventory_items",{})
	var equipment: Dictionary = _snapshot.get("equipment_items",{})
	var present_uids := {}
	var search := _search.text.strip_edges().to_lower()
	var entries: Array = []
	if not bool(_snapshot.get("inventory_ready",false)):
		_status.text = "正在等待伺服器提供背包資料"
		_status.visible = true
	else:
		_status.text = ""
		_status.visible = false
	for is_equipped in [false,true]:
		var origin: Dictionary = equipment if is_equipped else inventory
		for key in origin:
			var item: Dictionary = origin[key]
			if not _matches_tab(item,is_equipped):
				continue
			var item_id := int(item.get("item_definition_id",0))
			var uid := int(item.get("item_uid",key))
			var item_name := str(_catalog.name_for(item_id)) if _catalog != null else "道具 %d" % item_id
			if not search.is_empty() and not (search in item_name.to_lower() or search in str(item_id)):
				continue
			present_uids[uid] = true
			entries.append({"item":item,"name":item_name,"uid":uid,"equipped":is_equipped})
	entries.sort_custom(func(a: Dictionary,b: Dictionary): return int(a.uid) < int(b.uid))
	var page_count := maxi(1,ceili(float(entries.size())/float(PAGE_SIZE)))
	_page = clampi(_page,0,page_count-1)
	_page_label.text = "%d / %d" % [_page+1,page_count]
	_page_previous.disabled = _page == 0
	_page_next.disabled = _page+1 >= page_count
	_page_bar.visible = page_count > 1
	var first := _page*PAGE_SIZE
	for i in range(first,mini(first+PAGE_SIZE,entries.size())):
		_add_row(entries[i])
	# MaxCharacters/slot capacity is not currently in the inventory wire. Keep
	# truthful owned-count wording rather than inventing a capacity.
	_capacity.text = "%d 件" % inventory.size()
	if entries.is_empty():
		var empty := Label.new()
		empty.name = "InventoryEmptyState"
		empty.text = "目前沒有此類物品" if _tab not in [2,3] else "尚未開放此類物品"
		empty.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		empty.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		empty.size_flags_vertical = Control.SIZE_EXPAND_FILL
		empty.add_theme_color_override("font_color",Color("#93a9c1"))
		empty.add_theme_font_size_override("font_size",14)
		_rows.add_child(empty)
	if _selected_uid > 0 and not present_uids.has(_selected_uid):
		_selected_uid = 0
		_selected_equipped = false
	_update_actions()

func _add_row(entry: Dictionary) -> void:
	var item: Dictionary = entry.item
	var item_id := int(item.get("item_definition_id",0))
	var uid := int(entry.uid)
	var equipped := bool(entry.equipped)
	var button := Button.new()
	button.name = "ItemUid%d" % uid
	button.custom_minimum_size.y = 49
	button.theme_type_variation = &"ParadiseInventoryRow"
	button.focus_mode = Control.FOCUS_NONE
	button.toggle_mode = true
	button.button_pressed = _selected_uid == uid and _selected_equipped == equipped
	button.pressed.connect(func():
		_selected_uid = uid
		_selected_equipped = equipped
		for row in _rows.get_children():
			if row is Button:
				row.button_pressed = row == button
		_update_actions())
	_rows.add_child(button)

	var row_content := HBoxContainer.new()
	row_content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row_content.add_theme_constant_override("separation",7)
	button.add_child(row_content)
	row_content.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	row_content.offset_left = 6.0
	row_content.offset_right = -6.0

	var frame := PanelContainer.new()
	frame.custom_minimum_size = Vector2(40,40)
	frame.mouse_filter = Control.MOUSE_FILTER_IGNORE
	frame.theme_type_variation = &"ParadiseItemSlotContainer"
	row_content.add_child(frame)
	var image := TextureRect.new()
	image.name = "Photo"
	image.custom_minimum_size = Vector2(36,36)
	image.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	image.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	image.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	image.mouse_filter = Control.MOUSE_FILTER_IGNORE
	image.texture = _photos.photo_for_item(item_id,_catalog)
	frame.add_child(image)

	var labels := VBoxContainer.new()
	labels.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	labels.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row_content.add_child(labels)
	var item_name := Label.new()
	item_name.text = str(entry.name)
	item_name.mouse_filter = Control.MOUSE_FILTER_IGNORE
	item_name.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	item_name.add_theme_font_size_override("font_size",12)
	if _catalog != null and _catalog.has_name_color_for(item_id):
		item_name.add_theme_color_override("font_color",_catalog.name_color_for(item_id))
	labels.add_child(item_name)
	var meta := Label.new()
	var level := int(item.get("enhancement_level",0))
	meta.text = "x%d%s%s" % [
		int(item.get("quantity",1)),
		("   +%d" % level) if level > 0 else "",
		"  已裝備" if equipped else ""]
	meta.mouse_filter = Control.MOUSE_FILTER_IGNORE
	meta.add_theme_font_size_override("font_size",11)
	meta.add_theme_color_override("font_color",Color("#e0c57b"))
	labels.add_child(meta)
	var equipped_items: Dictionary = _snapshot.get("equipment_items",{})
	button.tooltip_text = _catalog.tooltip_text_for(item,equipped_items if not equipped else {}) if _catalog != null else "%s\nUID %d · 道具 ID %d" % [str(entry.name),uid,int(item.get("item_definition_id",0))]

func _selected_item() -> Dictionary:
	if _selected_uid <= 0:
		return {}
	var entries: Dictionary = _snapshot.get("equipment_items",{}) if _selected_equipped else _snapshot.get("inventory_items",{})
	if entries.has(_selected_uid):
		return entries[_selected_uid]
	for key in entries:
		if int(key) == _selected_uid:
			return entries[key]
	return {}

func _update_actions() -> void:
	var item := _selected_item()
	var valid := not item.is_empty() and _session_panel != null
	var item_id := int(item.get("item_definition_id",0))
	var category := str(_catalog.category_for(item_id)) if _catalog != null else ""
	_use.disabled = not valid or _selected_equipped or category != "recovery"
	_equip.disabled = not valid or (not _selected_equipped and category not in ["equip","weapon","horseequip"])
	_discard.disabled = not valid or _selected_equipped
	_selected_label.visible = valid
	_action_bar.visible = valid
	_selected_label.text = _catalog.offer_item_text(item) if valid and _catalog != null else ""

func _use_selected() -> void:
	if not _use.disabled and _session_panel.inventory_panel.submit_recovery_use(_selected_uid):
		_status.text = "已送出道具使用要求，等待伺服器確認"
	else:
		_status.text = "目前無法使用這個道具"
	_status.visible = true

func _equip_selected() -> void:
	if _equip.disabled:
		return
	var ok: bool = _session_panel.inventory_panel.submit_equipment_unequip(_selected_uid) if _selected_equipped else _session_panel.inventory_panel.submit_equipment_equip(_selected_uid)
	_status.text = "等待伺服器確認裝備" if ok else "目前無法變更裝備"
	_status.visible = true

func _ask_discard() -> void:
	if not _discard.disabled:
		_confirm.popup_centered()

func _discard_selected() -> void:
	if not _discard.disabled and _session_panel.inventory_panel.submit_inventory_discard(_selected_uid,1):
		_status.text = "等待伺服器確認丟棄"
	else:
		_status.text = "目前無法丟棄"
	_status.visible = true

func clear() -> void:
	_snapshot = {}
	_revision_key = ""
	_selected_uid = 0
	_selected_equipped = false
	_page = 0
	_photos.clear()
	_refresh()
