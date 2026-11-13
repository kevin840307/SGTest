extends SceneTree

class BenchCanvas extends Node2D:
	var textures: Array[Texture2D] = []
	var frames: Array = []
	var entities := 300
	var phase := 0

	func _draw() -> void:
		if frames.is_empty():
			return
		for i in entities:
			var frame: Dictionary = frames[(i * 37 + phase) % frames.size()]
			if frame.page < 0:
				continue
			var region: Array = frame.rect
			var crop: Array = frame.crop
			var origin: Array = frame.raw_origin
			var x := float((i * 83) % 1180) - float(origin[0]) + float(crop[0])
			var y := float((i * 47) % 640) - float(origin[1]) + float(crop[1])
			draw_texture_rect_region(textures[frame.page],
				Rect2(x, y, region[2], region[3]),
				Rect2(region[0], region[1], region[2], region[3]))

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 1:
		printerr("Usage: godot --script tools/benchmark_atlas_render.gd -- group.atlas.json")
		quit(2)
		return
	var path: String = args[0]
	var document: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not document is Dictionary or not document.has("pages"):
		printerr("Invalid group atlas manifest")
		quit(2)
		return
	DisplayServer.window_set_size(Vector2i(1280, 720))
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var canvas := BenchCanvas.new()
	canvas.frames = document.frames
	canvas.entities = 300
	for page in document.pages:
		var image := Image.new()
		if image.load(path.get_base_dir().path_join(page)) != OK:
			printerr("Cannot load page: ", page)
			quit(1)
			return
		canvas.textures.append(ImageTexture.create_from_image(image))
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
	print(JSON.stringify({"group": document.group_key, "side": document.atlas_side,
		"pages": document.pages.size(), "entities": canvas.entities,
		"renderer": RenderingServer.get_video_adapter_name(),
		"frame_p50_ms": times[60], "frame_p95_ms": times[114], "frame_p99_ms": times[118],
		"draw_calls_p50": draws[60], "draw_calls_p95": draws[114], "headless": false}))
	quit(0)
