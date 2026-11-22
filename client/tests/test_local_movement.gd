extends SceneTree
const LocalMovement = preload("res://movement/local_movement.gd")
var failures: Array[String] = []

func check(condition: bool, message: String) -> void:
	if not condition:
		failures.append(message)
		push_error(message)

func grid(w: int,h: int, walls: Array[Vector2i] = []) -> PackedByteArray:
	var bytes := PackedByteArray()
	bytes.resize(8+w*h)
	bytes.encode_u32(0,w)
	bytes.encode_u32(4,h)
	for wall in walls:
		bytes[8+wall.y*w+wall.x] = 7
	return bytes

func initial(actor: int,epoch: int,p: Vector2,tick: int = 1) -> Dictionary:
	return {"world_ready":true,"map_epoch":epoch,"actor_id":actor,"last_receipt_sequence":0,
		"last_receipt_status":-1,"self_state":{
			"x":p.x,"y":p.y,"facing":6,"connection_epoch":1,
			"movement_action_sequence":0,"moving":false,"server_tick":tick}}

func _initialize() -> void:
	run.call_deferred()

func run() -> void:
	var manifest := {"collision_cell":16,"size":[192,192]}
	var predictor := LocalMovement.new()
	check(not predictor.configure(PackedByteArray(),manifest,1,7),"invalid grid accepted")
	check(predictor.configure(grid(12,12),manifest,1,7),"grid rejected")
	var ready_only := initial(7,1,Vector2(24,24))
	ready_only.erase("self_state")
	ready_only["x"] = 24.0
	ready_only["y"] = 24.0
	ready_only["facing"] = 6
	predictor.observe(ready_only)
	check(predictor.position==Vector2(24,24),"WorldReady-only spawn was not initialized")
	var baseline := initial(7,1,Vector2(24,24))
	predictor.observe(baseline)
	check(predictor.position==Vector2(24,24),"initial authority absent")
	check(predictor.request_target(Vector2(152,24),1),"local route not accepted")
	predictor.advance(1.0/60.0)
	check(predictor.position.x>24.0 and predictor.position.x<=28.01 and predictor.facing==6,
		"click did not move locally before server receipt")
	var arrived := predictor.position.x
	for _frame in range(26):
		predictor.advance(1.0/60.0)
	check(predictor.position.x>arrived+100.0,"missing server packets froze local movement")
	check(predictor.position.x<=152.01,"local movement overshot goal")
	var before_retry := predictor.position
	# Server is still processing intent 1. Its older SelfState (even >96 px
	# behind due latency) must not overwrite our already moving visual position.
	predictor.observe(initial(7,1,Vector2(24,24),2))
	check(predictor.position == before_retry,"old pre-ACK SelfState rubber-banded player")
	check(predictor.request_target(Vector2(152,24),2),"same-cell route repath failed")
	check(predictor.position==before_retry,"same-cell route repath teleported")
	var rejected := initial(7,1,Vector2(56,24),3)
	rejected.last_receipt_sequence=2
	rejected.last_receipt_status=3
	predictor.observe(rejected)
	check(not predictor.active and predictor.position==Vector2(56,24),
		"denied move did not restore Server position")
	var walls: Array[Vector2i] = []
	for y in range(10):
		walls.append(Vector2i(5,y))
	walls.erase(Vector2i(5,8))
	var around := LocalMovement.new()
	check(around.configure(grid(12,12,walls),manifest,2,8),"wall map unavailable")
	around.observe(initial(8,2,Vector2(40,40)))
	check(around.request_target(Vector2(152,40),1),"wall detour A-star path not found")
	var pass_wall:=false
	for i in range(240):
		around.advance(1.0/60.0)
		check(around._walkable(around._cell(around.position)),"predicted actor entered blocked grid")
		if around.position.x>88.0:
			pass_wall=true
	check(pass_wall and around.position.distance_to(Vector2(152,40))<2.0,
		"detour path did not reach destination")
	var corner := LocalMovement.new()
	check(corner.configure(grid(12,12,[Vector2i(1,0),Vector2i(0,1)]),manifest,3,9),"corner grid invalid")
	corner.observe(initial(9,3,Vector2(8,8)))
	check(not corner.request_target(Vector2(24,24),1),"A-star crossed a blocked diagonal corner")
	var stale := around.position
	around.observe(initial(8,1,Vector2(24,24),999))
	check(around.position==stale,"stale MapEpoch moved actor")
	var facing := LocalMovement.new()
	check(facing._face(Vector2(100,42),6)==6 and
		facing._face(Vector2(100,40),6)==6 and
		facing._face(Vector2(-100,0),6)==2,
		"8-way facing hysteresis did not suppress edge flutter")
	var malformed := grid(12,12)
	malformed.resize(malformed.size()-1)
	check(not corner.configure(malformed,manifest,3,9),"truncated Blocking.dat accepted")
	if failures.is_empty():
		print("LOCAL_MOVEMENT_PASS no_packet_progress=true path_detour=true corner_block=true deny_rollback=true epoch_gate=true facing_hysteresis=true")
	else:
		print("LOCAL_MOVEMENT_FAIL ",failures)
	quit(0 if failures.is_empty() else 1)
