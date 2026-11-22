extends RefCounted
## Source-normalized part/timeline data only. No loading, networking or actor state.
const Timeline = preload("res://animation/action_timeline.gd")

const HUMANOID_BASE_PARTS := {
	12:["players/b/base/leg","B-LEG"],7:["players/b/base/right_arm","B-R-ARM"],
	9:["players/b/base/body","B-BODY"],5:["players/b/base/left_arm","B-L-ARM"],
	0:["players/b/base/head","B-HEAD"],1:["players/b/hair/hair00","B-HAIR00"]}
# Recovered2 build_human_model() uses Robot's zero equipment parts rather
# than borrowing humanoid Standard limbs. These five groups are normalized by
# asset_converter against the original robot.act + r_chardraworder.dat.
const ROBOT_BASE_PARTS := {
	12:["equipment/r/cloth/leg00","R-LEG00"],
	7:["equipment/r/cloth/r_arm00","R-R-ARM00"],
	9:["equipment/r/cloth/body00","R-BODY00"],
	5:["equipment/r/cloth/l_arm00","R-L-ARM00"],
	2:["equipment/r/cloth/bonnet00","R-BONNET00"]}
const BASE_PARTS := HUMANOID_BASE_PARTS
const RACE_KEYS := ["b","g","a","r","y"]

var key := ""
var parts: Dictionary = {}
var overlays: Dictionary = {}
var body_prefix := ""
var views: Dictionary = {}
var attack_available := false
var hit_available := false
var death_available := false

static func appearance_key(appearance: Dictionary = {}) -> String:
	if appearance.is_empty():
		return "0:0:0:0:0"
	if appearance.size() not in [3,5] or 			not appearance.has_all(["race","hair_index","hair_color"]):
		return ""
	if appearance.size() == 5 and 			not appearance.has_all(["body_index","leg_index"]):
		return ""
	for field in appearance:
		if not appearance[field] is int:
			return ""
	var race := int(appearance.race)
	var hair := int(appearance.hair_index)
	var color := int(appearance.hair_color)
	var body := int(appearance.get("body_index",0))
	var leg := int(appearance.get("leg_index",0))
	if race < 0 or race >= RACE_KEYS.size() or hair < 0 or hair > 65535 or 			color < 0 or color > 7 or body < 0 or body > 255 or leg < 0 or leg > 255:
		return ""
	# Robot has no hair selectors; Yas has no source-verified HK2 armor mapping.
	if race == 3 and (hair != 0 or color != 0):
		return ""
	if race == 4 and (body != 0 or leg != 0):
		return ""
	return "%d:%d:%d:%d:%d" % [race,hair,color,body,leg]

static func definition(appearance: Dictionary = {}) -> Dictionary:
	var resolved_key := appearance_key(appearance)
	if resolved_key.is_empty():
		return {}
	var value := {
		"race":0,"hair_index":0,"hair_color":0,"body_index":0,"leg_index":0
	} if appearance.is_empty() else appearance
	var race_index := int(value.race)
	var body_index := int(value.get("body_index",0))
	var leg_index := int(value.get("leg_index",0))
	if race_index == 3:
		# Recovered's Robot path has no separate Standard legs/arms. Until the
		# legacy CreateCharPara item->spr_id mapping is verified, render the one
		# source-verified zero outfit regardless of stored selector values.
		return {
			"key":resolved_key,
			"parts":ROBOT_BASE_PARTS.duplicate(true),
			"overlays":{},
			"body_prefix":"R-BODY00",
			"hair_slot":-1,
			"hair_color":0,
		}
	var race: String = RACE_KEYS[race_index]
	var prefix := race.to_upper()
	var hair := "hair%02d" % int(value.hair_index)
	var resolved := HUMANOID_BASE_PARTS.duplicate(true)
	for slot in resolved:
		if slot == 1:
			resolved[slot] = [
				"players/%s/hair/%s" % [race,hair],
				prefix+"-"+hair.to_upper()]
		else:
			resolved[slot] = [
				str(resolved[slot][0]).replace("players/b/","players/"+race+"/"),
				str(resolved[slot][1]).replace("B-",prefix+"-")]
	var equipment_overlays := {}
	if race_index != 4:
		# Recovered builds Standard Body/Leg first, then EquipBody/EquipLeg.
		# Body000 is the verified starter shirt; Leg000 is shorts and must NOT
		# replace Standard Leg, otherwise the lower legs disappear.
		equipment_overlays = {
			9:[
				"equipment/%s/cloth/body00" % race,
				"%s-BODY00" % prefix],
			12:[
				"equipment/%s/cloth/leg00" % race,
				"%s-LEG00" % prefix],
		}
	return {
		"key":resolved_key,
		"parts":resolved,
		"overlays":equipment_overlays,
		"body_prefix":prefix+"-BODY",
		"hair_slot":1,
		"hair_color":int(value.hair_color),
	}

