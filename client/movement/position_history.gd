extends RefCounted
## Bounded presentation-only snapshot interpolation. Authority remains in WorldState.
const MAX_SAMPLES := 8
const SERVER_TICK_USEC := 1000000.0 / 30.0
const DEFAULT_DELAY_TICKS := 2.0
const MAX_BRIDGE_TICKS := 8
const MAX_BRIDGE_PIXELS_PER_TICK := 64.0

var _samples: Array[Dictionary] = []
var _last_target_tick := -INF

func clear() -> void:
	_samples.clear()
	_last_target_tick = -INF

func size() -> int:
	return _samples.size()

func push(server_tick: int, position: Vector2, arrival_usec: int) -> bool:
	if server_tick < 0 or arrival_usec < 0 or not position.is_finite():
		return false
	var next := {"tick":server_tick,"p":position,"arrival":arrival_usec}
	if _samples.is_empty():
		_samples.append(next)
		return true
	var previous: Dictionary = _samples[-1]
	var previous_tick := int(previous.tick)
	if server_tick < previous_tick:
		return false
	if server_tick == previous_tick:
		if Vector2(previous.p) != position:
			_samples[-1] = next
		return true
	var gap := server_tick - previous_tick
	var distance := Vector2(previous.p).distance_to(position)
	if gap > MAX_BRIDGE_TICKS or distance > MAX_BRIDGE_PIXELS_PER_TICK * float(gap):
		_samples = [next]
		_last_target_tick = -INF
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
	var elapsed := float(maxi(0,now_usec-int(latest.arrival))) / SERVER_TICK_USEC
	var candidate := minf(float(latest.tick),float(latest.tick)+elapsed-delay_ticks)
	if is_inf(_last_target_tick):
		_last_target_tick = maxf(float(_samples[0].tick),candidate)
	else:
		# A newly-arrived packet may have more jitter than the prior one. Never
		# rewind presentation time; hold until the requested buffer refills.
		_last_target_tick = maxf(_last_target_tick,candidate)
	var target := minf(_last_target_tick,float(latest.tick))
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
