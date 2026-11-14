class_name SpatialGrid
extends RefCounted

const CELL := 256.0
var cells: Dictionary = {}

func rebuild(entities: Dictionary) -> void:
	cells.clear()
	for id in entities:
		var p: Vector2 = entities[id].p
		var key := Vector2i(floori(p.x / CELL), floori(p.y / CELL))
		if not cells.has(key):
			cells[key] = []
		cells[key].append(id)

func query(center: Vector2, radius: float) -> Array:
	var result: Array = []
	var start := Vector2i(floori((center.x-radius) / CELL), floori((center.y-radius) / CELL))
	var end := Vector2i(floori((center.x+radius) / CELL), floori((center.y+radius) / CELL))
	for y in range(start.y, end.y+1):
		for x in range(start.x, end.x+1):
			result.append_array(cells.get(Vector2i(x, y), []))
	return result
