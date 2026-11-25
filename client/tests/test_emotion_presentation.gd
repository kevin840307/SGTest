extends SceneTree

const Players=preload("res://player/player_presentation.gd")
const AppearanceProfile=preload("res://player/character_appearance_profile.gd")

func fail(reason:String)->void:
	push_error("EMOTION_PRESENTATION_FAIL "+reason)
	quit(1)

func _load_formal_manifests(appearance:Dictionary)->Dictionary:
	var definition:Dictionary=AppearanceProfile.definition(appearance)
	var assets:={}
	var profile_parts:Array=definition.parts.values()
	profile_parts.append_array(definition.overlays.values())
	for part in profile_parts:
		var asset_id:=str(part[0])
		if assets.has(asset_id):
			continue
		var path:="res://assets/"+asset_id+"/atlas.json"
		if not FileAccess.file_exists(path):
			return {}
		var parsed=JSON.parse_string(FileAccess.get_file_as_string(path))
		if not parsed is Dictionary:
			return {}
		assets[asset_id]={"manifest":parsed}
	return assets

func _verify_formal_asset_fallback()->bool:
	for race in range(5):
		var appearance={
			"race":race,"hair_index":0,"hair_color":0,"body_index":0,"leg_index":0,"sleeve_color":0
		}
		var assets:=_load_formal_manifests(appearance)
		if assets.is_empty():
			fail("formal manifest load race=%d"%race)
			return false
		var profile:=AppearanceProfile.new()
		if profile.configure(assets,appearance,{})!=OK:
			fail("formal profile configure race=%d"%race)
			return false
		for action in AppearanceProfile.EMOTION_ACTIONS:
			var expected:=race<=3
			if bool(profile.emotion_available.get(action,false))!=expected:
				fail("formal emotion fallback race=%d action=%s expected=%s"%[race,action,str(expected)])
				return false
			if expected:
				var view_key:String=str(profile.body_prefix)+"_"+str(action)+"1"
				if not profile.views.has(view_key):
					fail("formal emotion view missing "+view_key)
					return false
				var view:Dictionary=profile.views[view_key].view
				if str(view.clip)!=str(profile.body_prefix)+"_"+action+"8" or bool(view.flip_h) or bool(view.loop):
					fail("formal emotion source/facing/loop drift "+view_key)
					return false
	return true

func _initialize()->void:
	if not _verify_formal_asset_fallback():
		return
	var image:=Image.create(4,4,false,Image.FORMAT_RGBA8)
	image.fill(Color.WHITE)
	var texture:=ImageTexture.create_from_image(image)
	var assets:={}
	var source_slots:=[12.0,7.0,9.0,5.0,0.0,1.0]
	var definition:Dictionary=AppearanceProfile.definition({})
	var profile_parts:Array=definition.parts.values()
	profile_parts.append_array(definition.overlays.values())
	for part in profile_parts:
		var views:={}
		var clips:={"NS":[0,1],"NR":[0,1],"AA":[0,1],"NH":[0,1],"ND":[0,1],"ZH":[0,1]}
		for action in ["NS","NR","AA","NH","ND","ZH"]:
			for facing in range(1,9):
				var one_shot:=str(action) in ["AA","NH","ND","ZH"]
				views[str(part[1])+"_"+action+str(facing)]={
					"clip":str(action),
					"flip_h":false if action=="ZH" else facing>=5,
					"timeline_ticks":[[0,4],[1,4]] if one_shot else [[0,8],[1,8]],
					"loop":not one_shot,
					"draw_slots":source_slots
				}
		assets[part[0]]={
			"pages":[texture],
			"manifest":{
				"logical_views":views,
				"clips":clips,
				"frames":[
					{"page":0,"rect":[0,0,3,2],"trim":[2,0],"origin":[1,1]},
					{"page":0,"rect":[0,2,3,2],"trim":[2,0],"origin":[1,1]}
				]
			}
		}

	var players:=Players.new()
	if players.configure(assets)!=OK:
		fail("profile configure")
		return
	if Players.EMOTION_BY_ID!=["ZH","ZK","ZI","ZJ","ZL","ZB","ZC","ZG","ZA","ZE","ZD","ZF"]:
		fail("old EMOTACTLIB order")
		return

	var own:={"actor_id":1,"connection_epoch":1,"entity_map_epoch":1,"x":10.0,"y":10.0,"facing":3,"moving":false,"movement_action_sequence":1,"server_tick":1}
	var remote:={"actor_id":2,"connection_epoch":1,"entity_map_epoch":1,"x":20.0,"y":20.0,"facing":6,"moving":false,"movement_action_sequence":1,"server_tick":1}
	var snapshot:={
		"world_ready":true,"map_epoch":1,"map_id":15,"actor_id":1,
		"x":10.0,"y":10.0,"facing":3,"self_state":own,
		"entities":{2:remote},"last_aoi_tick":1,
		"emotion_events":[{
			"receiver_map_epoch":1,"actor_id":2,"connection_epoch":1,
			"emotion_sequence":1,"emotion_id":1
		}]
	}
	players.apply_snapshot(snapshot,0)
	if not players._actors.has(2):
		fail("remote actor missing")
		return
	var actor:Dictionary=players._actors[2]
	if str(actor.action)!="ZH" or int(actor.action_facing)!=8:
		fail("server visual did not select ZH fixed-facing8")
		return
	var drawn:Array=players.actors_at(0)
	var remote_draw:=drawn.filter(func(v):return str(v.id)=="2")
	if remote_draw.size()!=1 or (remote_draw[0].parts as Array).is_empty():
		fail("emotion action did not produce paper-doll layers")
		return

	snapshot.entities[2].moving=true
	snapshot.entities[2].facing=4
	snapshot.entities[2].server_tick=2
	snapshot.last_aoi_tick=2
	players.apply_snapshot(snapshot,100000)
	actor=players._actors[2]
	if str(actor.action)!="NR" or int(actor.action_facing)!=4:
		fail("movement did not interrupt emotion back to NR")
		return

	print("EMOTION_PRESENTATION_PASS formal_assets=true races_ABGR=true yas_fail_closed=true original_order=true ZH=true facing8=true paper_doll=true movement_cancel=true")
	quit(0)
