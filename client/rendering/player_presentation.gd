extends RefCounted
## Presentation of the current provisional B base profile. No network or authority.
const Timeline = preload("res://rendering/action_timeline.gd")
const PositionHistory = preload("res://rendering/position_history.gd")
# Original slot groups; base art appears once, equipment overlay slots stay empty.
const PARTS := {
	12: ["players/b/base/leg", "B-LEG"],
	7: ["players/b/base/right_arm", "B-R-ARM"],
	9: ["players/b/base/body", "B-BODY"],
	5: ["players/b/base/left_arm", "B-L-ARM"],
	0: ["players/b/base/head", "B-HEAD"],
	1: ["players/b/hair/hair00", "B-HAIR00"],
}
var _views := {}
var _actors := {}
var _epoch := 0
var _map := 0

func clear() -> void:
	_views.clear()
	_actors.clear()
	_epoch = 0
	_map = 0

func configure(assets: Dictionary) -> Error:
	clear()
	for part in PARTS.values():
		if not assets.has(part[0]):
			clear()
			return ERR_INVALID_DATA
		var manifest: Dictionary = assets[part[0]].manifest
		for action in ["NS","NR"]:
			for facing in range(1,9):
				var key: String = str(part[1])+"_"+action+str(facing)
				var value: Dictionary = manifest.get("logical_views",{}).get(key,{})
				if not value.has_all(["clip","flip_h","timeline_ticks","loop","draw_slots"]) or not value.flip_h is bool or not value.loop is bool or not value.draw_slots is Array or not manifest.clips.has(value.clip):
					clear()
					return ERR_INVALID_DATA
				var present := {}
				for raw_slot in value.draw_slots:
					var slot := int(raw_slot)
					if float(raw_slot) != slot or slot < 0 or slot > 15 or present.has(slot):
						clear()
						return ERR_INVALID_DATA
					present[slot] = true
				for slot in PARTS:
					if not present.has(slot):
						clear()
						return ERR_INVALID_DATA
				var timeline := Timeline.new()
				if timeline.configure_ticks(value.timeline_ticks,value.loop) != OK:
					clear()
					return ERR_INVALID_DATA
				for step in value.timeline_ticks:
					if int(step[0]) >= manifest.clips[value.clip].size():
						clear()
						return ERR_INVALID_DATA
				_views[key] = {"view":value,"timeline":timeline}
	return OK

func apply_snapshot(snapshot: Dictionary, now_usec: int) -> void:
	if not snapshot.get("world_ready",false) or int(snapshot.get("map_epoch",0)) <= 0 or now_usec < 0:
		return
	var epoch := int(snapshot.map_epoch)
	var map := int(snapshot.map_id)
	if epoch != _epoch or map != _map:
		_actors.clear()
		_epoch = epoch
		_map = map
	var own: Dictionary = snapshot.self_state.duplicate()
	if own.is_empty():
		own = {"actor_id":snapshot.actor_id,"connection_epoch":0,"entity_map_epoch":epoch,
			"x":snapshot.x,"y":snapshot.y,"facing":snapshot.facing,"moving":false,
			"movement_action_sequence":0,"server_tick":int(snapshot.get("last_aoi_tick",0))}
	var visible: Dictionary = snapshot.entities.duplicate()
	visible[int(snapshot.actor_id)] = own
	for id in _actors.keys():
		if not visible.has(id):
			_actors.erase(id)
	for id in visible:
		var value: Dictionary = visible[id]
		var old: Dictionary = _actors.get(id,{})
		var action := "NR" if bool(value.moving) else "NS"
		var generation_changed: bool = old.is_empty() or int(old.value.connection_epoch) != int(value.connection_epoch) or int(old.value.entity_map_epoch) != int(value.entity_map_epoch)
		var restarted: bool = generation_changed or old.action != action or int(old.value.movement_action_sequence) != int(value.movement_action_sequence)
		var history = PositionHistory.new() if generation_changed else old.history
		var tick := int(value.get("server_tick",snapshot.get("last_aoi_tick",-1)))
		if tick < 0 or not history.push(tick,Vector2(float(value.x),float(value.y)),now_usec):
			continue
		_actors[id] = {"value":value,"action":action,"started":now_usec if restarted else int(old.started),"history":history}

func actors_at(now_usec: int) -> Array:
	var result: Array = []
	# Stable insertion order independent of AOI arrival and camera culling.
	var ids: Array = _actors.keys()
	ids.sort()
	for id in ids:
		var actor: Dictionary = _actors[id]
		var value: Dictionary = actor.value
		var requested := str(actor.action)+str(value.facing)
		var body: Dictionary = _views.get("B-BODY_"+requested,{})
		if body.is_empty():
			continue
		var parts: Array = []
		for slot in body.view.draw_slots:
			if not PARTS.has(int(slot)):
				continue
			var part: Array = PARTS[int(slot)]
			var resolved: Dictionary = _views[str(part[1])+"_"+requested]
			parts.append({"asset":part[0],"clip":resolved.view.clip,
				"frame":resolved.timeline.frame_at_update(maxi(0,now_usec-int(actor.started))*50/1000000),
				"flip_h":resolved.view.flip_h,"part_slot":int(slot)})
		var sampled: Vector2 = actor.history.sample(now_usec)
		if not sampled.is_finite():
			sampled = Vector2(float(value.x),float(value.y))
		result.append({"id":str(id),"p":sampled,"parts":parts})
	return result


func position_at(actor_id: int, now_usec: int) -> Vector2:
	var actor: Dictionary = _actors.get(actor_id,{})
	if actor.is_empty():
		return Vector2.INF
	var sampled: Vector2 = actor.history.sample(now_usec)
	return sampled if sampled.is_finite() else Vector2(float(actor.value.x),float(actor.value.y))
