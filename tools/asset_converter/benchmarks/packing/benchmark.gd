extends SceneTree

# Fair benchmark: Map13 terrain/scenery is a fixed, independent asset set.
# Only monster atlas mode changes between processes.
const IDS_12 := ["EN114A","EN089A","EN121A","EN116A","EN023A","EN084A",
	"EN111A","EN125A","EN050C","EN080A","EN093A","EN079A"]
const CAMERA := Vector2(5000,2000)
const WARMUP := 60

class BenchCanvas extends Node2D:
	var width := 1440
	var height := 900
	var camera := CAMERA
	var terrain: Texture2D
	var terrain_grid := Vector2i()
	var tile_side := 32
	var tile_width := 0
	var tile_height := 0
	var tile_ids: Array = []
	var tile_remap: Dictionary = {}
	var tiles := PackedByteArray()
	var scenery_visuals: Dictionary = {}
	var scenery_objects: Array = []
	var monster_visuals: Array[Dictionary] = []
	var monsters: Array[Dictionary] = []
	var render_cpu_us := 0
	var render_count := 0
	var phase := 0

	func _draw() -> void:
		var t0 := Time.get_ticks_usec()
		_draw_terrain()
		_draw_scenery()
		_draw_monsters()
		render_cpu_us += Time.get_ticks_usec()-t0
		render_count += 1

	func _draw_terrain() -> void:
		if terrain == null or tiles.is_empty():
			return
		var first_x := clampi(floori((camera.x-width*0.5)/tile_side)-1,0,tile_width)
		var first_y := clampi(floori((camera.y-height*0.5)/tile_side)-1,0,tile_height)
		var last_x := clampi(ceili((camera.x+width*0.5)/tile_side)+1,first_x,tile_width)
		var last_y := clampi(ceili((camera.y+height*0.5)/tile_side)+1,first_y,tile_height)
		for y in range(first_y,last_y):
			for x in range(first_x,last_x):
				var tile := tiles.decode_u16(8+(y*tile_width+x)*2)
				if not tile_remap.has(tile):
					continue
				var atlas_index: int = int(tile_remap[tile])
				var sx := (atlas_index%terrain_grid.x)*tile_side
				var sy := (atlas_index/terrain_grid.x)*tile_side
				var screen := Vector2(x*tile_side,y*tile_side)-camera+Vector2(width,height)*0.5
				draw_texture_rect_region(terrain,Rect2(screen,Vector2(tile_side,tile_side)),
					Rect2(sx,sy,tile_side,tile_side))

	func _draw_scenery() -> void:
		for object in scenery_objects:
			var visual: Dictionary = scenery_visuals[str(object.atlas)]
			var indices: Array = visual.clips.get(str(object.clip),[])
			if indices.is_empty():
				continue
			var frame_index: int = int(indices[(phase/8)%indices.size()])
			var frame: Dictionary = visual.frames[frame_index]
			if int(frame.page)<0:
				continue
			var rect: Array = frame.rect
			var origin: Array = frame.origin
			var trim: Array = frame.trim
			var screen := Vector2(object.p[0],object.p[1])-camera+Vector2(width,height)*0.5
			var dst := Rect2(screen-Vector2(origin[0],origin[1])+Vector2(trim[0],trim[1]),
				Vector2(rect[2],rect[3]))
			draw_texture_rect_region(visual.textures[int(frame.page)],dst,
				Rect2(rect[0],rect[1],rect[2],rect[3]))

	func _draw_monsters() -> void:
		for entity in monsters:
			var visual: Dictionary = monster_visuals[int(entity.visual_index)]
			var group: Array = visual.groups[int(entity.group_index)]
			var step: int = (phase+int(entity.frame_offset))/int(entity.frame_stride)
			var frame_index: int = int(group[step%group.size()])
			var frame: Dictionary = visual.frames[frame_index]
			if int(frame.page)<0:
				continue
			var rect: Array = frame.rect
			var crop: Array = frame.crop
			var origin: Array = frame.get("raw_origin",frame.get("origin",[0,0]))
			var pos := Vector2(entity.x,entity.y)
			var dst := Rect2(pos-Vector2(origin[0],origin[1])+Vector2(crop[0],crop[1]),
				Vector2(rect[2],rect[3]))
			draw_texture_rect_region(visual.textures[int(frame.page)],dst,
				Rect2(rect[0],rect[1],rect[2],rect[3]))

func _percentile(values: Array[float], fraction: float) -> float:
	if values.is_empty():
		return 0.0
	values.sort()
	return values[int(floor(float(values.size()-1)*fraction))]

func _load_image(path: String, metrics: Dictionary) -> Texture2D:
	var image := Image.new()
	if image.load(path)!=OK:
		push_error("Image load failed: "+path)
		quit(2)
		return null
	metrics.rgba += image.get_width()*image.get_height()*4
	metrics.png += FileAccess.get_file_as_bytes(path).size()
	metrics.pages += 1
	return ImageTexture.create_from_image(image)

