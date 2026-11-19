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
var _seen_attack_events := {}
var _seen_damage_events := {}
var _seen_respawn_events := {}
var _attack_available := false
var _hit_available := false
var _death_available := false
var _epoch := 0
var _map := 0

func clear() -> void:
	_views.clear()
	_actors.clear()
	_seen_attack_events.clear()
	_seen_damage_events.clear()
	_seen_respawn_events.clear()
	_attack_available = false
	_hit_available = false
	_death_available = false
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
	# Current rebuilt Client has no equipment authority yet. The public recovered
	# baseline confirms unarmed normal attack uses source action AA. Treat AA as
	# optional until a full private pack is present; a partially-published AA set
	# is invalid rather than silently mixing parts/directions.
	var aa_parts := 0
	for part in PARTS.values():
		var manifest: Dictionary = assets[part[0]].manifest
		if manifest.get("logical_views",{}).has(str(part[1])+"_AA1"):
			aa_parts += 1
	if aa_parts != 0 and aa_parts != PARTS.size():
		clear()
		return ERR_INVALID_DATA
	_attack_available = aa_parts == PARTS.size()
	if _attack_available:
		for part in PARTS.values():
			var manifest: Dictionary = assets[part[0]].manifest
			for facing in range(1,9):
				var key: String = str(part[1])+"_AA"+str(facing)
				var value: Dictionary = manifest.get("logical_views",{}).get(key,{})
				if not value.has_all(["clip","flip_h","timeline_ticks","loop","draw_slots"]) or not value.flip_h is bool or not value.loop is bool or bool(value.loop) or not value.draw_slots is Array or not manifest.clips.has(value.clip):
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
				if timeline.configure_ticks(value.timeline_ticks,false) != OK:
					clear()
					return ERR_INVALID_DATA
				for step in value.timeline_ticks:
					if int(step[0]) >= manifest.clips[value.clip].size():
						clear()
						return ERR_INVALID_DATA
				_views[key] = {"view":value,"timeline":timeline}
	var hit_state := _load_optional_one_shot(assets,"NH")
	if hit_state < 0:
		clear()
		return ERR_INVALID_DATA
	_hit_available = hit_state == 1
	var death_state := _load_optional_one_shot(assets,"ND")
	if death_state < 0:
		clear()
		return ERR_INVALID_DATA
	_death_available = death_state == 1
	return OK

func _load_optional_one_shot(assets: Dictionary, action: String) -> int:
	var present_parts := 0
	for part in PARTS.values():
		var manifest: Dictionary = assets[part[0]].manifest
		if manifest.get("logical_views",{}).has(str(part[1])+"_"+action+"1"):
			present_parts += 1
	if present_parts == 0:
		return 0
	if present_parts != PARTS.size():
		return -1
	for part in PARTS.values():
		var manifest: Dictionary = assets[part[0]].manifest
		for facing in range(1,9):
			var key: String = str(part[1])+"_"+action+str(facing)
			var value: Dictionary = manifest.get("logical_views",{}).get(key,{})
			if not value.has_all(["clip","flip_h","timeline_ticks","loop","draw_slots"]) or not value.flip_h is bool or not value.loop is bool or bool(value.loop) or not value.draw_slots is Array or not manifest.clips.has(value.clip):
				return -1
			var slots := {}
			for raw_slot in value.draw_slots:
				var slot := int(raw_slot)
				if float(raw_slot) != slot or slot < 0 or slot > 15 or slots.has(slot):
					return -1
				slots[slot] = true
			for slot in PARTS:
				if not slots.has(slot):
					return -1
			var timeline := Timeline.new()
			if timeline.configure_ticks(value.timeline_ticks,false) != OK:
				return -1
			for step in value.timeline_ticks:
				if int(step[0]) >= manifest.clips[value.clip].size():
					return -1
			_views[key] = {"view":value,"timeline":timeline}
	return 1

