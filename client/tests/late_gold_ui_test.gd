extends SceneTree

const GALLERY = preload("res://scenes/late_gold_ui_gallery.tscn")
var failures: int = 0

func check(condition: bool, message: String) -> void:
	if not condition:
		failures += 1
		push_error("UI TEST: " + message)

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var gallery: Variant = GALLERY.instantiate()
	root.add_child(gallery)
	await process_frame
	check(gallery.theme != null, "gold theme present")
	check(gallery.item_slot_count() == 30, "inventory grid 30 slots")
	check(gallery.quick_slot_count() == 8, "quickbar 8 slots")
	check(gallery.has_node("StatusDock/Status/HP"), "status HP")
	check(gallery.has_node("ChatDock/ChatColumn/Messages"), "chat")
	check(gallery.has_node("MinimapDock/VBox/MapArea"), "minimap")
	check(gallery.has_node("RightMenu/Menu/InventoryButton"), "right menu")
	check(gallery.get_node("InventoryWindow").visible, "inventory initial visible")
	gallery.hide_inventory()
	check(not gallery.get_node("InventoryWindow").visible, "close inventory")
	gallery.show_inventory()
	check(gallery.get_node("InventoryWindow").visible, "reopen inventory")
	check(gallery.get_node("MinimapDock").anchor_left == 1.0, "minimap right anchored")
	check(gallery.get_node("StatusDock").anchor_bottom == 1.0, "status bottom anchored")
	gallery.queue_free()
	await process_frame
	if failures != 0:
		printerr("Gold UI gallery FAIL: ", failures)
		quit(1)
	else:
		print("Gold UI gallery PASS (original Godot Control/Theme/anchoring/slots)")
		quit(0)
