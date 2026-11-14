extends SceneTree

var failures := 0
const WORLD = preload("res://scripts/server_world.gd")
const POLICY = preload("res://scripts/action_policy.gd")

func check(condition: bool, message: String) -> void:
	if not condition:
		failures += 1
		push_error(message)

func arena():
	var world := WORLD.new()
	world.populate(1,0)
	world.join(1)
	world.monsters[1].p = world.players[1].p+Vector2(30,0)
	world.monsters[1].damage = 50
	return world

func _initialize() -> void:
	var world = arena()
	world.players[1].inventory[1001] = 2
	world.step(1.9)
	check(world.players[1].hp == 100 and world.pending_attacks.is_empty(), "spawn protection rejects attacks")
	world.step(0.21)
	check(world.monsters[1].pose == 2 and world.players[1].hp == 100, "attack announces before damage")
	world.step(0.21)
	check(world.players[1].hp == 50 and world.players[1].pose == 3, "windup resolves authoritative damage and hit")
	world.step(1.0)
	world.step(0.21)
	check(world.players[1].hp == 0 and world.players[1].pose == 4, "second hit kills player")
	var at: Vector2 = world.players[1].p
	world.move(1,1,0)
	check(not world.attack(1,1), "dead player cannot attack")
	world.drops[1] = {"p":at,"item":1001}
	check(not world.pickup(1,1), "dead player cannot claim loot")
	world.step(0.2)
	check(world.players[1].p == at, "dead player cannot move")
	world.step(3.0)
	check(world.players[1].hp == 100 and world.players[1].pose == 0, "authoritative respawn restores HP")
	check(world.players[1].inventory[1001] == 2, "death and respawn preserve inventory")
	check(world.players[1].protected_until > world.time, "respawn has protection")
	world = arena()
	world.players[1].protected_until = 0
	world.step(0.21)
	world.move(1,-1,0)
	world.step(0.3)
	check(world.players[1].hp == 100, "escaping range during windup avoids damage")
	world = arena()
	world.players[1].protected_until = 0
	world.step(0.21)
	check(world.attack(1,1), "player interrupts pending monster attack")
	world.step(0.21)
	check(world.players[1].hp == 100, "interrupted monster cannot deal delayed damage")
	world = arena()
	world.join(2)
	world.players[2].p = world.players[1].p
	world.attack(1,1)
	world.attack(2,1)
	var hits: Array = []
	for event in world.drain_events():
		if event.kind == 1:
			hits.append(event)
	check(hits.size() == 2 and hits[0].hp == 20 and hits[1].hp == 10, "same-tick events capture independent health")
	check(hits[1].revision > hits[0].revision and hits[1].action_time == hits[0].action_time, "same-tick changes have distinct revisions")
	check(POLICY.newer_revision(0,0xffffffff), "revision comparison handles uint32 wrap")
	check(not POLICY.newer_revision(0xffffffff,0), "wrapped stale revision rejected")
	var codec = preload("res://scripts/snapshot_codec.gd")
	var decoded: Array = codec.decode(codec.encode([[1,0,0,50,1,3,2.4,0xffffffff]]))
	check(decoded[0][3] == 50 and decoded[0][7] == 0xffffffff, "wire preserves health and complete uint32 revision")
	print("combat checks: %d failures" % failures)
	quit(1 if failures else 0)
