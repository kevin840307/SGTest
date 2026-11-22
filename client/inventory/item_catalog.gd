extends RefCounted
## Presentation metadata from privately converted source item definitions.
## Never derive gameplay authority, ownership, prices, recovery, damage or equip legality here.
const CATALOG_PATH := "res://content/item_catalog.json"
const MAX_BYTES := 6 * 1024 * 1024
const MAX_ITEMS := 20000
const CATEGORIES := [
	"booster", "equip", "general", "horse", "horsebattery",
	"horseequip", "recovery", "weapon"
]
const PRESENTATION_STRING_FIELDS := [
	"description", "special_effect", "req_job", "req_race", "horse_type",
	"hk_original_tooltip", "hk_grade", "hk_hit_or_block_label"
]
const PRESENTATION_INTEGER_FIELDS := [
	"price", "req_level", "durability", "spr_id", "equip_slot",
	"color_index", "rarity", "atk", "def", "matk", "mdef",
	"attack_range", "tick_count", "req_str", "req_dex", "req_con",
	"req_wis", "max_hp_bonus", "max_mp_bonus", "max_sp_bonus",
	"hit", "flee", "luk", "no_shield", "bullet_id",
	"use_range", "recover_hp", "recover_mp", "recover_sp", "move_speed",
	"body_id", "weapon_id", "engine_id", "max_battery", "battery",
	"atk_bonus", "def_bonus", "matk_bonus", "mdef_bonus", "luk_bonus",
	"hit_bonus", "flee_bonus", "crit_rate", "crit_damage", "aspd",
	"effect_type", "can_trade", "can_sell", "can_store", "can_repair",
	"hk_original_item_id", "hk_original_type", "hk_record_offset",
	"hk_grade_r", "hk_grade_g", "hk_grade_b", "hk_grade_a",
	"hk_equipment_category", "hk_original_max_loss", "hk_tooltip_loss_insert",
	"hk_attack", "hk_defense", "hk_magic_attack", "hk_magic_defense",
	"hk_hit_or_block", "hk_evasion", "hk_luck"
]
const REQUIREMENT_LABELS := {
	"req_level":"等級限制", "req_job":"職業限制", "req_race":"種族限制",
	"req_str":"力量限制", "req_dex":"敏捷限制", "req_con":"體質限制",
	"req_wis":"智力限制"
}
const STAT_LABELS := {
	"atk":"攻擊", "def":"防禦", "matk":"魔攻", "mdef":"魔防",
	"hit":"命中", "flee":"迴避", "luk":"幸運",
	"max_hp_bonus":"生命上限", "max_mp_bonus":"魔力上限", "max_sp_bonus":"SP上限",
	"attack_range":"攻擊距離", "move_speed":"移動速度", "aspd":"攻擊速度",
	"recover_hp":"生命回復", "recover_mp":"魔力回復", "recover_sp":"SP回復",
	"crit_rate":"暴擊率", "crit_damage":"暴擊傷害"
}

var _names := {}
var _icons := {}
var _categories := {}
var _presentation := {}
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


static func _safe_text(value: Variant, multiline: bool, max_length: int) -> bool:
	if typeof(value) != TYPE_STRING:
		return false
	var text := str(value)
	if text.strip_edges().is_empty() or text.length() > max_length:
		return false
	for character in text:
		var code := character.unicode_at(0)
		if code == 127:
			return false
		if code < 32 and not (multiline and character in ["\n","\t"]):
			return false
	return true


