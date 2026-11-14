extends SceneTree

var failures := 0

func check(condition: bool, message: String) -> void:
	if not condition:
		failures += 1
		push_error(message)

func _initialize() -> void:
	var world := preload("res://scripts/server_world.gd").new()
	world.populate(1, 0)
	world.join(2147483001)
	world.join(2)
	var id := 2147483001
	world.monsters[1].p = world.players[id].p + Vector2(30,0)
	check(world.attack(id,1), "valid nearby attack")
	check(not world.attack(id,1), "attack cooldown enforced")
	world.step(0.36)
	check(world.attack(id,1), "second attack")
	world.step(0.36)
	check(world.attack(id,1), "killing attack")
	check(world.drops.size() == 3 and world.monsters[1].pose == 4, "three drops and synchronized death pose")
	var drop_id: int = world.drops.keys()[0]
	world.players[2].p = world.players[id].p
	check(world.pickup(id,drop_id), "first pickup wins")
	check(not world.pickup(2,drop_id), "duplicate pickup rejected")
	world.move(id, INF, NAN)
	check(world.players[id].move == Vector2.ZERO, "non-finite input rejected")
	world.move(id, 1, 1)
	check(world.players[id].move.length() <= 1.001, "diagonal normalization")
	world.step(0.6)
	check(world.players[id].move == Vector2.ZERO, "input timeout stops motion")
	world.step(2.1)
	check(world.monsters[1].hp == 30, "respawn occurs after death interval")
	var codec = preload("res://scripts/snapshot_codec.gd")
	var decoded: Array = codec.decode(codec.encode([[id,123.25,456.5,30,8,2,1.25]]))
	check(decoded[0][0] == id and decoded[0][1] == 123.25 and decoded[0][6] == 1.25, "wire format preserves large peer IDs and clocks")
	check(codec.decode(PackedByteArray([1,2])).is_empty(), "malformed snapshot rejected")
	print("world/protocol checks: %d failures" % failures)
	quit(1 if failures else 0)