func _load_map(base: String, canvas: BenchCanvas) -> Dictionary:
	var metrics := {"pages":0,"rgba":0,"png":0,"visuals":0,"objects":0}
	var started := Time.get_ticks_usec()
	var map_root := base.path_join("maps/13")
	var world: Variant = JSON.parse_string(FileAccess.get_file_as_string(map_root.path_join("world.json")))
	if not world is Dictionary or world.has("sprite_bundle"):
		push_error("Map13 must use independent scenery assets")
		quit(2)
		return {}
	canvas.tile_side = int(world.tile_side)
	canvas.tile_width = int(world.tile_width)
	canvas.tile_height = int(world.tile_height)
	canvas.terrain_grid = Vector2i(int(world.atlas_grid[0]),int(world.atlas_grid[1]))
	canvas.tile_ids = world.tile_ids
	for index in canvas.tile_ids.size():
		canvas.tile_remap[int(canvas.tile_ids[index])] = index
	canvas.tiles = FileAccess.get_file_as_bytes(map_root.path_join("tiles.bin"))
	canvas.terrain = _load_image(map_root.path_join("terrain.png"),metrics)
	var view := Rect2(CAMERA-Vector2(canvas.width,canvas.height)*0.5,Vector2(canvas.width,canvas.height)).grow(220)
	var required: Dictionary = {}
	for raw in world.objects:
		var bounds := Rect2(raw.bounds[0],raw.bounds[1],raw.bounds[2],raw.bounds[3])
		if not bounds.intersects(view):
			continue
		var relative := str(raw.atlas)
		if not relative.begins_with("maps/visuals/") or relative.begins_with("Enemy/"):
			push_error("Map object crosses asset boundary: "+relative)
			quit(2)
			return {}
		canvas.scenery_objects.append(raw)
		required[relative] = true
	var paths: Array = required.keys()
	paths.sort()
	for relative_value in paths:
		var relative := str(relative_value)
		var manifest_path := base.path_join(relative)
		var data: Variant = JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
		if not data is Dictionary:
			push_error("Invalid scenery manifest: "+manifest_path)
			quit(2)
			return {}
		var textures: Array[Texture2D] = []
		for page in data.pages:
			textures.append(_load_image(manifest_path.get_base_dir().path_join(str(page)),metrics))
		canvas.scenery_visuals[relative] = {
			"textures":textures,"frames":data.frames,"clips":data.clips
		}
	metrics.visuals = paths.size()
	metrics.objects = canvas.scenery_objects.size()
	metrics.load_ms = float(Time.get_ticks_usec()-started)/1000.0
	return metrics

