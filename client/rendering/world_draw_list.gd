extends RefCounted
## Pure presentation snapshot -> visible Canvas commands. No loading or clocks.
## Caller owns AssetStore scope; clear this and all commands before scope release.

const Order = preload("res://rendering/world_order.gd")
const Fade = preload("res://rendering/building_fade.gd")
const Timeline = preload("res://rendering/action_timeline.gd")
var _world: Dictionary = {}
var _assets: Dictionary = {}
var _roots: Array = []
var _timelines: Dictionary = {}
var _animated: Dictionary = {}
var error := OK


func clear() -> void:
	_world = {}
	_assets = {}
	_roots.clear()
	_timelines.clear()
	_animated.clear()
	error = OK


func configure(world: Dictionary, assets: Dictionary) -> Error:
	clear()
	if world.get("scenery_schema") != "sgtest.scenery/v1" or world.get("scenery_order", {}).get("version") != 1:
		return ERR_INVALID_DATA
	var groups = world.scenery_order.get("groups", [])
	if not groups is Array or groups.size() != 3:
		return ERR_INVALID_DATA
	var seen := {}
	for group_index in 3:
		var group: Array = groups[group_index]
		for raw in group:
			var index := int(raw)
			if index < 0 or index >= world.objects.size() or seen.has(index):
				return ERR_INVALID_DATA
			var placement: Dictionary = world.objects[index]
			if placement.parent_index != null or not world.scenery_definitions.has(placement.definition):
				return ERR_INVALID_DATA
			if int(world.scenery_definitions[placement.definition].draw_group) != group_index:
				return ERR_INVALID_DATA
			seen[index] = true
	for index in world.objects.size():
		var placement: Dictionary = world.objects[index]
		if placement.parent_index == null and not seen.has(index):
			return ERR_INVALID_DATA
		if placement.parent_index != null:
			var parent := int(placement.parent_index)
			if parent < 0 or parent >= index or world.objects[parent].parent_index != null or index > parent + int(world.objects[parent].child_count):
				return ERR_INVALID_DATA
		if not world.scenery_definitions.has(placement.definition):
			return ERR_INVALID_DATA
		for appearance in world.scenery_definitions[placement.definition].appearances:
			if appearance != null:
				var id := str(appearance.atlas).trim_suffix("/atlas.json")
				if not assets.has(id) or not _valid_clip(assets[id], appearance.clip):
					return ERR_INVALID_DATA
	_world = world
	_assets = assets
	for index in world.objects.size():
		var placement: Dictionary = world.objects[index]
		if placement.parent_index != null:
			continue
		var bounds := _bounds(placement.bounds)
		var count := int(placement.child_count)
		if count < 0 or index + count >= world.objects.size():
			clear()
			return ERR_INVALID_DATA
		for child in range(index + 1, index + count + 1):
			if world.objects[child].parent_index != index:
				clear()
				return ERR_INVALID_DATA
			bounds = bounds.merge(_bounds(world.objects[child].bounds))
		_roots.append({"index":index, "bounds":bounds})
	for index in world.objects.size():
		var scenery: Dictionary = world.scenery_definitions[world.objects[index].definition]
		if int(scenery.sorting_mode) != 1:
			continue  # Only CELStaticObj2D initializes the literal scenery action.
		var appearance: Dictionary = scenery.appearances[0]
		var id := str(appearance.atlas).trim_suffix("/atlas.json")
		var definition = assets[id].manifest.get("act",{}).get(appearance.clip)
		if definition == null:
			continue
		var key := id + ":" + str(appearance.clip)
		if not _timelines.has(key):
			var timeline := Timeline.new()
			if timeline.configure(definition) != OK:
				clear()
				return ERR_INVALID_DATA
			for step in definition.frames:
				if int(step.index) >= assets[id].manifest.clips[appearance.clip].size():
					clear()
					return ERR_INVALID_DATA
			_timelines[key] = timeline
		_animated[index] = _timelines[key]
	return OK


func frames_at_update(now: int, started_at: Dictionary = {}) -> Dictionary:
	# Epoch/phase belongs to the caller. No render callback mutates a clock.
	var frames := {}
	for index in _animated:
		frames[index] = _animated[index].frame_at_update(now - int(started_at.get(index,0)))
	return frames


static func _valid_clip(asset: Dictionary, clip: String) -> bool:
	if not asset.has_all(["manifest", "pages"]) or not asset.manifest.get("clips", {}).has(clip):
		return false
	var indices = asset.manifest.clips[clip]
	if indices.is_empty():
		return false
	for raw in indices:
		var index := int(raw)
		if index < 0 or index >= asset.manifest.frames.size():
			return false
		var frame: Dictionary = asset.manifest.frames[index]
		if int(frame.page) == -1 and frame.rect.size() == 4 and frame.rect.all(func(value):return float(value) == 0.0):
			continue
		if int(frame.page) < 0 or int(frame.page) >= asset.pages.size():
			return false
	return true


