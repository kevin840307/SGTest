class_name ServerWorld
extends RefCounted

# Pure authoritative simulation: no UI, sockets, textures or scene dependency.
const SIZE := Vector2(2400, 1600)
const SPEED := 230.0
const MAX_DROPS := 5000
const PLAYER_HP := 100
const PLAYER_RESPAWN_SECONDS := 3.0
const SPAWN_PROTECTION_SECONDS := 2.0
const MONSTER_ATTACK_RANGE := 70.0
const MONSTER_ATTACK_COOLDOWN := 1.0
const MONSTER_WINDUP := 0.2
const GRID = preload("res://scripts/spatial_grid.gd")
const ACTION = preload("res://scripts/action_policy.gd")
const MOTOR = preload("res://scripts/movement.gd")
var players: Dictionary = {}
var monsters: Dictionary = {}
var drops: Dictionary = {}
var player_grid := GRID.new()
var time := 0.0
var tick := 0
var movers: Dictionary = {}
var think_buckets := [[],[],[],[],[],[]]
var next_drop := 1
var drop_revision := 0
var events: Array[Dictionary] = []
var pending_attacks: Dictionary = {}
var navigation: RefCounted
var bounds := SIZE
var spawn_point := Vector2(1200,800)

func configure_map(grid: RefCounted) -> Error:
	var spawn: Vector2 = grid.nearest(spawn_point)
	if spawn.x < 0:
		return ERR_INVALID_DATA
	navigation = grid
	bounds = Vector2(grid.width,grid.height)*grid.CELL
	spawn_point = spawn
	return OK

func _place(point: Vector2) -> Vector2:
	var clamped := point.clamp(Vector2.ZERO,bounds-Vector2.ONE*0.001)
	if navigation == null:
		return clamped
	var nearby: Vector2 = navigation.nearest(clamped)
	return spawn_point if nearby.x < 0 else nearby

func _move(from: Vector2, to: Vector2) -> Vector2:
	return MOTOR.move_to(from,to,bounds,navigation)

func _reachable(from: Vector2, to: Vector2) -> bool:
	return navigation == null or navigation.allows(from,to)

func _drop_point(origin: Vector2, requested: Vector2) -> Vector2:
	var placed := _place(requested)
	return placed if _reachable(origin,placed) else origin

static func facing(vector: Vector2) -> int:
	return [6, 7, 8, 1, 2, 3, 4, 5][posmod(roundi(vector.angle() / (PI / 4.0)), 8)]

func populate(monster_count: int, drop_count: int) -> void:
	monsters.clear()
	movers.clear()
	pending_attacks.clear()
	think_buckets = [[],[],[],[],[],[]]
	var rng := RandomNumberGenerator.new()
	rng.seed = 8413
	for i in monster_count:
		monsters[i+1] = {"p": _place(Vector2(rng.randf_range(80, SIZE.x-80), rng.randf_range(80, SIZE.y-80))),
			"hp": 30, "facing": 1, "pose": 0, "action_time": -(i%17)*0.037, "respawn": 0.0,
			"last_attack": -100.0, "damage": 10, "revision":0,
			"velocity":Vector2.ZERO,"phase_seed":(i%17)*0.037}
		think_buckets[i%6].append(i+1)
	for i in mini(drop_count, MAX_DROPS):
		drops[next_drop] = {"p": _place(Vector2(rng.randf_range(0, SIZE.x), rng.randf_range(0, SIZE.y))), "item": 1001}
		next_drop += 1
	drop_revision += 1

func join(id: int) -> void:
	players[id] = {"p": _place(spawn_point + Vector2(20*(players.size()%8), 0)),
		"move": Vector2.ZERO, "last_input": time, "last_attack": -100.0, "inventory": {},
		"facing": 1, "pose": 0, "action_time": 0.0, "seen_drops": {},
		"hp": PLAYER_HP, "respawn": 0.0, "protected_until": time+SPAWN_PROTECTION_SECONDS, "revision":0}
	players[id].seen_entities = [{},{}]
	players[id].input_queue = {}
	players[id].input_ack = 0
	players[id].input_max = 0
	players[id].input_motion = Vector2.ZERO
	players[id].queued_input = false
	players[id].life_epoch = 1

