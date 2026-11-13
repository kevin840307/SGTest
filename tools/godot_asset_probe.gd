extends SceneTree

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 1:
		printerr("Usage: godot --headless --script tools/godot_asset_probe.gd -- manifest.json")
		quit(2)
		return
	var manifest_path: String = args[0]
	var document: Variant = JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
	if not document is Dictionary or not document.has("frames") or not document.has("pages"):
		printerr("Invalid manifest")
		quit(1)
		return
	var base := manifest_path.get_base_dir()
	var images: Array[Image] = []
	for name in document.pages:
		var image := Image.new()
		if image.load(base.path_join(name)) != OK:
			printerr("Cannot load ", name)
			quit(1)
			return
		images.append(image)
	for frame in document.frames:
		var page: int = frame.page
		if page < 0:
			continue
		var rect: Array = frame.rect
		if page >= images.size() or rect[0] < 0 or rect[1] < 0 or rect[0] + rect[2] > images[page].get_width() or rect[1] + rect[3] > images[page].get_height():
			printerr("Frame outside texture")
			quit(1)
			return
	print("PASS: Godot loaded %d pages / %d frames" % [images.size(), document.frames.size()])
	quit(0)
