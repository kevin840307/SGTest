extends Node2D

# Godot presentation and prediction. Authority lives in the C# server.
var port := 27888
const WORLD_SIZE := Vector2(2400, 1600)
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
var asset_store := preload("res://scripts/runtime_asset_store.gd").new()
var monster_visuals := preload("res://scripts/monster_visuals.gd").new()
var world_generation := 0
var doll := preload("res://scripts/paper_doll.gd").new()
var rendered_players: Dictionary = {}
var rendered_monsters: Dictionary = {}
var last_snapshot := -1
var input_elapsed := 0.0
var bot := false
var bot_index := 0
const CODEC = preload("res://scripts/snapshot_codec.gd")
const ACTION = preload("res://scripts/action_policy.gd")
const INPUT = preload("res://scripts/input_codec.gd")
const LEGACY_FACING = preload("res://scripts/legacy_facing.gd")
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
var monster_asset_root := ""
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
var runtime_trace := false
var trace_started_usec := 0
var trace_last_usec := 0
var trace_physics_steps := 0
var trace_render_frames := 0
var trace_visual_updates := 0
var trace_snapshots := 0
var trace_owner_states := 0
var trace_actions := 0
var trace_input_commands := 0
var trace_last_rx := 0
var trace_last_tx := 0
var trace_origin := Vector2.ZERO
var trace_origin_set := false
var visual_target_fps := 0
var visual_elapsed := 0.0
var no_world_render := false
var unfocus_low_power_enabled := true
var render_target_fps := 60

const PLAYER_HP := 100
var own_id := 0
var network := preload("res://scripts/network_client.gd").new()
var wire := preload("res://scripts/message_pack.gd").new()

func _ready() -> void:
	var arguments := OS.get_cmdline_user_args()
	bot = arguments.has("--bot")
	runtime_trace = arguments.has("--runtime-trace")
	if runtime_trace:
		trace_started_usec = Time.get_ticks_usec()
		trace_last_usec = trace_started_usec
	if arguments.has("--server"):
		push_error("Start the C# server with dotnet run --project server/SGTest.Server.csproj")
		get_tree().quit(1)
		return
	if arguments.has("--profile"):
		profiler = preload("res://scripts/frame_profiler.gd").new()
	var host := "127.0.0.1"
	for argument in arguments:
		if argument.begins_with("--bot-index="):
			bot_index = int(argument.trim_prefix("--bot-index="))
		if argument.begins_with("--profile-seconds=") and profiler != null:
			profiler.sample_seconds = maxf(10.0,float(argument.trim_prefix("--profile-seconds=")))
		if argument.begins_with("--port="):
			port = clampi(int(argument.trim_prefix("--port=")),1024,65535)
		if argument.begins_with("--host="):
			host = argument.trim_prefix("--host=")
		if argument.begins_with("--assets="):
			asset_root = argument.trim_prefix("--assets=")
		if argument.begins_with("--monster-assets="):
			monster_asset_root = argument.trim_prefix("--monster-assets=")
		if argument.begins_with("--visual-fps="):
			visual_target_fps = int(argument.trim_prefix("--visual-fps="))
		if argument.begins_with("--render-fps="):
			render_target_fps = int(argument.trim_prefix("--render-fps="))
		if argument.begins_with("--unfocus-low-power="):
			unfocus_low_power_enabled = int(argument.trim_prefix("--unfocus-low-power=")) != 0
		if argument == "--no-world-render":
			no_world_render = true
	if render_target_fps < 30 or render_target_fps > 90:
		push_error("--render-fps must be within 30..90")
		get_tree().quit(1)
		return
	if visual_target_fps not in [0,5,10,15]:
		push_error("--visual-fps supports only 5/10/15 low-power presentation rates")
		get_tree().quit(1)
		return
	# Network/state/input live on the 30Hz fixed logic contract. Never allow an
	# external/project render cap below 30 to turn physics catch-up into 200ms
	# packet/command bursts; 5/10/15 low-power is presentation-only.
	if Engine.max_fps > 0 and Engine.max_fps < 30:
		Engine.max_fps = 30
	if "--render-fps=%d" % render_target_fps in arguments:
		Engine.max_fps = render_target_fps
	if visual_target_fps > 0:
		Engine.max_fps = 30
	print("SGO client starting")
	asset_store.configure(24 * 1024 * 1024, 16 * 1024 * 1024)
	asset_store.start()
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
	map_objects.reset(false)
	monster_visuals.reset(false)
	doll.reset(false)
	asset_store.close()
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
		map_objects.configure(asset_root,terrain.data,asset_store,"map-objects")
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
	var spawn_counts: Dictionary = {0: 1}
	if server_map != 0:
		var path := asset_root.path_join("maps/%d/population.json" % server_map)
		if FileAccess.get_sha256(path) != population_hash:
			push_error("Monster population does not match server")
			get_tree().quit(1)
			return
		var population: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
		if not population is Dictionary or int(population.get("version",0)) not in [1,2]:
			get_tree().quit(1)
			return
		definitions = population.definitions
		spawn_counts.clear()
		for spawn in population.spawns:
			var definition_id := int(spawn.definition)
			spawn_counts[definition_id] = int(spawn_counts.get(definition_id,0)) + 1
	var started := Time.get_ticks_usec()
	var monster_root: String = asset_root if monster_asset_root.is_empty() else monster_asset_root
	if monster_visuals.begin_async(asset_store,"map-monsters") != OK:
		push_error("Cannot start monster asset owner")
		get_tree().quit(1)
		return
	for definition in definitions:
		if generation != world_generation:
			return
		var priority := int(spawn_counts.get(int(definition.id),0))
		var error: Error = monster_visuals.add_async(monster_root,definition,priority)
		if error != OK:
			push_error("Cannot queue monster definition %s (%s)" % [definition.id,error])
			get_tree().quit(1)
			return
	while not monster_visuals.is_ready():
		if generation != world_generation:
			return
		monster_visuals.refresh()
		var load_error := monster_visuals.first_error()
		if load_error != OK:
			push_error("Cannot load monster palette (%s)" % load_error)
			get_tree().quit(1)
			return
		info_label.text = "載入怪物 %d / %d" % [monster_visuals.loaded_count(),monster_visuals.shared.size()]
		await get_tree().process_frame
	if generation != world_generation:
		return
	print("monster_palette "+JSON.stringify({"definitions":monster_visuals.definitions.size(),"shared_atlases":monster_visuals.shared.size(),"rgba_bytes":monster_visuals.rgba_bytes,"load_ms":(Time.get_ticks_usec()-started)/1000.0,"async_store":asset_store.statistics()}))
	info_label.text = "已連線　%s" % (str(terrain.data.name) if terrain != null else "測試場地")
	map_ready = true
	network.send(24,[server_map,fingerprint,population_hash])

