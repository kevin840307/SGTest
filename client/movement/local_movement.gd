extends RefCounted
## Visual-only local route prediction using the same original Blocking.dat cells
## and bounded eight-neighbour path rules as Server.Map.MapNavigator.
## Never writes WorldState, never authorizes combat, drops, portals or movement.
const CELL := 16
const PIXELS_PER_SECOND := 240.0 # Server 8px / 30Hz authoritative tick.
const MAX_EXPANDED := 2048 # Exactly the current WorldLimits.MaxPathExpandedCells.
const CORRECTION_DEADBAND := 24.0
const CORRECTION_SNAP := 96.0
const DIRECTIONS := [
	Vector2i(1,0),Vector2i(0,1),Vector2i(-1,0),Vector2i(0,-1),
	Vector2i(1,1),Vector2i(-1,1),Vector2i(-1,-1),Vector2i(1,-1)
]

var ready := false
var active := false
var moving := false
var position := Vector2.INF
var facing := 6
var width := 0
var height := 0
var map_epoch := 0
var actor_id := 0
var _bytes := PackedByteArray()
var _route: Array[Vector2i] = []
var _route_index := 0
var _target_cell := Vector2i(-1,-1)
var _expected_sequence := 0
var _last_server_tick := -1
var _last_authority := Vector2.INF

func clear() -> void:
	ready = false
	active = false
	moving = false
	position = Vector2.INF
	facing = 6
	width = 0
	height = 0
	map_epoch = 0
	actor_id = 0
	_bytes.clear()
	_route.clear()
	_route_index = 0
	_target_cell = Vector2i(-1,-1)
	_expected_sequence = 0
	_last_server_tick = -1
	_last_authority = Vector2.INF

func configure(blocking: PackedByteArray, manifest: Dictionary, epoch: int, own_actor: int) -> bool:
	clear()
	if blocking.size() < 9 or epoch <= 0 or own_actor <= 0 or int(manifest.get("collision_cell",0)) != CELL:
		return false
	var w := int(blocking.decode_u32(0))
	var h := int(blocking.decode_u32(4))
	var size: Array = manifest.get("size",[])
	if size.size() != 2 or w <= 0 or h <= 0 or w*h > 16777216 or blocking.size() != 8+w*h:
		return false
	if int(size[0]) != w*CELL or int(size[1]) != h*CELL:
		return false
	width = w
	height = h
	_bytes = blocking
	map_epoch = epoch
	actor_id = own_actor
	ready = true
	return true

func _cell(p: Vector2) -> Vector2i:
	if not p.is_finite() or p.x < 0.0 or p.y < 0.0 or p.x >= width*CELL or p.y >= height*CELL:
		return Vector2i(-1,-1)
	return Vector2i(floori(p.x/CELL),floori(p.y/CELL))

func _walkable(c: Vector2i) -> bool:
	return c.x >= 0 and c.y >= 0 and c.x < width and c.y < height and _bytes[8+c.y*width+c.x] == 0

func _direct_clear(start: Vector2, goal: Vector2) -> bool:
	var source := _cell(start)
	var target := _cell(goal)
	if not _walkable(source) or not _walkable(target):
		return false
	if source == target:
		return true
	var dx := goal.x-start.x
	var dy := goal.y-start.y
	var sx := signi(dx)
	var sy := signi(dy)
	var tx := INF if sx == 0 else (float((source.x+1)*CELL if sx > 0 else source.x*CELL)-start.x)/dx
	var ty := INF if sy == 0 else (float((source.y+1)*CELL if sy > 0 else source.y*CELL)-start.y)/dy
	var stepx := INF if sx == 0 else CELL/absf(dx)
	var stepy := INF if sy == 0 else CELL/absf(dy)
	var limit := absi(target.x-source.x)+absi(target.y-source.y)+2
	while source != target and limit > 0:
		limit -= 1
		if absf(tx-ty) <= 1.0e-12:
			if not _walkable(source+Vector2i(sx,0)) or not _walkable(source+Vector2i(0,sy)):
				return false
			source += Vector2i(sx,sy)
			tx += stepx
			ty += stepy
		elif tx < ty:
			source.x += sx
			tx += stepx
		else:
			source.y += sy
			ty += stepy
		if not _walkable(source):
			return false
	return source == target

