extends SceneTree

# Repeated atlas load/release probe. No game cache or scene-transition code exists yet.
func _find_manifests(folder: String, result: Array[String]) -> void:
	if not DirAccess.dir_exists_absolute(folder):
		return
	for name in DirAccess.get_files_at(folder):
		if name == "group.atlas.json":
			result.append(folder.path_join(name))
	for name in DirAccess.get_directories_at(folder):
		_find_manifests(folder.path_join(name), result)

func _load_textures(paths: Array[String]) -> Array[Texture2D]:
	var textures: Array[Texture2D] = []
	for path in paths:
		var document: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
		if not document is Dictionary:
			return []
		for page in document.pages:
			var image := Image.new()
			if image.load(path.get_base_dir().path_join(page)) != OK:
				return []
			textures.append(ImageTexture.create_from_image(image))
	return textures

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 1 or args.size() > 2:
		printerr("Usage: godot --script tools/benchmark_atlas_lifecycle.gd -- candidate-root [cycles]")
		quit(2)
		return
	var cycles := 10 if args.size() == 1 else int(args[1])
	if cycles < 2 or cycles > 100:
		printerr("cycles must be 2..100")
		quit(2)
		return
	var paths: Array[String] = []
	_find_manifests(args[0], paths)
	paths.sort()
	if paths.is_empty():
		printerr("No atlas manifests")
		quit(2)
		return
	for i in 3:
		await process_frame
	var baseline := int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var baseline_texture := int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var loaded: Array[int] = []
	var released: Array[int] = []
	var released_texture: Array[int] = []
	for cycle in cycles:
		var textures := _load_textures(paths)
		if textures.is_empty():
			printerr("Cannot load atlas pages")
			quit(1)
			return
		for i in 3:
			await process_frame
		loaded.append(int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)))
		textures.clear()
		for i in 3:
			await process_frame
		released.append(int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)))
		released_texture.append(int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)))
	print(JSON.stringify({"cycles": cycles, "groups": paths.size(),
		"baseline_video_bytes": baseline, "baseline_texture_bytes": baseline_texture,
		"loaded_video_bytes": loaded, "released_video_bytes": released,
		"released_texture_bytes": released_texture,
		"released_growth_bytes": released[-1] - released[0],
		"final_over_baseline_bytes": released[-1] - baseline,
		"renderer": RenderingServer.get_video_adapter_name()}))
	quit(0)