func _physics_process(delta: float) -> void:
	if runtime_trace:
		trace_physics_steps += 1
	for bytes in network.receive():
		var packet: Array = wire.decode(bytes)
		if packet.is_empty():
			push_error("Invalid server protocol packet")
			network.close()
			return
		_dispatch(int(packet[1]),packet[2])
	# ServerSimClock belongs to fixed logic, never to visual/render cadence.
	server_time = clock_sync.advance(Time.get_ticks_usec()/1000000.0)
	diagnostics_time += delta
	if int(diagnostics_time/5.0) > int((diagnostics_time-delta)/5.0) and prediction.initialized:
		print("prediction_metrics "+JSON.stringify({"pending":prediction.pending.size(),"max_pending":prediction.max_pending,"max_correction_px":prediction.max_correction,"reconciliations":prediction.reconciliations}))
		var asset_metrics := asset_store.statistics()
		print("asset_store_metrics "+JSON.stringify({"resident_bytes":asset_metrics.resident_bytes,"ready_queue_bytes":asset_metrics.ready_queue_bytes,"entries":asset_metrics.entries,"pending":asset_metrics.pending,"cache_hits":asset_metrics.cache_hits,"cache_misses":asset_metrics.cache_misses,"evictions":asset_metrics.evictions,"peak_resident_bytes":asset_metrics.peak_resident_bytes}))
	_client_step(delta)
	ping_elapsed += delta
	if ping_elapsed >= 1.0 and map_ready and network.is_open():
		ping_elapsed = 0.0
		network.send(23,[Time.get_ticks_usec()/1000000.0])
	if runtime_trace:
		_runtime_trace_report()

