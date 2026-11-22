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

func run() -> void:
	var image := Image.create(4,4,false,Image.FORMAT_RGBA8)
	image.fill(Color.WHITE)
	var texture := ImageTexture.create_from_image(image)
	var assets := {}
	var source_slots := [12.0,7.0,9.0,5.0,0.0,1.0] # Real JSON numbers use float Variants.
	var rendered_slots := [12,12,7,9,9,5,0,1]
	var definition: Dictionary = AppearanceProfile.definition({})
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
					"timeline_ticks":[[0,4],[1,4]] if one_shot else [[0,8],[1,8]],
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
	# Long-held mouse movement can issue fresh MoveTarget commands while
	# the character never stops running. A new command sequence must not
	# reset the NR animation clock or lock the paper doll in frame zero.
	own.movement_action_sequence = 2
	remote.movement_action_sequence = 2
	players.apply_snapshot(snapshot,100000)
	# 1.25x walk cadence advances NR sooner than the raw 50 Hz timeline;
	# all paper-doll parts stay on the same frame without affecting movement.
	var early_walk: Array = players.actors_at(140000)
	check(early_walk.size()==2 and int(early_walk[0].parts[0].frame)==1 and int(early_walk[1].parts[0].frame)==1,"walk animation did not speed up for owner and observer")
	var actors := players.actors_at(160000)
	check(actors.size()==2 and actors[0].parts.size()==8 and int(actors[0].parts[0].frame)==1 and int(actors[1].parts[0].frame)==1,"continuous movement sequence repath reset walk animation")
	for actor in actors:
		var base_leg_at: int = actor.parts.find_custom(
			func(part): return str(part.asset)=="players/b/base/leg")
		var shorts_at: int = actor.parts.find_custom(
			func(part): return str(part.asset)=="equipment/b/cloth/leg00")
		check(base_leg_at>=0 and shorts_at>base_leg_at,
			"starter shorts replaced Standard Leg")
	var commands := draw.build(Rect2(-100,-100,400,400),actors)
	check(draw.error==OK and commands.size()==16,"multipart ordered draw")
	for i in 16:
		var actor_offset := 0 if i<8 else 8
		check(commands[i].actor_id==("1" if i<8 else "2") and 				int(commands[i].slot)==rendered_slots[i-actor_offset],
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
