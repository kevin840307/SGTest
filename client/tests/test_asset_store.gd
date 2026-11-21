extends SceneTree

const Store = preload("res://asset_store/asset_store.gd")
var store = Store.new()
var failures: Array[String] = []
var errors: Array = []
var ready_events: Array = []
var peak_rgba := 0
var peak_assets := 0
var max_pump_us := 0
var pump_times: Array[int] = []


func _initialize() -> void:
	_run.call_deferred()


func check(condition: bool, reason: String) -> void:
	if not condition:
		failures.append(reason)
		push_error(reason)


func tick() -> void:
	var start := Time.get_ticks_usec()
	store.pump(2)
	var elapsed := Time.get_ticks_usec() - start
	max_pump_us = maxi(max_pump_us, elapsed)
	pump_times.append(elapsed)
	var stats: Dictionary = store.statistics()
	check(stats.decode_jobs <= store.max_workers and stats.texture_jobs <= store.max_texture_jobs, "Worker bound exceeded")
	peak_rgba = maxi(peak_rgba, int(stats.rgba_bytes))
	peak_assets = maxi(peak_assets, int(stats.assets))
	await create_timer(0.001).timeout


func until_ready(owner: StringName, epoch: int, ids: Array) -> void:
	var deadline := Time.get_ticks_msec() + 20000
	while Time.get_ticks_msec() < deadline:
		var done := true
		for id in ids:
			if store.get_asset(owner, epoch, id).is_empty():
				done = false
		if done:
			return
		await tick()
		if not errors.is_empty():
			check(false, "Unexpected load error: " + str(errors))
			return
	check(false, "Asset load timeout: " + str(ids))


