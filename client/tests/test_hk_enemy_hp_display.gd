extends SceneTree
## UI-084 diagnostic: original HK SPR geometry and trusted HP presentation.
## This is not a substitute for 800x600 native A/B and actual player combat.
const WorldView = preload("res://world/world_view.gd")
const EnemyHP = preload("res://ui/hk_enemy_hp_display.gd")
var failures: Array[String] = []

func check(condition: bool, detail: String) -> void:
	if not condition:
		failures.append(detail)
		push_error("HK_ENEMY_HP_FAIL " + detail)

func _initialize() -> void:
	run.call_deferred()

func run() -> void:
	var view := WorldView.new()
	root.add_child(view)
	var display = view.enemy_hp_display
	check(not display._panel.visible, "display begins hidden")
	check(display._panel.mouse_filter == Control.MOUSE_FILTER_IGNORE and
		display._bar.mouse_filter == Control.MOUSE_FILTER_IGNORE and
		display._badge.mouse_filter == Control.MOUSE_FILTER_IGNORE,
		"original controls cannot steal map mouse input")
	check(display._panel.size == Vector2(75,21) and
		display._bar.position == Vector2(20,5) and display._bar.size == Vector2(49,5) and
		display._badge.position == Vector2(5,3) and display._badge.size == Vector2(14,14),
		"HK 084 ITF rects, original image sizes")
	check(EnemyHP.badge_index(14,10) == 0 and EnemyHP.badge_index(13,10) == 1 and
		EnemyHP.badge_index(10,10) == 2 and EnemyHP.badge_index(7,10) == 2 and
		EnemyHP.badge_index(6,10) == 3 and EnemyHP.badge_index(4,10) == 3 and
		EnemyHP.badge_index(3,10) == 4 and EnemyHP.badge_index(0,10) == -1,
		"original enemy minus player level badge branch thresholds")
	check(display.show_health(Vector2(110,80),50,100,14,10),
		"source-backed HK display opens for authoritative living HP")
	check(display._panel.visible and display._panel.position == Vector2(110,80) and
		display._background.texture.get_size() == Vector2(75,21) and
		display._bar.texture_progress.get_size() == Vector2(107,10) and
		display._badge.texture.get_size() == Vector2(14,14) and
		display._bar.value == 50 and display._bar.max_value == 100,
		"original PNGs and authoritative 50/100 HP")
	check(display.show_health(Vector2(120,85),100,100,3,10) and
		display._badge.texture != null and display._bar.value == 100,
		"target refresh can display new max HP and lower-level badge")
	check(not display.show_health(Vector2(120,85),0,100,3,10) and
		not display._panel.visible, "dead monster HP clears HUD")
	check(not display.show_health(Vector2.INF,100,100,3,10) and
		not display._panel.visible, "invalid off-world coordinates fail closed")
	display.clear_target()
	check(not display._panel.visible and display._badge.texture == null,
		"despawn or map cleanup releases target")
	view.free()
	if failures.is_empty():
		print("HK_ENEMY_HP_DIAGNOSTIC_PASS original_spr=22 controls=2146_2147 " +
			"badge_branches=5 authoritative_hp=true death_clear=true")
	quit(0 if failures.is_empty() else 1)
