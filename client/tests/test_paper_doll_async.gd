extends SceneTree

const STORE = preload("res://scripts/runtime_asset_store.gd")
const DOLL = preload("res://scripts/paper_doll.gd")

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var store := STORE.new()
	store.configure(24*1024*1024,8*1024*1024)
	var doll := DOLL.new()
	assert(doll.begin_async(store,"res://assets/generated","player-base-test") == OK)
	var frames := 0
	while not doll.is_ready():
		store.poll_gpu(1,1200)
		doll.refresh()
		assert(doll.first_error() == OK)
		frames += 1
		assert(frames < 300)
		await process_frame
	assert(doll.parts.size() == 6)
	assert(doll.death != null)
	assert(doll.parts.body == doll.parts.leg)
	assert(doll.parts.body == doll.parts.head)
	assert(doll.parts.body == doll.death)
	assert(store.statistics().entries == 1,"base avatar must own one runtime manifest")
	for part in doll.parts:
		for direction in range(1,9):
			for action in ["NS","NR","AA"]:
				var clip: String = ("B-"+str(doll.PARTS[part])+"_"+action+str(direction)).to_upper()
				assert(not doll.parts[part].sample(clip,0.1).is_empty())
	var body: Dictionary = doll.parts.body.sample("B-BODY_NS1",0.1)
	assert(body.get("draw_layers") is Array and not body.draw_layers.is_empty())
	var resident := store.resident_bytes()
	assert(resident > 0)
	doll.reset(false)
	store.purge_unowned(true)
	await process_frame
	RenderingServer.force_sync()
	assert(store.resident_bytes() == 0)
	store.close()
	print("async paper doll: one session bundle / six parts + death / normalized draw-order / purge passed")
	quit()
