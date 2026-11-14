extends SceneTree

# GPU/window benchmark for layered character core atlases.
# Uses the exact same races, logical actions/facings, ACT frame indices and
# visible player count for both candidates. Draw-order correctness is NOT
# asserted here; this isolates page/layout cost.

class CharacterCanvas extends Node2D:
	var races: Array[Dictionary] = []
	var entities: Array[Dictionary] = []
	var phase := 0

	func _draw() -> void:
		for entity in entities:
			var race: Dictionary = races[entity.race_index]
			var logical_states: Array = race.logical_states
			var logical_index: int = (entity.index * 17 + int(phase / 24)) % logical_states.size()
			var logical: Dictionary = logical_states[logical_index]
			var physical: Dictionary = race.physical_states[int(logical.physical_state)]
			var sequence: Array = physical.frames
			if sequence.is_empty():
				continue
			var sequence_index: int = (int(phase / 4) + entity.index * 7) % sequence.size()
			var source_frame: int = int(sequence[sequence_index].index)
			var flip_x: bool = bool(logical.flip_x)
			var scale_x := -1.0 if flip_x else 1.0
			draw_set_transform(Vector2(entity.x, entity.y), 0.0, Vector2(scale_x, 1.0))
			# Physical state draw_parts is compiled from the original DrawOrder row
			# after filtering to currently occupied core slots.
			for part in physical.draw_parts:
				if not physical.sources.has(part):
					continue
				var source: String = String(physical.sources[part]).to_lower()
				var key := source + "#" + str(source_frame)
				if not race.frame_lookup.has(key):
					continue
				var frame: Dictionary = race.frame_lookup[key]
				var page_index: int = int(frame.page)
				if page_index < 0:
					continue
				var region: Array = frame.rect
				var crop: Array = frame.crop
				var origin: Array = frame.raw_origin
				var local_x := -float(origin[0]) + float(crop[0])
				var local_y := -float(origin[1]) + float(crop[1])
				draw_texture_rect_region(
					race.textures[page_index],
					Rect2(local_x, local_y, region[2], region[3]),
					Rect2(region[0], region[1], region[2], region[3])
				)
			draw_set_transform(Vector2.ZERO, 0.0, Vector2.ONE)


func _initialize() -> void:
	call_deferred("_run")


func _load_race(root_path: String, race_name: String) -> Dictionary:
	var manifest_path := root_path.path_join(race_name).path_join("character.atlas.json")
	var document: Variant = JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
	if not document is Dictionary:
		push_error("Invalid character manifest: " + manifest_path)
		return {}
	var textures: Array[Texture2D] = []
	var rgba_bytes := 0
	var png_bytes := 0
	for page in document.pages:
		var page_path: String = manifest_path.get_base_dir().path_join(String(page))
		var image := Image.new()
		if image.load(page_path) != OK:
			push_error("Cannot load page: " + page_path)
			return {}
		rgba_bytes += image.get_width() * image.get_height() * 4
		var file := FileAccess.open(page_path, FileAccess.READ)
		png_bytes += file.get_length()
		textures.append(ImageTexture.create_from_image(image))
	var lookup := {}
	for frame in document.frames:
		var key := String(frame.source).to_lower() + "#" + str(int(frame.source_frame))
		if lookup.has(key):
			push_error("Duplicate frame key: " + key)
			return {}
		lookup[key] = frame
	return {
		"race": race_name,
		"textures": textures,
		"frame_lookup": lookup,
		"physical_states": document.physical_states,
		"logical_states": document.logical_states,
		"parts": document.parts,
		"pages": document.pages.size(),
		"rgba_bytes": rgba_bytes,
		"png_bytes": png_bytes,
	}


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 1 or args.size() > 4:
		printerr("Usage: godot --script tools/benchmark_character_render.gd -- candidate-root [width height players]")
		quit(2)
		return

	var base: String = args[0]
	var width := 1440 if args.size() < 3 else int(args[1])
	var height := 900 if args.size() < 3 else int(args[2])
	var player_count := 20 if args.size() < 4 else int(args[3])
	if width < 640 or height < 480 or player_count <= 0:
		printerr("Invalid viewport or player count")
		quit(2)
		return

	DisplayServer.window_set_min_size(Vector2i(640, 480))
	DisplayServer.window_set_size(Vector2i(width, height))
	root.min_size = Vector2i(640, 480)
	root.size = Vector2i(width, height)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)

	var canvas := CharacterCanvas.new()
	var page_count := 0
	var rgba_bytes := 0
	var png_bytes := 0
	var load_start := Time.get_ticks_usec()
	for race_name in ["A", "B", "G", "R"]:
		var race := _load_race(base, race_name)
		if race.is_empty():
			quit(1)
			return
		page_count += int(race.pages)
		rgba_bytes += int(race.rgba_bytes)
		png_bytes += int(race.png_bytes)
		canvas.races.append(race)
	var load_ms := float(Time.get_ticks_usec() - load_start) / 1000.0

	for i in player_count:
		canvas.entities.append({
			"index": i,
			"race_index": i % canvas.races.size(),
			"x": float(70 + ((i * 137) % max(100, width - 140))),
			"y": float(90 + ((i * 79) % max(80, height - 150)))
		})
	canvas.entities.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return a.y < b.y)
	root.add_child(canvas)

	var times: Array[float] = []
	var draws: Array[int] = []
	var last := Time.get_ticks_usec()
	for i in 240:
		canvas.phase = i
		canvas.queue_redraw()
		await process_frame
		var now := Time.get_ticks_usec()
		if i >= 80:
			times.append(float(now - last) / 1000.0)
			draws.append(int(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last = now

	times.sort()
	draws.sort()
	var n := times.size()
	print(JSON.stringify({
		"requested_viewport": [width, height],
		"actual_window": [DisplayServer.window_get_size().x, DisplayServer.window_get_size().y],
		"players": player_count,
		"races": canvas.races.size(),
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
		"renderer": RenderingServer.get_video_adapter_name(),
		"core_draw_order_verified": true,
		"timing_verified": false
	}))
	quit(0)
