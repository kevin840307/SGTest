extends Node2D

# Standalone, server authoritative prototype. Start server with --headless -- --server.
var port := 27888
const WORLD_SIZE := Vector2(2400, 1600)
const VIEW_RADIUS := 900.0
var monster_count := 80
const MAX_DROPS := 5000

var is_server := false
var world := preload("res://scripts/server_world.gd").new()
var players: Dictionary = world.players
var monsters: Dictionary = world.monsters
var drops: Dictionary = world.drops
var sim_time := 0.0
var snapshot_time := 0.0
var local_state: Dictionary = {}
var replica := preload("res://scripts/client_replica.gd").new()
var visible_players: Dictionary = replica.players
var visible_monsters: Dictionary = replica.monsters
var visible_drops: Dictionary = {}
var ui_modern := false
var hud: CanvasLayer
var info_label: Label
var item_label: Label
var health_label: Label
var atlas := preload("res://scripts/entity_atlas.gd").new()
var doll := preload("res://scripts/paper_doll.gd").new()
var monster_grid := preload("res://scripts/spatial_grid.gd").new()
var drop_grid := preload("res://scripts/spatial_grid.gd").new()
var player_grid := preload("res://scripts/spatial_grid.gd").new()
var rendered_players: Dictionary = {}
var rendered_monsters: Dictionary = {}
var last_snapshot := -1
var input_elapsed := 0.0
var bot := false
var seed_drops := 0
var bind_address := "127.0.0.1"
const CODEC = preload("res://scripts/snapshot_codec.gd")
const ACTION = preload("res://scripts/action_policy.gd")
const INPUT = preload("res://scripts/input_codec.gd")
var prediction := preload("res://scripts/local_prediction.gd").new()
var prediction_observed := false
var clock_sync := preload("res://scripts/server_clock.gd").new()
var ping_elapsed := 0.0
var server_time := 0.0
var ground_elapsed := 0.0
var drop_renderer: MultiMeshInstance2D
var drops_dirty := false
var action_observed := false
var inventory_observed := false
var indexed_drop_revision := -1
var asset_root := "res://assets/generated"
var profiler: RefCounted
var diagnostics_time := 0.0
var combat_arena := false
var health_observed := false
var death_observed := false
var respawn_observed := false
var observed_dead: Dictionary = {}
var map_id := 13
var map_fingerprint := ""
var map_ready := false
var terrain: MultiMeshInstance2D
var map_objects := preload("res://scripts/map_objects.gd").new()
var render_order: Array = []
var render_elapsed := 1.0

func _ready() -> void:
	var arguments := OS.get_cmdline_user_args()
	is_server = arguments.has("--server")
	bot = arguments.has("--bot")
	combat_arena = arguments.has("--combat-arena")
	if arguments.has("--profile") and not is_server:
		profiler = preload("res://scripts/frame_profiler.gd").new()
	for argument in arguments:
		if argument.begins_with("--monsters="):
			monster_count = clampi(int(argument.trim_prefix("--monsters=")), 0, 10000)
		if argument.begins_with("--drops="):
			seed_drops = clampi(int(argument.trim_prefix("--drops=")), 0, MAX_DROPS)
		if argument.begins_with("--bind="):
			bind_address = argument.trim_prefix("--bind=")
		if argument.begins_with("--assets="):
			asset_root = argument.trim_prefix("--assets=")
		if argument.begins_with("--port="):
			port = clampi(int(argument.trim_prefix("--port=")),1024,65535)
		if argument.begins_with("--map="):
			map_id = maxi(0,int(argument.trim_prefix("--map=")))
	if combat_arena:
		map_id = 0
	multiplayer.peer_disconnected.connect(_on_peer_disconnected)
	if is_server:
		_start_server()
	else:
		print("SGO client starting")
		_build_ui()
		print("SGO client UI ready")
		_load_sample_atlas()
		var host := "127.0.0.1"
		for argument in arguments:
			if argument.begins_with("--host="):
				host = argument.trim_prefix("--host=")
		_start_client(host)
		print("SGO client connecting to %s:%d" % [host, port])
		for argument in arguments:
			if argument.begins_with("--capture="):
				await get_tree().create_timer(3.0).timeout
				await RenderingServer.frame_post_draw
				get_viewport().get_texture().get_image().save_png(argument.trim_prefix("--capture="))

