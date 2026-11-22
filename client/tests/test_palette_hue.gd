extends SceneTree
const PaletteHue = preload("res://player/palette_hue.gd")
const WorldCanvas = preload("res://world/world_canvas.gd")
var failures: Array[String] = []

func check(ok: bool, reason: String) -> void:
	if not ok:
		failures.append(reason)
		push_error("PALETTE_HUE_FAIL "+reason)

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	check(PaletteHue.HUE_DEGREES==[0.0,90.0,180.0,270.0,45.0,135.0,225.0,315.0],
		"original indexed hue table changed")
	var source := Image.create(2,1,false,Image.FORMAT_RGBA8)
	source.set_pixel(0,0,Color(1,0,0,1))
	source.set_pixel(1,0,Color(0.2,0.4,0.6,0.5))
	var zero := PaletteHue.shift_image(source,0)
	var ninety := PaletteHue.shift_image(source,1)
	var one_eighty := PaletteHue.shift_image(source,2)
	check(zero.get_pixel(0,0).is_equal_approx(Color(1,0,0,1)),
		"color index zero altered source")
	var p90 := ninety.get_pixel(0,0)
	var p180 := one_eighty.get_pixel(0,0)
	check(absf(p90.r-0.5)<0.01 and absf(p90.g-1.0)<0.01 and p90.b<0.01,
		"90-degree HSL shift differs from original policy")
	check(p180.r<0.01 and absf(p180.g-1.0)<0.01 and absf(p180.b-1.0)<0.01,
		"180-degree HSL shift differs from original policy")
	check(absf(ninety.get_pixel(1,0).a-source.get_pixel(1,0).a)<0.01,
		"palette shift changed alpha")

	var canvas := WorldCanvas.new()
	root.add_child(canvas)
	var texture := ImageTexture.create_from_image(source)
	var command := {
		"texture":texture,
		"destination":Rect2(0,0,2,1),
		"uv":Rect2(0,0,2,1),
		"modulate":Color.WHITE,
		"actor_id":"1",
		"palette_index":1,
	}
	var started := Time.get_ticks_usec()
	canvas.present([command])
	var submit_usec := Time.get_ticks_usec()-started
	check(submit_usec < 20000,
		"palette present blocked main thread instead of queuing worker")
	check(canvas.palette_pending_count()==1,
		"palette worker was not queued")
	var deadline := Time.get_ticks_msec()+5000
	while canvas.palette_pending_count()>0 and Time.get_ticks_msec()<deadline:
		await process_frame
	check(canvas.palette_pending_count()==0 and canvas.palette_texture_count()==1,
		"palette worker/cache did not complete")
	canvas.present([command])
	check(canvas.palette_pending_count()==0 and canvas.palette_texture_count()==1,
		"same texture/color did not reuse cache")

	var blue := Image.create(64,64,false,Image.FORMAT_RGBA8)
	blue.fill(Color(0,0,1,1))
	var blue_texture := ImageTexture.create_from_image(blue)
	var stale := command.duplicate(true)
	stale.texture=blue_texture
	stale.palette_index=2
	canvas.present([stale])
	canvas.clear()
	var stale_deadline := Time.get_ticks_msec()+5000
	while not canvas._palette_jobs.is_empty() and Time.get_ticks_msec()<stale_deadline:
		await process_frame
	check(canvas.palette_texture_count()==0,
		"stale map-generation palette result repopulated cache")
	canvas.queue_free()
	await process_frame
	if failures.is_empty():
		print("PALETTE_HUE_PASS original_hue_table=true alpha_preserved=true async=true shared_cache=true generation_cancel=true")
	quit(0 if failures.is_empty() else 1)
