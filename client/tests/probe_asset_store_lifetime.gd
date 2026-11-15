extends SceneTree

# PERF002 diagnostic: weak sentinels reveal retained CPU Images/JSON metadata.
# No gameplay or asset-file mutation; the extra object exists only in memory.
class ProbeStore:
	extends "res://scripts/assets/runtime_asset_store.gd"
	var images: Array = []
	var metadata: Array = []
	func _publish(item: Dictionary, bytes: int = 0) -> bool:
		_mutex.lock()
		if item.kind == "image":
			images.append(weakref(item.image))
		elif item.kind == "metadata":
			var sentinel := RefCounted.new()
			item.data["_lifetime_probe"] = sentinel
			metadata.append(weakref(sentinel))
		_mutex.unlock()
		return super._publish(item,bytes)
	func alive_counts() -> Dictionary:
		_mutex.lock()
		var result := {"images":_alive(images),"metadata":_alive(metadata)}
		_mutex.unlock()
		return result
	func _alive(values: Array) -> int:
		var count := 0
		for value in values:
			if value.get_ref() != null:count += 1
		return count

func _initialize() -> void:
	_run.call_deferred()

func memory() -> Dictionary:
	return {"static_bytes":OS.get_static_memory_usage(),
		"texture_bytes":RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TEXTURE_MEM_USED)}

func _check_metric_bounds() -> void:
	var store := preload("res://scripts/assets/runtime_asset_store.gd").new()
	for poll in 10000:store.poll_gpu(1,1200)
	assert(store.statistics().gpu_commit_frame_ms.is_empty(),"idle polling must not allocate telemetry samples")
	var limit: int = store.MAX_METRIC_SAMPLES
	for key in ["decode_ms","upload_submit_ms","gpu_commit_frame_ms"]:
		for value in limit+100:store._metric_append(key,float(value))
		var stats := store.statistics()
		assert(stats[key].size() == limit)
		assert(stats[key][0] == 100.0 and stats[key][-1] == float(limit+99),"recent ring preserves sample order")
		assert(int(stats.metric_sample_totals[key]) == limit+100)
		assert(not store.statistics(false).has(key),"summary metrics omit raw arrays")
	store.close()

func _run() -> void:
	_check_metric_bounds()
	await process_frame
	await process_frame
	var baseline := memory()
	# First identify backend-owned Image references independently of our store.
	var image := Image.create(32,32,false,Image.FORMAT_RGBA8)
	var weak: WeakRef = weakref(image)
	var texture := ImageTexture.create_from_image(image)
	image = null
	await process_frame
	await process_frame
	var control_alive_with_texture: bool = weak.get_ref() != null
	texture = null
	await process_frame
	await process_frame
	var control_alive_after_release: bool = weak.get_ref() != null
	var store := ProbeStore.new()
	var epoch := store.begin_owner("probe")
	var paths := ["res://assets/monsters/en010a/atlas.json","res://assets/players/b/base/body/atlas.json"]
	for path in paths:assert(store.acquire(path,"probe",epoch) == OK)
	var deadline := Time.get_ticks_msec()+15000
	while not paths.all(func(path: String) -> bool: return store.is_ready(path)):
		assert(Time.get_ticks_msec()<deadline,"store did not become ready")
		store.poll_gpu(1,1200)
		await process_frame
	await create_timer(0.1).timeout
	var loaded := {"alive":store.alive_counts(),"memory":memory(),"statistics":store.statistics()}
	store.release_owner("probe")
	store.purge_unowned(true)
	await process_frame
	await process_frame
	await create_timer(0.1).timeout
	var released := {"alive":store.alive_counts(),"memory":memory(),"statistics":store.statistics()}
	store.close()
	await process_frame
	await process_frame
	var closed := {"alive":store.alive_counts(),"memory":memory()}
	var report := {"renderer":RenderingServer.get_video_adapter_name(),"baseline":baseline,
		"control_image_alive_with_texture":control_alive_with_texture,
		"control_image_alive_after_release":control_alive_after_release,
		"loaded":loaded,"released":released,"closed":closed,
		"store_sha256":FileAccess.get_sha256("res://scripts/assets/runtime_asset_store.gd")}
	var target := ProjectSettings.globalize_path("res://../reports/perf002_lifetime.json")
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--report="):target = arg.trim_prefix("--report=")
	FileAccess.open(target,FileAccess.WRITE).store_string(JSON.stringify(report,"  "))
	print("asset_lifetime_probe "+JSON.stringify({"control":control_alive_with_texture,
		"loaded":loaded.alive,"released":released.alive,"closed":closed.alive}))
	if "--assert-released" in OS.get_cmdline_user_args():
		assert(int(loaded.alive.images) == 0,"CPU Images retained after GPU commit")
		assert(int(released.alive.images) == 0 and int(released.alive.metadata) == 0,
			"worker retained released images or metadata while idle")
	quit()
