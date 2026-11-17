extends SceneTree
const Players = preload("res://rendering/player_presentation.gd")
const WorldView = preload("res://rendering/world_view.gd")
const DrawList = preload("res://rendering/world_draw_list.gd")
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
	var slots := [12.0,7.0,9.0,5.0,0.0,1.0] # Real JSON numbers use float Variants.
	for part in Players.PARTS.values():
		var views := {}
		var clips := {"NS":[0,1],"NR":[0,1],"AA":[0,1],"NH":[0,1],"ND":[0,1]}
		for action in ["NS","NR","AA","NH","ND"]:
			for facing in range(1,9):
				var one_shot: bool = str(action) in ["AA","NH","ND"]
				views[str(part[1])+"_"+action+str(facing)] = {
					"clip":str(action),"flip_h":facing>=5,
					"timeline_ticks":[[0,4],[1,4]] if one_shot else [[0,8],[1,8]],
					"loop":not one_shot,"draw_slots":slots}
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
	players.apply_snapshot(snapshot,100000)
	var actors := players.actors_at(160000)
	check(actors.size()==2 and actors[0].parts.size()==6 and int(actors[0].parts[0].frame)==1,"paper doll/timeline restarted on snapshot poll")
	var commands := draw.build(Rect2(-100,-100,400,400),actors)
	check(draw.error==OK and commands.size()==12,"multipart ordered draw")
	for i in 12:
		check(commands[i].actor_id==("1" if i<6 else "2") and int(commands[i].slot)==slots[i%6],"actor parts interleaved / wrong source slot")
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
	view.clear_map()
	check(borrowed.has("x") and int(borrowed.map_id)==13,"view clear mutated caller snapshot")
	check(view.combat_overlay_count()==0,"view clear retained combat overlays")
	view.free()
	players.clear()
	check(players.actors_at(0).is_empty(),"clear retained actors/assets")
	assets[Players.PARTS[1][0]].manifest.logical_views={}
	check(players.configure(assets)!=OK and players.actors_at(0).is_empty(),"missing metadata did not fail closed")
	if failures.is_empty():
		print("PLAYER_PRESENTATION_PASS six_parts=true actor_atomic_sort=true logical_flip_pivot=true normalized_ACT=true authoritative_AA=true remote_AA_lock=true authoritative_NH=true persistent_ND=true respawn_clears_death=true own_without_self_state=true combat_overlay=true stop_despawn_transfer=true")
	quit(0 if failures.is_empty() else 1)
