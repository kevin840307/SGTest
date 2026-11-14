extends SceneTree

const STORE = preload("res://scripts/runtime_asset_store.gd")
const MAP_OBJECTS = preload("res://scripts/map_objects.gd")

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var world: Dictionary = JSON.parse_string(
		FileAccess.get_file_as_string("res://assets/maps/13/world.json")
	)
	assert(world.objects.size() > 0)
	assert(not str(world.get("sprite_bundle","")).is_empty())
	var store := STORE.new()
	store.configure(32 * 1024 * 1024, 16 * 1024 * 1024)
	var objects := MAP_OBJECTS.new()
	objects.configure("res://assets", world, store, "map-object-test")
	var first: Dictionary = world.objects[0]
	var view := Rect2(first.bounds[0], first.bounds[1], first.bounds[2], first.bounds[3]).grow(32)
	objects.prepare(view)
	var frames := 0
	while not objects.is_ready():
		store.poll_gpu(1,1200)
		objects.prepare(view)
		frames += 1
		assert(frames < 180)
		await process_frame
	assert(objects.visible.size() > 0)
	assert(objects.atlas != null)
	assert(int(store.statistics().entries) == 1,
		"all map scenery must share one map lifecycle bundle")
	var resident := store.resident_bytes()
	assert(resident > 0)
	# Leaving the viewport changes only the draw set. Texture lifetime is the map,
	# so we avoid 20-30 per-view acquire/release operations and keep one owner.
	objects.prepare(Rect2(-100000,-100000,10,10))
	assert(objects.visible.is_empty())
	assert(store.resident_bytes() == resident)
	objects.reset(false)
	store.purge_unowned(true)
	await process_frame
	RenderingServer.force_sync()
	assert(store.resident_bytes() == 0)
	store.close()
	print("async map object: one map bundle / viewport draw cull / map release passed")
	quit()