func _start_server() -> void:
	if map_id != 0:
		var folder := asset_root.path_join("maps/%d" % map_id)
		var metadata = JSON.parse_string(FileAccess.get_file_as_string(folder.path_join("world.json")))
		var grid := preload("res://scripts/collision_grid.gd").new()
		var error: Error = grid.open(folder.path_join("blocking.bin"))
		if not metadata is Dictionary or int(metadata.get("version",0)) != 2 or error != OK or world.configure_map(grid) != OK:
			push_error("Cannot load authoritative map %d" % map_id)
			get_tree().quit(1)
			return
		if FileAccess.get_sha256(folder.path_join("blocking.bin")) != str(metadata.collision_sha256):
			push_error("Map collision bytes do not match the manifest")
			get_tree().quit(1)
			return
		map_fingerprint = metadata.fingerprint
	var peer := ENetMultiplayerPeer.new()
	peer.set_bind_ip(bind_address)
	var error := peer.create_server(port, 128)
	if error != OK:
		push_error("Cannot start server: %s" % error)
		get_tree().quit(1)
		return
	multiplayer.multiplayer_peer = peer
	multiplayer.peer_connected.connect(_on_peer_connected)
	world.populate(monster_count, seed_drops)
	if combat_arena:
		world.monsters.clear()
		world.populate(1,0)
		world.monsters[1].p = Vector2(1230,800)
		world.monsters[1].damage = 50
	drop_grid.rebuild(drops)
	print("SGO server listening on UDP %d with %d monsters" % [port, monster_count])

func _start_client(host: String) -> void:
	var peer := ENetMultiplayerPeer.new()
	var error := peer.create_client(host, port)
	if error != OK:
		info_label.text = "連線失敗：%s" % error
		return
	multiplayer.multiplayer_peer = peer
	multiplayer.connected_to_server.connect(func() -> void:
		info_label.text = "已連線"
		print("client connected with peer %d" % multiplayer.get_unique_id()))
	multiplayer.connection_failed.connect(func() -> void:
		info_label.text = "無法連線到伺服器"
		print("client connection failed"))
	multiplayer.server_disconnected.connect(func() -> void:
		info_label.text = "伺服器已斷線"
		print("client disconnected"))

func _on_peer_connected(id: int) -> void:
	if not is_server:
		return
	world.join(id)
	_world_info.rpc_id(id,map_id,map_fingerprint)
	print("player %d joined" % id)

@rpc("authority", "call_remote", "reliable", 0)
func _world_info(server_map: int, fingerprint: String) -> void:
	if is_server:
		return
	map_id = server_map
	if terrain != null:
		terrain.queue_free()
		terrain = null
	map_objects.reset()
	render_order.clear()
	render_elapsed = 1.0
	if server_map != 0:
		terrain = preload("res://scripts/terrain_renderer.gd").new()
		var error: Error = terrain.open(asset_root,server_map)
		if error != OK or terrain.data.fingerprint != fingerprint:
			push_error("Map assets do not match the server (%d)" % server_map)
			get_tree().quit(1)
			return
		terrain.z_index = -100
		add_child(terrain)
		map_objects.configure(asset_root,terrain.data)
		var grid := preload("res://scripts/collision_grid.gd").new()
		if grid.open(asset_root.path_join("maps/%d/blocking.bin" % server_map)) != OK:
			get_tree().quit(1)
			return
		prediction.configure(grid,Vector2(terrain.data.size[0],terrain.data.size[1]))
		info_label.text = "已連線　%s" % terrain.data.name
	else:
		info_label.text = "已連線　測試場地"
		prediction.configure(null,WORLD_SIZE)
	map_ready = true

func _on_peer_disconnected(id: int) -> void:
	if is_server:
		players.erase(id)

func _physics_process(delta: float) -> void:
	if is_server:
		_server_step(delta)
	else:
		diagnostics_time += delta
		if diagnostics_time > 1.0 and diagnostics_time - delta <= 1.0:
			print("client peer status %d" % multiplayer.multiplayer_peer.get_connection_status())
		if int(diagnostics_time/5.0) > int((diagnostics_time-delta)/5.0) and prediction.initialized:
			print("prediction_metrics "+JSON.stringify({"pending":prediction.pending.size(),"max_pending":prediction.max_pending,"max_correction_px":prediction.max_correction,"reconciliations":prediction.reconciliations}))
		_client_step(delta)

