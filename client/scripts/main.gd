extends Node2D

# Godot presentation and prediction. Authority lives in the C# server.
var port := 27888
const WORLD_SIZE := Vector2(2400, 1600)
const VIEW_RADIUS := 900.0
const MAX_DROPS := 5000

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
var monster_visuals := preload("res://scripts/monster_visuals.gd").new()
var world_generation := 0
var doll := preload("res://scripts/paper_doll.gd").new()
var rendered_players: Dictionary = {}
var rendered_monsters: Dictionary = {}
var last_snapshot := -1
var input_elapsed := 0.0
var bot := false
const CODEC = preload("res://scripts/snapshot_codec.gd")
const ACTION = preload("res://scripts/action_policy.gd")
const INPUT = preload("res://scripts/input_codec.gd")
var prediction := preload("res://scripts/local_prediction.gd").new()
var prediction_observed := false
var clock_sync := preload("res://scripts/server_clock.gd").new()
var ping_elapsed := 0.0
var server_time := 0.0
var drop_renderer: MultiMeshInstance2D
var drops_dirty := false
var action_observed := false
var inventory_observed := false
var asset_root := "res://assets/generated"
var profiler: RefCounted
var diagnostics_time := 0.0
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

const PLAYER_HP := 100
var own_id := 0
var network := preload("res://scripts/network_client.gd").new()
var wire := preload("res://scripts/message_pack.gd").new()

func _ready() -> void:
	var arguments := OS.get_cmdline_user_args()
	bot = arguments.has("--bot")
	if arguments.has("--server"):
		push_error("Start the C# server with dotnet run --project server/SGTest.Server.csproj")
		get_tree().quit(1)
		return
	if arguments.has("--profile"):
		profiler = preload("res://scripts/frame_profiler.gd").new()
	var host := "127.0.0.1"
	for argument in arguments:
		if argument.begins_with("--port="):
			port = clampi(int(argument.trim_prefix("--port=")),1024,65535)
		if argument.begins_with("--host="):
			host = argument.trim_prefix("--host=")
		if argument.begins_with("--assets="):
			asset_root = argument.trim_prefix("--assets=")
	print("SGO client starting")
	_build_ui()
	_load_sample_atlas()
	_start_client(host)
	for argument in arguments:
		if argument.begins_with("--capture="):
			await get_tree().create_timer(3.0).timeout
			await RenderingServer.frame_post_draw
			get_viewport().get_texture().get_image().save_png(argument.trim_prefix("--capture="))

func _start_client(host: String) -> void:
	network.start("ws://%s:%d/game" % [host,port])
	print("SGO client connecting to %s:%d" % [host,port])

func _exit_tree() -> void:
	network.close()

func _world_info(server_map: int, fingerprint: String, population_hash: String) -> void:
	world_generation += 1
	var generation := world_generation
	map_ready = false
	monster_visuals.reset()
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
	var definitions: Array = [{"id":0,"sprite":"EN004A","atlas":"enemy/en004a.json"}]
	if server_map != 0:
		var path := asset_root.path_join("maps/%d/population.json" % server_map)
		if FileAccess.get_sha256(path) != population_hash:
			push_error("Monster population does not match server")
			get_tree().quit(1)
			return
		var population: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
		if not population is Dictionary or int(population.get("version",0)) != 1:
			get_tree().quit(1)
			return
		definitions = population.definitions
	var started := Time.get_ticks_usec()
	for definition in definitions:
		if generation != world_generation:
			return
		var error: Error = monster_visuals.add(asset_root,definition)
		if error != OK:
			push_error("Cannot load monster definition %s (%s)" % [definition.id,error])
			get_tree().quit(1)
			return
		info_label.text = "載入怪物 %d / %d" % [monster_visuals.definitions.size(),definitions.size()]
		await get_tree().process_frame
	if generation != world_generation:
		return
	print("monster_palette "+JSON.stringify({"definitions":monster_visuals.definitions.size(),"shared_atlases":monster_visuals.shared.size(),"rgba_bytes":monster_visuals.rgba_bytes,"load_ms":(Time.get_ticks_usec()-started)/1000.0}))
	info_label.text = "已連線　%s" % (str(terrain.data.name) if terrain != null else "測試場地")
	map_ready = true
	network.send(24,[server_map,fingerprint,population_hash])

