extends SceneTree

const STORE = preload("res://scripts/runtime_asset_store.gd")
const DOLL = preload("res://scripts/paper_doll.gd")

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var store := STORE.new()
	store.configure(24 * 1024 * 1024, 8 * 1024 * 1024)
	var doll := DOLL.new()
	assert(doll.begin_async(store, "res://assets/generated", "player-core-test") == OK)
	var frames := 0
	while not doll.is_ready():
		store.poll_gpu(1, 1200)
		doll.refresh()
		assert(doll.first_error() == OK)
		frames += 1
		assert(frames < 300)
		await process_frame
	assert(doll.parts.size() == 6)
	assert(doll.death != null)
	assert(not doll.orders.is_empty())
	for part in doll.parts:
		for direction in range(1,9):
			for action in ["NS","NR","AA"]:
				var clip: String = ("B-"+str(doll.PARTS[part])+"_"+action+str(direction)).to_upper()
				assert(not doll.parts[part].sample(clip,0.1).is_empty())
	var resident := store.resident_bytes()
	assert(resident > 0)
	doll.reset(false)
	store.purge_unowned(true)
	await process_frame
	RenderingServer.force_sync()
	assert(store.resident_bytes() == 0)
	store.close()
	print("async paper doll: 7 atlases / six parts / draw-order / purge passed")
	quit()
