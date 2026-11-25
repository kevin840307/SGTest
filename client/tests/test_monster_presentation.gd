extends SceneTree
const Monsters = preload("res://monster/monster_presentation.gd")
const DrawList = preload("res://world/world_draw_list.gd")
var failures: Array = []

func check(ok: bool, message: String) -> void:
	if not ok:
		failures.append(message)
		push_error(message)

func _initialize() -> void:
	run.call_deferred()

func run() -> void:
	var image := Image.create(8,8,false,Image.FORMAT_RGBA8)
	image.fill(Color.WHITE)
	var texture := ImageTexture.create_from_image(image)
	var views := {}
	var clips := {}
	for action in ["NS","NW","FA","NH","ND"]:
		clips[action]=[0,1]
		for facing in range(1,9):
			views["EN001A_"+action+str(facing)] = {
				"clip":action,"flip_h":facing>=5,
				"timeline_ticks":[[0,8],[1,8]],"loop":action!="ND"
			}
	var asset := {"pages":[texture],"manifest":{
		"logical_views":views,"clips":clips,"frames":[
			{"page":0,"rect":[0,0,4,4],"trim":[0,0],"origin":[2,2]},
			{"page":0,"rect":[4,0,4,4],"trim":[0,0],"origin":[2,2]}
	]}}
	var assets := {"Enemy/en001a":asset}
	var population := {
		"version":3,"kind":"visual_spawn_content","map_id":15,
		"stats_verified":false,"gameplay_ready":false,
		"definitions":[{
			"id":1,"sprite":"EN001A","atlas":"Enemy/en001a/atlas.json",
			# Deliberately bogus visual-document stats: renderer must ignore them.
			"stats":{"damage":999999},"stats_verified":false
		}]
	}
	var monsters := Monsters.new()
	check(monsters.configure(population,assets)==OK,"monster visual configure")
	var idle := {
		"receiver_map_epoch":1,"server_tick":1,"entity_id":1001,
		"enemy_id":1,"source_index":0,"map_id":15,"x":10.0,"y":20.0,
		"revision":1,"current_hp":100,"max_hp":100,"state":0,
		"facing":1,"target_actor_id":0
	}
	var snapshot := {
		"world_ready":true,"map_epoch":1,"map_id":15,
		"monsters":{1001:idle}
	}
	check(monsters.apply_snapshot(snapshot,0)==OK,"idle snapshot")
	check(monsters.pick_living_at(Vector2(10,20),0)==1001,
		"click at living monster did not choose target")
	check(monsters.pick_living_at(Vector2(500,500),0)==0 and
		monsters.pick_living_at(Vector2.INF,0)==0,
		"off-screen/invalid click selected a target")
	var actors := monsters.actors_at(0)
	check(actors.size()==1 and actors[0].clip=="NS" and actors[0].asset=="Enemy/en001a",
		"idle did not use verified S/NS visual mapping")

	var chase := idle.duplicate(true)
	chase.server_tick=2
	chase.x=30.0
	chase.revision=2
	chase.state=2
	chase.facing=6
	snapshot.monsters={1001:chase}
	check(monsters.apply_snapshot(snapshot,100000)==OK,"chase snapshot")
	actors=monsters.actors_at(120000)
	check(actors.size()==1 and actors[0].clip=="NW" and bool(actors[0].flip_h),
		"chase did not use verified W/NW facing view")

	var attack := chase.duplicate(true)
	attack.server_tick=3
	attack.revision=3
	attack.state=3
	snapshot.monsters={1001:attack}
	check(monsters.apply_snapshot(snapshot,200000)==OK,"attack snapshot")
	actors=monsters.actors_at(360000)
	check(actors.size()==1 and actors[0].clip=="FA" and int(actors[0].frame)==1,
		"attack did not use verified A/FA timeline")
	var repeated := attack.duplicate(true)
	repeated.server_tick=4
	repeated.revision=4
	snapshot.monsters={1001:repeated}
	check(monsters.apply_snapshot(snapshot,400000)==OK,"repeated attack")
	actors=monsters.actors_at(400000)
	check(int(actors[0].frame)==0,"authoritative repeat Attack did not restart FA")

	# Nonlethal authoritative damage interrupts Attack with one-shot Hit.
	var damaged := repeated.duplicate(true)
	damaged.server_tick=5
	damaged.revision=5
	damaged.state=2
	damaged.current_hp=80
	snapshot.monsters={1001:damaged}
	check(monsters.apply_snapshot(snapshot,500000)==OK,"nonlethal damage snapshot")
	actors=monsters.actors_at(500000)
	check(actors.size()==1 and actors[0].clip=="NH" and int(actors[0].frame)==0,
		"HP drop did not start one-shot NH")
	actors=monsters.actors_at(700000)
	check(actors.size()==1 and actors[0].clip=="NH" and int(actors[0].frame)==1,
		"NH did not retain original ACT frame durations")
	# Same revision and ordinary movement must not restart NH.
	var moved := damaged.duplicate(true)
	moved.server_tick=6
	moved.x=31.0
	snapshot.monsters={1001:moved}
	check(monsters.apply_snapshot(snapshot,750000)==OK,"same-revision move")
	actors=monsters.actors_at(850000)
	check(actors.size()==1 and actors[0].clip=="NW",
		"completed NH did not return to current authoritative movement")
	# A second verified HP loss restarts the hit, death preempts it.
	var damaged_again := moved.duplicate(true)
	damaged_again.server_tick=7
	damaged_again.revision=6
	damaged_again.current_hp=60
	snapshot.monsters={1001:damaged_again}
	check(monsters.apply_snapshot(snapshot,1000000)==OK,"second damage")
	actors=monsters.actors_at(1000000)
	check(actors.size()==1 and actors[0].clip=="NH" and int(actors[0].frame)==0,
		"second hit did not restart NH")
	var dead := damaged_again.duplicate(true)
	dead.server_tick=8
	dead.revision=7
	dead.state=4
	dead.current_hp=0
	snapshot.monsters={1001:dead}
	check(monsters.apply_snapshot(snapshot,1100000)==OK,"death snapshot")
	actors=monsters.actors_at(1400000)
	check(actors.size()==1 and actors[0].clip=="ND" and int(actors[0].frame)==1,
		"death did not hold verified D/ND terminal frame")
	check(monsters.pick_living_at(Vector2(float(dead.x),float(dead.y)),1400000)==0,
		"dead monster could still be selected for an attack")

	# Respawn begins a clean idle state; past damage animation cannot leak.
	var revived := dead.duplicate(true)
	revived.server_tick=9
	revived.revision=8
	revived.current_hp=100
	revived.state=0
	snapshot.monsters={1001:revived}
	check(monsters.apply_snapshot(snapshot,1500000)==OK,"respawn snapshot")
	actors=monsters.actors_at(1500000)
	check(actors.size()==1 and actors[0].clip=="NS",
		"respawn replayed old damage animation")
	check(monsters.pick_living_at(Vector2(float(revived.x),float(revived.y)),1500000)==1001,
		"respawn did not restore click selection")
	# One malformed AOI entry must not partially despawn a second actor or
	# apply a death update to the first; rejected cross-epoch frames must not
	# erase the previous map generation either.
	var another := revived.duplicate(true)
	another.entity_id = 1002
	another.source_index = 1
	another.x = 90.0
	another.revision = 1
	snapshot.monsters = {1001:revived,1002:another}
	check(monsters.apply_snapshot(snapshot,1550000)==OK and monsters.count()==2,
		"two-monster AOI baseline")
	# Reject impossible authoritative geometry/HP before altering any other
	# monster in the same AOI batch. Invalid positions cannot enter history.
	var invalid_position := revived.duplicate(true)
	invalid_position.x = INF
	snapshot.monsters = {1001:invalid_position,1002:another}
	check(monsters.apply_snapshot(snapshot,1570000)==ERR_INVALID_DATA and
		monsters.count()==2 and monsters.pick_living_at(Vector2(31,20),1570000)==1001,
		"non-finite monster position was accepted or changed an existing actor")
	var invalid_hp := revived.duplicate(true)
	invalid_hp.current_hp = invalid_hp.max_hp + 1
	snapshot.monsters = {1001:invalid_hp,1002:another}
	check(monsters.apply_snapshot(snapshot,1580000)==ERR_INVALID_DATA and
		monsters.count()==2 and monsters.pick_living_at(Vector2(90,20),1580000)==1002,
		"monster HP exceeding its authoritative max was accepted")
	# A malformed later actor must not invalidate, despawn, or silently turn
	# the earlier live target into a corpse in the same AOI batch.
	var negative_position := another.duplicate(true)
	negative_position.x = -1.0
	snapshot.monsters = {1001:revived,1002:negative_position}
	check(monsters.apply_snapshot(snapshot,1581000)==ERR_INVALID_DATA and
		monsters.count()==2 and monsters.pick_living_at(Vector2(90,20),1581000)==1002,
		"negative monster coordinate was admitted or partially changed the AOI")
	var alive_at_zero := another.duplicate(true)
	alive_at_zero.current_hp = 0
	alive_at_zero.state = 2
	snapshot.monsters = {1001:revived,1002:alive_at_zero}
	check(monsters.apply_snapshot(snapshot,1582000)==ERR_INVALID_DATA and
		monsters.pick_living_at(Vector2(90,20),1582000)==1002,
		"zero-HP monster with living state was admitted")
	var dead_with_hp := another.duplicate(true)
	dead_with_hp.current_hp = 50
	dead_with_hp.state = 4
	snapshot.monsters = {1001:revived,1002:dead_with_hp}
	check(monsters.apply_snapshot(snapshot,1583000)==ERR_INVALID_DATA and
		monsters.pick_living_at(Vector2(90,20),1583000)==1002,
		"positive-HP monster in dead state was admitted")
	var killed := revived.duplicate(true)
	killed.server_tick = 10
	killed.revision = 9
	killed.current_hp = 0
	killed.state = 4
	var invalid := another.duplicate(true)
	invalid.entity_id = 1003
	invalid.enemy_id = 2
	snapshot.monsters = {1001:killed,1003:invalid}
	check(monsters.apply_snapshot(snapshot,1600000)==ERR_INVALID_DATA,
		"malformed second AOI entity did not reject the whole frame")
	check(monsters.count()==2 and
		monsters.pick_living_at(Vector2(31,20),1600000)==1001 and
		monsters.pick_living_at(Vector2(90,20),1600000)==1002,
		"rejected AOI frame partially killed or despawned visible monsters")
	snapshot.map_epoch = 2
	check(monsters.apply_snapshot(snapshot,1650000)==ERR_INVALID_DATA and
		monsters.count()==2 and monsters.pick_living_at(Vector2(90,20),1650000)==1002,
		"rejected epoch transition cleared previous visible monsters")
	snapshot.map_epoch = 1
	snapshot.monsters = {1001:revived}
	check(monsters.apply_snapshot(snapshot,1700000)==OK and monsters.count()==1,
		"valid AOI despawn after rejection failed")
	actors=monsters.actors_at(1700000)
	var draw := DrawList.new()
	check(draw.configure({
		"scenery_schema":"sgtest.scenery/v1",
		"scenery_order":{"version":1,"groups":[[],[],[]]},
		"objects":[],"scenery_definitions":{}
	},assets,1)==OK,"draw configure")
	var commands := draw.build(Rect2(-100,-100,400,400),actors)
	check(draw.error==OK and commands.size()==1 and commands[0].actor_id=="monster:1001",
		"monster did not enter common actor/scenery draw order")

	# A delayed frame from the previous map generation must not replace the
	# newer AOI, even if all its individual monster definitions are valid.
	snapshot.map_epoch = 2
	snapshot.monsters = {1002:another}
	check(monsters.apply_snapshot(snapshot,1750000)==OK and
		monsters.count()==1 and monsters.pick_living_at(Vector2(90,20),1750000)==1002,
		"new generation did not replace monster AOI")
	snapshot.map_epoch = 1
	snapshot.monsters = {1001:revived}
	check(monsters.apply_snapshot(snapshot,1800000)==ERR_INVALID_DATA and
		monsters.count()==1 and monsters.pick_living_at(Vector2(90,20),1800000)==1002 and
		monsters.pick_living_at(Vector2(31,20),1800000)==0,
		"stale valid generation replaced the newer map monster AOI")
	snapshot.map_epoch = 2

	# A late same-generation WorldState snapshot must not despawn the newer
	# authoritative AOI or silently clear an existing click target.
	snapshot.last_aoi_tick = 200
	snapshot.monsters = {1002:another}
	check(monsters.apply_snapshot(snapshot,1850000)==OK and monsters.count()==1,
		"same-generation AOI high-water baseline")
	snapshot.last_aoi_tick = 199
	snapshot.monsters = {}
	check(monsters.apply_snapshot(snapshot,1900000)==OK and
		monsters.count()==1 and monsters.pick_living_at(Vector2(90,20),1900000)==1002,
		"stale same-generation AOI despawned a current selectable monster")
	snapshot.last_aoi_tick = 201
	check(monsters.apply_snapshot(snapshot,1950000)==OK and monsters.count()==0,
		"newer monster Despawn presentation retained actor")
	# A genuinely new map epoch starts a fresh tick domain, even if the new
	# AOI tick is numerically smaller than the preceding map generation's.
	snapshot.map_epoch = 3
	snapshot.last_aoi_tick = 1
	snapshot.monsters = {1002:another}
	check(monsters.apply_snapshot(snapshot,2000000)==OK and
		monsters.count()==1 and monsters.pick_living_at(Vector2(90,20),2000000)==1002,
		"new map epoch failed to reset the AOI tick high-water")
	snapshot.last_aoi_tick = 2
	snapshot.monsters = {}
	check(monsters.apply_snapshot(snapshot,2050000)==OK and monsters.count()==0,
		"new-generation AOI despawn was rejected after tick reset")
	var unknown := idle.duplicate(true)
	unknown.enemy_id=2
	snapshot.monsters={1001:unknown}
	check(monsters.apply_snapshot(snapshot,1000000)!=OK,
		"unknown visual definition did not fail closed")
	monsters.clear()
	check(monsters.actors_at(0).is_empty(),"monster clear retained presentation state")
	if failures.is_empty():
		print("MONSTER_PRESENTATION_PASS visual_population_only=true stats_ignored=true NS_idle=true NW_move=true FA_attack=true ND_death=true NH_hit_one_shot=true click_living_attack=true interpolation=true common_draw_order=true")
	quit(0 if failures.is_empty() else 1)
