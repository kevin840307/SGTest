extends RefCounted
const Camera = preload("res://world/world_camera.gd")


static func run(check: Callable) -> void:
	var camera := Camera.new()
	check.call(camera.configure(1,Vector2i(2000,1800),Vector2i(1001,701),Vector2(1000,900)) == OK,"Camera configure")
	check.call(camera.view_rect() == Rect2(499,549,1001,701),"Odd viewport origin")
	for x in range(-50,51):
		for y in [-100.25,0.0,1200.75]:
			var point := Vector2(x*3.25,y)
			check.call(camera.world_to_screen(camera.screen_to_world(point)).is_equal_approx(point),"Screen/world inverse")
	var before := camera.snapshot()
	for epoch in [0,1]:
		check.call(camera.configure(epoch,Vector2i(2,2),Vector2i(1,1),Vector2.ZERO) != OK,"Old configure accepted")
	check.call(camera.resize(2,Vector2i(100,100)) != OK,"Wrong resize epoch")
	check.call(camera.resize(1,Vector2i.ZERO) != OK,"Zero viewport")
	check.call(camera.set_focus(0,Vector2.ZERO) != OK,"Wrong focus epoch")
	check.call(camera.pan(1,Vector2(NAN,0)) != OK,"Nonfinite pan")
	check.call(camera.snapshot() == before,"Invalid camera mutation")
	camera.set_focus(1,Vector2(-100,-200))
	check.call(camera.view_rect().position == Vector2.ZERO,"Top-left clamp")
	camera.set_focus(1,Vector2(90000,90000))
	check.call(camera.view_rect().end == Vector2(2000,1800),"Bottom-right clamp")
	camera.set_focus(1,Vector2(800,800))
	var focus: Vector2 = camera.snapshot().focus
	for i in 64:
		camera.pan(1,Vector2(0.125,0.25))
	check.call(camera.snapshot().focus == focus+Vector2(8,16),"Fractional pan discarded")
	for rate in [30,60,120]:
		camera.set_focus(1,Vector2(700,700))
		for step in rate:
			camera.pan(1,Vector2(600.0/rate,0))
		check.call(camera.snapshot().focus == Vector2(1300,700),"Camera pan depends on render rate")
	camera.resize(1,Vector2i(4000,3001))
	check.call(camera.view_rect() == Rect2(-1000,-601,4000,3001),"Small world must center with odd black border")
	camera.resize(1,Vector2i(1000,700))
	check.call(camera.snapshot().focus == Vector2(1000,900),"Resize lost valid center")
	camera.clear()
	check.call(camera.view_rect() == Rect2() and camera.set_focus(1,Vector2.ZERO) != OK,"Cleared camera active")
	check.call(camera.configure(1,Vector2i(10,10),Vector2i(2,2),Vector2.ZERO) != OK,"Clear lost tombstone")
	check.call(camera.configure(2,Vector2i(10,10),Vector2i(2,2),Vector2.ZERO) == OK,"New map epoch rejected")
