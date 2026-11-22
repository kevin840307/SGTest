extends SceneTree
## Isolated regression for the exact Recovered animated cursor pack.
const SharedGameCursor = preload("res://ui/shared_game_cursor.gd")

func _fail(cursor: Node, reason: String) -> void:
	cursor.queue_free()
	push_error("SHARED_GAME_CURSOR_FAIL "+reason)
	quit(1)

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var cursor = SharedGameCursor.new()
	root.add_child(cursor)
	await process_frame
	if not cursor.using_recovered_cursor():
		_fail(cursor,"local Recovered cursor pack was not loaded")
		return
	var frames: SpriteFrames = cursor._cursor_visual.sprite_frames
	var expected_counts := {
		"normal":10,
		"attack":6,
		"check":6,
		"pick":5,
		"player":6,
		"skill":6,
		"autorun":10,
	}
	for animation_name in expected_counts:
		if not frames.has_animation(animation_name):
			_fail(cursor,"missing Recovered cursor animation: "+animation_name)
			return
		if frames.get_frame_count(animation_name) != int(expected_counts[animation_name]):
			_fail(cursor,"Recovered cursor frame mismatch: "+animation_name)
			return
	cursor.set_mode("attack")
	if cursor._mode != "attack" or cursor._cursor_visual.animation != "attack":
		_fail(cursor,"monster hover did not select C_ATTACK")
		return
	cursor.set_mode("inspect")
	if cursor._cursor_visual.animation != "check":
		_fail(cursor,"NPC hover did not select C_CHECK")
		return
	cursor.set_mode("pickup")
	if cursor._cursor_visual.animation != "pick":
		_fail(cursor,"ground drop hover did not select C_TAKE")
		return
	cursor.set_mode("player")
	if cursor._cursor_visual.animation != "player":
		_fail(cursor,"player hover did not select C_PLAYER")
		return
	cursor.set_mode("skill")
	if cursor._cursor_visual.animation != "skill":
		_fail(cursor,"skill target did not select C_SKILL")
		return
	cursor.set_mode("move")
	if cursor._cursor_visual.animation != "autorun" or not cursor._cursor_visual.visible:
		_fail(cursor,"latched autorun did not select C_AUTOWALK")
		return
	cursor.set_mode("normal")
	if cursor._cursor_visual.animation != "normal":
		_fail(cursor,"ordinary UI did not restore animated C_NORMAL cursor")
		return
	# Headless Godot has no native window, so mouse visibility itself is only
	# validated in the live GUI pass. The isolated test validates presentation.
	cursor.restore_pointer()
	cursor.queue_free()
	await process_frame
	print("SHARED_GAME_CURSOR_PASS recovered_exact=true normal=10 attack=6 check=6 take=5 player=6 skill=6 autorun=10")
	quit(0)