func _h(a: Vector2i,b: Vector2i) -> int:
	var dx := absi(a.x-b.x)
	var dy := absi(a.y-b.y)
	return 14*mini(dx,dy)+10*absi(dx-dy)

func _before(a: Array,b: Array) -> bool:
	if int(a[0]) != int(b[0]):
		return int(a[0]) < int(b[0])
	if int(a[1]) != int(b[1]):
		return int(a[1]) < int(b[1])
	return int(a[2]) < int(b[2])

func _heap_push(heap: Array, item: Array) -> void:
	heap.append(item)
	var i := heap.size()-1
	while i > 0:
		var parent := (i-1)/2
		if not _before(item,heap[parent]):
			break
		heap[i] = heap[parent]
		i = parent
	heap[i] = item

func _heap_pop(heap: Array) -> Array:
	var item: Array = heap[0]
	var last: Array = heap.pop_back()
	if heap.is_empty():
		return item
	var i := 0
	while 2*i+1 < heap.size():
		var child := 2*i+1
		if child+1 < heap.size() and _before(heap[child+1],heap[child]):
			child += 1
		if not _before(heap[child],last):
			break
		heap[i] = heap[child]
		i = child
	heap[i] = last
	return item

func _find_route(start: Vector2i, goal: Vector2i) -> Array[Vector2i]:
	var empty: Array[Vector2i] = []
	if not _walkable(start) or not _walkable(goal):
		return empty
	if start == goal:
		return [start]
	var begin := start.y*width+start.x
	var finish := goal.y*width+goal.x
	var best := {begin:0}
	var parent := {}
	var closed := {}
	var heap: Array = []
	var initial_h := _h(start,goal)
	_heap_push(heap,[initial_h,initial_h,begin,0])
	var expanded := 0
	while not heap.is_empty():
		var entry: Array = _heap_pop(heap)
		var idx := int(entry[2])
		var cost := int(entry[3])
		if int(best.get(idx,-1)) != cost or closed.has(idx):
			continue
		closed[idx] = true
		if expanded >= MAX_EXPANDED:
			return empty
		expanded += 1
		if idx == finish:
			var path: Array[Vector2i] = []
			var cursor := finish
			while cursor != begin:
				path.append(Vector2i(cursor%width,cursor/width))
				cursor = int(parent[cursor])
			path.append(start)
			path.reverse()
			return path
		var cell := Vector2i(idx%width,idx/width)
		for direction in DIRECTIONS:
			var next: Vector2i = cell+direction
			if not _walkable(next):
				continue
			if direction.x != 0 and direction.y != 0:
				if not _walkable(cell+Vector2i(direction.x,0)) or not _walkable(cell+Vector2i(0,direction.y)):
					continue
			var next_idx := next.y*width+next.x
			if closed.has(next_idx):
				continue
			var next_cost := cost+(14 if direction.x != 0 and direction.y != 0 else 10)
			if best.has(next_idx) and next_cost >= int(best[next_idx]):
				continue
			best[next_idx] = next_cost
			parent[next_idx] = idx
			var heuristic := _h(next,goal)
			_heap_push(heap,[next_cost+heuristic,heuristic,next_idx,next_cost])
	return empty

