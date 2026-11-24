extends SceneTree

func fail(message:String)->void:
	push_error("MAIN_AUTORUN_LATCH_FAIL "+message)
	quit(1)

func _initialize()->void:
	var file:=FileAccess.open("res://app/main.gd",FileAccess.READ)
	if file==null:
		fail("cannot read main.gd")
		return
	var source:=file.get_as_text()
	file.close()
	var physics_call:="_advance_mouse_autorun_state(now,Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT))"
	var poll_call:="_mouse_autorun.poll_long_hold(now_msec)"
	var release_guard:="if _mouse_autorun.holding and not left_pressed:"
	if source.find(physics_call)<0:
		fail("physics loop is not wired to advance autorun state")
		return
	var helper:=source.find("func _advance_mouse_autorun_state")
	var poll:=source.find(poll_call,helper)
	var release:=source.find(release_guard,helper)
	if helper<0 or poll<helper or release<poll:
		fail("1000ms poll must run before release fallback")
		return
	print("MAIN_AUTORUN_LATCH_PASS physics_wiring=true threshold_poll_before_release=true")
	quit(0)