func _physics_process(delta: float) -> void:
	for bytes in network.receive():
		var packet: Array = wire.decode(bytes)
		if packet.is_empty():
			push_error("Invalid server protocol packet")
			network.close()
			return
		_dispatch(int(packet[1]),packet[2])
	diagnostics_time += delta
	if int(diagnostics_time/5.0) > int((diagnostics_time-delta)/5.0) and prediction.initialized:
		print("prediction_metrics "+JSON.stringify({"pending":prediction.pending.size(),"max_pending":prediction.max_pending,"max_correction_px":prediction.max_correction,"reconciliations":prediction.reconciliations}))
	_client_step(delta)

func _dispatch(op: int, b: Array) -> void:
	match op:
		0:
			own_id = int(b[0])
			print("client connected with peer %d" % own_id)
		1: _world_info(int(b[0]),str(b[1]),str(b[2]))
		2: _owner_state(b[0],b[1],b[2],b[3],Vector2(b[4],b[5]),b[6],b[7],b[8],b[9],b[10])
		3: _snapshot_chunk(b[0],b[1],b[2],b[3])
		4: _action_event(b[0],b[1],b[2],b[3],b[4],b[5],b[6],Vector2(b[7],b[8]),b[9],b[10])
		5: _ground_delta(b[0],PackedInt32Array(b[1]))
		6:
			var inventory: Dictionary = {}
			for row in b[0]: inventory[int(row[0])] = int(row[1])
			_inventory(inventory)
		7: _interest_remove(b[0],b[1],PackedInt32Array(b[2]))
		8: _clock_pong(b[0],b[1])

func _owner_state(tick: int, clock: float, acknowledged: int, epoch: int, at: Vector2, hp: int, facing: int, pose: int, started: float, revision: int) -> void:
	if not map_ready:
		return
	var id := own_id
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

func _action_event(kind: int, id: int, facing: int, pose: int, clock: float, hp: int, revision: int, at: Vector2, tick: int, definition: int) -> void:
	if not action_observed:
		action_observed = true
		print("action_received")
	var changed: int = replica.event(kind,id,tick,at,hp,facing,pose,clock,revision,definition)
	_replica_changed(kind,id,changed)

func _replica_changed(kind: int, id: int, changed: int) -> void:
	if changed == 0:
		return
	var state: Dictionary = replica.group(kind)[id]
	if changed == 2:
		if kind == 0:
			rendered_players[id] = state.p
			if id == own_id:
				prediction.freeze(state.p)
		else:
			rendered_monsters[id] = state.p
	if kind == 0:
		if id == own_id and state.pose >= 2:
			prediction.facing = state.facing
		_update_health(id,state.hp,state.pose)
	render_elapsed = 1.0

func _update_health(id: int, hp: int, pose: int) -> void:
	if not health_observed and hp < PLAYER_HP:
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
	if id == own_id:
		health_label.text = "生命 %d / %d%s" % [hp,PLAYER_HP,"　倒地，等待復活" if hp == 0 else ""]

func _snapshot_chunk(tick: int, clock: float, kind: int, payload: PackedByteArray) -> void:
	if kind not in [0,1] or payload.size() > CODEC.ROWS_PER_PACKET*CODEC.STRIDE:
		return
	clock_sync.observe_snapshot(clock,Time.get_ticks_usec()/1000000.0)
	for row in CODEC.decode(payload):
		_replica_changed(kind,int(row[0]),replica.snapshot(kind,tick,row))
	if tick%150 == 0 and tick != last_snapshot:
		last_snapshot = tick
		print("snapshot_received tick=%d players=%d monsters=%d drops=%d" % [tick,visible_players.size(),visible_monsters.size(),visible_drops.size()])
	queue_redraw()

func _interest_remove(tick: int, kind: int, ids: PackedInt32Array) -> void:
	replica.erase_interest(kind,tick,ids)
	if kind == 0:
		for id in ids:
			if not visible_players.has(id):
				observed_dead.erase(id)
	render_elapsed = 1.0

func _clock_pong(sent: float, stamp: float) -> void:
	clock_sync.observe_pong(sent,Time.get_ticks_usec()/1000000.0,stamp)

func _ground_delta(added: Array, removed: PackedInt32Array) -> void:
	for row in added:
		visible_drops[int(row[0])] = {"p": Vector2(row[1], row[2]), "item": int(row[3])}
	for id in removed:
		visible_drops.erase(id)
	drops_dirty = true

func _inventory(inventory: Dictionary) -> void:
	if not inventory_observed:
		inventory_observed = true
		print("inventory_received")
	local_state = inventory
	item_label.text = "物品：%s" % str(local_state)

