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
const Pets = preload("res://pet/pet_presentation.gd")
const PetVisualCatalog = preload("res://pet/pet_visual_catalog.gd")
const Npcs = preload("res://npc/npc_presentation.gd")
const PvpNames = preload("res://pvp/pvp_name_presentation.gd")
const Canvas = preload("res://world/world_canvas.gd")
const MapNameDisplay = preload("res://ui/hk_mapname_display.gd")
const EnemyHPDisplay = preload("res://ui/hk_enemy_hp_display.gd")
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
var map_name_display := MapNameDisplay.new()
var enemy_hp_display := EnemyHPDisplay.new()
var _focused_monster_id := 0
var _started_at := 0
var _last_update := -1
var _last_rect := Rect2()
var show_players := false
var players := Players.new()
var local_movement := LocalMovement.new()
var _prediction_applied := false
var monsters := Monsters.new()
var pets := Pets.new()
var pet_visual_catalog := PetVisualCatalog.new()
var _pending_pet_assets: Dictionary = {}
var _loaded_pet_assets: Dictionary = {}
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
var _combat_seen_ring: Array[String] = []
var _combat_seen_next := 0
# Five WorldState visual streams retain at most 4096 recent events each.
# Keep more than their combined bounded window, but not an entire play session.
const COMBAT_DEDUP_MAX := 32768
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
	add_child(map_name_display)
	add_child(enemy_hp_display)
	store.asset_failed.connect(_asset_failed)


func open_pack(path: String) -> Error:
	var error := store.open_pack(path)
	if error != OK:
		return error
	var catalog_error := pet_visual_catalog.load_path("res://assets/catalog/pet_visuals.json")
	if catalog_error not in [OK,ERR_FILE_NOT_FOUND]:
		return catalog_error
	phase = "empty"
	return OK


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
	_focused_monster_id = 0
	enemy_hp_display.clear_target()
	pets.clear()
	_pending_pet_assets.clear()
	_loaded_pet_assets.clear()
	npcs.clear()
	pvp_names.clear()
	_player_snapshot = {}
	_players_dirty = true
	_pending_player_profiles.clear()
	_combat_seen.clear()
	_combat_seen_ring.clear()
	_combat_seen_next = 0
	_combat_overlays.clear()
	camera.clear()
	map_name_display.clear_notice()
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
	if phase != "ready" or not show_players or _player_snapshot.is_empty():
		return
	players.advance_remote_visual(delta)
	if not local_movement.ready:
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
	# The authenticated CharacterList already carries the selected character's
	# appearance. Use it during map palette loading so the local paper doll is
	# ready before the first AOI PlayerIdentity frame arrives.
	var selected_appearance: Variant = snapshot.get("selected_character_appearance",{})
	if selected_appearance is Dictionary and not selected_appearance.is_empty():
		candidates.append({"appearance":selected_appearance})
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
		# The PlayerPresentation profile and the WorldDrawList must see the
		# same late-loaded atlases. Previously only Players received hair01,
		# so the actor carried the layer but DrawList rejected it forever.
		var error := draw_list.add_actor_assets(assets)
		if error == OK:
			error = players.add_profile(assets,appearance)
		if error != OK:
			return error
		_pending_player_profiles.erase(key)
		added = true
	if added and not _player_snapshot.is_empty():
		players.apply_snapshot(_player_snapshot,Time.get_ticks_usec())
		_players_dirty = true
	return OK


func _request_snapshot_pet_visuals(snapshot: Dictionary) -> Error:
	if phase != "ready" or epoch <= 0 or pet_visual_catalog.definitions.is_empty():
		return OK
	for asset_id in pet_visual_catalog.asset_ids_for_snapshot(snapshot):
		var id := str(asset_id)
		if _loaded_pet_assets.has(id) or _pending_pet_assets.has(id):
			continue
		var error := store.acquire(&"world_view",epoch,id)
		if error != OK:
			return error
		_pending_pet_assets[id] = true
	return OK


