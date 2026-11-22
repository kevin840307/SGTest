extends RefCounted
## Shared multi-profile paper doll presentation. No loading or gameplay authority.
const Profile = preload("res://player/character_appearance_profile.gd")
const PositionHistory = preload("res://movement/position_history.gd")
const PARTS = Profile.BASE_PARTS
# Recovered's paper-doll walk clips use an 8 FPS visual cadence. Speed up
# SGTest's slower normalized NR timeline without changing Server movement.
const WALK_PLAYBACK_NUMERATOR := 5
const WALK_PLAYBACK_DENOMINATOR := 4
var _views: Dictionary = {}
var _profiles: Dictionary = {}
var _default_profile_key := "0:0:0:0:0"
var _actors := {}
var _seen_attack_events := {}
var _seen_damage_events := {}
var _seen_respawn_events := {}
var _epoch := 0
var _map := 0

func clear() -> void:
	_views.clear()
	_profiles.clear()
	_default_profile_key = "0:0:0:0:0"
	_actors.clear()
	_seen_attack_events.clear()
	_seen_damage_events.clear()
	_seen_respawn_events.clear()
	_epoch = 0
	_map = 0

func configure(assets: Dictionary, appearance: Dictionary = {}) -> Error:
	clear()
	var error := add_profile(assets,appearance)
	if error == OK:
		_default_profile_key = str(Profile.definition(appearance).key)
	return error

func add_profile(assets: Dictionary, appearance: Dictionary) -> Error:
	var data := Profile.definition(appearance)
	if data.is_empty():
		return ERR_INVALID_PARAMETER
	if _profiles.has(str(data.key)):
		return OK
	var profile := Profile.new()
	var error := profile.configure(assets,appearance,_views)
	if error != OK:
		return error
	_views.merge(profile.views)
	_profiles[profile.key] = profile
	return OK

func has_profile(appearance: Dictionary) -> bool:
	var key := Profile.appearance_key(appearance)
	return not key.is_empty() and _profiles.has(key)

func _profile_for(value: Dictionary) -> RefCounted:
	if not value.has("appearance"):
		return _profiles.get(_default_profile_key)
	if not value.appearance is Dictionary:
		return null
	var key := Profile.appearance_key(value.appearance)
	return null if key.is_empty() else _profiles.get(key)

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
		var profile = _profile_for(value)
		if profile == null:
			_actors.erase(id)
			continue
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
			if dead_visual and profile.death_available:
				action = "ND"
				action_facing = int(old.action_facing)
			elif profile.hit_available and _one_shot_active(old,"NH",old.profile.hit_available,now_usec):
				action = "NH"
				action_facing = int(old.action_facing)
			elif profile.attack_available and _attack_active(old,now_usec):
				action = "AA"
				action_facing = int(old.action_facing)
			elif str(old.action) != base_action:
				# A new MoveTarget sequence changes a destination, not the
				# walking animation. Repeated long-hold repaths must not
				# restart the NR clip at its first frame.
				started = now_usec
		var history = PositionHistory.new() if generation_changed else old.history
		var tick := int(value.get("server_tick",snapshot.get("last_aoi_tick",-1)))
		if tick < 0 or not history.push(tick,Vector2(float(value.x),float(value.y)),now_usec,bool(value.moving)):
			continue
		_actors[id] = {"value":value,"action":action,"action_facing":action_facing,
			"started":started,"history":history,"dead_visual":dead_visual,"profile":profile}

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
			if not _actors.has(attacker) or not _actors[attacker].profile.attack_available:
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
				if actor.profile.death_available:
					actor.action = "ND"
					actor.action_facing = int(event.get("facing",actor.value.facing))
					actor.started = now_usec
			elif actor.profile.hit_available and not bool(actor.get("dead_visual",false)):
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
				if _actors.has(attacker) and _actors[attacker].profile.attack_available:
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
							if victim_actor.profile.death_available:
								victim_actor.action="ND"
								victim_actor.action_facing=int(event.get("victim_facing",victim_actor.value.facing))
								victim_actor.started=now_usec
						elif victim_actor.profile.hit_available and not bool(victim_actor.get("dead_visual",false)):
							victim_actor.action="NH"
							victim_actor.action_facing=int(event.get("victim_facing",victim_actor.value.facing))
							victim_actor.started=now_usec
						_actors[victim]=victim_actor

	var pvp_skill_events=snapshot.get("pvp_skill_visual_events",[])
	if pvp_skill_events is Array:
		for event in pvp_skill_events:
			if not event is Dictionary:
				continue
			var attacker:=int(event.get("attacker_actor_id",0))
			var ac:=int(event.get("attacker_connection_epoch",0))
			var skill_sequence:=int(event.get("skill_sequence",0))
			var victim:=int(event.get("victim_actor_id",0))
			var vc:=int(event.get("victim_connection_epoch",0))
			var damage_sequence:=int(event.get("damage_sequence",0))
			var attack_key:="pvpskill:%d:%d:%d" % [attacker,ac,skill_sequence]
			if not _seen_attack_events.has(attack_key):
				_seen_attack_events[attack_key]=true
				if _actors.has(attacker) and _actors[attacker].profile.attack_available:
					var actor:Dictionary=_actors[attacker]
					var current_connection:=int(actor.value.connection_epoch)
					var own_without_self_state:=attacker==int(snapshot.actor_id) and current_connection==0
					if current_connection==ac or own_without_self_state:
						actor.action="AA"
						actor.action_facing=int(event.get("attacker_facing",actor.value.facing))
						actor.started=now_usec
						_actors[attacker]=actor
			var damage_key:="pvpskill:%d:%d:%d" % [victim,vc,damage_sequence]
			if not _seen_damage_events.has(damage_key):
				_seen_damage_events[damage_key]=true
				if _actors.has(victim):
					var actor:Dictionary=_actors[victim]
					var current_connection:=int(actor.value.connection_epoch)
					var own_without_self_state:=victim==int(snapshot.actor_id) and current_connection==0
					if current_connection==vc or own_without_self_state:
						if bool(event.get("died",false)):
							actor.dead_visual=true
							if actor.profile.death_available:
								actor.action="ND"
								actor.action_facing=int(event.get("victim_facing",actor.value.facing))
								actor.started=now_usec
						elif actor.profile.hit_available and not bool(actor.get("dead_visual",false)):
							actor.action="NH"
							actor.action_facing=int(event.get("victim_facing",actor.value.facing))
							actor.started=now_usec
						_actors[victim]=actor

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
	var body: Dictionary = actor.profile.views.get(actor.profile.body_prefix+"_"+action+str(facing),{})
	if body.is_empty():
		return false
	var elapsed := maxi(0,now_usec-int(actor.get("started",now_usec)))*50/1000000
	return not body.timeline.is_complete(elapsed)

