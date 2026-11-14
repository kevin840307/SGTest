extends RefCounted

# Monotonic presentation time. Snapshot reordering cannot rewind animations.
# Ping/pong compensates for transport age; correction is slewed, never stepped
# backwards. Pure inputs make jitter behavior testable without a live socket.
var time := 0.0
var initialized := false
var offset := 0.0
var previous_local := 0.0
var last_snapshot := -1.0
var rtt := 0.0
var samples: Array = []

func observe_snapshot(stamp: float, local: float) -> void:
	if not is_finite(stamp) or stamp < last_snapshot:
		return
	last_snapshot = stamp
	if not initialized:
		time = stamp
		offset = stamp-local
		previous_local = local
		initialized = true
	elif samples.is_empty():
		offset = maxf(offset,stamp-local)

func observe_pong(sent: float, received: float, stamp: float) -> void:
	var round_trip := received-sent
	if round_trip < 0 or round_trip > 2.0 or not is_finite(stamp):
		return
	observe_snapshot(stamp,received)
	samples.append({"at":received,"rtt":round_trip,"offset":stamp+round_trip*0.5-received})
	while samples.size() > 5 or (not samples.is_empty() and received-float(samples[0].at) > 4.0):
		samples.pop_front()
	var best: Dictionary = samples[0]
	for sample in samples:
		if sample.rtt < best.rtt:
			best = sample
	offset = best.offset
	rtt = best.rtt

func advance(local: float) -> float:
	if not initialized:
		return time
	var elapsed := maxf(0,local-previous_local)
	previous_local = maxf(previous_local,local)
	var error := local+offset-(time+elapsed)
	if error > 1.0:
		time = local+offset
	else:
		time += elapsed*clampf(1.0+error*0.5,0.9,1.1)
	return time
