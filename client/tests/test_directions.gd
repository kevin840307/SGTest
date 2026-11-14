extends SceneTree

var failures := 0

func check(condition: bool, message: String) -> void:
	if not condition:
		failures += 1
		push_error(message)

func _initialize() -> void:
	var policy = preload("res://scripts/action_policy.gd")
	var codec = preload("res://scripts/snapshot_codec.gd")
	var original := 1.36
	var row: Array = codec.decode(codec.encode([[1,0,0,30,6,2,original]]))[0]
	check(policy.wire_time(original) == row[6], "reliable and snapshot action clocks compare exactly")
	check(not policy.accepts_event(2, original, original, 2.0), "delayed attack cannot overwrite completed movement")
	check(not policy.accepts_event(2, 1.0, 1.36, 1.4), "old action rejected")
	check(int(ProjectSettings.get_setting("application/run/max_fps",-1)) == 60, "default render cap is 60 FPS")
	var facing = preload("res://scripts/legacy_facing.gd")
	check(facing.from_vector(Vector2.RIGHT) == 6, "original right facing is 6")
	check(facing.from_vector(Vector2.DOWN) == 8, "original down facing is 8")
	check(facing.from_vector(Vector2.LEFT) == 2, "original left facing is 2")
	check(facing.from_vector(Vector2.UP) == 4, "original up facing is 4")
	check(facing.from_vector(Vector2(5,10),8,28.0) == 8, "original-style hysteresis holds near sector boundary")
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
	var map13 := preload("res://scripts/entity_atlas.gd").new()
	map13.data = JSON.parse_string(FileAccess.get_file_as_string("res://assets/generated/enemy/en010a.json"))
	check(map13.data.get("normalized_schema_version",0) == 2,"Map13 monster has normalized semantic overlay")
	check(map13.sample("EN010A_NS2",0).clip == "EN010A_NS1" and not map13.sample("EN010A_NS2",0).flip_h,
		"physical EN010A_NS2 cannot override original logical facing mapping")
	# Normalized logical mapping must override a same-named physical source.
	atlas.compiled.clear()
	atlas.sample_cache.clear()
	atlas.data = {"clips":{"E_NS1":[0],"E_NS2":[1]},"aliases":{},"act":{},
		"logical_views":{"E_NS2":{"clip":"E_NS1","flip_h":false,"timeline_ticks":[[0,8]],"loop":true}},
		"frames":[{"id":0},{"id":1}]}
	check(atlas.sample("E_NS2",0.1).clip == "E_NS1", "normalized logical view overrides physical direct clip")
	atlas.compiled.clear()
	atlas.sample_cache.clear()
	atlas.data = {"clips":{"TEST_ND1":[0,1],"TEST_NS1":[0,1]}, "aliases":{}, "act":{}, "frames":[{"id":0},{"id":1}]}
	check(atlas.sample("TEST_ND1", 1.0).frame.id == 1, "untimed death holds final frame")
	check(atlas.sample("TEST_NS1", 1.0).frame.id == 0, "untimed idle loops")
	atlas.compiled.clear()
	atlas.sample_cache.clear()
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
	var player_as4: Dictionary = doll.parts.body.sample("B-BODY_AS4",0.1)
	check(player_as4.clip == "B-BODY_AS3" and player_as4.flip_h,
		"physical B-BODY_AS4 cannot override original character DirectionMapping")
	check(doll.orders.NS1.find("r-arm") < doll.orders.NS1.find("body"), "front-arm original layer order")
	check(doll.orders.NS8.find("l-arm") < doll.orders.NS8.find("body"), "rear-arm original layer order")
	print("direction/action checks: %d failures" % failures)
	quit(1 if failures else 0)
