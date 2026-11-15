extends SceneTree

const Store = preload("res://assets_runtime/asset_store.gd")
const Order = preload("res://rendering/world_order.gd")
const List = preload("res://rendering/world_draw_list.gd")
const Canvas = preload("res://rendering/world_canvas.gd")
const Sprites = preload("res://tests/world_sprites_candidate.gd")
const TerrainData = preload("res://rendering/terrain_data.gd")
const Terrain = preload("res://rendering/terrain.gd")
var store = Store.new()
var failures: Array = []
var captures: Array = []
var pages := {}
var texture_sources := {}
var metrics: Array = []
var output := ""


func _initialize() -> void:
	DisplayServer.window_set_flag(DisplayServer.WINDOW_FLAG_NO_FOCUS, true)
	root.title = "SGTest world rendering validation"
	root.size = Vector2i(320,180)
	RenderingServer.set_default_clear_color(Color.BLACK)
	_run.call_deferred()


func check(condition: bool, message: String) -> void:
	if not condition:
		failures.append(message)
		push_error(message)


func fixture() -> void:
	for x in range(-40,41):
		var boundary := -minf(absf(x),20.0)/2.0
		for y in range(-32,4):
			check(Order.point_in_front(Vector2(100,200),[-20,-10,20,-10],Vector2(100+x,200+y)) == (y > boundary), "Front boundary mismatch")
	var building := {"p":Vector2.ZERO,"sorting_mode":0,"front":[-20,-10,20,-10]}
	for point in [Vector2(0,0),Vector2(0,1),Vector2(-20,-10),Vector2(-20,-9),Vector2(20,-10),Vector2(20,-9)]:
		var actor := {"p":point,"sorting_mode":1}
		var rows: Array = [building]
		Order.insert_record(rows,actor)
		check(rows[0] == (building if Order.point_in_front(Vector2.ZERO,building.front,point) else actor), "Actor insertion mismatch")
	var rows: Array = [{"p":Vector2(10,5),"sorting_mode":1,"id":"first"}]
	Order.insert_record(rows,{"p":Vector2(0,5),"sorting_mode":1,"id":"second"})
	check(rows[0].id == "first", "Same-Y order changed")
	var a := {"p":Vector2(-20,-10),"sorting_mode":0,"front":[-30,-5,30,-5]}
	var b := {"p":Vector2.ZERO,"sorting_mode":0,"front":[-30,-20,30,-5]}
	check(Order.swap_required(a,b) and Order.swap_required(b,a), "Non-total predicate lost")
	var empty := {"manifest":{"clips":{"EMPTY":[0]},"frames":[{"page":-1.0,"rect":[0.0,0.0,0.0,0.0]}]},"pages":[]}
	check(List._valid_clip(empty,"EMPTY"),"Empty source frame rejected")
	empty.manifest.frames[0].rect = [0,0,1,1]
	check(not List._valid_clip(empty,"EMPTY"),"Nonempty frame with missing page accepted")


func load_ids(owner: StringName, epoch: int, ids: Array) -> Dictionary:
	for id in ids:
		check(store.acquire(owner,epoch,id) == OK,"Acquire failed: "+id)
	var deadline := Time.get_ticks_msec()+30000
	while Time.get_ticks_msec() < deadline:
		store.pump()
		var all_ready := true
		for id in ids:
			if store.get_asset(owner,epoch,id).is_empty():
				all_ready = false
		if all_ready:
			var result := {}
			for id in ids:
				result[id] = store.get_asset(owner,epoch,id)
			return result
		await process_frame
	check(false,"Assets timed out")
	return {}


func save_commands(commands: Array) -> Array:
	var trace: Array = []
	for command in commands:
		var id := str(command.texture.get_instance_id())
		if not pages.has(id):
			var file := output.path_join("page_"+id+".png")
			var image: Image = command.texture.get_image()
			if image.is_compressed():
				check(image.decompress() == OK,"Page decompression failed")
			check(image.save_png(file) == OK,"Page reference save failed")
			pages[id] = file
		trace.append({"index":command.index,"slot":command.slot,"frame":command.frame,"actor_id":command.actor_id,
			"texture_size":[command.texture.get_width(),command.texture.get_height()],
			"page":pages[id],"texture_source":texture_sources[id],"destination":[command.destination.position.x,command.destination.position.y,command.destination.size.x,command.destination.size.y],
			"uv":[command.uv.position.x,command.uv.position.y,command.uv.size.x,command.uv.size.y]})
	return trace


