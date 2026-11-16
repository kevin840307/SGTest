extends SceneTree

const Store = preload("res://assets_runtime/asset_store.gd")
const Order = preload("res://rendering/world_order.gd")
const List = preload("res://rendering/world_draw_list.gd")
const Canvas = preload("res://rendering/world_canvas.gd")
const Sprites = preload("res://tests/world_sprites_candidate.gd")
const TerrainData = preload("res://rendering/terrain_data.gd")
const Terrain = preload("res://rendering/terrain.gd")
const Timeline = preload("res://rendering/action_timeline.gd")
const Fade = preload("res://rendering/building_fade.gd")
const VisibilityFixture = preload("res://tests/building_visibility_fixture.gd")
const Visibility = preload("res://rendering/building_visibility.gd")
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
	VisibilityFixture.run(check)
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
	var timeline := Timeline.new()
	var definition := {"schema":"sgtest.action_timeline/v1","fixed_hz":50,"default_speed":25,"numerator":40,
		"loops":true,"frames":[{"index":1,"raw_time":6},{"index":10,"raw_time":300}]}
	check(timeline.configure(definition) == OK,"Timeline configuration failed")
	var step := 0
	var remaining := 9
	for tick in 2000:
		check(timeline.frame_at_update(tick) == (1 if step == 0 else 10),"Fixed-update counter mismatch")
		remaining -= 1
		if remaining == 0:
			step = 1 - step
			remaining = 9 if step == 0 else 480
	for fps in [30,60,120,0]:
		if fps > 0:
			for sample in fps * 10:
				timeline.frame_at_update(int(sample * 50.0 / fps))
		for tick in [0,8,9,488,489,1000000000000]:
			check(timeline.frame_at_update(tick) == (1 if tick % 489 < 9 else 10),"FPS/AFK changed timeline")
	definition.loops = false
	definition.frames = [{"index":0,"raw_time":0},{"index":4,"raw_time":1}]
	check(timeline.configure(definition) == OK,"Nonloop action rejected")
	check(timeline.frame_at_update(0) == 0 and timeline.frame_at_update(1) == 4 and timeline.frame_at_update(100) == 4,"Minimum tick/end frame incorrect")
	check(not timeline.is_complete(1) and timeline.is_complete(2),"Action completion boundary incorrect")
	definition.frames[0].raw_time = 0.5
	check(timeline.configure(definition) == ERR_INVALID_DATA and timeline.frame_at_update(0) == -1,"Malformed action retained previous timeline")
	definition.frames[0].raw_time = 1
	definition.default_speed = 50
	check(timeline.configure(definition) == ERR_INVALID_DATA,"Unverified speed override accepted")
	for updates in [-1,0,1,8,15,16,17]:
		var remaining_alpha := 256 - 16 * clampi(updates,0,16)
		var entering := Fade.layers(true,updates)
		var leaving := Fade.layers(false,updates)
		check(entering.primary_alpha == 255 and entering.alternate_alpha == mini(remaining_alpha,255) and entering.children,"Inside fade incorrect")
		check(leaving.primary_alpha == mini(remaining_alpha,255) and leaving.alternate_alpha == mini(256-remaining_alpha,255) and leaving.children == (remaining_alpha>0),"Outside fade incorrect")
	# The same asset can be referenced as a building or static object. Only the
	# static object's original ctor initializes 'action'; do not animate BLGs.
	definition.default_speed = 25
	definition.loops = true
	definition.frames = [{"index":1,"raw_time":1}]
	var image := Image.create(2,1,false,Image.FORMAT_RGBA8)
	var texture := ImageTexture.create_from_image(image)
	var fixture_asset := {"manifest":{"clips":{"TEST":[0,1]},"act":{"TEST":definition},"frames":[
		{"page":0,"rect":[0,0,1,1],"trim":[0,0],"origin":[0,0]},
		{"page":0,"rect":[1,0,1,1],"trim":[0,0],"origin":[0,0]}]},"pages":[texture]}
	var appearance := {"atlas":"fixture/atlas.json","clip":"TEST"}
	var world := {"scenery_schema":"sgtest.scenery/v1","scenery_order":{"version":1,"groups":[[],[0,1],[]]},
		"scenery_definitions":{"b":{"sorting_mode":0,"draw_group":1,"front":[-1,-1,1,-1],"appearances":[appearance]},
			"s":{"sorting_mode":1,"draw_group":1,"appearances":[appearance]}},"objects":[
		{"definition":"b","parent_index":null,"child_count":0,"p":[0,0],"bounds":[0,0,1,1]},
		{"definition":"s","parent_index":null,"child_count":0,"p":[1,1],"bounds":[1,1,1,1]}]}
	var list := List.new()
	check(list.configure(world,{"fixture":fixture_asset}) == OK,"Shared asset type fixture failed")
	var poses := list.frames_at_update(0)
	check(poses.size() == 1 and poses.get(1) == 1 and not poses.has(0),"Building incorrectly inherited static action")
	list.clear()


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
		trace.append({"index":command.index,"slot":command.slot,"frame":command.frame,"actor_id":command.actor_id,"alpha":command.alpha,
			"texture_size":[command.texture.get_width(),command.texture.get_height()],
			"page":pages[id],"texture_source":texture_sources[id],"destination":[command.destination.position.x,command.destination.position.y,command.destination.size.x,command.destination.size.y],
			"uv":[command.uv.position.x,command.uv.position.y,command.uv.size.x,command.uv.size.y]})
	return trace


