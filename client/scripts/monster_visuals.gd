extends RefCounted

# One immutable atlas per visual, one definition lookup per monster. Reset at
# map boundaries so the world's full creature catalog never stays resident.
const ATLAS = preload("res://scripts/entity_atlas.gd")
var definitions: Dictionary = {}
var shared: Dictionary = {}
var rgba_bytes := 0

func reset() -> void:
	definitions.clear()
	shared.clear()
	rgba_bytes = 0

func add(root: String, definition: Dictionary) -> Error:
	var id := int(definition.id)
	var sprite := str(definition.sprite)
	if definitions.has(id) or not sprite.is_valid_identifier():
		return ERR_INVALID_DATA
	if not shared.has(sprite):
		var atlas := ATLAS.new()
		var error: Error = atlas.open(root.path_join(str(definition.atlas)))
		if error != OK:
			return error
		shared[sprite] = {"atlas":atlas,"prefix":sprite}
		for page in atlas.pages:
			rgba_bytes += page.get_width()*page.get_height()*4
	definitions[id] = shared[sprite]
	return OK

func resolve(id: int) -> Dictionary:
	return definitions.get(id,{})
