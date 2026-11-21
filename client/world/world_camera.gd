extends RefCounted
## Presentation only: integer canvas origin, fractional pan accumulation, no clock.
var _epoch := 0
var _world := Vector2i.ZERO
var _size := Vector2i.ZERO
var _focus := Vector2.ZERO
var _origin := Vector2i.ZERO
var _active := false


func configure(epoch: int, world_size: Vector2i, viewport_size: Vector2i, focus: Vector2) -> Error:
	if epoch <= _epoch or world_size.x <= 0 or world_size.y <= 0 or not _valid_size(viewport_size) or not focus.is_finite():
		return ERR_INVALID_PARAMETER
	_epoch = epoch
	_world = world_size
	_size = viewport_size
	_focus = focus
	_active = true
	_clamp()
	return OK


func clear() -> void:
	_active = false # Keep epoch tombstone across cancelled map loads.
	_world = Vector2i.ZERO
	_size = Vector2i.ZERO
	_focus = Vector2.ZERO
	_origin = Vector2i.ZERO


func set_focus(epoch: int, focus: Vector2) -> Error:
	if not _active or epoch != _epoch or not focus.is_finite():
		return ERR_INVALID_PARAMETER
	_focus = focus
	_clamp()
	return OK


func pan(epoch: int, world_delta: Vector2) -> Error:
	return set_focus(epoch, _focus + world_delta)


func resize(epoch: int, viewport_size: Vector2i) -> Error:
	if not _active or epoch != _epoch or not _valid_size(viewport_size):
		return ERR_INVALID_PARAMETER
	_size = viewport_size
	_clamp()
	return OK


func view_rect() -> Rect2:
	return Rect2(Vector2(_origin),Vector2(_size)) if _active else Rect2()


func world_to_screen(point: Vector2) -> Vector2:
	return point - Vector2(_origin)


func screen_to_world(point: Vector2) -> Vector2:
	return point + Vector2(_origin)


func snapshot() -> Dictionary:
	return {"active":_active,"epoch":_epoch,"world_size":_world,"size":_size,"focus":_focus,"origin":_origin}


static func _valid_size(size: Vector2i) -> bool:
	return size.x > 0 and size.y > 0 and size.x <= 16384 and size.y <= 16384


func _clamp() -> void:
	for axis in 2:
		var half := float(_size[axis]) / 2.0
		if _world[axis] <= _size[axis]:
			_focus[axis] = float(_world[axis]) / 2.0
		else:
			_focus[axis] = clampf(_focus[axis], half, float(_world[axis])-half)
	_origin = Vector2i((_focus-Vector2(_size)/2.0).floor())
