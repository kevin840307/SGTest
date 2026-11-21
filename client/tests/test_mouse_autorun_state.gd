extends SceneTree
const Autorun = preload("res://movement/mouse_autorun_state.gd")

func fail(message: String) -> void:
	push_error("MOUSE_AUTORUN_STATE_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var state=Autorun.new()
	if state.terrain_press(100) or not state.holding or state.latched:
		fail("fresh terrain press")
		return
	if state.poll_long_hold(1099) or not state.holding or state.latched:
		fail("latched before exact 1000ms threshold")
		return
	if not state.poll_long_hold(1100) or not state.latched:
		fail("exact 1000ms hold did not latch")
		return
	if state.poll_long_hold(1200):
		fail("latch transition fired twice")
		return
	if not state.terrain_release(1200) or state.holding or not state.latched:
		fail("release lost latched autorun")
		return

	var actor:=Vector2(0,0)
	if not state.should_request(1200,actor,Vector2(100,0),false):
		fail("latched stopped route did not request cursor target")
		return
	state.note_request(Vector2(100,0),1200,true)
	if state.should_request(1599,actor,Vector2(100,0),false):
		fail("blocked/stopped route retried before 400ms")
		return
	if not state.should_request(1600,actor,Vector2(100,0),false):
		fail("blocked/stopped route did not retry at 400ms")
		return
	state.note_request(Vector2(100,0),1600,false)
	if state.should_request(1679,actor,Vector2(123,0),true):
		fail("cursor movement under 24px repathed")
		return
	if state.should_request(1679,actor,Vector2(124,0),true):
		fail("repath ignored 80ms throttle")
		return
	if not state.should_request(1680,actor,Vector2(124,0),true):
		fail("24px/80ms cursor repath missing")
		return
	if state.should_request(1680,Vector2(120,0),Vector2(124,0),false):
		fail("route inside 10px reach radius restarted")
		return

	var rejected=Autorun.new()
	rejected.terrain_press(0)
	rejected.poll_long_hold(1000)
	rejected.terrain_release(1000)
	rejected.defer_blocked_retry(1100)
	if rejected.should_request(1499,actor,Vector2(100,0),false):
		fail("rejected route bypassed 400ms retry guard")
		return
	if not rejected.should_request(1500,actor,Vector2(100,0),false):
		fail("rejected route did not reopen at retry boundary")
		return

	state.cancel()
	if state.active() or state.should_request(2000,actor,Vector2(200,0),false):
		fail("cancel retained autorun state")
		return
	if state.terrain_press(3000):
		fail("second fresh press reported prior latch")
		return
	if state.terrain_release(3500) or state.active():
		fail("short click became latched")
		return

	state.terrain_press(4000)
	state.poll_long_hold(5000)
	if not state.terrain_press(5100) or state.active():
		fail("first subsequent terrain press did not cancel latch")
		return

	print("MOUSE_AUTORUN_STATE_PASS hold_ms=1000 cursor_follow=true repath_distance=24 repath_ms=80 stopped_retry_ms=400 rejected_retry_ms=400 first_click_cancels=true render_fps_independent=true")
	quit(0)
