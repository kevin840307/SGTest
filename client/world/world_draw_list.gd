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
# Presentation may reject a bad *individual* actor/animation frame without
# blacking out all other players, NPCs, and scenery for the entire frame.
var skipped_actors := 0
var skipped_frames := 0
var skipped_parts := 0
var invalid_actor_examples: Array[String] = []


func clear() -> void:
	_world = {}
	_assets = {}
	_roots.clear()
	_timelines.clear()
	_animated.clear()
	_map_epoch = 0
	error = OK
	skipped_actors = 0
	skipped_frames = 0
	skipped_parts = 0
	invalid_actor_examples.clear()


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


func add_actor_assets(assets: Dictionary) -> Error:
	# Dynamic AOI appearance assets arrive after the map draw list has already
	# been configured. They are presentation-only and may extend the actor
	# atlas set, but must never replace a map/scenery asset with another value.
	for asset_id in assets:
		var id := str(asset_id)
		var asset: Variant = assets[asset_id]
		if not asset is Dictionary or not asset.has_all(["manifest","pages"]):
			return ERR_INVALID_DATA
		if _assets.has(id) and _assets[id] != asset:
			return ERR_INVALID_DATA
		_assets[id] = asset
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
	skipped_actors = 0
	skipped_frames = 0
	skipped_parts = 0
	invalid_actor_examples.clear()
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
			skipped_actors += 1
			if invalid_actor_examples.size()<4:
				invalid_actor_examples.append(_invalid_actor_reason(actor))
			continue
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
	# Soft actor/frame errors are already skipped individually. Keep strict
	# rejection for corrupt scenery/focus metadata (never render partial maps).
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


func _append(commands: Array, asset: Dictionary, clip: String, frame_index: int, position: Vector2, view: Rect2, index: int, slot: int, actor_id: String, alpha: int = 255, flip_h: bool = false, palette_index: int = 0) -> void:
	var indices: Array = asset.manifest.clips[clip]
	if frame_index < 0 or frame_index >= indices.size():
		# Keep this actor visible on transient malformed ACT samples too.
		# Show a safe first pose; never blank the other players and NPCs.
		skipped_frames += 1
		if indices.is_empty():
			return
		frame_index = 0
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
			"palette_index":palette_index,
			"modulate":Color(1,1,1,alpha/255.0)})


func _valid_part(part: Variant) -> bool:
	return part is Dictionary and part.has_all(["asset","clip"]) and \
		_assets.has(str(part.asset)) and _valid_clip(_assets[str(part.asset)],str(part.clip))


func _invalid_actor_reason(actor: Dictionary) -> String:
	var id := str(actor.get("id","?"))
	if not actor.has_all(["id","p"]) or not actor.get("p") is Vector2 or not (actor.get("p") as Vector2).is_finite():
		return "%s:position_invalid" % id
	var parts = actor.get("parts",[actor])
	if not parts is Array or parts.is_empty():
		return "%s:parts_missing" % id
	for part in parts:
		if not _valid_part(part):
			return "%s:bad_part=%s clip=%s asset_available=%s" % [
				id,str(part.get("asset","?")) if part is Dictionary else "not-dict",
				str(part.get("clip","?")) if part is Dictionary else "not-dict",
				str(_assets.has(str(part.get("asset","")))) if part is Dictionary else "false"]
	return "%s:unknown" % id


func _valid_actor(actor: Dictionary) -> bool:
	if not actor.has_all(["id","p"]) or not actor.p is Vector2 or not actor.p.is_finite():
		return false
	var parts = actor.get("parts",[actor])
	if not parts is Array or parts.is_empty():
		return false
	# Recovered draws its paper-doll layers independently. Never suppress the
	# ENTIRE avatar because one sleeve/hair animation page is missing.
	for part in parts:
		if _valid_part(part):
			return true
	return false


func build_actor_preview(assets: Dictionary, actor: Dictionary, view: Rect2) -> Array:
	# UI previews use the same SPR origin, trim, mirror and part ordering
	# as the world, without fabricating a map or retaining map state.
	clear()
	_assets = assets
	var commands: Array = []
	if not _valid_actor(actor):
		error = ERR_INVALID_DATA
	else:
		_append_actor(commands,actor,view)
	_assets = {}
	return commands


func _append_actor(commands: Array, actor: Dictionary, view: Rect2) -> void:
	# One bad paper-doll layer cannot suppress the remaining body/limbs.
	for part in actor.get("parts",[actor]):
		if not _valid_part(part):
			skipped_parts += 1
			if invalid_actor_examples.size()<4:
				invalid_actor_examples.append("%s:layer=%s clip=%s" % [
					str(actor.id),str(part.get("asset","?")) if part is Dictionary else "?",
					str(part.get("clip","?")) if part is Dictionary else "?"])
			continue
		_append(commands,_assets[part.asset],part.clip,int(part.get("frame",0)),actor.p,view,-1,
			int(part.get("part_slot",0)),actor.id,255,bool(part.get("flip_h",false)),
			int(part.get("palette_index",0)))