func _pump_pending_pet_visuals() -> Error:
	if _pending_pet_assets.is_empty():
		return OK
	var additions := {}
	for raw_id in _pending_pet_assets.keys():
		var id := str(raw_id)
		var asset: Dictionary = store.get_asset(&"world_view",epoch,id)
		if asset.is_empty():
			continue
		additions[id] = asset
	if additions.is_empty():
		return OK
	if draw_list.add_actor_assets(additions) != OK:
		return ERR_INVALID_DATA
	for id in additions:
		_pending_pet_assets.erase(id)
		_loaded_pet_assets[id] = true
	var all_assets := {}
	for raw_id in _loaded_pet_assets:
		var id := str(raw_id)
		var asset: Dictionary = store.get_asset(&"world_view",epoch,id)
		if asset.is_empty():
			return ERR_INVALID_DATA
		all_assets[id] = asset
	var definitions := pet_visual_catalog.definitions_for_assets(_loaded_pet_assets)
	if pets.configure_visuals(definitions,all_assets) != OK:
		return ERR_INVALID_DATA
	_players_dirty = true
	return OK


func _process(_delta: float) -> void:
	# Only presentation resource work runs here. Future network/reducer must
	# have a separate owner/scheduler; this is never a world simulation tick.
	store.pump(2)
	if phase == "ready" and not _pending_pet_assets.is_empty():
		if _pump_pending_pet_visuals() != OK:
			_fail("寵物外觀素材資料無法顯示")
			return
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
		var blocking: Variant = _map.get("extra",{}).get("blocking",PackedByteArray())
		if show_players:
			if players.configure(assets) != OK:
				_fail("角色素材資料無法顯示")
				return
			if blocking is PackedByteArray:
				# Remote movement intent reconstruction is presentation-only and
				# uses the exact same map collision source as local prediction.
				players.configure_remote_routes(blocking,_map.manifest)
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
				if pets.apply_snapshot(_player_snapshot,presentation_now) != OK:
					_fail("寵物狀態與地圖世代不一致")
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
			if blocking is PackedByteArray and local_movement.configure(
					blocking,_map.manifest,int(_player_snapshot.map_epoch),int(_player_snapshot.actor_id)):
				local_movement.observe(_player_snapshot)
		_started_at = Time.get_ticks_msec()
		phase = "ready"
		if not _player_snapshot.is_empty() and _request_snapshot_pet_visuals(_player_snapshot) != OK:
			_fail("寵物外觀素材載入失敗")
			return
		_show_hk_place_name()
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


func _remember_combat_visual(key: String) -> bool:
	if _combat_seen.has(key):
		return false
	_combat_seen[key] = true
	if _combat_seen_ring.size() < COMBAT_DEDUP_MAX:
		_combat_seen_ring.append(key)
	else:
		# O(1) ring eviction, not Array.pop_front() of 32768 entries on
		# every attack during an extended multi-player fight.
		var expired: String = _combat_seen_ring[_combat_seen_next]
		_combat_seen.erase(expired)
		_combat_seen_ring[_combat_seen_next] = key
		_combat_seen_next = (_combat_seen_next+1) % COMBAT_DEDUP_MAX
	return true


