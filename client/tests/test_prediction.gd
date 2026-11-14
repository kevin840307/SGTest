extends SceneTree
func _initialize() -> void:
	var model := preload("res://scripts/local_prediction.gd").new()
	model.configure(null,Vector2(2400,1600))
	assert(model.authority(0,0,1,Vector2(100,100),100,0))
	assert(model.step_input(Vector2.RIGHT,0.03,0.03))
	assert(model.predicted.x > 100)
	model.authority(1,1,1,Vector2(107.66667,100),100,.04)
	assert(model.pending.is_empty())
	model.authority(2,0,2,Vector2(200,200),100,.05)
	assert(not model.authority(3,1,1,Vector2.ZERO,100,.06))
	assert(model.predicted == Vector2(200,200))
	assert(not model.step_input(Vector2.RIGHT,1,1))
	var grid := preload("res://scripts/collision_grid.gd").new()
	grid.width=3
	grid.height=3
	grid.cells=PackedByteArray([0,1,0,0,1,0,0,1,0])
	model.configure(grid,Vector2(48,48))
	model.authority(0,0,1,Vector2(8,24),100,0)
	for i in range(1,10):
		model.step_input(Vector2.RIGHT,i/30.0,i/30.0)
		assert(model.predicted.x < 16 and grid.walkable(model.render(.5,1.0/60)))
	print("client prediction checks passed")
	quit()