func _init(load_private_default: bool = true) -> void:
	# Synthetic CI fixtures must never inherit the developer's private
	# source catalog; normal UI construction retains the original behavior.
	if load_private_default and FileAccess.file_exists(CATALOG_PATH):
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
	var version = manifest.get("version",-1)
	if manifest.get("kind","") != "source_derived_item_catalog" or not _whole_number(version,1,2):
		return false
	var schema_version := int(version)
	if typeof(manifest.get("source_reference")) != TYPE_STRING or str(manifest.source_reference).strip_edges().is_empty() or str(manifest.source_reference).length() > 256:
		return false
	var entries = manifest.get("items")
	if typeof(entries) != TYPE_ARRAY or entries.is_empty() or entries.size() > MAX_ITEMS or not _whole_number(manifest.get("item_count"),1,MAX_ITEMS) or int(manifest.item_count) != entries.size():
		return false
	var names := {}
	var icons := {}
	var categories := {}
	var presentation := {}
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
		if id <= 0 or id > 100000000 or names.has(id) or not _safe_text(name,false,128) or category not in CATEGORIES or icon < 0 or icon > 100000000:
			return false
		if path != "%s/%d.tres" % [category,id] or not _sha256_hex(digest):
			return false
		var clean_meta := {}
		if item.has("presentation"):
			if schema_version < 2 or typeof(item.presentation) != TYPE_DICTIONARY:
				return false
			var raw_meta: Dictionary = item.presentation
			if raw_meta.size() > PRESENTATION_STRING_FIELDS.size() + PRESENTATION_INTEGER_FIELDS.size():
				return false
			for raw_key in raw_meta:
				if typeof(raw_key) != TYPE_STRING:
					return false
				var key := str(raw_key)
				var value = raw_meta[raw_key]
				if key in PRESENTATION_STRING_FIELDS:
					if not _safe_text(value,true,4096):
						return false
					var clean_text := str(value).replace("\r\n","\n").replace("\r","\n").strip_edges()
					if key == "hk_grade" and clean_text not in ["S","A","B","C","D"]:
						return false
					if key == "hk_hit_or_block_label" and clean_text not in ["命中率","阻擋率"]:
						return false
					clean_meta[key] = clean_text
				elif key in PRESENTATION_INTEGER_FIELDS:
					var low := -1000000000
					var high := 1000000000
					if key in ["hk_grade_r","hk_grade_g","hk_grade_b","hk_grade_a"]:
						low = 1
						high = 255
					elif key == "hk_equipment_category":
						low = 0
						high = 255
					elif key == "hk_tooltip_loss_insert":
						low = -1
						high = 4096
					elif key in ["hk_attack","hk_defense","hk_magic_attack","hk_magic_defense","hk_hit_or_block","hk_evasion","hk_luck"]:
						low = 0
						high = 65535
					elif key == "hk_original_max_loss":
						low = 0
						high = 100000000
					if not _whole_number(value,low,high):
						return false
					clean_meta[key] = int(value)
				else:
					return false
		names[id] = name.strip_edges()
		icons[id] = icon
		categories[id] = category
		presentation[id] = clean_meta
	# Replace only after the ENTIRE manifest is validated. An invalid second
	# load cannot partially overwrite a previously verified catalog.
	_names = names
	_icons = icons
	_categories = categories
	_presentation = presentation
	source_reference = str(manifest.source_reference)
	loaded = true
	return true


func name_for(item_id: int) -> String:
	return _names.get(item_id,"道具 %d" % item_id)


func icon_id_for(item_id: int) -> int:
	return int(_icons.get(item_id,0))


func category_for(item_id: int) -> String:
	return str(_categories.get(item_id,""))


func presentation_for(item_id: int) -> Dictionary:
	var value = _presentation.get(item_id,{})
	return (value as Dictionary).duplicate(true) if value is Dictionary else {}


func has_name_color_for(item_id: int) -> bool:
	var meta: Dictionary = _presentation.get(item_id,{})
	for key in ["hk_grade_r","hk_grade_g","hk_grade_b","hk_grade_a"]:
		if not meta.has(key):
			return false
	return true


func name_color_for(item_id: int, fallback: Color = Color.WHITE) -> Color:
	var meta: Dictionary = _presentation.get(item_id,{})
	if not has_name_color_for(item_id):
		return fallback
	return Color8(
		int(meta.hk_grade_r),int(meta.hk_grade_g),
		int(meta.hk_grade_b),int(meta.hk_grade_a))


static func display_durability(raw: int) -> int:
	return 0 if raw <= 0 else ceili(float(raw) / 50.0)


