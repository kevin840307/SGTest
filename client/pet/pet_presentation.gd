extends RefCounted
## Presentation-only Pet AOI state. WorldState remains authoritative.
##
## The current compact PetEntity stream proves independent Pet coordinates but
## does not yet carry HK-verified facing/action or a trusted definition->sprite
## catalog. This presenter therefore smooths only committed positions. Optional
## visual bindings may expose a static source-backed actor through WorldDrawList;
## movement animation stays disabled until the visual/action mapping is verified.
const PositionHistory = preload("res://movement/position_history.gd")

var _pets: Dictionary = {}
var _visuals: Dictionary = {}
var _epoch := 0

func clear() -> void:
	_pets.clear()
	_visuals.clear()
	_epoch = 0

func clear_entities() -> void:
	_pets.clear()
	_epoch = 0

func configure_visuals(definitions: Dictionary, assets: Dictionary) -> Error:
	_visuals.clear()
	for raw_id in definitions:
		var definition_id := int(raw_id)
		var visual = definitions[raw_id]
		if definition_id <= 0 or not visual is Dictionary:
			_visuals.clear()
			return ERR_INVALID_DATA
		var asset_id := str(visual.get("asset",""))
		var clip := str(visual.get("clip",""))
		if asset_id.is_empty() or clip.is_empty() or not assets.has(asset_id):
			_visuals.clear()
			return ERR_INVALID_DATA
		var asset: Dictionary = assets[asset_id]
		var manifest: Dictionary = asset.get("manifest",{})
		var clips: Dictionary = manifest.get("clips",{})
		if not clips.has(clip) or not clips[clip] is Array or clips[clip].is_empty():
			_visuals.clear()
			return ERR_INVALID_DATA
		_visuals[definition_id] = {
			"asset":asset_id,
			"clip":clip,
			"frame":maxi(0,int(visual.get("frame",0)))
		}
	return OK

func apply_snapshot(snapshot: Dictionary, now_usec: int) -> Error:
	if not bool(snapshot.get("world_ready",false)) or int(snapshot.get("map_epoch",0)) <= 0 or now_usec < 0:
		return ERR_INVALID_PARAMETER
	var incoming_epoch := int(snapshot.map_epoch)
	if incoming_epoch != _epoch:
		_pets.clear()
		_epoch = incoming_epoch
	var visible = snapshot.get("pet_entities",{})
	if not visible is Dictionary:
		return ERR_INVALID_DATA

	for owner in _pets.keys():
		if not visible.has(owner) and not visible.has(str(owner)):
			_pets.erase(owner)

	for raw_owner in visible:
		var value = visible[raw_owner]
		if not value is Dictionary:
			return ERR_INVALID_DATA
		var owner := int(raw_owner)
		if owner <= 0 or int(value.get("owner_actor_id",0)) != owner:
			return ERR_INVALID_DATA
		var pet_uid := int(value.get("pet_uid",0))
		var definition_id := int(value.get("definition_id",0))
		var owner_connection_epoch := int(value.get("owner_connection_epoch",0))
		var owner_map_epoch := int(value.get("owner_map_epoch",0))
		var server_tick := int(value.get("server_tick",-1))
		var position := Vector2(float(value.get("x",INF)),float(value.get("y",INF)))
		if pet_uid <= 0 or definition_id <= 0 or owner_connection_epoch <= 0 or owner_map_epoch <= 0 or server_tick < 0 or not position.is_finite():
			return ERR_INVALID_DATA

		var old: Dictionary = _pets.get(owner,{})
		var same_generation := (
			not old.is_empty() and
			int(old.value.pet_uid) == pet_uid and
			int(old.value.owner_connection_epoch) == owner_connection_epoch and
			int(old.value.owner_map_epoch) == owner_map_epoch
		)
		var history = old.history if same_generation else PositionHistory.new()
		if same_generation:
			var old_tick := int(old.value.server_tick)
			if server_tick < old_tick:
				continue
			if server_tick == old_tick:
				# UI publication can repeat the same immutable AOI entity many
				# times between network frames. Do not create fake movement.
				if Vector2(float(old.value.x),float(old.value.y)) != position:
					return ERR_INVALID_DATA
				old.value = value.duplicate(true)
				_pets[owner] = old
				continue
		var moving := same_generation and Vector2(float(old.value.x),float(old.value.y)) != position
		if not history.push(server_tick,position,now_usec,moving):
			continue
		_pets[owner] = {
			"value":value.duplicate(true),
			"history":history
		}
	return OK

func position_at(owner_actor_id: int, now_usec: int) -> Vector2:
	var pet: Dictionary = _pets.get(owner_actor_id,{})
	if pet.is_empty() or now_usec < 0:
		return Vector2.INF
	# Never extrapolate Pet authority. The current Server baseline still mirrors
	# owner checkpoints; prediction here would fabricate a second movement model.
	var sampled: Vector2 = pet.history.sample(
		now_usec,PositionHistory.DEFAULT_DELAY_TICKS,0.0,0.0)
	if sampled.is_finite():
		return sampled
	return Vector2(float(pet.value.x),float(pet.value.y))

func actors_at(now_usec: int) -> Array:
	var result: Array = []
	var owners: Array = _pets.keys()
	owners.sort()
	for raw_owner in owners:
		var owner := int(raw_owner)
		var pet: Dictionary = _pets[owner]
		var value: Dictionary = pet.value
		var visual: Dictionary = _visuals.get(int(value.definition_id),{})
		if visual.is_empty():
			continue
		var position := position_at(owner,now_usec)
		if not position.is_finite():
			continue
		result.append({
			"id":"pet:"+str(int(value.pet_uid)),
			"p":position,
			"asset":visual.asset,
			"clip":visual.clip,
			"frame":int(visual.frame),
			"part_slot":0,
			"owner_actor_id":owner,
			"pet_uid":int(value.pet_uid)
		})
	return result

func unresolved_definition_ids() -> Array:
	var missing := {}
	for pet in _pets.values():
		var definition_id := int(pet.value.definition_id)
		if not _visuals.has(definition_id):
			missing[definition_id] = true
	var result: Array = missing.keys()
	result.sort()
	return result

func count() -> int:
	return _pets.size()
