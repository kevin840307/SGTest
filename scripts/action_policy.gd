extends RefCounted

const COMBAT_SECONDS := 0.35
const DEATH_SECONDS := 2.0

static func newer_revision(incoming: int, previous: int) -> bool:
	var difference := (incoming-previous)&0xffffffff
	return difference > 0 and difference < 0x80000000

# Reliable RPC numbers and packed snapshot floats must compare at the same
# precision. Otherwise a rounded-down snapshot can preserve an old attack.
static func wire_time(seconds: float) -> float:
	var bytes := PackedByteArray()
	bytes.resize(4)
	bytes.encode_float(0, seconds)
	return bytes.decode_float(0)

static func active(pose: int, started: float, clock: float) -> bool:
	return pose >= 2 and clock - started < (DEATH_SECONDS if pose == 4 else COMBAT_SECONDS)

static func accepts_event(pose: int, started: float, previous: float, clock: float) -> bool:
	return wire_time(started) >= wire_time(previous) and active(pose, wire_time(started), clock)