func _runtime_trace_report() -> void:
	var now := Time.get_ticks_usec()
	var elapsed := (now-trace_last_usec)/1000000.0
	if elapsed < 5.0:
		return
	var net := network.statistics()
	var rx := int(net.received_packets)
	var tx := int(net.sent_packets)
	var own_state: Dictionary = visible_players.get(own_id,{})
	var own_position: Vector2 = own_state.get("p",Vector2.ZERO)
	if not trace_origin_set and prediction.initialized:
		trace_origin = own_position
		trace_origin_set = true
	var focused := true
	var window_mode := -1
	if DisplayServer.get_name() != "headless":
		focused = DisplayServer.window_is_focused()
		window_mode = int(DisplayServer.window_get_mode())
	var report := {"elapsed_s":(now-trace_started_usec)/1000000.0,
		"window_s":elapsed,
		"physics_hz":trace_physics_steps/elapsed,
		"engine_frame_hz":trace_render_frames/elapsed,
		"visual_update_hz":trace_visual_updates/elapsed,
		"visual_target_fps":visual_target_fps,
		"no_world_render":no_world_render,
		"window_focused":focused,
		"window_mode":window_mode,
		"prediction_sequence":prediction.sequence,
		"prediction_last_tick":prediction.last_tick,
		"prediction_pending":prediction.pending.size(),
		"own_relative":[own_position.x-trace_origin.x,own_position.y-trace_origin.y] if trace_origin_set else [0.0,0.0],
		"texture_memory_bytes":int(RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TEXTURE_MEM_USED)),
		"video_memory_bytes":int(RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_VIDEO_MEM_USED)),
		"static_memory_bytes":OS.get_static_memory_usage(),
		"rx_packets":rx-trace_last_rx,
		"tx_packets":tx-trace_last_tx,
		"snapshots":trace_snapshots,
		"owner_states":trace_owner_states,
		"actions":trace_actions,
		"input_commands":trace_input_commands,
		"input_hz":trace_input_commands/elapsed,
		"queue_age_p95_ms":net.age_p95_ms,
		"queue_age_p99_ms":net.age_p99_ms,
		"queue_age_max_ms":net.age_max_ms,
		"incoming_depth":net.incoming_depth,
		"max_incoming_depth":net.max_incoming_depth,
		"network_failed":net.failed}
	print("runtime_trace "+JSON.stringify(report))
	trace_last_usec = now
	trace_physics_steps = 0
	trace_render_frames = 0
	trace_visual_updates = 0
	trace_snapshots = 0
	trace_owner_states = 0
	trace_actions = 0
	trace_input_commands = 0
	trace_last_rx = rx
	trace_last_tx = tx

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

func _authority_state_hash() -> String:
	var rows: Array = []
	var player_ids := visible_players.keys()
	player_ids.sort()
	for id in player_ids:
		var state: Dictionary = visible_players[id]
		var p: Vector2 = state.p
		rows.append([0,int(id),roundi(p.x*1000.0),roundi(p.y*1000.0),int(state.hp),
			int(state.facing),int(state.pose),int(state.revision),int(state.get("definition",0))])
	var monster_ids := visible_monsters.keys()
	monster_ids.sort()
	for id in monster_ids:
		var state: Dictionary = visible_monsters[id]
		var p: Vector2 = state.p
		rows.append([1,int(id),roundi(p.x*1000.0),roundi(p.y*1000.0),int(state.hp),
			int(state.facing),int(state.pose),int(state.revision),int(state.get("definition",0))])
	var drop_ids := visible_drops.keys()
	drop_ids.sort()
	for id in drop_ids:
		var drop: Dictionary = visible_drops[id]
		var p: Vector2 = drop.p
		rows.append([2,int(id),roundi(p.x*1000.0),roundi(p.y*1000.0),int(drop.get("definition",0))])
	return JSON.stringify(rows).sha256_text()

func _owner_state(tick: int, clock: float, acknowledged: int, epoch: int, at: Vector2, hp: int, facing: int, pose: int, started: float, revision: int) -> void:
	if runtime_trace:
		trace_owner_states += 1
	if not map_ready:
		return
	var id := own_id
	if visible_players.has(id):
		var current: Dictionary = visible_players[id]
		if ACTION.newer_revision(current.revision,revision) and (int(current.hp) == 0) != (hp == 0):
			return
	clock_sync.observe_snapshot(clock,Time.get_ticks_usec()/1000000.0)
	_replica_changed(0,id,replica.snapshot(0,tick,[id,at.x,at.y,hp,facing,pose,started,revision]))
	if prediction.authority(tick,acknowledged,epoch,at,hp,Time.get_ticks_usec()/1000000.0):
		if prediction.motion.is_zero_approx():
			prediction.facing = facing
		if not prediction_observed:
			prediction_observed = true
			print("prediction_ready")
	if runtime_trace and tick%150 == 0:
		print("state_checkpoint "+JSON.stringify({"tick":tick,"hash":_authority_state_hash(),
			"players":visible_players.size(),"monsters":visible_monsters.size(),"drops":visible_drops.size()}))

