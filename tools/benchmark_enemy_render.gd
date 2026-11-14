extends SceneTree

# Render A/B for Enemy atlas candidates. Same visible instance count and frame
# selection are used for dense and source-local layouts; no network/game logic.
class EnemyCanvas extends Node2D:
	var visuals: Array[Dictionary] = []
	var entities: Array[Dictionary] = []
	var phase := 0

	func _draw() -> void:
		for entity in entities:
			var visual: Dictionary = visuals[entity.visual_index]
			var frames: Array = visual.frames
			var frame: Dictionary = frames[(entity.index * 37 + phase) % frames.size()]
			if frame.page < 0:
				continue
			var region: Array = frame.rect
			var crop: Array = frame.crop
			var origin: Array = frame.raw_origin
			var x: float = entity.x - float(origin[0]) + float(crop[0])
			var y: float = entity.y - float(origin[1]) + float(crop[1])
			draw_texture_rect_region(
				visual.textures[frame.page],
				Rect2(x, y, region[2], region[3]),
				Rect2(region[0], region[1], region[2], region[3])
			)

func _find_manifests(folder: String, result: Array[String]) -> void:
	if not DirAccess.dir_exists_absolute(folder):
		return
	for name in DirAccess.get_files_at(folder):
		if name == "group.atlas.json":
			result.append(folder.path_join(name))
	for name in DirAccess.get_directories_at(folder):
		_find_manifests(folder.path_join(name), result)

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 1 or args.size() > 5:
		printerr("Usage: godot --script tools/benchmark_enemy_render.gd -- candidate-root [width height kinds instances]")
		quit(2)
		return
	var base: String = args[0]
	var width := 1440 if args.size() < 3 else int(args[1])
	var height := 900 if args.size() < 3 else int(args[2])
	var kind_limit := 0 if args.size() < 4 else int(args[3])
	var instance_count := 300 if args.size() < 5 else int(args[4])
	if width < 640 or height < 480 or instance_count <= 0:
		printerr("Invalid viewport or instance count")
		quit(2)
		return
	var paths: Array[String] = []
	_find_manifests(base.path_join("Enemy"), paths)
	paths.sort()
	if paths.is_empty():
		printerr("No Enemy atlas groups under: ", base)
		quit(2)
		return
	if kind_limit > 0 and paths.size() > kind_limit:
		paths.resize(kind_limit)

	DisplayServer.window_set_min_size(Vector2i(640, 480))
	DisplayServer.window_set_size(Vector2i(width, height))
	root.min_size = Vector2i(640, 480)
	root.size = Vector2i(width, height)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)

	var canvas := EnemyCanvas.new()
	var page_count := 0
	var rgba_bytes := 0
	var png_bytes := 0
	var load_start := Time.get_ticks_usec()
	for path in paths:
		var document: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
		if not document is Dictionary or not document.has("frames"):
			printerr("Invalid manifest: ", path)
			quit(2)
			return
		var textures: Array[Texture2D] = []
		for page in document.pages:
			var page_path: String = path.get_base_dir().path_join(page)
			var image := Image.new()
			if image.load(page_path) != OK:
				printerr("Cannot load: ", page_path)
				quit(1)
				return
			rgba_bytes += image.get_width() * image.get_height() * 4
			var file := FileAccess.open(page_path, FileAccess.READ)
			png_bytes += file.get_length()
			page_count += 1
			textures.append(ImageTexture.create_from_image(image))
		canvas.visuals.append({"textures": textures, "frames": document.frames})
	var load_ms := float(Time.get_ticks_usec() - load_start) / 1000.0

	for i in instance_count:
		canvas.entities.append({
			"index": i,
			"visual_index": i % canvas.visuals.size(),
			"x": float(50 + ((i * 83) % max(100, width - 100))),
			"y": float(50 + ((i * 47) % max(80, height - 80)))
		})
	canvas.entities.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return a.y < b.y)
	root.add_child(canvas)

	var times: Array[float] = []
	var draws: Array[int] = []
	var last := Time.get_ticks_usec()
	for i in 180:
		canvas.phase = i
		canvas.queue_redraw()
		await process_frame
		var now := Time.get_ticks_usec()
		if i >= 60:
			times.append(float(now - last) / 1000.0)
			draws.append(int(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last = now
	times.sort()
	draws.sort()
	var n := times.size()
	print(JSON.stringify({
		"requested_viewport": [width, height],
		"actual_window": [DisplayServer.window_get_size().x, DisplayServer.window_get_size().y],
		"kinds": canvas.visuals.size(),
		"instances": instance_count,
		"pages": page_count,
		"rgba_area_bytes": rgba_bytes,
		"png_bytes": png_bytes,
		"load_ms": load_ms,
		"frame_p50_ms": times[int(floor(float(n - 1) * 0.50))],
		"frame_p95_ms": times[int(floor(float(n - 1) * 0.95))],
		"frame_p99_ms": times[int(floor(float(n - 1) * 0.99))],
		"draw_calls_p50": draws[int(floor(float(n - 1) * 0.50))],
		"draw_calls_p95": draws[int(floor(float(n - 1) * 0.95))],
		"video_mem_monitor_bytes": int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)),
		"texture_mem_monitor_bytes": int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)),
		"renderer": RenderingServer.get_video_adapter_name()
	}))
	quit(0)
