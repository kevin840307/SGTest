extends RefCounted
## Default ACT playback at explicit elapsed fixed updates; no clocks or events.
## SetSpeed during playback is deliberately outside this immutable sampler.
var _ends := PackedInt64Array()
var _frames := PackedInt32Array()
var _loops := false
var _total := 0


func configure(definition: Dictionary) -> Error:
	_ends.clear()
	_frames.clear()
	_total = 0
	if definition.get("schema") != "sgtest.action_timeline/v1" or definition.get("fixed_hz") != 50 or definition.get("default_speed") != 25 or definition.get("numerator") != 40:
		return ERR_INVALID_DATA
	var frames = definition.get("frames", [])
	if not frames is Array or frames.is_empty() or frames.size() > 65536 or not definition.get("loops") is bool:
		return ERR_INVALID_DATA
	var ends := PackedInt64Array()
	var indices := PackedInt32Array()
	var total := 0
	for frame in frames:
		if not frame is Dictionary or not frame.has_all(["index","raw_time"]):
			return ERR_INVALID_DATA
		var index := int(frame.index)
		var raw := int(frame.raw_time)
		if float(frame.index) != index or float(frame.raw_time) != raw or index < 0 or index > 2147483647 or raw < 0 or raw > 4294967295:
			return ERR_INVALID_DATA
		total += maxi(1, raw * 40 / 25)
		ends.append(total)
		indices.append(index)
	_ends = ends
	_frames = indices
	_total = total
	_loops = definition.loops
	return OK


func configure_ticks(steps: Array, loops: bool) -> Error:
	# Converter logical_views already contain normalized fixed-50Hz durations.
	_ends.clear()
	_frames.clear()
	_total = 0
	if steps.is_empty() or steps.size() > 65536:
		return ERR_INVALID_DATA
	var ends := PackedInt64Array()
	var frames := PackedInt32Array()
	var total := 0
	for step in steps:
		if not step is Array or step.size() != 2:
			return ERR_INVALID_DATA
		var index := int(step[0])
		var ticks := int(step[1])
		if float(step[0]) != index or float(step[1]) != ticks or index < 0 or index > 2147483647 or ticks < 1 or ticks > 6871947672:
			return ERR_INVALID_DATA
		total += ticks
		ends.append(total)
		frames.append(index)
	_ends = ends
	_frames = frames
	_total = total
	_loops = loops
	return OK


func frame_at_update(elapsed: int) -> int:
	if _total == 0:
		return -1
	var tick := maxi(0,elapsed)
	if _loops:
		tick %= _total
	elif tick >= _total:
		return _frames[-1]
	var low := 0
	var high := _ends.size()
	while low < high:
		var middle := (low + high) / 2
		if tick < _ends[middle]:
			high = middle
		else:
			low = middle + 1
	return _frames[low]


## Recovered2's AnimatedSprite2D uses a uniform 8 FPS paper-doll walk
## cadence, not variable ACT raw_time durations. Keep the source frame order,
## but share one continuous elapsed clock across all paper-doll layers.
func frame_at_uniform_cadence(elapsed_usec: int, frame_duration_usec: int) -> int:
	if _frames.is_empty() or frame_duration_usec <= 0:
		return -1
	var index := maxi(0,elapsed_usec) / frame_duration_usec
	if _loops:
		index %= _frames.size()
	else:
		index = mini(index,_frames.size()-1)
	return _frames[index]


func is_complete(elapsed: int) -> bool:
	return _total > 0 and not _loops and elapsed >= _total
