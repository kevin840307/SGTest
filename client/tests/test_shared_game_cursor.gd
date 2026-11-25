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
	# The HK item cursor follows real bag/equipment row hover, not the
	# adjacent empty row or an unrelated button with a similar tooltip.
	var inventory_rows := VBoxContainer.new()
	inventory_rows.name = "InventoryRows"
	inventory_rows.visible = false
	root.add_child(inventory_rows)
	var item_row := Button.new()
	item_row.tooltip_text = "Server item UID 42"
	inventory_rows.add_child(item_row)
	var child_photo := TextureRect.new()
	item_row.add_child(child_photo)
	if not cursor._is_item_row_control(item_row) or not cursor._is_item_row_control(child_photo):
		_fail(cursor,"actual inventory row/photo failed item cursor selection")
		return
	var equipped_rows := VBoxContainer.new()
	equipped_rows.name = "EquippedRows"
	equipped_rows.visible = false
	root.add_child(equipped_rows)
	var equipped_item := Button.new()
	equipped_item.tooltip_text = "Equipped item UID 41"
	equipped_rows.add_child(equipped_item)
	if not cursor._is_item_row_control(equipped_item):
		_fail(cursor,"equipped item failed HK item hover")
		return
	var empty_row := Button.new()
	inventory_rows.add_child(empty_row)
	var unrelated := Button.new()
	unrelated.tooltip_text = "Server item UID 42"
	root.add_child(unrelated)
	if cursor._is_item_row_control(empty_row) or cursor._is_item_row_control(unrelated):
		_fail(cursor,"empty or unrelated UI incorrectly selected item cursor")
		return
	cursor._item_hover_active = true
	cursor.set_mode("normal")
	if cursor._effective_mode() != "item" or cursor._native_shape_for_mode() != Input.CURSOR_POINTING_HAND:
		_fail(cursor,"normal world update overwrote bag item hover")
		return
	cursor.set_mode("attack")
	if cursor._effective_mode() != "attack":
		_fail(cursor,"bag hover overrode higher priority combat cursor")
		return
	cursor._item_hover_active = false
	cursor.set_mode("normal")
	inventory_rows.queue_free()
	equipped_rows.queue_free()
	unrelated.queue_free()
	await process_frame
	if not cursor.using_recovered_cursor():
		# Public CI cannot include the private Recovered sprite frames. The
		# production path must still load all nine published fallback icons,
		# map every input intent to the appropriate native cursor, and clean up.
		var fallback_icons := [
			"arrow.png", "point.png", "text.png", "drag.png",
			"attack.png", "inspect.png", "wait.png", "forbidden.png", "move.png"
		]
		for filename in fallback_icons:
			if not cursor._loaded.has("fallback/" + filename):
				_fail(cursor, "missing portable cursor icon: " + filename)
				return
		if cursor._loaded.size() != fallback_icons.size() or cursor._cursor_visual.visible:
			_fail(cursor, "fallback did not load nine icons or left animated cursor visible")
			return
		var shapes := {
			"normal":Input.CURSOR_ARROW,
			"attack":Input.CURSOR_CROSS,
			"inspect":Input.CURSOR_HELP,
			"pickup":Input.CURSOR_POINTING_HAND,
			"move":Input.CURSOR_MOVE,
			"wait":Input.CURSOR_WAIT,
			"forbidden":Input.CURSOR_FORBIDDEN,
			"player":Input.CURSOR_ARROW,
			"skill":Input.CURSOR_ARROW,
			"item":Input.CURSOR_POINTING_HAND,
		}
		for mode in shapes:
			cursor.set_mode(mode)
			# Headless Godot reports its current mouse shape as Arrow even when
			# set_default_cursor_shape changes; verify the mapping actually used
			# by _apply_mode rather than the unavailable window-system cursor.
			if cursor._mode != mode or cursor._native_shape_for_mode() != shapes[mode]:
				_fail(cursor, "portable cursor mode did not select native shape: " + mode)
				return
		if Input.mouse_mode != Input.MOUSE_MODE_VISIBLE:
			_fail(cursor, "native fallback pointer is hidden")
			return
		cursor.restore_pointer()
		cursor.queue_free()
		await process_frame
		if is_instance_valid(cursor) or Input.mouse_mode != Input.MOUSE_MODE_VISIBLE:
			push_error("SHARED_GAME_CURSOR_FAIL fallback cursor was not released")
			quit(1)
			return
		print("SHARED_GAME_CURSOR_FALLBACK_PASS portable_icons=9 native_modes=9 cleanup=true recovered_reference_not_verified=true")
		quit(0)
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
	# HK C_ITEM has six source-backed frames with (-1,-1) hotspot. A partial
	# private extraction cannot replace normal/attack/pick cursor rendering.
	var has_hk_item := FileAccess.file_exists("res://ui/private_ie_hk2/cursor/C_ITEM.0000.png")
	if has_hk_item:
		if not frames.has_animation("item") or frames.get_frame_count("item") != 6:
			_fail(cursor, "HK C_ITEM source frames not loaded as complete six-frame animation")
			return
		cursor.set_mode("item")
		if cursor._cursor_visual.animation != "item" or cursor._cursor_visual.offset != Vector2(-1, -1):
			_fail(cursor, "HK C_ITEM hotspot/animation parity")
			return
	else:
		if frames.has_animation("item"):
			_fail(cursor, "item animation fabricated without HK source")
			return
		cursor.set_mode("item")
		if cursor._cursor_visual.animation != "normal" or cursor._cursor_visual.offset != Vector2.ZERO:
			_fail(cursor, "missing HK C_ITEM failed to restore exact normal fallback")
			return
	cursor.set_mode("normal")
	if cursor._cursor_visual.offset != Vector2.ZERO:
		_fail(cursor, "HK C_ITEM offset leaked into normal cursor")
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
	print("SHARED_GAME_CURSOR_PASS recovered_exact=true normal=10 attack=6 check=6 take=5 player=6 skill=6 autorun=10 hk_item_6_frames=%s item_hotspot_verified=%s" % [str(has_hk_item), str(has_hk_item)])
	quit(0)