func observe(snapshot: Dictionary) -> void:
	if not ready or not bool(snapshot.get("world_ready",false)) or int(snapshot.get("map_epoch",0)) != map_epoch or int(snapshot.get("actor_id",0)) != actor_id:
		return
	var own: Dictionary = snapshot.get("self_state",{})
	if own.is_empty():
		# WorldReady itself already carries the trusted spawn coordinates;
		# the first SelfState can follow later. Don't make the first click wait.
		if not position.is_finite():
			var spawn := Vector2(float(snapshot.get("x",INF)),float(snapshot.get("y",INF)))
			if spawn.is_finite() and _walkable(_cell(spawn)):
				position = spawn
				facing = clampi(int(snapshot.get("facing",6)),1,8)
		return
	var server_tick := int(own.get("server_tick",-1))
	if server_tick < _last_server_tick:
		return
	_last_server_tick = server_tick
	var authoritative := Vector2(float(own.get("x",snapshot.get("x",0))),float(own.get("y",snapshot.get("y",0))))
	if not authoritative.is_finite() or not _walkable(_cell(authoritative)):
		return
	_last_authority = authoritative
	if not position.is_finite() or not active:
		position = authoritative
		facing = int(own.get("facing",6))
		return
	if bool(snapshot.get("player_dead",false)) or bool(snapshot.get("waiting_for_new_map",false)):
		stop()
		active = false
		position = authoritative
		return
	var receipt := int(snapshot.get("last_receipt_sequence",0))
	if _expected_sequence > 0 and receipt >= _expected_sequence and int(snapshot.get("last_receipt_status",-1)) != 0:
		stop()
		active = false
		position = authoritative
		return
	# A newly queued intent may already be moving locally before the Server
	# processes it. Old SelfState locations must never pull it backwards.
	var confirmed := int(own.get("movement_action_sequence",0)) >= _expected_sequence
	if not confirmed and receipt < _expected_sequence:
		return
	var delta := position.distance_to(authoritative)
	if delta > CORRECTION_SNAP:
		position = authoritative
		if moving and _target_cell != Vector2i(-1,-1):
			_set_route(_target_cell)
	elif delta > CORRECTION_DEADBAND:
		# Smaller disagreement is network age, not a command to rewind each frame.
		position = position.lerp(authoritative,0.12)
	if not moving and receipt >= _expected_sequence and not bool(own.get("moving",false)) and delta <= CORRECTION_DEADBAND:
		active = false
		position = authoritative

func _set_route(goal: Vector2i) -> bool:
	_route = _find_route(_cell(position),goal)
	_route_index = 1
	_target_cell = goal
	moving = _route.size() > 1
	return moving

func request_target(point: Vector2, sequence: int) -> bool:
	if not ready or not position.is_finite() or not point.is_finite():
		return false
	var goal := _cell(point)
	if not _walkable(goal):
		return false
	_expected_sequence = maxi(_expected_sequence,sequence)
	if moving and goal == _target_cell:
		active = true
		return true
	if not _set_route(goal):
		return false
	active = true
	return true

func stop() -> void:
	moving = false
	_route.clear()
	_route_index = 0
	_target_cell = Vector2i(-1,-1)

func advance(delta: float) -> void:
	if not ready or not active or not moving or delta <= 0.0:
		return
	var remaining := PIXELS_PER_SECOND*minf(delta,0.1)
	for step in 8:
		if remaining <= 0.00001 or _route_index >= _route.size():
			break
		# Follow server's six-cell visibility lookahead; never cut across walls.
		var farthest := mini(_route.size()-1,_route_index+6)
		for probe in range(farthest,_route_index,-1):
			var candidate := Vector2(_route[probe])*CELL+Vector2.ONE*8.0
			if _direct_clear(position,candidate):
				_route_index = probe
				break
		var next := Vector2(_route[_route_index])*CELL+Vector2.ONE*8.0
		var motion := next-position
		var distance := motion.length()
		if distance <= 0.00001:
			_route_index += 1
			continue
		var travel := minf(distance,remaining)
		var arrived := position+motion/distance*travel
		if not _direct_clear(position,arrived):
			stop()
			break
		facing = _face(motion,facing)
		position = arrived
		remaining -= travel
		if travel >= distance-0.00001:
			position = next
			_route_index += 1
	if _route_index >= _route.size():
		stop()

func _face(displacement: Vector2, previous: int) -> int:
	var angle := atan2(displacement.y,displacement.x)
	var sector := floori(angle/(PI/4.0)+0.5)
	var new_facing := wrapi(sector+5,0,8)+1
	if previous >= 1 and previous <= 8:
		var old_sector := (previous+2)%8
		var diff := atan2(sin(angle-old_sector*PI/4.0),cos(angle-old_sector*PI/4.0))
		if absf(diff) <= PI/6.0:
			return previous
	return new_facing
