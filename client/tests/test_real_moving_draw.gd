extends SceneTree
const View = preload("res://world/world_view.gd")
var failures: Array[String] = []
func check(ok: bool, label: String) -> void:
	if not ok: failures.append(label); push_error(label)
func _initialize() -> void: run.call_deferred()
func state(actor_id: int, p: Vector2, t: int, moving: bool = false) -> Dictionary:
	return {"actor_id":actor_id,"connection_epoch":1,"entity_map_epoch":15,
		"x":p.x,"y":p.y,"facing":6,"moving":moving,"movement_action_sequence":1,"server_tick":t}
func run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 1: push_error("usage -- <real_pack>"); quit(2); return
	root.size = Vector2i(1280,720)
	var view := View.new()
	view.show_players=true
	root.add_child(view)
	check(view.open_pack(args[0])==OK,"open real pack")
	check(view.request_map(15)==OK,"load map15")
	var own: Dictionary = state(1,Vector2(7872,11296),1)
	# Full paper-doll customization from the real local dev identity database:
	# older tests only exercised the default undyed hair/body/legs.
	own["appearance"]={"race":0,"hair_index":1,"hair_color":1,
		"body_index":1,"leg_index":1,"sleeve_color":2}
	var remote: Dictionary = state(2,Vector2(7892,11300),1)
	var snapshot := {"world_ready":true,"map_epoch":1,"map_id":15,"actor_id":1,
		"x":7872.0,"y":11296.0,"facing":6,"self_state":own,"entities":{2:remote},"monsters":{},"player_identities":{
		1:{"connection_epoch":1,"entity_map_epoch":15,"server_unix_ms":100000,
			"pink_until_unix_ms":0,"pvp_updated_unix_ms":1,"mode":0,
			"red_man":false,"display_name":"自己的人物"},
		2:{"connection_epoch":1,"entity_map_epoch":15,"server_unix_ms":100000,
			"pink_until_unix_ms":0,"pvp_updated_unix_ms":1,"mode":1,
			"red_man":false,"display_name":"其他玩家"}}}
	view.set_player_snapshot(snapshot)
	var deadline := Time.get_ticks_msec()+30000
	while view.phase in ["map","palette"] and Time.get_ticks_msec()<deadline:
		await process_frame
	check(view.phase=="ready","map real assets ready")
	if view.phase!="ready": quit(1); return
	var visible_npcs: Array = view.npcs.actors_at(view.camera.view_rect())
	var npc_names: Array = view.npcs.labels_at(view.camera.view_rect())
	print("REAL_NPC_COUNT ",view.npc_count()," near=",visible_npcs.size()," named=",npc_names.size()," sample=",npc_names.slice(0,2))
	check(visible_npcs.size()>=1,"real NPC fixture not visible for regression")
	# Force exactly the same old all-or-nothing error branch with a broken
	# transient AOI actor and with a bad NR animation frame. A good actor,
	# NPC and scenery must stay drawn regardless of those isolated failures.
	var own_profile = view.players._actors[1].profile
	var hair_asset := str(own_profile.parts[1][0])
	var hair_prefix := str(own_profile.parts[1][1])
	for facing in range(1,9):
		for act in ["NS","NR"]:
			var body = own_profile.views.get(own_profile.body_prefix+"_"+act+str(facing),{})
			var hair = own_profile.views.get(hair_prefix+"_"+act+str(facing),{})
			var missing: Array = []
			var asset: Dictionary = view.draw_list._assets.get(hair_asset,{})
			if not hair.is_empty():
				if not asset.is_empty():
					for i in range(asset.manifest.clips[hair.view.clip].size()):
						var fr = asset.manifest.frames[int(asset.manifest.clips[hair.view.clip][i])]
						if int(fr.page)<0: missing.append(i)
			check(not body.is_empty() and not hair.is_empty() and not asset.is_empty() and missing.is_empty(),
				"hair01 source view invalid face=%d action=%s" % [facing,act])
	var original_actors: Array = view.players.actors_at(Time.get_ticks_usec())
	var invalid_actors: Array = visible_npcs.duplicate(true)
	invalid_actors.append_array(original_actors)
	invalid_actors.append({"id":"corrupt:77","p":Vector2(7880,11300),
		"parts":[{"asset":"unknown/not_loaded","clip":"NR","frame":0}]})
	var area: Rect2 = view.camera.view_rect()
	var retained := view.draw_list.build(area,invalid_actors)
	check(view.draw_list.error==OK and view.draw_list.skipped_actors==1,
		"invalid AOI actor failed to isolate soft presentation error")
	check(retained.any(func(c):return str(c.actor_id)=="1") and
		retained.any(func(c):return str(c.actor_id).begins_with("npc:")),
		"corrupt single actor blanked valid players or NPCs")
	invalid_actors.pop_back()
	# Recovered renders individual paper-doll Sprite2Ds independently.
	# An invalid hair/jacket layer must preserve every healthy body limb.
	var partial_actor: Dictionary = (original_actors[0] as Dictionary).duplicate(true)
	partial_actor.parts[0]["asset"]="missing/hair-or-cloth"
	invalid_actors.append(partial_actor)
	retained = view.draw_list.build(area,invalid_actors)
	check(view.draw_list.error==OK and view.draw_list.skipped_parts>=1 and \
		retained.any(func(c):return str(c.actor_id)=="1"),
		"one missing paper-doll layer incorrectly made entire own avatar invisible")
	invalid_actors.pop_back()
	var invalid_frame: Dictionary = (original_actors[0] as Dictionary).duplicate(true)
	invalid_frame.parts[0].frame = 123456
	invalid_actors.append(invalid_frame)
	retained = view.draw_list.build(area,invalid_actors)
	check(view.draw_list.error==OK and view.draw_list.skipped_frames>=1,
		"bad walk frame failed to isolate soft presentation error")
	check(retained.any(func(c):return str(c.actor_id)=="2") and
		retained.any(func(c):return str(c.actor_id).begins_with("npc:")),
		"single bad walk frame blanked valid entities")
	var detected := {}
	for i in 125:
		var now := Time.get_ticks_usec()
		if i>0: view.players.set_local_visual(1,Vector2(7872+i*1.4,11296),true,1+(i/15)%8,now)
		view._players_dirty = true
		view.present()
		var actors := view.players.actors_at(now)
		var npc_actors := view.npcs.actors_at(view.camera.view_rect())
		var commands: Array = view._canvas._commands
		var id_counts := {}
		for com in commands:
			var id := str(com.get("actor_id",""))
			if not id.is_empty(): id_counts[id] = int(id_counts.get(id,0))+1
		var k := "ok" if view.draw_list.error==OK else str(view.draw_list.error)
		if not detected.has(k) or (i>0 and (commands.is_empty() or id_counts.get("1",0)==0)):
			print("FRAME ",i," error=",view.draw_list.error," actorparts=",actors.size()," npcactors=",npc_actors.size()," commands=",commands.size()," ids=",id_counts," player_parts=",actors[0].parts if not actors.is_empty() and i==1 else "")
			detected[k]=true
		check(not commands.is_empty(),"empty world draw commands on move frame "+str(i))
		check(id_counts.get("1",0)>0,"self actor disappeared frame "+str(i))
		check(id_counts.get("2",0)>0,"remote actor disappeared frame "+str(i))
		var own_actor_rows: Array = actors.filter(func(v):return str(v.id)=="1")
		check(not own_actor_rows.is_empty() and own_actor_rows[0].parts.any(
			func(part):return str(part.asset)=="players/b/hair/hair01"),
			"own hair01 disappeared from paper doll frame "+str(i))
		check(commands.any(func(c):return str(c.actor_id)=="1" and int(c.slot)==1),
			"own hair01 was not drawn frame "+str(i))
		var label_ids: Array = view._canvas._player_labels.map(func(l):return str(l.actor_id))
		check(label_ids.has("1") and label_ids.has("2"),
			"player name detached/disappeared during movement frame "+str(i))
		for npc_label in view.npcs.labels_at(view.camera.view_rect()):
			check(label_ids.has(str(npc_label.actor_id)),
				"NPC name vanished on camera movement frame "+str(i))
		for name_label in view._canvas._player_labels:
			var label_id := str(name_label.actor_id)
			var matching := commands.filter(func(c):return str(c.actor_id)==label_id)
			if matching.is_empty(): continue
			if label_id.begins_with("npc:"):
				# NPC names still use their static visible sprite top.
				var min_y := INF
				var min_x := INF
				var max_x := -INF
				for c in matching:
					var bb: Rect2 = (c.destination as Rect2).abs()
					min_y = minf(min_y,bb.position.y)
					min_x = minf(min_x,bb.position.x)
					max_x = maxf(max_x,bb.end.x)
				check(is_equal_approx(float(name_label.p.y),min_y-6.0) and \
					is_equal_approx(float(name_label.p.x),(min_x+max_x)*0.5),
					"NPC name lost static sprite anchor frame "+str(i))
			else:
				var actor_id := int(label_id)
				var feet := view.players.position_at(actor_id,now)
				var expected := feet+Vector2(0,view.players.stable_name_top_offset(actor_id))
				check(name_label.p.distance_to(expected)<0.01,
					"player name jittered with animation bounds frame "+str(i))
		if failures.size()>9: break
		await process_frame
	# A Server correction of 200px caused a real user's terrain to pan
	# backwards abruptly. Camera2D-style follow now has exactly one owner,
	# on render frames, and bounds large corrections to 480px/s.
	var follow_before: Vector2 = view.camera.view_rect().position
	var visual_focus: Vector2 = view._camera_visual_focus
	var future_usec := view._camera_last_usec+16666
	view._follow_camera_visual(visual_focus+Vector2(200,0),future_usec)
	var after_first: Vector2 = view.camera.view_rect().position
	check(after_first.distance_to(follow_before)<=8.1 and after_first.distance_to(follow_before)>0.1,
		"large Server correction shifted entire terrain more than one bounded frame")
	for follow_frame in 42:
		future_usec += 16666
		view._follow_camera_visual(visual_focus+Vector2(200,0),future_usec)
	check(view._camera_visual_focus.distance_to(visual_focus+Vector2(200,0))<1.0,
		"camera failed to converge after smoothing authoritative correction")
	# Normal 4px local walks must follow immediately, with no artificial lag.
	var recovered_focus: Vector2 = view._camera_visual_focus
	view._follow_camera_visual(recovered_focus+Vector2(4,0),future_usec+16666)
	check(view._camera_visual_focus.distance_to(recovered_focus+Vector2(4,0))<0.01,
		"normal local motion introduced unnecessary camera smoothing")
	print("REAL_MOVING_DRAW_RESULT failures=",failures)
	view.queue_free()
	await process_frame
	quit(0 if failures.is_empty() else 1)
