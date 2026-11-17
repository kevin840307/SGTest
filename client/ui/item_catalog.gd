extends RefCounted
## Pure presentation metadata from privately converted source Items/*.tres.
## Never derive gameplay effects, ownership, inventory UIDs or pickup grants.
const CATALOG_PATH := "res://content/item_catalog.json"
const MAX_BYTES := 6 * 1024 * 1024
const MAX_ITEMS := 20000
const CATEGORIES := [
	"booster", "equip", "general", "horse", "horsebattery",
	"horseequip", "recovery", "weapon"
]

var _names := {}
var _icons := {}
var _categories := {}
var source_reference := ""
var loaded := false


func _init() -> void:
	if FileAccess.file_exists(CATALOG_PATH):
		var file := FileAccess.open(CATALOG_PATH,FileAccess.READ)
		if file != null and file.get_length() <= MAX_BYTES:
			load_json_text(file.get_as_text())


func load_json_text(content: String) -> bool:
	if content.to_utf8_buffer().size() > MAX_BYTES:
		return false
	var parsed = JSON.parse_string(content)
	if typeof(parsed) != TYPE_DICTIONARY:
		return false
	var manifest: Dictionary = parsed
	if manifest.get("kind","") != "source_derived_item_catalog" or manifest.get("version",-1) != 1:
		return false
	if typeof(manifest.get("source_reference")) != TYPE_STRING or str(manifest.source_reference).strip_edges().is_empty() or str(manifest.source_reference).length() > 256:
		return false
	var entries = manifest.get("items")
	if typeof(entries) != TYPE_ARRAY or entries.is_empty() or entries.size() > MAX_ITEMS or typeof(manifest.get("item_count")) != TYPE_INT or int(manifest.item_count) != entries.size():
		return false
	var names := {}
	var icons := {}
	var categories := {}
	for raw in entries:
		if typeof(raw) != TYPE_DICTIONARY:
			return false
		var item: Dictionary = raw
		if typeof(item.get("item_id")) != TYPE_INT or typeof(item.get("name")) != TYPE_STRING or typeof(item.get("icon_id")) != TYPE_INT or typeof(item.get("category")) != TYPE_STRING or typeof(item.get("source_path")) != TYPE_STRING or typeof(item.get("source_sha256")) != TYPE_STRING:
			return false
		var id := int(item.item_id)
		var name := str(item.name)
		var category := str(item.category)
		var icon := int(item.icon_id)
		var path := str(item.source_path)
		var digest := str(item.source_sha256)
		if id <= 0 or id > 100000000 or names.has(id) or name.strip_edges().is_empty() or name.length() > 128 or category not in CATEGORIES or icon < 0 or icon > 100000000:
			return false
		if path != "%s/%d.tres" % [category,id] or digest.length() != 64 or not digest.is_valid_hex_number():
			return false
		for character in name:
			if character.unicode_at(0) < 32 or character.unicode_at(0) == 127:
				return false
		names[id] = name.strip_edges()
		icons[id] = icon
		categories[id] = category
	# Replace only after the ENTIRE manifest is validated. An invalid second
	# load cannot partially overwrite a previously verified name catalog.
	_names = names
	_icons = icons
	_categories = categories
	source_reference = str(manifest.source_reference)
	loaded = true
	return true


func name_for(item_id: int) -> String:
	return _names.get(item_id,"道具 %d" % item_id)


func icon_id_for(item_id: int) -> int:
	return int(_icons.get(item_id,0))


func category_for(item_id: int) -> String:
	return str(_categories.get(item_id,""))
