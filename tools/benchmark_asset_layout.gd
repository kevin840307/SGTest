extends SceneTree

# Headless CPU decode comparison only. No GPU upload, rendering or draw calls.
func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 1:
		printerr("Usage: godot --headless --script tools/benchmark_asset_layout.gd -- folder")
		quit(2)
		return
	var folder: String = args[0]
	var files := DirAccess.get_files_at(folder)
	var manifests: Array[String] = []
	for name in files:
		if name.ends_with(".json") and not name.ends_with(".act.json"):
			manifests.append(name)
	manifests.sort()
	var timings: Array[float] = []
	var page_count := 0
	var frame_count := 0
	var rgba_bytes := 0
	for repeat in 5:
		var images: Array[Image] = []
		var start := Time.get_ticks_usec()
		page_count = 0
		frame_count = 0
		rgba_bytes = 0
		for name in manifests:
			var document: Variant = JSON.parse_string(FileAccess.get_file_as_string(folder.path_join(name)))
			if not document is Dictionary:
				printerr("Invalid JSON: ", name)
				quit(1)
				return
			frame_count += document.frames.size()
			for page in document.pages:
				var image := Image.new()
				if image.load(folder.path_join(page)) != OK:
					printerr("Cannot load: ", page)
					quit(1)
					return
				images.append(image)
				page_count += 1
				rgba_bytes += image.get_width() * image.get_height() * 4
		timings.append(float(Time.get_ticks_usec() - start) / 1000.0)
		images.clear()
	timings.sort()
	print(JSON.stringify({"manifests": manifests.size(), "pages": page_count,
		"frames": frame_count, "rgba_area_bytes": rgba_bytes,
		"decode_median_ms": timings[2], "decode_trials_ms": timings}))
	quit(0)
