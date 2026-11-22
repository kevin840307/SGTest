extends SceneTree
const WorldView = preload("res://world/world_view.gd")
const Profile = preload("res://player/character_appearance_profile.gd")

var failures: Array[String] = []

func _initialize() -> void:
	_run.call_deferred()

func check(ok: bool, message: String) -> void:
	if not ok:
		failures.append(message)
		push_error("WORLD_APPEARANCE_PROFILE_FAIL "+message)

func actor_state(id: int, x: float, appearance: Dictionary, tick: int) -> Dictionary:
	return {
		"actor_id":id,
		"connection_epoch":1,
		"entity_map_epoch":15,
		"x":x,
		"y":11296.0,
		"facing":6,
		"moving":false,
		"movement_action_sequence":1,
		"server_tick":tick,
		"appearance":appearance.duplicate(true),
	}

func wait_ready(view: Node) -> void:
	var deadline := Time.get_ticks_msec()+30000
	while view.phase in ["map","palette"] and Time.get_ticks_msec()<deadline:
		await process_frame
	check(view.phase=="ready","Map15 did not reach ready")

func wait_profile(view: Node, appearance: Dictionary) -> void:
	var deadline := Time.get_ticks_msec()+30000
	while not view.players.has_profile(appearance) and Time.get_ticks_msec()<deadline:
		await process_frame
	check(view.players.has_profile(appearance),"appearance profile did not load: "+str(appearance))

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()!=1:
		push_error("WORLD_APPEARANCE_PROFILE_USAGE -- <private-assets.pck>")
		quit(2)
		return
	var view := WorldView.new()
	view.show_players=true
	root.add_child(view)
	check(view.open_pack(args[0])==OK,"cannot open runtime PCK")
	check(view.request_map(15)==OK,"cannot request Map15")

	var own_app := {
		"race":0,"hair_index":0,"hair_color":1,
		"body_index":1,"leg_index":2,"sleeve_color":2}
	var remote_app := {
		"race":1,"hair_index":0,"hair_color":2,
		"body_index":2,"leg_index":1,"sleeve_color":1}
	var own := actor_state(1,7872.0,own_app,1)
	var remote := actor_state(2,7952.0,remote_app,1)
	var snapshot := {
		"world_ready":true,
		"map_epoch":15,
		"map_id":15,
		"actor_id":1,
		"x":own.x,
		"y":own.y,
		"facing":own.facing,
		"self_state":own,
		"entities":{2:remote},
		"player_identities":{
			1:{"connection_epoch":1,"entity_map_epoch":15,"server_unix_ms":1000,
				"pink_until_unix_ms":0,"pvp_updated_unix_ms":0,"mode":0,
				"red_man":false,"display_name":"AppearanceSelf"},
			2:{"connection_epoch":1,"entity_map_epoch":15,"server_unix_ms":1000,
				"pink_until_unix_ms":0,"pvp_updated_unix_ms":0,"mode":0,
				"red_man":false,"display_name":"AppearanceRemote"},
		},
		"last_aoi_tick":1,
	}
	view.set_player_snapshot(snapshot)
	await wait_ready(view)
	await wait_profile(view,own_app)
	await wait_profile(view,remote_app)
	check(
		view.players._actors.has(1) and
		str(view.players._actors[1].profile.key)==Profile.appearance_key(own_app),
		"self actor did not keep verified starter clothing profile")
	check(
		view.players._actors.has(2) and
		str(view.players._actors[2].profile.key)==Profile.appearance_key(remote_app),
		"initial remote actor did not use authoritative appearance")
	view._players_dirty=true
	view.present()
	var initial_remote_label: Dictionary = {}
	for label in view._canvas._player_labels:
		if str(label.actor_id)=="2":
			initial_remote_label=label
			break
	var initial_remote_feet:=view.players.position_at(2,Time.get_ticks_usec())
	check(not initial_remote_label.is_empty() and
		bool(initial_remote_label.get("recovered_player_anchor",false)) and
		initial_remote_label.p.distance_to(initial_remote_feet)<0.01,
		"first player-name frame did not use Recovered fixed player-node anchor")

	var dynamic_app := {
		"race":2,"hair_index":1,"hair_color":2,
		"body_index":1,"leg_index":2,"sleeve_color":2}
	remote=actor_state(2,7976.0,dynamic_app,2)
	snapshot.entities={2:remote}
	snapshot.last_aoi_tick=2
	view.set_player_snapshot(snapshot)
	await wait_profile(view,dynamic_app)
	var dynamic_deadline := Time.get_ticks_msec()+30000
	while (not view._pending_player_profiles.is_empty() or
			not view.players._actors.has(2) or
			str(view.players._actors[2].profile.key)!=Profile.appearance_key(dynamic_app)) and 			Time.get_ticks_msec()<dynamic_deadline:
		await process_frame
	check(view._pending_player_profiles.is_empty(),"dynamic profile queue did not drain")
	check(
		view.players._actors.has(2) and
		str(view.players._actors[2].profile.key)==Profile.appearance_key(dynamic_app),
		"runtime AOI appearance did not switch actor profile")
	view._players_dirty=true
	view.present()
	var dynamic_remote_label: Dictionary = {}
	for label in view._canvas._player_labels:
		if str(label.actor_id)=="2":
			dynamic_remote_label=label
			break
	var dynamic_remote_feet:=view.players.position_at(2,Time.get_ticks_usec())
	check(not dynamic_remote_label.is_empty() and
		bool(dynamic_remote_label.get("recovered_player_anchor",false)) and
		dynamic_remote_label.p.distance_to(dynamic_remote_feet)<0.01,
		"appearance load changed player-name local anchor")
	for asset_id in [
		"players/a/base/leg",
		"equipment/a/cloth/body00",
		"equipment/a/cloth/leg00",
		"equipment/a/cloth/l_arm00",
		"equipment/a/cloth/r_arm00",
		"players/a/hair/hair01"]:
		check(view._palette.has(asset_id),"dynamic profile asset not acquired: "+asset_id)
		check(view.draw_list._assets.has(asset_id),
			"dynamic profile asset loaded for PlayerPresentation but not DrawList: "+asset_id)
	var remote_actor: Dictionary = view.players._actors[2]
	var rendered := view.players.actors_at(Time.get_ticks_usec())
	var remote_render: Dictionary = {}
	for actor in rendered:
		if str(actor.id)=="2":
			remote_render=actor
			break
	check(not remote_render.is_empty(),"remote layered actor was not rendered")
	if not remote_render.is_empty():
		check(remote_render.parts.any(func(part):return str(part.asset)=="players/a/hair/hair01"),
			"dynamic hair01 missing from paper-doll parts")
		view._players_dirty=true
		view.present()
		check(view._canvas._commands.any(func(c):
			return str(c.actor_id)=="2" and int(c.get("slot",-1))==1),
			"dynamic hair01 was rejected by DrawList after profile load")
		var base_leg_at: int = remote_render.parts.find_custom(
			func(part): return str(part.asset)=="players/a/base/leg")
		var shorts_at: int = remote_render.parts.find_custom(
			func(part): return str(part.asset)=="equipment/a/cloth/leg00")
		check(
			base_leg_at>=0 and shorts_at>base_leg_at,
			"starter shorts replaced Standard Leg in world rendering")
		for side in ["left","right"]:
			var slot := 5 if side=="left" else 7
			var base_arm_at: int = remote_render.parts.find_custom(
				func(part): return int(part.part_slot)==slot and str(part.asset)=="players/a/base/%s_arm" % side)
			var sleeve_at: int = remote_render.parts.find_custom(
				func(part): return int(part.part_slot)==slot and str(part.asset)=="equipment/a/cloth/%s_arm00" % ("l" if side=="left" else "r"))
			check(base_arm_at>=0 and sleeve_at>base_arm_at,
				"starter sleeve replaced Standard %s arm in world rendering" % side)
		var hair_colored: bool = remote_render.parts.any(
			func(part): return str(part.asset)=="players/a/hair/hair01" and int(part.get("palette_index",-1))==2)
		var body_colored: bool = remote_render.parts.any(
			func(part): return str(part.asset)=="equipment/a/cloth/body00" and int(part.get("palette_index",-1))==1)
		var leg_colored: bool = remote_render.parts.any(
			func(part): return str(part.asset)=="equipment/a/cloth/leg00" and int(part.get("palette_index",-1))==2)
		var left_sleeve_colored: bool = remote_render.parts.any(
			func(part): return str(part.asset)=="equipment/a/cloth/l_arm00" and int(part.get("palette_index",-1))==2)
		var right_sleeve_colored: bool = remote_render.parts.any(
			func(part): return str(part.asset)=="equipment/a/cloth/r_arm00" and int(part.get("palette_index",-1))==2)
		check(hair_colored and body_colored and leg_colored and
				left_sleeve_colored and right_sleeve_colored,
			"authoritative hair/body/leg/sleeve palette indexes did not reach world commands")
	# WorldView.present() is the production path that feeds palette commands
	# into the batched WorldCanvas. Palette baking must stay off the main thread.
	var present_started := Time.get_ticks_usec()
	view.present()
	var present_usec := Time.get_ticks_usec()-present_started
	check(present_usec < 50000,
		"world palette submission blocked the main thread")
	var palette_deadline := Time.get_ticks_msec()+30000
	while view._canvas.palette_pending_count()>0 and Time.get_ticks_msec()<palette_deadline:
		await process_frame
	check(view._canvas.palette_pending_count()==0,
		"async world palette jobs did not drain")
	check(view._canvas.palette_texture_count()>=5,
		"world palette cache did not bake shared hair/body/leg/sleeve variants")

	view.clear_map()
	check(view._canvas.palette_texture_count()==0,
		"map clear retained palette GPU textures")
	var drain_deadline := Time.get_ticks_msec()+30000
	while (view.store.statistics().decode_jobs+
			view.store.statistics().texture_jobs)>0 and 			Time.get_ticks_msec()<drain_deadline:
		view.store.pump(2)
		await process_frame
	var final: Dictionary=view.store.statistics()
	check(
		final.assets+final.scopes+final.rgba_bytes+
		final.decode_jobs+final.texture_jobs==0,
		"world appearance scope retained resources")
	view.queue_free()
	await process_frame
	if failures.is_empty():
		print("WORLD_APPEARANCE_PROFILE_PASS initial_clothing=true standard_limbs_preserved=true shorts_overlay=true sleeves_overlay=true dynamic_aoi=true bounded_loading=true resources_drained=true")
	quit(0 if failures.is_empty() else 1)
