extends RefCounted
## Pure presentation snapshot -> visible Canvas commands. No loading or clocks.
## Caller owns AssetStore scope; clear this and all commands before scope release.

const Order = preload("res://world/world_order.gd")
const Fade = preload("res://map/building_fade.gd")
const Timeline = preload("res://animation/action_timeline.gd")
const Visibility = preload("res://map/building_visibility.gd")
var _world: Dictionary = {}
var _assets: Dictionary = {}
var _roots: Array = []
var _timelines: Dictionary = {}
var _animated: Dictionary = {}
var _map_epoch := 0
var error := OK


func clear() -> void:
	_world = {}
	_assets = {}
	_roots.clear()
	_timelines.clear()
	_animated.clear()
	_map_epoch = 0
	error = OK


func configure(world: Dictionary, assets: Dictionary, map_epoch: int = 0) -> Error:
	clear()
	if map_epoch < 0:
		return ERR_INVALID_PARAMETER
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
	_map_epoch = map_epoch
	return OK


func frames_at_update(now: int, started_at: Dictionary = {}) -> Dictionary:
	# Epoch/phase belongs to the caller. No render callback mutates a clock.
	var frames := {}
	for index in _animated:
		frames[index] = _animated[index].frame_at_update(now - int(started_at.get(index,0)))
	return frames


func visible_main_indices(view: Rect2) -> Array:
	if _world.is_empty():
		return []
	var visible := {}
	for root in _roots:
		if root.bounds.intersects(view):
			visible[root.index] = true
	var indices: Array = []
	for raw in _world.scenery_order.groups[1]:
		if visible.has(int(raw)):
			indices.append(int(raw))
	return indices


func build_focus(view: Rect2, actors: Array, snapshot: Dictionary, expected_update: int, frames: Dictionary = {}) -> Array:
	if _map_epoch <= 0 or not snapshot.get("active",false) or snapshot.get("epoch") != _map_epoch or snapshot.get("update") != expected_update or expected_update < 0:
		error = ERR_INVALID_PARAMETER
		return []
	var selected := int(snapshot.get("selected",-1))
	var transitions: Dictionary = snapshot.get("transitions",{})
	if selected < -1 or (selected >= 0 and (selected >= _world.objects.size() or _world.objects[selected].parent_index != null or not transitions.get(selected,{}).get("inside",false))):
		error = ERR_INVALID_DATA
		return []
	if selected >= 0:
		var definition: Dictionary = _world.scenery_definitions[_world.objects[selected].definition]
		if int(definition.sorting_mode) != 0 or definition.get("front",[]).size() != 4 or definition.get("inside_cells",[]).is_empty() or definition.get("outside_cells",[]).is_empty():
			error = ERR_INVALID_DATA
			return []
	return build(view,actors,{},frames,transitions,selected)


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


func build(view: Rect2, actors: Array = [], inside: Dictionary = {}, frames: Dictionary = {}, transitions: Dictionary = {}, selected_overlay: int = -1) -> Array:
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
	var overlay: Array = []
	if selected_overlay >= 0:
		for child in range(selected_overlay+1,selected_overlay+1+int(_world.objects[selected_overlay].child_count)):
			if _bounds(_world.objects[child].bounds).intersects(view):
				overlay.append(_record(child))
	# Actor order is the caller's stable presentation order, independent of camera.
	for actor in actors:
		if not _valid_actor(actor):
			error = ERR_INVALID_DATA
			return []
		var record: Dictionary = actor.duplicate()
		record.sorting_mode = 1
		record.index = -1
		var route_inside := false
		if selected_overlay >= 0:
			var building: Dictionary = _world.objects[selected_overlay]
			var definition: Dictionary = _world.scenery_definitions[building.definition]
			route_inside = Visibility.entrance(Vector2(building.p[0],building.p[1]),definition.front,record.p)
		Order.insert_record(overlay if route_inside else groups[1], record)
	var commands: Array = []
	for group in groups:
		for record in group:
			if record.index == -1:
				_append_actor(commands,record,view)
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
					_children(commands,index,view,frames)
				if dual:
					_placement(commands,index,view,1,frames,int(fade.alternate_alpha))
				if index == selected_overlay:
					# DIRECT DrawMapList 0x0052A2FF–0x0052A314: selected room's
					# visible children are drawn again after the building Draw.
					for inside_record in overlay:
						if inside_record.index == -1:
							_append_actor(commands,inside_record,view)
						else:
							_child(commands,int(inside_record.index),view,frames)
	return commands if error == OK else []


func _children(commands: Array, index: int, view: Rect2, frames: Dictionary) -> void:
	for child in range(index + 1, index + int(_world.objects[index].child_count) + 1):
		_child(commands,child,view,frames)


func _child(commands: Array, child: int, view: Rect2, frames: Dictionary) -> void:
	var definition: Dictionary = _world.scenery_definitions[_world.objects[child].definition]
	var slot := 1 if definition.appearances.size() > 1 and definition.appearances[1] != null else 0
	if int(definition.sorting_mode) != 0 or slot == 1:
		_placement(commands,child,view,slot,frames,255)


func _placement(commands: Array, index: int, view: Rect2, slot: int, frames: Dictionary, alpha: int) -> void:
	if alpha <= 0:
		return
	var placement: Dictionary = _world.objects[index]
	var definition: Dictionary = _world.scenery_definitions[placement.definition]
	var appearance: Dictionary = definition.appearances[slot]
	var id := str(appearance.atlas).trim_suffix("/atlas.json")
	_append(commands, _assets[id], appearance.clip, int(frames.get(index,0)), Vector2(placement.p[0],placement.p[1]), view, index, slot, "",alpha)


func _append(commands: Array, asset: Dictionary, clip: String, frame_index: int, position: Vector2, view: Rect2, index: int, slot: int, actor_id: String, alpha: int = 255, flip_h: bool = false) -> void:
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
	if flip_h:
		# Mirror around the original SPR origin, including trimmed asymmetry.
		destination.position.x = position.x - (at.x-position.x)
		destination.size.x = -destination.size.x
	if destination.abs().intersects(view):
		commands.append({"texture":asset.pages[int(frame.page)],"destination":destination,"uv":uv,
			"index":index,"slot":slot,"frame":frame_index,"actor_id":actor_id,"alpha":alpha,
			"modulate":Color(1,1,1,alpha/255.0)})


func _valid_actor(actor: Dictionary) -> bool:
	if not actor.has_all(["id","p"]) or not actor.p is Vector2 or not actor.p.is_finite():
		return false
	var parts = actor.get("parts",[actor])
	if not parts is Array or parts.is_empty():
		return false
	for part in parts:
		if not part is Dictionary or not part.has_all(["asset","clip"]) or not _assets.has(part.asset) or not _valid_clip(_assets[part.asset],part.clip):
			return false
	return true


func _append_actor(commands: Array, actor: Dictionary, view: Rect2) -> void:
	# The whole paper doll occupies one position in scenery/actor order.
	for part in actor.get("parts",[actor]):
		_append(commands,_assets[part.asset],part.clip,int(part.get("frame",0)),actor.p,view,-1,int(part.get("part_slot",0)),actor.id,255,bool(part.get("flip_h",false)))
