extends RefCounted
## Shared multi-profile paper doll presentation. No loading or gameplay authority.
const Profile = preload("res://player/character_appearance_profile.gd")
const PositionHistory = preload("res://movement/position_history.gd")
const LocalMovement = preload("res://movement/local_movement.gd")
const PARTS = Profile.BASE_PARTS
# Recovered2 uses one synchronized 8 FPS SpriteFrames clock for walking.
# Keep this presentation cadence independent from movement speed, position
# delta, network tick, repath frequency, and Server correction magnitude.
# Future mecha/mount renderers must consume this same fixed cadence contract.
const WALK_FRAME_USEC := 125000
# Paradise.exe _EMOTACTLIB selection order. Emotion IDs are 1-based on wire/UI.
const EMOTION_BY_ID := ["ZH","ZK","ZI","ZJ","ZL","ZB","ZC","ZG","ZA","ZE","ZD","ZF"]
# Peer walking remains visual-only. Keep playing through short AOI gaps
# instead of waiting on every authoritative coordinate update.
const REMOTE_MAX_PREDICTION_TICKS := 4.0
const REMOTE_MAX_PREDICTION_PIXELS := 32.0
# Sparse checkpoints remain a fallback, but a trusted movement intent may
# reconstruct the same Blocking.dat route continuously. Only a large mismatch
# abandons that visual route and re-anchors to Server state.
const REMOTE_ROUTE_RECONCILE_PIXELS := 96.0
# Presentation replay memory is bounded like WorldView's combat overlay ring.
# This changes neither Server authority nor network reliability semantics.
const COMBAT_VISUAL_DEDUPE_MAX := 32768
var _views: Dictionary = {}
var _profiles: Dictionary = {}
var _default_profile_key := "0:0:0:0:0:0"
var _actors := {}
var _seen_visual_events := {}
var _seen_visual_ring: Array[String] = []
var _seen_visual_ring_head := 0
var _epoch := 0
var _map := 0
var _last_snapshot_tick := -1
var _local_visual: Dictionary = {} # own actor presentation only; never authoritative
var _own_actor_id := 0
var _remote_blocking := PackedByteArray()
var _remote_manifest: Dictionary = {}
var _remote_routes: Dictionary = {}

func clear() -> void:
	_views.clear()
	_profiles.clear()
	_default_profile_key = "0:0:0:0:0:0"
	_actors.clear()
	_clear_visual_event_history()
	_epoch = 0
	_map = 0
	_last_snapshot_tick = -1
	_local_visual.clear()
	_own_actor_id = 0
	_remote_blocking.clear()
	_remote_manifest.clear()
	_remote_routes.clear()


func _clear_visual_event_history() -> void:
	_seen_visual_events.clear()
	_seen_visual_ring.clear()
	_seen_visual_ring_head = 0

func _remember_visual_event(domain: String, event_key: String) -> bool:
	# Keep the three animation namespaces independent, and evict in O(1).
	# Old keys may eventually be evicted during a very long session; within
	# the bounded reliable-replay window duplicates cannot restart an animation.
	var scoped_key := domain + "|" + event_key
	if _seen_visual_events.has(scoped_key):
		return false
	if _seen_visual_ring.size() == COMBAT_VISUAL_DEDUPE_MAX:
		_seen_visual_events.erase(_seen_visual_ring[_seen_visual_ring_head])
		_seen_visual_ring[_seen_visual_ring_head] = scoped_key
		_seen_visual_ring_head = (_seen_visual_ring_head + 1) % COMBAT_VISUAL_DEDUPE_MAX
	else:
		_seen_visual_ring.append(scoped_key)
	_seen_visual_events[scoped_key] = true
	return true


func _visual_actor_matches_generation(actor_id: int, connection_epoch: int) -> bool:
	if not _actors.has(actor_id):
		return false
	var actor: Dictionary = _actors[actor_id]
	var current_epoch := int(actor.value.connection_epoch)
	# An own-actor WorldReady fallback may precede the first SelfState.
	return current_epoch == connection_epoch or (actor_id == _own_actor_id and current_epoch == 0)


