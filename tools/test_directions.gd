extends SceneTree

var failures := 0

func check(condition: bool, message: String) -> void:
	if not condition:
		failures += 1
		push_error(message)

func _initialize() -> void:
	var world := preload("res://scripts/server_world.gd").new()
	var directions := [6,7,8,1,2,3,4,5] # Recovered GameConstants.DIR_STRINGS
	for i in 8:
		check(world.facing(Vector2.from_angle(i*PI/4)) == directions[i], "eight-way world heading %d" % i)
	world.populate(1,0)
	world.join(1)
	world.monsters[1].p = world.players[1].p+Vector2(100,0)
	world.monsters[1].facing = 2
	check(world.attack(1,1), "accepted attack before pose-lock test")
	world.move(1,0,1)
	var monster_at: Vector2 = world.monsters[1].p
	world.step(0.2)
	check(world.players[1].facing == 6, "movement cannot rotate active attack")
	check(world.monsters[1].p == monster_at and world.monsters[1].facing == 2, "AI cannot move or rotate active hit")
	world.step(0.16)
	check(world.players[1].facing == 8, "movement heading resumes after attack")
	var policy = preload("res://scripts/action_policy.gd")
	var codec = preload("res://scripts/snapshot_codec.gd")
	var original := 1.36
	var row: Array = codec.decode(codec.encode([[1,0,0,30,6,2,original]]))[0]
	check(policy.wire_time(original) == row[6], "reliable and snapshot action clocks compare exactly")
	check(not policy.accepts_event(2, original, original, 2.0), "delayed attack cannot overwrite completed movement")
	check(not policy.accepts_event(2, 1.0, 1.36, 1.4), "old action rejected")
	var atlas := preload("res://scripts/entity_atlas.gd").new()
	atlas.data = JSON.parse_string(FileAccess.get_file_as_string("res://assets/generated/enemy/en004a.json"))
	for direction in range(1,9):
		for action in ["NS","NW","FA","NH","ND"]:
			var result: Dictionary = atlas.sample("EN004A_%s%d" % [action,direction], 0.0)
			check(not result.is_empty(), "monster %s direction %d resolves" % [action,direction])
	check(atlas.sample("EN004A_NS4",0).clip == "EN004A_NS3" and atlas.sample("EN004A_NS4",0).flip_h,
		"idle fourth heading uses mirrored third source")
	check(atlas.sample("EN004A_NW6",0).clip == "EN004A_NW2" and atlas.sample("EN004A_NW6",0).flip_h,
		"walking sixth heading uses mirrored second source")
	atlas.data = {"clips":{"TEST_ND1":[0,1],"TEST_NS1":[0,1]}, "aliases":{}, "act":{}, "frames":[{"id":0},{"id":1}]}
	check(atlas.sample("TEST_ND1", 1.0).frame.id == 1, "untimed death holds final frame")
	check(atlas.sample("TEST_NS1", 1.0).frame.id == 0, "untimed idle loops")
	atlas.data.act = {"D":{"timeline":[[0,5],[1,5]],"loop":false}}
	check(atlas.sample("TEST_ND1", 0.24, 8, 0.35).frame.id == 1, "ACT sequence fits authoritative action interval")
	check(atlas.sample("TEST_ND1",0.174,8,0.35).frame.id == 0 and atlas.sample("TEST_ND1",0.176,8,0.35).frame.id == 1,
		"sample cache preserves exact transition times without frame-clock quantization")
	var entries: int = atlas.sample_cache.size()
	for i in 1000:
		atlas.sample("TEST_ND1",i*0.01,8,0.35)
	check(atlas.sample_cache.size() == entries, "sample cache grows by clip, not elapsed time or monster count")
	var doll := preload("res://scripts/paper_doll.gd").new()
	check(doll.open("res://assets/generated") == OK, "original player parts and layer table load")
	for part in doll.parts:
		for direction in range(1,9):
			for action in ["NS","NR","AA"]:
				var clip: String = ("B-"+str(doll.PARTS[part])+"_"+action+str(direction)).to_upper()
				check(not doll.parts[part].sample(clip,0.1).is_empty(), "player %s %s resolves" % [part,clip])
	check(doll.orders.NS1.find("r-arm") < doll.orders.NS1.find("body"), "front-arm original layer order")
	check(doll.orders.NS8.find("l-arm") < doll.orders.NS8.find("body"), "rear-arm original layer order")
	print("direction/action checks: %d failures" % failures)
	quit(1 if failures else 0)
