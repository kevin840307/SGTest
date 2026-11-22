extends RefCounted
## Visual-only local route prediction using the same original Blocking.dat cells
## and bounded eight-neighbour path rules as Server.Map.MapNavigator.
## Never writes WorldState, never authorizes combat, drops, portals or movement.
const CELL := 16
const PIXELS_PER_SECOND := 240.0 # Server 8px / 30Hz authoritative tick.
const MAX_EXPANDED := 2048 # Exactly the current WorldLimits.MaxPathExpandedCells.
const FAST_PATH_BUDGET_USEC := 5000 # Recovered caps normal query at 6000us.
const FALLBACK_FRAME_BUDGET_USEC := 1500 # Same fallback frame budget as Recovered.
const SEARCH_BUDGET_CHECK_INTERVAL := 16
const CORRECTION_DEADBAND := 24.0
const CORRECTION_SNAP := 96.0
const SETTLE_SNAP := 5.0
const VISUAL_RECOVERY_SPEED := 480.0
const MAX_VISUAL_CORRECTION := 320.0
const DIRECTIONS := [
	Vector2i(1,0),Vector2i(0,1),Vector2i(-1,0),Vector2i(0,-1),
	Vector2i(1,1),Vector2i(-1,1),Vector2i(-1,-1),Vector2i(1,-1)
]

var ready := false
var active := false
var moving := false
var position := Vector2.INF
# Logical route and displayed paper doll are separate during a Server
# correction. Never teleport the rendered actor 146-200px in one frame.
var visual_position := Vector2.INF
var _visual_settling := false
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
var _settle_target := Vector2.INF
var _full_path_searches := 0
var _last_correction_warn_msec := -5000
var correction_events := 0
var large_correction_events := 0
var max_correction_distance := 0.0
var _search: Dictionary = {}

func clear() -> void:
	ready = false
	active = false
	moving = false
	position = Vector2.INF
	visual_position = Vector2.INF
	_visual_settling = false
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
	_settle_target = Vector2.INF
	_full_path_searches = 0
	_last_correction_warn_msec = -5000
	correction_events = 0
	large_correction_events = 0
	max_correction_distance = 0.0
	_search.clear()

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

func _begin_search(start: Vector2i, goal: Vector2i) -> bool:
	if not _walkable(start) or not _walkable(goal) or start == goal:
		return false
	var begin := start.y*width+start.x
	var finish := goal.y*width+goal.x
	var heap: Array = []
	var heuristic := _h(start,goal)
	_heap_push(heap,[heuristic,heuristic,begin,0])
	_search = {"begin":begin,"finish":finish,"start":start,"goal":goal,
		"best":{begin:0},"parents":{},"closed":{},"heap":heap,"expanded":0}
	return true


func _abort_search() -> void:
	_search.clear()
	_route.clear()
	_route_index = 0
	moving = false
	active = false


func _search_frame(budget_usec: int) -> void:
	# Recovered: complete normal A* within a 6ms synchronous budget;
	# only large-detour fallback yields across render frames (1.5ms budget).
	# The route remains private until the FULL path reaches the goal.
	if _search.is_empty():
		return
	var heap: Array = _search.heap
	var best: Dictionary = _search.best
	var parents: Dictionary = _search.parents
	var closed: Dictionary = _search.closed
	var expanded := int(_search.expanded)
	var started_usec := Time.get_ticks_usec()
	var used := 0
	var goal: Vector2i = _search.goal
	while not heap.is_empty():
		if used > 0 and used % SEARCH_BUDGET_CHECK_INTERVAL == 0 and Time.get_ticks_usec() - started_usec >= budget_usec:
			break
		var entry: Array = _heap_pop(heap)
		var idx := int(entry[2])
		var cost := int(entry[3])
		if int(best.get(idx,-1)) != cost or closed.has(idx):
			continue
		if expanded >= MAX_EXPANDED:
			_abort_search()
			return
		closed[idx] = true
		expanded += 1
		used += 1
		if idx == int(_search.finish):
			var path: Array[Vector2i] = []
			var cursor := idx
			while cursor != int(_search.begin):
				path.append(Vector2i(cursor%width,cursor/width))
				cursor = int(parents[cursor])
			path.append(Vector2i(_search.start))
			path.reverse()
			_route = path
			_route_index = 1
			_search.clear()
			moving = _route.size() > 1
			if not moving:
				active = false
			return
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
			parents[next_idx] = idx
			var heuristic := _h(next,goal)
			_heap_push(heap,[next_cost+heuristic,heuristic,next_idx,next_cost])
	_search["expanded"] = expanded
	if heap.is_empty():
		_abort_search()


