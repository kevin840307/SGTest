extends SceneTree

# CPU PNG decode and ImageTexture construction. Headless results are not GPU VRAM or frame time.
func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 1:
		printerr("Usage: godot --headless --script tools/benchmark_group_atlas.gd -- group.atlas.json")
		quit(2)
		return
	var path: String = args[0]
	var document: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not document is Dictionary or not document.has("pages") or not document.has("frames"):
		printerr("Invalid group atlas manifest: ", path)
		quit(2)
		return
	var folder: String = path.get_base_dir()
	var decode_trials: Array[float] = []
	var texture_trials: Array[float] = []
	var rgba_bytes := 0
	for repeat in 5:
		var images: Array[Image] = []
		var decode_start := Time.get_ticks_usec()
		for page in document.pages:
			var image := Image.new()
			if image.load(folder.path_join(page)) != OK:
				printerr("Cannot load page: ", page)
				quit(1)
				return
			images.append(image)
		decode_trials.append(float(Time.get_ticks_usec() - decode_start) / 1000.0)
		var textures: Array[ImageTexture] = []
		var texture_start := Time.get_ticks_usec()
		for image in images:
			textures.append(ImageTexture.create_from_image(image))
		texture_trials.append(float(Time.get_ticks_usec() - texture_start) / 1000.0)
		if repeat == 0:
			for image in images:
				rgba_bytes += image.get_width() * image.get_height() * 4
		textures.clear()
		images.clear()
	decode_trials.sort()
	texture_trials.sort()
	print(JSON.stringify({"group": document.group_key, "side": document.atlas_side,
		"sources": document.sources.size(), "frames": document.frames.size(),
		"pages": document.pages.size(), "rgba_area_bytes": rgba_bytes,
		"decode_median_ms": decode_trials[2], "texture_create_median_ms": texture_trials[2],
		"decode_trials_ms": decode_trials, "texture_trials_ms": texture_trials,
		"headless": true}))
	quit(0)