func drain() -> void:
	var deadline := Time.get_ticks_msec() + 20000
	while store.statistics().decode_jobs + store.statistics().texture_jobs > 0 and Time.get_ticks_msec() < deadline:
		await tick()
	check(store.statistics() == {"assets":0,"scopes":0,"ready":0,"decode_jobs":0,"texture_jobs":0,"rgba_bytes":0}, "Store did not return to baseline: " + str(store.statistics()))


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.is_empty():
		push_error("Expected private assets.pck path [report path]")
		quit(2)
		return
	if args.size() > 2:
		store.max_texture_jobs = int(args[2])
	# Bad metadata must return an error, not leave a worker result half-initialized.
	var invalid_file := "user://invalid_asset_%d.json" % OS.get_process_id()
	for broken in [
		{"version":9,"asset_id":"Enemy/bad","pages":"not an array","page_sizes":[]},
		{"version":9,"asset_id":"Enemy/bad","pages":[".."],"page_sizes":[[1,1]]},
	]:
		var file := FileAccess.open(invalid_file, FileAccess.WRITE)
		file.store_string(JSON.stringify(broken))
		file.close()
		var job = Store.DecodeJob.new()
		job.asset_id = "Enemy/bad"
		job.path = invalid_file
		job.run()
		check(job.result.has("error"), "Invalid metadata did not fail closed")
	for broken in [{"map_id":13,"version":2}, {"map_id":13,"version":3}]:
		var file := FileAccess.open(invalid_file, FileAccess.WRITE)
		file.store_string(JSON.stringify(broken))
		file.close()
		var job = Store.DecodeJob.new()
		job.asset_id = "maps/13"
		job.path = invalid_file
		job.run()
		check(job.result.has("error"), "Map without scenery contract accepted")
	DirAccess.remove_absolute(invalid_file)
	check(store.open_pack(args[0], "bad digest") == ERR_FILE_CORRUPT, "Wrong pack checksum accepted")
	check(store.open_pack(args[0]) == OK, "Cannot open pack/catalog")
	store.asset_ready.connect(func(owner, epoch, id): ready_events.append([owner, epoch, id]))
	store.asset_failed.connect(func(owner, epoch, id, reason): errors.append([owner, epoch, id, reason]))
	var a := store.begin_scope(&"map")
	var b := store.begin_scope(&"character")
	check(a > 0 and b > 0, "Scope creation failed")
	check(store.acquire(&"map", a, "Enemy/missing") == ERR_FILE_NOT_FOUND, "Unknown ID accepted")
	check(store.acquire(&"map", a, "maps/../15") == ERR_FILE_NOT_FOUND, "Unsafe ID accepted")
	check(store.acquire(&"map", a + 100, "Enemy/en010a") == ERR_INVALID_PARAMETER, "Stale epoch accepted")
	check(store.acquire(&"map", a, "Enemy/en010a") == OK, "Acquire failed")
	check(store.acquire(&"map", a, "Enemy/en010a") == OK, "Idempotent acquire failed")
	check(store.acquire(&"character", b, "Enemy/en010a") == OK, "Shared acquire failed")
	await until_ready(&"map", a, ["Enemy/en010a"])
	var first: Dictionary = store.get_asset(&"map", a, "Enemy/en010a")
	var second: Dictionary = store.get_asset(&"character", b, "Enemy/en010a")
	if first.is_empty() or second.is_empty():
		quit(1)
		return
	check(first.pages[0].get_instance_id() == second.pages[0].get_instance_id(), "Owners do not share Texture instance")
	check(first.is_read_only() and first.manifest.is_read_only() and first.manifest.frames.is_read_only() and first.pages.is_read_only(), "Shared data is mutable")
	var texture_ref: WeakRef = weakref(first.pages[0])
	first = {}
	second = {}
	store.end_scope(&"map", a)
	check(store.statistics().ready == 1, "Releasing one owner removed shared resource")
	store.end_scope(&"character", b)
	await drain()
	await process_frame
	check(texture_ref.get_ref() == null, "Texture retained after all owners/references released")
	# Cancel after a Texture request has started; a replacement scope must not receive old ready.
	a = store.begin_scope(&"map")
	store.acquire(&"map", a, "maps/15")
	var deadline := Time.get_ticks_msec() + 10000
	while store.statistics().texture_jobs == 0 and Time.get_ticks_msec() < deadline:
		await tick()
	check(store.statistics().texture_jobs > 0, "Cancellation did not exercise an in-flight Texture")
	var replacement := store.begin_scope(&"map")
	store.end_scope(&"map", a) # An old callback must not release the replacement scope.
	check(store.acquire(&"map", replacement, "maps/13") == OK, "Replacement acquire failed")
	await until_ready(&"map", replacement, ["maps/13"])
	check(store.get_asset(&"map", a, "maps/15").is_empty(), "Old scope still readable")
	check(not ready_events.has([&"map", a, "maps/15"]), "Cancelled owner received ready")
	store.end_scope(&"map", replacement)
	await drain()
	# Full map palettes, not a synthetic one-texture loop.
	for cycle in 30:
		var map_id := 15 if cycle % 2 == 0 else 13
		var map_asset := "maps/%d" % map_id
		a = store.begin_scope(&"map")
		store.acquire(&"map", a, map_asset)
		await until_ready(&"map", a, [map_asset])
		var map_data: Dictionary = store.get_asset(&"map", a, map_asset)
		if map_data.is_empty():
			break
		var palette := {}
		for definition in map_data.manifest.scenery_definitions.values():
			for appearance in definition.appearances:
				if appearance != null:
					palette[str(appearance.atlas).trim_suffix("/atlas.json")] = true
		for row in map_data.extra.npcs.npcs:
			if row.visual.resolved:
				palette[str(row.visual.atlas).trim_suffix("/atlas.json")] = true
		for row in map_data.extra.population.definitions:
			palette[str(row.atlas).trim_suffix("/atlas.json")] = true
		for part in ["body", "leg", "head", "left_arm", "right_arm", "death"]:
			palette["players/b/base/" + part] = true
		palette["players/b/hair/hair00"] = true
		for id in palette:
			check(store.acquire(&"map", a, id) == OK, "Map palette capacity/mapping failed: " + id)
		await until_ready(&"map", a, palette.keys())
		map_data = {}
		store.end_scope(&"map", a)
		await drain()
		print("ASSETSTORE_CYCLE ", cycle + 1, " map=", map_id, " assets=", palette.size() + 1)
	store.max_assets = 1
	a = store.begin_scope(&"limits")
	check(store.acquire(&"limits", a, "Enemy/en010a") == OK, "Limit test setup failed")
	check(store.acquire(&"limits", a, "Enemy/en009a") == ERR_OUT_OF_MEMORY, "Asset capacity bypassed")
	store.end_scope(&"limits", a)
	await drain()
	store.max_assets = 256
	store.max_rgba_bytes = 1
	a = store.begin_scope(&"budget")
	store.acquire(&"budget", a, "Enemy/en010a")
	deadline = Time.get_ticks_msec() + 10000
	while errors.is_empty() and Time.get_ticks_msec() < deadline:
		await tick()
	check(errors.size() == 1 and str(errors[0][3]).contains("budget"), "Byte budget was not enforced")
	errors.clear()
	store.end_scope(&"budget", a)
	await drain()
	pump_times.sort()
	var report := {"status": "PASS" if failures.is_empty() else "FAIL", "cycles":30,
		"max_texture_jobs":store.max_texture_jobs,
		"failures":failures, "peak_assets":peak_assets, "peak_rgba_budget_bytes":peak_rgba,
		"pump_p95_us":pump_times[int((pump_times.size() - 1) * 0.95)],
		"pump_p99_us":pump_times[int((pump_times.size() - 1) * 0.99)], "pump_max_us":max_pump_us,
		"final":store.statistics(), "scope":"Headless Store ownership/load/cancel; not RSS/VRAM or rendered game E2E"}
	if args.size() > 1:
		var file := FileAccess.open(args[1], FileAccess.WRITE)
		if file != null:
			file.store_string(JSON.stringify(report, "\t"))
	print("ASSETSTORE_RESULT ", JSON.stringify(report))
	quit(0 if failures.is_empty() else 1)
