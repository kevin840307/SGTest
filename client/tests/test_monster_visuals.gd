extends SceneTree

func _initialize() -> void:
	var visuals := preload("res://scripts/rendering/monster_visuals.gd").new()
	var path := "res://assets/maps/13/population.json"
	var population: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(path))
	assert(population.definitions.size() == 9)
	var first: Dictionary = population.definitions[0]
	assert(str(first.atlas).begins_with("Enemy/"))
	assert(visuals.add("res://assets",first) == OK)
	var first_bytes := visuals.rgba_bytes
	var alias := first.duplicate(true)
	alias.id = 999
	assert(visuals.add("res://assets",alias) == OK)
	assert(visuals.definitions.size() == 2 and visuals.shared.size() == 1)
	assert(visuals.rgba_bytes == first_bytes)
	assert(visuals.resolve(int(first.id)).atlas == visuals.resolve(999).atlas,
		"two definitions using the same monster visual must share one AssetStore identity")
	visuals.reset()
	var expected_paths: Dictionary = {}
	for row in population.definitions:
		assert(str(row.atlas) == "Enemy/%s/atlas.json" % str(row.sprite).to_lower())
		expected_paths[str(row.atlas)] = true
		assert(visuals.add("res://assets",row) == OK)
	assert(visuals.definitions.size() == 9)
	assert(expected_paths.size() > 1)
	assert(visuals.shared.size() == expected_paths.size(),
		"different monster visuals must remain independent atlases")
	for row in population.definitions:
		var visual: Dictionary = visuals.resolve(int(row.id))
		assert(visual.prefix == str(row.sprite))
		for action in ["NS","NW","FA","NH","ND"]:
			for direction in range(1,9):
				assert(not visual.atlas.sample("%s_%s%d" % [visual.prefix,action,direction],0.1).is_empty())
	visuals.reset()
	print("map palette: independent monster assets / same-visual reuse / 360 action-direction checks passed")
	quit()
