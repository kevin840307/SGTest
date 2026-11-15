class_name DropRenderer
extends MultiMeshInstance2D

# One instanced draw for ground items; rebuild only when reliable deltas arrive.
func rebuild(drops: Dictionary) -> void:
	if multimesh == null:
		multimesh = MultiMesh.new()
		multimesh.transform_format = MultiMesh.TRANSFORM_2D
		var quad := QuadMesh.new()
		quad.size = Vector2(10, 10)
		multimesh.mesh = quad
		modulate = Color.GOLD
	multimesh.instance_count = drops.size()
	var index := 0
	for id in drops:
		multimesh.set_instance_transform_2d(index, Transform2D(0, drops[id].p))
		index += 1
