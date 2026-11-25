extends RefCounted
## Presentation-only Pet AOI state. WorldState remains authoritative.
##
## The current compact PetEntity stream proves independent Pet coordinates but
## does not yet carry HK-verified facing/action or a trusted definition/evolution
## -> sprite catalog. The original Pet data keeps PETID and EvolID separately, so
## a visual binding is only valid for that pair. This presenter smooths only
## committed positions. Movement animation remains disabled until verified mapping exists;
## attack playback is optional and only consumes an explicitly source-backed logical view.
const Timeline = preload("res://animation/action_timeline.gd")
const PositionHistory = preload("res://movement/position_history.gd")

var _pets: Dictionary = {}
var _visuals: Dictionary = {}
var _epoch := 0
var _last_attack_sequence := 0

func clear() -> void:
	_pets.clear()
	_visuals.clear()
	_epoch = 0
	_last_attack_sequence = 0

func clear_entities() -> void:
	_pets.clear()
	_epoch = 0
	_last_attack_sequence = 0

func configure_visuals(definitions: Dictionary, assets: Dictionary) -> Error:
	_visuals.clear()
	for raw_definition in definitions:
		var definition_id := int(raw_definition)
		var evolutions = definitions[raw_definition]
		if definition_id <= 0 or not evolutions is Dictionary:
			_visuals.clear()
			return ERR_INVALID_DATA
		for raw_evolution in evolutions:
			var evolution_id := int(raw_evolution)
			var visual = evolutions[raw_evolution]
			if evolution_id < 0 or evolution_id > 65535 or not visual is Dictionary:
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
			var binding := {
				"asset":asset_id,
				"clip":clip,
				"frame":maxi(0,int(visual.get("frame",0)))
			}
			var attack_view_name := str(visual.get("attack_view",""))
			if not attack_view_name.is_empty():
				var logical: Dictionary = manifest.get("logical_views",{})
				var attack_view = logical.get(attack_view_name,{})
				if (
					not attack_view is Dictionary or
					not attack_view.has_all(["clip","flip_h","timeline_ticks","loop"]) or
					not attack_view.flip_h is bool or not attack_view.loop is bool or
					not attack_view.timeline_ticks is Array
				):
					_visuals.clear()
					return ERR_INVALID_DATA
				var attack_clip_name := str(attack_view.clip)
				if not clips.has(attack_clip_name) or not clips[attack_clip_name] is Array or clips[attack_clip_name].is_empty():
					_visuals.clear()
					return ERR_INVALID_DATA
				var timeline := Timeline.new()
				if timeline.configure_ticks(attack_view.timeline_ticks,false) != OK:
					_visuals.clear()
					return ERR_INVALID_DATA
				var attack_clip: Array = clips[attack_clip_name]
				for step in attack_view.timeline_ticks:
					if int(step[0]) < 0 or int(step[0]) >= attack_clip.size():
						_visuals.clear()
						return ERR_INVALID_DATA
				binding["attack"] = {
					"view":attack_view.duplicate(true),
					"timeline":timeline
				}
			_visuals[_visual_key(definition_id,evolution_id)] = binding
	return OK

func apply_snapshot(snapshot: Dictionary, now_usec: int) -> Error:
	if not bool(snapshot.get("world_ready",false)) or int(snapshot.get("map_epoch",0)) <= 0 or now_usec < 0:
		return ERR_INVALID_PARAMETER
	var incoming_epoch := int(snapshot.map_epoch)
	if incoming_epoch < _epoch:
		return ERR_INVALID_DATA
	var visible = snapshot.get("pet_entities",{})
	if not visible is Dictionary:
		return ERR_INVALID_DATA

	# Preflight every PetEntity before retiring an old map generation or
	# despawning its AOI. A malformed later entry must reject the whole frame.
	var validated: Dictionary = {}
	for raw_owner in visible:
		var value = visible[raw_owner]
		if not value is Dictionary:
			return ERR_INVALID_DATA
		var owner := int(raw_owner)
		if owner <= 0 or validated.has(owner) or int(value.get("owner_actor_id",0)) != owner:
			return ERR_INVALID_DATA
		var pet_uid := int(value.get("pet_uid",0))
		var definition_id := int(value.get("definition_id",0))
		var evolution_id := int(value.get("evolution_id",-1))
		var owner_connection_epoch := int(value.get("owner_connection_epoch",0))
		var owner_map_epoch := int(value.get("owner_map_epoch",0))
		var server_tick := int(value.get("server_tick",-1))
		var position := Vector2(float(value.get("x",INF)),float(value.get("y",INF)))
		if pet_uid <= 0 or definition_id <= 0 or evolution_id < 0 or evolution_id > 65535 or owner_connection_epoch <= 0 or owner_map_epoch <= 0 or server_tick < 0 or not position.is_finite():
			return ERR_INVALID_DATA
		var old: Dictionary = _pets.get(owner,{}) if incoming_epoch == _epoch else {}
		if (not old.is_empty() and int(old.value.pet_uid) == pet_uid and
			int(old.value.owner_connection_epoch) == owner_connection_epoch and
			int(old.value.owner_map_epoch) == owner_map_epoch and
			server_tick == int(old.value.server_tick) and
			Vector2(float(old.value.x),float(old.value.y)) != position):
			return ERR_INVALID_DATA
		validated[owner] = {"value":value,"position":position,"server_tick":server_tick}

	if incoming_epoch != _epoch:
		_pets.clear()
		_epoch = incoming_epoch
		_last_attack_sequence = 0
	for owner in _pets.keys():
		if not validated.has(owner):
			_pets.erase(owner)

	for owner in validated:
		var entry: Dictionary = validated[owner]
		var value: Dictionary = entry.value
		var pet_uid := int(value.pet_uid)
		var owner_connection_epoch := int(value.owner_connection_epoch)
		var owner_map_epoch := int(value.owner_map_epoch)
		var server_tick := int(entry.server_tick)
		var position: Vector2 = entry.position
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
				# Preflight already checked immutable same-tick coordinates.
				# Repeated publications must not create fake movement.
				old.value = value.duplicate(true)
				_pets[owner] = old
				continue
		var moving := same_generation and Vector2(float(old.value.x),float(old.value.y)) != position
		if not history.push(server_tick,position,now_usec,moving):
			continue
		_pets[owner] = {
			"value":value.duplicate(true),
			"history":history,
			"attack_started":int(old.get("attack_started",-1)) if same_generation else -1,
			"attack_sequence":int(old.get("attack_sequence",0)) if same_generation else 0
		}
	_apply_attack_result(snapshot,now_usec)
	_apply_attack_visuals(snapshot,now_usec)
	return OK

