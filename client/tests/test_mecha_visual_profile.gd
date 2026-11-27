extends SceneTree
const Profile = preload("res://mecha/mecha_visual_profile.gd")

func _fail(message:String)->void:
	push_error("MECHA_VISUAL_PROFILE_FAIL "+message)
	quit(1)

func _first_asset(definition:Dictionary,slot:int)->String:
	var parts:Dictionary=definition.get("parts",{})
	if not parts.has(slot):
		return ""
	var candidates:Array=parts[slot].get("candidates",[])
	return str(candidates[0].get("asset","")) if not candidates.is_empty() else ""

func _initialize()->void:
	var profile=Profile.new()
	if not profile.item_catalog.loaded:
		_fail("private presentation catalog did not load")
		return
	var appearance={"race":0,"hair_index":0,"hair_color":0}
	var ms=profile.source_definition({
		"mounted":true,"definition_id":50001,
		"weapon_definition_id":50293,"engine_definition_id":50297
	},appearance,{})
	if str(ms.get("type",""))!="MS":
		_fail("MS source definition missing")
		return
	var expected={
		9:"mecha/ms/standard/b-msbody00",
		12:"mecha/ms/standard/b-msleg00",
		5:"mecha/ms/standard/b-l-msarm00",
		7:"mecha/ms/standard/b-r-msarm00",
	}
	for slot in expected:
		if _first_asset(ms,int(slot))!=str(expected[slot]):
			_fail("MS slot %d mapped to %s"%[int(slot),_first_asset(ms,int(slot))])
			return
	var ma=profile.source_definition({
		"mounted":true,"definition_id":50002,
		"weapon_definition_id":50266,"engine_definition_id":50267
	},appearance,{})
	if str(ma.get("type",""))!="MA" or _first_asset(ma,9)!="mecha/ma/standard/b_ma02":
		_fail("MA source body mapping "+str(ma))
		return
	if not profile.source_definition({"mounted":false,"definition_id":50001},appearance,{}).is_empty():
		_fail("unmounted player exposed Mecha visual")
		return
	print("MECHA_VISUAL_PROFILE_PASS ms_body=true ms_weapon=true ms_engine=true ma_body=true unmounted_closed=true")
	quit(0)
