extends Node2D
## One map presentation owner. No gameplay state, networking, or implicit transfer.
signal status_changed(message: String)

const Store = preload("res://asset_store/asset_store.gd")
const Camera = preload("res://world/world_camera.gd")
const TerrainData = preload("res://map/terrain_data.gd")
const Terrain = preload("res://map/terrain.gd")
const DrawList = preload("res://world/world_draw_list.gd")
const Players = preload("res://player/player_presentation.gd")
const LocalMovement = preload("res://movement/local_movement.gd")
const AppearanceProfile = preload("res://player/character_appearance_profile.gd")
const Monsters = preload("res://monster/monster_presentation.gd")
const Npcs = preload("res://npc/npc_presentation.gd")
const PvpNames = preload("res://pvp/pvp_name_presentation.gd")
const Canvas = preload("res://world/world_canvas.gd")
var store := Store.new()
var camera := Camera.new()
var draw_list := DrawList.new()
var phase := "closed"
var map_id := 0
var epoch := 0
var _size := Vector2i(1280,720)
var _map: Dictionary = {}
var _palette: Array = []
var _terrain := Terrain.new()
var _canvas := Canvas.new()
var _started_at := 0
var _last_update := -1
var _last_rect := Rect2()
var show_players := false
var players := Players.new()
var local_movement := LocalMovement.new()
var _prediction_applied := false
var monsters := Monsters.new()
var npcs := Npcs.new()
var pvp_names := PvpNames.new()
var _player_snapshot := {}
var _players_dirty := false
var _next_draw_warning_msec := 0
# Camera follows render positions once per frame; it is NEVER repositioned
# directly from an asynchronous Server snapshot callback.
const CAMERA_CORRECTION_MAX_SPEED := 480.0
const CAMERA_CORRECTION_SNAP_THRESHOLD := 320.0
var _camera_visual_focus := Vector2.INF
var _camera_last_usec := 0
var _pending_player_profiles: Dictionary = {}
var _combat_seen := {}
var _combat_overlays: Array[Dictionary] = []
const COMBAT_OVERLAY_LIFETIME_USEC := 900000
# Retain only reusable map/atlas textures across short MapEpoch transfers.
# Actor, AOI, inventory and combat state are NEVER cached here.
const MAX_PREVIOUS_MAP_CACHE_BYTES := 128 * 1024 * 1024
var enable_map_cache := true
var _cache_epoch := 0
var _cache_assets: Dictionary = {}
var _cache_bytes := 0


func _ready() -> void:
	add_child(_terrain)
	add_child(_canvas)
	store.asset_failed.connect(_asset_failed)


func open_pack(path: String) -> Error:
	var error := store.open_pack(path)
	if error == OK:
		phase = "empty"
	return error


func request_map(id: int) -> Error:
	if phase == "closed" or id <= 0:
		return ERR_INVALID_PARAMETER
	_retain_ready_map_assets()
	clear_map(true)
	epoch = store.begin_scope(&"world_view")
	map_id = id
	var error := store.acquire(&"world_view",epoch,"maps/"+str(id))
	if error != OK:
		_fail("無法載入地圖 " + str(id))
		return error
	phase = "map"
	status_changed.emit("載入地圖 " + str(id) + "…")
	return OK


func _release_map_cache() -> void:
	if _cache_epoch > 0:
		store.end_scope(&"world_view_cache",_cache_epoch)
	_cache_epoch = 0
	_cache_assets.clear()
	_cache_bytes = 0


