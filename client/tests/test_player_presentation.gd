extends SceneTree
const Players = preload("res://player/player_presentation.gd")
const AppearanceProfile = preload("res://player/character_appearance_profile.gd")
const WorldView = preload("res://world/world_view.gd")
const DrawList = preload("res://world/world_draw_list.gd")
var failures: Array = []
func check(ok: bool, message: String) -> void:
	if not ok:
		failures.append(message)
		push_error(message)

func _initialize() -> void:
	run.call_deferred()

func grid(w: int,h: int, walls: Array[Vector2i] = []) -> PackedByteArray:
	var bytes := PackedByteArray()
	bytes.resize(8+w*h)
	bytes.encode_u32(0,w)
	bytes.encode_u32(4,h)
	for wall in walls:
		bytes[8+wall.y*w+wall.x] = 7
	return bytes

func run() -> void:
	var image := Image.create(4,4,false,Image.FORMAT_RGBA8)
	image.fill(Color.WHITE)
	var texture := ImageTexture.create_from_image(image)
	var assets := {}
	var source_slots := [12.0,7.0,9.0,5.0,0.0,1.0] # Real JSON numbers use float Variants.
	var definition: Dictionary = AppearanceProfile.definition({})
	var rendered_slots: Array[int] = []
	for raw_slot in source_slots:
		var slot := int(raw_slot)
		if definition.parts.has(slot):
			rendered_slots.append(slot)
		if definition.overlays.has(slot):
			rendered_slots.append(slot)
	var profile_parts: Array = definition.parts.values()
	profile_parts.append_array(definition.overlays.values())
	for part in profile_parts:
		var views := {}
		var clips := {"NS":[0,1],"NR":[0,1],"AA":[0,1],"NH":[0,1],"ND":[0,1]}
		for action in ["NS","NR","AA","NH","ND"]:
			for facing in range(1,9):
				var one_shot: bool = str(action) in ["AA","NH","ND"]
				views[str(part[1])+"_"+action+str(facing)] = {
					"clip":str(action),"flip_h":facing>=5,
					# One leg layer has deliberately uneven original ACT timings.
					# All body, leg, sleeve and equipment layers share fixed 8 FPS.
					"timeline_ticks":[[0,4],[1,4]] if one_shot else ([[0,5],[1,11]] if str(part[1]) == "B-LEG" else [[0,8],[1,8]]),
					"loop":not one_shot,"draw_slots":source_slots}
		assets[part[0]] = {"pages":[texture],"manifest":{"logical_views":views,"clips":clips,"frames":[
			{"page":0,"rect":[0,0,3,2],"trim":[2,0],"origin":[1,1]},
			{"page":0,"rect":[0,2,3,2],"trim":[2,0],"origin":[1,1]}]}}
	var players := Players.new()
	check(players.configure(assets)==OK,"normalized base profile configure")
	var draw := DrawList.new()
	check(draw.configure({"scenery_schema":"sgtest.scenery/v1","scenery_order":{"version":1,"groups":[[],[],[]]},"objects":[],"scenery_definitions":{}},assets,1)==OK,"draw configure")
	var own := {"actor_id":1,"connection_epoch":1,"entity_map_epoch":1,"x":10.0,"y":10.0,"facing":6,"moving":true,"movement_action_sequence":1,"server_tick":1}
	var remote := own.duplicate()
	remote.actor_id=2
	remote.y=20.0
	var snapshot := {"world_ready":true,"map_epoch":1,"map_id":15,"actor_id":1,"x":10.0,"y":10.0,"facing":6,"self_state":own,"entities":{2:remote},"last_aoi_tick":1}
	players.apply_snapshot(snapshot,0)
	# The local avatar is presented from locally predicted position; remote
	# avatars independently continue walking on their last known velocity.
	var prediction_probe := Players.new()
	check(prediction_probe.configure(assets)==OK,"prediction probe configure")
	var probe_snapshot := snapshot.duplicate(true)
	prediction_probe.apply_snapshot(probe_snapshot,0)
	probe_snapshot.entities[2].x=18.0
	probe_snapshot.entities[2].server_tick=2
	prediction_probe.apply_snapshot(probe_snapshot,33333)
	var known_remote: Vector2 = prediction_probe.position_at(2,33333)
	prediction_probe.set_local_visual(1,Vector2(60,10),true,3,50000)
	var predicted_own: Vector2 = prediction_probe.position_at(1,200000)
	var predicted_remote: Vector2 = prediction_probe.position_at(2,200000)
	check(predicted_own==Vector2(60,10),"owner view waited for authoritative SelfState")
	check(predicted_remote.x>18.0 and predicted_remote.x<=50.01 and predicted_remote.x>=known_remote.x,
		"remote stopped walking between received AOI positions")
	check(int(prediction_probe.actors_at(200000).filter(func(v):return str(v.id)=="1")[0].p.x)==60,
		"paper doll ignored own local position")
	check(probe_snapshot.self_state.x==10.0,"visual prediction mutated source authority")
	prediction_probe.clear_local_visual()
	check(prediction_probe.position_at(1,200000).distance_to(Vector2(10,10))<2.0,
		"clearing own prediction kept stale position")
	# PlayerMovementIntent carries the Server-accepted final target. When the
	# current map exposes the same Blocking.dat, a peer should keep walking the
	# reconstructed route far beyond the old 32px dead-reckoning cap without
	# mutating the authoritative snapshot or rewinding on sparse checkpoints.
	var route_probe := Players.new()
	check(route_probe.configure(assets)==OK,"remote route profile configure")
	var route_manifest := {"collision_cell":16,"size":[192,192]}
	check(route_probe.configure_remote_routes(grid(12,12),route_manifest),
		"remote route collision configure")
	var route_snapshot: Dictionary = snapshot.duplicate(true)
	route_snapshot.entities[2].x=24.0
	route_snapshot.entities[2].y=24.0
	route_snapshot.entities[2].server_tick=1
	route_snapshot.entities[2].moving=true
	route_snapshot.remote_movement_intents={2:{
		"receiver_map_epoch":1,"server_tick":1,"actor_id":2,
		"connection_epoch":1,"entity_map_epoch":1,"movement_action_sequence":1,
		"target_x":152.0,"target_y":24.0,"moving":true,"facing":6}}
	route_probe.apply_snapshot(route_snapshot,0)
	check(route_probe.position_at(2,0)==Vector2(24,24),"remote route did not seed authority")
	for _frame in range(20):
		route_probe.advance_remote_visual(1.0/60.0)
	var routed_before_checkpoint := route_probe.position_at(2,333333)
	check(routed_before_checkpoint.x>100.0 and routed_before_checkpoint.x<=104.01,
		"movement intent remained capped to short checkpoint extrapolation")
	check(route_snapshot.entities[2].x==24.0,
		"remote route presentation mutated WorldState snapshot")
	route_snapshot.entities[2].x=64.0
	route_snapshot.entities[2].server_tick=8
	route_probe.apply_snapshot(route_snapshot,333333)
	check(route_probe.position_at(2,333333)==routed_before_checkpoint,
		"sparse authoritative checkpoint rewound healthy remote route")
	for _frame in range(20):
		route_probe.advance_remote_visual(1.0/60.0)
	check(route_probe.position_at(2,666666).distance_to(Vector2(152,24))<0.01,
		"remote route did not reach authoritative final target")
	check(not bool(route_probe._actors[2].value.moving),
		"remote paper doll kept walking after reconstructed route arrival")
	route_snapshot.entities[2].x=152.0
	route_snapshot.entities[2].moving=false
	route_snapshot.entities[2].server_tick=20
	route_snapshot.remote_movement_intents[2].moving=false
	route_snapshot.remote_movement_intents[2].server_tick=20
	route_probe.apply_snapshot(route_snapshot,700000)
	check(route_probe.position_at(2,700000)==Vector2(152,24) and not bool(route_probe._actors[2].value.moving),
		"authoritative remote stop did not anchor the final position")
	# A newly reported avatar appearance may need async atlas loads. Keep
	# the self avatar visible using an existing/default profile during loading.
	var pending_profile := Players.new()
	check(pending_profile.configure(assets)==OK,"pending profile probe")
	pending_profile.apply_snapshot(snapshot,0)
	var profile_wait: Dictionary = snapshot.duplicate(true)
	profile_wait.self_state["appearance"]={"race":1,"hair_index":1,"hair_color":0,
		"body_index":0,"leg_index":0,"sleeve_color":0}
	profile_wait.self_state.x=26.0
	profile_wait.self_state.server_tick=2
	pending_profile.apply_snapshot(profile_wait,33000)
	check(pending_profile.actors_at(33000).any(func(a):return str(a.id)=="1"),
		"pending custom appearance erased the entire local avatar")
	check(pending_profile.position_at(1,33000).x==26.0,
		"pending appearance froze owner's position and camera")
	# WorldReady comes before SelfState. A locally walking player must never
	# revert to NS and reset the walk phase on repeated empty-SelfState polls.
	var spawn_probe := Players.new()
	check(spawn_probe.configure(assets)==OK,"WorldReady-only visual configure")
	var spawn_snapshot: Dictionary = snapshot.duplicate(true)
	spawn_snapshot.self_state = {}
	spawn_probe.apply_snapshot(spawn_snapshot,0)
	spawn_probe.set_local_visual(1,Vector2(14,10),true,6,0)
	for at_usec in [25000,50000,75000,125000]:
		spawn_probe.apply_snapshot(spawn_snapshot,at_usec)
		check(str(spawn_probe._actors[1].action)=="NR",
			"WorldReady missing SelfState reset walk pose at %d" % at_usec)
	check(int(spawn_probe.actors_at(125000).filter(func(v):return str(v.id)=="1")[0].parts[0].frame)==1,
		"WorldReady replay restarted the walking phase instead of moving")
	check(spawn_probe.position_at(1,125000)==Vector2(14,10),
		"WorldReady fallback left own walk behind predicted position")
	# Recovered2's fixed 8 FPS shared paper-doll timing is the contract;
	# movement speed and raw ACT durations must never change this cadence.
	# A variable raw ACT duration in B-LEG must not desynchronize the legs.
	var cadence := Players.new()
	check(cadence.configure(assets)==OK,"walk-cadence profile setup")
	var walk_snapshot: Dictionary = snapshot.duplicate(true)
	cadence.apply_snapshot(walk_snapshot,0)
	for sample in [[124999,0],[125000,1],[249999,1],[250000,0],[374999,0],[375000,1]]:
		var sample_actors: Array = cadence.actors_at(int(sample[0]))
		check(sample_actors.size()==2,"fixed walk cadence actor lost")
		for sample_actor in sample_actors:
			for layer in sample_actor.parts:
				check(int(layer.frame)==int(sample[1]),"8 FPS body/legs/clothes diverged at %d usec" % int(sample[0]))
	# Change facing during the walk: retain the same phase for the new
	# directional clips, exactly like Recovered2 sharing the Body frame.
	walk_snapshot.self_state.facing=3
	walk_snapshot.entities[2].facing=3
	cadence.apply_snapshot(walk_snapshot,188000)
	var turned: Array = cadence.actors_at(188000)
	check(turned.size()==2 and int(turned[0].parts[0].frame)==1 and not bool(turned[0].parts[0].flip_h),
		"turning restarted the walk cycle")
	# A one-shot overlay cannot freeze the underlying walk pose on its first
	# frame while the Server still considers the player to be walking.
	walk_snapshot.player_attack_events=[{
		"receiver_map_epoch":1,"server_tick":2,"attacker_actor_id":2,
		"attacker_connection_epoch":1,"attack_sequence":1,
		"target_entity_id":1001500001,"status":0,"damage":1,
		"target_revision":2,"facing":3}]
	cadence.apply_snapshot(walk_snapshot,350000)
	var after_attack: Dictionary=cadence.actors_at(550000).filter(func(v):return str(v.id)=="2")[0]
	check(int(after_attack.parts[0].frame)==0,"one-shot completion changed the fixed walk phase")
	# A genuine stop/restart does reset the cycle, unlike path repaths.
	walk_snapshot.player_attack_events=[]
	walk_snapshot.self_state.moving=false
	walk_snapshot.entities[2].moving=false
	cadence.apply_snapshot(walk_snapshot,680000)
	walk_snapshot.self_state.moving=true
	walk_snapshot.entities[2].moving=true
	cadence.apply_snapshot(walk_snapshot,725000)
	check(int(cadence.actors_at(849999)[0].parts[0].frame)==0 and
		int(cadence.actors_at(850000)[0].parts[0].frame)==1,
		"idle->walk did not restart the exact 125ms walking cadence")
	# Long-held mouse movement can issue fresh MoveTarget commands while
	# the character never stops running. A new command sequence must not
	# reset the NR animation clock or lock the paper doll in frame zero.
	own.movement_action_sequence = 2
	remote.movement_action_sequence = 2
	players.apply_snapshot(snapshot,100000)
	# The 125ms shared walk clock advances at a fixed 8 FPS regardless of
	# 50 Hz ACT per-layer frame time, position delta, or MoveTarget retransmissions.
	var early_walk: Array = players.actors_at(140000)
	check(early_walk.size()==2 and int(early_walk[0].parts[0].frame)==1 and int(early_walk[1].parts[0].frame)==1,"fixed walk animation phase diverged for owner and observer")
	var actors := players.actors_at(160000)
	check(actors.size()==2 and actors[0].parts.size()==rendered_slots.size() and
		int(actors[0].parts[0].frame)==1 and int(actors[1].parts[0].frame)==1,
		"continuous movement sequence repath reset walk animation")
	for actor in actors:
		var base_leg_at: int = actor.parts.find_custom(
			func(part): return str(part.asset)=="players/b/base/leg")
		var shorts_at: int = actor.parts.find_custom(
			func(part): return str(part.asset)=="equipment/b/cloth/leg00")
		check(base_leg_at>=0 and shorts_at>base_leg_at,
			"starter shorts replaced Standard Leg")
	var commands := draw.build(Rect2(-100,-100,400,400),actors)
	check(draw.error==OK and commands.size()==rendered_slots.size()*2,"multipart ordered draw")
	for i in commands.size():
		var part_index: int = i%rendered_slots.size()
		check(commands[i].actor_id==("1" if i<rendered_slots.size() else "2") and
			int(commands[i].slot)==rendered_slots[part_index],
			"actor parts interleaved / wrong source slot")
	check(commands[0].destination==Rect2(9,9,-3,2),"mirrored trimmed origin wrong")
	# A reliable remote attack event uses Server facing=3 even while the last
	# movement snapshot still faces 6. Repeated snapshot polling cannot restart
	# or interrupt the one-shot AA.
	snapshot.player_attack_events=[{
		"receiver_map_epoch":1,"server_tick":2,
		"attacker_actor_id":2,"attacker_connection_epoch":1,
		"attack_sequence":1,"target_entity_id":1001500001,
		"status":0,"damage":30,"target_revision":2,"facing":3}]
	players.apply_snapshot(snapshot,170000)
	actors=players.actors_at(170000)
	var remote_attack: Dictionary=actors.filter(func(v): return str(v.id)=="2")[0]
	check(not bool(remote_attack.parts[0].flip_h),"remote AA ignored Server attack facing")
	players.apply_snapshot(snapshot,230000)
	actors=players.actors_at(250000)
	remote_attack=actors.filter(func(v): return str(v.id)=="2")[0]
	check(not bool(remote_attack.parts[0].flip_h),"snapshot poll interrupted one-shot AA")
	actors=players.actors_at(350000)
	remote_attack=actors.filter(func(v): return str(v.id)=="2")[0]
	check(bool(remote_attack.parts[0].flip_h),"completed AA did not return to movement facing")

	# Durable Monster->Player damage presentation is independent from HP
	# authority. NH locks event facing for one source timeline, lethal ND stays
	# latched until a shared authoritative respawn visual clears it.
	snapshot.player_attack_events=[]
	snapshot.player_damage_visual_events=[{
		"receiver_map_epoch":1,"server_tick":3,
		"victim_actor_id":2,"victim_connection_epoch":1,
		"damage_sequence":1,"attacker_monster_entity_id":1001500001,
		"attacker_monster_revision":3,"damage":18,
		"current_hp":82,"max_hp":100,"died":false,"facing":3}]
	players.apply_snapshot(snapshot,360000)
	var remote_hit: Dictionary=players.actors_at(360000).filter(
		func(v): return str(v.id)=="2")[0]
	check(not bool(remote_hit.parts[0].flip_h),
		"remote NH ignored authoritative hit facing")
	players.apply_snapshot(snapshot,400000)
	remote_hit=players.actors_at(560000).filter(func(v): return str(v.id)=="2")[0]
	check(bool(remote_hit.parts[0].flip_h),
		"completed NH did not return to movement facing")
	snapshot.player_damage_visual_events.append({
		"receiver_map_epoch":1,"server_tick":4,
		"victim_actor_id":2,"victim_connection_epoch":1,
		"damage_sequence":2,"attacker_monster_entity_id":1001500001,
		"attacker_monster_revision":4,"damage":82,
		"current_hp":0,"max_hp":100,"died":true,"facing":3})
	players.apply_snapshot(snapshot,600000)
	var remote_dead: Dictionary=players.actors_at(600000).filter(
		func(v): return str(v.id)=="2")[0]
	check(not bool(remote_dead.parts[0].flip_h),"remote ND ignored death facing")
	remote_dead=players.actors_at(2000000).filter(func(v): return str(v.id)=="2")[0]
	check(not bool(remote_dead.parts[0].flip_h),
		"death animation incorrectly returned to live movement without respawn")
	# A reliable pre-death attack visual may still be present/arrive after the
	# lethal damage visual. Once ND is latched, no transient attack may revive
	# the paper doll; only PlayerRespawnVisual is allowed to clear death.
	snapshot.player_damage_visual_events=[]
	snapshot.player_attack_events=[{
		"receiver_map_epoch":1,"server_tick":5,
		"attacker_actor_id":2,"attacker_connection_epoch":1,
		"attack_sequence":2,"target_entity_id":1001500001,
		"status":0,"damage":30,"target_revision":5,"facing":6}]
	players.apply_snapshot(snapshot,2050000)
	remote_dead=players.actors_at(2050000).filter(func(v): return str(v.id)=="2")[0]
	check(str(remote_dead.parts[0].clip)=="ND",
		"reliable attack visual overrode latched death before respawn")
	snapshot.player_attack_events=[]
	snapshot.player_respawn_visual_events=[{
		"receiver_map_epoch":1,"server_tick":5,
		"actor_id":2,"connection_epoch":1,"respawn_sequence":1,"facing":6}]
	players.apply_snapshot(snapshot,2100000)
	var remote_revived: Dictionary=players.actors_at(2100000).filter(
		func(v): return str(v.id)=="2")[0]
	check(bool(remote_revived.parts[0].flip_h),
		"authoritative respawn visual did not clear persistent death state")

	# No refresh/render callback advances authoritative state.
	check(own.x==10.0 and own.facing==6,"presentation mutated authority")
	own.moving=false
	snapshot.entities={}
	snapshot.player_attack_events=[]
	snapshot.player_damage_visual_events=[]
	snapshot.player_respawn_visual_events=[]
	players.apply_snapshot(snapshot,2200000)
	actors=players.actors_at(2200000)
	check(actors.size()==1 and int(actors[0].parts[0].frame)==0,"despawn or stop action reset failed")
	snapshot.map_epoch=2
	snapshot.map_id=13
	snapshot.self_state={}
	snapshot.x=50.0
	snapshot.last_aoi_tick=2
	players.apply_snapshot(snapshot,2300000)
	actors=players.actors_at(2300000)
	check(actors.size()==1 and actors[0].p==Vector2(50,10),"transfer retained old owner")
	# Initial WorldReady after login/transfer can precede changed-only SelfState.
	# A server-only own attack visual still carries its authenticated generation.
	snapshot.player_attack_events=[{
		"receiver_map_epoch":2,"server_tick":3,
		"attacker_actor_id":1,"attacker_connection_epoch":9,
		"attack_sequence":1,"target_entity_id":1001500001,
		"status":0,"damage":30,"target_revision":2,"facing":3}]
	players.apply_snapshot(snapshot,2320000)
	actors=players.actors_at(2320000)
	check(actors.size()==1 and not bool(actors[0].parts[0].flip_h),
		"own AA required changed-only SelfState generation")
	var view := WorldView.new()
	root.add_child(view)
	view.show_players=true
	view.map_id=13
	var borrowed: Dictionary=snapshot.duplicate(true)
	view.set_player_snapshot(borrowed)
	view._consume_player_attack_events({"player_attack_events":[{
		"attacker_actor_id":1,"attacker_connection_epoch":1,
		"attack_sequence":1,"target_entity_id":1001,"status":0,"damage":30}]},600000)
	view._consume_player_attack_events({"player_attack_events":[{
		"attacker_actor_id":1,"attacker_connection_epoch":1,
		"attack_sequence":1,"target_entity_id":1001,"status":0,"damage":30}]},610000)
	check(view.combat_overlay_count()==1,"damage overlay replay was not deduplicated")
	view._consume_player_attack_events({"player_attack_events":[{
		"attacker_actor_id":1,"attacker_connection_epoch":1,
		"attack_sequence":2,"target_entity_id":1001,"status":1,"damage":0}]},620000)
	check(view.combat_overlay_count()==2,"MISS overlay was not retained independently")
	view._consume_player_damage_visual_events({"player_damage_visual_events":[{
		"victim_actor_id":1,"victim_connection_epoch":1,
		"damage_sequence":1,"damage":18,"died":false}]},630000)
	view._consume_player_damage_visual_events({"player_damage_visual_events":[{
		"victim_actor_id":1,"victim_connection_epoch":1,
		"damage_sequence":1,"damage":18,"died":false}]},640000)
	check(view.combat_overlay_count()==3,
		"player damage overlay was missing or replayed")
	# Attack sequence 1 and damage sequence 1 are separate presentation
	# namespaces and must not suppress each other.
	view._consume_player_damage_visual_events({"player_damage_visual_events":[{
		"victim_actor_id":1,"victim_connection_epoch":1,
		"damage_sequence":2,"damage":82,"died":true}]},650000)
	check(view.combat_overlay_count()==4,
		"lethal player damage overlay was not retained independently")
	var pvp_overlay={"pvp_combat_visual_events":[{
		"attacker_actor_id":1,"attacker_connection_epoch":1,"attack_sequence":1,
		"victim_actor_id":2,"victim_connection_epoch":1,"damage_sequence":1,
		"damage":58,"died":false,"attacker_facing":3,"victim_facing":3}]}
	view._consume_pvp_combat_visual_events(pvp_overlay,660000)
	view._consume_pvp_combat_visual_events(pvp_overlay,670000)
	check(view.combat_overlay_count()==5,
		"PvP combat overlay was missing or replayed")
	var pvp_players:=Players.new()
	check(pvp_players.configure(assets)==OK,"PvP presentation configure")
	var pvp_snapshot:Dictionary=snapshot.duplicate(true)
	pvp_snapshot.map_epoch=3
	pvp_snapshot.map_id=13
	pvp_snapshot.actor_id=1
	pvp_snapshot.x=10.0
	pvp_snapshot.y=10.0
	pvp_snapshot.facing=6
	pvp_snapshot.self_state={
		"actor_id":1,"connection_epoch":1,"entity_map_epoch":3,
		"x":10.0,"y":10.0,"facing":6,"moving":false,
		"movement_action_sequence":0,"server_tick":10}
	var pvp_remote={
		"actor_id":2,"connection_epoch":2,"entity_map_epoch":3,
		"x":20.0,"y":10.0,"facing":6,"moving":false,
		"movement_action_sequence":0,"server_tick":10}
	pvp_snapshot.entities={2:pvp_remote}
	pvp_snapshot.player_attack_events=[]
	pvp_snapshot.player_damage_visual_events=[]
	pvp_snapshot.player_respawn_visual_events=[]
	pvp_snapshot.pvp_combat_visual_events=[]
	pvp_players.apply_snapshot(pvp_snapshot,3000000)
	pvp_snapshot.pvp_combat_visual_events=[{
		"receiver_map_epoch":3,"server_tick":11,
		"attacker_actor_id":1,"attacker_connection_epoch":1,"attack_sequence":1,
		"victim_actor_id":2,"victim_connection_epoch":2,"damage_sequence":1,
		"damage":58,"died":false,"attacker_facing":3,"victim_facing":3}]
	pvp_players.apply_snapshot(pvp_snapshot,3010000)
	var pvp_actors:=pvp_players.actors_at(3010000)
	var pvp_attacker:Dictionary=pvp_actors.filter(func(v): return str(v.id)=="1")[0]
	var pvp_victim:Dictionary=pvp_actors.filter(func(v): return str(v.id)=="2")[0]
	check(not bool(pvp_attacker.parts[0].flip_h),
		"PvP attacker AA ignored authoritative facing")
	check(not bool(pvp_victim.parts[0].flip_h),
		"PvP victim NH ignored authoritative facing")
	# Death dominance applies to every transient combat visual namespace. A
	# queued PvP attack/skill from the same actor must not resurrect AA before
	# the authoritative respawn visual arrives.
	pvp_snapshot.pvp_combat_visual_events=[{
		"receiver_map_epoch":3,"server_tick":12,
		"attacker_actor_id":2,"attacker_connection_epoch":2,"attack_sequence":2,
		"victim_actor_id":1,"victim_connection_epoch":1,"damage_sequence":2,
		"damage":100,"died":true,"attacker_facing":6,"victim_facing":3}]
	pvp_players.apply_snapshot(pvp_snapshot,3020000)
	pvp_snapshot.pvp_combat_visual_events=[{
		"receiver_map_epoch":3,"server_tick":13,
		"attacker_actor_id":1,"attacker_connection_epoch":1,"attack_sequence":3,
		"victim_actor_id":2,"victim_connection_epoch":2,"damage_sequence":3,
		"damage":1,"died":false,"attacker_facing":6,"victim_facing":6}]
	pvp_players.apply_snapshot(pvp_snapshot,3030000)
	pvp_attacker=pvp_players.actors_at(3030000).filter(func(v): return str(v.id)=="1")[0]
	check(str(pvp_attacker.parts[0].clip)=="ND",
		"PvP attack visual overrode latched death before respawn")
	pvp_snapshot.pvp_combat_visual_events=[]
	pvp_snapshot.pvp_skill_visual_events=[{
		"receiver_map_epoch":3,"server_tick":14,
		"attacker_actor_id":1,"attacker_connection_epoch":1,"skill_sequence":1,
		"skill_id":99001,"victim_actor_id":2,"victim_connection_epoch":2,
		"damage_sequence":4,"damage":1,"died":false,
		"attacker_facing":6,"victim_facing":6}]
	pvp_players.apply_snapshot(pvp_snapshot,3040000)
	pvp_attacker=pvp_players.actors_at(3040000).filter(func(v): return str(v.id)=="1")[0]
	check(str(pvp_attacker.parts[0].clip)=="ND",
		"PvP skill visual overrode latched death before respawn")
	pvp_players.clear()
	view.clear_map()
	check(borrowed.has("x") and int(borrowed.map_id)==13,"view clear mutated caller snapshot")
	check(view.combat_overlay_count()==0,"view clear retained combat overlays")
	view.free()
	players.clear()
	check(players.actors_at(0).is_empty(),"clear retained actors/assets")
	assets[Players.PARTS[1][0]].manifest.logical_views={}
	check(players.configure(assets)!=OK and players.actors_at(0).is_empty(),"missing metadata did not fail closed")
	if failures.is_empty():
		print("PLAYER_PRESENTATION_PASS layered_parts=true actor_atomic_sort=true logical_flip_pivot=true normalized_ACT=true authoritative_AA=true remote_AA_lock=true authoritative_NH=true persistent_ND=true respawn_clears_death=true own_without_self_state=true combat_overlay=true player_damage_overlay=true pvp_attack_hit_visual=true pvp_damage_overlay=true no_remote_hp_dependency=true stop_despawn_transfer=true")
	quit(0 if failures.is_empty() else 1)
