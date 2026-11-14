extends SceneTree

func _initialize() -> void:
	var facing_views: Dictionary = {}
	for facing in range(1,9):
		facing_views[str(facing)] = {"clip":"TEST_NS1","flip_h":facing >= 5,"draw_order_key":null,
			"draw_layers":null,"draw_slots":null}
	facing_views["1"] = {"clip":"TEST_NS1","flip_h":false,"draw_order_key":"NS1",
		"draw_layers":["Body+2","Head"],"draw_slots":[10,9,0]}
	var source := {"schema":"sgtest.animation","version":2,
		"frames":[{"id":"fixture#0","pivot":[3,7]},{"id":"fixture#1","pivot":[5,9]}],
		"clips":{"TEST_NS1":{"frames":[0,1]}},
		"behaviors":{"NS":{"loop":true,"timeline":[{"default_ticks":8,"frame_index":0},
			{"default_ticks":8,"frame_index":1}],"facings":facing_views,"events":[]}}}
	var definition := preload("res://scripts/animation_definition.gd").new()
	assert(definition.configure(source) == OK)
	assert(definition.sample("NS",1,0.159).frame.id == "fixture#0")
	assert(definition.sample("NS",1,0.160).frame.id == "fixture#1")
	assert(definition.sample("NS",1,0.320).frame.id == "fixture#0")
	assert(definition.sample("NS",1,100.0,0.32,0).frame.id == "fixture#1", "one-shot holds final source frame")
	assert(definition.sample("NS",8,0.1).flip_h)
	assert(not definition.sample("NS",1,0.1).flip_h)
	assert(definition.sample("NS",1,0.1).frame.pivot == [3,7])
	assert(definition.sample("NS",1,0.1).draw_order_key == "NS1")
	assert(definition.sample("NS",1,0.1).draw_layers == ["Body+2","Head"])
	assert(definition.sample("NS",1,0.1).draw_slots == [10,9,0])
	var legacy := source.duplicate(true)
	legacy.version = 1
	assert(definition.configure(legacy) == ERR_FILE_CORRUPT, "v1 must not silently accept the v2 z-order contract")
	assert(definition.configure(source) == OK)
	source.frames[0].pivot[0] = 99
	assert(definition.sample("NS",1,0.1).frame.pivot == [3,7], "shared source is copied once, not mutable by caller")
	for fps in [30,45,60,75,90]:
		for second in range(1,6):
			# Reaching the same action timestamp after different render schedules
			# produces exactly the same frame; no accumulated render delta state.
			for frame in range(second*fps):
				definition.sample("NS",1,float(frame)/fps)
			assert(definition.sample("NS",1,float(second)) == definition.sample("NS",1,float(second)))
	var shared: Dictionary = definition.sample("NS",2,0.1)
	for actor in 1000:
		assert(is_same(definition.sample("NS",3,0.1),shared), "same source view descriptors are shared only when draw metadata also matches")
	source.clips.TEST_NS1.frames = [-1]
	assert(definition.configure(source) == ERR_FILE_CORRUPT)
	assert(definition.sample("NS",1,0).is_empty())
	print("normalized animation timing / facings / one-shot / sharing checks passed")
	quit()