func _retain_ready_map_assets() -> void:
	if not enable_map_cache or not show_players or phase != "ready" or epoch <= 0 or _palette.is_empty():
		return
	var candidates: Array = ["maps/"+str(map_id)]
	candidates.append_array(_palette)
	var additional_bytes := 0
	var additions := 0
	for id in candidates:
		if _cache_assets.has(id):
			continue
		additional_bytes += store.asset_bytes(id)
		additions += 1
	# A Map15+Map13 cache fits under 128 MiB; larger future maps evict
	# all old cache references before preparing a new map generation.
	if _cache_bytes+additional_bytes > MAX_PREVIOUS_MAP_CACHE_BYTES or _cache_assets.size()+additions > store.max_assets-16:
		_release_map_cache()
	if additional_bytes > MAX_PREVIOUS_MAP_CACHE_BYTES or additions > store.max_assets-16:
		return
	if _cache_epoch <= 0:
		_cache_epoch = store.begin_scope(&"world_view_cache")
		if _cache_epoch <= 0:
			return
	for id in candidates:
		if _cache_assets.has(id):
			continue
		if store.acquire(&"world_view_cache",_cache_epoch,id) != OK:
			_release_map_cache()
			return
		_cache_assets[id] = true
		_cache_bytes += store.asset_bytes(id)


func clear_map(retain_cache: bool = false) -> void:
	_canvas.clear()
	_terrain.configure(null)
	draw_list.clear()
	players.clear()
	local_movement.clear()
	_prediction_applied = false
	monsters.clear()
	npcs.clear()
	pvp_names.clear()
	_player_snapshot = {}
	_players_dirty = true
	_pending_player_profiles.clear()
	_combat_seen.clear()
	_combat_overlays.clear()
	camera.clear()
	_map = {}
	_palette = []
	_last_update = -1
	_last_rect = Rect2()
	_next_draw_warning_msec = 0
	_camera_visual_focus = Vector2.INF
	_camera_last_usec = 0
	get_viewport().canvas_transform = Transform2D.IDENTITY
	if epoch > 0:
		store.end_scope(&"world_view",epoch)
	if not retain_cache:
		_release_map_cache()
	map_id = 0
	if phase != "closed":
		phase = "empty"


func begin_local_move(target: Vector2, expected_sequence: int) -> bool:
	if phase != "ready" or not show_players or not local_movement.request_target(target,expected_sequence):
		return false
	players.set_local_visual(local_movement.actor_id,local_movement.visual_position,
		local_movement.moving,local_movement.facing,Time.get_ticks_usec())
	_prediction_applied = true
	return true


func stop_local_move() -> void:
	if local_movement.active:
		local_movement.stop()
		players.set_local_visual(local_movement.actor_id,local_movement.visual_position,
			false,local_movement.facing,Time.get_ticks_usec())
		_prediction_applied = true


func _advance_local_visual(delta: float) -> void:
	# Presentation samples follow the render clock, as in Recovered's
	# Camera2D/Sprite2D pipeline. Fixed-physics sampling made a custom Canvas
	# repeat positions at high render FPS, creating visible step/hold jitter.
	if phase != "ready" or not show_players or _player_snapshot.is_empty() or not local_movement.ready:
		return
	local_movement.advance(delta)
	local_movement.advance_visual(delta)
	if local_movement.active:
		players.set_local_visual(local_movement.actor_id,local_movement.visual_position,
			local_movement.moving,local_movement.facing,Time.get_ticks_usec())
		_prediction_applied = true
	elif _prediction_applied:
		players.clear_local_visual()
		players.apply_snapshot(_player_snapshot,Time.get_ticks_usec())
		_prediction_applied = false


func resize_view(size: Vector2i) -> Error:
	if not Camera._valid_size(size):
		return ERR_INVALID_PARAMETER
	_size = size
	return camera.resize(epoch,size) if phase == "ready" else OK


func _snapshot_profile_definitions(snapshot: Dictionary) -> Dictionary:
	var profiles := {}
	if snapshot.is_empty():
		return profiles
	var candidates: Array = []
	var own: Variant = snapshot.get("self_state",{})
	if own is Dictionary and not own.is_empty():
		candidates.append(own)
	var entities_value: Variant = snapshot.get("entities",{})
	if entities_value is Dictionary:
		for entity in entities_value.values():
			if entity is Dictionary:
				candidates.append(entity)
	for value in candidates:
		var appearance: Variant = value.get("appearance",{})
		if not appearance is Dictionary or appearance.is_empty():
			continue
		var definition: Dictionary = AppearanceProfile.definition(appearance)
		if definition.is_empty():
			continue
		profiles[str(definition.key)] = {
			"appearance":appearance.duplicate(true),
			"definition":definition,
		}
	return profiles