func _diagnose_correction(reason: String, authoritative: Vector2) -> void:
	var distance := position.distance_to(authoritative)
	correction_events += 1
	max_correction_distance = maxf(max_correction_distance,distance)
	if distance >= 96.0:
		large_correction_events += 1
	var now := Time.get_ticks_msec()
	if now-_last_correction_warn_msec < 5000:
		return
	_last_correction_warn_msec = now
	push_warning("LOCAL_MOVE_CORRECTION reason=%s epoch=%d actor=%d sequence=%d visual=%s authority=%s delta=%.1f" % [
		reason,map_epoch,actor_id,_expected_sequence,str(position),str(authoritative),distance])


func _begin_visual_correction() -> void:
	if not visual_position.is_finite() or not position.is_finite() or \
		visual_position.distance_to(position)>MAX_VISUAL_CORRECTION:
		visual_position = position
		_visual_settling = false
		active = false
		moving = false
		return
	if visual_position.distance_to(position) <= SETTLE_SNAP:
		visual_position = position
		_visual_settling = false
		active = false
		moving = false
		return
	# Presentation-only bounded correction: Server remains the simulation
	# authority, but a 146px stop correction must not teleport the Canvas.
	_visual_settling = true
	active = true
	moving = true
	facing = _face(position-visual_position,facing)


func advance_visual(delta: float) -> void:
	if not ready or not position.is_finite() or delta <= 0.0:
		return
	if not visual_position.is_finite():
		visual_position = position
	var remaining := visual_position.distance_to(position)
	var step := VISUAL_RECOVERY_SPEED*minf(delta,0.1)
	if remaining <= maxf(SETTLE_SNAP,step):
		visual_position = position
		if _visual_settling:
			_visual_settling = false
			active = false
			moving = false
	elif remaining > 0.0:
		visual_position = visual_position.move_toward(position,step)
		if _visual_settling:
			moving = true
			facing = _face(position-visual_position,facing)


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
				visual_position = spawn
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
		visual_position = authoritative
		_visual_settling = false
		facing = int(own.get("facing",6))
		return
	if bool(snapshot.get("player_dead",false)) or bool(snapshot.get("waiting_for_new_map",false)):
		stop()
		active = false
		position = authoritative
		visual_position = authoritative
		_visual_settling = false
		return
	var receipt := int(snapshot.get("last_receipt_sequence",0))
	if _expected_sequence > 0 and receipt >= _expected_sequence and int(snapshot.get("last_receipt_status",-1)) != 0:
		# A rejected command means movement is unauthorized: rollback now.
		_diagnose_correction("receipt_rejected_%d" % int(snapshot.get("last_receipt_status",-1)),authoritative)
		stop()
		position = authoritative
		visual_position = authoritative
		_visual_settling = false
		active = false
		return
	# A newly queued intent may already be moving locally before the Server
	# processes it. Old SelfState locations must never pull it backwards.
	# A receipt ACK and a matching SelfState are distinct ordered streams.
	# A successful receipt may arrive while SelfState still describes the OLD
	# idle action. Never accept that old idle as completion of a new route:
	# it stops after the first ~4px and then jumps along Server updates.
	if _visual_settling and not bool(own.get("moving",false)):
		position = authoritative
		return
	var confirmed := int(own.get("movement_action_sequence",0)) >= _expected_sequence
	if not confirmed:
		return
	var delta := position.distance_to(authoritative)
	# Normal moving SelfState is a delayed sample, not our current draw
	# position. Repeated lerp(0.12) against it slowed the character after
	# EVERY packet and made mid-distance movement rubber-band and slide.
	if bool(own.get("moving",false)):
		if delta > CORRECTION_SNAP * 2.0:
			# Genuine large divergence: authoritative relocation / bad route.
			_diagnose_correction("moving_divergence",authoritative)
			position = authoritative
			if moving and _target_cell != Vector2i(-1,-1):
				_set_route(_target_cell)
		return
	# The Server reached a final position. Do not switch directly to
	# the ~83ms delayed owner PositionHistory (a visible stop-time rewind).
	if delta <= SETTLE_SNAP:
		position = authoritative
		visual_position = authoritative
		stop()
		active = false
		_visual_settling = false
		_settle_target = Vector2.INF
	elif delta > CORRECTION_SNAP or not _direct_clear(position,authoritative):
		# Preserve the display position while the logical route is corrected.
		_diagnose_correction("stop_divergence",authoritative)
		position = authoritative
		stop()
		_begin_visual_correction()
		_settle_target = Vector2.INF
	else:
		if not _settle_target.is_finite():
			stop()
		_settle_target = authoritative
		moving = true

