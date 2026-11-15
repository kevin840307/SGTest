extends RefCounted

const SPEED := 230.0
const STEP := 1.0/30.0

static func move_to(from: Vector2, to: Vector2, bounds: Vector2, grid: RefCounted) -> Vector2:
	var target := to.clamp(Vector2.ZERO,bounds-Vector2.ONE*0.001)
	return target if grid == null else grid.slide(from,target)

static func step(from: Vector2, motion: Vector2, delta: float, bounds: Vector2, grid: RefCounted) -> Vector2:
	return move_to(from,from+motion*SPEED*delta,bounds,grid)