func _profile_definition_parts(definition: Dictionary) -> Array:
	if definition.is_empty():
		return []
	var result: Array = definition.parts.values()
	result.append_array(definition.get("overlays",{}).values())
	return result


func _add_profile_asset_ids(ids: Dictionary, profiles: Dictionary) -> void:
	for record in profiles.values():
		for part in _profile_definition_parts(record.definition):
			ids[str(part[0])] = true


func _request_snapshot_profiles(snapshot: Dictionary) -> Error:
	if not show_players or phase != "ready" or epoch <= 0:
		return OK
	var profiles := _snapshot_profile_definitions(snapshot)
	for key in profiles:
		var record: Dictionary = profiles[key]
		var appearance: Dictionary = record.appearance
		if players.has_profile(appearance) or _pending_player_profiles.has(key):
			continue
		for part in _profile_definition_parts(record.definition):
			var asset_id := str(part[0])
			if _palette.has(asset_id):
				continue
			var error := store.acquire(&"world_view",epoch,asset_id)
			if error != OK:
				return error
			_palette.append(asset_id)
		_pending_player_profiles[key] = appearance.duplicate(true)
	return OK


func _pump_pending_player_profiles() -> Error:
	if _pending_player_profiles.is_empty():
		return OK
	var added := false
	for key in _pending_player_profiles.keys():
		var appearance: Dictionary = _pending_player_profiles[key]
		var definition: Dictionary = AppearanceProfile.definition(appearance)
		if definition.is_empty():
			return ERR_INVALID_DATA
		var assets := {}
		var ready := true
		for part in _profile_definition_parts(definition):
			var asset_id := str(part[0])
			var asset: Dictionary = store.get_asset(&"world_view",epoch,asset_id)
			if asset.is_empty():
				ready = false
				break
			assets[asset_id] = asset
		if not ready:
			continue
		var error := players.add_profile(assets,appearance)
		if error != OK:
			return error
		_pending_player_profiles.erase(key)
		added = true
	if added and not _player_snapshot.is_empty():
		players.apply_snapshot(_player_snapshot,Time.get_ticks_usec())
		_players_dirty = true
	return OK