func _combat_overlay_position(overlay: Dictionary, now_usec: int) -> Vector2:
	var current := Vector2.INF
	if overlay.has("target_entity_id"):
		current = monsters.position_at(int(overlay.target_entity_id),now_usec)
	elif overlay.has("target_actor_id"):
		current = players.position_at(int(overlay.target_actor_id),now_usec)
	if current.is_finite():
		# Remember a previously visible, authoritative target location so a
		# kill/damage float survives AOI despawn during its short animation.
		overlay["last_target_position"] = current
		return current
	var previous: Variant = overlay.get("last_target_position",Vector2.INF)
	return previous if previous is Vector2 and previous.is_finite() else Vector2.INF


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
		# Pet presentation consumes the same authoritative snapshot/interpolation
		# path. actors_at remains empty until a source-backed visual binding exists.
		actors.append_array(pets.actors_at(now_usec))
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
		var p := _combat_overlay_position(overlay,now_usec)
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
	# UI-084 follows the selected monster's actual rendered SPR bounds, not a
	# guessed fixed-height actor, and reads HP only from accepted Server AOI.
	if _focused_monster_id > 0:
		var focused: Dictionary = monsters.focused_health(_focused_monster_id)
		var focused_key := "monster:" + str(_focused_monster_id)
		if focused.is_empty():
			_focused_monster_id = 0
			enemy_hp_display.clear_target()
		elif sprite_bounds.has(focused_key):
			var source_bounds: Rect2 = sprite_bounds[focused_key]
			var screen_anchor := camera.world_to_screen(Vector2(
				source_bounds.get_center().x - EnemyHPDisplay.FRAME_SIZE.x * 0.5,
				source_bounds.position.y - 36.0))
			enemy_hp_display.show_health(screen_anchor,int(focused.current_hp),
				int(focused.max_hp),int(focused.enemy_level),
				int(_player_snapshot.get("player_level",0)))
		else:
			enemy_hp_display.clear_target()
	else:
		enemy_hp_display.clear_target()
	var name_labels: Array = []
	if show_players:
		for raw_label in pvp_names.labels_at(now_usec):
			var label: Dictionary = raw_label
			var key := str(label.actor_id)
			if not sprite_bounds.has(key):
				continue
			var actor_id := int(label.actor_id)
			var feet := players.position_at(actor_id,now_usec)
			if not feet.is_finite():
				continue
			var rendered := label.duplicate(true)
			# Recovered player.tscn owns NameLabel under the Player node at a
			# fixed local rect (-130..-107). Do not derive player-name height
			# from paper-doll frames or appearance loading: that causes an
			# initial jump and animation-frame bob.
			rendered["p"] = feet
			rendered["recovered_player_anchor"] = true
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


func _show_hk_place_name() -> void:
	# Only a loaded source manifest and accepted authoritative WorldReady
	# may open this banner. Offline map previews cannot masquerade as play.
	if phase != "ready" or not show_players or _player_snapshot.is_empty():
		return
	var manifest: Dictionary = _map.get("manifest",{})
	if int(manifest.get("map_id",0)) != map_id:
		return
	map_name_display.show_source_name(str(manifest.get("name","")),map_id,
		int(_player_snapshot.get("map_epoch",0)))


func set_player_snapshot(snapshot: Dictionary) -> void:
	if not show_players or not snapshot.get("world_ready",false) or int(snapshot.map_id) != map_id:
		return
	if not _player_snapshot.is_empty():
		var previous_epoch: int = int(_player_snapshot.map_epoch)
		var incoming_epoch: int = int(snapshot.map_epoch)
		if incoming_epoch < previous_epoch:
			return
		if incoming_epoch != previous_epoch:
			# A former target ID has no meaning after a MapEpoch change.
			_focused_monster_id = 0
			enemy_hp_display.clear_target()
		if incoming_epoch == previous_epoch:
			var previous_tick: int = int(_player_snapshot.get("last_aoi_tick",-1))
			var incoming_tick: int = int(snapshot.get("last_aoi_tick",-1))
			if incoming_tick >= 0 and previous_tick >= 0 and incoming_tick < previous_tick:
				# A delayed same-generation snapshot cannot remove newer ground
				# drops, monsters or players from the visible world.
				return
	_player_snapshot = snapshot
	_players_dirty = true
	if phase == "ready":
		_show_hk_place_name()
		if _request_snapshot_profiles(snapshot) != OK:
			_fail("角色外觀素材載入失敗")
			return
		if _request_snapshot_pet_visuals(snapshot) != OK:
			_fail("寵物外觀素材載入失敗")
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
		if pets.apply_snapshot(snapshot,now_usec) != OK:
			_fail("寵物狀態與地圖世代不一致")
			return
		_consume_player_attack_events(snapshot,now_usec)
		_consume_player_skill_events(snapshot,now_usec)
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
		# Only a structurally valid committed hit/MISS may reserve the
		# per-attacker sequence. A malformed packet must not poison its key.
		var damage := int(event.get("damage",-1))
		if attacker <= 0 or connection <= 0 or sequence <= 0 or target <= 0 \
				or status not in [0,1] or damage < 0 or (status == 1 and damage != 0):
			continue
		var key := "attack:%d:%d:%d" % [attacker,connection,sequence]
		if not _remember_combat_visual(key):
			continue
		var overlay: Dictionary = {
			"target_entity_id":target,
			"text":"MISS" if status==1 else str(damage),
			"miss":status==1,
			"started_usec":now_usec
		}
		var last_point := monsters.position_at(target,now_usec)
		if last_point.is_finite():
			overlay["last_target_position"] = last_point
		_combat_overlays.append(overlay)