func render_memory() -> Dictionary:
	return {"texture":RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TEXTURE_MEM_USED),
		"buffer":RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_BUFFER_MEM_USED)}


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 3 or args[1] not in ["canvas","sprites"]:
		quit(2)
		return
	output = args[2]
	fixture()
	check(store.open_pack(args[0]) == OK,"Pack load failed")
	store.asset_failed.connect(func(_owner,_epoch,id,reason):check(false,id+": "+reason))
	var view := SubViewport.new()
	view.world_2d = World2D.new()
	view.disable_3d = true
	view.size = Vector2i(1280,720)
	view.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(view)
	var preview := TextureRect.new()
	preview.texture = view.get_texture()
	preview.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	preview.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	root.add_child(preview)
	preview.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for map_id in [15,13]:
		view.size = Vector2i(1280,720)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		var baseline := render_memory()
		var epoch := store.begin_scope(&"world")
		var map_key := "maps/%d" % map_id
		var initial: Dictionary = await load_ids(&"world",epoch,[map_key])
		if initial.is_empty():
			break
		var map_asset: Dictionary = initial[map_key]
		var world: Dictionary = map_asset.manifest
		var ids := {"npcs/npc001":true}
		for definition in world.scenery_definitions.values():
			for appearance in definition.appearances:
				if appearance != null:
					ids[str(appearance.atlas).trim_suffix("/atlas.json")] = true
		var assets: Dictionary = await load_ids(&"world",epoch,ids.keys())
		for id in assets:
			for page_index in assets[id].pages.size():
				texture_sources[str(assets[id].pages[page_index].get_instance_id())] = [id,page_index]
		var list := List.new()
		var invalid: Dictionary = world.duplicate(true)
		invalid.scenery_order.groups[1].append(invalid.scenery_order.groups[1][0])
		check(list.configure(invalid,assets) == ERR_INVALID_DATA,"Duplicate root order accepted")
		var configure_begin := Time.get_ticks_usec()
		check(list.configure(world,assets) == OK,"World configuration failed")
		var configure_us := Time.get_ticks_usec()-configure_begin
		var data := TerrainData.new()
		check(data.configure(map_asset) == OK,"Terrain configuration failed")
		var terrain := Terrain.new()
		terrain.configure(data)
		view.add_child(terrain)
		var renderer: Node2D = Canvas.new() if args[1] == "canvas" else Sprites.new()
		view.add_child(renderer)
		var cameras: Array = [Vector2(8200,8500),Vector2(10500,9900),Vector2(12500,10400)] if map_id == 15 else [Vector2(5120,2400),Vector2(3200,3840),Vector2(5760,3840)]
		var first_view_us := 0
		var preparations: Array = []
		var samples: Array = []
		var jumps: Array = []
		for size in [Vector2i(1280,720),Vector2i(1920,1080)]:
			view.size = size
			for camera in cameras:
				for inside_mode in [false,true]:
					var inside := {}
					if inside_mode:
						for index in world.objects.size():
							if int(world.objects[index].child_count) > 0:
								inside[index] = true
					var actors := [{"id":"fixture_actor","p":camera+Vector2(640,420),"asset":"npcs/npc001","clip":"NPC001","frame":0}]
					var frames := {}
					if inside_mode:
						for index in world.objects.size():
							if int(world.objects[index].frames) > 1:
								frames[index] = 1
					var first_begin := Time.get_ticks_usec()
					var commands: Array = list.build(Rect2(camera,Vector2(size)),actors,inside,frames)
					check(list.error == OK,"Draw list failed")
					view.canvas_transform = Transform2D(0,-camera)
					terrain.set_view_rect(Rect2(camera,Vector2(size)))
					renderer.present(commands)
					await RenderingServer.frame_post_draw
					await RenderingServer.frame_post_draw
					if first_view_us == 0:
						first_view_us = Time.get_ticks_usec()-first_begin
					var file := output.path_join("capture_%d.png" % captures.size())
					view.get_texture().get_image().save_png(file)
					captures.append({"map":map_id,"camera":[camera.x,camera.y],"size":[size.x,size.y],"inside":inside_mode,
						"actors":[{"id":"fixture_actor","p":[actors[0].p.x,actors[0].p.y],"asset":"npcs/npc001","clip":"NPC001","frame":0}],
						"commands":save_commands(commands),"file":file})
					commands = []
			# Same camera trace: preparation plus rendering, reused Sprite2D nodes.
			for step in 180:
				var camera: Vector2 = cameras[(step / 3) % cameras.size()] + Vector2(step % 3,0)
				var begin := Time.get_ticks_usec()
				var commands: Array = list.build(Rect2(camera,Vector2(size)))
				view.canvas_transform = Transform2D(0,-camera)
				terrain.set_view_rect(Rect2(camera,Vector2(size)))
				renderer.present(commands)
				var prepared := Time.get_ticks_usec()-begin
				await RenderingServer.frame_post_draw
				var elapsed := Time.get_ticks_usec()-begin
				if step >= 60:
					preparations.append(prepared)
					samples.append(elapsed)
					if step % 3 == 0:
						jumps.append(elapsed)
		metrics.append({"map":map_id,"prepare_us":preparations,"frame_us":samples,"jump_us":jumps,
			"configure_us":configure_us,"first_view_us":first_view_us,
			"render_memory":render_memory(),"baseline_memory":baseline,
			"draw_calls":RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TOTAL_DRAW_CALLS_IN_FRAME)})
		renderer.clear()
		renderer.free()
		terrain.free()
		data = null
		list.clear()
		assets = {}
		initial = {}
		map_asset = {}
		world = {}
		store.end_scope(&"world",epoch)
		var deadline := Time.get_ticks_msec()+30000
		while store.statistics().decode_jobs + store.statistics().texture_jobs > 0 and Time.get_ticks_msec() < deadline:
			store.pump()
			await process_frame
		check(store.statistics().assets == 0 and store.statistics().rgba_bytes == 0,"Scope did not return to baseline")
		view.size = Vector2i(1280,720)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		var released := render_memory()
		metrics[-1].released_memory = released
		check(released.texture == baseline.texture,"Texture counter did not return to viewport baseline")
	preview.free()
	view.free()
	var file := FileAccess.open(output.path_join("result.json"),FileAccess.WRITE)
	file.store_string(JSON.stringify({"failures":failures,"captures":captures,"metrics":metrics,"final":store.statistics()}))
	file.close()
	quit(0 if failures.is_empty() else 1)
