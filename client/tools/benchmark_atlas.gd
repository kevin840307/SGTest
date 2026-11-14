extends SceneTree

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var path := args[0] if not args.is_empty() else "res://assets/bundles/map/13/sprites.json"
	var atlas := preload("res://scripts/entity_atlas.gd").new()
	var start := Time.get_ticks_usec()
	var error: Error = atlas.open(path)
	print(JSON.stringify({"path": path, "error": error, "decode_upload_ms": (Time.get_ticks_usec()-start)/1000.0,
		"pages": atlas.pages.size(), "static_memory_bytes": OS.get_static_memory_usage(),
		"renderer": RenderingServer.get_video_adapter_name()}))
	quit(0 if error == OK else 1)
