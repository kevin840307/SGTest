extends SceneTree

# Baseline only: intentionally synchronous CPU decode / GPU upload. No draft
# resource manager is used, so its spikes and recovery can be measured honestly.
var cycles := 30
var root_path := "res://assets"
var report_path := ""
var monster_root := ""
var records: Array = []
var frame_intervals: Array[float] = []
var previous_usec := 0
var track_frames := false

func marker(cycle: int, phase: String) -> void:
	FileAccess.open(report_path+".progress.json",FileAccess.WRITE).store_string(JSON.stringify({"cycle":cycle,"phase":phase}))

func _initialize() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--cycles="): cycles = maxi(1,int(arg.trim_prefix("--cycles=")))
		if arg.begins_with("--assets="): root_path = arg.trim_prefix("--assets=")
		if arg.begins_with("--monster-assets="): monster_root = arg.trim_prefix("--monster-assets=")
		if arg.begins_with("--report="): report_path = arg.trim_prefix("--report=")
	_run.call_deferred()

func _process(_delta: float) -> bool:
	var now := Time.get_ticks_usec()
	if previous_usec > 0 and track_frames:
		frame_intervals.append((now-previous_usec)/1000.0)
	previous_usec = now
	return false

func memory() -> Dictionary:
	return {"godot_static_bytes":OS.get_static_memory_usage(),
		"texture_bytes":int(RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TEXTURE_MEM_USED)),
		"video_bytes":int(RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_VIDEO_MEM_USED))}

func load_palette(manifests: Array) -> Dictionary:
	var textures: Array[Texture2D] = []
	var decode: Array[float] = []
	var submit: Array[float] = []
	var sync_wait: Array[float] = []
	var rgba_bytes := 0
	var started := Time.get_ticks_usec()
	for path in manifests:
		var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(path))
		for page in data.pages:
			var stamp := Time.get_ticks_usec()
			var image := Image.load_from_file(str(path).get_base_dir().path_join(page))
			assert(image != null)
			decode.append((Time.get_ticks_usec()-stamp)/1000.0)
			rgba_bytes += image.get_width()*image.get_height()*4
			stamp = Time.get_ticks_usec()
			textures.append(ImageTexture.create_from_image(image))
			submit.append((Time.get_ticks_usec()-stamp)/1000.0)
			stamp = Time.get_ticks_usec()
			RenderingServer.force_sync()
			sync_wait.append((Time.get_ticks_usec()-stamp)/1000.0)
	return {"textures":textures,"decode_ms":decode,"upload_submit_ms":submit,"gpu_sync_wait_ms":sync_wait,
		"load_wall_ms":(Time.get_ticks_usec()-started)/1000.0,"rgba_bytes":rgba_bytes}

func references(textures: Array) -> Array:
	var weak: Array = []
	for texture in textures:
		weak.append(weakref(texture))
	return weak

func _run() -> void:
	if report_path.is_empty():
		report_path = ProjectSettings.globalize_path("res://").path_join("../reports/asset_lifecycle_baseline.json")
	var population: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(root_path.path_join("maps/13/population.json")))
	var manifests: Array = []
	for definition in population.definitions:
		var path := (root_path if monster_root.is_empty() else monster_root).path_join(str(definition.atlas))
		if path not in manifests: manifests.append(path)
	await process_frame
	await process_frame
	await create_timer(1.0).timeout
	RenderingServer.force_sync()
	var cold_baseline := memory()
	for cycle in cycles+2:
		track_frames = cycle >= 2
		if cycle == 2: previous_usec = Time.get_ticks_usec()
		await process_frame
		marker(cycle,"loading")
		var loaded := load_palette(manifests)
		var active := memory()
		var weak := references(loaded.textures)
		var texture_count: int = loaded.textures.size()
		loaded.textures.clear()
		loaded.erase("textures")
		var stamp := Time.get_ticks_usec()
		RenderingServer.force_sync()
		await process_frame
		await process_frame
		RenderingServer.force_sync()
		var alive := 0
		for item in weak:
			if item.get_ref() != null: alive += 1
		var unloaded := memory()
		var record := {"cycle":cycle,"warmup":cycle<2,"pages":texture_count,"active":active,
			"after_unload":unloaded,"alive_texture_handles":alive,"unload_wall_ms":(Time.get_ticks_usec()-stamp)/1000.0,
			"loading":loaded}
		records.append(record)
		print("asset_cycle "+JSON.stringify({"cycle":cycle,"load_ms":loaded.load_wall_ms,"alive":alive,
			"texture_after":unloaded.texture_bytes,"static_after":unloaded.godot_static_bytes}))
		assert(alive == 0,"GPU texture handle retained after explicit release")
		marker(cycle,"unloaded")
		await create_timer(0.05).timeout
	track_frames = false
	frame_intervals.sort()
	var measured: Array = records.filter(func(row: Dictionary) -> bool: return not row.warmup)
	var baseline: Dictionary = records[1].after_unload
	var texture_peak := 0
	var texture_after_peak := 0
	for row in measured:
		texture_peak = maxi(texture_peak,row.active.texture_bytes)
		texture_after_peak = maxi(texture_after_peak,row.after_unload.texture_bytes)
	var report := {"baseline_mode":"synchronous raw PNG decode + ImageTexture create + benchmark-only force_sync",
		"monster_root":root_path if monster_root.is_empty() else monster_root,
		"renderer":RenderingServer.get_video_adapter_name(),"cycles":cycles,"warmup_cycles":2,
		"frame_sample_scope":"excludes 1s window warmup and first 2 load/unload cycles",
		"manifests":manifests.size(),"cold_baseline":cold_baseline,"warm_baseline":baseline,
		"texture_peak_bytes":texture_peak,"texture_after_peak_bytes":texture_after_peak,
		"all_texture_handles_released":measured.all(func(row: Dictionary) -> bool: return row.alive_texture_handles==0),
		"engine_texture_recovery_passed":texture_after_peak <= int(baseline.texture_bytes)+1048576,
		"frame_ms_p95":frame_intervals[ceili(frame_intervals.size()*.95)-1],
		"frame_ms_p99":frame_intervals[ceili(frame_intervals.size()*.99)-1],"frame_ms_max":frame_intervals[-1],
		"gpu_measurement_note":"submit is CPU call duration; force_sync wait is CPU/GPU completion wait, not isolated GPU timestamp",
		"physical_driver_vram":"NOT VERIFIED; engine texture/video counters only","records":records}
	if report_path.is_empty():
		report_path = ProjectSettings.globalize_path("res://").path_join("../reports/asset_lifecycle_baseline.json")
	FileAccess.open(report_path,FileAccess.WRITE).store_string(JSON.stringify(report,"  "))
	print("asset_lifecycle_report "+report_path)
	quit(0 if report.all_texture_handles_released and report.engine_texture_recovery_passed else 1)
