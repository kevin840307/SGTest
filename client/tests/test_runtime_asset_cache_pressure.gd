extends SceneTree

const STORE = preload("res://scripts/assets/runtime_asset_store.gd")
var _files: Array[String] = []
var _folder := ""

func _initialize() -> void:
	_run.call_deferred()

func _manifest(name: String) -> String:
	var path := _folder.path_join(name+".json")
	var file := FileAccess.open(path,FileAccess.WRITE)
	file.store_string(JSON.stringify({"version":9,"pages":["page.png"],"frames":[],"clips":{},"aliases":{},"act":{}}))
	file.close()
	_files.append(path)
	return path

func _ready(store: RefCounted, path: String) -> void:
	var deadline := Time.get_ticks_msec()+5000
	while not store.is_ready(path):
		assert(Time.get_ticks_msec()<deadline and not store.is_failed(path),"cache fixture did not become ready")
		store.poll_gpu(1,1200)
		await process_frame

func _report(result: Dictionary) -> void:
	result["store_sha256"] = FileAccess.get_sha256("res://scripts/assets/runtime_asset_store.gd")
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--report="):
			FileAccess.open(argument.trim_prefix("--report="),FileAccess.WRITE).store_string(JSON.stringify(result,"  "))
	print("cache_pressure "+JSON.stringify(result))

func _cleanup() -> void:
	for path in _files:DirAccess.remove_absolute(path)
	DirAccess.remove_absolute(_folder)

func _run() -> void:
	_folder = "user://cache-pressure-%d" % OS.get_process_id()
	DirAccess.make_dir_recursive_absolute(_folder)
	var image := Image.create(32,32,false,Image.FORMAT_RGBA8)
	image.fill(Color.WHITE)
	var png := _folder.path_join("page.png")
	assert(image.save_png(png) == OK)
	image = null
	_files.append(png)
	var store := STORE.new()
	store.configure(16*1024*1024,1024*1024)
	var pinned_epoch := store.begin_owner("pinned")
	var cache_epoch := store.begin_owner("cache")
	var paths: Array[String] = []
	for index in store.MAX_JOBS:
		var path := _manifest(str(index))
		paths.append(path)
		var owner := "pinned" if index == 0 else "cache"
		assert(store.acquire(path,owner,pinned_epoch if index == 0 else cache_epoch) == OK)
		await _ready(store,path)
		if index > 0:store.release(path,owner,cache_epoch)
	assert(int(store.statistics(false).entries) == store.MAX_JOBS)
	assert(int(store.statistics(false).evictions) == 0,"below both budgets must keep useful cache")
	assert(store.resident_bytes() < store.cache_budget_bytes)
	# Touch the oldest unowned entry, so entry #2 becomes the LRU victim.
	assert(store.get_atlas(paths[1]) != null)
	var before := store.statistics(false)
	var newcomer := _manifest("new")
	var epoch := store.begin_owner("new")
	var error := store.acquire(newcomer,"new",epoch)
	var result := {"before":before,"new_acquire_error":error,
		"after_acquire":store.statistics(false),"pinned_kept":store.is_ready(paths[0]),
		"recent_kept":store.is_ready(paths[1]),"oldest_removed":store.error(paths[2]) == ERR_DOES_NOT_EXIST}
	if error != OK:
		_report(result)
		store.close()
		_cleanup()
		quit(1)
		return
	await _ready(store,newcomer)
	assert(result.pinned_kept and result.recent_kept and result.oldest_removed)
	assert(int(result.after_acquire.evictions) == int(before.evictions)+1,"make one slot, not a full cache purge")
	assert(int(result.after_acquire.entries) == store.MAX_JOBS)
	assert(int(result.after_acquire.resident_bytes) == int(before.resident_bytes)-4096)
	# Pin every remaining entry: count pressure must not evict live resources.
	var all_epoch := store.begin_owner("all")
	for path in paths:
		if path == paths[2]:continue
		assert(store.acquire(path,"all",all_epoch) == OK)
	assert(store.acquire(newcomer,"all",all_epoch) == OK)
	var blocked := _manifest("blocked")
	assert(store.acquire(blocked,"all",all_epoch) == ERR_BUSY)
	store.release(paths[3],"all",all_epoch)
	assert(store.acquire(blocked,"all",all_epoch) == OK,"releasing one entry must restore admission")
	await _ready(store,blocked)
	assert(store.error(paths[3]) == ERR_DOES_NOT_EXIST)
	assert(store.is_ready(paths[0]) and store.is_ready(paths[1]))
	store.close()
	result["all_owned_rejected_then_released_slot_recovered"] = true
	result["final_resident_bytes"] = store.resident_bytes()
	assert(store.resident_bytes() == 0)
	_report(result)
	_cleanup()
	print("cache pressure LRU / pinned owners / recent access / single eviction / all-owned backpressure / recovery passed")
	quit()