func apply_snapshot(snapshot: Dictionary, now_usec: int) -> void:
	if not snapshot.get("world_ready",false) or int(snapshot.get("map_epoch",0)) <= 0 or now_usec < 0:
		return
	var epoch := int(snapshot.map_epoch)
	var map := int(snapshot.map_id)
	if epoch != _epoch or map != _map:
		_actors.clear()
		_seen_attack_events.clear()
		_seen_damage_events.clear()
		_seen_respawn_events.clear()
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
		var base_action := "NR" if bool(value.moving) else "NS"
		var generation_changed: bool = old.is_empty() or int(old.value.connection_epoch) != int(value.connection_epoch) or int(old.value.entity_map_epoch) != int(value.entity_map_epoch)
		var action := base_action
		var action_facing := int(value.facing)
		var started := now_usec
		var dead_visual := false
		if not generation_changed:
			started = int(old.started)
			dead_visual = bool(old.get("dead_visual",false))
			if dead_visual and _death_available:
				action = "ND"
				action_facing = int(old.action_facing)
			elif _one_shot_active(old,"NH",_hit_available,now_usec):
				action = "NH"
				action_facing = int(old.action_facing)
			elif _attack_active(old,now_usec):
				action = "AA"
				action_facing = int(old.action_facing)
			elif str(old.action) != base_action or int(old.value.movement_action_sequence) != int(value.movement_action_sequence):
				started = now_usec
		var history = PositionHistory.new() if generation_changed else old.history
		var tick := int(value.get("server_tick",snapshot.get("last_aoi_tick",-1)))
		if tick < 0 or not history.push(tick,Vector2(float(value.x),float(value.y)),now_usec):
			continue
		_actors[id] = {"value":value,"action":action,"action_facing":action_facing,
			"started":started,"history":history,"dead_visual":dead_visual}

	var events = snapshot.get("player_attack_events",[])
	if events is Array:
		for event in events:
			if not event is Dictionary:
				continue
			var attacker := int(event.get("attacker_actor_id",0))
			var connection := int(event.get("attacker_connection_epoch",0))
			var sequence := int(event.get("attack_sequence",0))
			var key := "%d:%d:%d" % [attacker,connection,sequence]
			if _seen_attack_events.has(key):
				continue
			_seen_attack_events[key] = true
			if not _attack_available or not _actors.has(attacker):
				continue
			var actor: Dictionary = _actors[attacker]
			var actor_connection := int(actor.value.connection_epoch)
			var own_without_self_state := attacker == int(snapshot.actor_id) and actor_connection == 0
			if actor_connection != connection and not own_without_self_state:
				continue
			actor.action = "AA"
			actor.action_facing = int(event.get("facing",actor.value.facing))
			actor.started = now_usec
			_actors[attacker] = actor

	var damage_events = snapshot.get("player_damage_visual_events",[])
	if damage_events is Array:
		for event in damage_events:
			if not event is Dictionary:
				continue
			var victim := int(event.get("victim_actor_id",0))
			var connection := int(event.get("victim_connection_epoch",0))
			var sequence := int(event.get("damage_sequence",0))
			var key := "%d:%d:%d" % [victim,connection,sequence]
			if _seen_damage_events.has(key):
				continue
			_seen_damage_events[key] = true
			if not _actors.has(victim):
				continue
			var actor: Dictionary = _actors[victim]
			var actor_connection := int(actor.value.connection_epoch)
			var own_without_self_state := victim == int(snapshot.actor_id) and actor_connection == 0
			if actor_connection != connection and not own_without_self_state:
				continue
			var died := bool(event.get("died",false))
			if died:
				actor.dead_visual = true
				if _death_available:
					actor.action = "ND"
					actor.action_facing = int(event.get("facing",actor.value.facing))
					actor.started = now_usec
			elif _hit_available and not bool(actor.get("dead_visual",false)):
				actor.action = "NH"
				actor.action_facing = int(event.get("facing",actor.value.facing))
				actor.started = now_usec
			_actors[victim] = actor

	var pvp_events=snapshot.get("pvp_combat_visual_events",[])
	if pvp_events is Array:
		for event in pvp_events:
			if not event is Dictionary:
				continue
			var attacker:=int(event.get("attacker_actor_id",0))
			var attacker_connection:=int(event.get("attacker_connection_epoch",0))
			var attack_sequence:=int(event.get("attack_sequence",0))
			var victim:=int(event.get("victim_actor_id",0))
			var victim_connection:=int(event.get("victim_connection_epoch",0))
			var damage_sequence:=int(event.get("damage_sequence",0))
			var attack_key:="pvp:%d:%d:%d" % [attacker,attacker_connection,attack_sequence]
			if not _seen_attack_events.has(attack_key):
				_seen_attack_events[attack_key]=true
				if _attack_available and _actors.has(attacker):
					var attacker_actor:Dictionary=_actors[attacker]
					var current_connection:=int(attacker_actor.value.connection_epoch)
					var own_without_self_state:=attacker==int(snapshot.actor_id) and current_connection==0
					if current_connection==attacker_connection or own_without_self_state:
						attacker_actor.action="AA"
						attacker_actor.action_facing=int(event.get("attacker_facing",attacker_actor.value.facing))
						attacker_actor.started=now_usec
						_actors[attacker]=attacker_actor
			var damage_key:="pvp:%d:%d:%d" % [victim,victim_connection,damage_sequence]
			if not _seen_damage_events.has(damage_key):
				_seen_damage_events[damage_key]=true
				if _actors.has(victim):
					var victim_actor:Dictionary=_actors[victim]
					var current_connection:=int(victim_actor.value.connection_epoch)
					var own_without_self_state:=victim==int(snapshot.actor_id) and current_connection==0
					if current_connection==victim_connection or own_without_self_state:
						if bool(event.get("died",false)):
							victim_actor.dead_visual=true
							if _death_available:
								victim_actor.action="ND"
								victim_actor.action_facing=int(event.get("victim_facing",victim_actor.value.facing))
								victim_actor.started=now_usec
						elif _hit_available and not bool(victim_actor.get("dead_visual",false)):
							victim_actor.action="NH"
							victim_actor.action_facing=int(event.get("victim_facing",victim_actor.value.facing))
							victim_actor.started=now_usec
						_actors[victim]=victim_actor

	var respawn_events = snapshot.get("player_respawn_visual_events",[])
	if respawn_events is Array:
		for event in respawn_events:
			if not event is Dictionary:
				continue
			var revived := int(event.get("actor_id",0))
			var connection := int(event.get("connection_epoch",0))
			var sequence := int(event.get("respawn_sequence",0))
			var key := "%d:%d:%d" % [revived,connection,sequence]
			if _seen_respawn_events.has(key):
				continue
			_seen_respawn_events[key] = true
			if not _actors.has(revived):
				continue
			var actor: Dictionary = _actors[revived]
			var actor_connection := int(actor.value.connection_epoch)
			var own_without_self_state := revived == int(snapshot.actor_id) and actor_connection == 0
			if actor_connection != connection and not own_without_self_state:
				continue
			actor.dead_visual = false
			actor.action = "NR" if bool(actor.value.moving) else "NS"
			actor.action_facing = int(event.get("facing",actor.value.facing))
			actor.started = now_usec
			_actors[revived] = actor

