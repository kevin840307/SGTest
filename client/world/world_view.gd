extends Node2D
## One map presentation owner. No gameplay state, networking, or implicit transfer.
signal status_changed(message: String)

const Store = preload("res://asset_store/asset_store.gd")
const Camera = preload("res://world/world_camera.gd")
const TerrainData = preload("res://map/terrain_data.gd")
const Terrain = preload("res://map/terrain.gd")
const DrawList = preload("res://world/world_draw_list.gd")
const Players = preload("res://player/player_presentation.gd")
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
var monsters := Monsters.new()
var npcs := Npcs.new()
var pvp_names := PvpNames.new()
var _player_snapshot := {}
var _players_dirty := false
var _combat_seen := {}
var _combat_overlays: Array[Dictionary] = []
const COMBAT_OVERLAY_LIFETIME_USEC := 900000


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
	clear_map()
	epoch = store.begin_scope(&"world_view")
	map_id = id
	var error := store.acquire(&"world_view",epoch,"maps/"+str(id))
	if error != OK:
		_fail("無法載入地圖 " + str(id))
		return error
	phase = "map"
	status_changed.emit("載入地圖 " + str(id) + "…")
	return OK


func clear_map() -> void:
	_canvas.clear()
	_terrain.configure(null)
	draw_list.clear()
	players.clear()
	monsters.clear()
	npcs.clear()
	pvp_names.clear()
	_player_snapshot = {}
	_players_dirty = true
	_combat_seen.clear()
	_combat_overlays.clear()
	camera.clear()
	_map = {}
	_palette = []
	_last_update = -1
	_last_rect = Rect2()
	get_viewport().canvas_transform = Transform2D.IDENTITY
	if epoch > 0:
		store.end_scope(&"world_view",epoch)
	map_id = 0
	if phase != "closed":
		phase = "empty"


func resize_view(size: Vector2i) -> Error:
	if not Camera._valid_size(size):
		return ERR_INVALID_PARAMETER
	_size = size
	return camera.resize(epoch,size) if phase == "ready" else OK


func _process(_delta: float) -> void:
	# Only presentation resource work runs here. Future network/reducer must
	# have a separate owner/scheduler; this is never a world simulation tick.
	store.pump(2)
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
			if npc is Dictionary and npc.get("visual",{}) is Dictionary and bool(npc.visual.get("resolved",false)):
				var atlas := str(npc.visual.get("atlas",""))
				if atlas.ends_with("/atlas.json"):
					ids[atlas.trim_suffix("/atlas.json")] = true
		if show_players:
			for part in Players.PARTS.values():
				ids[part[0]] = true
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
			camera.set_focus(epoch,Vector2(_player_snapshot.x,_player_snapshot.y))
		_terrain.configure(data)
		_started_at = Time.get_ticks_msec()
		phase = "ready"
		status_changed.emit("地圖 " + str(map_id) + "｜地圖檢視・尚未連線")
	if phase == "ready":
		present()


func present() -> void:
	if phase != "ready":
		return
	var now_usec := Time.get_ticks_usec()
	if show_players and not _player_snapshot.is_empty():
		var focus := players.position_at(int(_player_snapshot.actor_id),now_usec)
		if focus.is_finite():
			camera.set_focus(epoch,focus)
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
	var player_labels:Array=[]
	if show_players:
		for raw_label in pvp_names.labels_at(now_usec):
			var label:Dictionary=raw_label
			var actor_position:=players.position_at(int(label.actor_id),now_usec)
			if not actor_position.is_finite() or not rect.has_point(actor_position):
				continue
			var rendered:=label.duplicate(true)
			rendered["p"]=actor_position+Vector2(0.0,-54.0)
			player_labels.append(rendered)
	_canvas.present(
		draw_list.build(rect,actors,{},draw_list.frames_at_update(update)),
		markers,combat_labels,player_labels)
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
		var now_usec := Time.get_ticks_usec()
		players.apply_snapshot(snapshot,now_usec)
		pvp_names.apply_snapshot(snapshot,now_usec)
		if monsters.apply_snapshot(snapshot,now_usec) != OK:
			_fail("怪物狀態與地圖素材不一致")
			return
		_consume_player_attack_events(snapshot,now_usec)
		_consume_player_damage_visual_events(snapshot,now_usec)
		_consume_pvp_combat_visual_events(snapshot,now_usec)
		_consume_pvp_skill_visual_events(snapshot,now_usec)
		var focus := players.position_at(int(snapshot.actor_id),now_usec)
		if focus.is_finite():
			camera.set_focus(epoch,focus)


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
