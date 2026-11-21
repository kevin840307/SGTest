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


static func _whole_number(value: Variant, low: int, high: int) -> bool:
	# Godot JSON may deserialize integer literals as floats; accept exact,
	# bounded whole numbers only. Reject NaN, fractions and out-of-range.
	if typeof(value) != TYPE_INT and typeof(value) != TYPE_FLOAT:
		return false
	var number := float(value)
	return is_finite(number) and number >= float(low) and number <= float(high) and floor(number) == number


static func _sha256_hex(digest: String) -> bool:
	if digest.length() != 64:
		return false
	for i in 64:
		var c := digest.substr(i,1).to_lower()
		if "0123456789abcdef".find(c) == -1:
			return false
	return true


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
	if typeof(entries) != TYPE_ARRAY or entries.is_empty() or entries.size() > MAX_ITEMS or not _whole_number(manifest.get("item_count"),1,MAX_ITEMS) or int(manifest.item_count) != entries.size():
		return false
	var names := {}
	var icons := {}
	var categories := {}
	for raw in entries:
		if typeof(raw) != TYPE_DICTIONARY:
			return false
		var item: Dictionary = raw
		if not _whole_number(item.get("item_id"),1,100000000) or typeof(item.get("name")) != TYPE_STRING or not _whole_number(item.get("icon_id"),0,100000000) or typeof(item.get("category")) != TYPE_STRING or typeof(item.get("source_path")) != TYPE_STRING or typeof(item.get("source_sha256")) != TYPE_STRING:
			return false
		var id := int(item.item_id)
		var name := str(item.name)
		var category := str(item.category)
		var icon := int(item.icon_id)
		var path := str(item.source_path)
		var digest := str(item.source_sha256)
		if id <= 0 or id > 100000000 or names.has(id) or name.strip_edges().is_empty() or name.length() > 128 or category not in CATEGORIES or icon < 0 or icon > 100000000:
			return false
		if path != "%s/%d.tres" % [category,id] or not _sha256_hex(digest):
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


func offer_item_text(item: Dictionary) -> String:
	var level := int(item.get("enhancement_level",0))
	var enhancement := "" if level <= 0 else "｜+%d" % level
	var durability := int(item.get("durability_raw",-1))
	var durability_text := "" if durability < 0 else "｜耐久 raw %d" % durability
	return "%s ×%d%s%s｜UID %d" % [
		name_for(int(item.get("item_definition_id",0))),
		int(item.get("quantity",0)),enhancement,durability_text,
		int(item.get("item_uid",0))]
