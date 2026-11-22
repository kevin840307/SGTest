extends SceneTree
## Subpixel 2D camera movement regression independent of private map assets.
const Fixture = preload("res://tests/world_camera_fixture.gd")
var failures: Array[String] = []

func _initialize() -> void:
	Fixture.run(Callable(self,"check"))
	if failures.is_empty():
		print("WORLD_CAMERA_PASS subpixel_pan=true exact_world_screen_inverse=true render_rate_independent=true clamps=true")
	quit(0 if failures.is_empty() else 1)

func check(ok: bool, message: String) -> void:
	if not ok:
		failures.append(message)
		push_error("WORLD_CAMERA_FAIL " + message)
