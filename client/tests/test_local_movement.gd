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
	# Regression: MoveReceipt can arrive BEFORE the matching SelfState.
	# An accepted receipt with an old 'idle' SelfState must not stop the
	# client after the first 4px step (it would then teleport with AOI updates).
	var ack_gap := LocalMovement.new()
	check(ack_gap.configure(grid(12,12),manifest,4,10),"ACK gap grid invalid")
	ack_gap.observe(initial(10,4,Vector2(24,24)))
	check(ack_gap.request_target(Vector2(152,24),1),"ACK gap route not started")
	ack_gap.advance(1.0/60.0)
	var first_step := ack_gap.position
	var receipt_before_self := initial(10,4,Vector2(24,24),2)
	receipt_before_self.last_receipt_sequence=1
	receipt_before_self.last_receipt_status=0
	# movement_action_sequence=0 and moving=false: the OLD state.
	ack_gap.observe(receipt_before_self)
	check(ack_gap.active and ack_gap.moving and ack_gap.position==first_step,
		"accepted receipt with old idle SelfState interrupted walk immediately")
	for frame in 20:
		var prior := ack_gap.position
		ack_gap.advance(1.0/60.0)
		check(ack_gap.position.distance_to(prior)<=4.001 and ack_gap.position.x>=prior.x,
			"local predicted motion jumped after accepted receipt")
	check(ack_gap.position.x>first_step.x+70.0,
		"ACK-only transition froze local walking; server would teleport ahead")
	# Held-click same destination can consume a newer wire sequence without
	# changing Server MovementActionSequence. Keep the original active action.
	check(ack_gap.request_target(Vector2(152,24),2),"repeated target rejected")
	check(ack_gap._expected_sequence==1,
		"repeat-target receipt incorrectly changed the active action sequence")
	var duplicate_ack := initial(10,4,Vector2(92,24),25)
	duplicate_ack.self_state.moving = true
	duplicate_ack.self_state.movement_action_sequence = 1
	duplicate_ack.last_receipt_sequence = 2
	duplicate_ack.last_receipt_status = 0
	var before_duplicate := ack_gap.position
	ack_gap.observe(duplicate_ack)
	check(ack_gap.active and ack_gap.moving and ack_gap.position==before_duplicate,
		"duplicate ACK broke the original moving route")
	# Recovered correction semantics: a REAL divergence cancels the local route
	# and adopts the authoritative anchor immediately. It must never create a
	# second 480px/s visual "catch-up walk" that looks like sliding.
	var wide_manifest := {"collision_cell":16,"size":[640,192]}
	var correction := LocalMovement.new()
	check(correction.configure(grid(40,12),wide_manifest,5,27),"correction map invalid")
	correction.observe(initial(27,5,Vector2(24,24)))
	check(correction.request_target(Vector2(552,24),1),"long route rejected")
	for frame in 16:
		correction.advance(1.0/60.0)
	var late_server := initial(27,5,Vector2(300,24),30)
	late_server.self_state.moving=true
	late_server.self_state.movement_action_sequence=1
	late_server.last_receipt_sequence=1
	late_server.last_receipt_status=0
	correction.observe(late_server)
	check(not correction.active and not correction.moving and \
		correction.position==Vector2(300,24) and correction.visual_position==Vector2(300,24) and \
		correction.last_stop_reason=="moving_divergence",
		"large Server divergence created a fake catch-up route instead of Recovered correction")

	# Once the Server has actually shown this action moving, an authoritative
	# stop is final. Recovered anchors immediately; no idle/sliding transition.
	var stop_correction := LocalMovement.new()
	check(stop_correction.configure(grid(40,12),wide_manifest,6,28),"stop rollback map invalid")
	stop_correction.observe(initial(28,6,Vector2(24,24)))
	check(stop_correction.request_target(Vector2(552,24),1),"stop rollback route rejected")
	for frame in 36:
		stop_correction.advance(1.0/60.0)
	var moving_state := initial(28,6,Vector2(160,24),59)
	moving_state.self_state.moving=true
	moving_state.self_state.movement_action_sequence=1
	moving_state.last_receipt_sequence=1
	moving_state.last_receipt_status=0
	stop_correction.observe(moving_state)
	var remote_stop := initial(28,6,Vector2(24,24),60)
	remote_stop.self_state.moving=false
	remote_stop.self_state.movement_action_sequence=1
	remote_stop.last_receipt_sequence=1
	remote_stop.last_receipt_status=0
	stop_correction.observe(remote_stop)
	check(not stop_correction.active and not stop_correction.moving and \
		stop_correction.position==Vector2(24,24) and stop_correction.visual_position==Vector2(24,24) and \
		stop_correction.last_stop_reason=="server_stop",
		"authoritative stop created a presentation slide instead of a final anchor")
	# Long-hold retargets must not stack routes while local prediction is far
	# ahead of the last Server position. Recovered repaths from local position;
	# SGTest additionally gates that repath because the Server is authoritative.
	var hold_gate := LocalMovement.new()
	check(hold_gate.configure(grid(40,12),wide_manifest,7,29),"hold gate map invalid")
	hold_gate.observe(initial(29,7,Vector2(24,24)))
	check(hold_gate.request_target(Vector2(552,24),1),"hold gate route rejected")
	for frame in 9:
		hold_gate.advance(1.0/60.0)
	check(hold_gate.authority_error()>32.0 and not hold_gate.retarget_safe(32.0),
		"long-hold allowed a new A-star while prediction was >32px ahead")
	var caught_up := initial(29,7,Vector2(44,24),20)
	caught_up.self_state.moving=true
	caught_up.self_state.movement_action_sequence=1
	caught_up.last_receipt_sequence=1
	caught_up.last_receipt_status=0
	hold_gate.observe(caught_up)
	check(hold_gate.authority_error()<=32.0 and hold_gate.retarget_safe(32.0),
		"long-hold stayed blocked after Server caught up")
	# Sustained 60fps local walk while Server sends old-but-accepted positions.
	# These packets used to repeatedly lerp backwards, causing 4px/8px
	# alternation (stutter) and the visual slipping between walk sprites.
	var continuous := LocalMovement.new()
	check(continuous.configure(grid(12,12),manifest,1,7),"continuous map invalid")
	continuous.observe(initial(7,1,Vector2(24,24)))
	check(continuous.request_target(Vector2(152,24),1),"straight route not started")
	check(continuous._full_path_searches == 1 and continuous._search.is_empty() and \
		continuous._route.size()==1,
		"Recovered direct-clear path did not finish before movement")
	var preceding := continuous.position
	for frame in 18:
		continuous.advance(1.0/60.0)
		var current := continuous.position
		check(is_equal_approx(current.distance_to(preceding),4.0),
			"server stutter: predicted frame travel wasn't stable at 60 FPS frame %d" % frame)
		preceding = current
		if frame%3==2:
			var delayed := initial(7,1,Vector2(24+maxi(0,frame-7)*4,24),frame+2)
			delayed.self_state.moving = true
			delayed.self_state.movement_action_sequence = 1
			delayed.last_receipt_sequence = 1
			delayed.last_receipt_status = 0
			continuous.observe(delayed)
			check(continuous.position == current,"moving SelfState rewound the local view")
	# After this action has been observed moving, Server stop is authoritative.
	# Do not synthesize a second slow route to the older position.
	var server_stop := initial(7,1,Vector2(72,24),30)
	server_stop.self_state.moving = false
	server_stop.self_state.movement_action_sequence = 1
	server_stop.last_receipt_sequence = 1
	server_stop.last_receipt_status = 0
	continuous.observe(server_stop)
	check(not continuous.active and not continuous.moving and \
		continuous.position == Vector2(72,24) and continuous.visual_position == Vector2(72,24) and \
		continuous.last_stop_reason=="server_stop",
		"Server stop produced a fake settling walk")
	# A fresh command after one completed stop cannot inherit the prior
	# settling target or a stale route.
	check(continuous.request_target(Vector2(152,24),2),"new move after stop rejected")
	check(continuous.moving and continuous.active,
		"new click did not restart a clean Recovered route after Server stop")
	var walls: Array[Vector2i] = []
	for y in range(10):
		walls.append(Vector2i(5,y))
	walls.erase(Vector2i(5,8))
	var around := LocalMovement.new()
	check(around.configure(grid(12,12,walls),manifest,2,8),"wall map unavailable")
	around.observe(initial(8,2,Vector2(40,40)))
	check(around.request_target(Vector2(152,40),1),"wall detour A-star path not found")
	check(around._search.is_empty() and around.moving and around._route.size()>1,
		"A-star did not finish/smooth the complete obstacle route before walking")
	var pass_wall:=false
	for i in range(240):
		var pending_before := not around._search.is_empty()
		var before_frame := around.position
		around.advance(1.0/60.0)
		if pending_before and not around._search.is_empty():
			check(around.position == before_frame and not around.moving and around._route.is_empty(),
				"actor walked a partial A-star path before the final goal was found")
		if around.moving:
			check(around._search.is_empty() and around._route.size()>1,
				"actor started walking without a complete route")
		check(around._walkable(around._cell(around.position)),"predicted actor entered blocked grid")
		if around.position.x>88.0:
			pass_wall=true
	check(pass_wall and around.position.distance_to(Vector2(152,40))<2.0,
		"detour path did not reach destination")
	var corner := LocalMovement.new()
	check(corner.configure(grid(12,12,[Vector2i(1,0),Vector2i(0,1)]),manifest,3,9),"corner grid invalid")
	corner.observe(initial(9,3,Vector2(8,8)))
	var corner_result := corner.request_target(Vector2(24,24),1)
	corner.advance(1.0/60.0)
	check((not corner_result or not corner.moving) and not corner.active and corner.position==Vector2(8,8),
		"A-star crossed a blocked diagonal corner")
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