func _server_step(delta: float) -> void:
	world.step(delta)
	sim_time = world.time
	for event in world.drain_events():
		_publish_action(event.kind,event.id,event)
	if indexed_drop_revision != world.drop_revision:
		drop_grid.rebuild(drops)
		indexed_drop_revision = world.drop_revision
	ground_elapsed += delta
	if ground_elapsed >= 0.5:
		ground_elapsed = 0.0
		for pid in players:
			_send_ground_delta(pid)
	snapshot_time += delta
	if snapshot_time >= 0.1:
		snapshot_time -= 0.1
		monster_grid.rebuild(monsters)
		player_grid.rebuild(players)
		for pid in players:
			_send_snapshot(pid)

func _send_snapshot(pid: int) -> void:
	var center: Vector2 = players[pid].p
	var ps: Array = []
	var ms: Array = []
	for id in player_grid.query(center, VIEW_RADIUS):
		if center.distance_squared_to(players[id].p) <= VIEW_RADIUS * VIEW_RADIUS:
			var player: Dictionary = players[id]
			ps.append([id, player.p.x, player.p.y, player.hp, player.facing, player.pose, player.action_time,player.revision])
	for id in monster_grid.query(center, VIEW_RADIUS):
		var monster: Dictionary = monsters[id]
		if center.distance_squared_to(monster.p) <= VIEW_RADIUS * VIEW_RADIUS:
			ms.append([id, monster.p.x, monster.p.y, monster.hp, monster.facing, monster.pose, monster.action_time,monster.revision])
	for kind in 2:
		var rows: Array = ps if kind == 0 else ms
		var seen: Dictionary = players[pid].seen_entities[kind]
		var current: Dictionary = {}
		for row in rows:
			current[int(row[0])] = true
		var removed := PackedInt32Array()
		for id in seen:
			if not current.has(id):
				removed.append(id)
		players[pid].seen_entities[kind] = current
		for offset in range(0,removed.size(),128):
			_interest_remove.rpc_id(pid,world.tick,kind,removed.slice(offset,offset+128))
		for offset in range(0,rows.size(),CODEC.ROWS_PER_PACKET):
			_snapshot_chunk.rpc_id(pid,world.tick,sim_time,kind,CODEC.encode(rows.slice(offset,offset+CODEC.ROWS_PER_PACKET)))
	_send_owner_state(pid)

func _send_owner_state(pid: int) -> void:
	var state: Dictionary = players[pid]
	_owner_state.rpc_id(pid,world.tick,world.time,state.input_ack,state.life_epoch,state.p,state.hp,state.facing,state.pose,state.action_time,state.revision)

@rpc("authority", "call_remote", "unreliable_ordered", 7)
func _owner_state(tick: int, clock: float, acknowledged: int, epoch: int, at: Vector2, hp: int, facing: int, pose: int, started: float, revision: int) -> void:
	if is_server or not map_ready:
		return
	var id := multiplayer.get_unique_id()
	if visible_players.has(id):
		var current: Dictionary = visible_players[id]
		if ACTION.newer_revision(current.revision,revision) and (int(current.hp) == 0) != (hp == 0):
			return
	clock_sync.observe_snapshot(clock,Time.get_ticks_usec()/1000000.0)
	server_time = clock_sync.advance(Time.get_ticks_usec()/1000000.0)
	_replica_changed(0,id,replica.snapshot(0,tick,[id,at.x,at.y,hp,facing,pose,started,revision]))
	if prediction.authority(tick,acknowledged,epoch,at,hp,Time.get_ticks_usec()/1000000.0):
		if prediction.motion.is_zero_approx():
			prediction.facing = facing
		if not prediction_observed:
			prediction_observed = true
			print("prediction_ready")