func _pose(kind: int, id: int, state: Dictionary, pose: int) -> void:
	state.pose = pose
	state.action_time = time
	state.revision = (int(state.revision)+1)&0xffffffff
	# Capture values now: a second hit in this tick must not mutate older events.
	events.append({"kind":kind,"id":id,"hp":state.hp,"pose":pose,
		"facing":state.facing,"action_time":time,"p":state.p,"revision":state.revision})

func drain_events() -> Array[Dictionary]:
	var result := events
	events = []
	return result

func move(id: int, x: float, y: float) -> void:
	# Trusted server-scripted motion. Remote peers use bounded, sequenced input.
	if players.has(id) and players[id].hp > 0 and is_finite(x) and is_finite(y) and absf(x) <= 1.0 and absf(y) <= 1.0:
		players[id].last_input = time
		players[id].move = Vector2(x,y).limit_length(1.0)

func queue_input(id: int, epoch: int, sequence: int, motion: Vector2) -> bool:
	if not players.has(id) or players[id].hp <= 0 or int(players[id].life_epoch) != epoch:
		return false
	if sequence < 0 or sequence > 0xffffffff or not motion.is_finite() or absf(motion.x) > 1 or absf(motion.y) > 1:
		return false
	var state: Dictionary = players[id]
	var distance := (sequence-int(state.input_ack))&0xffffffff
	if distance == 0 or distance > 64 or state.input_queue.has(sequence):
		return false
	state.queued_input = true
	state.input_queue[sequence] = motion.limit_length(1.0)
	state.last_input = time
	if ACTION.newer_revision(sequence,int(state.input_max)):
		state.input_max = sequence
	return true

func _consume_input(state: Dictionary) -> void:
	if time-float(state.last_input) > 0.5:
		state.input_queue.clear()
		state.input_ack = state.input_max
		state.input_motion = Vector2.ZERO
		state.move = Vector2.ZERO
		return
	if not ACTION.newer_revision(int(state.input_max),int(state.input_ack)):
		state.move = Vector2.ZERO
		return
	var next := (int(state.input_ack)+1)&0xffffffff
	if state.input_queue.has(next):
		state.input_motion = state.input_queue[next]
		state.input_queue.erase(next)
	state.input_ack = next
	state.move = state.input_motion

func step(delta: float) -> void:
	tick += 1
	time += delta
	for id in players:
		var state: Dictionary = players[id]
		if state.hp <= 0:
			state.move = Vector2.ZERO
			if time >= state.respawn:
				state.hp = PLAYER_HP
				state.p = _place(spawn_point+Vector2(20*(id%8),0))
				state.protected_until = time+SPAWN_PROTECTION_SECONDS
				state.life_epoch = (int(state.life_epoch)+1)&0xffffffff
				state.input_queue.clear()
				state.input_ack = 0
				state.input_max = 0
				state.input_motion = Vector2.ZERO
				_pose(0,id,state,0)
			continue
		if state.queued_input:
			_consume_input(state)
		elif time-state.last_input > 0.5:
			state.move = Vector2.ZERO
		state.p = MOTOR.step(state.p,state.move,delta,bounds,navigation)
		var locked: bool = ACTION.active(state.pose, state.action_time, time)
		if not locked:
			_locomotion(state,1 if state.move.length_squared() > 0.01 else 0)
		if not locked and state.move.length_squared() > 0.01:
			state.facing = facing(state.move)
	player_grid.rebuild(players)
	_resolve_attacks()
	_move_monsters(delta)
	if delta >= 0.2:
		for bucket in think_buckets:
			for id in bucket:
				_think_monster(id)
	else:
		for id in think_buckets[tick%6]:
			_think_monster(id)

func _locomotion(state: Dictionary, pose: int, phase: float = 0.0) -> void:
	if int(state.pose) != pose:
		state.pose = pose
		state.action_time = time-phase
		state.revision = (int(state.revision)+1)&0xffffffff

func _move_monsters(delta: float) -> void:
	for id in movers.keys():
		var monster: Dictionary = monsters[id]
		if monster.hp <= 0:
			movers.erase(id)
			continue
		if ACTION.active(monster.pose,monster.action_time,time):
			continue
		var previous: Vector2 = monster.p
		monster.p = _move(previous,previous+monster.velocity*delta)
		var displacement: Vector2 = monster.p-previous
		if not displacement.is_zero_approx():
			monster.facing = facing(displacement)

