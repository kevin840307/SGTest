extends RefCounted

# Pure data reducer. No SceneTree, texture, Timer or authoritative game logic.
# Renders may interpolate, but server epochs/sequences decide valid state.
var map_epoch: int = 1
var local_entity_id: int = 0
var latest_server_tick: int = 0
var entities: Dictionary = {}
var _tombstones: Dictionary = {}

func reset_map(epoch: int) -> void:
	map_epoch = epoch
	entities.clear()
	_tombstones.clear()
	local_entity_id = 0
	latest_server_tick = 0

func apply(packet: Dictionary) -> bool:
	var kind: String = str(packet.get("type", ""))
	var epoch: int = int(packet.get("map_epoch", map_epoch))
	if kind == "welcome":
		if epoch != map_epoch:
			reset_map(epoch)
		local_entity_id = int(packet.get("owned_id", 0))
		latest_server_tick = maxi(latest_server_tick, int(packet.get("server_tick", 0)))
		return true
	if epoch != map_epoch:
		return false
	latest_server_tick = maxi(latest_server_tick, int(packet.get("server_tick", 0)))
	var entity_id: int = int(packet.get("entity_id", 0))
	if entity_id <= 0:
		return false

	if kind == "spawn":
		var revision: int = int(packet.get("entity_revision", 1))
		if revision <= int(_tombstones.get(entity_id, -1)):
			return false
		if entities.has(entity_id):
			var present: Dictionary = entities[entity_id]
			if revision <= int(present.get("revision", 0)):
				return false
		entities[entity_id] = {
			"revision": revision,
			"name": str(packet.get("name", "Entity")),
			"x": float(packet.get("x", 0.0)),
			"y": float(packet.get("y", 0.0)),
			"facing": int(packet.get("facing", 0)),
			"move_seq": 0,
			"movement": {},
			"action_seq": 0,
			"action": "",
			"hp": int(packet.get("hp", 100))
		}
		return true

	if kind == "despawn":
		var end_revision: int = int(packet.get("entity_revision", 0))
		if entities.has(entity_id):
			var current: Dictionary = entities[entity_id]
			if end_revision < int(current.get("revision", 0)):
				return false
			entities.erase(entity_id)
		_tombstones[entity_id] = maxi(end_revision, int(_tombstones.get(entity_id, -1)))
		return true

	if not entities.has(entity_id):
		return false
	var entity: Dictionary = entities[entity_id]
	var incoming_revision: int = int(packet.get("entity_revision", entity.get("revision", 1)))
	if incoming_revision != int(entity.get("revision", 1)):
		return false

	if kind == "move":
		var seq: int = int(packet.get("movement_seq", 0))
		if seq <= int(entity["move_seq"]):
			return false
		var duration: int = int(packet.get("duration_ticks", 0))
		if duration <= 0:
			return false
		entity["move_seq"] = seq
		entity["facing"] = int(packet.get("facing", entity["facing"]))
		entity["x"] = float(packet.get("start_x", entity["x"]))
		entity["y"] = float(packet.get("start_y", entity["y"]))
		entity["movement"] = {
			"start_x": float(packet.get("start_x", entity["x"])),
			"start_y": float(packet.get("start_y", entity["y"])),
			"end_x": float(packet.get("end_x", entity["x"])),
			"end_y": float(packet.get("end_y", entity["y"])),
			"start_tick": int(packet.get("start_tick", latest_server_tick)),
			"duration_ticks": duration
		}
		return true

	if kind == "stop":
		var seq_stop: int = int(packet.get("movement_seq", 0))
		if seq_stop <= int(entity["move_seq"]):
			return false
		entity["move_seq"] = seq_stop
		entity["x"] = float(packet.get("x", entity["x"]))
		entity["y"] = float(packet.get("y", entity["y"]))
		entity["movement"] = {}
		entity["facing"] = int(packet.get("facing", entity["facing"]))
		return true

	if kind == "action":
		var action_seq: int = int(packet.get("action_seq", 0))
		if action_seq <= int(entity["action_seq"]):
			return false
		entity["action_seq"] = action_seq
		entity["action"] = str(packet.get("action", ""))
		entity["facing"] = int(packet.get("facing", entity["facing"]))
		return true

	if kind == "hp":
		var event_seq: int = int(packet.get("action_seq", 0))
		if event_seq <= int(entity["action_seq"]):
			return false
		entity["action_seq"] = event_seq
		entity["hp"] = int(packet.get("hp", entity["hp"]))
		return true
	return false

func visual_position(entity_id: int, render_tick: float) -> Vector2:
	if not entities.has(entity_id):
		return Vector2.ZERO
	var entity: Dictionary = entities[entity_id]
	var base := Vector2(float(entity["x"]), float(entity["y"]))
	var movement: Dictionary = entity["movement"]
	if movement.is_empty():
		return base
	var t: float = clampf(
		(render_tick - float(movement["start_tick"])) / float(movement["duration_ticks"]),
		0.0, 1.0
	)
	return Vector2(
		lerpf(float(movement["start_x"]), float(movement["end_x"]), t),
		lerpf(float(movement["start_y"]), float(movement["end_y"]), t)
	)
