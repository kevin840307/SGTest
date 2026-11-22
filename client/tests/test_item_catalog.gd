extends SceneTree
const ItemCatalog = preload("res://inventory/item_catalog.gd")


func _fail(message: String) -> void:
	push_error("ITEM_CATALOG_FAIL " + message)
	quit(1)


func _entry(id: int, name: String, category: String, icon: int, presentation := {}) -> Dictionary:
	var entry := {
		"item_id":id,"name":name,"category":category,"icon_id":icon,
		"source_path":"%s/%d.tres" % [category,id],
		"source_sha256":"0123456789abcdef".repeat(4)
	}
	if not presentation.is_empty():
		entry["presentation"] = presentation
	return entry


func _manifest(entries: Array, version: int = 1) -> String:
	return JSON.stringify({
		"kind":"source_derived_item_catalog","version":version,
		"source_reference":"ci:converted fixture, not gameplay authority",
		"item_count":entries.size(),"items":entries})


func _initialize() -> void:
	var catalog = ItemCatalog.new(false)
	if catalog.loaded or catalog.name_for(2701) != "道具 2701" or catalog.icon_id_for(2701) != 0:
		_fail("unverified names escaped fallback")
		return

	# Schema v1 remains readable so an older private catalog does not break
	# during a staged client update.
	var legacy := [_entry(2701,"測試地之物","general",421)]
	if not catalog.load_json_text(_manifest(legacy,1)) or catalog.name_for(2701) != "測試地之物":
		_fail("legacy v1 catalog compatibility broken")
		return

	var data := [
		_entry(2701,"測試地之物","general",421,{
			"description":"來源描述\n第二行","move_speed":-50}),
		_entry(15001,"測試之盔","equip",626,{
			"description":"測試裝備描述","atk":257,"durability":50,
			"req_level":54,"special_effect":"測試效果"})
	]
	if not catalog.load_json_text(_manifest(data,2)) or not catalog.loaded:
		_fail("valid v2 synthetic fixture rejected")
		return
	if catalog.name_for(2701) != "測試地之物" or catalog.icon_id_for(2701) != 421 or catalog.category_for(15001) != "equip" or catalog.name_for(9999) != "道具 9999":
		_fail("names, icon ID and unknown fallback mismatch")
		return
	var meta := catalog.presentation_for(15001)
	if int(meta.get("atk",0)) != 257 or int(meta.get("durability",0)) != 50 or int(meta.get("req_level",0)) != 54:
		_fail("presentation metadata lost")
		return
	if ItemCatalog.display_durability(0) != 0 or ItemCatalog.display_durability(1) != 1 or ItemCatalog.display_durability(50) != 1 or ItemCatalog.display_durability(51) != 2 or ItemCatalog.display_durability(80) != 2:
		_fail("server-compatible durability display conversion mismatch")
		return
	var owned := {
		"item_uid":9001,"item_definition_id":15001,"quantity":1,
		"enhancement_level":5,"durability_raw":80
	}
	var offer := catalog.offer_item_text(owned)
	if "耐久 2/50" not in offer or "+5" not in offer or "raw" in offer:
		_fail("owned item summary still exposes raw durability or loses enhancement")
		return
	var detail := catalog.detail_text_for(owned)
	if "測試裝備描述" not in detail or "等級限制：54" not in detail or "攻擊 257" not in detail or "特殊效果：測試效果" not in detail:
		_fail("shared detail presenter missing source values")
		return

	var hk_data = data.duplicate(true)
	hk_data[1]["presentation"]["hk_original_tooltip"] = "HK 原版說明\n等級限制： 54\n物品效果： 攻擊 300"
	hk_data[1]["presentation"]["hk_original_item_id"] = 31207
	hk_data[1]["presentation"]["hk_original_type"] = 0
	hk_data[1]["presentation"]["hk_record_offset"] = 0x480
	hk_data[1]["presentation"]["hk_equipment_category"] = 6
	hk_data[1]["presentation"]["hk_original_max_loss"] = 50
	hk_data[1]["presentation"]["hk_tooltip_loss_insert"] = "HK 原版說明".length()
	hk_data[1]["presentation"]["hk_attack"] = 300
	hk_data[1]["presentation"]["hk_defense"] = 200
	hk_data[1]["presentation"]["hk_magic_attack"] = 100
	hk_data[1]["presentation"]["hk_magic_defense"] = 80
	hk_data[1]["presentation"]["hk_hit_or_block"] = 12
	hk_data[1]["presentation"]["hk_hit_or_block_label"] = "阻擋率"
	hk_data[1]["presentation"]["hk_evasion"] = 9
	hk_data[1]["presentation"]["hk_luck"] = 7
	hk_data[1]["presentation"]["hk_grade"] = "A"
	hk_data[1]["presentation"]["hk_grade_r"] = 255
	hk_data[1]["presentation"]["hk_grade_g"] = 1
	hk_data[1]["presentation"]["hk_grade_b"] = 255
	hk_data[1]["presentation"]["hk_grade_a"] = 255
	# Comparison values must come from the current converted definitions, not
	# from the older HK evidence fields.
	for pair in [["atk",300],["def",200],["matk",100],["mdef",80],["hit",12],["flee",9],["luk",7]]:
		hk_data[1]["presentation"][pair[0]] = pair[1]
	var equipped_presentation := {
		"atk":280,"def":210,"matk":120,"mdef":70,"hit":10,"flee":12,"luk":7,
		"hk_original_item_id":31208,"hk_original_type":0,"hk_record_offset":0x500,
		"hk_equipment_category":6,"hk_hit_or_block_label":"阻擋率"
	}
	hk_data.append(_entry(15002,"已裝備測試盾","equip",627,equipped_presentation))
	if not catalog.load_json_text(_manifest(hk_data,2)):
		_fail("HK evidence presentation fields rejected")
		return
	var hk_detail := catalog.detail_text_for(owned)
	if "HK 原版說明" not in hk_detail or "物品效果： 攻擊 300" not in hk_detail or "測試裝備描述" in hk_detail or "物品數值：" in hk_detail:
		_fail("HK original tooltip did not take presentation priority")
		return
	if "HK 原版說明\n損耗值：2／50\n等級限制： 54" not in hk_detail or "目前耐久度" in hk_detail:
		_fail("HK original loss line was not reproduced at the confirmed insertion point")
		return
	var equipped_owned := {"item_uid":9002,"item_definition_id":15002,"quantity":1,"slot_id":6}
	var comparison_tooltip := catalog.tooltip_text_for(owned,{9002:equipped_owned})
	var expected_comparison := "裝備後能力值的改變：\n攻擊：+20\n防禦：-10\n魔攻：-20\n魔防：+10\n阻擋率：+2\n迴避率：-3\n幸運：+0"
	if expected_comparison not in comparison_tooltip:
		_fail("HK original equipment comparison semantics were not reproduced from current definition values")
		return
	if "裝備後能力值的改變：" in catalog.tooltip_text_for(owned,{9001:owned}):
		_fail("equipped comparison did not skip the same runtime item")
		return
	var drift_data = hk_data.duplicate(true)
	drift_data[1]["presentation"]["hk_original_max_loss"] = 51
	if not catalog.load_json_text(_manifest(drift_data,2)):
		_fail("valid HK version-drift evidence rejected")
		return
	var drift_detail := catalog.detail_text_for(owned)
	if "損耗值：2／51" in drift_detail or "目前耐久度：2" not in drift_detail:
		_fail("cross-version max drift mixed HK presentation into runtime durability")
		return
	var stat_drift_data = hk_data.duplicate(true)
	stat_drift_data[1]["presentation"]["atk"] = 301
	if not catalog.load_json_text(_manifest(stat_drift_data,2)):
		_fail("valid HK stat-drift evidence rejected")
		return
	var stat_drift_detail := catalog.detail_text_for(owned)
	if "HK 原版說明" in stat_drift_detail or "測試裝備描述" not in stat_drift_detail or "攻擊 301" not in stat_drift_detail:
		_fail("old HK stat values overrode the current converted definition")
		return
	if not catalog.load_json_text(_manifest(hk_data,2)):
		_fail("HK evidence fixture could not be restored after drift tests")
		return
	if int(catalog.presentation_for(15001).get("hk_original_item_id",0)) != 31207:
		_fail("HK evidence metadata lost")
		return
	if not catalog.has_name_color_for(15001) or catalog.has_name_color_for(2701):
		_fail("HK grading color availability mismatch")
		return
	var grade_color := catalog.name_color_for(15001)
	if not grade_color.is_equal_approx(Color8(255,1,255,255)):
		_fail("HK original effective grading RGBA mismatch")
		return

	var invalid = data.duplicate(true)
	invalid[1]["item_id"] = 2701
	if catalog.load_json_text(_manifest(invalid,2)):
		_fail("duplicate item ID accepted")
		return
	if catalog.name_for(15001) != "測試之盔":
		_fail("invalid reload overwrote previously accepted catalog")
		return

	invalid = data.duplicate(true)
	invalid[1]["source_path"] = "../private.tres"
	if catalog.load_json_text(_manifest(invalid,2)):
		_fail("source path traversal accepted")
		return
	invalid = data.duplicate(true)
	invalid[1]["name"] = "換行\n偽造"
	if catalog.load_json_text(_manifest(invalid,2)):
		_fail("unsafe multiline source name accepted")
		return
	invalid = data.duplicate(true)
	invalid[1]["source_sha256"] = "no-evidence"
	if catalog.load_json_text(_manifest(invalid,2)):
		_fail("source hash validation missing")
		return
	invalid = data.duplicate(true)
	invalid[1]["presentation"]["unknown_rule"] = 1
	if catalog.load_json_text(_manifest(invalid,2)):
		_fail("unknown presentation field accepted")
		return
	invalid = hk_data.duplicate(true)
	invalid[1]["presentation"]["hk_grade_g"] = 0
	if catalog.load_json_text(_manifest(invalid,2)):
		_fail("invalid HK effective grading channel accepted")
		return
	invalid = hk_data.duplicate(true)
	invalid[1]["presentation"]["hk_attack"] = 65536
	if catalog.load_json_text(_manifest(invalid,2)):
		_fail("out-of-range HK structured stat accepted")
		return
	invalid = hk_data.duplicate(true)
	invalid[1]["presentation"]["hk_hit_or_block_label"] = "猜測欄位"
	if catalog.load_json_text(_manifest(invalid,2)):
		_fail("unverified HK comparison label accepted")
		return

	var private_load_ms := -1.0
	var private_bytes := 0
	if FileAccess.file_exists(ItemCatalog.CATALOG_PATH):
		var private_file := FileAccess.open(ItemCatalog.CATALOG_PATH,FileAccess.READ)
		if private_file != null:
			private_bytes = private_file.get_length()
		var started := Time.get_ticks_usec()
		var private_catalog = ItemCatalog.new(true)
		private_load_ms = float(Time.get_ticks_usec()-started)/1000.0
		if not private_catalog.loaded:
			_fail("full private catalog rejected by runtime validator")
			return

	print("ITEM_CATALOG_PASS schema=V2 legacy=V1_COMPAT metadata=SOURCE_DERIVED durability=DISPLAYED_NOT_RAW authority=SERVER private_bytes=%d private_load_ms=%.3f" % [private_bytes,private_load_ms])
	quit(0)