func _send_ground_delta(pid: int) -> void:
	var seen: Dictionary = players[pid].seen_drops
	var current: Dictionary = {}
	var added: Array = []
	var removed := PackedInt32Array()
	var center: Vector2 = players[pid].p
	for id in drop_grid.query(center, VIEW_RADIUS):
		if center.distance_squared_to(drops[id].p) > VIEW_RADIUS * VIEW_RADIUS:
			continue
		current[id] = true
		if not seen.has(id):
			added.append([id, drops[id].p.x, drops[id].p.y, drops[id].item])
	for id in seen:
		if not current.has(id):
			removed.append(id)
	players[pid].seen_drops = current
	for offset in range(0, added.size(), 20):
		_ground_delta.rpc_id(pid, added.slice(offset, offset+20), PackedInt32Array())
	for offset in range(0, removed.size(), 128):
		_ground_delta.rpc_id(pid, [], removed.slice(offset, offset+128))

@rpc("any_peer", "call_remote", "unreliable_ordered", 1)
func _move_input(epoch: int, payload: PackedByteArray) -> void:
	if not is_server:
		return
	var id := multiplayer.get_remote_sender_id()
	for frame in INPUT.decode(payload):
		world.queue_input(id,epoch,int(frame.sequence),frame.motion)

@rpc("any_peer", "reliable")
func _attack(monster_id: int) -> void:
	if not is_server:
		return
	var id := multiplayer.get_remote_sender_id()
	var first_drop: int = world.next_drop
	if world.attack(id, monster_id):
		for pid in players:
			var added: Array = []
			for drop_id in range(first_drop, world.next_drop):
				var drop: Dictionary = drops[drop_id]
				if players[pid].p.distance_squared_to(drop.p) <= VIEW_RADIUS*VIEW_RADIUS:
					players[pid].seen_drops[drop_id] = true
					added.append([drop_id, drop.p.x, drop.p.y, drop.item])
			if not added.is_empty():
				_ground_delta.rpc_id(pid, added, PackedInt32Array())

@rpc("any_peer", "reliable")
func _pickup(drop_id: int) -> void:
	if not is_server:
		return
	var id := multiplayer.get_remote_sender_id()
	if world.pickup(id, drop_id):
		_inventory.rpc_id(id, players[id].inventory)
		for pid in players:
			if players[pid].seen_drops.has(drop_id):
				players[pid].seen_drops.erase(drop_id)
				_ground_delta.rpc_id(pid, [], PackedInt32Array([drop_id]))

func _publish_action(kind: int, id: int, state: Dictionary) -> void:
	for pid in players:
		if players[pid].p.distance_squared_to(state.p) <= VIEW_RADIUS * VIEW_RADIUS:
			_action_event.rpc_id(pid, kind, id, state.facing, state.pose, state.action_time,state.hp,state.revision,state.p,world.tick)
	if kind == 0 and state.pose in [0,4] and players.has(id):
		_send_owner_state(id)

@rpc("authority", "call_remote", "reliable", 4)
func _action_event(kind: int, id: int, facing: int, pose: int, clock: float, hp: int, revision: int, at: Vector2, tick: int) -> void:
	if not action_observed:
		action_observed = true
		print("action_received")
	var changed: int = replica.event(kind,id,tick,at,hp,facing,pose,clock,revision)
	_replica_changed(kind,id,changed)

func _replica_changed(kind: int, id: int, changed: int) -> void:
	if changed == 0:
		return
	var state: Dictionary = replica.group(kind)[id]
	if changed == 2:
		if kind == 0:
			rendered_players[id] = state.p
			if id == multiplayer.get_unique_id():
				prediction.freeze(state.p)
		else:
			rendered_monsters[id] = state.p
	if kind == 0:
		if id == multiplayer.get_unique_id() and state.pose >= 2:
			prediction.facing = state.facing
		_update_health(id,state.hp,state.pose)
	render_elapsed = 1.0

func _update_health(id: int, hp: int, pose: int) -> void:
	if not health_observed and hp < world.PLAYER_HP:
		health_observed = true
		print("health_received")
	if not death_observed and hp == 0:
		death_observed = true
		print("death_received")
	if hp == 0:
		observed_dead[id] = true
	if observed_dead.has(id) and not respawn_observed and hp > 0 and pose < 2:
		respawn_observed = true
		print("respawn_received")
		observed_dead.erase(id)
	if id == multiplayer.get_unique_id():
		health_label.text = "生命 %d / %d%s" % [hp,world.PLAYER_HP,"　倒地，等待復活" if hp == 0 else ""]