func _set_route(goal: Vector2i) -> bool:
	# Server.MapNavigator ALWAYS runs the same 8-way octile A*, even when
	# the straight segment is clear. The previous Client shortcut invented a
	# two-point diagonal route while the Server followed octile grid waypoints;
	# the resulting trajectory drift caused late corrections / snap backs.
	# Use exactly the Server A* path and its six-cell steering lookahead.
	_full_path_searches += 1
	_route.clear()
	_route_index = 0
	_target_cell = goal
	moving = false
	if not _begin_search(_cell(position),goal):
		return false
	# Complete the usual short A* within the click's 5ms budget; large routes
	# finish incrementally before a MoveTarget is ever sent to the Server.
	_search_frame(FAST_PATH_BUDGET_USEC)
	return moving or not _search.is_empty()

func authority_error() -> float:
	if not position.is_finite() or not _last_authority.is_finite():
		return INF
	return position.distance_to(_last_authority)


func retarget_safe(max_authority_error: float = 32.0) -> bool:
	# Recovered can repath immediately because its local player position is the
	# movement source. SGTest also has an authoritative Server. Do not stack a
	# new cursor route on top of an unresolved correction / large prediction
	# lead; otherwise every 80ms hold repath starts from a different cell than
	# ServerWorld and the two paths can diverge by hundreds of pixels.
	return ready and active and not _visual_settling and \
		position.is_finite() and _last_authority.is_finite() and \
		authority_error() <= max_authority_error


func request_target(point: Vector2, sequence: int) -> bool:
	if not ready or not position.is_finite() or not point.is_finite():
		return false
	var goal := _cell(point)
	if not _walkable(goal):
		return false
	_settle_target = Vector2.INF
	_visual_settling = false
	if not visual_position.is_finite():
		visual_position = position
	if goal == _target_cell and (moving or not _search.is_empty()):
		# Server deduplicates identical-target updates but consumes their wire
		# sequence without increasing movement_action_sequence. Keep the pending
		# ROUTE sequence; otherwise later SelfStates can never confirm it.
		active = true
		return true
	if not _set_route(goal):
		return false
	_expected_sequence = maxi(_expected_sequence,sequence)
	active = true
	return true

func stop() -> void:
	_settle_target = Vector2.INF
	_search.clear()
	moving = false
	_route.clear()
	_route_index = 0
	_target_cell = Vector2i(-1,-1)

func advance(delta: float) -> void:
	if not ready or not active or delta <= 0.0:
		return
	var remaining := PIXELS_PER_SECOND*minf(delta,0.1)
	if _settle_target.is_finite():
		var drift := position.distance_to(_settle_target)
		if drift <= maxf(SETTLE_SNAP,remaining):
			position = _settle_target
			_settle_target = Vector2.INF
			active = false
			moving = false
		else:
			facing = _face(_settle_target-position,facing)
			position = position.move_toward(_settle_target,remaining)
			moving = true # Never visibly slide toward a correction in idle pose.
		return
	if not _search.is_empty():
		_search_frame(FALLBACK_FRAME_BUDGET_USEC)
	if not moving:
		return
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
