extends SceneTree

const Store = preload("res://assets_runtime/asset_store.gd")
const Data = preload("res://rendering/terrain_data.gd")
const Draw = preload("res://rendering/terrain.gd")
const Chunks = preload("res://tests/terrain_chunks_candidate.gd")
const VisibleChunks = preload("res://tests/terrain_visible_chunks_candidate.gd")
var failures: Array = []
var store = Store.new()
var view: SubViewport
var renderer: Node2D
var captures: Array = []
var samples: Array = []


func _initialize() -> void:
	DisplayServer.window_set_flag(DisplayServer.WINDOW_FLAG_NO_FOCUS, true)
	root.title = "SGTest terrain validation"
	root.size = Vector2i(320, 180)
	RenderingServer.set_default_clear_color(Color.BLACK)
	_run.call_deferred()


func check(value: bool, message: String) -> void:
	if not value:
		failures.append(message)
		push_error(message)


func frame_at(camera: Vector2) -> void:
	view.canvas_transform = Transform2D(0, -camera)
	check(renderer.set_view_rect(Rect2(camera, Vector2(view.size))) == OK, "View exceeds renderer budget")
	if renderer is VisibleChunks and not renderer is Chunks:
		check(renderer.get_child_count() <= renderer.max_cached_chunks, "Chunk cache exceeds bound")
	await RenderingServer.frame_post_draw


