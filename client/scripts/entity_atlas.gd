class_name EntityAtlas
extends RefCounted

# One object per entity type, shared by all instances; no textures per monster.
# Metadata and returned sample descriptors are immutable; reload through open().
var data: Dictionary = {}
var pages: Array[Texture2D] = []
var sample_cache: Dictionary = {}
var compiled: Dictionary = {}

func open(path: String) -> Error:
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not parsed is Dictionary:
		return ERR_FILE_CORRUPT
	if int(parsed.get("version", 0)) not in [3, 4, 5, 6, 7, 8, 9]:
		return ERR_FILE_CORRUPT
	data = parsed
	pages.clear()
	sample_cache.clear()
	compiled.clear()
	for page in data.pages:
		var image := Image.load_from_file(path.get_base_dir().path_join(page))
		if image == null:
			return ERR_FILE_CANT_READ
		pages.append(ImageTexture.create_from_image(image))
	return OK

func sample(clip: String, seconds: float, fps: float = 8.0, duration: float = 0.0) -> Dictionary:
	seconds = maxf(seconds, 0.0)
	var requested := clip
	var cached: Dictionary = sample_cache.get(requested,{})
	if not cached.is_empty() and cached.seconds == seconds and cached.fps == fps and cached.duration == duration:
		return cached.result
	if not compiled.has(requested):
		var flip := false
		var timeline_ticks: Array = []
		var looping := false
		var logical: Dictionary = data.get("logical_views",{}).get(requested,{})
		if not logical.is_empty():
			clip = str(logical.get("clip",""))
			flip = bool(logical.get("flip_h",false))
			if not data.clips.has(clip):
				return {}
			timeline_ticks = logical.get("timeline_ticks",[])
			looping = bool(logical.get("loop",false))
		else:
			if not data.clips.has(clip):
				var alias: Dictionary = data.aliases.get(clip,{})
				if alias.is_empty():
					return {}
				clip = alias.clip
				flip = alias.flip_h
			var action := clip.substr(clip.length()-2,1)
			var timing: Dictionary = data.act.get(action,{})
			for entry in timing.get("timeline",[]):
				timeline_ticks.append([int(entry[0]),maxi(1,int(entry[1])*40/25)])
			looping = bool(timing.get("loop",action in ["S","W","R","U"]))
		var indices: Array = data.clips[clip]
		if indices.is_empty():
			return {}
		var ends := PackedInt32Array()
		var timeline_indices := PackedInt32Array()
		var total := 0
		for entry in timeline_ticks:
			var frame_index := int(entry[0])
			var ticks := int(entry[1])
			if frame_index < 0 or frame_index >= indices.size() or ticks < 1:
				return {}
			total += ticks
			ends.append(total)
			timeline_indices.append(frame_index)
		var frames: Array = []
		for frame_id in indices:
			var frame: Dictionary = data.frames[frame_id]
			var rect: Array = frame.get("rect",[0,0,0,0])
			var trim: Array = frame.get("trim",[0,0])
			var origin: Array = frame.get("origin",[0,0])
			frames.append({"frame":frame,"flip_h":flip,"clip":clip,
				"rect":Rect2(rect[0],rect[1],rect[2],rect[3]),"offset":Vector2(trim[0]-origin[0],trim[1]-origin[1])})
		compiled[requested] = {"frames":frames,"ends":ends,"indices":timeline_indices,
			"total":total,"loop":looping}
	var plan: Dictionary = compiled[requested]
	var advance := int(seconds*fps)
	if duration > 0.0 and not plan.loop:
		advance = int(seconds*plan.frames.size()/duration)
	var index: int = advance%plan.frames.size() if plan.loop else mini(advance,plan.frames.size()-1)
	if plan.total > 0:
		var tick := int(seconds*plan.total/duration) if duration > 0.0 else int(seconds*50.0)
		tick = tick%int(plan.total) if plan.loop else mini(tick,int(plan.total)-1)
		# Endpoints are exclusive: a tick on the boundary selects the next frame.
		index = plan.indices[plan.ends.bsearch(tick,false)]
	var result: Dictionary = plan.frames[index]
	# Exact input equality, no time quantization or lost animation frames.
	# Monsters may have different phases; compiled timelines and frame descriptors
	# avoid scanning ACT or allocating dictionaries for each actor.
	if cached.is_empty():
		cached = {"seconds":seconds,"fps":fps,"duration":duration,"result":result}
		sample_cache[requested] = cached
	else:
		cached.seconds = seconds
		cached.fps = fps
		cached.duration = duration
		cached.result = result
	return result

func draw_frame(canvas: CanvasItem, at: Vector2, clip: String, seconds: float, fps: float = 8.0, duration: float = 0.0) -> void:
	var resolved := sample(clip, seconds, fps, duration)
	if resolved.is_empty():
		return
	var frame: Dictionary = resolved.frame
	if frame.page < 0:
		return
	canvas.draw_set_transform(at, 0.0, Vector2(-1, 1) if resolved.flip_h else Vector2.ONE)
	canvas.draw_texture_rect_region(pages[frame.page], Rect2(resolved.offset, resolved.rect.size), resolved.rect)
	canvas.draw_set_transform(Vector2.ZERO)
