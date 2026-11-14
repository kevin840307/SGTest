extends SceneTree

# Visual stress probe: 20 player, 300 monster, 500 small visual proxies.
# It does not simulate network, combat, drops, collision or cache eviction.
class MixedCanvas extends Node2D:
	var visuals: Dictionary = {}
	var entities: Array[Dictionary] = []
	var phase := 0

	func _draw() -> void:
		for entity in entities:
			var options: Array = visuals[entity.role]
			var visual: Dictionary = options[entity.visual_index]
			var frames: Array = visual.frames
			var frame: Dictionary = frames[(entity.index * 37 + phase) % frames.size()]
			if frame.page < 0:
				continue
			var region: Array = frame.rect
			var crop: Array = frame.crop
			var origin: Array = frame.raw_origin
			var x: float = entity.x - float(origin[0]) + float(crop[0])
			var y: float = entity.y - float(origin[1]) + float(crop[1])
			draw_texture_rect_region(visual.textures[frame.page],
				Rect2(x, y, region[2], region[3]),
				Rect2(region[0], region[1], region[2], region[3]))

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
	if args.size() != 1 and args.size() != 3:
		printerr("Usage: godot --script tools/benchmark_mixed_render.gd -- candidate-root [width height]")
		quit(2)
		return
	var base: String = args[0]
	var width := 1280 if args.size() == 1 else int(args[1])
	var height := 720 if args.size() == 1 else int(args[2])
	if width < 640 or height < 480:
		printerr("Viewport too small")
		quit(2)
		return
	DisplayServer.window_set_min_size(Vector2i(640, 480))
	DisplayServer.window_set_size(Vector2i(width, height))
	root.min_size = Vector2i(640, 480)
	root.size = Vector2i(width, height)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var canvas := MixedCanvas.new()
	var page_count := 0
	var rgba_bytes := 0
	var png_bytes := 0
	var load_start := Time.get_ticks_usec()
	for role in ["Player", "Enemy", "MS"]:
		var paths: Array[String] = []
		_find_manifests(base.path_join(role), paths)
		paths.sort()
		if paths.is_empty():
			printerr("No atlas groups for role: ", role)
			quit(2)
			return
		var options: Array[Dictionary] = []
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
					quit(2)
					return
				rgba_bytes += image.get_width() * image.get_height() * 4
				var file := FileAccess.open(page_path, FileAccess.READ)
				png_bytes += file.get_length()
				page_count += 1
				textures.append(ImageTexture.create_from_image(image))
			options.append({"textures": textures, "frames": document.frames})
		canvas.visuals[role] = options
	var load_ms := float(Time.get_ticks_usec() - load_start) / 1000.0
	var quotas := {"Player": 20, "Enemy": 300, "MS": 500}
	var sequence := 0
	for role in ["Player", "Enemy", "MS"]:
		var count: int = quotas[role]
		var kinds: int = canvas.visuals[role].size()
		for i in count:
			canvas.entities.append({"role": role, "index": i, "visual_index": i % kinds,
				"x": float((sequence * 83) % (width - 100)),
				"y": float((sequence * 47) % (height - 80))})
			sequence += 1
	canvas.entities.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return a.y < b.y)
	root.add_child(canvas)
	var times: Array[float] = []
	var draws: Array[int] = []
	var last := Time.get_ticks_usec()
	for i in 160:
		canvas.phase = i
		canvas.queue_redraw()
		await process_frame
		var now := Time.get_ticks_usec()
		if i >= 40:
			times.append(float(now - last) / 1000.0)
			draws.append(int(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last = now
	times.sort()
	draws.sort()
	var actual_size := DisplayServer.window_get_size()
	print(JSON.stringify({"requested_viewport": [width, height],
		"actual_window": [actual_size.x, actual_size.y],
		"groups": {"player": canvas.visuals["Player"].size(),
		"monster": canvas.visuals["Enemy"].size(), "proxy": canvas.visuals["MS"].size()},
		"instances": {"player": 20, "monster": 300, "proxy": 500},
		"pages": page_count, "rgba_area_bytes": rgba_bytes, "png_bytes": png_bytes,
		"load_ms": load_ms, "frame_p50_ms": times[60], "frame_p95_ms": times[114],
		"frame_p99_ms": times[118],
		"draw_calls_p50": draws[60],
		"draw_calls_p95": draws[114],
		"video_mem_monitor_bytes": int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)),
		"texture_mem_monitor_bytes": int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)),
		"renderer": RenderingServer.get_video_adapter_name()}))
	quit(0)
