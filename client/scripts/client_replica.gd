extends RefCounted

# Transport-independent authoritative client state. Unreliable packets update
# individual entities; one missing packet never blocks an entire world frame.
const POLICY = preload("res://scripts/action_policy.gd")
const MAX_SNAPSHOT_AGE := 9 # 300ms at the authoritative 30Hz simulation.
const TOMBSTONE_TICKS := 90
var players: Dictionary = {}
var monsters: Dictionary = {}
var removed := [{},{}]
var latest_tick := -1
var prune_tick := -1
const INTERPOLATION_DELAY := 0.12
const MAX_EXTRAPOLATION := 0.1

func _remember(state: Dictionary, tick: int, snap: bool) -> void:
	if snap or not state.has("history"):
		state.history = []
	var history: Array = state.history
	if not history.is_empty() and tick <= int(history[-1][0]):
		return
	history.append([tick,state.p])
	if history.size() > 8:
		history.pop_front()

# Each actor owns its own small history. Missing another actor's packet never
# changes this timeline. Linear interpolation avoids easing at every snapshot.
func position_at(state: Dictionary, clock: float, navigation: RefCounted = null) -> Vector2:
	var history: Array = state.get("history",[])
	if history.size() < 2 or POLICY.active(state.pose,state.action_time,clock):
		return state.p
	var target := (clock-INTERPOLATION_DELAY)*30.0
	var result: Vector2 = history[0][1]
	var origin := result
	for i in range(1,history.size()):
		var a: Array = history[i-1]
		var b: Array = history[i]
		if target <= float(b[0]):
			origin = a[1]
			result = origin.lerp(b[1],clampf((target-float(a[0]))/float(b[0]-a[0]),0,1))
			return result if navigation == null else navigation.slide(origin,result)
	var last: Array = history[-1]
	var previous: Array = history[-2]
	origin = last[1]
	var velocity: Vector2 = (last[1]-previous[1])/float(last[0]-previous[0]) if int(state.pose) == 1 else Vector2.ZERO
	result = origin+velocity*clampf(target-float(last[0]),0,MAX_EXTRAPOLATION*30.0)
	return result if navigation == null else navigation.slide(origin,result)

func group(kind: int) -> Dictionary:
	return players if kind == 0 else monsters

func _advance(tick: int) -> void:
	latest_tick = maxi(latest_tick,tick)
	if latest_tick-prune_tick < 30:
		return
	prune_tick = latest_tick
	for tombstones in removed:
		for id in tombstones.keys():
			if latest_tick-int(tombstones[id]) > TOMBSTONE_TICKS:
				tombstones.erase(id)

func _state(row: Array) -> Dictionary:
	return {"p":Vector2(row[1],row[2]),"hp":int(row[3]),"facing":int(row[4]),
		"pose":int(row[5]),"action_time":float(row[6]),"revision":int(row[7]),
		"definition":int(row[8]) if row.size() > 8 else 0,"snapshot_tick":-1,"event_tick":-1,"tick":-1}

func snapshot(kind: int, tick: int, row: Array) -> int:
	if kind not in [0,1] or row.size() < 8 or tick < latest_tick-MAX_SNAPSHOT_AGE:
		return 0
	_advance(tick)
	var id := int(row[0])
	if int(removed[kind].get(id,-1)) >= tick:
		return 0
	var states := group(kind)
	var previous: Dictionary = states.get(id,{})
	if not previous.is_empty():
		if tick <= previous.snapshot_tick or tick < previous.event_tick:
			return 0
		if POLICY.newer_revision(previous.revision,int(row[7])):
			return 0
	var result := 1
	if previous.is_empty() or (int(previous.pose) != int(row[5]) and (int(previous.pose) == 4 or int(row[5]) == 4)):
		result = 2 # spawn, death or respawn must not interpolate across the map.
	var state: Dictionary = previous if not previous.is_empty() else _state(row)
	state.p = Vector2(row[1],row[2])
	state.definition = int(row[8]) if row.size() > 8 else 0
	state.hp = int(row[3])
	state.facing = int(row[4])
	state.pose = int(row[5])
	state.action_time = float(row[6])
	state.revision = int(row[7])
	state.snapshot_tick = tick
	state.tick = maxi(tick,state.event_tick)
	_remember(state,tick,result == 2)
	states[id] = state
	return result

func event(kind: int, id: int, tick: int, at: Vector2, hp: int, facing: int, pose: int, started: float, revision: int, definition: int = 0) -> int:
	if kind not in [0,1] or int(removed[kind].get(id,-1)) >= tick:
		return 0
	var states := group(kind)
	var previous: Dictionary = states.get(id,{})
	if previous.is_empty() and tick < latest_tick-MAX_SNAPSHOT_AGE:
		return 0
	if not previous.is_empty() and not POLICY.newer_revision(revision,previous.revision):
		return 0
	_advance(tick)
	var result := 2 if previous.is_empty() or pose in [0,4] else 1
	var state: Dictionary = previous if not previous.is_empty() else _state([id,at.x,at.y,hp,facing,pose,started,revision])
	state.p = at
	state.definition = definition
	state.hp = hp
	state.facing = facing
	state.pose = pose
	state.action_time = POLICY.wire_time(started)
	state.revision = revision
	state.event_tick = tick
	state.tick = maxi(tick,state.snapshot_tick)
	_remember(state,tick,result == 2)
	states[id] = state
	return result

func erase_interest(kind: int, tick: int, ids: PackedInt32Array) -> void:
	if kind not in [0,1]:
		return
	_advance(tick)
	var states := group(kind)
	for id in ids:
		removed[kind][id] = maxi(tick,int(removed[kind].get(id,-1)))
		if states.has(id) and int(states[id].tick) <= tick:
			states.erase(id)

func reset() -> void:
	players.clear()
	monsters.clear()
	removed = [{},{}]
	latest_tick = -1
	prune_tick = -1
