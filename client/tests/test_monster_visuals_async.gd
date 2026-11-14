extends SceneTree

const STORE = preload("res://scripts/runtime_asset_store.gd")
const VISUALS = preload("res://scripts/monster_visuals.gd")

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var store := STORE.new()
	var visuals := VISUALS.new()
	assert(visuals.begin_async(store, "map13-test") == OK)
	var population: Dictionary = JSON.parse_string(
		FileAccess.get_file_as_string("res://assets/maps/13/population.json")
	)
	var counts: Dictionary = {}
	for spawn in population.spawns:
		var id := int(spawn.definition)
		counts[id] = int(counts.get(id, 0)) + 1
	for definition in population.definitions:
		assert(visuals.add_async("res://assets", definition,
			int(counts.get(int(definition.id), 0))) == OK)
	var frames := 0
	while not visuals.is_ready():
		visuals.poll_gpu(1, 1200)
		assert(visuals.first_error() == OK)
		frames += 1
		assert(frames < 300)
		await process_frame
	assert(visuals.definitions.size() == 9)
	assert(visuals.loaded_count() == 1,"nine map definitions must share one lifecycle bundle")
	assert(int(store.statistics().entries) == 1)
	for row in population.definitions:
		var visual: Dictionary = visuals.resolve(int(row.id))
		assert(visual.atlas != null)
		for action in ["NS","NW","FA","NH","ND"]:
			for direction in range(1,9):
				assert(not visual.atlas.sample("%s_%s%d" % [visual.prefix,action,direction], 0.1).is_empty())
	var resident := store.resident_bytes()
	assert(resident > 0)

	# New map generation must drop map-local definitions/strong refs. Purge the
	# now-unowned warm cache, queue work, then immediately advance generation to
	# prove stale worker results cannot resurrect the previous palette.
	assert(visuals.begin_async(store, "map13-test") == OK)
	assert(visuals.definitions.is_empty() and visuals.shared.is_empty())
	store.purge_unowned(true)
	for definition in population.definitions:
		assert(visuals.add_async("res://assets", definition,
			int(counts.get(int(definition.id), 0))) == OK)
	assert(visuals.begin_async(store, "map13-test") == OK)
	assert(visuals.definitions.is_empty() and visuals.shared.is_empty())
	var current: Dictionary = population.definitions[0]
	assert(visuals.add_async("res://assets", current, 1) == OK)
	frames = 0
	while not visuals.is_ready():
		visuals.poll_gpu(1, 1200)
		assert(visuals.first_error() == OK)
		frames += 1
		assert(frames < 300)
		await process_frame
	assert(visuals.loaded_count() == 1)
	assert(visuals.resolve(int(current.id)).atlas != null)
	assert(int(store.statistics().cancelled_jobs) >= 1)

	visuals.reset(true)
	await process_frame
	RenderingServer.force_sync()
	assert(store.resident_bytes() == 0)
	store.close()
	print("async map palette: 9 visuals / 360 logical views / generation cancel / owner purge passed")
	quit()