func _apply_attack_result(snapshot: Dictionary, now_usec: int) -> void:
	var sequence := int(snapshot.get("last_pet_attack_result_sequence",0))
	if sequence <= 0 or sequence <= _last_attack_sequence:
		return
	var status := int(snapshot.get("last_pet_attack_result_status",-1))
	var result = snapshot.get("last_pet_attack_result",{})
	# Only a committed Applied/Miss result represents an actual attack event.
	# Rejections/cooldowns are UI feedback, not animation triggers.
	if status not in [0,1] or not result is Dictionary:
		_last_attack_sequence = sequence
		return
	if int(result.get("map_epoch",0)) != _epoch or int(result.get("attack_sequence",0)) != sequence:
		return
	var pet_uid := int(result.get("pet_uid",0))
	if pet_uid <= 0:
		return
	for owner in _pets.keys():
		var pet: Dictionary = _pets[owner]
		if int(pet.value.pet_uid) != pet_uid:
			continue
		# Do not burn a committed result before its source-backed PetEntity
		# exists. AOI may spawn after the reliable result; its replay must still
		# start the one-shot attack, without re-running an already newer event.
		_last_attack_sequence = sequence
		if sequence <= int(pet.get("attack_sequence",0)):
			return
		pet["attack_started"] = now_usec
		pet["attack_sequence"] = sequence
		_pets[owner] = pet
		return


func _apply_attack_visuals(snapshot: Dictionary, now_usec: int) -> void:
	var events = snapshot.get("pet_attack_visual_events",[])
	if not events is Array:
		return
	for raw_event in events:
		if not raw_event is Dictionary:
			continue
		var event: Dictionary = raw_event
		if int(event.get("receiver_map_epoch",0)) != _epoch:
			continue
		var owner := int(event.get("owner_actor_id",0))
		var pet: Dictionary = _pets.get(owner,{})
		if pet.is_empty():
			continue
		var value: Dictionary = pet.value
		if (
			int(event.get("owner_connection_epoch",0)) != int(value.owner_connection_epoch) or
			int(event.get("owner_map_epoch",0)) != int(value.owner_map_epoch) or
			int(event.get("pet_uid",0)) != int(value.pet_uid)
		):
			continue
		var sequence := int(event.get("attack_sequence",0))
		if sequence <= int(pet.get("attack_sequence",0)):
			continue
		pet["attack_started"] = now_usec
		pet["attack_sequence"] = sequence
		_pets[owner] = pet


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
		var visual: Dictionary = _visuals.get(
			_visual_key(int(value.definition_id),int(value.evolution_id)),{})
		if visual.is_empty():
			continue
		var position := position_at(owner,now_usec)
		if not position.is_finite():
			continue
		var clip := str(visual.clip)
		var frame := int(visual.frame)
		var flip_h := false
		var attack: Dictionary = visual.get("attack",{})
		var attack_started := int(pet.get("attack_started",-1))
		if attack_started >= 0 and not attack.is_empty():
			var elapsed := maxi(0,now_usec-attack_started)*50/1000000
			if not attack.timeline.is_complete(elapsed):
				clip = str(attack.view.clip)
				frame = attack.timeline.frame_at_update(elapsed)
				flip_h = bool(attack.view.flip_h)
		result.append({
			"id":"pet:"+str(int(value.pet_uid)),
			"p":position,
			"asset":visual.asset,
			"clip":clip,
			"frame":frame,
			"flip_h":flip_h,
			"part_slot":0,
			"owner_actor_id":owner,
			"pet_uid":int(value.pet_uid)
		})
	return result

func unresolved_definition_ids() -> Array:
	var missing := {}
	for pet in _pets.values():
		var definition_id := int(pet.value.definition_id)
		var evolution_id := int(pet.value.evolution_id)
		if not _visuals.has(_visual_key(definition_id,evolution_id)):
			missing[definition_id] = true
	var result: Array = missing.keys()
	result.sort()
	return result

func unresolved_visual_keys() -> Array:
	var missing := {}
	for pet in _pets.values():
		var definition_id := int(pet.value.definition_id)
		var evolution_id := int(pet.value.evolution_id)
		var key := _visual_key(definition_id,evolution_id)
		if not _visuals.has(key):
			missing[key] = true
	var result: Array = missing.keys()
	result.sort()
	return result

func _visual_key(definition_id:int,evolution_id:int)->String:
	return str(definition_id)+":"+str(evolution_id)

func count() -> int:
	return _pets.size()
