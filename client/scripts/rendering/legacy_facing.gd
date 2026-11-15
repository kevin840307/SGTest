extends RefCounted

# Original / Recovered eight-direction convention in Godot screen coordinates:
# +X=6, +X+Y=7, +Y=8, -X+Y=1, -X=2, -X-Y=3, -Y=4, +X-Y=5.
const DIRECTIONS := [6,7,8,1,2,3,4,5]

static func valid(facing: int) -> bool:
	return facing >= 1 and facing <= 8

static func from_vector(vector: Vector2, current: int = 1, hold_degrees: float = 0.0) -> int:
	if vector.length_squared() <= 0.000001:
		return current if valid(current) else 1
	var angle := fposmod(vector.angle(),TAU)
	if hold_degrees > 0.0 and valid(current):
		var current_index := DIRECTIONS.find(current)
		if current_index >= 0:
			var center := float(current_index)*PI/4.0
			var difference := absf(wrapf(angle-center,-PI,PI))
			if rad_to_deg(difference) <= hold_degrees:
				return current
	var index := posmod(roundi(angle/(PI/4.0)),8)
	return DIRECTIONS[index]

static func vector(facing: int) -> Vector2:
	var index := DIRECTIONS.find(facing)
	return Vector2.from_angle(float(index if index >= 0 else 3)*PI/4.0)