func configure(assets: Dictionary, appearance: Dictionary, shared_views: Dictionary) -> Error:
	var data := definition(appearance)
	if data.is_empty():
		return ERR_INVALID_PARAMETER
	key = str(data.key)
	parts = data.parts
	overlays = data.get("overlays",{})
	body_prefix = str(data.body_prefix)
	for part in parts.values():
		if not assets.has(part[0]):
			return ERR_INVALID_DATA
	for part in overlays.values():
		if not assets.has(part[0]):
			return ERR_INVALID_DATA
	for action in ["NS","NR"]:
		if _load_action(assets,action,true,false,shared_views) != 1:
			return ERR_INVALID_DATA
	for action in ["AA","NH","ND"]:
		var available := _load_action(assets,action,false,true,shared_views)
		if available < 0:
			return ERR_INVALID_DATA
		match action:
			"AA": attack_available = available == 1
			"NH": hit_available = available == 1
			"ND": death_available = available == 1
	return OK

func _load_action(assets: Dictionary, action: String, required: bool, one_shot: bool, shared_views: Dictionary) -> int:
	var all_parts: Array = parts.values()
	all_parts.append_array(overlays.values())
	var present := 0
	for part in all_parts:
		if assets[part[0]].manifest.get("logical_views",{}).has(str(part[1])+"_"+action+"1"):
			present += 1
	if present == 0 and not required:
		return 0
	if present != all_parts.size():
		return -1
	for part in all_parts:
		var manifest: Dictionary = assets[part[0]].manifest
		for facing in range(1,9):
			var view_key := str(part[1])+"_"+action+str(facing)
			var view: Dictionary = manifest.get("logical_views",{}).get(view_key,{})
			if not view.has_all(["clip","flip_h","timeline_ticks","loop","draw_slots"]) or not view.flip_h is bool or not view.loop is bool or (one_shot and view.loop) or not view.draw_slots is Array or not view.timeline_ticks is Array or not manifest.clips.has(view.clip):
				return -1
			var slots := {}
			for raw_slot in view.draw_slots:
				if not (raw_slot is int or raw_slot is float):
					return -1
				var slot := int(raw_slot)
				if float(raw_slot) != slot or slot < 0 or slot > 15 or slots.has(slot):
					return -1
				slots[slot] = true
			for slot in parts:
				if not slots.has(slot):
					return -1
			for step in view.timeline_ticks:
				if not step is Array or step.size()!=2 or not (step[0] is int or step[0] is float):
					return -1
				if int(step[0]) >= manifest.clips[view.clip].size():
					return -1
			if shared_views.has(view_key):
				if shared_views[view_key].view != view:
					return -1
				views[view_key] = shared_views[view_key]
				continue
			var timeline := Timeline.new()
			if timeline.configure_ticks(view.timeline_ticks,view.loop) != OK:
				return -1
			views[view_key] = {"view":view.duplicate(true),"timeline":timeline}
	return 1
