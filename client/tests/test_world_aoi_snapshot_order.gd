extends SceneTree
# GP-017-01 / GP-013-01: stale full snapshots must not rewind the world view.
const WorldView = preload("res://world/world_view.gd")
var failures: Array[String] = []

func _initialize() -> void:
	run.call_deferred()

func check(condition: bool, message: String) -> void:
	if not condition:
		failures.append(message)
		push_error(message)

func run() -> void:
	var view := WorldView.new()
	root.add_child(view)
	# Presentation can receive snapshots while the map is loading. Avoid a
	# synthetic ready scene or triggering any asset/Server side effects.
	view.map_id = 13
	view.show_players = true
	view.phase = "map"
	var latest := {
		"world_ready": true, "map_id": 13, "map_epoch": 3,
		"last_aoi_tick": 20, "ground_drops": {7001: {"drop_id": 7001, "x": 2.0, "y": 3.0}}
	}
	view.set_player_snapshot(latest)
	check(view._player_snapshot.get("ground_drops",{}).has(7001),
		"fresh ground drop snapshot was not retained")
	var stale := latest.duplicate(true)
	stale.last_aoi_tick = 19
	stale.ground_drops = {}
	view.set_player_snapshot(stale)
	check(view._player_snapshot.get("ground_drops",{}).has(7001),
		"older same-map AOI snapshot erased a newer ground drop")
	var fresh_despawn := stale.duplicate(true)
	fresh_despawn.last_aoi_tick = 21
	view.set_player_snapshot(fresh_despawn)
	check(view._player_snapshot.get("ground_drops",{}).is_empty(),
		"newer ground drop despawn snapshot was not applied")
	var next_map := latest.duplicate(true)
	next_map.map_epoch = 4
	next_map.last_aoi_tick = 1
	view.set_player_snapshot(next_map)
	check(view._player_snapshot.get("ground_drops",{}).has(7001) and
		int(view._player_snapshot.map_epoch) == 4,
		"new map epoch with lower tick was incorrectly ignored")
	var old_map := stale.duplicate(true)
	old_map.map_epoch = 3
	old_map.last_aoi_tick = 99
	view.set_player_snapshot(old_map)
	check(view._player_snapshot.get("ground_drops",{}).has(7001) and
		int(view._player_snapshot.map_epoch) == 4,
		"older map epoch rewound a newer world view")
	view.queue_free()
	await process_frame
	if failures.is_empty():
		print("WORLD_AOI_SNAPSHOT_ORDER_PASS same_epoch_stale=true ground_drop_preserved=true new_epoch_tick_reset=true")
	quit(0 if failures.is_empty() else 1)
