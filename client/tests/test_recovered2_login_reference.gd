extends SceneTree
## Optional read-only source scene preview. No source gameplay/network scripts run.
func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if (args.size()<2 or args.size()>4) or DisplayServer.get_name()=="headless":
		quit(2)
		return
	var text := FileAccess.get_file_as_string(args[0])
	if text.is_empty():
		quit(2)
		return
	var lines := PackedStringArray()
	var uid := RegEx.new()
	uid.compile(" uid=\"[^\"]+\"")
	for line in text.split("\n"):
		if line.contains('type="Script"') or line.begins_with("script ="):
			continue
		line = uid.sub(line,"",true)
		line = line.replace("res://2.ogv","res://ui/private_recovered2/2.ogv").replace("res://ui/themes/paradise_window_theme.tres","res://ui/private_recovered2/paradise_window_theme.tres")
		lines.append(line)
	var path := "user://recovered2_login_source_reference.tscn"
	var file := FileAccess.open(path,FileAccess.WRITE)
	file.store_string("\n".join(lines))
	file.close()
	root.size = Vector2i(1400,900) if args.has("base-resolution") else Vector2i(1152,620)
	root.content_scale_size = Vector2i(1400,900)
	root.content_scale_mode = Window.CONTENT_SCALE_MODE_CANVAS_ITEMS
	root.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_KEEP
	var scene = load(path).instantiate()
	root.add_child(scene)
	var video = scene.get_node("IntroVideoWrapper/AspectRatioContainer/BackgroundVideo")
	if args.has("static-login"):
		video.hide()
	else:
		video.play()
	await create_timer(0.4).timeout
	await RenderingServer.frame_post_draw
	var saved := root.get_texture().get_image().save_png(args[1])
	scene.queue_free()
	await process_frame
	quit(0 if saved==OK else 1)
