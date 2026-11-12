extends SceneTree

const STATE = preload("res://core/state/world_state.gd")
var failures: int = 0

func _check(condition: bool, message: String) -> void:
	if not condition:
		failures += 1
		push_error(message)

func _initialize() -> void:
	var s = STATE.new()
	_check(s.apply({"type":"welcome","owned_id":1,"map_epoch":7,"server_tick":100}), "welcome")
	_check(s.local_entity_id == 1 and s.map_epoch == 7, "session state")
	_check(s.apply({"type":"spawn","map_epoch":7,"server_tick":100,"entity_id":2,
		"entity_revision":1,"name":"Remote","x":10.0,"y":20.0,"facing":0}), "spawn")
	_check(s.apply({"type":"move","map_epoch":7,"entity_id":2,"entity_revision":1,
		"movement_seq":1,"start_tick":100,"duration_ticks":10,"start_x":10.0,
		"start_y":20.0,"end_x":110.0,"end_y":20.0,"facing":0}), "move")
	_check(is_equal_approx(s.visual_position(2, 105.0).x, 60.0), "server-tick interpolation")
	_check(s.apply({"type":"action","map_epoch":7,"entity_id":2,
		"action_seq":1,"action":"hit","facing":4}), "hit")
	_check(s.entities[2]["action"] == "hit", "hit set")
	_check(s.apply({"type":"move","map_epoch":7,"entity_id":2,
		"movement_seq":2,"start_tick":110,"duration_ticks":10,"start_x":110.0,
		"start_y":20.0,"end_x":210.0,"end_y":20.0,"facing":0}), "move while hit")
	_check(s.entities[2]["action"] == "hit", "move must not replace hit")
	_check(s.apply({"type":"stop","map_epoch":7,"entity_id":2,
		"movement_seq":3,"x":120.0,"y":20.0,"facing":3}), "stop barrier")
	_check(not s.apply({"type":"move","map_epoch":7,"entity_id":2,
		"movement_seq":2,"start_tick":110,"duration_ticks":10}), "reject stale move")
	_check(is_equal_approx(s.visual_position(2, 150.0).x, 120.0), "stop authoritative")
	_check(s.apply({"type":"despawn","map_epoch":7,"entity_id":2,"entity_revision":2}), "despawn")
	_check(not s.apply({"type":"spawn","map_epoch":7,"entity_id":2,
		"entity_revision":1}), "stale spawn cannot resurrect")
	_check(not s.apply({"type":"move","map_epoch":6,"entity_id":2,
		"movement_seq":4}), "old map epoch blocked")
	_check(s.entities.is_empty(), "no ghosts")
	if failures > 0:
		printerr("WorldState tests FAILED: ", failures)
		quit(1)
	else:
		print("WorldState tests PASS: epoch, movement, facing, hit and despawn barriers")
		quit(0)