func _consume_player_skill_events(snapshot: Dictionary,now_usec: int) -> void:
	# Opcode 46 is an authoritative per-target visual for both single and
	# area skills. The skill-use result is only a receipt, not an extra hit.
	var events = snapshot.get("player_skill_events",[])
	if not events is Array:
		return
	for event in events:
		if not event is Dictionary:
			continue
		var attacker := int(event.get("attacker_actor_id",0))
		var connection := int(event.get("attacker_connection_epoch",0))
		var sequence := int(event.get("skill_sequence",0))
		var skill_id := int(event.get("skill_id",0))
		var target := int(event.get("target_entity_id",0))
		var status := int(event.get("status",-1))
		var damage := int(event.get("damage",-1))
		if attacker <= 0 or connection <= 0 or sequence <= 0 or target <= 0 \
				or skill_id < 1 or skill_id > 100000000 or status not in [0,1] \
				or (status == 0 and damage <= 0) or (status == 1 and damage != 0):
			continue
		# One area skill sequence legitimately hits several different monsters.
		var key := "skill:%d:%d:%d:%d" % [attacker,connection,sequence,target]
		if not _remember_combat_visual(key):
			continue
		var overlay: Dictionary = {
			"target_entity_id":target,
			"text":"MISS" if status==1 else str(damage),
			"miss":status==1,
			"started_usec":now_usec
		}
		var last_point := monsters.position_at(target,now_usec)
		if last_point.is_finite():
			overlay["last_target_position"] = last_point
		_combat_overlays.append(overlay)


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
		if victim <= 0 or connection <= 0 or sequence <= 0 or damage <= 0:
			continue
		var key := "hit:%d:%d:%d" % [victim,connection,sequence]
		if not _remember_combat_visual(key):
			continue
		# Presentation-only: use only the committed damage amount from opcode42.
		# Do not inspect or infer remote HP; actor position comes from the normal
		# authoritative player snapshot/interpolation path.
		var overlay: Dictionary = {
			"target_actor_id":victim,
			"text":str(damage),
			"miss":false,
			"started_usec":now_usec
		}
		var last_point := players.position_at(victim,now_usec)
		if last_point.is_finite():
			overlay["last_target_position"] = last_point
		_combat_overlays.append(overlay)


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
		if attacker<=0 or attacker_connection<=0 or attack_sequence<=0 or victim<=0 or victim_connection<=0 or damage_sequence<=0 or damage<=0:
			continue
		var key:="pvphit:%d:%d:%d:%d:%d:%d" % [
			attacker,attacker_connection,attack_sequence,
			victim,victim_connection,damage_sequence]
		if not _remember_combat_visual(key):
			continue
		var overlay: Dictionary = {
			"target_actor_id":victim,
			"text":str(damage),
			"miss":false,
			"started_usec":now_usec
		}
		var last_point := players.position_at(victim,now_usec)
		if last_point.is_finite():
			overlay["last_target_position"] = last_point
		_combat_overlays.append(overlay)


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
		if attacker<=0 or ac<=0 or sequence<=0 or skill_id<=0 or victim<=0 or vc<=0 or damage_sequence<=0 or damage<=0:
			continue
		var key:="pvpskillhit:%d:%d:%d:%d:%d:%d:%d" % [
			attacker,ac,sequence,skill_id,victim,vc,damage_sequence]
		if not _remember_combat_visual(key):
			continue
		var overlay: Dictionary = {
			"target_actor_id":victim,
			"text":str(damage),
			"miss":false,
			"started_usec":now_usec
		}
		var last_point := players.position_at(victim,now_usec)
		if last_point.is_finite():
			overlay["last_target_position"] = last_point
		_combat_overlays.append(overlay)


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
	var selected := monsters.pick_living_at(world_point,Time.get_ticks_usec())
	# main.gd also calls this for passive cursor hover. Only a real held
	# left-button click may change target; hovering never selects an enemy.
	if Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT) and selected != _focused_monster_id:
		_focused_monster_id = selected
		_players_dirty = true
		if selected == 0:
			enemy_hp_display.clear_target()
	return selected
