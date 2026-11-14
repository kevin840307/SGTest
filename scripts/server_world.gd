class_name ServerWorld
extends RefCounted

# Pure authoritative simulation: no UI, sockets, textures or scene dependency.
const SIZE := Vector2(2400, 1600)
const SPEED := 230.0
const MAX_DROPS := 5000
const GRID = preload("res://scripts/spatial_grid.gd")
var players: Dictionary = {}
var monsters: Dictionary = {}
var drops: Dictionary = {}
var player_grid := GRID.new()
var time := 0.0
var ai_elapsed := 0.0
var next_drop := 1
var drop_revision := 0

static func facing(vector: Vector2) -> int:
	return [6, 7, 8, 1, 2, 3, 4, 5][posmod(roundi(vector.angle() / (PI / 4.0)), 8)]

func populate(monster_count: int, drop_count: int) -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 8413
	for i in monster_count:
		monsters[i+1] = {"p": Vector2(rng.randf_range(80, SIZE.x-80), rng.randf_range(80, SIZE.y-80)),
			"hp": 30, "facing": 1, "pose": 0, "action_time": 0.0, "respawn": 0.0}
	for i in mini(drop_count, MAX_DROPS):
		drops[next_drop] = {"p": Vector2(rng.randf_range(0, SIZE.x), rng.randf_range(0, SIZE.y)), "item": 1001}
		next_drop += 1
	drop_revision += 1

func join(id: int) -> void:
	players[id] = {"p": Vector2(1200, 800) + Vector2(20*(players.size()%8), 0),
		"move": Vector2.ZERO, "last_input": time, "last_attack": -100.0, "inventory": {},
		"facing": 1, "pose": 0, "action_time": 0.0, "seen_drops": {}}

func move(id: int, x: float, y: float) -> void:
	if players.has(id) and is_finite(x) and is_finite(y):
		players[id].last_input = time
		players[id].move = Vector2(x,y).limit_length(1.0)

func step(delta: float) -> void:
	time += delta
	for id in players:
		var state: Dictionary = players[id]
		if time-state.last_input > 0.5:
			state.move = Vector2.ZERO
		state.p = (state.p + state.move*SPEED*delta).clamp(Vector2.ZERO, SIZE)
		if time-state.action_time > 0.35:
			state.pose = 1 if state.move.length_squared() > 0.01 else 0
		if state.move.length_squared() > 0.01:
			state.facing = facing(state.move)
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
				monster.pose = 0
				monster.p = Vector2(100+(id*97)%2200, 100+(id*53)%1400)
			continue
		var closest := 500.0
		var target := Vector2.ZERO
		for pid in player_grid.query(monster.p, 500.0):
			var distance: float = monster.p.distance_to(players[pid].p)
			if distance < closest:
				closest = distance
				target = players[pid].p
		if closest < 500.0 and closest > 45.0:
			monster.facing = facing(monster.p.direction_to(target))
			monster.p = (monster.p + monster.p.direction_to(target)*65.0*elapsed).clamp(Vector2.ZERO, SIZE)
		if time-monster.action_time > 0.35:
			monster.pose = 1 if closest < 500.0 and closest > 45.0 else 0

func attack(id: int, monster_id: int) -> bool:
	if not players.has(id) or not monsters.has(monster_id):
		return false
	var player: Dictionary = players[id]
	var monster: Dictionary = monsters[monster_id]
	if monster.hp <= 0 or time-player.last_attack < 0.35 or player.p.distance_to(monster.p) > 160.0:
		return false
	player.last_attack = time
	player.facing = facing(monster.p-player.p)
	player.pose = 2
	player.action_time = time
	monster.pose = 3
	monster.action_time = time
	monster.hp -= 10
	if monster.hp <= 0:
		for item in [1001,1002,1003]:
			if drops.size() >= MAX_DROPS:
				break
			drops[next_drop] = {"p": monster.p+Vector2((item-1002)*22,0), "item": item}
			next_drop += 1
		monster.pose = 4
		monster.respawn = time+2.0
		drop_revision += 1
	return true

func pickup(id: int, drop_id: int) -> bool:
	if not players.has(id) or not drops.has(drop_id) or players[id].p.distance_to(drops[drop_id].p) > 100.0:
		return false
	var inventory: Dictionary = players[id].inventory
	var item: int = drops[drop_id].item
	inventory[item] = inventory.get(item,0)+1
	drops.erase(drop_id)
	drop_revision += 1
	return true
