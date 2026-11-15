extends SceneTree

const STORE = preload("res://scripts/assets/runtime_asset_store.gd")
const WARMUP := 30

class BenchCanvas extends Node2D:
	var atlases: Array = []
	var clips: Array = []
	var instances: Array[Dictionary] = []
	var phase := 0
	func _draw() -> void:
		for entity in instances:
			var visual := int(entity.visual)
			var atlas: RefCounted = atlases[visual]
			var names: Array = clips[visual]
			if names.is_empty():
				continue
			var clip := str(names[int(entity.clip) % names.size()])
			atlas.draw_frame(
				self,
				Vector2(float(entity.x), float(entity.y)),
				clip,
				float(phase + int(entity.offset)) / 8.0,
				8.0)

func _pct(values: Array[float], q: float) -> float:
	if values.is_empty():
		return 0.0
	values.sort()
	return values[int(floor(float(values.size() - 1) * q))]

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 7:
		printerr("pack enemy_csv instances frames width height output")
		quit(2)
		return
	var pack := str(args[0])
	if not ProjectSettings.load_resource_pack(pack, true):
		printerr("pack mount failed")
		quit(3)
		return
	var ids: Array[String] = []
	for raw in str(args[1]).split(",", false):
		ids.append(str(raw).to_lower())
	var count := int(args[2])
	var frame_count := int(args[3])
	var width := int(args[4])
	var height := int(args[5])
	var output_path := str(args[6])

	DisplayServer.window_set_size(Vector2i(width, height))
	root.size = Vector2i(width, height)
	RenderingServer.render_loop_enabled = true
	Engine.max_fps = 0
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)

	var tex_before := int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var video_before := int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var nodes_before := int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))

	var store := STORE.new()
	store.configure(256 * 1024 * 1024, 32 * 1024 * 1024)
	var owner := "production-bench"
	var epoch := store.begin_owner(owner)
	var paths: Array[String] = []
	var started := Time.get_ticks_usec()
	for id in ids:
		var path := "res://assets/Enemy/%s/atlas.json" % id
		paths.append(path)
		var err: Error = store.acquire(path, owner, epoch, 10)
		if err != OK:
			printerr("acquire failed ",path," ",err)
			store.close()
			quit(4)
			return
	var deadline := Time.get_ticks_msec() + 30000
	while Time.get_ticks_msec() < deadline:
		store.poll_gpu(16, 10000)
		var ready := true
		for path in paths:
			if store.is_failed(path):
				printerr("load failed ",path," ",store.error(path))
				store.close()
				quit(5)
				return
			if not store.is_ready(path):
				ready = false
		if ready:
			break
		await process_frame
	for path in paths:
		if not store.is_ready(path):
			printerr("load timeout ",path)
			store.close()
			quit(6)
			return
	var load_ms := float(Time.get_ticks_usec() - started) / 1000.0

	var canvas := BenchCanvas.new()
	for path in paths:
		var atlas: RefCounted = store.get_atlas(path)
		canvas.atlases.append(atlas)
		var names: Array = atlas.data.get("logical_views", {}).keys()
		if names.is_empty():
			names = atlas.data.get("clips", {}).keys()
		names.sort()
		canvas.clips.append(names)
	for i in count:
		var visual := i % ids.size()
		canvas.instances.append({
			"visual": visual,
			"clip": i * 7,
			"offset": (i * 11) % 13,
			"x": 24 + ((i * 83) % maxi(100, width - 48)),
			"y": 32 + ((i * 47) % maxi(80, height - 64)),
		})
	root.add_child(canvas)
	await process_frame

	var tex_after := int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var video_after := int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var nodes_after := int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var intervals: Array[float] = []
	var draws: Array[float] = []
	var last := Time.get_ticks_usec()
	for frame_id in frame_count:
		canvas.phase = frame_id
		canvas.queue_redraw()
		await process_frame
		var now := Time.get_ticks_usec()
		if frame_id >= WARMUP:
			intervals.append(float(now - last) / 1000.0)
			draws.append(float(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last = now
	var stats := store.statistics(false)
	var result := {
		"renderer_path": "production_store_imported_entityatlas",
		"assets": ids,
		"instances": count,
		"frames": frame_count,
		"load_ms": load_ms,
		"frame_p50_ms": _pct(intervals, .5),
		"frame_p95_ms": _pct(intervals, .95),
		"frame_p99_ms": _pct(intervals, .99),
		"draw_calls_p50": _pct(draws, .5),
		"draw_calls_p95": _pct(draws, .95),
		"texture_mem_delta": maxi(0, tex_after - tex_before),
		"video_mem_delta": maxi(0, video_after - video_before),
		"node_delta": maxi(0, nodes_after - nodes_before),
		"store": stats,
	}
	var output := JSON.stringify(result)
	print(output)
	var f := FileAccess.open(output_path, FileAccess.WRITE)
	if f != null:
		f.store_string(output + "\n")
		f.close()
	store.release_owner(owner)
	store.purge_unowned(true)
	store.close()
	quit(0)
