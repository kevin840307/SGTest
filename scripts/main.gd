extends Node2D

# Standalone, server authoritative prototype. Start server with --headless -- --server.
const PORT := 27888
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
var visible_players: Dictionary = {}
var visible_monsters: Dictionary = {}
var visible_drops: Dictionary = {}
var ui_modern := false
var hud: CanvasLayer
var info_label: Label
var item_label: Label
var atlas := preload("res://scripts/entity_atlas.gd").new()
var monster_grid := preload("res://scripts/spatial_grid.gd").new()
var drop_grid := preload("res://scripts/spatial_grid.gd").new()
var player_grid := preload("res://scripts/spatial_grid.gd").new()
var rendered_players: Dictionary = {}
var rendered_monsters: Dictionary = {}
var snapshot_sequence := 0
var last_snapshot := -1
var input_elapsed := 0.0
var animation_time := 0.0
var bot := false
var seed_drops := 0
var bind_address := "127.0.0.1"
const CODEC = preload("res://scripts/snapshot_codec.gd")
var pending_snapshots: Dictionary = {}
var player_poses: Dictionary = {}
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

func _ready() -> void:
	var arguments := OS.get_cmdline_user_args()
	is_server = arguments.has("--server")
	bot = arguments.has("--bot")
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
		print("SGO client connecting to %s:%d" % [host, PORT])
		for argument in arguments:
			if argument.begins_with("--capture="):
				await get_tree().create_timer(3.0).timeout
				await RenderingServer.frame_post_draw
				get_viewport().get_texture().get_image().save_png(argument.trim_prefix("--capture="))

func _start_server() -> void:
	var peer := ENetMultiplayerPeer.new()
	peer.set_bind_ip(bind_address)
	var error := peer.create_server(PORT, 128)
	if error != OK:
		push_error("Cannot start server: %s" % error)
		get_tree().quit(1)
		return
	multiplayer.multiplayer_peer = peer
	multiplayer.peer_connected.connect(_on_peer_connected)
	world.populate(monster_count, seed_drops)
	drop_grid.rebuild(drops)
	print("SGO server listening on UDP %d with %d monsters" % [PORT, monster_count])

func _start_client(host: String) -> void:
	var peer := ENetMultiplayerPeer.new()
	var error := peer.create_client(host, PORT)
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
	print("player %d joined" % id)

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
		_client_step(delta)

func _server_step(delta: float) -> void:
	world.step(delta)
	sim_time = world.time
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
		snapshot_sequence += 1
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
			ps.append([id, player.p.x, player.p.y, 0, player.facing, player.pose, player.action_time])
	for id in monster_grid.query(center, VIEW_RADIUS):
		var monster: Dictionary = monsters[id]
		if center.distance_squared_to(monster.p) <= VIEW_RADIUS * VIEW_RADIUS:
			ms.append([id, monster.p.x, monster.p.y, monster.hp, monster.facing, monster.pose, monster.action_time])
	var encoded := CODEC.encode(ps + ms)
	var count := maxi(1, ceili(encoded.size() / 800.0))
	for index in count:
		_snapshot_chunk.rpc_id(pid, snapshot_sequence, sim_time, ps.size(), index, count,
			encoded.slice(index * 800, mini((index+1)*800, encoded.size())))

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
func _move_input(x: float, y: float) -> void:
	if not is_server:
		return
	world.move(multiplayer.get_remote_sender_id(), x, y)

@rpc("any_peer", "reliable")
func _attack(monster_id: int) -> void:
	if not is_server:
		return
	var id := multiplayer.get_remote_sender_id()
	var first_drop: int = world.next_drop
	if world.attack(id, monster_id):
		_publish_action(0, id, players[id])
		_publish_action(1, monster_id, monsters[monster_id])
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
			_action_event.rpc_id(pid, kind, id, state.facing, state.pose, state.action_time)

@rpc("authority", "call_remote", "reliable", 4)
func _action_event(kind: int, id: int, facing: int, pose: int, clock: float) -> void:
	if not action_observed:
		action_observed = true
		print("action_received")
	if kind == 1 and visible_monsters.has(id):
		var state: Dictionary = visible_monsters[id]
		if clock >= state.action_time:
			state.facing = facing
			state.pose = pose
			state.action_time = clock
	elif kind == 0 and player_poses.has(id):
		var row: Array = player_poses[id]
		if clock >= row[6]:
			row[4] = facing
			row[5] = pose
			row[6] = clock

