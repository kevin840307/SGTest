extends SceneTree

# Same 25 HK2 ground frames and 500 visible instances for every candidate.
class GroundCanvas extends Node2D:
	var textures: Array[Texture2D] = []
	var frames: Array = []
	var locations: Array[Vector2] = []

	func _draw() -> void:
		for index in locations.size():
			var frame: Dictionary = frames[index % frames.size()]
			var region: Array = frame.rect
			var crop: Array = frame.crop
			var origin: Array = frame.raw_origin
			var point := locations[index]
			draw_texture_rect_region(textures[frame.page],
				Rect2(point.x - origin[0] + crop[0], point.y - origin[1] + crop[1],
					region[2], region[3]),
				Rect2(region[0], region[1], region[2], region[3]))


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 3:
		printerr("Usage: godot --rendering-method gl_compatibility --script tools/benchmark_ground_render.gd -- catalog.json width height")
		quit(2)
		return
	var path: String = args[0]
	var width := int(args[1])
	var height := int(args[2])
	if width < 640 or width > 1440 or height < 480:
		printerr("Window dimensions outside benchmark range")
		quit(2)
		return
	var catalog: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not catalog is Dictionary or not catalog.has("pages") or not catalog.has("frames"):
		printerr("Invalid catalog")
		quit(2)
		return
	DisplayServer.window_set_min_size(Vector2i(640, 480))
	DisplayServer.window_set_size(Vector2i(width, height))
	root.min_size = Vector2i(640, 480)
	root.size = Vector2i(width, height)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var canvas := GroundCanvas.new()
	var rgba_bytes := 0
	var png_bytes := 0
	var load_start := Time.get_ticks_usec()
	for name in catalog.pages:
		var page_path: String = path.get_base_dir().path_join(name)
		var image := Image.new()
		if image.load(page_path) != OK:
			printerr("Cannot decode page: ", page_path)
			quit(1)
			return
		rgba_bytes += image.get_width() * image.get_height() * 4
		var file := FileAccess.open(page_path, FileAccess.READ)
		png_bytes += file.get_length()
		canvas.textures.append(ImageTexture.create_from_image(image))
	var load_ms := float(Time.get_ticks_usec() - load_start) / 1000.0
	canvas.frames = catalog.frames
	for index in 500:
		canvas.locations.append(Vector2(float((index * 83) % (width - 100)),
			float((index * 47) % (height - 80))))
	canvas.locations.sort_custom(func(a: Vector2, b: Vector2) -> bool: return a.y < b.y)
	root.add_child(canvas)
	var times: Array[float] = []
	var draws: Array[int] = []
	var last := Time.get_ticks_usec()
	for index in 160:
		canvas.queue_redraw()
		await process_frame
		var now := Time.get_ticks_usec()
		if index >= 40:
			times.append(float(now - last) / 1000.0)
			draws.append(int(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last = now
	times.sort()
	draws.sort()
	var actual := DisplayServer.window_get_size()
	print(JSON.stringify({"variant": catalog.variant, "source_sha256": catalog.source_sha256,
		"instances": 500, "kinds": catalog.sample_indices.size(),
		"pages": catalog.pages.size(), "rgba_area_bytes": rgba_bytes,
		"png_bytes": png_bytes, "load_ms": load_ms,
		"actual_window": [actual.x, actual.y],
		"frame_p50_ms": times[60], "frame_p95_ms": times[114],
		"frame_p99_ms": times[118], "draw_calls_p50": draws[60],
		"draw_calls_p95": draws[114],
		"video_mem_monitor_bytes": int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)),
		"renderer": RenderingServer.get_video_adapter_name()}))
	quit(0)
