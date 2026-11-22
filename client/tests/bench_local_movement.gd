extends SceneTree
const Move = preload("res://movement/local_movement.gd")
func _initialize() -> void: run.call_deferred()
func run() -> void:
	var w := 960
	var h := 960
	var bytes := PackedByteArray()
	bytes.resize(8+w*h)
	bytes.encode_u32(0,w);bytes.encode_u32(4,h)
	for y in range(80,130):
		bytes[8+y*w+130] = 1
	var m := Move.new()
	m.configure(bytes,{"collision_cell":16,"size":[w*16,h*16]},1,1)
	m.position = Vector2(85*16+8,105*16+8)
	var end := Vector2(175*16+8,105*16+8)
	var start := Time.get_ticks_usec()
	var ok := m.request_target(end,1)
	var millis := float(Time.get_ticks_usec()-start)/1000.0
	print("MAP960_PATH_ENQUEUE_MS ",millis," requested=",ok," expanded_budget=",Move.MAX_EXPANDED," Astar=",m._full_path_searches)
	var worst_frame := 0.0
	var search_frames := 0
	while m.active and not m._search.is_empty() and search_frames < 20:
		start = Time.get_ticks_usec()
		m.advance(1.0/60.0)
		var frame_ms := float(Time.get_ticks_usec()-start)/1000.0
		worst_frame = maxf(worst_frame,frame_ms)
		search_frames += 1
	print("MAP960_PATH_SEARCH_FRAMES ",search_frames," worst_ms=",worst_frame," active=",m.active)
	m.stop()
	m.position=Vector2(85*16+8,150*16+8)
	start = Time.get_ticks_usec()
	ok = m.request_target(Vector2(175*16+8,150*16+8),2)
	millis = float(Time.get_ticks_usec()-start)/1000.0
	print("MAP960_DIRECT_PATH_MS ",millis," requested=",ok," Astar=",m._full_path_searches)
	quit()
