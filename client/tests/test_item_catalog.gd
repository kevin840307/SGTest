extends SceneTree
const ItemCatalog = preload("res://inventory/item_catalog.gd")


func _fail(message: String) -> void:
	push_error("ITEM_CATALOG_FAIL " + message)
	quit(1)


func _entry(id: int, name: String, category: String, icon: int) -> Dictionary:
	return {
		"item_id":id,"name":name,"category":category,"icon_id":icon,
		"source_path":"%s/%d.tres" % [category,id],
		"source_sha256":"0123456789abcdef".repeat(4)
	}


func _manifest(entries: Array) -> String:
	return JSON.stringify({
		"kind":"source_derived_item_catalog","version":1,
		"source_reference":"ci:converted fixture, not original gameplay",
		"item_count":entries.size(),"items":entries})


func _initialize() -> void:
	var catalog = ItemCatalog.new()
	if catalog.loaded or catalog.name_for(2701) != "道具 2701" or catalog.icon_id_for(2701) != 0:
		_fail("unverified names escaped fallback")
		return
	var data := [_entry(2701,"測試地之物","general",421),
		_entry(15001,"測試之盔","equip",626)]
	if not catalog.load_json_text(_manifest(data)) or not catalog.loaded:
		_fail("valid synthetic fixture rejected")
		return
	if catalog.name_for(2701) != "測試地之物" or catalog.icon_id_for(2701) != 421 or catalog.category_for(15001) != "equip" or catalog.name_for(9999) != "道具 9999":
		_fail("names, icon ID and unknown fallback mismatch")
		return
	var invalid = data.duplicate(true)
	invalid[1]["item_id"] = 2701
	if catalog.load_json_text(_manifest(invalid)):
		_fail("duplicate item ID accepted")
		return
	if catalog.name_for(15001) != "測試之盔":
		_fail("invalid reload overwrote previously accepted catalog")
		return
	invalid = data.duplicate(true)
	invalid[1]["source_path"] = "../private.tres"
	if catalog.load_json_text(_manifest(invalid)):
		_fail("source path traversal accepted")
		return
	invalid = data.duplicate(true)
	invalid[1]["name"] = "換行\n偽造"
	if catalog.load_json_text(_manifest(invalid)):
		_fail("unsafe multiline source name accepted")
		return
	invalid = data.duplicate(true)
	invalid[1]["source_sha256"] = "no-evidence"
	if catalog.load_json_text(_manifest(invalid)):
		_fail("source hash validation missing")
		return
	print("ITEM_CATALOG_PASS provenance=VALIDATED original_content=NOT_COPIED name=SOURCE_DERIVED invalid_reload=PRESERVED")
	quit(0)
