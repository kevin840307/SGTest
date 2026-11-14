extends SceneTree

# ASSET204 benchmark: real RuntimeAssetStore worker decode + bounded main-thread
# GPU commits. Same Map13 manifests and 30-cycle release/reload shape as the
# synchronous baseline, but never decodes PNGs on the main thread.
const STORE = preload("res://scripts/runtime_asset_store.gd")

var cycles := 30
var root_path := "res://assets"
var monster_root := ""
var report_path := ""
var records: Array = []
var frame_intervals: Array[float] = []
var previous_usec := 0
var track_frames := false

func marker(cycle: int, phase: String) -> void:
	FileAccess.open(report_path + ".progress.json", FileAccess.WRITE).store_string(
		JSON.stringify({"cycle": cycle, "phase": phase})
	)

func _initialize() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--cycles="): cycles = maxi(1, int(arg.trim_prefix("--cycles=")))
		if arg.begins_with("--assets="): root_path = arg.trim_prefix("--assets=")
		if arg.begins_with("--monster-assets="): monster_root = arg.trim_prefix("--monster-assets=")
		if arg.begins_with("--report="): report_path = arg.trim_prefix("--report=")
	_run.call_deferred()

func _process(_delta: float) -> bool:
	var now := Time.get_ticks_usec()
	if previous_usec > 0 and track_frames:
		frame_intervals.append((now - previous_usec) / 1000.0)
	previous_usec = now
	return false

func memory() -> Dictionary:
	return {
		"godot_static_bytes": OS.get_static_memory_usage(),
		"texture_bytes": int(RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TEXTURE_MEM_USED)),
		"video_bytes": int(RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_VIDEO_MEM_USED)),
	}

func percentile(values: Array, fraction: float) -> float:
	if values.is_empty():
		return 0.0
	var ordered := values.duplicate()
	ordered.sort()
	return float(ordered[clampi(ceili(ordered.size() * fraction) - 1, 0, ordered.size() - 1)])

func weak_pages(atlas: RefCounted) -> Array:
	var refs: Array = []
	for page in atlas.pages:
		refs.append(weakref(page))
	return refs

func alive_count(refs: Array) -> int:
	var alive := 0
	for item in refs:
		if item.get_ref() != null:
			alive += 1
	return alive

