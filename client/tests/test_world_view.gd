extends SceneTree
const Main = preload("res://app/main.gd")
const Fixture = preload("res://tests/world_camera_fixture.gd")
var app: Node
var failures: Array = []
var captures: Array = []
var pages := {}
var sources := {}
var output := ""


func _initialize() -> void:
	root.size = Vector2i(1280,720)
	RenderingServer.set_default_clear_color(Color.BLACK)
	run.call_deferred()


func check(condition: bool, message: String) -> void:
	if not condition:
		failures.append(message)
		push_error(message)


func wait_ready(id: int) -> void:
	var deadline := Time.get_ticks_msec()+30000
	while app.world_view.phase in ["map","palette"] and Time.get_ticks_msec()<deadline:
		await process_frame
	check(app.world_view.phase == "ready" and app.world_view.map_id == id,"Map request did not settle to latest target")


func key(code: int, pressed: bool) -> void:
	var event := InputEventKey.new()
	event.physical_keycode = code
	event.pressed = pressed
	root.push_input(event)


func mouse(button: int, pressed: bool, point: Vector2) -> void:
	var event := InputEventMouseButton.new()
	event.button_index = button
	event.pressed = pressed
	event.position = point
	root.push_input(event)


func serialized_commands(commands: Array) -> Array:
	var result := []
	for command in commands:
		var id := str(command.texture.get_instance_id())
		if not pages.has(id):
			var file := output.path_join("page_"+str(pages.size())+".png")
			var image: Image = command.texture.get_image()
			if image.is_compressed():
				check(image.decompress() == OK,"Texture decompression")
			check(image.save_png(file) == OK,"Texture reference save")
			pages[id] = file
		result.append({"index":command.index,"slot":command.slot,"frame":command.frame,
			"actor_id":command.actor_id,"alpha":int(round(command.modulate.a*255)),
			"texture_size":[command.texture.get_width(),command.texture.get_height()],
			"page":pages[id],"texture_source":sources[id],
			"destination":[command.destination.position.x,command.destination.position.y,command.destination.size.x,command.destination.size.y],
			"uv":[command.uv.position.x,command.uv.position.y,command.uv.size.x,command.uv.size.y]})
	return result


func run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2:
		quit(2)
		return
	output = args[1]
	Fixture.run(check)
	app = Main.new()
	app.auto_start = false
	root.add_child(app)
	check(app.world_view.open_pack(args[0]) == OK,"Open pack")
	check(app.world_view.request_map(15) == OK,"Initial map request")
	# Cancel metadata/texture work while a real load is already in flight.
	await process_frame
	var old_epoch: int = app.world_view.epoch
	app.world_view.request_map(13)
	await wait_ready(13)
	check(app.world_view.camera.pan(old_epoch,Vector2.ONE) != OK,"Stale map camera update accepted")
	var camera = app.world_view.camera
	var before: Vector2 = camera.snapshot().focus
	key(KEY_RIGHT,true)
	for i in 4:
		await process_frame
	key(KEY_RIGHT,false)
	check(camera.snapshot().focus.x > before.x,"Keyboard input did not pan")
	app._notification(Node.NOTIFICATION_WM_WINDOW_FOCUS_OUT)
	check(app._held.is_empty() and not app._dragging,"Focus loss did not stop input")
	mouse(MOUSE_BUTTON_MIDDLE,true,Vector2(600,500))
	before = camera.snapshot().focus
	var motion := InputEventMouseMotion.new()
	motion.position = Vector2(580,490)
	motion.relative = Vector2(-20,-10)
	root.push_input(motion)
	mouse(MOUSE_BUTTON_MIDDLE,false,Vector2(580,490))
	check(camera.snapshot().focus == before+Vector2(20,10),"Middle drag did not pan")
	# Toolbar must consume middle input, leaving world pan disabled.
	mouse(MOUSE_BUTTON_MIDDLE,true,Vector2(10,10))
	check(not app._dragging,"Toolbar leaked mouse input to world")
	mouse(MOUSE_BUTTON_MIDDLE,false,Vector2(10,10))
	for id in [15,13]:
		var button: Button = app.toolbar.get_child(0).get_node("Map"+str(id))
		var point := button.get_global_rect().get_center()
		mouse(MOUSE_BUTTON_LEFT,true,point)
		mouse(MOUSE_BUTTON_LEFT,false,point)
		await wait_ready(id)
	# Frame captures use real entry/presentation with toolbar hidden.
	app.toolbar.hide()
	for id in [15,13]:
		app.world_view.request_map(id)
		await wait_ready(id)
		for asset_id in app.world_view.draw_list._assets:
			var asset: Dictionary = app.world_view.draw_list._assets[asset_id]
			for page_id in asset.pages.size():
				sources[str(asset.pages[page_id].get_instance_id())] = [asset_id,page_id]
		for size in [Vector2i(1280,720),Vector2i(1001,701)]:
			root.size = size
			await process_frame
			for focus in ([Vector2(8840,8860),Vector2.ZERO,Vector2(15360,15360)] if id == 15 else [Vector2(5760,2760),Vector2.ZERO,Vector2(15360,15360)]):
				app.world_view.camera.set_focus(app.world_view.epoch,focus)
				await process_frame
				await RenderingServer.frame_post_draw
				var view: Rect2 = app.world_view.camera.view_rect()
				check(view.size == Vector2(size),"Window resize not applied")
				var file := output.path_join("capture_"+str(captures.size())+".png")
				check(root.get_texture().get_image().save_png(file) == OK,"Capture save")
				captures.append({"file":file,"map":id,"camera":[view.position.x,view.position.y],
					"size":[size.x,size.y],"inside":false,"timing":true,"elapsed_updates":app.world_view._last_update,
					"fade_updates":16,"actors":[],"commands":serialized_commands(app.world_view._canvas._commands)})
	# Same formal owner, repeated rapid requests. No parallel map owners accumulate.
	for i in 6:
		app.world_view.request_map(15)
		await process_frame
		app.world_view.request_map(13)
		await wait_ready(13)
	app.world_view.clear_map()
	var deadline := Time.get_ticks_msec()+30000
	while app.world_view.store.statistics().decode_jobs+app.world_view.store.statistics().texture_jobs>0 and Time.get_ticks_msec()<deadline:
		await process_frame
	var final: Dictionary = app.world_view.store.statistics()
	check(final.assets+final.scopes+final.rgba_bytes+final.decode_jobs+final.texture_jobs == 0,"Entry leaked resources")
	app.queue_free()
	await process_frame
	await process_frame
	var result := {"status":"PASS" if failures.is_empty() else "FAIL","failures":failures,"captures":captures,"final":final}
	var file := FileAccess.open(output.path_join("result.json"),FileAccess.WRITE)
	file.store_string(JSON.stringify(result))
	file.close()
	print("WORLD_VIEW_RESULT ",result.status)
	quit(0 if failures.is_empty() else 1)
