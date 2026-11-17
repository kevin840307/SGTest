extends RefCounted
## Presentation-only monster sampler. Server WorldState remains authoritative.
## Visual metadata comes only from v3 population sprite/atlas + normalized ACT views;
## combat stats in any visual document are deliberately ignored.
const Timeline = preload("res://rendering/action_timeline.gd")
const PositionHistory = preload("res://rendering/position_history.gd")

const STATE_IDLE := 0
const STATE_WANDER := 1
const STATE_CHASE := 2
const STATE_ATTACK := 3
const STATE_DEAD := 4
const ACTIONS := ["NS","NW","FA","ND"]

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
				if timeline.configure_ticks(view.timeline_ticks,view.loop) != OK:
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
	if map != _map:
		return ERR_INVALID_DATA
	if epoch != _epoch:
		_monsters.clear()
		_epoch = epoch
	var visible = snapshot.get("monsters",{})
	if not visible is Dictionary:
		return ERR_INVALID_DATA
	for id in _monsters.keys():
		if not visible.has(id):
			_monsters.erase(id)
	for raw_id in visible:
		var value = visible[raw_id]
		if not value is Dictionary:
			return ERR_INVALID_DATA
		var id := int(raw_id)
		var enemy_id := int(value.get("enemy_id",0))
		var state := int(value.get("state",-1))
		var facing := int(value.get("facing",0))
		var revision := int(value.get("revision",0))
		var tick := int(value.get("server_tick",-1))
		if id <= 0 or int(value.get("entity_id",0)) != id or not _definitions.has(enemy_id) or int(value.get("map_id",0)) != _map or state < STATE_IDLE or state > STATE_DEAD or facing < 1 or facing > 8 or revision <= 0 or tick < 0:
			return ERR_INVALID_DATA
		var old: Dictionary = _monsters.get(id,{})
		if not old.is_empty():
			var old_value: Dictionary = old.value
			if int(old_value.enemy_id) != enemy_id or int(old_value.source_index) != int(value.source_index) or int(old_value.max_hp) != int(value.max_hp):
				return ERR_INVALID_DATA
			if revision < int(old_value.revision):
				continue
		var action: String = _action(state)
		var generation_changed: bool = old.is_empty()
		var history = PositionHistory.new() if generation_changed else old.history
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
		var requested: String = str(monster.action)+str(int(value.facing))
		var resolved: Dictionary = _views.get(enemy_id,{}).get(requested,{})
		if resolved.is_empty():
			continue
		var definition: Dictionary = _definitions[enemy_id]
		var elapsed := maxi(0,now_usec-int(monster.started))*50/1000000
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