func fixture() -> void:
	var bytes := PackedByteArray()
	bytes.resize(8 + 4 * 2)
	bytes.encode_u32(0, 2)
	bytes.encode_u32(4, 2)
	for index in 4:
		bytes.encode_u16(8 + index * 2, [3, 65535, 5, 3][index])
	var image := Image.create(64, 32, false, Image.FORMAT_RGBA8)
	var texture := ImageTexture.create_from_image(image)
	var asset := {"manifest":{"version":3,"tile_width":2,"tile_height":2,"tile_side":32,"atlas_grid":[2,1],"tile_ids":[3,5]},
		"extra":{"tiles":bytes},"pages":[texture]}
	var data := Data.new()
	check(data.configure(asset) == OK and data.cells == PackedInt32Array([0,-1,1,0]), "Original tile remap/empty cell failed")
	check(data.regions[1] == Rect2(32,0,32,32), "Tile UV remap failed")
	var drawing := Draw.new()
	drawing.configure(data)
	drawing.set_view_rect(Rect2(0,0,64,64))
	drawing.configure(data)
	check(drawing._cells == Rect2i(), "Reconfigured map kept the old visible rectangle")
	drawing.free()
	check(data.visible_cells(Rect2(-128,-128,32,32)).size == Vector2i.ZERO, "Off-map view not empty")
	var corrupt: Dictionary = asset.duplicate(true)
	corrupt.manifest.version = 2
	check(Data.new().configure(corrupt) == ERR_INVALID_DATA, "Legacy world v2 accepted without hierarchy rebuild")
	corrupt = asset.duplicate(true)
	corrupt.extra.tiles.append(0)
	check(Data.new().configure(corrupt) == ERR_INVALID_DATA, "Trailing tile bytes accepted")
	corrupt = asset.duplicate(true)
	corrupt.extra.tiles.encode_u16(8, 4)
	check(Data.new().configure(corrupt) == ERR_INVALID_DATA, "Unknown original tile ID accepted")
	corrupt = asset.duplicate(true)
	corrupt.manifest.tile_ids = [3,3]
	check(Data.new().configure(corrupt) == ERR_INVALID_DATA, "Duplicate original tile ID accepted")


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 3 or args[1] not in ["draw", "chunks", "visible_chunks"]:
		quit(2)
		return
	fixture()
	check(store.open_pack(args[0]) == OK, "PCK/catalog load failed")
	store.asset_failed.connect(func(_owner, _epoch, id, reason): check(false, id + ": " + reason))
	view = SubViewport.new()
	view.world_2d = World2D.new()
	view.disable_3d = true
	view.size = Vector2i(1280, 720)
	view.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(view)
	var preview := TextureRect.new()
	preview.texture = view.get_texture()
	preview.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	preview.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	preview.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	root.add_child(preview)
	preview.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var baseline: Dictionary = {}
	await RenderingServer.frame_post_draw
	baseline = memory()
	for map_id in [15, 13]:
		view.size = Vector2i(1280,720)
		var epoch := store.begin_scope(&"terrain")
		var id := "maps/%d" % map_id
		check(store.acquire(&"terrain", epoch, id) == OK, "Map acquire failed")
		var deadline := Time.get_ticks_msec() + 20000
		while store.get_asset(&"terrain", epoch, id).is_empty() and Time.get_ticks_msec() < deadline:
			store.pump()
			await process_frame
		var asset: Dictionary = store.get_asset(&"terrain", epoch, id)
		if asset.is_empty():
			check(false, "Map resource timeout")
			break
		var data := Data.new()
		var start := Time.get_ticks_usec()
		check(data.configure(asset) == OK, "Invalid original terrain data")
		var data_us := Time.get_ticks_usec() - start
		start = Time.get_ticks_usec()
		renderer = Draw.new() if args[1] == "draw" else (Chunks.new() if args[1] == "chunks" else VisibleChunks.new())
		renderer.configure(data)
		var prepare_us := Time.get_ticks_usec() - start
		view.add_child(renderer)
		var center := Vector2(7872,11296) if map_id == 15 else Vector2(2560,2880)
		if args[1] == "visible_chunks":
			renderer.max_cached_chunks = 1
			check(renderer.set_view_rect(Rect2(center, Vector2(view.size))) == ERR_OUT_OF_MEMORY, "Oversize view not rejected")
			renderer.max_cached_chunks = 32
		var first_start := Time.get_ticks_usec()
		await frame_at(center)
		await frame_at(center)
		var first_view_us := Time.get_ticks_usec() - first_start + prepare_us
		for size in [Vector2i(1280,720), Vector2i(1920,1080)]:
			view.size = size
			var cameras := [Vector2(-16,-16), center, center + Vector2(0.25,0.125), Vector2(15200,15240)]
			for camera_index in cameras.size():
				var camera: Vector2 = cameras[camera_index]
				await frame_at(camera)
				await frame_at(camera)
				var image := view.get_texture().get_image()
				var output := args[2].get_base_dir().path_join("%s_m%d_%dx%d_c%d.png" % [args[1],map_id,size.x,size.y,camera_index])
				check(image.save_png(output) == OK, "Cannot save GPU capture")
				captures.append({"file":output,"map":map_id,"size":[size.x,size.y],"camera":[camera.x,camera.y]})
			# Same fixed trace and workload for both candidates. No FPS cap/snapshot changes.
			var times: Array[int] = []
			var updates: Array[int] = []
			var draw_calls: Array[int] = []
			for frame in 330:
				var camera := center + Vector2(frame * 8.25, sin(frame * 0.035) * 400.0)
				start = Time.get_ticks_usec()
				view.canvas_transform = Transform2D(0, -camera)
				check(renderer.set_view_rect(Rect2(camera, Vector2(size))) == OK, "Trace view exceeds renderer budget")
				var update_us := Time.get_ticks_usec() - start
				await RenderingServer.frame_post_draw
				if frame >= 90:
					times.append(Time.get_ticks_usec() - start)
					updates.append(update_us)
					draw_calls.append(view.get_render_info(Viewport.RENDER_INFO_TYPE_CANVAS, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME))
			times.sort()
			updates.sort()
			draw_calls.sort()
			samples.append({"map":map_id,"size":[size.x,size.y],"frames":times.size(),"data_prepare_us":data_us,
				"renderer_prepare_us":prepare_us,"initial_view_ready_us":first_view_us,"frame_p95_us":times[227],"frame_p99_us":times[237],"frame_max_us":times[-1],
				"update_p95_us":updates[227],"update_p99_us":updates[237],"draw_calls_p95":draw_calls[227],"memory":memory()})
		# Identical distant camera jumps for every candidate, including cache eviction.
		var jumps: Array[int] = []
		for step in 40:
			start = Time.get_ticks_usec()
			await frame_at(Vector2((step * 5 % 14) * 1024, (step * 3 % 14) * 1024))
			jumps.append(Time.get_ticks_usec() - start)
		jumps.sort()
		for sample in samples:
			if sample.map == map_id:
				sample.jump_p95_us = jumps[37]
				sample.jump_p99_us = jumps[39]
		renderer.free()
		renderer = null
		data = null
		asset = {}
		store.end_scope(&"terrain", epoch)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
	check(store.statistics().assets == 0, "Terrain resources still owned")
	view.size = Vector2i(1280,720)
	await RenderingServer.frame_post_draw
	await RenderingServer.frame_post_draw
	var report := {"status":"PASS" if failures.is_empty() else "FAIL", "mode":args[1],"failures":failures,
		"adapter":RenderingServer.get_video_adapter_name(),"driver":RenderingServer.get_current_rendering_driver_name(),
		"captures":captures,"samples":samples,"baseline_memory":baseline,"released_memory":memory(),
		"scope":"Terrain only; no scenery, actors, Y-sort, network or gameplay"}
	var file := FileAccess.open(args[2], FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"\t"))
	print("TERRAIN_RESULT ",JSON.stringify(report))
	quit(0 if failures.is_empty() else 1)


func memory() -> Dictionary:
	return {"texture_bytes":RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TEXTURE_MEM_USED),
		"buffer_bytes":RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_BUFFER_MEM_USED),
		"video_bytes":RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_VIDEO_MEM_USED)}
