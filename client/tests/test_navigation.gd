extends SceneTree

var failures := 0
const GRID = preload("res://scripts/collision_grid.gd")

func check(condition: bool, message: String) -> void:
	if not condition:
		failures += 1
		push_error(message)

func _initialize() -> void:
	var grid := GRID.new()
	grid.width = 3
	grid.height = 3
	grid.cells = PackedByteArray([0,1,0,0,1,0,0,1,0])
	check(not grid.allows(Vector2(8,24),Vector2(40,24)), "long segment cannot tunnel through wall")
	check(grid.slide(Vector2(8,24),Vector2(40,32)).x == 8, "blocked horizontal movement slides along wall")
	check(not grid.walkable(Vector2(-1,0)) and not grid.walkable(Vector2(48,0)), "outside map is blocked")
	check(grid.walkable(grid.nearest(Vector2(24,24))), "blocked spawn relocates to legal cell")
	grid.cells = PackedByteArray([0,1,0,1,0,0,0,0,0])
	check(not grid.allows(Vector2(8,8),Vector2(24,24)), "diagonal corner cutting rejected")
	grid.cells[4] = 3
	check(not grid.walkable(Vector2(24,24)), "special terrain remains blocked for normal character")
	var original := GRID.new()
	check(original.open("res://assets/maps/13/blocking.bin") == OK, "original Map13 collision loads")
	check(original.width == 960 and original.height == 960, "original dimensions preserved")
	check(original.walkable(Vector2(1200,800)), "actual spawn is legal")
	var terrain := preload("res://scripts/terrain_renderer.gd").new()
	check(terrain.open("res://assets",13) == OK, "terrain atlas/checksums load")
	terrain.update_view(Rect2(0,0,1440,900))
	check(terrain.multimesh.instance_count <= 1600, "only viewport terrain has GPU instances")
	terrain.update_view(Rect2(100000,100000,1440,900))
	check(terrain.multimesh.instance_count == 0, "out-of-world camera cannot allocate enormous instance buffer")
	terrain.free()
	print("navigation/map checks: %d failures" % failures)
	quit(1 if failures else 0)