func _load_monsters(root_path: String, ids: Array, canvas: BenchCanvas) -> Dictionary:
	var metrics := {"pages":0,"rgba":0,"png":0,"visuals":0}
	var started := Time.get_ticks_usec()
	for monster_id in ids:
		var manifest := root_path.path_join("Enemy").path_join(monster_id).path_join("group.atlas.json")
		if not FileAccess.file_exists(manifest):
			manifest = root_path.path_join("enemy").path_join(monster_id.to_lower()).path_join("atlas.json")
		if not FileAccess.file_exists(manifest):
			push_error("Missing independent monster manifest: "+manifest)
			quit(2)
			return {}
		var data: Variant = JSON.parse_string(FileAccess.get_file_as_string(manifest))
		var textures: Array[Texture2D] = []
		for page in data.pages:
			textures.append(_load_image(manifest.get_base_dir().path_join(str(page)),metrics))
		var groups_by_source: Dictionary = {}
		for frame_index in data.frames.size():
			var frame: Dictionary = data.frames[frame_index]
			if int(frame.page)<0:
				continue
			var source := str(frame.get("source","clip"+str(frame_index)))
			if not groups_by_source.has(source):
				groups_by_source[source] = []
			groups_by_source[source].append(frame_index)
		var source_names: Array = groups_by_source.keys()
		source_names.sort()
		var groups: Array = []
		for source in source_names:
			groups.append(groups_by_source[source])
		canvas.monster_visuals.append({
			"id":monster_id,"textures":textures,"frames":data.frames,
			"groups":groups,"sources":source_names
		})
	metrics.visuals = canvas.monster_visuals.size()
	metrics.load_ms = float(Time.get_ticks_usec()-started)/1000.0
	return metrics

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()<7:
		printerr("Usage: mode_root map_assets width height kinds instances frames [output]")
		quit(2)
		return
	var mode_root := str(args[0])
	var map_assets := str(args[1])
	var width := int(args[2])
	var height := int(args[3])
	var kinds := int(args[4])
	var count := int(args[5])
	var frame_count := int(args[6])
	if kinds!=2 and kinds!=12:
		push_error("This benchmark intentionally supports only 2 or 12 monster kinds")
		quit(2)
		return
	var selected: Array = IDS_12.slice(0,kinds)
	DisplayServer.window_set_size(Vector2i(width,height))
	root.size = Vector2i(width,height)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var canvas := BenchCanvas.new()
	canvas.width = width
	canvas.height = height
	var map_metrics := _load_map(map_assets,canvas)
	var monster_metrics := _load_monsters(mode_root,selected,canvas)
	if map_metrics.is_empty() or monster_metrics.is_empty():
		return
	for index in count:
		var visual_index := index%canvas.monster_visuals.size()
		var group_count: int = canvas.monster_visuals[visual_index].groups.size()
		canvas.monsters.append({
			"visual_index":visual_index,
			"group_index":(index*7+3)%group_count,
			"frame_offset":(index*11)%13,
			"frame_stride":3+(index*17)%8,
			"change_after":4+(index*23)%29,
			"seed":index*1627+991,
			"x":float(35+((index*83)%max(100,width-70))),
			"y":float(55+((index*47)%max(80,height-80)))
		})
	canvas.monsters.sort_custom(func(a: Dictionary,b: Dictionary)->bool:return float(a.y)<float(b.y))
	root.add_child(canvas)
	var intervals: Array[float] = []
	var draws: Array[float] = []
	var updates: Array[float] = []
	var last := Time.get_ticks_usec()
	var changes := 0
	var seen: Dictionary = {}
	for frame_id in frame_count:
		var update_start := Time.get_ticks_usec()
		for index in canvas.monsters.size():
			var entity: Dictionary = canvas.monsters[index]
			var visual: Dictionary = canvas.monster_visuals[int(entity.visual_index)]
			if frame_id>=int(entity.change_after):
				var seed: int = (int(entity.seed)*1103515245+12345)&0x7fffffff
				entity.seed = seed
				entity.group_index = seed%visual.groups.size()
				entity.change_after = frame_id+5+(seed%37)
				entity.frame_offset = (seed>>4)%9
				entity.frame_stride = 3+((seed>>8)%9)
				changes += 1
			if frame_id%2==0:
				entity.x += float((int(entity.seed)%3)-1)*0.20
				entity.y += float(((int(entity.seed)>>4)%3)-1)*0.15
			seen[str(entity.visual_index)+":"+str(entity.group_index)] = true
			canvas.monsters[index] = entity
		var update_ms := float(Time.get_ticks_usec()-update_start)/1000.0
		canvas.phase = frame_id
		canvas.queue_redraw()
		await process_frame
		var now := Time.get_ticks_usec()
		if frame_id>=WARMUP:
			intervals.append(float(now-last)/1000.0)
			draws.append(float(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
			updates.append(update_ms)
		last = now
	var total_groups := 0
	for visual in canvas.monster_visuals:
		total_groups += visual.groups.size()
	var result := {
		"scenario":"map13_independent_assets_random_monsters",
		"map_id":13,"map_asset_root":map_assets,"monster_asset_root":mode_root,
		"asset_boundary_verified":true,
		"kinds":kinds,"monster_ids":selected,"instances":count,"frames":frame_count,
		"map_visual_assets":map_metrics.visuals,"map_visible_objects":map_metrics.objects,
		"map_pages":map_metrics.pages,"map_rgba_bytes":map_metrics.rgba,
		"map_png_bytes":map_metrics.png,"map_load_ms":map_metrics.load_ms,
		"monster_visual_assets":monster_metrics.visuals,"monster_pages":monster_metrics.pages,
		"monster_rgba_bytes":monster_metrics.rgba,"monster_png_bytes":monster_metrics.png,
		"monster_load_ms":monster_metrics.load_ms,
		"total_load_ms":float(map_metrics.load_ms)+float(monster_metrics.load_ms),
		"source_groups_exercised":seen.size(),"source_groups_available":total_groups,
		"action_changes":changes,
		"update_cpu_p95_ms":_percentile(updates,0.95),
		"draw_submit_cpu_mean_ms":float(canvas.render_cpu_us)/max(1,canvas.render_count)/1000.0,
		"frame_p50_ms":_percentile(intervals,0.50),
		"frame_p95_ms":_percentile(intervals,0.95),
		"frame_p99_ms":_percentile(intervals,0.99),
		"draw_calls_p50":_percentile(draws,0.50),
		"draw_calls_p95":_percentile(draws,0.95),
		"texture_mem_monitor_bytes":int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)),
		"video_mem_monitor_bytes":int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)),
		"renderer":RenderingServer.get_video_adapter_name()
	}
	var output := JSON.stringify(result)
	print(output)
	if args.size()>=8:
		var file := FileAccess.open(str(args[7]),FileAccess.WRITE)
		if file!=null:
			file.store_string(output+"\n")
			file.close()
	quit(0)