func _run() -> void:
	if report_path.is_empty():
		report_path = ProjectSettings.globalize_path("res://").path_join("../reports/asset_lifecycle_async.json")
	var population: Dictionary = JSON.parse_string(
		FileAccess.get_file_as_string(root_path.path_join("maps/13/population.json"))
	)
	var base := root_path if monster_root.is_empty() else monster_root
	var manifests: Array[String] = []
	var priorities: Dictionary = {}
	for definition in population.definitions:
		var path: String = base.path_join(str(definition.atlas))
		if path not in manifests:
			manifests.append(path)
		priorities[path] = 0
	for spawn in population.spawns:
		var definition_id := int(spawn.definition)
		for definition in population.definitions:
			if int(definition.id) == definition_id:
				var path: String = base.path_join(str(definition.atlas))
				priorities[path] = int(priorities.get(path, 0)) + 1
				break
	manifests.sort()

	var store := STORE.new()
	store.configure(64 * 1024 * 1024, 16 * 1024 * 1024)
	store.start()

	await process_frame
	await process_frame
	await create_timer(1.0).timeout
	RenderingServer.force_sync()
	var cold_baseline := memory()

	for cycle in cycles + 2:
		track_frames = cycle >= 2
		if cycle == 2:
			previous_usec = Time.get_ticks_usec()
		await process_frame
		marker(cycle, "loading")
		var owner := "asset204-lifecycle"
		var epoch: int = store.begin_owner(owner)
		var before := store.statistics()
		var started := Time.get_ticks_usec()
		for path in manifests:
			assert(store.acquire(path, owner, epoch, int(priorities.get(path, 0))) == OK)
		var load_frames := 0
		while store.pending_count() > 0:
			store.poll_gpu(1, 1200)
			for path in manifests:
				assert(not store.is_failed(path), "async runtime asset failed: %s" % path)
			load_frames += 1
			await process_frame
			assert(load_frames < 600, "async runtime asset load exceeded 600 frames")
		var load_wall_ms := (Time.get_ticks_usec() - started) / 1000.0
		var active := memory()
		var weak: Array = []
		var page_count := 0
		var rgba_bytes := 0
		for path in manifests:
			var atlas: RefCounted = store.get_atlas(path)
			assert(atlas != null)
			rgba_bytes += store.bytes_for(path)
			weak.append_array(weak_pages(atlas))
			page_count += atlas.pages.size()
			atlas = null
		var after_load := store.statistics()
		var decode_start := int(before.decode_ms.size())
		var upload_start := int(before.upload_submit_ms.size())
		var commit_start := int(before.gpu_commit_frame_ms.size())
		var loading := {
			"load_wall_ms": load_wall_ms,
			"load_frames": load_frames,
			"rgba_bytes": rgba_bytes,
			"decode_ms": after_load.decode_ms.slice(decode_start),
			"upload_submit_ms": after_load.upload_submit_ms.slice(upload_start),
			"gpu_commit_frame_ms": after_load.gpu_commit_frame_ms.slice(commit_start),
			"gpu_sync_wait_ms": [],
		}

		marker(cycle, "unloading")
		var unload_started := Time.get_ticks_usec()
		store.release_owner(owner)
		store.purge_unowned(true)
		var release_frames := 0
		var alive := alive_count(weak)
		while alive > 0 and release_frames < 8:
			await process_frame
			RenderingServer.force_sync()
			release_frames += 1
			alive = alive_count(weak)
		var unloaded := memory()
		var record := {
			"cycle": cycle,
			"warmup": cycle < 2,
			"pages": page_count,
			"active": active,
			"after_unload": unloaded,
			"alive_texture_handles": alive,
			"release_frames": release_frames,
			"unload_wall_ms": (Time.get_ticks_usec() - unload_started) / 1000.0,
			"loading": loading,
		}
		records.append(record)
		print("asset_cycle " + JSON.stringify({
			"cycle": cycle,
			"load_ms": load_wall_ms,
			"load_frames": load_frames,
			"alive": alive,
			"resident_after": store.resident_bytes(),
			"texture_after": unloaded.texture_bytes,
		}))
		assert(alive == 0, "GPU texture handle retained after RuntimeAssetStore purge")
		assert(store.resident_bytes() == 0, "RuntimeAssetStore resident bytes retained after purge")
		marker(cycle, "unloaded")
		await create_timer(0.05).timeout

	track_frames = false
	store.close()
	RenderingServer.force_sync()
	frame_intervals.sort()
	var measured: Array = records.filter(func(row: Dictionary) -> bool: return not row.warmup)
	var baseline: Dictionary = records[1].after_unload
	var texture_peak := 0
	var texture_after_peak := 0
	var load_walls: Array = []
	var load_frames_all: Array = []
	for row in measured:
		texture_peak = maxi(texture_peak, row.active.texture_bytes)
		texture_after_peak = maxi(texture_after_peak, row.after_unload.texture_bytes)
		load_walls.append(row.loading.load_wall_ms)
		load_frames_all.append(row.loading.load_frames)
	var final_stats := store.statistics()
	var report := {
		"baseline_mode": "RuntimeAssetStore worker PNG decode + one-page/1.2ms main-thread GPU commit budget",
		"monster_root": base,
		"renderer": RenderingServer.get_video_adapter_name(),
		"cycles": cycles,
		"warmup_cycles": 2,
		"manifests": manifests.size(),
		"cold_baseline": cold_baseline,
		"warm_baseline": baseline,
		"texture_peak_bytes": texture_peak,
		"texture_after_peak_bytes": texture_after_peak,
		"all_texture_handles_released": measured.all(func(row: Dictionary) -> bool: return row.alive_texture_handles == 0),
		"engine_texture_recovery_passed": texture_after_peak <= int(baseline.texture_bytes) + 1048576,
		"store_resident_recovery_passed": int(final_stats.resident_bytes) == 0,
		"frame_ms_p95": percentile(frame_intervals, 0.95),
		"frame_ms_p99": percentile(frame_intervals, 0.99),
		"frame_ms_max": frame_intervals[-1],
		"load_wall_ms_p50": percentile(load_walls, 0.50),
		"load_wall_ms_p95": percentile(load_walls, 0.95),
		"load_frames_p95": percentile(load_frames_all, 0.95),
		"store_metrics": final_stats,
		"physical_driver_vram": "NOT VERIFIED; engine texture/video counters only",
		"records": records,
	}
	FileAccess.open(report_path, FileAccess.WRITE).store_string(JSON.stringify(report, "  "))
	print("asset_lifecycle_report " + report_path)
	quit(0 if report.all_texture_handles_released and report.engine_texture_recovery_passed and report.store_resident_recovery_passed else 1)
