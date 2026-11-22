extends SceneTree
const LocalMovement = preload("res://movement/local_movement.gd")
var failures: Array[String] = []

func check(ok: bool, message: String) -> void:
	if not ok:
		failures.append(message)
		push_error("RECOVERED_OBSTACLE_FAIL "+message)

func grid(w: int,h: int,walls: Array[Vector2i]) -> PackedByteArray:
	var bytes:=PackedByteArray()
	bytes.resize(8+w*h)
	bytes.encode_u32(0,w)
	bytes.encode_u32(4,h)
	for wall in walls:
		bytes[8+wall.y*w+wall.x]=1
	return bytes

func snapshot(actor: int,epoch: int,p: Vector2,tick: int=1) -> Dictionary:
	return {
		"world_ready":true,"map_epoch":epoch,"actor_id":actor,
		"x":p.x,"y":p.y,"facing":6,
		"self_state":{
			"actor_id":actor,"x":p.x,"y":p.y,"facing":6,
			"moving":false,"server_tick":tick,"movement_action_sequence":0
		},
		"last_receipt_sequence":0,"last_receipt_status":0
	}

func _initialize() -> void:
	var walls: Array[Vector2i] = []
	# Vertical wall forces a visible detour. Start/goal are on opposite sides.
	for y in range(3,10):
		walls.append(Vector2i(7,y))
	var mover:=LocalMovement.new()
	var manifest={"collision_cell":16,"size":[16*18,16*14]}
	check(mover.configure(grid(18,14,walls),manifest,1,77),"configure")
	var start:=Vector2(72,104) # cell 4,6 center-ish
	var goal:=Vector2(200,104) # cell 12,6
	mover.observe(snapshot(77,1,start))
	var before_usec:=Time.get_ticks_usec()
	check(mover.request_target(goal,1),"route rejected")
	var route_usec:=Time.get_ticks_usec()-before_usec
	check(mover.moving and mover.active,"A* returned before complete route was ready")
	check(mover._search.is_empty(),"A* leaked incomplete search across render frames")
	check(mover._route.size()>=2,"obstacle path was not a detour")
	var route_waypoints:=mover._route.size()
	var previous:=mover.position
	var moving_frames:=0
	var zero_frames:=0
	var max_step:=0.0
	var crossed_wall:=false
	for frame in range(240):
		if not mover.active:
			break
		mover.advance(1.0/60.0)
		mover.advance_visual(1.0/60.0)
		var travel:=mover.position.distance_to(previous)
		max_step=maxf(max_step,travel)
		if mover.moving:
			moving_frames+=1
			if travel<0.01:
				zero_frames+=1
		check(travel<=4.01 or not mover.active,
			"catch-up/slide step %.2f at frame %d" % [travel,frame])
		check(mover.visual_position.distance_to(mover.position)<0.01,
			"visual follower detached from route at frame %d" % frame)
		check(mover._walkable(mover._cell(mover.position)),
			"entered blocked cell at frame %d" % frame)
		if mover.position.x>128:
			crossed_wall=true
		previous=mover.position
	check(crossed_wall,"never passed obstacle")
	check(mover.position.distance_to(Vector2(200,104))<=12.0,"did not reach goal side")
	check(zero_frames==0,"movement paused after full A* and later caught up")
	check(max_step<=4.01,"movement exceeded fixed 240px/s 60FPS step")
	if failures.is_empty():
		print("RECOVERED_OBSTACLE_PASS route_usec=",route_usec,
			" waypoints=",route_waypoints," moving_frames=",moving_frames,
			" zero_frames=",zero_frames," max_step=",snappedf(max_step,0.01))
	quit(0 if failures.is_empty() else 1)