func _process(_delta: float) -> void:
	# Only presentation resource work runs here. Future network/reducer must
	# have a separate owner/scheduler; this is never a world simulation tick.
	store.pump(2)
	if phase == "ready" and not _pending_player_profiles.is_empty():
		if _pump_pending_player_profiles() != OK:
			_fail("角色外觀素材資料無法顯示")
			return
	if phase == "map":
		_map = store.get_asset(&"world_view",epoch,"maps/"+str(map_id))
		if _map.is_empty():
			return
		var ids := {}
		for definition in _map.manifest.scenery_definitions.values():
			for appearance in definition.appearances:
				if appearance != null:
					ids[str(appearance.atlas).trim_suffix("/atlas.json")] = true
		var npc_population: Dictionary = _map.get("extra",{}).get("npcs",{})
		for npc in npc_population.get("npcs",[]):
			if not npc is Dictionary:
				continue
			var visual: Variant = npc.get("visual",null)
			if visual is Dictionary and bool(visual.get("resolved",false)):
				var atlas := str(visual.get("atlas",""))
				if atlas.ends_with("/atlas.json"):
					ids[atlas.trim_suffix("/atlas.json")] = true
		if show_players:
			var default_profile: Dictionary = AppearanceProfile.definition({})
			for part in _profile_definition_parts(default_profile):
				ids[str(part[0])] = true
			_add_profile_asset_ids(ids,_snapshot_profile_definitions(_player_snapshot))
			var population: Dictionary = _map.get("extra",{}).get("population",{})
			for definition in population.get("definitions",[]):
				if definition is Dictionary:
					var atlas := str(definition.get("atlas",""))
					if atlas.ends_with("/atlas.json"):
						ids[atlas.trim_suffix("/atlas.json")] = true
		_palette = ids.keys()
		for id in _palette:
			if store.acquire(&"world_view",epoch,id) != OK:
				_fail("地圖素材載入失敗")
				return
		phase = "palette"
	if phase == "palette":
		var assets := {}
		for id in _palette:
			var asset := store.get_asset(&"world_view",epoch,id)
			if asset.is_empty():
				return
			assets[id] = asset
		var data := TerrainData.new()
		if data.configure(_map) != OK or draw_list.configure(_map.manifest,assets,epoch) != OK:
			_fail("地圖資料無法顯示")
			return
		var npc_population: Dictionary = _map.get("extra",{}).get("npcs",{})
		if npcs.configure(npc_population,assets) != OK:
			_fail("NPC 素材與地圖資料不一致")
			return
		if show_players:
			if players.configure(assets) != OK:
				_fail("角色素材資料無法顯示")
				return
			for record in _snapshot_profile_definitions(_player_snapshot).values():
				if players.add_profile(assets,record.appearance) != OK:
					_fail("角色外觀素材資料無法顯示")
					return
			var population: Dictionary = _map.get("extra",{}).get("population",{})
			if monsters.configure(population,assets) != OK:
				_fail("怪物素材資料無法顯示")
				return
			if not _player_snapshot.is_empty():
				var presentation_now:=Time.get_ticks_usec()
				players.apply_snapshot(_player_snapshot,presentation_now)
				pvp_names.apply_snapshot(_player_snapshot,presentation_now)
				if monsters.apply_snapshot(_player_snapshot,presentation_now) != OK:
					_fail("怪物狀態與地圖素材不一致")
					return
		var world_size := Vector2i(_map.manifest.size[0],_map.manifest.size[1])
		if camera.configure(epoch,world_size,_size,Vector2(world_size)/2.0) != OK:
			_fail("相機設定失敗")
			return
		if show_players and not _player_snapshot.is_empty():
			_camera_visual_focus = Vector2(_player_snapshot.x,_player_snapshot.y)
			_camera_last_usec = Time.get_ticks_usec()
			camera.set_focus(epoch,_camera_visual_focus)
		_terrain.configure(data)
		if show_players and not _player_snapshot.is_empty():
			var blocking: Variant = _map.get("extra",{}).get("blocking",PackedByteArray())
			if blocking is PackedByteArray and local_movement.configure(
					blocking,_map.manifest,int(_player_snapshot.map_epoch),int(_player_snapshot.actor_id)):
				local_movement.observe(_player_snapshot)
		_started_at = Time.get_ticks_msec()
		phase = "ready"
		status_changed.emit("地圖 " + str(map_id) + "｜地圖檢視・尚未連線")
	if phase == "ready":
		_advance_local_visual(_delta)
		present()


func _follow_camera_visual(target: Vector2, now_usec: int) -> void:
	if not target.is_finite():
		return
	if not _camera_visual_focus.is_finite():
		_camera_visual_focus = target
	else:
		var gap := _camera_visual_focus.distance_to(target)
		# Normal 240px/s walking follows the paper doll 1:1. Only a genuine
		# asynchronous 30Hz Server correction gets bounded camera recovery,
		# so a 150-200px rollback cannot shake the whole terrain in one frame.
		if gap <= 16.0 or gap >= CAMERA_CORRECTION_SNAP_THRESHOLD:
			_camera_visual_focus = target
		else:
			var dt := clampf(float(now_usec-_camera_last_usec)/1000000.0,0.0,0.08)
			_camera_visual_focus = _camera_visual_focus.move_toward(
				target,CAMERA_CORRECTION_MAX_SPEED*dt)
	_camera_last_usec = now_usec
	camera.set_focus(epoch,_camera_visual_focus)