func _attack_active(actor: Dictionary,now_usec: int) -> bool:
	if not actor.profile.attack_available or str(actor.get("action","")) != "AA":
		return false
	var facing := int(actor.get("action_facing",0))
	var body: Dictionary = actor.profile.views.get(actor.profile.body_prefix+"_AA"+str(facing),{})
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
		var profile = actor.profile
		var action := str(actor.action)
		var facing := int(actor.action_facing)
		var elapsed := maxi(0,now_usec-int(actor.started))*50/1000000
		if action in ["AA","NH"]:
			var one_shot_body: Dictionary = profile.views.get(profile.body_prefix+"_"+action+str(facing),{})
			if one_shot_body.is_empty() or one_shot_body.timeline.is_complete(elapsed):
				action = "NR" if bool(value.moving) else "NS"
				facing = int(value.facing)
				elapsed = 0
		# Animation timing is presentation-only and independent of velocity,
		# frame rate and incoming MoveTarget sequence changes.
		if action == "NR":
			elapsed = elapsed * WALK_PLAYBACK_NUMERATOR / WALK_PLAYBACK_DENOMINATOR
		var requested := action+str(facing)
		var body: Dictionary = profile.views.get(profile.body_prefix+"_"+requested,{})
		if body.is_empty():
			continue
		var parts: Array = []
		for slot in body.view.draw_slots:
			var logical_slot := int(slot)
			if profile.parts.has(logical_slot):
				var part: Array = profile.parts[logical_slot]
				var resolved: Dictionary = profile.views[str(part[1])+"_"+requested]
				parts.append({"asset":part[0],"clip":resolved.view.clip,
					"frame":resolved.timeline.frame_at_update(elapsed),
					"flip_h":resolved.view.flip_h,"part_slot":logical_slot,
					"palette_index":profile.hair_color if logical_slot==1 else 0})
			# Recovered paper doll draws Standard first, then the equipment
			# overlay for the same logical body/leg slot.
			if profile.overlays.has(logical_slot):
				var overlay: Array = profile.overlays[logical_slot]
				var overlay_view: Dictionary = profile.views[str(overlay[1])+"_"+requested]
				parts.append({"asset":overlay[0],"clip":overlay_view.view.clip,
					"frame":overlay_view.timeline.frame_at_update(elapsed),
					"flip_h":overlay_view.view.flip_h,"part_slot":logical_slot,
					"palette_index":int(overlay[2]) if overlay.size()>2 else 0})
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
