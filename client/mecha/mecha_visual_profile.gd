extends RefCounted
## Source-derived mounted-mecha paper-doll mapping only.
## Ownership, equip legality, mount state, battery and stats remain Server authority.
const ItemCatalog = preload("res://inventory/item_catalog.gd")
const Timeline = preload("res://animation/action_timeline.gd")

const RACE_KEYS := ["b","g","a","r","y"]

var item_catalog := ItemCatalog.new()
var parts: Dictionary = {}
var views: Dictionary = {}
var mecha_type := ""
var body_prefix := ""


func _spr_id(definition_id: int) -> int:
	if definition_id <= 0:
		return 0
	var meta: Dictionary = item_catalog.presentation_for(definition_id)
	return maxi(0,int(meta.get("spr_id",0)))


func _candidate(asset_id: String, prefix: String) -> Dictionary:
	return {"asset":asset_id.to_lower(),"prefix":prefix.to_upper()}


func _fallback_candidates(asset_id: String, prefix: String, race: String) -> Array:
	var values: Array = [_candidate(asset_id,prefix)]
	if race != "b":
		values.append(_candidate(
			asset_id.replace("/"+race+"-","/b-").replace("/"+race+"_","/b_"),
			prefix.replace(race.to_upper()+"-","B-").replace(race.to_upper()+"_","B_")))
	return values


func source_definition(
	presentation: Dictionary,
	appearance: Dictionary,
	equipment_presentation: Dictionary = {}
) -> Dictionary:
	if not item_catalog.loaded or not bool(presentation.get("mounted",false)):
		return {}
	var definition_id := int(presentation.get(
		"definition_id",presentation.get("mecha_definition_id",0)))
	if definition_id <= 0:
		return {}
	var mecha_meta := item_catalog.presentation_for(definition_id)
	var type := str(mecha_meta.get("horse_type","")).to_upper()
	if type not in ["MS","MA"]:
		return {}
	var race_index := int(appearance.get("race",0))
	if race_index < 0 or race_index >= RACE_KEYS.size():
		return {}
	var race:String=str(RACE_KEYS[race_index])
	var prefix:String=race.to_upper()
	var body_definition := int(mecha_meta.get("body_id",0))
	if body_definition <= 0:
		return {}
	var body_spr := _spr_id(body_definition)
	var result_parts := {}
	if type == "MA":
		var ma_name := "ma%02d" % body_spr
		result_parts[9] = {
			"candidates":_fallback_candidates(
				"mecha/ma/standard/%s_%s" % [race,ma_name],
				"%s_%s" % [prefix,ma_name.to_upper()],race),
			"palette_index":int(item_catalog.presentation_for(body_definition).get("color_index",0))
		}
		return {"type":type,"parts":result_parts,"body_slot":9}

	var weapon_definition := int(presentation.get("weapon_definition_id",0))
	var engine_definition := int(presentation.get("engine_definition_id",0))
	var weapon_spr := _spr_id(weapon_definition)
	var leg_spr := _spr_id(engine_definition)
	var core := [
		[9,"msbody",body_spr],
		[12,"msleg",leg_spr],
		[5,"l-msarm",weapon_spr],
		[7,"r-msarm",weapon_spr],
	]
	for spec in core:
		var slot := int(spec[0])
		var part_name := str(spec[1])
		var spr := int(spec[2])
		result_parts[slot] = {
			"candidates":_fallback_candidates(
				"mecha/ms/standard/%s-%s%02d" % [race,part_name,spr],
				"%s-%s%02d" % [prefix,part_name.to_upper(),spr],race),
			"palette_index":0
		}

	# Recovered2 uses the dedicated MS/Head sprites for the visible pilot.
	# MA intentionally omits them unless its separate resource-anchor feature is enabled.
	var slot_defs: Dictionary = equipment_presentation.get("slot_definitions",{})
	var bonnet_definition := int(slot_defs.get(0,slot_defs.get("0",0)))
	if race_index == 3:
		var bonnet_spr := _spr_id(bonnet_definition)
		var bonnet_name := "bonnet%02d" % bonnet_spr
		result_parts[2] = {
			"candidates":_fallback_candidates(
				"mecha/ms/head/%s-%s" % [race,bonnet_name],
				"%s-%s" % [prefix,bonnet_name.to_upper()],race),
			"palette_index":0
		}
	else:
		result_parts[0] = {
			"candidates":_fallback_candidates(
				"mecha/ms/head/%s-head" % race,"%s-HEAD" % prefix,race),
			"palette_index":0
		}
		var hair_index := int(appearance.get("hair_index",0))
		var hair_name := "hair%02d" % hair_index
		result_parts[1] = {
			"candidates":_fallback_candidates(
				"mecha/ms/head/%s-%s" % [race,hair_name],
				"%s-%s" % [prefix,hair_name.to_upper()],race),
			"palette_index":int(appearance.get("hair_color",0))
		}
		if bonnet_definition > 0:
			var bonnet_spr := _spr_id(bonnet_definition)
			var bonnet_name := "bonnet%02d" % bonnet_spr
			result_parts[2] = {
				"candidates":_fallback_candidates(
					"mecha/ms/head/%s-%s" % [race,bonnet_name],
					"%s-%s" % [prefix,bonnet_name.to_upper()],race),
				"palette_index":0
			}
	return {"type":type,"parts":result_parts,"body_slot":9}


