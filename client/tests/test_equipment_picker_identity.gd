extends SceneTree
const InventoryPanel = preload("res://inventory/inventory_panel.gd")
const ItemCatalog = preload("res://inventory/item_catalog.gd")

func _snapshot(actor: int, reconnect: int, uid: int) -> Dictionary:
	return {
		"actor_id": actor, "reconnect_count": reconnect,
		"world_ready": true, "inventory_ready": true, "equipment_ready": true,
		"inventory_revision": 1, "equipment_revision": 1, "equipment_sequence": 0,
		"inventory_items": {},
		"equipment_items": {uid: {"item_uid": uid, "item_definition_id": 15001, "slot_id": 2}}
	}

func _expect(panel, uid: int) -> bool:
	return panel.equipment_equipped_picker.item_count == 1 and int(panel.equipment_equipped_picker.get_item_metadata(0)) == uid

func _initialize() -> void:
	var panel = InventoryPanel.new()
	root.add_child(panel)
	await process_frame
	var catalog = ItemCatalog.new(false)
	panel.apply_snapshot(_snapshot(11,0,101),true,true,null,catalog)
	if not _expect(panel,101):
		push_error("EQUIPMENT_PICKER_IDENTITY_FAIL initial")
		quit(1)
		return
	panel.apply_snapshot(_snapshot(12,0,202),true,true,null,catalog)
	if not _expect(panel,202):
		push_error("EQUIPMENT_PICKER_IDENTITY_FAIL actor_change")
		quit(1)
		return
	panel.apply_snapshot(_snapshot(12,1,303),true,true,null,catalog)
	if not _expect(panel,303):
		push_error("EQUIPMENT_PICKER_IDENTITY_FAIL reconnect")
		quit(1)
		return
	panel.queue_free()
	print("EQUIPMENT_PICKER_IDENTITY_PASS actor=REBUILT reconnect=REBUILT")
	quit(0)
