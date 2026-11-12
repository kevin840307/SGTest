extends Control

# Fully original Godot Control layout. No dependency on TestGoGo UI scripts
# or copyrighted legacy sprites. Use only as a skin/layout development gallery.
const SLOT_COUNT: int = 30
const QUICK_COUNT: int = 8

@onready var _inventory_window: PanelContainer = $InventoryWindow
@onready var _inventory_grid: GridContainer = $InventoryWindow/InventoryContent/InventoryTabs/Items/InventoryGrid
@onready var _selected_item: Label = $InventoryWindow/InventoryContent/SelectedItem
@onready var _quickbar_slots: HBoxContainer = $Quickbar/QuickVBox/Slots

func _ready() -> void:
	for index in range(SLOT_COUNT):
		var button := Button.new()
		button.custom_minimum_size = Vector2(48, 45)
		button.text = str(index + 1) if index < 5 else "·"
		button.tooltip_text = "測試欄位 %s" % str(index + 1)
		button.pressed.connect(_select_item.bind(index + 1))
		_inventory_grid.add_child(button)

	for index in range(QUICK_COUNT):
		var shortcut := Button.new()
		shortcut.custom_minimum_size = Vector2(36, 35)
		shortcut.text = str(index + 1)
		shortcut.tooltip_text = "F%s" % str(index + 1)
		_quickbar_slots.add_child(shortcut)

	$InventoryWindow/InventoryContent/WindowHeader/CloseButton.pressed.connect(_close_inventory)
	$RightMenu/Menu/InventoryButton.pressed.connect(_open_inventory)
	$RightMenu/Menu/CharacterButton.pressed.connect(
		func(): _selected_item.text = "角色資訊視窗尚待原版畫面確認")
	$RightMenu/Menu/SkillButton.pressed.connect(
		func(): _selected_item.text = "技能視窗尚待原版畫面確認")

func _select_item(index: int) -> void:
	_selected_item.text = "物品欄位 %d（尚無原版物品圖像）" % index

func _close_inventory() -> void:
	_inventory_window.hide()

func _open_inventory() -> void:
	_inventory_window.show()

func show_inventory() -> void:
	_open_inventory()

func hide_inventory() -> void:
	_close_inventory()

func item_slot_count() -> int:
	return _inventory_grid.get_child_count()

func quick_slot_count() -> int:
	return _quickbar_slots.get_child_count()
