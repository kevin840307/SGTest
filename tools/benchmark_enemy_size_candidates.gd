extends SceneTree

# Batch CPU decode/ImageTexture construction comparison. Headless values are not
# GPU upload spikes, runtime VRAM, draw calls, or frame times.
func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 4:
		printerr("Usage: godot --headless --script tools/benchmark_enemy_size_candidates.gd -- output.json atlas1024 atlas2048 atlas4096")
		quit(2)
		return
	var output: String = args[0]
	var roots := [args[1], args[2], args[3]]
	var manifests: Array[Dictionary] = []
	for root_path in roots:
		var by_group := {}
		for name in DirAccess.get_directories_at(root_path.path_join("Enemy")):
			var manifest_path: String = root_path.path_join("Enemy").path_join(name).path_join("group.atlas.json")
			var row: Variant = JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
			if not row is Dictionary or row.get("group_key", "") != "Enemy/" + name:
				printerr("Invalid Enemy atlas manifest: ", manifest_path)
				quit(1)
				return
			by_group[name] = {"path": manifest_path, "manifest": row}
		manifests.append(by_group)
	var names: Array = manifests[0].keys()
	names.sort()
	if names.is_empty() or manifests[1].keys().size() != names.size() or manifests[2].keys().size() != names.size():
		printerr("Candidate roots do not contain the same Enemy groups")
		quit(1)
		return
	var results := []
	for name in names:
		var reference: Dictionary = manifests[0][name].manifest
		var rows := []
		for index in 3:
			if not manifests[index].has(name):
				printerr("Candidate missing Enemy group: ", name)
				quit(1)
				return
			var entry: Dictionary = manifests[index][name]
			var manifest: Dictionary = entry.manifest
			if JSON.stringify(manifest.sources) != JSON.stringify(reference.sources) or manifest.frames.size() != reference.frames.size():
				printerr("Candidate source mismatch: ", name)
				quit(1)
				return
			var decode_trials: Array[float] = []
			var texture_trials: Array[float] = []
			var rgba_bytes := 0
			var png_bytes := 0
			for trial in 3:
				var images: Array[Image] = []
				var start := Time.get_ticks_usec()
				for page_name in manifest.pages:
					var page_path: String = entry.path.get_base_dir().path_join(page_name)
					var image := Image.new()
					if image.load(page_path) != OK:
						printerr("Cannot decode: ", page_path)
						quit(1)
						return
					images.append(image)
					if trial == 0:
						rgba_bytes += image.get_width() * image.get_height() * 4
						var file := FileAccess.open(page_path, FileAccess.READ)
						png_bytes += file.get_length()
				decode_trials.append(float(Time.get_ticks_usec() - start) / 1000.0)
				var textures: Array[ImageTexture] = []
				start = Time.get_ticks_usec()
				for image in images:
					textures.append(ImageTexture.create_from_image(image))
				texture_trials.append(float(Time.get_ticks_usec() - start) / 1000.0)
				textures.clear()
				images.clear()
			decode_trials.sort()
			texture_trials.sort()
			rows.append({"side": manifest.atlas_side, "pages": manifest.pages.size(),
				"rgba_area_bytes": rgba_bytes, "png_bytes": png_bytes,
				"decode_median_ms": decode_trials[1],
				"texture_create_median_ms": texture_trials[1]})
		results.append({"enemy": name, "candidates": rows})
	var document := {"version": 1, "headless": true, "source_groups": names.size(),
		"trials_per_candidate": 3, "groups": results}
	var file := FileAccess.open(output, FileAccess.WRITE)
	if file == null:
		printerr("Cannot write report: ", output)
		quit(1)
		return
	file.store_string(JSON.stringify(document))
	file.close()
	print(JSON.stringify({"groups": names.size(), "output": output, "headless": true}))
	quit(0)
