extends RefCounted
## Map companion NPC sprite instances. All graphics come from the single
## AssetStore scope and draw through the same WorldDrawList as scenery/players.
## The server, not this visual catalog, authorizes transfers and services.

var _entries: Array[Dictionary] = []
var _by_spawn: Dictionary = {}

func clear() -> void:
	_entries.clear()
	_by_spawn.clear()

func configure(population: Dictionary, assets: Dictionary) -> Error:
	clear()
	# NPC companion data is optional in synthetic/partial test worlds.
	# An absent catalog means no sprites, not a broken map.
	if population.is_empty():
		return OK
	if population.get("schema") != "sgtest.npcs/v1":
		return ERR_INVALID_DATA
	var records = population.get("npcs", null)
	if not records is Array:
		return ERR_INVALID_DATA
	for raw in records:
		if not raw is Dictionary:
			clear()
			return ERR_INVALID_DATA
		var spawn_id := int(raw.get("spawn_id", 0))
		var point = raw.get("p", null)
		var visual = raw.get("visual",null)
		# Some authorized NPC records have no sprite in the current pack.
		# Keep these in gameplay/Server data; skip only their presentation.
		if visual == null:
			continue
		if not visual is Dictionary:
			clear()
			return ERR_INVALID_DATA
		if not bool(visual.get("resolved",false)):
			continue
		if spawn_id <= 0 or _by_spawn.has(spawn_id) or not point is Array or point.size() != 2:
			clear()
			return ERR_INVALID_DATA
		var atlas := str(visual.get("atlas", ""))
		if not atlas.ends_with("/atlas.json"):
			clear()
			return ERR_INVALID_DATA
		var asset_id := atlas.trim_suffix("/atlas.json")
		if not assets.has(asset_id):
			clear()
			return ERR_INVALID_DATA
		var manifest: Dictionary = assets[asset_id].manifest
		var clips: Dictionary = manifest.get("clips", {})
		if clips.is_empty():
			clear()
			return ERR_INVALID_DATA
		# Original NPC SPR has one or more clips; source appearance resolves
		# the atlas, not arbitrary animation logic inferred by the client.
		var names: Array = clips.keys()
		names.sort()
		var clip := str(names[0])
		var frames: Array = clips[clip]
		if frames.is_empty():
			clear()
			return ERR_INVALID_DATA
		var frame_id := int(frames[0])
		var frame_table: Array = manifest.get("frames", [])
		if frame_id < 0 or frame_id >= frame_table.size():
			clear()
			return ERR_INVALID_DATA
		var frame: Dictionary = frame_table[frame_id]
		if not frame.has_all(["rect", "origin", "trim"]):
			clear()
			return ERR_INVALID_DATA
		var p := Vector2(float(point[0]), float(point[1]))
		if not p.is_finite():
			clear()
			return ERR_INVALID_DATA
		var rect_data: Array = frame.rect
		var trim: Array = frame.trim
		var origin: Array = frame.origin
		if rect_data.size() != 4 or trim.size() != 2 or origin.size() != 2:
			clear()
			return ERR_INVALID_DATA
		var bounds := Rect2(p + Vector2(float(trim[0])-float(origin[0]), float(trim[1])-float(origin[1])),
			Vector2(float(rect_data[2]), float(rect_data[3])))
		# This is a presentation record. A non-audited NPC may be visible but
		# never becomes a client-authorized shop, quest or teleport.
		var interaction: Dictionary = raw.get("interaction", {})
		var display_name := str(raw.get("name","")).strip_edges()
		# NPC.dat already contains the source CP950 name, normalized by the
		# converter. Never fabricate names from sprite type or NPC id.
		var entry := {"spawn_id": spawn_id, "p": p, "bounds": bounds,
			"asset": asset_id, "clip": clip, "name":display_name,
			"interaction": interaction}
		_entries.append(entry)
		_by_spawn[spawn_id] = entry
	return OK

func count() -> int:
	return _entries.size()

func actors_at(view: Rect2) -> Array:
	var result: Array = []
	for entry in _entries:
		if not (entry.bounds as Rect2).intersects(view):
			continue
		result.append({"id": "npc:" + str(entry.spawn_id), "p": entry.p,
			"parts": [{"asset": entry.asset, "clip": entry.clip, "frame": 0, "part_slot": 0}]})
	return result

func labels_at(view: Rect2) -> Array:
	var result: Array = []
	for entry in _entries:
		if not (entry.bounds as Rect2).intersects(view):
			continue
		var name := str(entry.get("name", ""))
		if name.is_empty():
			continue
		result.append({"actor_id":"npc:"+str(entry.spawn_id),
			"text":name,"color":Color(0.80,0.60,0.98,1.0),
			"p":Vector2(float(entry.p.x),float(entry.bounds.position.y)-5.0)})
	return result


func pick_at(point: Vector2) -> int:
	if not point.is_finite():
		return 0
	var best_id := 0
	var best_distance := INF
	for entry in _entries:
		var bounds: Rect2 = entry.bounds
		# The click target follows the *actual* SPR image, not a guessed
		# circle around the feet. Small 1-frame portals are still clickable.
		var hit := bounds.grow(6.0)
		if not hit.has_point(point):
			continue
		var distance := point.distance_squared_to(entry.p)
		if distance < best_distance or (is_equal_approx(distance,best_distance) and int(entry.spawn_id) < best_id):
			best_distance = distance
			best_id = int(entry.spawn_id)
	return best_id

func interaction_for(spawn_id: int) -> Dictionary:
	if not _by_spawn.has(spawn_id):
		return {}
	return (_by_spawn[spawn_id].interaction as Dictionary).duplicate(true)