static func _bounds(values: Array) -> Rect2:
	return Rect2(values[0], values[1], values[2], values[3])


func _record(index: int) -> Dictionary:
	var placement: Dictionary = _world.objects[index]
	var definition: Dictionary = _world.scenery_definitions[placement.definition]
	return {"index":index, "p":Vector2(placement.p[0],placement.p[1]),
		"sorting_mode":definition.sorting_mode,"front":definition.get("front", [])}


func build(view: Rect2, actors: Array = [], inside: Dictionary = {}, frames: Dictionary = {}, transitions: Dictionary = {}) -> Array:
	error = OK
	if _world.is_empty():
		error = ERR_UNCONFIGURED
		return []
	var visible := {}
	for root in _roots:
		if root.bounds.intersects(view):
			visible[root.index] = true
	var groups: Array = [[],[],[]]
	for group in 3:
		for index in _world.scenery_order.groups[group]:
			if visible.has(int(index)):
				groups[group].append(_record(int(index)))
	# Actor order is the caller's stable presentation order, independent of camera.
	for actor in actors:
		if not actor.has_all(["id", "p", "asset", "clip"]) or not _assets.has(actor.asset) or not _valid_clip(_assets[actor.asset], actor.clip):
			error = ERR_INVALID_DATA
			return []
		var record: Dictionary = actor.duplicate()
		record.sorting_mode = 1
		record.index = -1
		Order.insert_record(groups[1], record)
	var commands: Array = []
	for group in groups:
		for record in group:
			if record.index == -1:
				_append(commands, _assets[record.asset], record.clip, int(record.get("frame",0)), record.p, view, -1, 0, record.id)
			else:
				var index := int(record.index)
				var target := bool(inside.get(index,false))
				var definition: Dictionary = _world.scenery_definitions[_world.objects[index].definition]
				var fade := {"primary_alpha":255 if target else 0,"alternate_alpha":0 if target else 255,"children":target}
				if transitions.has(index):
					var transition: Dictionary = transitions[index]
					if not transition.has_all(["inside","updates"]) or definition.sorting_mode != 0:
						error = ERR_INVALID_DATA
						return []
					fade = Fade.layers(bool(transition.inside),int(transition.updates))
				var dual: bool = definition.appearances.size() > 1 and definition.appearances[1] != null
				_placement(commands,index,view,0,frames,int(fade.primary_alpha) if int(definition.sorting_mode) == 0 else 255)
				if bool(fade.children):
					for child in range(index + 1, index + int(_world.objects[index].child_count) + 1):
						var child_definition: Dictionary = _world.scenery_definitions[_world.objects[child].definition]
						var child_slot := 1 if child_definition.appearances.size() > 1 and child_definition.appearances[1] != null else 0
						if int(child_definition.sorting_mode) != 0 or child_slot == 1:
							_placement(commands, child, view, child_slot, frames,255)
				if dual:
					_placement(commands,index,view,1,frames,int(fade.alternate_alpha))
	return commands if error == OK else []


func _placement(commands: Array, index: int, view: Rect2, slot: int, frames: Dictionary, alpha: int) -> void:
	if alpha <= 0:
		return
	var placement: Dictionary = _world.objects[index]
	var definition: Dictionary = _world.scenery_definitions[placement.definition]
	var appearance: Dictionary = definition.appearances[slot]
	var id := str(appearance.atlas).trim_suffix("/atlas.json")
	_append(commands, _assets[id], appearance.clip, int(frames.get(index,0)), Vector2(placement.p[0],placement.p[1]), view, index, slot, "",alpha)


func _append(commands: Array, asset: Dictionary, clip: String, frame_index: int, position: Vector2, view: Rect2, index: int, slot: int, actor_id: String, alpha: int = 255) -> void:
	var indices: Array = asset.manifest.clips[clip]
	if frame_index < 0 or frame_index >= indices.size():
		error = ERR_INVALID_PARAMETER
		return
	var frame: Dictionary = asset.manifest.frames[int(indices[frame_index])]
	if int(frame.page) == -1:
		return  # A real empty source frame; preserve its identity/timing externally.
	var uv := _bounds(frame.rect)
	var at := position + Vector2(frame.trim[0]-frame.origin[0], frame.trim[1]-frame.origin[1])
	var destination := Rect2(at,uv.size)
	if destination.intersects(view):
		commands.append({"texture":asset.pages[int(frame.page)],"destination":destination,"uv":uv,
			"index":index,"slot":slot,"frame":frame_index,"actor_id":actor_id,"alpha":alpha,
			"modulate":Color(1,1,1,alpha/255.0)})
