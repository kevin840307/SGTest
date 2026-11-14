class_name EntityAtlas
extends RefCounted

# One object per entity type, shared by all instances; no textures per monster.
var data: Dictionary = {}
var pages: Array[Texture2D] = []

func open(path: String) -> Error:
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not parsed is Dictionary:
		return ERR_FILE_CORRUPT
	if int(parsed.get("version", 0)) not in [3, 4, 5, 6, 7]:
		return ERR_FILE_CORRUPT
	data = parsed
	pages.clear()
	for page in data.pages:
		var image := Image.load_from_file(path.get_base_dir().path_join(page))
		if image == null:
			return ERR_FILE_CANT_READ
		pages.append(ImageTexture.create_from_image(image))
	return OK

func sample(clip: String, seconds: float, fps: float = 8.0, duration: float = 0.0) -> Dictionary:
	var flip := false
	if not data.clips.has(clip):
		var alias: Dictionary = data.aliases.get(clip, {})
		if alias.is_empty():
			return {}
		clip = alias.clip
		flip = alias.flip_h
	var indices: Array = data.clips[clip]
	if indices.is_empty():
		return {}
	seconds = maxf(seconds, 0.0)
	var action := clip.substr(clip.length() - 2, 1)
	var timing: Dictionary = data.act.get(action, {})
	var timeline: Array = timing.get("timeline", [])
	var looped: bool = timing.get("loop", action in ["S", "W", "R", "U"])
	var advance := int(seconds * fps)
	if duration > 0.0 and not looped:
		advance = int(seconds * indices.size() / duration)
	var index := advance % indices.size() if looped else mini(advance, indices.size()-1)
	if not timeline.is_empty():
		var total := 0
		for entry in timeline:
			total += int(entry[1])
		# Tick rate is explicit policy (50Hz), raw ACT ticks remain in manifest.
		var tick := int(seconds * 50.0)
		if total > 0:
			if duration > 0.0:
				tick = int(seconds * total / duration)
			tick = tick % total if looped else mini(tick, total - 1)
			for entry in timeline:
				if tick < int(entry[1]):
					index = clampi(int(entry[0]), 0, indices.size() - 1)
					break
				tick -= int(entry[1])
	var frame: Dictionary = data.frames[indices[index]]
	return {"frame": frame, "flip_h": flip, "clip": clip}

func draw_frame(canvas: CanvasItem, at: Vector2, clip: String, seconds: float, fps: float = 8.0, duration: float = 0.0) -> void:
	var resolved := sample(clip, seconds, fps, duration)
	if resolved.is_empty():
		return
	var frame: Dictionary = resolved.frame
	if frame.page < 0:
		return
	var rect := Rect2(frame.rect[0], frame.rect[1], frame.rect[2], frame.rect[3])
	var offset := Vector2(frame.trim[0] - frame.origin[0], frame.trim[1] - frame.origin[1])
	canvas.draw_set_transform(at, 0.0, Vector2(-1, 1) if resolved.flip_h else Vector2.ONE)
	canvas.draw_texture_rect_region(pages[frame.page], Rect2(offset, rect.size), rect)
	canvas.draw_set_transform(Vector2.ZERO)
