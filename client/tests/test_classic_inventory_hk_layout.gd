extends SceneTree
## Focused 023_ITEM.itf geometry/scroll regression.
const ClassicWorld = preload("res://ui/classic_world_shell.gd")

func _fail(shell: Node, reason: String) -> void:
	shell.queue_free()
	push_error("CLASSIC_INVENTORY_HK_LAYOUT_FAIL "+reason)
	quit(1)

func _initialize() -> void:
	root.size=Vector2i(800,600)
	_run.call_deferred()

func _run() -> void:
	var shell=ClassicWorld.new()
	root.add_child(shell)
	await process_frame
	var inv:Control=shell.inventory
	if inv==null or inv.size!=Vector2(259,506):
		_fail(shell,"BG_ITEM window geometry drifted")
		return
	var expected_y=[21.0,58.0,95.0,132.0,169.0]
	for i in 5:
		var tab:=inv.get_node_or_null("InventoryTab%d"%i) as TextureButton
		if tab==null or tab.position!=Vector2(0,expected_y[i]) or tab.size!=Vector2(35,37):
			_fail(shell,"023_ITEM tab %d rect drifted"%i)
			return
	var up:=inv.get_node_or_null("InventoryScrollUp") as TextureButton
	var down:=inv.get_node_or_null("InventoryScrollDown") as TextureButton
	if up==null or down==null or up.position!=Vector2(204,60) or up.size!=Vector2(17,36) or down.position!=Vector2(204,459) or down.size!=Vector2(17,36):
		_fail(shell,"023_ITEM scroll button rect drifted")
		return
	if shell._inventory_rows.position!=Vector2(42,62) or shell._inventory_rows.size!=Vector2(152,432) or ClassicWorld.ITEM_ROW_COUNT!=16:
		_fail(shell,"023_ITEM 16-row hit area drifted")
		return
	(inv.get_node("InventoryTab2") as TextureButton).pressed.emit()
	if shell._inventory_tab!=3:
		_fail(shell,"PET visual tab no longer maps to logical pet category")
		return
	(inv.get_node("InventoryTab3") as TextureButton).pressed.emit()
	if shell._inventory_tab!=4:
		_fail(shell,"RIDE visual tab no longer maps to logical ride category")
		return
	(inv.get_node("InventoryTab4") as TextureButton).pressed.emit()
	if shell._inventory_tab!=2:
		_fail(shell,"ARMOR visual tab no longer maps to logical mecha/armor category")
		return
	(inv.get_node("InventoryTab0") as TextureButton).pressed.emit()
	if shell._inventory_tab!=0:
		_fail(shell,"ITEM visual tab no longer maps to logical general category")
		return
	var items:Dictionary={}
	for uid in range(1,19):
		# Deliberately make definition IDs distinct from inventory UIDs. Original
		# CItemItf::FilterList sorts by CSGOMap::CompareItem (definition ID desc).
		items[uid]={"item_definition_id":1000+uid,"quantity":uid}
	shell._stats={"inventory_items":items,"equipment_items":{}}
	shell._refresh_inventory()
	if shell._inventory_rows.get_child_count()!=16 or not up.disabled or down.disabled:
		_fail(shell,"top page did not expose exactly 16 rows with only down enabled")
		return
	var first:=shell._inventory_rows.get_child(0) as Button
	var last:=shell._inventory_rows.get_child(15) as Button
	if first==null or last==null or not first.text.contains("Item 1018") or not last.text.contains("Item 1003"):
		_fail(shell,"top page did not preserve original definition-id descending order")
		return
	shell._scroll_inventory(100)
	if int(inv.get_meta("item_scroll",-1))!=2 or up.disabled or not down.disabled:
		_fail(shell,"bottom page clamp/scroll button state drifted")
		return
	first=shell._inventory_rows.get_child(0) as Button
	last=shell._inventory_rows.get_child(15) as Button
	if first==null or last==null or not first.text.contains("Item 1016") or not last.text.contains("Item 1001"):
		_fail(shell,"bottom page did not expose original sorted tail 1016..1001")
		return
	shell._scroll_inventory(-100)
	if int(inv.get_meta("item_scroll",-1))!=0 or not up.disabled or down.disabled:
		_fail(shell,"return-to-top clamp/scroll button state drifted")
		return
	shell.queue_free()
	await process_frame
	print("CLASSIC_INVENTORY_HK_LAYOUT_PASS tabs=5 rows=16 row_step=27 scroll_range=0..2 rects=023_ITEM.itf")
	quit(0)