@rpc("authority", "call_remote", "unreliable", 2)
func _snapshot_chunk(tick: int, clock: float, kind: int, payload: PackedByteArray) -> void:
	if is_server or kind not in [0,1] or payload.size() > CODEC.ROWS_PER_PACKET*CODEC.STRIDE:
		return
	clock_sync.observe_snapshot(clock,Time.get_ticks_usec()/1000000.0)
	for row in CODEC.decode(payload):
		_replica_changed(kind,int(row[0]),replica.snapshot(kind,tick,row))
	if tick%150 == 0 and tick != last_snapshot:
		last_snapshot = tick
		print("snapshot_received tick=%d players=%d monsters=%d drops=%d" % [tick,visible_players.size(),visible_monsters.size(),visible_drops.size()])
	queue_redraw()

@rpc("authority", "call_remote", "reliable", 4)
func _interest_remove(tick: int, kind: int, ids: PackedInt32Array) -> void:
	replica.erase_interest(kind,tick,ids)
	if kind == 0:
		for id in ids:
			if not visible_players.has(id):
				observed_dead.erase(id)
	render_elapsed = 1.0

@rpc("any_peer", "call_remote", "unreliable", 6)
func _clock_ping(sent: float) -> void:
	if is_server and players.has(multiplayer.get_remote_sender_id()):
		_clock_pong.rpc_id(multiplayer.get_remote_sender_id(),sent,world.time)

@rpc("authority", "call_remote", "unreliable", 6)
func _clock_pong(sent: float, stamp: float) -> void:
	clock_sync.observe_pong(sent,Time.get_ticks_usec()/1000000.0,stamp)

@rpc("authority", "call_remote", "reliable", 3)
func _ground_delta(added: Array, removed: PackedInt32Array) -> void:
	for row in added:
		visible_drops[int(row[0])] = {"p": Vector2(row[1], row[2]), "item": int(row[3])}
	for id in removed:
		visible_drops.erase(id)
	drops_dirty = true

@rpc("authority", "call_remote", "reliable", 3)
func _inventory(inventory: Dictionary) -> void:
	if not inventory_observed:
		inventory_observed = true
		print("inventory_received")
	local_state = inventory
	item_label.text = "物品：%s" % str(local_state)

func _client_step(delta: float) -> void:
	if multiplayer.multiplayer_peer == null or multiplayer.multiplayer_peer.get_connection_status() != MultiplayerPeer.CONNECTION_CONNECTED:
		return
	if not map_ready:
		return
	var own_id := multiplayer.get_unique_id()
	if visible_players.has(own_id) and int(visible_players[own_id].hp) <= 0:
		return
	var motion := Input.get_vector("ui_left", "ui_right", "ui_up", "ui_down")
	if bot:
		motion = Vector2.from_angle(Time.get_ticks_msec() / 1000.0)
	prediction.step_input(motion,Time.get_ticks_usec()/1000000.0,server_time)
	var packet := prediction.packet()
	if not packet.is_empty():
		_move_input.rpc_id(1,prediction.epoch,packet)
	input_elapsed += delta
	if input_elapsed < 0.05:
		return
	input_elapsed = 0.0
	if bot:
		var own: Vector2 = visible_players.get(multiplayer.get_unique_id(), {"p":Vector2.ZERO}).p
		for id in visible_monsters:
			if visible_monsters[id].hp > 0 and own.distance_squared_to(visible_monsters[id].p) < 150.0*150.0:
				_attack.rpc_id(1,id)
				break
		for id in visible_drops:
			if own.distance_squared_to(visible_drops[id].p) < 90.0*90.0:
				_pickup.rpc_id(1,id)
				break

func _unhandled_input(event: InputEvent) -> void:
	if is_server:
		return
	if event is InputEventKey and event.pressed and event.keycode == KEY_F2:
		ui_modern = not ui_modern
		_style_ui()
	if multiplayer.multiplayer_peer == null or multiplayer.multiplayer_peer.get_connection_status() != MultiplayerPeer.CONNECTION_CONNECTED:
		return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		var world_point := get_global_mouse_position()
		var nearest := 90.0
		var selected := -1
		for id in visible_monsters:
			var distance: float = world_point.distance_to(visible_monsters[id].p)
			if distance < nearest:
				nearest = distance
				selected = id
		if selected >= 0:
			_attack.rpc_id(1, selected)
			return
		for id in visible_drops:
			if world_point.distance_to(visible_drops[id].p) < 35.0:
				_pickup.rpc_id(1, id)
				return

