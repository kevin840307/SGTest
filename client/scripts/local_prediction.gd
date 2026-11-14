extends RefCounted

const MOTOR = preload("res://scripts/movement.gd")
const POLICY = preload("res://scripts/action_policy.gd")
const INPUT = preload("res://scripts/input_codec.gd")
const LEGACY_FACING = preload("res://scripts/legacy_facing.gd")
const MAX_PENDING := 64
const MAX_AUTHORITY_AGE := 0.5
var navigation: RefCounted
var bounds := Vector2(2400,1600)
var predicted := Vector2.ZERO
var previous := Vector2.ZERO
var correction := Vector2.ZERO
var motion := Vector2.ZERO
var facing := 1
var pending: Array = []
var sequence := 0
var epoch := -1
var last_tick := -1
var last_received := 0.0
var alive := false
var initialized := false
var max_pending := 0
var max_correction := 0.0
var reconciliations := 0
var walk_started := 0.0

func configure(grid: RefCounted, world_bounds: Vector2) -> void:
	navigation = grid
	bounds = world_bounds
	initialized = false
	alive = false
	pending.clear()
	last_tick = -1
	epoch = -1
	sequence = 0

func freeze(at: Vector2) -> void:
	predicted = at
	previous = at
	correction = Vector2.ZERO
	motion = Vector2.ZERO
	pending.clear()
	alive = false

func authority(tick: int, acknowledged: int, life_epoch: int, at: Vector2, hp: int, local: float) -> bool:
	if tick <= last_tick or (epoch >= 0 and life_epoch != epoch and not POLICY.newer_revision(life_epoch,epoch)):
		return false
	var reset := not initialized or life_epoch != epoch or not alive or hp <= 0
	last_tick = tick
	last_received = local
	epoch = life_epoch
	initialized = true
	alive = hp > 0
	if reset:
		freeze(at)
		alive = hp > 0
		sequence = acknowledged
		return true
	var remaining: Array = []
	for frame in pending:
		if POLICY.newer_revision(int(frame.sequence),acknowledged):
			remaining.append(frame)
	pending = remaining
	if POLICY.newer_revision(acknowledged,sequence):
		sequence = acknowledged
	var replay := at
	for frame in pending:
		replay = MOTOR.step(replay,frame.motion,MOTOR.STEP,bounds,navigation)
	var change := replay-predicted
	max_correction = maxf(max_correction,change.length())
	reconciliations += 1
	if change.length_squared() > 128.0*128.0:
		previous = replay
		correction = Vector2.ZERO
	else:
		previous += change
		correction -= change
		if navigation != null and not navigation.walkable(previous):
			previous = replay
	predicted = replay
	return true

func step_input(requested: Vector2, local: float, clock: float) -> bool:
	previous = predicted
	if not requested.is_finite() or absf(requested.x) > 1 or absf(requested.y) > 1:
		motion = Vector2.ZERO
		return false
	if not initialized or not alive or local-last_received > MAX_AUTHORITY_AGE or pending.size() >= MAX_PENDING:
		motion = Vector2.ZERO
		return false
	var next_motion := requested.limit_length(1.0)
	if motion.is_zero_approx() and not next_motion.is_zero_approx():
		walk_started = clock
	motion = next_motion
	if not motion.is_zero_approx():
		facing = LEGACY_FACING.from_vector(motion,facing)
	sequence = (sequence+1)&0xffffffff
	pending.append({"sequence":sequence,"motion":motion})
	max_pending = maxi(max_pending,pending.size())
	predicted = MOTOR.step(predicted,motion,MOTOR.STEP,bounds,navigation)
	return true

func packet() -> PackedByteArray:
	return INPUT.encode(pending)

func render(fraction: float, delta: float) -> Vector2:
	correction *= exp(-20.0*delta)
	var base := previous.lerp(predicted,clampf(fraction,0,1))
	var target := base+correction
	return target if navigation == null else navigation.slide(base,target)
