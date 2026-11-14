extends SceneTree

func _initialize() -> void:
	var visuals := preload("res://scripts/monster_visuals.gd").new()
	var definition := {"id":1,"sprite":"EN004A","atlas":"enemy/en004a.json"}
	assert(visuals.add("res://assets/generated",definition) == OK)
	var first_bytes: int = visuals.rgba_bytes
	var alias := definition.duplicate()
	alias.id = 2
	assert(visuals.add("res://assets/generated",alias) == OK)
	assert(visuals.definitions.size() == 2 and visuals.shared.size() == 1)
	assert(visuals.rgba_bytes == first_bytes)
	assert(visuals.resolve(1).atlas == visuals.resolve(2).atlas)
	assert(visuals.resolve(999).is_empty())
	assert(visuals.add("res://assets/generated",alias) == ERR_INVALID_DATA)
	visuals.reset()
	assert(visuals.definitions.is_empty() and visuals.shared.is_empty() and visuals.rgba_bytes == 0)
	var path := "res://assets/generated/maps/13/population.json"
	var population: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(path))
	for row in population.definitions:
		assert(visuals.add("res://assets/generated",row) == OK)
	assert(visuals.definitions.size() == 9 and visuals.shared.size() == 9)
	for row in population.definitions:
		var visual: Dictionary = visuals.resolve(int(row.id))
		for action in ["NS","NW","FA","NH","ND"]:
			for direction in range(1,9):
				assert(not visual.atlas.sample("%s_%s%d" % [visual.prefix,action,direction],0.1).is_empty())
	visuals.reset()
	print("map palette: 9 visuals / 360 action-direction combinations and shared lifetime passed")
	quit()
