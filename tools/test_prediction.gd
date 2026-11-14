extends SceneTree
var failures := 0
const WORLD = preload("res://scripts/server_world.gd")
const PREDICT = preload("res://scripts/local_prediction.gd")
const CODEC = preload("res://scripts/input_codec.gd")
func check(ok: bool, message: String) -> void:
	if not ok:
		failures += 1
		push_error(message)
func _initialize() -> void:
	var world := WORLD.new()
	world.join(1)
	var model := PREDICT.new()
	model.configure(null,world.bounds)
	model.authority(0,0,1,world.players[1].p,100,0)
	var start: Vector2 = world.players[1].p
	for i in range(1,121):
		var motion := Vector2.RIGHT if i < 60 else Vector2.DOWN
		check(model.step_input(motion,i/30.0,i/30.0),"local input remains responsive")
		# Consecutive loss plus reversed/duplicate redundant inputs.
		if i%13 not in [0,1]:
			var frames := CODEC.decode(model.packet())
			frames.reverse()
			for frame in frames:
				world.queue_input(1,1,frame.sequence,frame.motion)
		world.step(1.0/30.0)
		if i%3 == 0:
			model.authority(world.tick,world.players[1].input_ack,1,world.players[1].p,100,i/30.0)
	check(world.players[1].p.distance_to(start) <= 230.0*4.0+0.01,"redundancy cannot create speed credit")
	for frame in CODEC.decode(model.packet()):
		world.queue_input(1,1,frame.sequence,frame.motion)
	for i in 3:
		world.step(1.0/30.0)
	model.authority(world.tick,world.players[1].input_ack,1,world.players[1].p,100,4.0)
	check(model.predicted.distance_to(world.players[1].p) < 0.01,"acknowledged input replay converges")
	check(model.max_pending <= 6,"packet loss keeps input queue bounded")
	check(not world.queue_input(1,0,121,Vector2.RIGHT),"previous-life input rejected")
	check(not world.queue_input(1,1,100000,Vector2.RIGHT),"future input window bounded")
	model.authority(124,0,2,Vector2(100,100),100,4.1)
	check(not model.authority(125,120,1,Vector2(200,200),100,4.2),"old life cannot override respawn")
	check(model.pending.is_empty() and model.predicted == Vector2(100,100),"respawn clears old prediction")
	check(not model.step_input(Vector2.RIGHT,5,5),"missing authority stops unbounded prediction")
	check(CODEC.decode(PackedByteArray([1,2,3])).is_empty(),"malformed input rejected")
	var grid := preload("res://scripts/collision_grid.gd").new()
	grid.width = 3
	grid.height = 3
	grid.cells = PackedByteArray([0,1,0,0,1,0,0,1,0])
	var wall_model := PREDICT.new()
	wall_model.configure(grid,Vector2(48,48))
	wall_model.authority(0,0,1,Vector2(8,24),100,0)
	for i in range(1,10):
		wall_model.step_input(Vector2.RIGHT,i/30.0,i/30.0)
		check(wall_model.predicted.x < 16,"local prediction uses authoritative walls")
		check(grid.walkable(wall_model.render(0.5,1.0/60.0)),"render correction stays on legal ground")
	var npcs := WORLD.new()
	npcs.populate(2,0)
	npcs.join(1)
	npcs.monsters[1].p = Vector2(1000,800)
	npcs.monsters[2].p = Vector2(1400,800)
	for i in 6:
		npcs.step(1.0/30.0)
	var a: Vector2 = npcs.monsters[1].p
	var b: Vector2 = npcs.monsters[2].p
	for i in 30:
		npcs.step(1.0/30.0)
		var next_a: Vector2 = npcs.monsters[1].p
		var next_b: Vector2 = npcs.monsters[2].p
		check(absf(next_a.distance_to(a)-65.0/30.0) < 0.001,"NPC integrates every tick between AI decisions")
		check(next_a.x > a.x and next_b.x < b.x,"NPC movement remains independent")
		a = next_a
		b = next_b
	check(npcs.monsters[1].action_time != npcs.monsters[2].action_time,"NPC walking phases differ")
	print("prediction/continuous motion checks: %d failures" % failures)
	quit(1 if failures else 0)
