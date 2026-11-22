extends RefCounted
## Bounded presentation-only snapshot interpolation. Authority remains in WorldState.
const MAX_SAMPLES := 8
const SERVER_TICK_USEC := 1000000.0 / 30.0
const DEFAULT_DELAY_TICKS := 2.5 # 83.3ms: absorbs 20Hz UI polls of 30Hz authoritative ticks.
const MAX_BRIDGE_TICKS := 8
const MAX_BRIDGE_PIXELS_PER_TICK := 64.0
# Visual-only short dead reckoning. Never changes WorldState or move/attack wire.
const MAX_PREDICTION_TICKS := 1.5
const MAX_PREDICTION_PIXELS := 12.0
const MAX_PREDICTED_SPEED_PER_TICK := 8.0

var _samples: Array[Dictionary] = []
var _last_target_tick := -INF
var _clock_tick := 0
var _clock_usec := 0

func clear() -> void:
	_samples.clear()
	_last_target_tick = -INF
	_clock_tick = 0
	_clock_usec = 0

func size() -> int:
	return _samples.size()

func push(server_tick: int, position: Vector2, arrival_usec: int,
		is_moving: bool = false) -> bool:
	if server_tick < 0 or arrival_usec < 0 or not position.is_finite():
		return false
	var next := {"tick":server_tick,"p":position,"arrival":arrival_usec,
		"moving":is_moving}
	if _samples.is_empty():
		_samples.append(next)
		_clock_tick = server_tick
		_clock_usec = arrival_usec
		return true
	var previous: Dictionary = _samples[-1]
	var previous_tick := int(previous.tick)
	if server_tick < previous_tick:
		return false
	if server_tick == previous_tick:
		if Vector2(previous.p) != position or bool(previous.get("moving",false)) != is_moving:
			_samples[-1] = next
		return true
	var gap := server_tick - previous_tick
	var distance := Vector2(previous.p).distance_to(position)
	if gap > MAX_BRIDGE_TICKS or distance > MAX_BRIDGE_PIXELS_PER_TICK * float(gap):
		_samples = [next]
		_last_target_tick = -INF
		_clock_tick = server_tick
		_clock_usec = arrival_usec
		return true
	_samples.append(next)
	while _samples.size() > MAX_SAMPLES:
		_samples.pop_front()
	return true

func sample(now_usec: int, delay_ticks := DEFAULT_DELAY_TICKS) -> Vector2:
	if _samples.is_empty() or now_usec < 0 or delay_ticks < 0.0 or delay_ticks > 8.0:
		return Vector2.INF
	if _samples.size() == 1:
		if is_inf(_last_target_tick):
			_last_target_tick = float(_samples[0].tick)
		return Vector2(_samples[0].p)
	var latest: Dictionary = _samples[-1]
	# UI can publish at 20 Hz while the Server runs at 30 Hz. Anchoring the
	# render clock to each *new* packet's arrival creates a sawtooth: each
	# update rewinds its desired time by delay_ticks, making the camera hold.
	# Use one continuous local time origin per entity generation instead.
	var elapsed := float(maxi(0,now_usec-_clock_usec)) / SERVER_TICK_USEC
	var candidate := float(_clock_tick)+elapsed-delay_ticks
	if is_inf(_last_target_tick):
		_last_target_tick = maxf(float(_samples[0].tick),candidate)
	else:
		_last_target_tick = maxf(_last_target_tick,candidate)
	# Recovered keeps drawing locally between packets, then accepts delayed
	# authoritative corrections. Never run indefinitely on an old velocity:
	# only a confirmed moving actor may lead by <=1.5 server ticks / 12 px.
	var prediction_limit := MAX_PREDICTION_TICKS if bool(latest.get("moving",false)) else 0.0
	_last_target_tick = minf(_last_target_tick,float(latest.tick)+prediction_limit)
	var target := _last_target_tick
	if target > float(latest.tick):
		var prior: Dictionary = _samples[-2]
		var interval := float(int(latest.tick)-int(prior.tick))
		if interval <= 0.0:
			return Vector2(latest.p)
		var step := (Vector2(latest.p)-Vector2(prior.p))/interval
		step = step.limit_length(MAX_PREDICTED_SPEED_PER_TICK)
		return Vector2(latest.p)+(
			step*(target-float(latest.tick))).limit_length(MAX_PREDICTION_PIXELS)
	if target <= float(_samples[0].tick):
		return Vector2(_samples[0].p)
	for i in range(1,_samples.size()):
		var right: Dictionary = _samples[i]
		if target > float(right.tick):
			continue
		var left: Dictionary = _samples[i-1]
		var span := float(int(right.tick)-int(left.tick))
		if span <= 0.0:
			return Vector2(right.p)
		var alpha := clampf((target-float(left.tick))/span,0.0,1.0)
		return Vector2(left.p).lerp(Vector2(right.p),alpha)
	return Vector2(latest.p)

func target_tick() -> float:
	return _last_target_tick
