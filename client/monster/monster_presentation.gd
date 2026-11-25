extends RefCounted
## Presentation-only monster sampler. Server WorldState remains authoritative.
## Visual metadata comes only from v3 population sprite/atlas + normalized ACT views;
## combat stats in any visual document are deliberately ignored.
const Timeline = preload("res://animation/action_timeline.gd")
const PositionHistory = preload("res://movement/position_history.gd")

const STATE_IDLE := 0
const STATE_WANDER := 1
const STATE_CHASE := 2
const STATE_ATTACK := 3
const STATE_DEAD := 4
const ACTIONS := ["NS","NW","FA","NH","ND"]

var _definitions := {}
var _views := {}
var _monsters := {}
var _epoch := 0
var _map := 0

func clear() -> void:
	_definitions.clear()
	_views.clear()
	_monsters.clear()
	_epoch = 0
	_map = 0

func configure(population: Dictionary, assets: Dictionary) -> Error:
	clear()
	if int(population.get("version",0)) != 3 or int(population.get("map_id",0)) <= 0:
		return ERR_INVALID_DATA
	var raw_definitions = population.get("definitions",[])
	if not raw_definitions is Array or raw_definitions.size() > 4096:
		return ERR_INVALID_DATA
	_map = int(population.map_id)
	for raw in raw_definitions:
		if not raw is Dictionary:
			clear()
			return ERR_INVALID_DATA
		var id := int(raw.get("id",0))
		var sprite := str(raw.get("sprite","")).strip_edges().to_upper()
		var atlas := str(raw.get("atlas","")).replace("\\","/")
		if id <= 0 or _definitions.has(id) or not _safe_sprite(sprite) or not atlas.ends_with("/atlas.json"):
			clear()
			return ERR_INVALID_DATA
		var asset_id: String = atlas.trim_suffix("/atlas.json")
		if not _safe_path(asset_id) or not assets.has(asset_id):
			clear()
			return ERR_INVALID_DATA
		var manifest: Dictionary = assets[asset_id].get("manifest",{})
		var logical: Dictionary = manifest.get("logical_views",{})
		var per_definition: Dictionary = {}
		for raw_action in ACTIONS:
			var action: String = str(raw_action)
			for facing: int in range(1,9):
				var key: String = sprite+"_"+action+str(facing)
				var view = logical.get(key)
				if not view is Dictionary or not view.has_all(["clip","flip_h","timeline_ticks","loop"]) or not view.flip_h is bool or not view.loop is bool or not manifest.get("clips",{}).has(view.clip):
					clear()
					return ERR_INVALID_DATA
				var timeline := Timeline.new()
				if timeline.configure_ticks(view.timeline_ticks,false if action == "NH" else view.loop) != OK:
					clear()
					return ERR_INVALID_DATA
				var clip: Array = manifest.clips[view.clip]
				for step in view.timeline_ticks:
					if int(step[0]) < 0 or int(step[0]) >= clip.size():
						clear()
						return ERR_INVALID_DATA
				per_definition[action+str(facing)] = {
					"view":view,"timeline":timeline
				}
		_definitions[id] = {"sprite":sprite,"asset":asset_id}
		_views[id] = per_definition
	return OK

func apply_snapshot(snapshot: Dictionary, now_usec: int) -> Error:
	if not bool(snapshot.get("world_ready",false)) or int(snapshot.get("map_epoch",0)) <= 0 or now_usec < 0:
		return ERR_INVALID_PARAMETER
	var epoch := int(snapshot.map_epoch)
	var map := int(snapshot.map_id)
	if map != _map or epoch < _epoch:
		# Reject delayed map-generation snapshots before changing any AOI state.
		return ERR_INVALID_DATA
	var visible = snapshot.get("monsters",{})
	if not visible is Dictionary:
		return ERR_INVALID_DATA
	# Validate every entity before clearing an epoch, despawning AOI actors, or
	# advancing animation histories. A single invalid entry must not turn an
	# otherwise rejected authoritative frame into a partial visible update.
	for raw_id in visible:
		var candidate = visible[raw_id]
		if not candidate is Dictionary:
			return ERR_INVALID_DATA
		var candidate_id := int(raw_id)
		var enemy_id := int(candidate.get("enemy_id",0))
		var state := int(candidate.get("state",-1))
		var facing := int(candidate.get("facing",0))
		var revision := int(candidate.get("revision",0))
		var tick := int(candidate.get("server_tick",-1))
		if candidate_id <= 0 or int(candidate.get("entity_id",0)) != candidate_id or not _definitions.has(enemy_id) or int(candidate.get("map_id",0)) != _map or state < STATE_IDLE or state > STATE_DEAD or facing < 1 or facing > 8 or revision <= 0 or tick < 0 or not candidate.has_all(["source_index","max_hp","current_hp","x","y"]):
			return ERR_INVALID_DATA
		var previous: Dictionary = _monsters.get(candidate_id,{}) if epoch == _epoch else {}
		if not previous.is_empty():
			var previous_value: Dictionary = previous.value
			if int(previous_value.enemy_id) != enemy_id or int(previous_value.source_index) != int(candidate.source_index) or int(previous_value.max_hp) != int(candidate.max_hp):
				return ERR_INVALID_DATA
	if epoch != _epoch:
		_monsters.clear()
		_epoch = epoch
	for id in _monsters.keys():
		if not visible.has(id):
			_monsters.erase(id)
	for raw_id in visible:
		var value: Dictionary = visible[raw_id]
		var id := int(raw_id)
		var state := int(value.state)
		var revision := int(value.revision)
		var tick := int(value.server_tick)
		var old: Dictionary = _monsters.get(id,{})
		if not old.is_empty() and revision < int(old.value.revision):
			continue
		var action: String = _action(state)
		var generation_changed: bool = old.is_empty()
		var history = PositionHistory.new() if generation_changed else old.history
		# Damage is inferred only from a newer authoritative HP revision. Entry
		# snapshots, duplicate frames and ordinary movement cannot fabricate hits.
		var took_hit: bool = not generation_changed and revision > int(old.value.revision) and int(value.current_hp) > 0 and int(value.current_hp) < int(old.value.current_hp)
		var hit_started: int = -1 if generation_changed else int(old.hit_started)
		if took_hit:
			hit_started = now_usec
		elif state == STATE_DEAD or (not generation_changed and state == STATE_ATTACK and revision > int(old.value.revision)):
			# Death wins over Hit; a fresh authoritative attack interrupts it.
			hit_started = -1
		if not history.push(tick,Vector2(float(value.x),float(value.y)),now_usec):
			continue
		var restarted: bool = generation_changed or str(old.action) != action
		# Each stationary Attack event advances the authoritative monster revision;
		# replay FA from frame zero. Chase/Wander position deltas must NOT restart NW.
		if not old.is_empty() and state == STATE_ATTACK and revision > int(old.value.revision):
			restarted = true
		_monsters[id] = {
			"value":value.duplicate(true),
			"action":action,
			"started":now_usec if restarted else int(old.started),
			"hit_started":hit_started,
			"history":history
		}
	return OK

