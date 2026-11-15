extends SceneTree

const STORE = preload("res://scripts/assets/runtime_asset_store.gd")

class PausedStore:
	extends "res://scripts/assets/runtime_asset_store.gd"
	func start() -> void:
		_running = true

func _initialize() -> void:
	_run.call_deferred()

func _page_refs(store: RefCounted, path: String) -> Array:
	var result: Array = []
	for texture in store._entries[path].atlas.pages:
		if texture != null:result.append(weakref(texture))
	return result

func _alive(refs: Array) -> int:
	var count := 0
	for ref in refs:
		if ref.get_ref() != null:count += 1
	return count

func _run() -> void:
	# A paused worker models a decoder busy in PNG decode/backpressure. Released
	# generations must not accumulate requests while that worker cannot run.
	var paused := PausedStore.new()
	for generation in 1000:
		var epoch := paused.begin_owner("map")
		assert(paused.acquire("user://cancel-probe.json","map",epoch) == OK)
		paused.release_owner("map")
	var queued := paused.statistics(false)
	paused.close()
	var folder := "user://perf003-%d" % OS.get_process_id()
	DirAccess.make_dir_recursive_absolute(folder)
	var image := Image.create(32,32,false,Image.FORMAT_RGBA8)
	image.fill(Color.WHITE)
	assert(image.save_png(folder.path_join("page.png")) == OK)
	image = null
	var path := folder.path_join("sprites.json")
	var file := FileAccess.open(path,FileAccess.WRITE)
	file.store_string(JSON.stringify({"version":9,"pages":["page.png","missing.png"],"frames":[],"clips":{},"aliases":{},"act":{}}))
	file.close()
	var results := {"queued_after_1000_cancels":queued.queued_decode_jobs,"entry_count":queued.entries}
	for mode in ["partial_cancel","decode_failure"]:
		var store := STORE.new()
		var epoch := store.begin_owner("map")
		assert(store.acquire(path,"map",epoch) == OK)
		var deadline := Time.get_ticks_msec()+5000
		while store.resident_bytes() == 0:
			assert(Time.get_ticks_msec()<deadline,"first page did not upload")
			store.poll_gpu(1,100000)
			await process_frame
		var partial := store.resident_bytes()
		var refs := _page_refs(store,path)
		if mode == "decode_failure":
			while not store.is_failed(path):
				assert(Time.get_ticks_msec()<deadline,"second page did not fail")
				store.poll_gpu(1,100000)
				await process_frame
		var before_release := store.resident_bytes()
		store.release_owner("map")
		store.purge_unowned(true)
		await process_frame
		await process_frame
		results[mode] = {"bytes_after_first_page":partial,"resident_before_release":before_release,
			"resident_after_release":store.resident_bytes(),"texture_handles_alive":_alive(refs),"statistics":store.statistics(false)}
		# Repair the same path and prove that a new generation does real work;
		# stale failure/partial state must not poison its resource or accounting.
		file = FileAccess.open(path,FileAccess.WRITE)
		file.store_string(JSON.stringify({"version":9,"pages":["page.png"],"frames":[],"clips":{},"aliases":{},"act":{}}))
		file.close()
		epoch = store.begin_owner("retry")
		assert(store.acquire(path,"retry",epoch) == OK)
		deadline = Time.get_ticks_msec()+5000
		while not store.is_ready(path):
			assert(Time.get_ticks_msec()<deadline and not store.is_failed(path))
			store.poll_gpu(1,100000)
			await process_frame
		assert(store.resident_bytes() == 4096)
		store.release_owner("retry")
		store.purge_unowned(true)
		assert(store.resident_bytes() == 0)
		results[mode]["same_path_retry_passed"] = true
		store.close()
		file = FileAccess.open(path,FileAccess.WRITE)
		file.store_string(JSON.stringify({"version":9,"pages":["page.png","missing.png"],"frames":[],"clips":{},"aliases":{},"act":{}}))
		file.close()
	for name in ["sprites.json","page.png"]:DirAccess.remove_absolute(folder.path_join(name))
	DirAccess.remove_absolute(folder)
	results["store_sha256"] = FileAccess.get_sha256("res://scripts/assets/runtime_asset_store.gd")
	var report_path := ""
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--report="):report_path = argument.trim_prefix("--report=")
	if not report_path.is_empty():FileAccess.open(report_path,FileAccess.WRITE).store_string(JSON.stringify(results,"  "))
	print("cancel_probe "+JSON.stringify(results))
	if "--assert-bounded" in OS.get_cmdline_user_args():
		assert(int(results.queued_after_1000_cancels) == 0)
		assert(int(results.partial_cancel.resident_after_release) == 0)
		assert(int(results.decode_failure.resident_after_release) == 0)
		assert(int(results.decode_failure.resident_before_release) == 0)
		assert(int(results.partial_cancel.texture_handles_alive) == 0 and int(results.decode_failure.texture_handles_alive) == 0)
	quit()
