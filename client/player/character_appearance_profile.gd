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
const EMOTION_ACTIONS := ["ZA","ZB","ZC","ZD","ZE","ZF","ZG","ZH","ZI","ZJ","ZK","ZL"]
# Original A/B/G/R ACT files share these exact Z-action source frames/ticks.
# `default_ticks` are the converter-normalized legacy raw times: 6->9,
# 5->8 and 7->11. Each source frame appears twice in the ACT timeline.
const EMOTION_SOURCE := {
	"ZA":[5,9],"ZB":[5,9],"ZC":[5,9],"ZD":[5,9],"ZE":[5,9],"ZF":[5,9],
	"ZG":[11,9],"ZH":[8,8],"ZI":[5,11],"ZJ":[5,8],"ZK":[9,8],"ZL":[5,8],
}
# HK CharDrawOrder has no Z rows. These are the recovered original-client Z
# orders from Recovered2, normalized through the same +2 slot expansion as
# asset_converter. Shadow is not a paper-doll equipment layer.
const EMOTION_DRAW_SLOTS := {
	"DEFAULT":[11,13,12,10,9,8,7,6,5,4,3,0,1,2],
	"ZC":[11,13,12,10,9,0,1,8,7,6,5,4,3,2],
	"ZE":[11,8,7,6,5,13,12,10,9,4,3,0,1,2],
	"ZF":[11,13,12,6,5,10,9,8,7,4,3,0,1,2],
	"ZI":[11,13,12,10,9,6,5,8,7,3,4,0,1,2],
}
const ROBOT_ZC_DRAW_SLOTS := [11,13,12,10,9,0,1,4,3,2,8,7,6,5]

var key := ""
var parts: Dictionary = {}
var overlays: Dictionary = {}
var body_prefix := ""
var hair_color := 0
var views: Dictionary = {}
var attack_available := false
var hit_available := false
var death_available := false
var emotion_available: Dictionary = {}

static func appearance_key(appearance: Dictionary = {}) -> String:
	if appearance.is_empty():
		return "0:0:0:0:0:0"
	if appearance.size() not in [3,5,6] or 			not appearance.has_all(["race","hair_index","hair_color"]):
		return ""
	if appearance.size() >= 5 and 			not appearance.has_all(["body_index","leg_index"]):
		return ""
	if appearance.size() == 6 and not appearance.has("sleeve_color"):
		return ""
	for field in appearance:
		if not appearance[field] is int:
			return ""
	var race := int(appearance.race)
	var hair := int(appearance.hair_index)
	var color := int(appearance.hair_color)
	var body := int(appearance.get("body_index",0))
	var leg := int(appearance.get("leg_index",0))
	var sleeve := int(appearance.get("sleeve_color",0))
	if race < 0 or race >= RACE_KEYS.size() or hair < 0 or hair > 65535 or 			color < 0 or color > 7 or body < 0 or body > 255 or leg < 0 or leg > 255 or 			sleeve < 0 or sleeve > 255:
		return ""
	# Robot has no hair selectors; Yas has no source-verified HK2 armor mapping.
	if race == 3 and (hair != 0 or color != 0):
		return ""
	if race in [0,1,2] and (body > 2 or leg > 2 or sleeve > 2):
		return ""
	if race == 3 and (body != 0 or leg != 0 or sleeve > 2):
		return ""
	if race == 4 and (body != 0 or leg != 0 or sleeve != 0):
		return ""
	return "%d:%d:%d:%d:%d:%d" % [race,hair,color,body,leg,sleeve]