func _durability_text(item: Dictionary, meta: Dictionary) -> String:
	var raw := int(item.get("durability_raw",-1))
	if raw < 0:
		return ""
	var current := display_durability(raw)
	var maximum := int(meta.get("durability",0))
	return "%d/%d" % [current,maximum] if maximum > 0 else str(current)


func _hk_structured_stats_match_current(meta: Dictionary) -> bool:
	if int(meta.get("hk_original_type",-1)) not in [0,1] or not meta.has("hk_equipment_category"):
		return true
	for pair in [
		["hk_attack","atk"],["hk_defense","def"],
		["hk_magic_attack","matk"],["hk_magic_defense","mdef"],
		["hk_hit_or_block","hit"],["hk_evasion","flee"],["hk_luck","luk"]
	]:
		# Missing current fields mean the current source definition uses its
		# default zero value. Never let an older HK non-zero value override it.
		if int(meta.get(pair[0],0)) != int(meta.get(pair[1],0)):
			return false
	return true


func _hk_original_with_runtime_loss(item: Dictionary, meta: Dictionary) -> Dictionary:
	var text := str(meta.get("hk_original_tooltip","")).strip_edges()
	if not _hk_structured_stats_match_current(meta):
		return {"text":"","inserted_loss":false}
	var raw := int(item.get("durability_raw",-1))
	var at := int(meta.get("hk_tooltip_loss_insert",-1))
	var hk_max := int(meta.get("hk_original_max_loss",0))
	var current_max := int(meta.get("durability",-1))
	# Cross-version evidence must not override current gameplay data. Reproduce
	# the original dynamic line only when both versions agree on its maximum.
	if text.is_empty() or raw < 0 or at < 0 or hk_max <= 0 or current_max != hk_max:
		return {"text":text,"inserted_loss":false}
	at = clampi(at,0,text.length())
	var line := "損耗值：%d／%d" % [display_durability(raw),hk_max]
	var left := text.substr(0,at)
	var right := text.substr(at)
	if right.begins_with("\n"):
		text = left + "\n" + line + right
	elif right.is_empty():
		text = left + "\n" + line
	else:
		text = left + "\n" + line + "\n" + right
	return {"text":text,"inserted_loss":true}


func offer_item_text(item: Dictionary) -> String:
	var item_id := int(item.get("item_definition_id",0))
	var meta: Dictionary = _presentation.get(item_id,{})
	var parts: Array[String] = ["%s ×%d" % [name_for(item_id),int(item.get("quantity",0))]]
	var level := int(item.get("enhancement_level",0))
	if level > 0:
		parts.append("+%d" % level)
	var durability := _durability_text(item,meta)
	if not durability.is_empty():
		parts.append("耐久 %s" % durability)
	parts.append("UID %d" % int(item.get("item_uid",0)))
	return "｜".join(parts)


func detail_text_for(item: Dictionary, include_identity: bool = true) -> String:
	var item_id := int(item.get("item_definition_id",0))
	var meta: Dictionary = _presentation.get(item_id,{})
	var title := name_for(item_id)
	var level := int(item.get("enhancement_level",0))
	if level > 0:
		title += " +%d" % level
	var lines: Array[String] = [title]
	var hk_result := _hk_original_with_runtime_loss(item,meta)
	var hk_original := str(hk_result.text)
	if not hk_original.is_empty():
		# Static text comes from the exact HK record. The original dynamic
		# loss line is inserted only when HK/current maximums are identical.
		lines.append(hk_original)
		lines.append("數量：%d" % int(item.get("quantity",0)))
		var raw_durability := int(item.get("durability_raw",-1))
		if raw_durability >= 0 and not bool(hk_result.inserted_loss):
			# On version drift, never combine current raw state with an HK max.
			lines.append("目前耐久度：%d" % display_durability(raw_durability))
		if include_identity:
			lines.append("UID %d · 道具 ID %d" % [int(item.get("item_uid",0)),item_id])
		return "\n".join(lines)
	var description := str(meta.get("description","")).strip_edges()
	if not description.is_empty():
		lines.append(description)
	lines.append("數量：%d" % int(item.get("quantity",0)))
	var durability := _durability_text(item,meta)
	if not durability.is_empty():
		lines.append("耐久度：%s" % durability)
	for key in ["req_level","req_job","req_race","req_str","req_dex","req_con","req_wis"]:
		if meta.has(key):
			lines.append("%s：%s" % [REQUIREMENT_LABELS[key],str(meta[key])])
	var stat_parts: Array[String] = []
	for key in ["atk","def","matk","mdef","hit","flee","luk","max_hp_bonus","max_mp_bonus","max_sp_bonus","attack_range","move_speed","aspd","recover_hp","recover_mp","recover_sp","crit_rate","crit_damage"]:
		if meta.has(key):
			stat_parts.append("%s %s" % [STAT_LABELS[key],str(meta[key])])
	if not stat_parts.is_empty():
		lines.append("物品數值：" + "，".join(stat_parts))
	var special := str(meta.get("special_effect","")).strip_edges()
	if not special.is_empty() and special != "無":
		lines.append("特殊效果：" + special)
	if include_identity:
		lines.append("UID %d · 道具 ID %d" % [int(item.get("item_uid",0)),item_id])
	return "\n".join(lines)


