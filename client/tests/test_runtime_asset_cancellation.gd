extends SceneTree

const STORE = preload("res://scripts/assets/runtime_asset_store.gd")

class PausedStore:
	extends "res://scripts/assets/runtime_asset_store.gd"
	var order: Array = []
	func start() -> void:
		_running = true
	func resume() -> void:
		super.start()
	func _decode_job(job: Dictionary) -> void:
		_mutex.lock()
		order.append(str(job.path))
		_mutex.unlock()
		super._decode_job(job)
	func decode_order() -> Array:
		_mutex.lock()
		var result := order.duplicate()
		_mutex.unlock()
		return result

func _initialize() -> void:
	_run.call_deferred()

func _write(path: String, pages: Array) -> void:
	var output := FileAccess.open(path,FileAccess.WRITE)
	output.store_string(JSON.stringify({"version":9,"pages":pages,"frames":[],"clips":{},"aliases":{},"act":{}}))
	output.close()

func _ready(store: RefCounted, path: String) -> void:
	var deadline := Time.get_ticks_msec()+5000
	while not store.is_ready(path):
		assert(Time.get_ticks_msec()<deadline and not store.is_failed(path),"asset did not load")
		store.poll_gpu(1,1200)
		await process_frame

func _run() -> void:
	var folder := "user://asset-cancel-%d" % OS.get_process_id()
	DirAccess.make_dir_recursive_absolute(folder)
	var image := Image.create(32,32,false,Image.FORMAT_RGBA8)
	image.fill(Color.WHITE)
	assert(image.save_png(folder.path_join("page.png")) == OK)
	image = null
	for name in ["low","high","middle"]:_write(folder.path_join(name+".json"),[])
	var path := folder.path_join("pages.json")
	_write(path,["page.png","page.png","page.png"])
	var paused := PausedStore.new()
	var epoch := paused.begin_owner("bulk")
	for index in paused.MAX_JOBS:
		assert(paused.acquire(folder.path_join("absent%d.json" % index),"bulk",epoch) == OK)
	assert(paused.acquire("user://overflow.json","bulk",epoch) == ERR_BUSY)
	paused.release_owner("bulk")
	assert(int(paused.statistics(false).queued_decode_jobs) == 0)
	assert(int(paused.statistics(false).active_decode_jobs) == 0)
	for generation in 1000:
		epoch = paused.begin_owner("map")
		assert(paused.acquire(path,"map",epoch) == OK)
		paused.release_owner("map")
	assert(int(paused.statistics(false).queued_decode_jobs) == 0)
	# Already-invalid results cannot re-enter the ready queue, even if the
	# publisher has just finished a slow PNG decode after owner cancellation.
	assert(not paused._publish({"path":path,"token":-1,"kind":"metadata","data":{}}))
	epoch = paused.begin_owner("priority")
	for row in [["low",0],["high",20],["middle",5]]:
		assert(paused.acquire(folder.path_join(str(row[0])+".json"),"priority",epoch,int(row[1])) == OK)
	paused.resume()
	for name in ["high","middle","low"]:await _ready(paused,folder.path_join(name+".json"))
	var order := paused.decode_order()
	assert(order == [folder.path_join("high.json"),folder.path_join("middle.json"),folder.path_join("low.json")])
	paused.release_owner("priority")
	paused.close()
	# Missing-page jobs each produce metadata+error with zero accounted image
	# bytes. The item cap must still stop the producer at MAX_READY_ITEMS.
	var metadata_store := STORE.new()
	epoch = metadata_store.begin_owner("zero-byte")
	for index in metadata_store.MAX_JOBS:
		var missing_path := folder.path_join("missing%d.json" % index)
		_write(missing_path,["absent.png"])
		assert(metadata_store.acquire(missing_path,"zero-byte",epoch) == OK)
	var item_deadline := Time.get_ticks_msec()+5000
	while int(metadata_store.statistics(false).ready_queue_items) < metadata_store.MAX_READY_ITEMS:
		assert(Time.get_ticks_msec()<item_deadline)
		await process_frame
	await create_timer(0.02).timeout
	assert(int(metadata_store.statistics(false).ready_queue_items) == metadata_store.MAX_READY_ITEMS)
	assert(int(metadata_store.statistics(false).ready_queue_bytes) == 0)
	metadata_store.release_owner("zero-byte")
	assert(int(metadata_store.statistics(false).ready_queue_items) == 0)
	metadata_store.close()
	for index in metadata_store.MAX_JOBS:
		DirAccess.remove_absolute(folder.path_join("missing%d.json" % index))
	# A tiny ready budget forces the worker to wait while holding one page.
	# Cancellation must wake that publisher, discard queued CPU images, and
	# allow the next owner to load instead of submitting stale pages first.
	var store := STORE.new()
	store.configure(1,1)
	epoch = store.begin_owner("old-map")
	assert(store.acquire(path,"old-map",epoch) == OK)
	var deadline := Time.get_ticks_msec()+5000
	while int(store.statistics(false).ready_queue_bytes) == 0:
		assert(Time.get_ticks_msec()<deadline)
		await process_frame
	store.release_owner("old-map")
	var cancelled := store.statistics(false)
	assert(int(cancelled.ready_queue_bytes) == 0 and int(cancelled.ready_queue_items) == 0)
	assert(store.poll_gpu(4,100000) == 0 and int(store.statistics(false).uploaded_pages) == 0)
	epoch = store.begin_owner("new-map")
	assert(store.acquire(path,"new-map",epoch,100) == OK)
	await _ready(store,path)
	assert(int(store.statistics(false).uploaded_pages) == 3,"only new generation may upload")
	assert(int(store.statistics(false).decoded_pages) <= 5,"cancelled job must stop before its third page")
	# Partial upload cancellation must release its resident accounting as well.
	store.release_owner("new-map")
	assert(store.resident_bytes() == 0)
	epoch = store.begin_owner("closing")
	assert(store.acquire(path,"closing",epoch) == OK)
	deadline = Time.get_ticks_msec()+5000
	while int(store.statistics(false).ready_queue_bytes) == 0:
		assert(Time.get_ticks_msec()<deadline)
		await process_frame
	var started := Time.get_ticks_usec()
	store.close()
	assert(Time.get_ticks_usec()-started < 500000,"close blocked on ready-budget publisher")
	assert(int(store.statistics(false).active_decode_jobs) == 0)
	epoch = store.begin_owner("restarted")
	assert(store.acquire(path,"restarted",epoch) == OK)
	await _ready(store,path)
	store.release_owner("restarted")
	store.close()
	for name in ["low.json","high.json","middle.json","pages.json","page.png"]:
		DirAccess.remove_absolute(folder.path_join(name))
	DirAccess.remove_absolute(folder)
	print("runtime cancellation: 1000 generations / MAX_JOBS / stale publish / priority / zero-byte item cap / byte backpressure / close+restart passed")
	quit()