func _one_shot_active(actor: Dictionary,action: String,available: bool,now_usec: int) -> bool:
	if not available or str(actor.get("action","")) != action:
		return false
	var facing := int(actor.get("action_facing",0))
	var body: Dictionary = _views.get("B-BODY_"+action+str(facing),{})
	if body.is_empty():
		return false
	var elapsed := maxi(0,now_usec-int(actor.get("started",now_usec)))*50/1000000
	return not body.timeline.is_complete(elapsed)

func _attack_active(actor: Dictionary,now_usec: int) -> bool:
	if not _attack_available or str(actor.get("action","")) != "AA":
		return false
	var facing := int(actor.get("action_facing",0))
	var body: Dictionary = _views.get("B-BODY_AA"+str(facing),{})
	if body.is_empty():
		return false
	var elapsed := maxi(0,now_usec-int(actor.get("started",now_usec)))*50/1000000
	return not body.timeline.is_complete(elapsed)


func actors_at(now_usec: int) -> Array:
	var result: Array = []
	# Stable insertion order independent of AOI arrival and camera culling.
	var ids: Array = _actors.keys()
	ids.sort()
	for id in ids:
		var actor: Dictionary = _actors[id]
		var value: Dictionary = actor.value
		var action := str(actor.action)
		var facing := int(actor.action_facing)
		var elapsed := maxi(0,now_usec-int(actor.started))*50/1000000
		if action in ["AA","NH"]:
			var one_shot_body: Dictionary = _views.get("B-BODY_"+action+str(facing),{})
			if one_shot_body.is_empty() or one_shot_body.timeline.is_complete(elapsed):
				action = "NR" if bool(value.moving) else "NS"
				facing = int(value.facing)
				elapsed = 0
		var requested := action+str(facing)
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
				"frame":resolved.timeline.frame_at_update(elapsed),
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