func actors_at(now_usec: int) -> Array:
	var result: Array = []
	var ids: Array = _monsters.keys()
	ids.sort()
	for id in ids:
		var monster: Dictionary = _monsters[id]
		var value: Dictionary = monster.value
		var enemy_id := int(value.enemy_id)
		var action: String = str(monster.action)
		var elapsed: int = maxi(0,now_usec-int(monster.started))*50/1000000
		if action != "ND" and int(monster.hit_started) >= 0:
			var hit_view: Dictionary = _views.get(enemy_id,{}).get("NH"+str(int(value.facing)),{})
			var hit_elapsed: int = maxi(0,now_usec-int(monster.hit_started))*50/1000000
			# NH plays once using source ACT durations, then sampling falls back
			# to the current authoritative AI state without mutating any state.
			if not hit_view.is_empty() and not hit_view.timeline.is_complete(hit_elapsed):
				action = "NH"
				elapsed = hit_elapsed
		var requested: String = action+str(int(value.facing))
		var resolved: Dictionary = _views.get(enemy_id,{}).get(requested,{})
		if resolved.is_empty():
			continue
		var definition: Dictionary = _definitions[enemy_id]
		var sampled: Vector2 = monster.history.sample(now_usec)
		if not sampled.is_finite():
			sampled = Vector2(float(value.x),float(value.y))
		result.append({
			"id":"monster:"+str(id),
			"p":sampled,
			"asset":definition.asset,
			"clip":resolved.view.clip,
			"frame":resolved.timeline.frame_at_update(elapsed),
			"flip_h":resolved.view.flip_h,
			"part_slot":0
		})
	return result

func count() -> int:
	return _monsters.size()

## Presentation-only click hit-test. The Server still validates attack range,
## target HP, wall collision, cooldown and all actual combat consequences.
func pick_living_at(point: Vector2, now_usec: int, radius: float = 32.0) -> int:
	if not point.is_finite() or now_usec < 0 or not is_finite(radius) or radius <= 0.0 or radius > 256.0:
		return 0
	var best_id := 0
	var best_squared := radius*radius
	var ids: Array = _monsters.keys()
	ids.sort()
	for raw_id in ids:
		var id := int(raw_id)
		var state: Dictionary = _monsters[id].value
		if int(state.get("current_hp",0)) <= 0 or int(state.get("state",-1)) == STATE_DEAD:
			continue
		var position := position_at(id,now_usec)
		if not position.is_finite():
			continue
		var squared := point.distance_squared_to(position)
		if squared < best_squared or (is_equal_approx(squared,best_squared) and best_id != 0 and id < best_id):
			best_id = id
			best_squared = squared
	return best_id


func position_at(entity_id: int, now_usec: int) -> Vector2:
	var monster: Dictionary = _monsters.get(entity_id,{})
	if monster.is_empty():
		return Vector2.INF
	var sampled: Vector2 = monster.history.sample(now_usec)
	return sampled if sampled.is_finite() else Vector2(float(monster.value.x),float(monster.value.y))

static func _action(state: int) -> String:
	match state:
		STATE_IDLE:
			return "NS" # source ACT key S
		STATE_WANDER, STATE_CHASE:
			return "NW" # source ACT key W
		STATE_ATTACK:
			return "FA" # source ACT key A
		STATE_DEAD:
			return "ND" # source ACT key D
	return ""

static func _safe_sprite(value: String) -> bool:
	if value.is_empty() or value.length() > 64:
		return false
	for c in value:
		if not (c >= "0" and c <= "9") and not (c >= "A" and c <= "Z"):
			return false
	return true

static func _safe_path(value: String) -> bool:
	if value.is_empty() or value.contains("\\") or value.contains(":") or value.begins_with("/"):
		return false
	for part in value.split("/"):
		if part.is_empty() or part == "." or part == "..":
			return false
	return true
