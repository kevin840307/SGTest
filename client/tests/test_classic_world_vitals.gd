extends SceneTree
## Classic HK HUD must gate HP and MP readiness independently.
const ClassicWorldShell = preload("res://ui/classic_world_shell.gd")

func _initialize() -> void:
	var shell = ClassicWorldShell.new()
	shell._hp_label = Label.new()
	shell._mp_label = Label.new()
	shell._level_label = Label.new()
	shell._stats = {
		"player_profile_ready": true,
		"player_level": 7,
		"player_current_hp": 75,
		"player_max_hp": 100,
		"player_current_mp": 0,
		"player_max_mp": 0,
	}
	shell._refresh_hud()
	if shell._hp_label.text != "75 / 100":
		push_error("CLASSIC_WORLD_VITALS_FAIL HP readiness/value")
		quit(1)
		return
	if shell._mp_label.text != "—":
		push_error("CLASSIC_WORLD_VITALS_FAIL MP reused HP readiness")
		quit(1)
		return
	if shell._level_label.text != "7":
		push_error("CLASSIC_WORLD_VITALS_FAIL level")
		quit(1)
		return

	shell._stats["player_current_mp"] = 12
	shell._stats["player_max_mp"] = 20
	shell._refresh_hud()
	if shell._mp_label.text != "12 / 20":
		push_error("CLASSIC_WORLD_VITALS_FAIL MP authoritative value")
		quit(1)
		return

	shell._hp_label.free()
	shell._mp_label.free()
	shell._level_label.free()
	shell.free()
	print("CLASSIC_WORLD_VITALS_PASS independent_hp_mp_readiness=true")
	quit(0)