static func definition(appearance: Dictionary = {}) -> Dictionary:
	var resolved_key := appearance_key(appearance)
	if resolved_key.is_empty():
		return {}
	var value := {
		"race":0,"hair_index":0,"hair_color":0,"body_index":0,"leg_index":0,
		"sleeve_color":0
	} if appearance.is_empty() else appearance
	var race_index := int(value.race)
	var body_index := int(value.get("body_index",0))
	var leg_index := int(value.get("leg_index",0))
	var sleeve_color := int(value.get("sleeve_color",0))
	if race_index == 3:
		# Robot already uses the zero Arm00 sprites as its base limbs. Original
		# Left_hand/Right_hand share one selector, so color both arms in place.
		var robot := ROBOT_BASE_PARTS.duplicate(true)
		robot[5] = [robot[5][0],robot[5][1],sleeve_color]
		robot[7] = [robot[7][0],robot[7][1],sleeve_color]
		return {
			"key":resolved_key,
			"parts":robot,
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
		# Recovered builds Standard limbs first, then all starter equipment.
		# Body00/Leg00/L-Arm00/R-Arm00 stay the same sprites; color_index 0/1/2
		# selects the source palette. Shorts and sleeves never replace bare limbs.
		equipment_overlays = {
			9:[
				"equipment/%s/cloth/body00" % race,
				"%s-BODY00" % prefix,
				body_index],
			12:[
				"equipment/%s/cloth/leg00" % race,
				"%s-LEG00" % prefix,
				leg_index],
			5:[
				"equipment/%s/cloth/l_arm00" % race,
				"%s-L-ARM00" % prefix,
				sleeve_color],
			7:[
				"equipment/%s/cloth/r_arm00" % race,
				"%s-R-ARM00" % prefix,
				sleeve_color],
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
	hair_color = int(data.get("hair_color",0))
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
	for action in EMOTION_ACTIONS:
		var available := _load_action(assets,action,false,true,shared_views)
		if available < 0:
			return ERR_INVALID_DATA
		emotion_available[action] = available == 1
	return OK

func _emotion_fallback_view(manifest:Dictionary,prefix:String,action:String)->Dictionary:
	# Compatibility for stale generated manifests only. The formal atlas already
	# contains the original Z SPR clips; newly rebuilt manifests supply these
	# logical views directly and therefore never enter this path.
	if action not in EMOTION_ACTIONS or not EMOTION_SOURCE.has(action):
		return {}
	var race_index:=int(key.get_slice(":",0)) if not key.is_empty() else -1
	if race_index<0 or race_index>3:
		return {}
	var clip:=prefix+"_"+action+"8"
	var clips:Dictionary=manifest.get("clips",{})
	if not clips.has(clip) or not clips[clip] is Array:
		return {}
	var source:Array=EMOTION_SOURCE[action]
	var frame_count:=int(source[0])
	var ticks:=int(source[1])
	if clips[clip].size()!=frame_count:
		return {}
	var timeline_ticks:Array=[]
	for frame in frame_count:
		timeline_ticks.append([frame,ticks])
		timeline_ticks.append([frame,ticks])
	var draw_slots:Array
	if race_index==3 and action=="ZC":
		draw_slots=ROBOT_ZC_DRAW_SLOTS.duplicate()
	else:
		draw_slots=(EMOTION_DRAW_SLOTS.get(action,EMOTION_DRAW_SLOTS.DEFAULT) as Array).duplicate()
	return {
		"clip":clip,"flip_h":false,"timeline_ticks":timeline_ticks,"loop":false,
		"act_key":action,"draw_order_key":action+"8","draw_slots":draw_slots,
	}

func _resolved_view(manifest:Dictionary,prefix:String,action:String,facing:int)->Dictionary:
	var view_key:=prefix+"_"+action+str(facing)
	var authored:Dictionary=manifest.get("logical_views",{}).get(view_key,{})
	if not authored.is_empty():
		return authored
	return _emotion_fallback_view(manifest,prefix,action)

func _load_action(assets: Dictionary, action: String, required: bool, one_shot: bool, shared_views: Dictionary) -> int:
	var all_parts: Array = parts.values()
	all_parts.append_array(overlays.values())
	var present := 0
	for part in all_parts:
		var manifest:Dictionary=assets[part[0]].manifest
		if not _resolved_view(manifest,str(part[1]),action,1).is_empty():
			present += 1
	if present == 0 and not required:
		return 0
	if present != all_parts.size():
		return -1
	for part in all_parts:
		var manifest: Dictionary = assets[part[0]].manifest
		for facing in range(1,9):
			var view_key := str(part[1])+"_"+action+str(facing)
			var view: Dictionary = _resolved_view(manifest,str(part[1]),action,facing)
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
