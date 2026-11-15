extends SceneTree

const WARMUP := 30

class AtlasData:
	extends RefCounted
	var texture: Texture2D
	var frames: Array = []
	var clip_names: Array = []
	var clip_frames: Array = []
	var fast_rects: Array = []
	var fast_offsets: Array = []

	func load_from(base_path: String) -> Error:
		var manifest_path := base_path.path_join("atlas.json")
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
		if not parsed is Dictionary:
			return ERR_FILE_CORRUPT
		var resource := ResourceLoader.load(base_path.path_join("page_0.png"))
		if resource == null or not resource is Texture2D:
			return ERR_FILE_CANT_READ
		texture = resource as Texture2D
		frames = parsed.get("frames", [])
		var clips: Dictionary = parsed.get("clips", {})
		var keys := clips.keys()
		keys.sort()
		for key in keys:
			var clip_name := str(key)
			clip_names.append(clip_name)
			var packed := PackedInt32Array()
			for frame_id in clips[clip_name]:
				packed.append(int(frame_id))
			clip_frames.append(packed)
		for frame in frames:
			var rect: Array = frame.get("rect", [0,0,0,0])
			var trim: Array = frame.get("trim", [0,0])
			var origin: Array = frame.get("origin", [0,0])
			fast_rects.append(Rect2(float(rect[0]), float(rect[1]), float(rect[2]), float(rect[3])))
			fast_offsets.append(Vector2(float(trim[0])-float(origin[0]), float(trim[1])-float(origin[1])))
		print("atlas_loaded ", base_path, " clips=", clip_frames.size(), " frames=", frames.size(), " texture=", texture.get_width(), "x", texture.get_height())
		return OK

class BenchCanvas extends Node2D:
	var renderer_mode := "current"
	var atlases: Array[AtlasData] = []
	var instances: Array[Dictionary] = []
	var visuals := PackedInt32Array()
	var clips := PackedInt32Array()
	var offsets := PackedInt32Array()
	var positions := PackedVector2Array()
	var phase := 0

	func _draw() -> void:
		if renderer_mode == "fast":
			_draw_fast()
		else:
			_draw_current()

	func _draw_current() -> void:
		for entity in instances:
			var visual := int(entity.visual)
			var atlas := atlases[visual]
			var clip_index := int(entity.clip) % maxi(1, atlas.clip_frames.size())
			var plan: PackedInt32Array = atlas.clip_frames[clip_index]
			if plan.is_empty():
				continue
			var frame_id := int(plan[(phase + int(entity.offset)) % plan.size()])
			var frame: Dictionary = atlas.frames[frame_id]
			var rect: Array = frame.get("rect", [0,0,0,0])
			var trim: Array = frame.get("trim", [0,0])
			var origin: Array = frame.get("origin", [0,0])
			var src := Rect2(float(rect[0]), float(rect[1]), float(rect[2]), float(rect[3]))
			var at := Vector2(float(entity.x), float(entity.y))
			var offset := Vector2(float(trim[0])-float(origin[0]), float(trim[1])-float(origin[1]))
			draw_texture_rect_region(atlas.texture, Rect2(at + offset, src.size), src)

	func _draw_fast() -> void:
		var count := visuals.size()
		for i in count:
			var visual := visuals[i]
			var atlas := atlases[visual]
			var plan: PackedInt32Array = atlas.clip_frames[clips[i]]
			if plan.is_empty():
				continue
			var frame_id := int(plan[(phase + offsets[i]) % plan.size()])
			var src: Rect2 = atlas.fast_rects[frame_id]
			draw_texture_rect_region(atlas.texture, Rect2(positions[i] + atlas.fast_offsets[frame_id], src.size), src)

func _pct(values: Array[float], q: float) -> float:
	if values.is_empty():
		return 0.0
	values.sort()
	return values[int(floor(float(values.size()-1) * q))]

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 8:
		printerr("asset_root mode sample_csv instances frames width height kind [output]")
		quit(2)
		return
	var asset_root := str(args[0])
	var renderer_mode := str(args[1])
	var samples: Array[String] = []
	for raw in str(args[2]).split(",", false):
		samples.append(str(raw))
	var count := int(args[3])
	var frame_count := int(args[4])
	var width := int(args[5])
	var height := int(args[6])
	var kind := str(args[7])
	DisplayServer.window_set_size(Vector2i(width, height))
	root.size = Vector2i(width, height)
	RenderingServer.render_loop_enabled = true
	Engine.max_fps = 0
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)

	var tex_before := int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var video_before := int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var nodes_before := int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var started := Time.get_ticks_usec()
	var canvas := BenchCanvas.new()
	canvas.renderer_mode = renderer_mode
	for sample in samples:
		var atlas := AtlasData.new()
		var err := atlas.load_from(asset_root.path_join(sample))
		if err != OK:
			printerr("atlas load failed "+sample+" err="+str(err))
			quit(3)
			return
		canvas.atlases.append(atlas)
	var load_ms := float(Time.get_ticks_usec() - started) / 1000.0

	for i in count:
		var visual := i % samples.size()
		var atlas: AtlasData = canvas.atlases[visual]
		var clip_index := (i * 7) % maxi(1, atlas.clip_frames.size())
		var phase_offset := (i * 11) % 13
		var pos := Vector2(24 + ((i * 83) % maxi(100, width - 48)), 32 + ((i * 47) % maxi(80, height - 64)))
		canvas.instances.append({"visual":visual, "clip":clip_index, "offset":phase_offset, "x":pos.x, "y":pos.y})
		canvas.visuals.append(visual)
		canvas.clips.append(clip_index)
		canvas.offsets.append(phase_offset)
		canvas.positions.append(pos)

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

	var result := {
		"renderer_path":"sgtest_bptc_"+renderer_mode,
		"kind":kind,
		"assets":samples,
		"instances":count,
		"frames":frame_count,
		"load_ms":load_ms,
		"frame_p50_ms":_pct(intervals,.5),
		"frame_p95_ms":_pct(intervals,.95),
		"frame_p99_ms":_pct(intervals,.99),
		"draw_calls_p50":_pct(draws,.5),
		"draw_calls_p95":_pct(draws,.95),
		"texture_mem_delta":maxi(0,tex_after-tex_before),
		"video_mem_delta":maxi(0,video_after-video_before),
		"node_delta":maxi(0,nodes_after-nodes_before),
		"adapter":RenderingServer.get_video_adapter_name()
	}
	var output := JSON.stringify(result)
	print(output)
	if args.size() >= 9:
		var f := FileAccess.open(str(args[8]), FileAccess.WRITE)
		if f != null:
			f.store_string(output+"\n")
			f.close()
	quit(0)
