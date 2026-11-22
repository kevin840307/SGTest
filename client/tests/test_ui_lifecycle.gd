extends SceneTree
## Exercise real scene ownership in offline, normal, and optional local-art UI.
const Main = preload("res://app/main.gd")

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	await process_frame
	var baseline := int(Performance.get_monitor(Performance.OBJECT_ORPHAN_NODE_COUNT))
	for mode in ["offline", "normal", "local_auth"]:
		if mode == "local_auth" and not FileAccess.file_exists("res://ui/private_recovered2/paradise_window_theme.tres"):
			print("UI_LIFECYCLE_LOCAL_ART_NOT_VERIFIED missing_private_theme=true")
			continue
		for iteration in 3:
			var app := Main.new()
			app.auto_start = false
			app.offline_map_only = mode == "offline"
			app.preview_auth_offline = mode == "local_auth"
			root.add_child(app)
			await process_frame
			if mode == "local_auth":
				var count := app.recovered2_auth._stage.get_child_count()
				if not app.recovered2_auth.attach(app.session_panel) or app.recovered2_auth._stage.get_child_count() != count:
					push_error("UI_LIFECYCLE_FAIL repeated attach rebuilt UI")
					quit(1)
					return
			app.queue_free()
			await process_frame
			await process_frame
			var remaining := int(Performance.get_monitor(Performance.OBJECT_ORPHAN_NODE_COUNT)) - baseline
			if remaining != 0:
				Node.print_orphan_nodes()
				push_error("UI_LIFECYCLE_FAIL mode=%s iteration=%d orphan_nodes=%d" % [mode,iteration,remaining])
				quit(1)
				return
	print("UI_LIFECYCLE_PASS repeated_scene_teardown=true no_orphan_nodes=true")
	quit(0)