func configure(assets: Dictionary, appearance: Dictionary = {}) -> Error:
	clear()
	var error := add_profile(assets,appearance)
	if error == OK:
		_default_profile_key = str(Profile.definition(appearance).key)
	return error

func configure_remote_routes(blocking: PackedByteArray, manifest: Dictionary) -> bool:
	_remote_blocking.clear()
	_remote_manifest.clear()
	_remote_routes.clear()
	# Validate the exact same collision contract once before any peer route is
	# reconstructed. A bad/missing map simply falls back to checkpoint history.
	var probe = LocalMovement.new()
	if not probe.configure(blocking,manifest,1,1):
		return false
	_remote_blocking = blocking.duplicate()
	_remote_manifest = manifest.duplicate(true)
	return true


func _remote_intent_for(snapshot: Dictionary, actor_id: int, value: Dictionary) -> Dictionary:
	if actor_id == _own_actor_id:
		return {}
	var intents: Variant = snapshot.get("remote_movement_intents",{})
	if not intents is Dictionary:
		return {}
	var intent: Variant = intents.get(actor_id,{})
	if not intent is Dictionary or intent.is_empty():
		return {}
	if int(intent.get("actor_id",0)) != actor_id or \
			int(intent.get("connection_epoch",0)) != int(value.get("connection_epoch",-1)) or \
			int(intent.get("entity_map_epoch",0)) != int(value.get("entity_map_epoch",-1)):
		return {}
	return intent


func _route_generation_matches(route: Dictionary, value: Dictionary) -> bool:
	return not route.is_empty() and \
		int(route.get("connection_epoch",0)) == int(value.get("connection_epoch",-1)) and \
		int(route.get("entity_map_epoch",0)) == int(value.get("entity_map_epoch",-1))


func _sync_remote_route(actor_id: int, value: Dictionary, intent: Dictionary) -> void:
	if actor_id == _own_actor_id or intent.is_empty() or _remote_blocking.is_empty() or _remote_manifest.is_empty():
		_remote_routes.erase(actor_id)
		return
	var authoritative := Vector2(float(value.get("x",INF)),float(value.get("y",INF)))
	var target := Vector2(float(intent.get("target_x",INF)),float(intent.get("target_y",INF)))
	var heading := int(intent.get("facing",0))
	var sequence := int(intent.get("movement_action_sequence",0))
	if not authoritative.is_finite() or not target.is_finite() or heading < 1 or heading > 8 or sequence <= 0:
		_remote_routes.erase(actor_id)
		return
	var route: Dictionary = _remote_routes.get(actor_id,{})
	var same_generation := _route_generation_matches(route,value)
	var controller = route.get("controller") if same_generation else null
	if not bool(intent.get("moving",false)):
		if controller == null:
			controller = LocalMovement.new()
			if not controller.configure(_remote_blocking,_remote_manifest,int(value.entity_map_epoch),actor_id):
				_remote_routes.erase(actor_id)
				return
		if not controller.seed_visual(authoritative,heading):
			_remote_routes.erase(actor_id)
			return
		_remote_routes[actor_id] = {"controller":controller,
			"connection_epoch":int(value.connection_epoch),
			"entity_map_epoch":int(value.entity_map_epoch),"sequence":sequence,
			"target":target}
		return
	var route_changed := controller == null or int(route.get("sequence",0)) != sequence or Vector2(route.get("target",Vector2.INF)) != target
	if controller == null:
		controller = LocalMovement.new()
		if not controller.configure(_remote_blocking,_remote_manifest,int(value.entity_map_epoch),actor_id):
			_remote_routes.erase(actor_id)
			return
	var anchor := authoritative
	if same_generation and controller.visual_position.is_finite():
		anchor = controller.visual_position
	if not route_changed:
		# Checkpoints are deliberately sparse. Do not rewind a healthy route for
		# normal network delay; only a true divergence re-seeds from authority.
		if controller.position.is_finite() and controller.position.distance_to(authoritative) <= REMOTE_ROUTE_RECONCILE_PIXELS:
			return
		anchor = authoritative
	if not controller.seed_visual(anchor,heading) or not controller.request_target(target,sequence):
		_remote_routes.erase(actor_id)
		return
	_remote_routes[actor_id] = {"controller":controller,
		"connection_epoch":int(value.connection_epoch),
		"entity_map_epoch":int(value.entity_map_epoch),"sequence":sequence,
		"target":target}