func _think_monster(id: int) -> void:
	var monster: Dictionary = monsters[id]
	if monster.hp <= 0:
		if time >= monster.respawn:
			monster.hp = 30
			monster.p = _place(Vector2(100+(id*97)%2200,100+(id*53)%1400))
			monster.velocity = Vector2.ZERO
			_pose(1,id,monster,0)
		return
	if ACTION.active(monster.pose,monster.action_time,time):
		return
	var closest := 500.0
	var target := Vector2.ZERO
	var target_id := 0
	for pid in player_grid.query(monster.p,500.0):
		if players[pid].hp <= 0:
			continue
		var distance: float = monster.p.distance_to(players[pid].p)
		if distance < closest:
			closest = distance
			target = players[pid].p
			target_id = pid
	if target_id != 0 and closest <= MONSTER_ATTACK_RANGE and _reachable(monster.p,target):
		if time >= players[target_id].protected_until and time-monster.last_attack >= MONSTER_ATTACK_COOLDOWN:
			monster.last_attack = time
			monster.facing = facing(target-monster.p)
			monster.velocity = Vector2.ZERO
			movers.erase(id)
			_pose(1,id,monster,2)
			pending_attacks[id] = {"target":target_id,"due":time+MONSTER_WINDUP,"started":time}
			return
	if closest < 500.0 and closest > 45.0:
		monster.velocity = monster.p.direction_to(target)*65.0
		monster.facing = facing(monster.velocity)
		movers[id] = true
		_locomotion(monster,1,monster.phase_seed)
	else:
		monster.velocity = Vector2.ZERO
		movers.erase(id)
		_locomotion(monster,0,monster.phase_seed)

func _resolve_attacks() -> void:
	for id in pending_attacks.keys():
		var pending: Dictionary = pending_attacks[id]
		if time < pending.due:
			continue
		pending_attacks.erase(id)
		if not monsters.has(id) or not players.has(pending.target):
			continue
		var monster: Dictionary = monsters[id]
		var player: Dictionary = players[pending.target]
		if monster.hp <= 0 or monster.pose != 2 or monster.action_time != pending.started:
			continue
		if player.hp <= 0 or time < player.protected_until or monster.p.distance_to(player.p) > MONSTER_ATTACK_RANGE:
			continue
		if not _reachable(monster.p,player.p):
			continue
		player.hp = maxi(0,player.hp-int(monster.damage))
		if player.hp == 0:
			player.move = Vector2.ZERO
			player.input_queue.clear()
			player.input_motion = Vector2.ZERO
			player.respawn = time+PLAYER_RESPAWN_SECONDS
		_pose(0,int(pending.target),player,4 if player.hp == 0 else 3)

func attack(id: int, monster_id: int) -> bool:
	if not players.has(id) or not monsters.has(monster_id):
		return false
	var player: Dictionary = players[id]
	var monster: Dictionary = monsters[monster_id]
	if player.hp <= 0 or monster.hp <= 0 or time-player.last_attack < 0.35 or player.p.distance_to(monster.p) > 160.0:
		return false
	if not _reachable(player.p,monster.p):
		return false
	player.last_attack = time
	player.facing = facing(monster.p-player.p)
	_pose(0,id,player,2)
	monster.hp -= 10
	monster.velocity = Vector2.ZERO
	movers.erase(monster_id)
	pending_attacks.erase(monster_id)
	if monster.hp <= 0:
		for item in [1001,1002,1003]:
			if drops.size() >= MAX_DROPS:
				break
			drops[next_drop] = {"p": _drop_point(monster.p,monster.p+Vector2((item-1002)*22,0)), "item": item}
			next_drop += 1
		monster.respawn = time+2.0
		drop_revision += 1
	_pose(1,monster_id,monster,4 if monster.hp <= 0 else 3)
	return true

func pickup(id: int, drop_id: int) -> bool:
	if not players.has(id) or players[id].hp <= 0 or not drops.has(drop_id) or players[id].p.distance_to(drops[drop_id].p) > 100.0:
		return false
	if not _reachable(players[id].p,drops[drop_id].p):
		return false
	var inventory: Dictionary = players[id].inventory
	var item: int = drops[drop_id].item
	inventory[item] = inventory.get(item,0)+1
	drops.erase(drop_id)
	drop_revision += 1
	return true