func _hk_equipment_comparison_text(item: Dictionary, equipped_items: Dictionary) -> String:
	if equipped_items.is_empty():
		return ""
	var item_id := int(item.get("item_definition_id",0))
	var meta: Dictionary = _presentation.get(item_id,{})
	if int(meta.get("hk_original_type",-1)) not in [0,1] or not meta.has("hk_equipment_category"):
		return ""
	var category := int(meta.get("hk_equipment_category",-1))
	var hit_label := str(meta.get("hk_hit_or_block_label",""))
	if hit_label not in ["命中率","阻擋率"]:
		return ""
	var source_uid := int(item.get("item_uid",0))
	var compatible: Array[Dictionary] = []
	for raw_key in equipped_items:
		var equipped = equipped_items[raw_key]
		if not equipped is Dictionary:
			continue
		var equipped_item: Dictionary = equipped
		var equipped_uid := int(equipped_item.get("item_uid",raw_key))
		if source_uid > 0 and equipped_uid == source_uid:
			continue
		var equipped_meta: Dictionary = _presentation.get(int(equipped_item.get("item_definition_id",0)),{})
		if int(equipped_meta.get("hk_original_type",-1)) not in [0,1]:
			continue
		if int(equipped_meta.get("hk_equipment_category",-2)) != category:
			continue
		if str(equipped_meta.get("hk_hit_or_block_label","")) != hit_label:
			continue
		compatible.append({
			"item":equipped_item,
			"meta":equipped_meta,
			"slot_id":int(equipped_item.get("slot_id",0)),
			"uid":equipped_uid
		})
	if compatible.is_empty():
		return ""
	# The original client walks its equipped-item vector in order and the last
	# compatible category wins. Reproduce that deterministically from the
	# server-owned equipment snapshot using slot first, then UID as a tie-break.
	compatible.sort_custom(func(a: Dictionary,b: Dictionary):
		var a_slot := int(a.slot_id)
		var b_slot := int(b.slot_id)
		return a_slot < b_slot if a_slot != b_slot else int(a.uid) < int(b.uid))
	var equipped_meta: Dictionary = compatible.back().meta
	var rows := [
		["攻擊","atk"],["防禦","def"],["魔攻","matk"],["魔防","mdef"],
		[hit_label,"hit"],["迴避率","flee"],["幸運","luk"]
	]
	var lines: Array[String] = ["裝備後能力值的改變："]
	for row in rows:
		# HK only supplies the original field/category semantics. The numbers are
		# always taken from the current converted item definitions so old-version
		# evidence can never become gameplay authority.
		var delta := int(meta.get(row[1],0)) - int(equipped_meta.get(row[1],0))
		lines.append("%s：%s" % [row[0],("+%d" % delta) if delta >= 0 else str(delta)])
	return "\n".join(lines)


func tooltip_text_for(item: Dictionary, equipped_items: Dictionary = {}) -> String:
	var text := detail_text_for(item,true)
	var comparison := _hk_equipment_comparison_text(item,equipped_items)
	return text if comparison.is_empty() else text + "\n" + comparison