func _draw() -> void:
	if is_server:
		return
	if terrain == null:
		draw_rect(Rect2(-position, WORLD_SIZE), Color(0.12, 0.19, 0.16))
		for x in range(0, int(WORLD_SIZE.x), 100):
			draw_line(Vector2(x, 0) - position, Vector2(x, WORLD_SIZE.y) - position, Color(0.18, 0.26, 0.21), 1)
		for y in range(0, int(WORLD_SIZE.y), 100):
			draw_line(Vector2(0, y) - position, Vector2(WORLD_SIZE.x, y) - position, Color(0.18, 0.26, 0.21), 1)
	var screen := get_viewport_rect().size
	var visible := Rect2(-screen * 0.5 - Vector2(100, 160), screen + Vector2(200, 320))
	for command in render_order:
		match int(command[1]):
			0: _draw_player(int(command[2]),visible)
			1: _draw_monster(int(command[2]),visible)
			2: map_objects.draw_object(self,command[2],position,server_time)

func _draw_monster(id: int, visible: Rect2) -> void:
	if not visible_monsters.has(id):
		return
	var point: Vector2 = rendered_monsters.get(id, visible_monsters[id].p) - position
	if not visible.has_point(point):
		return
	if not atlas.pages.is_empty():
		var monster: Dictionary = visible_monsters[id]
		var action: String = ["NS", "NW", "FA", "NH", "ND"][monster.pose]
		var seconds: float = maxf(0.0,server_time-monster.action_time)
		var duration: float = ACTION.DEATH_SECONDS if monster.pose == 4 else ACTION.COMBAT_SECONDS
		atlas.draw_frame(self, point, "EN004A_%s%d" % [action, monster.facing], maxf(seconds, 0.0), 8.0,
			duration if monster.pose >= 2 else 0.0)
		if monster.hp > 0 and monster.hp < 30:
			draw_rect(Rect2(point+Vector2(-20,-150),Vector2(40,4)),Color(0.1,0.1,0.1))
			draw_rect(Rect2(point+Vector2(-20,-150),Vector2(40.0*monster.hp/30.0,4)),Color.CRIMSON)
	else:
		draw_circle(point, 16, Color.CRIMSON)

func _draw_player(id: int, visible: Rect2) -> void:
	if not visible_players.has(id):
		return
	var color := Color.CYAN if id == multiplayer.get_unique_id() else Color.LIGHT_BLUE
	var point: Vector2 = rendered_players.get(id, visible_players[id].p) - position
	if not visible.has_point(point):
		return
	var state: Dictionary = visible_players[id]
	var pose: int = state.pose
	var facing: int = state.facing
	var seconds: float = maxf(0.0,server_time-state.action_time)
	if id == multiplayer.get_unique_id() and prediction.initialized and state.hp > 0 and not ACTION.active(pose,state.action_time,server_time):
		pose = 0 if prediction.motion.is_zero_approx() else 1
		facing = prediction.facing
		seconds = maxf(0.0,server_time-prediction.walk_started)
	if not doll.parts.is_empty():
		doll.draw(self,point,pose,facing,seconds)
	else:
		draw_circle(point,13,color)
	if state.pose == 2 and ACTION.active(state.pose,state.action_time,server_time):
		var direction := [6,7,8,1,2,3,4,5].find(int(state.facing))
		draw_line(point,point+Vector2.from_angle(direction*PI/4.0)*45.0,Color.WHITE,4)