func _action_event(kind: int, id: int, facing: int, pose: int, clock: float, hp: int, revision: int, at: Vector2, tick: int, definition: int) -> void:
	if runtime_trace:
		trace_actions += 1
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
	if runtime_trace:
		trace_snapshots += 1
	if kind not in [0,1] or payload.size() > CODEC.ROWS_PER_PACKET*CODEC.STRIDE:
		return
	clock_sync.observe_snapshot(clock,Time.get_ticks_usec()/1000000.0)
	for row in CODEC.decode(payload):
		_replica_changed(kind,int(row[0]),replica.snapshot(kind,tick,row))
	if tick%150 == 0 and tick != last_snapshot:
		last_snapshot = tick
		print("snapshot_received tick=%d players=%d monsters=%d drops=%d" % [tick,visible_players.size(),visible_monsters.size(),visible_drops.size()])
	# World redraw cadence is owned by VisualClock in _process. Network/state
	# updates must not force a render frame in low-power/no-world modes.

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
		# Deterministic per-input-sequence bot trace. Render FPS and wall-clock
		# scheduling cannot change the requested motion series.
		motion = Vector2.from_angle(float((prediction.sequence+1)%360)*PI/180.0+bot_index*0.733)
	prediction.step_input(motion,Time.get_ticks_usec()/1000000.0,server_time)
	var packet := prediction.packet()
	if not packet.is_empty():
		network.send(20,[prediction.epoch,packet])
		if runtime_trace:
			trace_input_commands += 1
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
		var facing := replica.facing_at(monster)
		atlas.draw_frame(self, point, "%s_%s%d" % [visual.prefix,action,facing], maxf(seconds, 0.0), 8.0,
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
	var facing: int = replica.facing_at(state)
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
		var direction := LEGACY_FACING.vector(replica.facing_at(state))
		draw_line(point,point+direction*45.0,Color.WHITE,4)


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

	var performance_title := Label.new()
	performance_title.text = "效能"
	stack.add_child(performance_title)
	var fps_button := OptionButton.new()
	var fps_options := [30,45,60,75,90]
	for fps in fps_options:
		fps_button.add_item("%d FPS" % fps)
	var fps_index := fps_options.find(render_target_fps)
	fps_button.select(fps_index if fps_index >= 0 else fps_options.find(60))
	fps_button.item_selected.connect(func(index: int) -> void:
		render_target_fps = int(fps_options[index])
		Engine.max_fps = render_target_fps)
	stack.add_child(fps_button)
	var unfocus_toggle := CheckBox.new()
	unfocus_toggle.text = "Unfocus 節能（失焦降為 15 FPS）"
	unfocus_toggle.button_pressed = unfocus_low_power_enabled
	unfocus_toggle.toggled.connect(func(enabled: bool) -> void:
		unfocus_low_power_enabled = enabled)
	stack.add_child(unfocus_toggle)
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
	# Headless clients never draw the paper doll; avoid decoding 8.3 MiB of
	# visual-only resources while preserving their network/state workload.
	if DisplayServer.get_name() == "headless":
		return
	var player_path := asset_root.path_join("player/b/standard/b-body.json")
	if FileAccess.file_exists(player_path):
		var error: Error = doll.begin_async(asset_store,asset_root,"player-core")
		if error != OK:
			push_error("Cannot queue player composition: %s" % error)

func _process(delta: float) -> void:
	if runtime_trace:
		trace_render_frames += 1
	# One visual commit budget for every owner. Network/state processing stays in
	# _physics_process and is never gated by visual readiness.
	asset_store.poll_gpu(1,1200)
	doll.refresh()
	monster_visuals.refresh()
	if doll.first_error() != OK:
		push_error("Player composition async load failed: %s" % doll.first_error())
		doll.reset(false)
	if profiler != null:
		profiler.sample(delta)
	visual_elapsed += delta
	var minimized := false
	var focused := true
	if DisplayServer.get_name() != "headless":
		minimized = DisplayServer.window_get_mode() == DisplayServer.WINDOW_MODE_MINIMIZED
		focused = DisplayServer.window_is_focused()
	# Minimized windows always stop world rendering without unloading resources.
	# Unfocused windows default to 15 FPS presentation and can be disabled by the
	# future performance setting/CLI; network, reducer and fixed logic stay 30Hz.
	if no_world_render or minimized:
		return
	var effective_visual_fps := visual_target_fps
	if unfocus_low_power_enabled and not focused:
		effective_visual_fps = 15 if effective_visual_fps == 0 else mini(effective_visual_fps,15)
	if effective_visual_fps > 0:
		var interval := 1.0/float(effective_visual_fps)
		if visual_elapsed + 0.000001 < interval:
			return
		visual_elapsed = fmod(visual_elapsed,interval)
	if runtime_trace:
		trace_visual_updates += 1
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
	if terrain != null and map_ready:
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
			render_order.append([rendered_monsters.get(id,visible_monsters[id].p).y,1,id])
		for id in visible_players:
			render_order.append([rendered_players.get(id,visible_players[id].p).y,0,id])
		render_order.sort_custom(func(a: Array,b: Array) -> bool: return a[0] < b[0])
	drop_renderer.position = -position
	if drops_dirty:
		drop_renderer.rebuild(visible_drops)
		drops_dirty = false
	queue_redraw()
