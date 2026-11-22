extends SceneTree
const Players = preload("res://player/player_presentation.gd")
const Profile = preload("res://player/character_appearance_profile.gd")
const DrawList = preload("res://world/world_draw_list.gd")
var failures: Array = []

func check(ok: bool, reason: String) -> void:
	if not ok:
		failures.append(reason)
		push_error("PLAYER_PROFILES_FAIL "+reason)

func _definition_parts(definition: Dictionary) -> Array:
	var result: Array = definition.parts.values()
	result.append_array(definition.get("overlays",{}).values())
	return result

func _initialize() -> void:
	var image := Image.create(4,4,false,Image.FORMAT_RGBA8)
	image.fill(Color.WHITE)
	var texture := ImageTexture.create_from_image(image)
	var assets := {}
	for race in 3:
		var appearance := {"race":race,"hair_index":0,"hair_color":0}
		var definition := Profile.definition(appearance)
		for part in _definition_parts(definition):
			assets[part[0]] = _asset(str(part[1]),texture,race)
	var hair1 := Profile.definition({"race":1,"hair_index":1,"hair_color":0})
	assets[hair1.parts[1][0]] = _asset(str(hair1.parts[1][1]),texture,1)
	var dressed := Profile.definition({
		"race":0,"hair_index":0,"hair_color":0,
		"body_index":1,"leg_index":2})
	var players := Players.new()
	check(players.configure(assets)==OK,"B default profile rejected")
	for race in [1,2]:
		check(players.add_profile(assets,{"race":race,"hair_index":0,"hair_color":0})==OK,"G/A profile rejected")
	check(players.add_profile(assets,{"race":1,"hair_index":1,"hair_color":0})==OK,"extra hair profile rejected")
	check(players.add_profile(assets,{
		"race":0,"hair_index":0,"hair_color":0,
		"body_index":1,"leg_index":2})==OK,"creation clothes profile rejected")
	check(
		players._profiles["1:0:0:0:0"].views["G-BODY_NR1"].timeline==
		players._profiles["1:1:0:0:0"].views["G-BODY_NR1"].timeline,
		"same body allocated another timeline")
	var own := _actor(1,0)
	own.appearance.body_index = 1
	own.appearance.leg_index = 2
	var girl := _actor(2,1)
	var alien := _actor(3,2)
	var unknown := _actor(4,3)
	var snapshot := {"world_ready":true,"map_epoch":1,"map_id":15,"actor_id":1,
		"self_state":own,"entities":{2:girl,3:alien,4:unknown}}
	players.apply_snapshot(snapshot,0)
	var history = players._actors[2].history
	girl.appearance.hair_index = 1
	for actor in [own,girl,alien]:
		actor.movement_action_sequence += 9
		actor.server_tick = 2
	players.apply_snapshot(snapshot,100000)
	var actors := players.actors_at(160000)
	check(actors.size()==3 and players._actors[2].history==history,"unknown race fell back or profile change reset interpolation")
	for i in 3:
		var race_key: String = ["b","g","a"][i]
		check(actors[i].parts.size()==8 and int(actors[i].parts[0].frame)==1,"retarget/profile change restarted fixed-speed walk")
		for part in actors[i].parts:
			var asset_path := str(part.asset)
			check(
				asset_path.begins_with("players/"+race_key+"/") or
				asset_path.begins_with("equipment/"+race_key+"/cloth/"),
				"profile borrowed another race's parts")
	check(actors[1].parts.any(func(part): return part.asset=="players/g/hair/hair01"),"selected hair not rendered")
	var body_colored: bool = actors[0].parts.any(
		func(part): return part.asset=="equipment/b/cloth/body00" and int(part.get("palette_index",-1))==1)
	var leg_colored: bool = actors[0].parts.any(
		func(part): return part.asset=="equipment/b/cloth/leg00" and int(part.get("palette_index",-1))==2)
	check(body_colored and leg_colored,
		"starter clothing color indexes did not reach presentation metadata")
	for i in 3:
		var race_key: String = ["b","g","a"][i]
		var base_leg := "players/%s/base/leg" % race_key
		var shorts := "equipment/%s/cloth/leg00" % race_key
		var base_leg_at: int = actors[i].parts.find_custom(
			func(part): return str(part.asset)==base_leg)
		var shorts_at: int = actors[i].parts.find_custom(
			func(part): return str(part.asset)==shorts)
		check(
			base_leg_at >= 0 and shorts_at > base_leg_at,
			"starter shorts replaced or covered the required Standard Leg layer")
	snapshot.player_attack_events=[{"attacker_actor_id":2,"attacker_connection_epoch":1,"attack_sequence":1,"facing":3}]
	snapshot.player_damage_visual_events=[{"victim_actor_id":3,"victim_connection_epoch":1,"damage_sequence":1,"facing":2,"died":true}]
	players.apply_snapshot(snapshot,200000)
	actors = players.actors_at(200000)
	check(str(actors[0].parts[0].clip)=="NR" and str(actors[1].parts[0].clip)=="AA" and str(actors[2].parts[0].clip)=="ND","AA/ND flags or body timeline leaked across profiles")
	# Appearance changes during AA must not sample a clip absent on the new race.
	girl.appearance = {"race":0,"hair_index":0,"hair_color":0}
	snapshot.player_attack_events = []
	snapshot.player_damage_visual_events = []
	players.apply_snapshot(snapshot,210000)
	var changed := players.actors_at(210000)
	check(str(changed[1].parts[0].clip)=="NR","profile change retained unsupported AA")
	var draw := DrawList.new()
	for actor in actors:
		check(draw.build_actor_preview(assets,actor,Rect2(-100,-100,500,500)).size()==8,"shared geometry renderer rejected layered profile")
	var color_profile := Profile.definition(
		{"race":0,"hair_index":0,"hair_color":1})
	check(
		not color_profile.is_empty() and
		color_profile.key=="0:0:1:0:0" and
		color_profile.parts==Profile.definition(
			{"race":0,"hair_index":0,"hair_color":0}).parts and
		color_profile.overlays==Profile.definition(
			{"race":0,"hair_index":0,"hair_color":0}).overlays,
		"verified hair palette changed source atlases or was rejected")
	var clothing := Profile.definition({
		"race":0,"hair_index":0,"hair_color":0,
		"body_index":1,"leg_index":2})
	check(
		str(clothing.parts[9][0])=="players/b/base/body" and
		str(clothing.parts[12][0])=="players/b/base/leg" and
		str(clothing.overlays[9][0])=="equipment/b/cloth/body00" and
		str(clothing.overlays[12][0])=="equipment/b/cloth/leg00" and
		int(clothing.overlays[9][2])==1 and
		int(clothing.overlays[12][2])==2 and
		clothing.key=="0:0:0:1:2",
		"starter color indexes changed sprites instead of Body00/Leg00 palettes")
	var yas := Profile.definition({"race":4,"hair_index":2,"hair_color":7})
	check(
		not yas.is_empty() and
		str(yas.parts[9][0])=="players/y/base/body" and
		str(yas.parts[1][0])=="players/y/hair/hair02",
		"Yas profile did not use source Y body/hair assets")
	var robot := Profile.definition({"race":3,"hair_index":0,"hair_color":0})
	check(
		not robot.is_empty() and
		str(robot.parts[9][0])=="equipment/r/cloth/body00" and
		not robot.parts.has(1),
		"Robot profile did not use source zero-cloth body without hair")
	check(
		Profile.definition({"race":3,"hair_index":1,"hair_color":0}).is_empty() and
		Profile.definition({"race":3,"hair_index":0,"hair_color":1}).is_empty(),
		"Robot incorrectly admitted disabled hair controls")
	var bad := assets.duplicate(true)
	bad["players/g/hair/hair01"].manifest.logical_views["G-HAIR01_NS1"].timeline_ticks = [[99,8]]
	var probe := Players.new()
	check(probe.configure(bad,{"race":1,"hair_index":1,"hair_color":0})!=OK,"invalid source frame not rejected")
	var before_profiles := players._profiles.size()
	var before_views := players._views.size()
	var extra := assets.duplicate(true)
	var missing := Profile.definition({"race":2,"hair_index":1,"hair_color":0})
	extra[missing.parts[1][0]] = _asset(str(missing.parts[1][1]),texture,2)
	extra[missing.parts[1][0]].manifest.logical_views.erase("A-HAIR01_NR8")
	check(players.add_profile(extra,{"race":2,"hair_index":1,"hair_color":0})!=OK,"partial profile accepted")
	check(players._profiles.size()==before_profiles and players._views.size()==before_views,"failed profile polluted shared metadata")
	players.clear()
	check(players._profiles.is_empty() and players._views.is_empty() and players.actors_at(0).is_empty(),"clear retained profile data or actors")
	if failures.is_empty():
		print("PLAYER_PROFILES_PASS BGA_layered_parts=true standard_leg_preserved=true starter_shorts_overlay=true RY_definitions=true hair_selection=true hair_palette=true creation_clothing=true shared_timelines=true fixed_walk=true preserves_interpolation=true per_profile_AA_ND=true no_unknown_fallback=true")
	quit(0 if failures.is_empty() else 1)

func _actor(id: int, race: int) -> Dictionary:
	return {"actor_id":id,"connection_epoch":1,"entity_map_epoch":1,"server_tick":1,
		"x":float(id*20),"y":20.0,"facing":6,"moving":true,"movement_action_sequence":1,
		"appearance":{"race":race,"hair_index":0,"hair_color":0}}

func _asset(prefix: String, texture: Texture2D, race: int) -> Dictionary:
	var actions := ["NS","NR"]
	if race==1:
		actions.append("AA")
	if race==2:
		actions.append_array(["NH","ND"])
	var views := {}
	var clips := {}
	for action in actions:
		clips[action] = [0,1]
		for facing in range(1,9):
			views[prefix+"_"+action+str(facing)] = {"clip":action,"flip_h":facing>=5,
				"timeline_ticks":[[0,8],[1,8]],"loop":action in ["NS","NR"],"draw_slots":[12,7,9,5,0,1]}
	return {"pages":[texture],"manifest":{"logical_views":views,"clips":clips,"frames":[
		{"page":0,"rect":[0,0,3,2],"trim":[2,0],"origin":[1,1]},
		{"page":0,"rect":[0,2,3,2],"trim":[2,0],"origin":[1,1]}]}}