func present() -> void:
	if phase != "ready":
		return
	var now_usec := Time.get_ticks_usec()
	if show_players and not _player_snapshot.is_empty():
		var focus := players.position_at(int(_player_snapshot.actor_id),now_usec)
		if not focus.is_finite():
			var own: Dictionary = _player_snapshot.get("self_state",{})
			focus = Vector2(float(own.get("x",_player_snapshot.get("x",INF))),
				float(own.get("y",_player_snapshot.get("y",INF))))
		_follow_camera_visual(focus,now_usec)
	var rect := camera.view_rect()
	# Stateless ACT sampling; no callbacks, combat, or visibility state advances.
	var update := (Time.get_ticks_msec()-_started_at)*50/1000
	if rect == _last_rect and update == _last_update and not _players_dirty:
		return
	get_viewport().canvas_transform = Transform2D(0,-rect.position)
	_terrain.set_view_rect(rect)
	var actors: Array = npcs.actors_at(rect)
	if show_players:
		actors.append_array(players.actors_at(now_usec))
		actors.append_array(monsters.actors_at(now_usec))
		# Same-y insertion stays deterministic across AOI arrival order.
		actors.sort_custom(func(a: Dictionary,b: Dictionary) -> bool:
			return str(a.id) < str(b.id))
	# Immutable ground drop UIDs are AOI-filtered by Server first. Culling
	# here is only presentation, never gameplay or interest authority.
	var markers: Array = []
	if show_players and not _player_snapshot.is_empty():
		var ground: Dictionary = _player_snapshot.get("ground_drops",{})
		var keys: Array = ground.keys()
		keys.sort()
		for id in keys:
			var drop: Dictionary = ground[id]
			var p := Vector2(float(drop.x),float(drop.y))
			if rect.has_point(p):
				markers.append({"p":p,"drop_id":int(id)})
	var combat_labels: Array = []
	var surviving: Array[Dictionary] = []
	for overlay in _combat_overlays:
		var age := now_usec-int(overlay.started_usec)
		if age < 0 or age > COMBAT_OVERLAY_LIFETIME_USEC:
			continue
		surviving.append(overlay)
		var p := Vector2(INF,INF)
		if overlay.has("target_entity_id"):
			p = monsters.position_at(int(overlay.target_entity_id),now_usec)
		elif overlay.has("target_actor_id"):
			p = players.position_at(int(overlay.target_actor_id),now_usec)
		if not p.is_finite() or not rect.has_point(p):
			continue
		var progress := clampf(float(age)/float(COMBAT_OVERLAY_LIFETIME_USEC),0.0,1.0)
		combat_labels.append({
			"p":p+Vector2(0.0,-42.0-20.0*progress),
			"text":str(overlay.text),
			"miss":bool(overlay.miss),
			"alpha":1.0-progress
		})
	_combat_overlays = surviving
	var draw_commands: Array = draw_list.build(rect,actors,{},draw_list.frames_at_update(update))
	# Source SPR origin/trim differ across races, armor and NPC types. Name
	# anchoring must follow the actual visible pixel bounds, never a fixed -54.
	var sprite_bounds: Dictionary = {}
	for command in draw_commands:
		var key := str(command.get("actor_id",""))
		if key.is_empty():
			continue
		var bounds: Rect2 = (command.destination as Rect2).abs()
		if sprite_bounds.has(key):
			sprite_bounds[key] = (sprite_bounds[key] as Rect2).merge(bounds)
		else:
			sprite_bounds[key] = bounds
	var name_labels: Array = []
	if show_players:
		for raw_label in pvp_names.labels_at(now_usec):
			var label: Dictionary = raw_label
			var key := str(label.actor_id)
			if not sprite_bounds.has(key):
				continue
			var bound: Rect2 = sprite_bounds[key]
			var rendered := label.duplicate(true)
			rendered["p"] = Vector2(bound.get_center().x,bound.position.y-6.0)
			name_labels.append(rendered)
	for raw_label in npcs.labels_at(rect):
		var key := str(raw_label.actor_id)
		if not sprite_bounds.has(key):
			continue
		var rendered: Dictionary = raw_label.duplicate()
		var bound: Rect2 = sprite_bounds[key]
		rendered["p"] = Vector2(bound.get_center().x,bound.position.y-6.0)
		name_labels.append(rendered)
	if (draw_list.skipped_actors>0 or draw_list.skipped_parts>0 or draw_list.skipped_frames>0) and Time.get_ticks_msec()>=_next_draw_warning_msec:
		_next_draw_warning_msec = Time.get_ticks_msec()+5000
		push_warning("WORLD_ACTOR_RENDER_SKIPPED map=%d actors=%d parts=%d frames=%d remaining=%d details=%s" % [
			map_id,draw_list.skipped_actors,draw_list.skipped_parts,draw_list.skipped_frames,
			draw_commands.size(),str(draw_list.invalid_actor_examples)])
	_canvas.present(draw_commands,markers,combat_labels,name_labels)
	_players_dirty = false
	_last_rect = rect
	_last_update = update