func _client_step(delta: float) -> void:
	if not network.is_open() or own_id == 0:
		return
	if not map_ready:
		return
	var own_id := own_id
	if visible_players.has(own_id) and int(visible_players[own_id].hp) <= 0:
		return
	var motion := Input.get_vector("ui_left", "ui_right", "ui_up", "ui_down")
	if bot:
		motion = Vector2.from_angle(Time.get_ticks_msec() / 1000.0)
	prediction.step_input(motion,Time.get_ticks_usec()/1000000.0,server_time)
	var packet := prediction.packet()
	if not packet.is_empty():
		network.send(20,[prediction.epoch,packet])
	input_elapsed += delta
	if input_elapsed < 0.05:
		return
	input_elapsed = 0.0
	if bot:
		var own: Vector2 = visible_players.get(own_id, {"p":Vector2.ZERO}).p
		for id in visible_monsters:
			if visible_monsters[id].hp > 0 and own.distance_squared_to(visible_monsters[id].p) < 150.0*150.0:
				network.send(21,[id])
				break
		for id in visible_drops:
			if own.distance_squared_to(visible_drops[id].p) < 90.0*90.0:
				network.send(22,[id])
				break

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and event.keycode == KEY_F2:
		ui_modern = not ui_modern
		_style_ui()
	if not network.is_open() or own_id == 0:
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
			network.send(21,[selected])
			return
		for id in visible_drops:
			if world_point.distance_to(visible_drops[id].p) < 35.0:
				network.send(22,[id])
				return

func _draw() -> void:
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
	var monster: Dictionary = visible_monsters[id]
	var visual: Dictionary = monster_visuals.resolve(int(monster.definition))
	if not visual.is_empty():
		var atlas: RefCounted = visual.atlas
		var action: String = ["NS", "NW", "FA", "NH", "ND"][monster.pose]
		var seconds: float = maxf(0.0,server_time-monster.action_time)
		var duration: float = ACTION.DEATH_SECONDS if monster.pose == 4 else ACTION.COMBAT_SECONDS
		atlas.draw_frame(self, point, "%s_%s%d" % [visual.prefix,action,monster.facing], maxf(seconds, 0.0), 8.0,
			duration if monster.pose >= 2 else 0.0)
		if monster.hp > 0 and monster.hp < 30:
			draw_rect(Rect2(point+Vector2(-20,-150),Vector2(40,4)),Color(0.1,0.1,0.1))
			draw_rect(Rect2(point+Vector2(-20,-150),Vector2(40.0*monster.hp/30.0,4)),Color.CRIMSON)
	else:
		draw_circle(point, 16, Color.CRIMSON)

func _draw_player(id: int, visible: Rect2) -> void:
	if not visible_players.has(id):
		return
	var color := Color.CYAN if id == own_id else Color.LIGHT_BLUE
	var point: Vector2 = rendered_players.get(id, visible_players[id].p) - position
	if not visible.has_point(point):
		return
	var state: Dictionary = visible_players[id]
	var pose: int = state.pose
	var facing: int = state.facing
	var seconds: float = maxf(0.0,server_time-state.action_time)
	if id == own_id and prediction.initialized and state.hp > 0 and not ACTION.active(pose,state.action_time,server_time):
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
	var player_path := asset_root.path_join("player/b/standard/b-body.json")
	if FileAccess.file_exists(player_path):
		var error: Error = doll.open(asset_root)
		if error != OK:
			push_error("Cannot load player composition: %s" % error)

func _process(delta: float) -> void:
	if profiler != null:
		profiler.sample(delta)
	server_time = clock_sync.advance(Time.get_ticks_usec()/1000000.0)
	ping_elapsed += delta
	if ping_elapsed >= 1.0 and map_ready and network.is_open():
		ping_elapsed = 0.0
		network.send(23,[Time.get_ticks_usec()/1000000.0])
	for id in rendered_players.keys():
		if not visible_players.has(id):
			rendered_players.erase(id)
	for id in rendered_monsters.keys():
		if not visible_monsters.has(id):
			rendered_monsters.erase(id)
	for id in visible_players:
		if id == own_id and prediction.initialized:
			rendered_players[id] = prediction.render(Engine.get_physics_interpolation_fraction(),delta)
			continue
		rendered_players[id] = replica.position_at(visible_players[id],server_time,prediction.navigation)
	for id in visible_monsters:
		rendered_monsters[id] = replica.position_at(visible_monsters[id],server_time,prediction.navigation)
	position = rendered_players.get(own_id, position)
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