@rpc("authority", "call_remote", "unreliable", 2)
func _snapshot_chunk(sequence: int, clock: float, player_count: int, part: int, total: int, payload: PackedByteArray) -> void:
	if is_server or sequence <= last_snapshot:
		return
	if total < 1 or total > 512 or part < 0 or part >= total or payload.size() > 800:
		return
	if not pending_snapshots.has(sequence):
		pending_snapshots[sequence] = {}
	var chunks: Dictionary = pending_snapshots[sequence]
	chunks[part] = payload
	for old in pending_snapshots.keys():
		if old < sequence - 2:
			pending_snapshots.erase(old)
	if chunks.size() != total:
		return
	var bytes := PackedByteArray()
	for index in total:
		bytes.append_array(chunks[index])
	var rows := CODEC.decode(bytes)
	var ps := rows.slice(0, player_count)
	var ms := rows.slice(player_count)
	server_time = clock
	last_snapshot = sequence
	pending_snapshots.erase(sequence)
	if sequence % 50 == 0:
		print("snapshot_received sequence=%d players=%d monsters=%d drops=%d" % [sequence, ps.size(), ms.size(), visible_drops.size()])
	visible_players.clear()
	var previous_monsters := visible_monsters.duplicate()
	var previous_poses := player_poses.duplicate()
	visible_monsters.clear()
	player_poses.clear()
	for row in ps:
		visible_players[int(row[0])] = Vector2(row[1], row[2])
		player_poses[int(row[0])] = row
		if previous_poses.has(int(row[0])) and previous_poses[int(row[0])][6] > row[6]:
			player_poses[int(row[0])] = previous_poses[int(row[0])]
	for row in ms:
		visible_monsters[int(row[0])] = {"p": Vector2(row[1], row[2]), "hp": int(row[3]),
			"facing": int(row[4]), "pose": int(row[5]), "action_time": float(row[6])}
		if previous_monsters.has(int(row[0])) and previous_monsters[int(row[0])].action_time > float(row[6]):
			var prior: Dictionary = previous_monsters[int(row[0])]
			visible_monsters[int(row[0])].facing = prior.facing
			visible_monsters[int(row[0])].pose = prior.pose
			visible_monsters[int(row[0])].action_time = prior.action_time
	var own_id := multiplayer.get_unique_id()
	if visible_players.has(own_id):
		var target: Vector2 = visible_players[own_id]
		if rendered_players.is_empty():
			position = target
	queue_redraw()

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
	input_elapsed += delta
	if input_elapsed < 0.05:
		return
	input_elapsed = 0.0
	var motion := Input.get_vector("ui_left", "ui_right", "ui_up", "ui_down")
	if bot:
		motion = Vector2.from_angle(Time.get_ticks_msec() / 1000.0)
		var own: Vector2 = visible_players.get(multiplayer.get_unique_id(), Vector2.ZERO)
		for id in visible_monsters:
			if visible_monsters[id].hp > 0 and own.distance_squared_to(visible_monsters[id].p) < 150.0*150.0:
				_attack.rpc_id(1,id)
				break
		for id in visible_drops:
			if own.distance_squared_to(visible_drops[id].p) < 90.0*90.0:
				_pickup.rpc_id(1,id)
				break
	_move_input.rpc_id(1, motion.x, motion.y)

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
	draw_rect(Rect2(-position, WORLD_SIZE), Color(0.12, 0.19, 0.16))
	for x in range(0, int(WORLD_SIZE.x), 100):
		draw_line(Vector2(x, 0) - position, Vector2(x, WORLD_SIZE.y) - position, Color(0.18, 0.26, 0.21), 1)
	for y in range(0, int(WORLD_SIZE.y), 100):
		draw_line(Vector2(0, y) - position, Vector2(WORLD_SIZE.x, y) - position, Color(0.18, 0.26, 0.21), 1)
	var screen := get_viewport_rect().size
	var visible := Rect2(-screen * 0.5 - Vector2(100, 160), screen + Vector2(200, 320))
	for id in visible_monsters:
		var point: Vector2 = rendered_monsters.get(id, visible_monsters[id].p) - position
		if not visible.has_point(point):
			continue
		if not atlas.pages.is_empty():
			var monster: Dictionary = visible_monsters[id]
			var action: String = ["NS", "NW", "FA", "NH", "ND"][monster.pose]
			var seconds: float = server_time - monster.action_time if monster.pose >= 2 else animation_time
			atlas.draw_frame(self, point, "EN004A_%s%d" % [action, monster.facing], maxf(seconds, 0.0))
		else:
			draw_circle(point, 16, Color.CRIMSON)
	for id in visible_players:
		var color := Color.CYAN if id == multiplayer.get_unique_id() else Color.LIGHT_BLUE
		var point: Vector2 = rendered_players.get(id, visible_players[id]) - position
		draw_circle(point, 13, color)
		if player_poses.has(id):
			var row: Array = player_poses[id]
			if int(row[5]) == 2 and server_time-float(row[6]) < 0.35:
				var direction := [6,7,8,1,2,3,4,5].find(int(row[4]))
				draw_line(point, point + Vector2.from_angle(direction * PI / 4.0) * 45.0, Color.WHITE, 4)

func _build_ui() -> void:
	get_window().max_size = Vector2i(1440, 1080)
	get_window().min_size = Vector2i(640, 360)
	drop_renderer = preload("res://scripts/drop_renderer.gd").new()
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

func _process(delta: float) -> void:
	if is_server:
		return
	if profiler != null:
		profiler.sample(delta)
	animation_time += delta
	server_time += delta
	var weight := 1.0 - exp(-delta * 20.0)
	for id in rendered_players.keys():
		if not visible_players.has(id):
			rendered_players.erase(id)
	for id in rendered_monsters.keys():
		if not visible_monsters.has(id):
			rendered_monsters.erase(id)
	for id in visible_players:
		var target: Vector2 = visible_players[id]
		rendered_players[id] = (rendered_players.get(id, target) as Vector2).lerp(target, weight)
	for id in visible_monsters:
		var target: Vector2 = visible_monsters[id].p
		rendered_monsters[id] = (rendered_monsters.get(id, target) as Vector2).lerp(target, weight)
	position = rendered_players.get(multiplayer.get_unique_id(), position)
	drop_renderer.position = -position
	if drops_dirty:
		drop_renderer.rebuild(visible_drops)
		drops_dirty = false
	queue_redraw()
