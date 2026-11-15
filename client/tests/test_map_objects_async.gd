extends SceneTree

const STORE = preload("res://scripts/assets/runtime_asset_store.gd")
const MAP_OBJECTS = preload("res://scripts/rendering/map_objects.gd")

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var world: Dictionary = JSON.parse_string(
		FileAccess.get_file_as_string("res://assets/maps/13/world.json")
	)
	assert(world.objects.size() > 0)
	assert(not world.has("sprite_bundle"))
	var unique: Dictionary = {}
	for object in world.objects:
		var relative := str(object.get("atlas",""))
		assert(relative.begins_with("maps/visuals/") and relative.ends_with("/atlas.json"))
		assert(not relative.begins_with("monsters/"))
		unique[relative] = true
	assert(unique.size() > 1,"Map13 scenery must keep independent source assets")

	var store := STORE.new()
	store.configure(32 * 1024 * 1024,16 * 1024 * 1024)
	var objects := MAP_OBJECTS.new()
	objects.configure("res://assets",world,store,"map-object-test")
	var first: Dictionary = world.objects[0]
	var view := Rect2(first.bounds[0],first.bounds[1],first.bounds[2],first.bounds[3]).grow(32)
	objects.prepare(view)
	var frames := 0
	while not objects.is_ready():
		store.poll_gpu(2,1600)
		objects.prepare(view)
		frames += 1
		assert(frames < 240)
		await process_frame
	assert(objects.visible.size() > 0)
	assert(objects.requested.size() > 0)
	assert(objects.atlases.size() == objects.requested.size())
	for path in objects.atlases:
		assert(str(path).contains("/maps/visuals/"))
		assert(not str(path).contains("/bundles/"))
	# Multiple instances of one scenery asset still share a single AssetStore entry.
	assert(int(store.statistics().entries) == objects.requested.size())
	var resident := store.resident_bytes()
	assert(resident > 0)
	objects.reset(false)
	store.purge_unowned(true)
	await process_frame
	RenderingServer.force_sync()
	assert(store.resident_bytes() == 0)
	store.close()
	print("async map object: independent scenery assets / shared store / release passed")
	quit()