func _asset_failed(owner: StringName, failed_epoch: int, _id: String, _reason: String) -> void:
	if owner == &"world_view" and failed_epoch == epoch:
		_fail("地圖素材載入失敗")


func _fail(message: String) -> void:
	clear_map()
	phase = "failed"
	status_changed.emit(message)


func _exit_tree() -> void:
	clear_map()


func set_player_snapshot(snapshot: Dictionary) -> void:
	if not show_players or not snapshot.get("world_ready",false) or int(snapshot.map_id) != map_id:
		return
	if not _player_snapshot.is_empty() and int(snapshot.map_epoch) < int(_player_snapshot.map_epoch):
		return
	_player_snapshot = snapshot
	_players_dirty = true
	if phase == "ready":
		if _request_snapshot_profiles(snapshot) != OK:
			_fail("角色外觀素材載入失敗")
			return
		var now_usec := Time.get_ticks_usec()
		if local_movement.ready:
			local_movement.observe(snapshot)
		players.apply_snapshot(snapshot,now_usec)
		if local_movement.active:
			players.set_local_visual(local_movement.actor_id,local_movement.visual_position,
				local_movement.moving,local_movement.facing,now_usec)
			_prediction_applied = true
		pvp_names.apply_snapshot(snapshot,now_usec)
		if monsters.apply_snapshot(snapshot,now_usec) != OK:
			_fail("怪物狀態與地圖素材不一致")
			return
		_consume_player_attack_events(snapshot,now_usec)
		_consume_player_damage_visual_events(snapshot,now_usec)
		_consume_pvp_combat_visual_events(snapshot,now_usec)
		_consume_pvp_skill_visual_events(snapshot,now_usec)
		# Camera is owned by present() on the render clock. Updating it here
		# allowed every network packet to yank terrain back to the Server,
		# then _process() yanked it back to local prediction next frame.


func _consume_player_attack_events(snapshot: Dictionary,now_usec: int) -> void:
	var events = snapshot.get("player_attack_events",[])
	if not events is Array:
		return
	for event in events:
		if not event is Dictionary:
			continue
		var attacker := int(event.get("attacker_actor_id",0))
		var connection := int(event.get("attacker_connection_epoch",0))
		var sequence := int(event.get("attack_sequence",0))
		var target := int(event.get("target_entity_id",0))
		var status := int(event.get("status",-1))
		var key := "attack:%d:%d:%d" % [attacker,connection,sequence]
		if _combat_seen.has(key):
			continue
		_combat_seen[key] = true
		if target <= 0 or status not in [0,1]:
			continue
		_combat_overlays.append({
			"target_entity_id":target,
			"text":"MISS" if status==1 else str(int(event.get("damage",0))),
			"miss":status==1,
			"started_usec":now_usec
		})


func _consume_player_damage_visual_events(snapshot: Dictionary,now_usec: int) -> void:
	var events = snapshot.get("player_damage_visual_events",[])
	if not events is Array:
		return
	for event in events:
		if not event is Dictionary:
			continue
		var victim := int(event.get("victim_actor_id",0))
		var connection := int(event.get("victim_connection_epoch",0))
		var sequence := int(event.get("damage_sequence",0))
		var damage := int(event.get("damage",0))
		var key := "hit:%d:%d:%d" % [victim,connection,sequence]
		if _combat_seen.has(key):
			continue
		_combat_seen[key] = true
		if victim <= 0 or connection <= 0 or sequence <= 0 or damage <= 0:
			continue
		# Presentation-only: use only the committed damage amount from opcode42.
		# Do not inspect or infer remote HP; actor position comes from the normal
		# authoritative player snapshot/interpolation path.
		_combat_overlays.append({
			"target_actor_id":victim,
			"text":str(damage),
			"miss":false,
			"started_usec":now_usec
		})


