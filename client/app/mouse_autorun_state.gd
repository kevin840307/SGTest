extends RefCounted

# Verified against the user's private Recovered client on 2026-11-18.
# This contains input/repath policy only; pathfinding and movement stay Server-owned.
const HOLD_TO_LATCH_MSEC := 1000
const REPATH_INTERVAL_MSEC := 80
const REPATH_DISTANCE := 24.0
const REACH_DISTANCE := 10.0
const BLOCKED_RETRY_MSEC := 400

var latched := false
var holding := false
var _press_started_msec := -1
var _last_target := Vector2(INF,INF)
var _last_request_msec := -1
var _retry_not_before_msec := 0

func terrain_press(now_msec: int) -> bool:
	var was_latched := latched
	latched = false
	holding = not was_latched
	_press_started_msec = now_msec if holding else -1
	_reset_pacing()
	return was_latched

func poll_long_hold(now_msec: int) -> bool:
	if latched or not holding or _press_started_msec < 0:
		return false
	if now_msec - _press_started_msec < HOLD_TO_LATCH_MSEC:
		return false
	latched = true
	return true

func terrain_release(now_msec: int) -> bool:
	if holding and _press_started_msec >= 0 and now_msec - _press_started_msec >= HOLD_TO_LATCH_MSEC:
		latched = true
	holding = false
	_press_started_msec = -1
	return latched

func active() -> bool:
	return holding or latched

func should_request(now_msec: int,actor_position: Vector2,
		cursor_position: Vector2,is_moving: bool) -> bool:
	if not active() or not actor_position.is_finite() or not cursor_position.is_finite():
		return false
	if actor_position.distance_to(cursor_position) <= REACH_DISTANCE:
		return false
	if is_moving:
		if _last_target.is_finite() and _last_target.distance_to(cursor_position) < REPATH_DISTANCE:
			return false
		return _last_request_msec < 0 or now_msec - _last_request_msec >= REPATH_INTERVAL_MSEC
	# While physically held, a stopped route follows immediately. Once latched,
	# rejected/stopped navigation is rate-limited to avoid A* retry storms.
	return not latched or now_msec >= _retry_not_before_msec

func note_request(target: Vector2,now_msec: int,was_stopped: bool) -> void:
	_last_target = target
	_last_request_msec = now_msec
	if latched and was_stopped:
		_retry_not_before_msec = now_msec + BLOCKED_RETRY_MSEC

func defer_blocked_retry(now_msec: int) -> void:
	if latched:
		_retry_not_before_msec = maxi(
			_retry_not_before_msec,now_msec+BLOCKED_RETRY_MSEC)

func cancel() -> void:
	latched = false
	holding = false
	_press_started_msec = -1
	_reset_pacing()

func snapshot() -> Dictionary:
	return {
		"latched":latched,"holding":holding,
		"last_target":_last_target,
		"last_request_msec":_last_request_msec,
		"retry_not_before_msec":_retry_not_before_msec
	}

func _reset_pacing() -> void:
	_last_target = Vector2(INF,INF)
	_last_request_msec = -1
	_retry_not_before_msec = 0