func advance_remote_visual(delta: float) -> void:
	if delta <= 0.0:
		return
	for raw_id in _remote_routes.keys():
		var actor_id := int(raw_id)
		if not _actors.has(actor_id):
			_remote_routes.erase(actor_id)
			continue
		var route: Dictionary = _remote_routes[actor_id]
		var controller = route.get("controller")
		if controller == null:
			_remote_routes.erase(actor_id)
			continue
		controller.advance(delta)
		controller.advance_visual(delta)
		var actor: Dictionary = _actors[actor_id]
		var was_moving := bool(actor.value.get("moving",false))
		actor.value["moving"] = bool(controller.moving)
		actor.value["facing"] = int(controller.facing)
		if str(actor.action) in ["NS","NR"]:
			if bool(controller.moving) and not was_moving:
				actor.walk_started = Time.get_ticks_usec()
			actor.action = "NR" if bool(controller.moving) else "NS"
			actor.action_facing = int(controller.facing)
		_actors[actor_id] = actor


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
	# WorldState ordinarily rejects delayed map snapshots, but presentation
	# must not roll back to a previous map if a queued frame arrives late.
	if _epoch > 0 and epoch < _epoch:
		return
	var map := int(snapshot.map_id)
	var snapshot_tick := int(snapshot.get("last_aoi_tick",-1))
	# The reducer's accepted AOI tick is the ordering fence for the whole
	# visible set, including peers removed by a newer Despawn. Individual
	# actor history cannot protect an actor that was already erased.
	if epoch == _epoch and map == _map and snapshot_tick >= 0 and _last_snapshot_tick >= 0 and snapshot_tick < _last_snapshot_tick:
		return
	_own_actor_id = int(snapshot.actor_id)
	if epoch != _epoch or map != _map:
		_actors.clear()
		_remote_routes.clear()
		_clear_visual_event_history()
		_epoch = epoch
		_map = map
		_last_snapshot_tick = -1
	if snapshot_tick >= 0:
		_last_snapshot_tick = maxi(_last_snapshot_tick,snapshot_tick)
	var own: Dictionary = snapshot.self_state.duplicate()
	if own.is_empty():
		own = {"actor_id":snapshot.actor_id,"connection_epoch":0,"entity_map_epoch":epoch,
			"x":snapshot.x,"y":snapshot.y,"facing":snapshot.facing,"moving":false,
			"movement_action_sequence":0,"server_tick":int(snapshot.get("last_aoi_tick",0))}
	var selected_appearance: Variant = snapshot.get("selected_character_appearance",{})
	if not own.has("appearance") and selected_appearance is Dictionary and not selected_appearance.is_empty():
		own["appearance"] = selected_appearance.duplicate(true)
	# Apply the immediate local NR state AFTER constructing WorldReady fallback.
	# Previously each SelfState-less snapshot forced NS and reset the walk
	# clock every ~50ms, so the actor appeared to slide on its first steps.
	if not _local_visual.is_empty() and int(_local_visual.get("actor_id",0)) == _own_actor_id:
		own["moving"] = bool(_local_visual.moving)
		own["facing"] = int(_local_visual.facing)
	var visible: Dictionary = snapshot.entities.duplicate()
	visible[int(snapshot.actor_id)] = own
	for id in _actors.keys():
		if not visible.has(id):
			_actors.erase(id)
			_remote_routes.erase(id)
	for id in visible:
		# Presentation may use authoritative intent state without ever mutating
		# the reducer snapshot shared with gameplay/UI consumers.
		var value: Dictionary = (visible[id] as Dictionary).duplicate(true)
		var tick := int(value.get("server_tick",snapshot.get("last_aoi_tick",-1)))
		var previous: Dictionary = _actors.get(id,{})
		if not previous.is_empty():
			var accepted: Dictionary = previous.value
			# An AOI set can have a fresh set tick but still carry an older
			# entity record. Reject the stale peer *before* applying movement
			# intent; PositionHistory.push alone rejects only coordinates and
			# cannot undo a reversed/erased visual route.
			if tick >= 0 and int(accepted.get("server_tick",-1)) > tick and \
					int(accepted.get("connection_epoch",-1)) == int(value.get("connection_epoch",-2)) and \
					int(accepted.get("entity_map_epoch",-1)) == int(value.get("entity_map_epoch",-2)):
				continue
		var intent := _remote_intent_for(snapshot,int(id),value)
		_sync_remote_route(int(id),value,intent)
		var route: Dictionary = _remote_routes.get(int(id),{})
		if not route.is_empty():
			var route_controller = route.get("controller")
			if route_controller != null:
				value["moving"] = bool(route_controller.moving)
				value["facing"] = int(route_controller.facing)
		var profile = _profile_for(value)
		if profile == null:
			# Recovered retains an existing paper doll while cosmetics/loadouts
			# update. The old code erased the entire actor on every snapshot
			# until an asynchronous appearance profile finished loading.
			var retained: Dictionary = _actors.get(id,{})
			if not retained.is_empty() and \
					int(retained.value.connection_epoch)==int(value.connection_epoch) and \
					int(retained.value.entity_map_epoch)==int(value.entity_map_epoch):
				profile = retained.profile
			if profile == null and int(id)==_own_actor_id and not value.has("appearance"):
				# Legacy characters without an authoritative appearance may use the
				# default body. An explicit appearance must never flash the default
				# paper doll while its exact profile is still loading.
				profile = _profiles.get(_default_profile_key)
			if profile == null:
				_actors.erase(id)
				continue
		var old: Dictionary = _actors.get(id,{})
		var base_action := "NR" if bool(value.moving) else "NS"
		var generation_changed: bool = old.is_empty() or int(old.value.connection_epoch) != int(value.connection_epoch) or int(old.value.entity_map_epoch) != int(value.entity_map_epoch)
		var action := base_action
		var action_facing := int(value.facing)
		var started := now_usec
		var walk_started := now_usec
		var dead_visual := false
		if not generation_changed:
			started = int(old.started)
			walk_started = int(old.get("walk_started",started))
			# Attack/hit one-shots and turn updates must not restart the
			# underlying walk cycle; only a true idle->walk transition does.
			if base_action == "NR" and not bool(old.value.moving):
				walk_started = now_usec
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
			elif (not bool(value.moving)) and str(old.action) in EMOTION_BY_ID and \
					_one_shot_active(old,str(old.action),bool(profile.emotion_available.get(str(old.action),false)),now_usec):
				action = str(old.action)
				action_facing = 8
			elif str(old.action) != base_action:
				# A new MoveTarget sequence changes a destination, not the
				# walking animation. Repeated long-hold repaths must not
				# restart the NR clip at its first frame.
				started = now_usec
		var history = PositionHistory.new() if generation_changed else old.history
		if tick < 0 or not history.push(tick,Vector2(float(value.x),float(value.y)),now_usec,bool(value.moving)):
			continue
		_actors[id] = {"value":value,"action":action,"action_facing":action_facing,
			"started":started,"walk_started":walk_started,"history":history,
			"dead_visual":dead_visual,"profile":profile}

	var events = snapshot.get("player_attack_events",[])
	if events is Array:
		for event in events:
			if not event is Dictionary:
				continue
			var attacker := int(event.get("attacker_actor_id",0))
			var connection := int(event.get("attacker_connection_epoch",0))
			var sequence := int(event.get("attack_sequence",0))
			# A reliable visual can precede its AOI Spawn. Only a visible
			# actor with the matching generation may consume the replay key.
			if int(event.get("receiver_map_epoch",_epoch)) != _epoch or \
					not _visual_actor_matches_generation(attacker,connection):
				continue
			var key := "%d:%d:%d" % [attacker,connection,sequence]
			if not _remember_visual_event("attack",key):
				continue
			var actor: Dictionary = _actors[attacker]
			if not actor.profile.attack_available or bool(actor.get("dead_visual",false)):
				continue
			actor.action = "AA"
			actor.action_facing = int(event.get("facing",actor.value.facing))
			actor.started = now_usec
			_actors[attacker] = actor

	var emotion_events = snapshot.get("emotion_events",[])
	if emotion_events is Array:
		for event in emotion_events:
			if not event is Dictionary:
				continue
			var actor_id := int(event.get("actor_id",0))
			var connection := int(event.get("connection_epoch",0))
			var sequence := int(event.get("emotion_sequence",0))
			var emotion_id := int(event.get("emotion_id",0))
			if int(event.get("receiver_map_epoch",_epoch)) != _epoch or \
					not _visual_actor_matches_generation(actor_id,connection) or \
					emotion_id < 1 or emotion_id > EMOTION_BY_ID.size():
				continue
			var key := "%d:%d:%d" % [actor_id,connection,sequence]
			if not _remember_visual_event("emotion",key):
				continue
			var actor: Dictionary = _actors[actor_id]
			var emotion_action := str(EMOTION_BY_ID[emotion_id-1])
			if bool(actor.get("dead_visual",false)) or bool(actor.value.moving) or \
					not bool(actor.profile.emotion_available.get(emotion_action,false)):
				continue
			actor.action = emotion_action
			actor.action_facing = 8
			actor.started = now_usec
			_actors[actor_id] = actor

	var damage_events = snapshot.get("player_damage_visual_events",[])
	if damage_events is Array:
		for event in damage_events:
			if not event is Dictionary:
				continue
			var victim := int(event.get("victim_actor_id",0))
			var connection := int(event.get("victim_connection_epoch",0))
			var sequence := int(event.get("damage_sequence",0))
			if int(event.get("receiver_map_epoch",_epoch)) != _epoch or \
					not _visual_actor_matches_generation(victim,connection):
				continue
			var key := "%d:%d:%d" % [victim,connection,sequence]
			if not _remember_visual_event("damage",key):
				continue
			var actor: Dictionary = _actors[victim]
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
			if int(event.get("receiver_map_epoch",_epoch)) != _epoch:
				continue
			var attack_key:="pvp:%d:%d:%d" % [attacker,attacker_connection,attack_sequence]
			if _visual_actor_matches_generation(attacker,attacker_connection) and \
					_remember_visual_event("attack",attack_key):
				if _actors.has(attacker) and _actors[attacker].profile.attack_available and \
						not bool(_actors[attacker].get("dead_visual",false)):
					var attacker_actor:Dictionary=_actors[attacker]
					var current_connection:=int(attacker_actor.value.connection_epoch)
					var own_without_self_state:=attacker==int(snapshot.actor_id) and current_connection==0
					if current_connection==attacker_connection or own_without_self_state:
						attacker_actor.action="AA"
						attacker_actor.action_facing=int(event.get("attacker_facing",attacker_actor.value.facing))
						attacker_actor.started=now_usec
						_actors[attacker]=attacker_actor
			var damage_key:="pvp:%d:%d:%d" % [victim,victim_connection,damage_sequence]
			if _visual_actor_matches_generation(victim,victim_connection) and \
					_remember_visual_event("damage",damage_key):
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
			if int(event.get("receiver_map_epoch",_epoch)) != _epoch:
				continue
			var attack_key:="pvpskill:%d:%d:%d" % [attacker,ac,skill_sequence]
			if _visual_actor_matches_generation(attacker,ac) and \
					_remember_visual_event("attack",attack_key):
				if _actors.has(attacker) and _actors[attacker].profile.attack_available and \
						not bool(_actors[attacker].get("dead_visual",false)):
					var actor:Dictionary=_actors[attacker]
					var current_connection:=int(actor.value.connection_epoch)
					var own_without_self_state:=attacker==int(snapshot.actor_id) and current_connection==0
					if current_connection==ac or own_without_self_state:
						actor.action="AA"
						actor.action_facing=int(event.get("attacker_facing",actor.value.facing))
						actor.started=now_usec
						_actors[attacker]=actor
			var damage_key:="pvpskill:%d:%d:%d" % [victim,vc,damage_sequence]
			if _visual_actor_matches_generation(victim,vc) and \
					_remember_visual_event("damage",damage_key):
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
			if int(event.get("receiver_map_epoch",_epoch)) != _epoch or \
					not _visual_actor_matches_generation(revived,connection):
				continue
			var key := "%d:%d:%d" % [revived,connection,sequence]
			if not _remember_visual_event("respawn",key):
				continue
			var actor: Dictionary = _actors[revived]
			actor.dead_visual = false
			actor.action = "NR" if bool(actor.value.moving) else "NS"
			actor.action_facing = int(event.get("facing",actor.value.facing))
			actor.started = now_usec
			_actors[revived] = actor