func _consume_pvp_combat_visual_events(snapshot: Dictionary,now_usec: int) -> void:
	var events=snapshot.get("pvp_combat_visual_events",[])
	if not events is Array:
		return
	for event in events:
		if not event is Dictionary:
			continue
		var attacker:=int(event.get("attacker_actor_id",0))
		var attacker_connection:=int(event.get("attacker_connection_epoch",0))
		var attack_sequence:=int(event.get("attack_sequence",0))
		var victim:=int(event.get("victim_actor_id",0))
		var victim_connection:=int(event.get("victim_connection_epoch",0))
		var damage_sequence:=int(event.get("damage_sequence",0))
		var damage:=int(event.get("damage",0))
		var key:="pvphit:%d:%d:%d:%d:%d:%d" % [
			attacker,attacker_connection,attack_sequence,
			victim,victim_connection,damage_sequence]
		if _combat_seen.has(key):
			continue
		_combat_seen[key]=true
		if attacker<=0 or attacker_connection<=0 or attack_sequence<=0 or victim<=0 or victim_connection<=0 or damage_sequence<=0 or damage<=0:
			continue
		_combat_overlays.append({
			"target_actor_id":victim,
			"text":str(damage),
			"miss":false,
			"started_usec":now_usec
		})


func _consume_pvp_skill_visual_events(snapshot:Dictionary,now_usec:int)->void:
	var events=snapshot.get("pvp_skill_visual_events",[])
	if not events is Array:
		return
	for event in events:
		if not event is Dictionary:
			continue
		var attacker:=int(event.get("attacker_actor_id",0))
		var ac:=int(event.get("attacker_connection_epoch",0))
		var sequence:=int(event.get("skill_sequence",0))
		var skill_id:=int(event.get("skill_id",0))
		var victim:=int(event.get("victim_actor_id",0))
		var vc:=int(event.get("victim_connection_epoch",0))
		var damage_sequence:=int(event.get("damage_sequence",0))
		var damage:=int(event.get("damage",0))
		var key:="pvpskillhit:%d:%d:%d:%d:%d:%d:%d" % [
			attacker,ac,sequence,skill_id,victim,vc,damage_sequence]
		if _combat_seen.has(key):
			continue
		_combat_seen[key]=true
		if attacker<=0 or ac<=0 or sequence<=0 or skill_id<=0 or victim<=0 or vc<=0 or damage_sequence<=0 or damage<=0:
			continue
		_combat_overlays.append({
			"target_actor_id":victim,
			"text":str(damage),
			"miss":false,
			"started_usec":now_usec
		})


func combat_overlay_count() -> int:
	return _combat_overlays.size()


func pick_ground_drop_at(point: Vector2, radius: float = 14.0) -> int:
	if phase != "ready" or not show_players or _player_snapshot.is_empty() or not point.is_finite():
		return 0
	var drops: Dictionary = _player_snapshot.get("ground_drops",{})
	var best := 0
	var distance := radius*radius
	for raw_id in drops:
		var id := int(raw_id)
		var drop: Dictionary = drops[raw_id]
		var d := point.distance_squared_to(Vector2(float(drop.x),float(drop.y)))
		if d < distance or (is_equal_approx(d,distance) and best != 0 and id < best):
			best = id
			distance = d
	return best


func ground_drop_count() -> int:
	if phase != "ready" or not show_players:
		return 0
	return (_player_snapshot.get("ground_drops",{}) as Dictionary).size()



func npc_count() -> int:
	return npcs.count() if phase == "ready" else 0


func pick_npc_at(world_point: Vector2) -> int:
	return npcs.pick_at(world_point) if phase == "ready" else 0


func npc_interaction_for(spawn_id: int) -> Dictionary:
	return npcs.interaction_for(spawn_id) if phase == "ready" else {}


func monster_count() -> int:
	return monsters.count() if phase == "ready" and show_players else 0


func pick_monster_at(world_point: Vector2) -> int:
	if phase != "ready" or not show_players or _player_snapshot.is_empty():
		return 0
	return monsters.pick_living_at(world_point,Time.get_ticks_usec())
