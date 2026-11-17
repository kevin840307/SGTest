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
		var clips := {"SOURCE":[0,1]}
		for action in ["NS","NR"]:
			for facing in range(1,9):
				views[str(part[1])+"_"+action+str(facing)] = {"clip":"SOURCE","flip_h":facing>=5,"timeline_ticks":[[0,8],[1,8]],"loop":true,"draw_slots":slots}
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
	# No refresh/render callback advances authoritative state.
	check(own.x==10.0 and own.facing==6,"presentation mutated authority")
	own.moving=false
	snapshot.entities={}
	players.apply_snapshot(snapshot,200000)
	actors=players.actors_at(200000)
	check(actors.size()==1 and int(actors[0].parts[0].frame)==0,"despawn or stop action reset failed")
	snapshot.map_epoch=2
	snapshot.map_id=13
	snapshot.self_state={}
	snapshot.x=50.0
	snapshot.last_aoi_tick=2
	players.apply_snapshot(snapshot,300000)
	actors=players.actors_at(300000)
	check(actors.size()==1 and actors[0].p==Vector2(50,10),"transfer retained old owner")
	# Initial WorldReady after login/transfer can precede changed-only SelfState.
	# A server-only own attack visual still carries its authenticated generation.
	snapshot.player_attack_events=[{
		"receiver_map_epoch":2,"server_tick":3,
		"attacker_actor_id":1,"attacker_connection_epoch":9,
		"attack_sequence":1,"target_entity_id":1001500001,
		"status":0,"damage":30,"target_revision":2,"facing":3}]
	players.apply_snapshot(snapshot,320000)
	actors=players.actors_at(320000)
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
		"attack_sequence":1,"target_entity_id":1001,"status":0,"damage":30}]},500000)
	view._consume_player_attack_events({"player_attack_events":[{
		"attacker_actor_id":1,"attacker_connection_epoch":1,
		"attack_sequence":1,"target_entity_id":1001,"status":0,"damage":30}]},510000)
	check(view.combat_overlay_count()==1,"damage overlay replay was not deduplicated")
	view._consume_player_attack_events({"player_attack_events":[{
		"attacker_actor_id":1,"attacker_connection_epoch":1,
		"attack_sequence":2,"target_entity_id":1001,"status":1,"damage":0}]},520000)
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
		print("PLAYER_PRESENTATION_PASS six_parts=true actor_atomic_sort=true logical_flip_pivot=true normalized_ACT=true stop_despawn_transfer=true")
	quit(0 if failures.is_empty() else 1)
