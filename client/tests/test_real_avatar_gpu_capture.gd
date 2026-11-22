extends SceneTree
## Windows GPU regression: capture actual displayed pixels using original
## assets.pck, not just Canvas drawing commands or dummy headless renderer.
const WorldView = preload("res://world/world_view.gd")

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var arguments := OS.get_cmdline_user_args()
	if arguments.size() != 2:
		push_error("usage -- <private assets.pck> <output.png>")
		quit(2)
		return
	root.size = Vector2i(1140,660)
	var world := WorldView.new()
	world.show_players = true
	root.add_child(world)
	world.resize_view(Vector2i(1140,660))
	if world.open_pack(arguments[0]) != OK or world.request_map(15) != OK:
		push_error("avatar GPU original pack/map did not load")
		quit(1)
		return
	var p := Vector2(7872,11296)
	var own := {
		"actor_id":27,"connection_epoch":1,"entity_map_epoch":1,
		"x":p.x,"y":p.y,"server_tick":1,"facing":6,
		"moving":true,"movement_action_sequence":1,
		"appearance":{"race":0,"hair_index":1,"hair_color":1,
			"body_index":1,"leg_index":1,"sleeve_color":2}
	}
	world.set_player_snapshot({
		"world_ready":true,"map_epoch":1,"map_id":15,"actor_id":27,
		"x":p.x,"y":p.y,"facing":6,"self_state":own,
		"entities":{},"monsters":{},"player_identities":{}
	})
	var deadline := Time.get_ticks_msec()+30000
	while world.phase != "ready" and Time.get_ticks_msec()<deadline:
		await process_frame
	if world.phase != "ready":
		push_error("avatar GPU load failed phase="+world.phase)
		quit(1)
		return
	for frame in 55:
		world.players.set_local_visual(27,p+Vector2(frame*1.3,0),true,1+(frame/7)%8,Time.get_ticks_usec())
		world._players_dirty = true
		await process_frame
	await RenderingServer.frame_post_draw
	var on_img := root.get_texture().get_image()
	if on_img == null or on_img.is_empty():
		push_error("GPU screenshot unavailable")
		quit(1)
		return
	var avatar_commands: Array = world._canvas._commands.filter(func(c):return str(c.actor_id)=="27")
	if avatar_commands.size() < 4:
		push_error("Own paper doll has no draw commands "+str(world.draw_list.invalid_actor_examples))
		quit(1)
		return
	var bounds: Rect2 = avatar_commands[0].destination.abs()
	for c in avatar_commands:
		bounds = bounds.merge((c.destination as Rect2).abs())
	var output := arguments[1]
	var saved := on_img.save_png(output)
	if saved != OK:
		push_error("failed to save screenshot")
		quit(1)
		return
	# Remove *only* own presentation for a second on-screen GPU render.
	# Require measurable opaque pixels in the true sprite rectangle; a
	# nonempty command array alone previously passed with invisible avatars.
	world.players._actors.erase(27)
	world.players.clear_local_visual()
	world._players_dirty = true
	world.present()
	await process_frame
	await RenderingServer.frame_post_draw
	var off_img := root.get_texture().get_image()
	var origin: Vector2 = world.camera.view_rect().position
	var rect := Rect2(bounds.position-origin,bounds.size).grow(3).abs()
	var x0 := maxi(0,int(rect.position.x))
	var x1 := mini(on_img.get_width(),int(ceil(rect.end.x)))
	var y0 := maxi(0,int(rect.position.y))
	var y1 := mini(on_img.get_height(),int(ceil(rect.end.y)))
	var differences := 0
	for y in range(y0,y1):
		for x in range(x0,x1):
			var on: Color = on_img.get_pixel(x,y)
			var off: Color = off_img.get_pixel(x,y)
			if absf(on.r-off.r)+absf(on.g-off.g)+absf(on.b-off.b)>0.08:
				differences += 1
	print("GPU_AVATAR_PIXEL_DIFF pixels=",differences,
		" layers=",avatar_commands.size()," viewport=",root.size,
		" bounds=",bounds," origin=",origin," output=",output)
	world.queue_free()
	await process_frame
	quit(0 if differences>=80 else 1)