func _build_ui() -> void:
	get_window().max_size = Vector2i(1440, 1080)
	get_window().min_size = Vector2i(640, 360)
	drop_renderer = preload("res://scripts/drop_renderer.gd").new()
	drop_renderer.z_index = -1
	add_child(drop_renderer)
	var camera := Camera2D.new()
	camera.enabled = true
	add_child(camera)
	hud = CanvasLayer.new()
	add_child(hud)
	var panel := PanelContainer.new()
	panel.name = "Panel"
	panel.set_anchors_preset(Control.PRESET_TOP_LEFT)
	panel.position = Vector2(15, 15)
	hud.add_child(panel)
	var stack := VBoxContainer.new()
	panel.add_child(stack)
	info_label = Label.new()
	info_label.text = "連線中… 方向鍵移動，點怪攻擊，點掉落拾取；F2 切換 UI"
	stack.add_child(info_label)
	item_label = Label.new()
	item_label.text = "物品：{}"
	stack.add_child(item_label)
	health_label = Label.new()
	health_label.text = "生命 —"
	stack.add_child(health_label)
	var sizes := OptionButton.new()
	var window_sizes := [Vector2i(960, 540), Vector2i(1280, 720), Vector2i(1440, 810), Vector2i(1440, 900)]
	for size in window_sizes:
		sizes.add_item("%d × %d" % [size.x, size.y])
	_sync_window_size(sizes, window_sizes)
	sizes.item_selected.connect(func(index: int) -> void:
		get_window().size = window_sizes[index])
	get_window().size_changed.connect(func() -> void: _sync_window_size(sizes, window_sizes))
	stack.add_child(sizes)
	_style_ui()

func _sync_window_size(button: OptionButton, presets: Array) -> void:
	var size := get_window().size
	var index := presets.find(size)
	button.select(index)
	if index < 0:
		button.text = "自訂 %d × %d" % [size.x, size.y]

func _style_ui() -> void:
	var panel := hud.get_node("Panel") as PanelContainer
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.07, 0.1, 0.15, 0.94) if ui_modern else Color(0.27, 0.19, 0.1, 0.94)
	style.border_color = Color(0.2, 0.65, 0.8) if ui_modern else Color(0.78, 0.56, 0.25)
	style.set_border_width_all(2)
	style.set_content_margin_all(10)
	panel.add_theme_stylebox_override("panel", style)

func _load_sample_atlas() -> void:
	var path := asset_root.path_join("enemy/en004a.json")
	if FileAccess.file_exists(path):
		var error: Error = atlas.open(path)
		if error != OK:
			push_error("Cannot load entity atlas: %s" % error)
	var player_path := asset_root.path_join("player/b/standard/b-body.json")
	if FileAccess.file_exists(player_path):
		var error: Error = doll.open(asset_root)
		if error != OK:
			push_error("Cannot load player composition: %s" % error)

func _process(delta: float) -> void:
	if is_server:
		return
	if profiler != null:
		profiler.sample(delta)
	server_time = clock_sync.advance(Time.get_ticks_usec()/1000000.0)
	ping_elapsed += delta
	if ping_elapsed >= 1.0 and map_ready and multiplayer.multiplayer_peer != null and multiplayer.multiplayer_peer.get_connection_status() == MultiplayerPeer.CONNECTION_CONNECTED:
		ping_elapsed = 0.0
		_clock_ping.rpc_id(1,Time.get_ticks_usec()/1000000.0)
	for id in rendered_players.keys():
		if not visible_players.has(id):
			rendered_players.erase(id)
	for id in rendered_monsters.keys():
		if not visible_monsters.has(id):
			rendered_monsters.erase(id)
	for id in visible_players:
		if id == multiplayer.get_unique_id() and prediction.initialized:
			rendered_players[id] = prediction.render(Engine.get_physics_interpolation_fraction(),delta)
			continue
		rendered_players[id] = replica.position_at(visible_players[id],server_time,prediction.navigation)
	for id in visible_monsters:
		rendered_monsters[id] = replica.position_at(visible_monsters[id],server_time,prediction.navigation)
	position = rendered_players.get(multiplayer.get_unique_id(), position)
	var size := get_viewport_rect().size
	var view := Rect2(position-size*0.5,size)
	if terrain != null:
		terrain.position = -position
		terrain.update_view(view)
		map_objects.prepare(view.grow(180))
	render_elapsed += delta
	if render_elapsed >= 0.1:
		render_elapsed = 0.0
		render_order.clear()
		for object in map_objects.visible:
			render_order.append([float(object.p[1])+float(object.z),2,object])
		for id in visible_monsters:
			render_order.append([visible_monsters[id].p.y,1,id])
		for id in visible_players:
			render_order.append([visible_players[id].p.y,0,id])
		render_order.sort_custom(func(a: Array,b: Array) -> bool: return a[0] < b[0])
	drop_renderer.position = -position
	if drops_dirty:
		drop_renderer.rebuild(visible_drops)
		drops_dirty = false
	queue_redraw()