func render_memory() -> Dictionary:
	return {"texture":RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TEXTURE_MEM_USED),
		"buffer":RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_BUFFER_MEM_USED)}


func interior_focus(building: Dictionary) -> Vector2i:
	var f: Array = building.front
	var upper := minf(float(f[1])-float(f[0])/2.0,float(f[3])+float(f[2])/2.0)
	var lower := maxf(float(f[1])+float(f[0])/2.0,float(f[3])-float(f[2])/2.0)
	var point: Vector2i = building.p + Vector2i(0,int(floor((lower+upper)/2.0)))
	check(Visibility.entrance(building.p,f,point),"Real building interior sample invalid")
	return point


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() not in [3,4] or args[1] not in ["canvas","sprites"]:
		quit(2)
		return
	output = args[2]
	var timing := args.size() == 4 and args[3] == "timing"
	var focus_mode := args.size() == 4 and args[3] == "focus"
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
		check(list.configure(world,assets,epoch) == OK,"World configuration failed")
		var configure_us := Time.get_ticks_usec()-configure_begin
		if timing:
			check(not list._timelines.is_empty(),"No real scenery ACT was loaded")
			var initial_poses := list.frames_at_update(0)
			var first_index: int = initial_poses.keys()[0]
			var phases := {first_index:9}
			check(list.frames_at_update(9,phases)[first_index] == initial_poses[first_index],"Per-placement start update ignored")
		var data := TerrainData.new()
		check(data.configure(map_asset) == OK,"Terrain configuration failed")
		var terrain := Terrain.new()
		terrain.configure(data)
		view.add_child(terrain)
		var renderer: Node2D = Canvas.new() if args[1] == "canvas" else Sprites.new()
		view.add_child(renderer)
		var cameras: Array = [Vector2(8200,8500),Vector2(10500,9900),Vector2(12500,10400)] if map_id == 15 else [Vector2(5120,2400),Vector2(3200,3840),Vector2(5760,3840)]
		if timing:
			for placement in world.objects:
				var suffix := "a_fly_blackbload_l" if map_id == 15 else "fish01"
				if str(placement.atlas).get_base_dir().ends_with("/"+suffix):
					cameras[2] = Vector2(placement.p[0],placement.p[1])-Vector2(640,420)
					break
		var first_view_us := 0
		var preparations: Array = []
		var samples: Array = []
		var jumps: Array = []
		for size in [Vector2i(1280,720),Vector2i(1920,1080)]:
			view.size = size
			var camera_index := 0
			for camera in cameras:
				var visibility := Visibility.new()
				var focus_inputs: Array = []
				var inside_focus := Vector2i(camera+Vector2(size)/2.0)
				var outside_focus := inside_focus
				if focus_mode:
					check(visibility.configure(world,epoch) == OK,"Focus state configure failed")
					var eligible: Array = visibility._buildings.keys()
					if not eligible.is_empty():
						var chosen: int = eligible[camera_index % eligible.size()]
						if camera_index == 2:
							for index in eligible:
								if int(world.objects[index].child_count) > 0:
									chosen = index
									break
						var building: Dictionary = visibility._buildings[chosen]
						inside_focus = interior_focus(building)
						outside_focus = building.p + Vector2i(0,100)
				camera_index += 1
				var phases: Array = [{"inside":false,"updates":-1,"elapsed":0},{"inside":true,"updates":-1,"elapsed":0}]
				if timing:
					phases = [{"inside":false,"updates":0,"elapsed":0},{"inside":true,"updates":1,"elapsed":8},
						{"inside":true,"updates":8,"elapsed":9},{"inside":true,"updates":16,"elapsed":81},
						{"inside":false,"updates":1,"elapsed":560},{"inside":false,"updates":8,"elapsed":561},
						{"inside":false,"updates":16,"elapsed":1000000}]
				if focus_mode:
					phases = [{"inside":false,"elapsed":0,"updates":-1},{"inside":true,"elapsed":1,"updates":-1},
						{"inside":true,"elapsed":8,"updates":-1},{"inside":true,"elapsed":16,"updates":-1},
						{"inside":false,"elapsed":17,"updates":-1},{"inside":false,"elapsed":24,"updates":-1},
						{"inside":false,"elapsed":32,"updates":-1}]
				for phase in phases:
					var inside_mode: bool = phase.inside
					var sample_camera: Vector2 = camera
					var focus := inside_focus if inside_mode else outside_focus
					if focus_mode:
						sample_camera = Vector2(focus) - Vector2(size)/2.0
					var inside := {}
					if inside_mode:
						for index in world.objects.size():
							if int(world.objects[index].child_count) > 0:
								inside[index] = true
					var actors := [{"id":"fixture_actor","p":Vector2(focus) if focus_mode else camera+Vector2(640,420),"asset":"npcs/npc001","clip":"NPC001","frame":0}]
					if focus_mode:
						actors.append({"id":"outside_actor","p":Vector2(outside_focus)+Vector2(30,0),"asset":"npcs/npc001","clip":"NPC001","frame":0})
					var frames := {}
					if timing or focus_mode:
						frames = list.frames_at_update(int(phase.elapsed)+(1 if focus_mode else 0))
					var transitions := {}
					for index in world.objects.size():
						var definition: Dictionary = world.scenery_definitions[world.objects[index].definition]
						if not timing and not focus_mode and inside_mode and int(world.objects[index].frames) > 1:
							frames[index] = 1
						if timing and world.objects[index].parent_index == null and definition.sorting_mode == 0 and definition.appearances.size() > 1 and definition.appearances[1] != null and definition.appearances[0].source != definition.appearances[1].source:
							transitions[index] = {"inside":inside_mode,"updates":phase.updates}
					var first_begin := Time.get_ticks_usec()
					var focus_snapshot := {}
					var commands: Array
					if focus_mode:
						for tick in range(visibility.snapshot().update+1,int(phase.elapsed)+1):
							check(visibility.advance(epoch,tick,focus,list.visible_main_indices(Rect2(sample_camera,Vector2(size)))) == OK,"Focus trace step failed")
							focus_inputs.append({"update":tick,"focus":[focus.x,focus.y],"camera":[sample_camera.x,sample_camera.y]})
						focus_snapshot = visibility.snapshot()
						var stale := focus_snapshot.duplicate(true)
						stale.epoch = epoch-1
						check(list.build_focus(Rect2(sample_camera,Vector2(size)),actors,stale,int(phase.elapsed),frames).is_empty() and list.error == ERR_INVALID_PARAMETER,"Old map focus snapshot accepted")
						check(list.build_focus(Rect2(sample_camera,Vector2(size)),actors,focus_snapshot,int(phase.elapsed)+1,frames).is_empty() and list.error == ERR_INVALID_PARAMETER,"Old update focus snapshot accepted")
						commands = list.build_focus(Rect2(sample_camera,Vector2(size)),actors,focus_snapshot,int(phase.elapsed),frames)
					else:
						commands = list.build(Rect2(camera,Vector2(size)),actors,inside,frames,transitions)
					check(list.error == OK,"Draw list failed")
					view.canvas_transform = Transform2D(0,-sample_camera)
					terrain.set_view_rect(Rect2(sample_camera,Vector2(size)))
					renderer.present(commands)
					await RenderingServer.frame_post_draw
					await RenderingServer.frame_post_draw
					if first_view_us == 0:
						first_view_us = Time.get_ticks_usec()-first_begin
					var file := output.path_join("capture_%d.png" % captures.size())
					view.get_texture().get_image().save_png(file)
					var actor_trace: Array = []
					for actor in actors:
						actor_trace.append({"id":actor.id,"p":[actor.p.x,actor.p.y],"asset":actor.asset,"clip":actor.clip,"frame":actor.frame})
					captures.append({"map":map_id,"camera":[sample_camera.x,sample_camera.y],"size":[size.x,size.y],"inside":inside_mode,
						"timing":timing,"focus_mode":focus_mode,"focus_state":focus_snapshot,"focus_inputs":focus_inputs.duplicate(true),
						"fade_updates":phase.updates,"elapsed_updates":int(phase.elapsed)+(1 if focus_mode else 0),
						"sampled_frames":frames,
						"actors":actor_trace,
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
			"scenery_act_assets":list._timelines.size(),
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
