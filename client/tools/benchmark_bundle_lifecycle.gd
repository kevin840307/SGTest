extends SceneTree

const STORE = preload("res://scripts/runtime_asset_store.gd")

var manifest := ""
var cycles := 20
var report_path := ""
var intervals: Array[float] = []
var last_usec := 0
var track := false

func _initialize() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--manifest="):
			manifest = arg.trim_prefix("--manifest=")
		elif arg.begins_with("--cycles="):
			cycles = maxi(1,int(arg.trim_prefix("--cycles=")))
		elif arg.begins_with("--report="):
			report_path = arg.trim_prefix("--report=")
	if manifest.is_empty():
		printerr("--manifest is required")
		quit(2)
		return
	_run.call_deferred()

func _process(_delta: float) -> bool:
	var now := Time.get_ticks_usec()
	if track and last_usec > 0:
		intervals.append((now-last_usec)/1000.0)
	last_usec = now
	return false

func percentile(values: Array, fraction: float) -> float:
	if values.is_empty(): return 0.0
	var ordered := values.duplicate()
	ordered.sort()
	return float(ordered[clampi(ceili(ordered.size()*fraction)-1,0,ordered.size()-1)])

func memory() -> Dictionary:
	return {
		"texture":int(RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TEXTURE_MEM_USED)),
		"video":int(RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_VIDEO_MEM_USED)),
		"static":OS.get_static_memory_usage(),
	}

func _run() -> void:
	var store := STORE.new()
	store.configure(64*1024*1024,16*1024*1024)
	store.start()
	await process_frame
	await process_frame
	await create_timer(0.5).timeout
	var records: Array = []
	for cycle in cycles+2:
		track = cycle >= 2
		if cycle == 2: last_usec = Time.get_ticks_usec()
		var owner := "bundle-bench"
		var epoch := store.begin_owner(owner)
		var before := store.statistics()
		var started := Time.get_ticks_usec()
		assert(store.acquire(manifest,owner,epoch,1000) == OK)
		var load_frames := 0
		while store.pending_count() > 0:
			store.poll_gpu(1,1200)
			assert(not store.is_failed(manifest))
			load_frames += 1
			await process_frame
			assert(load_frames < 600)
		var atlas: RefCounted = store.get_atlas(manifest)
		assert(atlas != null)
		var weak: Array = []
		for page in atlas.pages: weak.append(weakref(page))
		var active := memory()
		var after := store.statistics()
		var row := {
			"cycle":cycle,
			"warmup":cycle<2,
			"load_ms":(Time.get_ticks_usec()-started)/1000.0,
			"load_frames":load_frames,
			"pages":atlas.pages.size(),
			"bytes":store.bytes_for(manifest),
			"active":active,
			"decode_ms":after.decode_ms.slice(int(before.decode_ms.size())),
			"upload_ms":after.upload_submit_ms.slice(int(before.upload_submit_ms.size())),
		}
		atlas = null
		store.release_owner(owner)
		store.purge_unowned(true)
		for wait in 8:
			var alive := 0
			for ref in weak:
				if ref.get_ref() != null: alive += 1
			if alive == 0: break
			await process_frame
			RenderingServer.force_sync()
		row["after"] = memory()
		records.append(row)
	track = false
	store.close()
	RenderingServer.force_sync()
	var measured: Array = records.filter(func(row): return not row.warmup)
	var loads: Array = measured.map(func(row): return row.load_ms)
	var frames: Array = measured.map(func(row): return row.load_frames)
	var decode: Array = []
	var upload: Array = []
	for row in measured:
		decode.append_array(row.decode_ms)
		upload.append_array(row.upload_ms)
	var result := {
		"manifest":manifest,
		"cycles":cycles,
		"pages":measured[0].pages,
		"rgba_bytes":measured[0].bytes,
		"load_p50_ms":percentile(loads,0.50),
		"load_p95_ms":percentile(loads,0.95),
		"load_frames_p50":percentile(frames,0.50),
		"load_frames_p95":percentile(frames,0.95),
		"decode_page_p95_ms":percentile(decode,0.95),
		"upload_page_p95_ms":percentile(upload,0.95),
		"frame_p95_ms":percentile(intervals,0.95),
		"frame_p99_ms":percentile(intervals,0.99),
		"texture_peak":measured.map(func(row): return int(row.active.texture)).max(),
		"video_peak":measured.map(func(row): return int(row.active.video)).max(),
		"static_peak":measured.map(func(row): return int(row.active.static)).max(),
		"texture_after_max":measured.map(func(row): return int(row.after.texture)).max(),
		"store_resident_after":store.resident_bytes(),
	}
	if not report_path.is_empty():
		FileAccess.open(report_path,FileAccess.WRITE).store_string(JSON.stringify(result,"  "))
	print(JSON.stringify(result))
	quit()
