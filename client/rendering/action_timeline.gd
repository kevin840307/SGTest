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


func is_complete(elapsed: int) -> bool:
	return _total > 0 and not _loops and elapsed >= _total
