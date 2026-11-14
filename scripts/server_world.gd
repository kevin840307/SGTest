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
var players: Dictionary = {}
var monsters: Dictionary = {}
var drops: Dictionary = {}
var player_grid := GRID.new()
var time := 0.0
var ai_elapsed := 0.0
var next_drop := 1
var drop_revision := 0
var events: Array[Dictionary] = []
var pending_attacks: Dictionary = {}

static func facing(vector: Vector2) -> int:
	return [6, 7, 8, 1, 2, 3, 4, 5][posmod(roundi(vector.angle() / (PI / 4.0)), 8)]

func populate(monster_count: int, drop_count: int) -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 8413
	for i in monster_count:
		monsters[i+1] = {"p": Vector2(rng.randf_range(80, SIZE.x-80), rng.randf_range(80, SIZE.y-80)),
			"hp": 30, "facing": 1, "pose": 0, "action_time": 0.0, "respawn": 0.0,
			"last_attack": -100.0, "damage": 10, "revision":0}
	for i in mini(drop_count, MAX_DROPS):
		drops[next_drop] = {"p": Vector2(rng.randf_range(0, SIZE.x), rng.randf_range(0, SIZE.y)), "item": 1001}
		next_drop += 1
	drop_revision += 1

func join(id: int) -> void:
	players[id] = {"p": Vector2(1200, 800) + Vector2(20*(players.size()%8), 0),
		"move": Vector2.ZERO, "last_input": time, "last_attack": -100.0, "inventory": {},
		"facing": 1, "pose": 0, "action_time": 0.0, "seen_drops": {},
		"hp": PLAYER_HP, "respawn": 0.0, "protected_until": time+SPAWN_PROTECTION_SECONDS, "revision":0}

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
	if players.has(id) and players[id].hp > 0 and is_finite(x) and is_finite(y):
		players[id].last_input = time
		players[id].move = Vector2(x,y).limit_length(1.0)

func step(delta: float) -> void:
	time += delta
	for id in players:
		var state: Dictionary = players[id]
		if state.hp <= 0:
			state.move = Vector2.ZERO
			if time >= state.respawn:
				state.hp = PLAYER_HP
				state.p = Vector2(1200,800)+Vector2(20*(id%8),0)
				state.protected_until = time+SPAWN_PROTECTION_SECONDS
				_pose(0,id,state,0)
			continue
		if time-state.last_input > 0.5:
			state.move = Vector2.ZERO
		state.p = (state.p + state.move*SPEED*delta).clamp(Vector2.ZERO, SIZE)
		var locked: bool = ACTION.active(state.pose, state.action_time, time)
		if not locked:
			state.pose = 1 if state.move.length_squared() > 0.01 else 0
		if not locked and state.move.length_squared() > 0.01:
			state.facing = facing(state.move)
	_resolve_attacks()
	ai_elapsed += delta
	if ai_elapsed < 0.2:
		return
	var elapsed := ai_elapsed
	ai_elapsed = 0.0
	player_grid.rebuild(players)
	for id in monsters:
		var monster: Dictionary = monsters[id]
		if monster.hp <= 0:
			if time >= monster.respawn:
				monster.hp = 30
				monster.p = Vector2(100+(id*97)%2200, 100+(id*53)%1400)
				_pose(1,id,monster,0)
			continue
		if ACTION.active(monster.pose, monster.action_time, time):
			continue
		var closest := 500.0
		var target := Vector2.ZERO
		var target_id := 0
		for pid in player_grid.query(monster.p, 500.0):
			if players[pid].hp <= 0:
				continue
			var distance: float = monster.p.distance_to(players[pid].p)
			if distance < closest:
				closest = distance
				target = players[pid].p
				target_id = pid
		if target_id != 0 and closest <= MONSTER_ATTACK_RANGE:
			if time >= players[target_id].protected_until and time-monster.last_attack >= MONSTER_ATTACK_COOLDOWN:
				monster.last_attack = time
				monster.facing = facing(target-monster.p)
				_pose(1,id,monster,2)
				pending_attacks[id] = {"target":target_id,"due":time+MONSTER_WINDUP,"started":time}
				continue
		if closest < 500.0 and closest > 45.0:
			monster.facing = facing(monster.p.direction_to(target))
			monster.p = (monster.p + monster.p.direction_to(target)*65.0*elapsed).clamp(Vector2.ZERO, SIZE)
		if time-monster.action_time > 0.35:
			monster.pose = 1 if closest < 500.0 and closest > 45.0 else 0

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
		player.hp = maxi(0,player.hp-int(monster.damage))
		if player.hp == 0:
			player.move = Vector2.ZERO
			player.respawn = time+PLAYER_RESPAWN_SECONDS
		_pose(0,int(pending.target),player,4 if player.hp == 0 else 3)

func attack(id: int, monster_id: int) -> bool:
	if not players.has(id) or not monsters.has(monster_id):
		return false
	var player: Dictionary = players[id]
	var monster: Dictionary = monsters[monster_id]
	if player.hp <= 0 or monster.hp <= 0 or time-player.last_attack < 0.35 or player.p.distance_to(monster.p) > 160.0:
		return false
	player.last_attack = time
	player.facing = facing(monster.p-player.p)
	_pose(0,id,player,2)
	monster.hp -= 10
	pending_attacks.erase(monster_id)
	if monster.hp <= 0:
		for item in [1001,1002,1003]:
			if drops.size() >= MAX_DROPS:
				break
			drops[next_drop] = {"p": monster.p+Vector2((item-1002)*22,0), "item": item}
			next_drop += 1
		monster.respawn = time+2.0
		drop_revision += 1
	_pose(1,monster_id,monster,4 if monster.hp <= 0 else 3)
	return true

func pickup(id: int, drop_id: int) -> bool:
	if not players.has(id) or players[id].hp <= 0 or not drops.has(drop_id) or players[id].p.distance_to(drops[drop_id].p) > 100.0:
		return false
	var inventory: Dictionary = players[id].inventory
	var item: int = drops[drop_id].item
	inventory[item] = inventory.get(item,0)+1
	drops.erase(drop_id)
	drop_revision += 1
	return true
