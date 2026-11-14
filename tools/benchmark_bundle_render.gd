extends SceneTree

class BundleCanvas extends Node2D:
	var textures: Array[Texture2D] = []
	var frames: Array = []
	var entities := 300
	var phase := 0
	func _draw() -> void:
		if frames.is_empty(): return
		for i in entities:
			var frame: Dictionary = frames[(i*37+phase)%frames.size()]
			if int(frame.page) < 0: continue
			var rect: Array = frame.rect
			var trim: Array = frame.get("trim",[0,0])
			var origin: Array = frame.get("origin",[0,0])
			var x := float(80+((i*83)%1280))-float(origin[0])+float(trim[0])
			var y := float(80+((i*47)%740))-float(origin[1])+float(trim[1])
			draw_texture_rect_region(textures[int(frame.page)],
				Rect2(x,y,rect[2],rect[3]),Rect2(rect[0],rect[1],rect[2],rect[3]))

func _initialize() -> void:
	_run.call_deferred()

func percentile(values: Array, fraction: float) -> float:
	var ordered := values.duplicate()
	ordered.sort()
	return float(ordered[clampi(ceili(ordered.size()*fraction)-1,0,ordered.size()-1)])

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 1:
		printerr("manifest path required")
		quit(2)
		return
	var path: String = args[0]
	var data: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	assert(data is Dictionary)
	DisplayServer.window_set_size(Vector2i(1440,900))
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var canvas := BundleCanvas.new()
	canvas.frames = data.frames
	for page in data.pages:
		var image := Image.new()
		assert(image.load(path.get_base_dir().path_join(page)) == OK)
		canvas.textures.append(ImageTexture.create_from_image(image))
	root.add_child(canvas)
	var times: Array[float] = []
	var draws: Array[int] = []
	var last := Time.get_ticks_usec()
	for i in 200:
		canvas.phase = i
		canvas.queue_redraw()
		await process_frame
		var now := Time.get_ticks_usec()
		if i >= 60:
			times.append((now-last)/1000.0)
			draws.append(int(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last = now
	print(JSON.stringify({
		"manifest":path,"pages":data.pages.size(),"entities":canvas.entities,
		"frame_p50_ms":percentile(times,0.50),"frame_p95_ms":percentile(times,0.95),
		"frame_p99_ms":percentile(times,0.99),"draw_p50":percentile(draws,0.50),
		"draw_p95":percentile(draws,0.95),"renderer":RenderingServer.get_video_adapter_name()
	}))
	quit()