func set_local_visual(own_actor: int, pos: Vector2, is_moving: bool, heading: int, now_usec: int) -> void:
	if own_actor <= 0 or not pos.is_finite() or heading < 1 or heading > 8:
		return
	_local_visual = {"actor_id":own_actor,"position":pos,"moving":is_moving,"facing":heading}
	if not _actors.has(own_actor):
		return
	var actor: Dictionary = _actors[own_actor]
	var old_move := bool(actor.value.moving)
	actor.value["moving"] = is_moving
	actor.value["facing"] = heading
	var current_action := str(actor.action)
	if current_action in ["NS","NR"] or (is_moving and current_action in EMOTION_BY_ID):
		if is_moving and not old_move:
			actor.walk_started = now_usec
		actor.action = "NR" if is_moving else "NS"
		actor.action_facing = heading
		if current_action in EMOTION_BY_ID:
			actor.started = now_usec
	_actors[own_actor] = actor

func clear_local_visual() -> void:
	_local_visual.clear()


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
		if action in ["AA","NH"] or action in EMOTION_BY_ID:
			var one_shot_body: Dictionary = profile.views.get(profile.body_prefix+"_"+action+str(facing),{})
			if one_shot_body.is_empty() or one_shot_body.timeline.is_complete(elapsed):
				action = "NR" if bool(value.moving) else "NS"
				facing = int(value.facing)
				elapsed = 0
		# SGTest uses the Recovered2 fixed 8 FPS walk cadence; independent from motion packets.
		# Preserve this clock during turns, re-paths and temporary AA/NH.
		var walk_elapsed_usec := maxi(0,now_usec-int(actor.get("walk_started",actor.started)))
		var requested := action+str(facing)
		var body: Dictionary = profile.views.get(profile.body_prefix+"_"+requested,{})
		if body.is_empty():
			# Unsupported transient action/facing should not drop the player.
			# Recovered leaves paper-doll Sprite2Ds visible in their last pose.
			requested = "NS6"
			action = "NS"
			facing = 6
			body = profile.views.get(profile.body_prefix+"_"+requested,{})
			if body.is_empty():
				continue
		# Source B-BODY *_NS/NR logical draw_slots omit the separate hair slot
		# in all eight directions. Recovered inserts Hair after Head in its
		# paper-doll layer order. Keep this independent cosmetic slot present
		# without changing the remaining source draw order.
		var draw_slots: Array = []
		for raw_slot in body.view.draw_slots:
			draw_slots.append(int(raw_slot))
		if profile.parts.has(1) and not draw_slots.has(1):
			var after_head := draw_slots.find(0)
			if after_head >= 0:
				draw_slots.insert(after_head+1,1)
			else:
				draw_slots.append(1)
		var parts: Array = []
		for slot in draw_slots:
			var logical_slot := int(slot)
			if profile.parts.has(logical_slot):
				var part: Array = profile.parts[logical_slot]
				var resolved: Dictionary = profile.views.get(str(part[1])+"_"+requested,{})
				if resolved.is_empty():
					continue
				parts.append({"asset":part[0],"clip":resolved.view.clip,
					"frame":resolved.timeline.frame_at_uniform_cadence(walk_elapsed_usec,WALK_FRAME_USEC) if action == "NR" else resolved.timeline.frame_at_update(elapsed),
					"flip_h":resolved.view.flip_h,"part_slot":logical_slot,
					"palette_index":(
						profile.hair_color if logical_slot==1
						else int(part[2]) if part.size()>2
						else 0)})
			# Recovered paper doll draws Standard first, then the equipment
			# overlay for the same logical body/leg slot.
			if profile.overlays.has(logical_slot):
				var overlay: Array = profile.overlays[logical_slot]
				var overlay_view: Dictionary = profile.views.get(str(overlay[1])+"_"+requested,{})
				if overlay_view.is_empty():
					continue
				parts.append({"asset":overlay[0],"clip":overlay_view.view.clip,
					"frame":overlay_view.timeline.frame_at_uniform_cadence(walk_elapsed_usec,WALK_FRAME_USEC) if action == "NR" else overlay_view.timeline.frame_at_update(elapsed),
					"flip_h":overlay_view.view.flip_h,"part_slot":logical_slot,
					"palette_index":int(overlay[2]) if overlay.size()>2 else 0})
		var sampled := Vector2.INF
		if int(_local_visual.get("actor_id",0)) == int(id):
			sampled = Vector2(_local_visual.position)
		elif int(id) == _own_actor_id:
			sampled = Vector2(float(value.x),float(value.y))
		else:
			var remote_route: Dictionary = _remote_routes.get(int(id),{})
			var route_controller = remote_route.get("controller") if not remote_route.is_empty() else null
			if route_controller != null and route_controller.visual_position.is_finite():
				sampled = route_controller.visual_position
			else:
				sampled = actor.history.sample(now_usec,PositionHistory.DEFAULT_DELAY_TICKS,REMOTE_MAX_PREDICTION_TICKS,REMOTE_MAX_PREDICTION_PIXELS)
		if not sampled.is_finite():
			sampled = Vector2(float(value.x),float(value.y))
		result.append({"id":str(id),"p":sampled,"parts":parts})
	return result


func position_at(actor_id: int, now_usec: int) -> Vector2:
	var actor: Dictionary = _actors.get(actor_id,{})
	if actor.is_empty():
		return Vector2.INF
	var sampled := Vector2.INF
	if int(_local_visual.get("actor_id",0)) == actor_id:
		sampled = Vector2(_local_visual.position)
	elif actor_id == _own_actor_id:
		sampled = Vector2(float(actor.value.x),float(actor.value.y))
	else:
		var remote_route: Dictionary = _remote_routes.get(actor_id,{})
		var route_controller = remote_route.get("controller") if not remote_route.is_empty() else null
		if route_controller != null and route_controller.visual_position.is_finite():
			sampled = route_controller.visual_position
		else:
			sampled = actor.history.sample(now_usec,PositionHistory.DEFAULT_DELAY_TICKS,REMOTE_MAX_PREDICTION_TICKS,REMOTE_MAX_PREDICTION_PIXELS)
	return sampled if sampled.is_finite() else Vector2(float(actor.value.x),float(actor.value.y))
