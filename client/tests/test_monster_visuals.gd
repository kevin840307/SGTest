extends SceneTree

func _initialize() -> void:
	var visuals := preload("res://scripts/monster_visuals.gd").new()
	var path := "res://assets/generated/maps/13/population.json"
	var population: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(path))
	assert(population.definitions.size() == 9)
	var first: Dictionary = population.definitions[0]
	assert(visuals.add("res://assets/generated",first) == OK)
	var first_bytes := visuals.rgba_bytes
	var alias := first.duplicate(true)
	alias.id = 999
	assert(visuals.add("res://assets/generated",alias) == OK)
	assert(visuals.definitions.size() == 2 and visuals.shared.size() == 1)
	assert(visuals.rgba_bytes == first_bytes)
	assert(visuals.resolve(int(first.id)).atlas == visuals.resolve(999).atlas)
	visuals.reset()
	for row in population.definitions:
		assert(visuals.add("res://assets/generated",row) == OK)
	assert(visuals.definitions.size() == 9)
	assert(visuals.shared.size() == 1,"same map/lifetime must be one runtime bundle")
	for row in population.definitions:
		var visual: Dictionary = visuals.resolve(int(row.id))
		assert(visual.prefix == str(row.sprite))
		for action in ["NS","NW","FA","NH","ND"]:
			for direction in range(1,9):
				assert(not visual.atlas.sample("%s_%s%d" % [visual.prefix,action,direction],0.1).is_empty())
	visuals.reset()
	print("map palette: 9 visuals / 360 action-direction combinations / one lifecycle bundle passed")
	quit()
