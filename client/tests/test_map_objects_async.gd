extends SceneTree

const STORE = preload("res://scripts/runtime_asset_store.gd")
const MAP_OBJECTS = preload("res://scripts/map_objects.gd")

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var world: Dictionary = JSON.parse_string(
		FileAccess.get_file_as_string("res://assets/generated/maps/13/world.json")
	)
	assert(world.objects.size() > 0)
	var store := STORE.new()
	store.configure(16 * 1024 * 1024, 8 * 1024 * 1024)
	var objects := MAP_OBJECTS.new()
	objects.configure("res://assets/generated", world, store, "map-object-test")
	var first: Dictionary = world.objects[0]
	var view := Rect2(first.bounds[0], first.bounds[1], first.bounds[2], first.bounds[3]).grow(32)
	objects.prepare(view)
	var frames := 0
	while not objects.cache.has(first.atlas):
		store.poll_gpu(1, 1200)
		objects.prepare(view)
		frames += 1
		assert(frames < 180)
		await process_frame
	assert(objects.visible.size() > 0)
	assert(objects.cache[first.atlas].atlas != null)
	var resident := store.resident_bytes()
	assert(resident > 0)
	# Leave the map-object viewport: the local strong reference is released,
	# while the store is allowed to retain it as bounded unowned LRU.
	objects.prepare(Rect2(-100000,-100000,10,10))
	assert(objects.cache.is_empty())
	assert(store.resident_bytes() == resident)
	store.purge_unowned(true)
	await process_frame
	RenderingServer.force_sync()
	assert(store.resident_bytes() == 0)
	objects.reset(false)
	store.close()
	print("async map object: visible acquire / local release / LRU purge passed")
	quit()
