extends SceneTree

# Validate the generated visual catalog in the same image decoder Godot will use.
# This does not validate legacy animation timing or visual alignment.
func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 1:
		printerr("Usage: godot --headless --script tools/godot_enemy_catalog_probe.gd -- enemy_catalog.json")
		quit(2)
		return
	var path: String = args[0]
	var catalog: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not catalog is Dictionary or not catalog.has("groups"):
		printerr("Invalid enemy catalog: ", path)
		quit(2)
		return
	var atlas_root := path.get_base_dir()
	var groups := 0
	var actions := 0
	var logical_directions := 0
	var steps := 0
	for group in catalog.groups:
		var dimensions: Array[Vector2i] = []
		for page in group.pages:
			var image := Image.new()
			if image.load(atlas_root.path_join(page)) != OK:
				_fail("Cannot decode atlas page: " + str(page))
				return
			dimensions.append(image.get_size())
		for action in group.actions:
			if action.directions.size() != 8:
				_fail("Action does not have eight logical facings")
				return
			for direction in action.directions:
				for frame in direction.frames:
					var page_index: int = frame.page
					if page_index < -1 or page_index >= dimensions.size():
						_fail("Frame refers to invalid page")
						return
					if page_index >= 0:
						var rect: Array = frame.rect
						var size := dimensions[page_index]
						if rect.size() != 4 or rect[0] < 0 or rect[1] < 0 or rect[2] <= 0 or rect[3] <= 0 or rect[0] + rect[2] > size.x or rect[1] + rect[3] > size.y:
							_fail("Frame region lies outside atlas page")
							return
					steps += 1
				logical_directions += 1
			actions += 1
		groups += 1
	print(JSON.stringify({"groups": groups, "actions": actions,
		"logical_directions": logical_directions, "steps": steps,
		"timing_verified": false, "visual_alignment_verified": false}))
	quit(0)


func _fail(message: String) -> void:
	printerr(message)
	quit(1)