func requested_asset_ids(
	presentation: Dictionary,
	appearance: Dictionary,
	equipment_presentation: Dictionary,
	asset_store
) -> Dictionary:
	var definition := source_definition(presentation,appearance,equipment_presentation)
	var ids := {}
	for raw_part in definition.get("parts",{}).values():
		var part: Dictionary = raw_part
		for raw_candidate in part.get("candidates",[]):
			var candidate: Dictionary = raw_candidate
			var asset_id := str(candidate.get("asset",""))
			if not asset_id.is_empty() and asset_store.has_asset(asset_id):
				ids[asset_id] = true
				break
	return ids


func configure(
	assets: Dictionary,
	presentation: Dictionary,
	appearance: Dictionary,
	equipment_presentation: Dictionary
) -> Error:
	parts.clear()
	views.clear()
	mecha_type = ""
	body_prefix = ""
	var definition := source_definition(presentation,appearance,equipment_presentation)
	if definition.is_empty():
		return ERR_INVALID_PARAMETER
	for raw_slot in definition.parts:
		var slot := int(raw_slot)
		var spec: Dictionary = definition.parts[raw_slot]
		var selected := {}
		for raw_candidate in spec.get("candidates",[]):
			var candidate: Dictionary = raw_candidate
			if assets.has(str(candidate.get("asset",""))):
				selected = candidate
				break
		if selected.is_empty():
			if slot in [0,1,2]:
				continue
			return ERR_FILE_NOT_FOUND
		var asset_id := str(selected.asset)
		var manifest: Dictionary = assets[asset_id].get("manifest",{})
		var prefix := str(selected.prefix)
		if not manifest.get("logical_views",{}) is Dictionary:
			return ERR_INVALID_DATA
		parts[slot] = [asset_id,prefix,int(spec.get("palette_index",0))]
		for view_key in manifest.logical_views:
			if not str(view_key).begins_with(prefix+"_"):
				continue
			var view: Dictionary = manifest.logical_views[view_key]
			if not view.has_all(["clip","flip_h","timeline_ticks","loop"]) or 					not manifest.get("clips",{}).has(str(view.clip)):
				return ERR_INVALID_DATA
			var timeline := Timeline.new()
			if timeline.configure_ticks(view.timeline_ticks,bool(view.loop)) != OK:
				return ERR_INVALID_DATA
			views[str(view_key)] = {"view":view.duplicate(true),"timeline":timeline}
	mecha_type = str(definition.type)
	var body: Array = parts.get(int(definition.body_slot),[])
	if body.is_empty():
		return ERR_INVALID_DATA
	body_prefix = str(body[1])
	return OK


func _resolved(part: Array, action: String, facing: int) -> Dictionary:
	var prefix := str(part[1])
	var exact:Dictionary=views.get(prefix+"_"+action+str(facing),{})
	if not exact.is_empty():
		return exact
	# Dedicated MS pilot head sprites are source NS-only. Keep those source
	# sprites idle while the machine body walks/attacks, matching Recovered2.
	return views.get(prefix+"_NS"+str(facing),{})


func parts_at(action: String, facing: int, elapsed_updates: int) -> Array:
	if parts.is_empty() or facing < 1 or facing > 8:
		return []
	var mecha_action := "NR" if action=="NR" else "NA" if action=="AA" else "NS"
	var body: Array = parts.get(9,[])
	if body.is_empty():
		return []
	var body_view: Dictionary = _resolved(body,mecha_action,facing)
	if body_view.is_empty() and mecha_action=="NA":
		mecha_action = "NS"
		body_view = _resolved(body,mecha_action,facing)
	if body_view.is_empty():
		return []
	var draw_slots: Array = []
	for raw_slot in body_view.view.get("draw_slots",[9]):
		var slot := int(raw_slot)
		if parts.has(slot) and not draw_slots.has(slot):
			draw_slots.append(slot)
	for slot in parts:
		if not draw_slots.has(int(slot)):
			draw_slots.append(int(slot))
	var result: Array = []
	for raw_slot in draw_slots:
		var slot := int(raw_slot)
		var part: Array = parts.get(slot,[])
		if part.is_empty():
			continue
		var resolved := _resolved(part,mecha_action,facing)
		if resolved.is_empty():
			continue
		result.append({
			"asset":str(part[0]),"clip":str(resolved.view.clip),
			"frame":resolved.timeline.frame_at_update(elapsed_updates),
			"flip_h":bool(resolved.view.flip_h),"part_slot":slot,
			"palette_index":int(part[2]) if part.size()>2 else 0
		})
	return result
