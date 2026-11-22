extends RefCounted
## Recovered2 item photos, private and lazy. No game-item authority or atlas.
## Only the currently shown UI rows request a texture. An LRU bound keeps
## texture memory stable when large inventories are scrolled.
const ROOT := "res://ui/private_recovered2/item_photos"
const MAX_VISIBLE_PHOTOS := 96
var _textures: Dictionary = {}
var _recent: Array[int] = []

func photo_for_icon(source_icon_id: int) -> Texture2D:
	# Original Recovered2 item UI calls get_photo(icon_id - 1).
	var index := source_icon_id - 1
	if index < 0 or index > 9999:
		return null
	if _textures.has(index):
		_touch(index)
		return _textures[index]
	var path := ROOT.path_join("Photo%04d.png" % index)
	if not FileAccess.file_exists(path):
		return null
	var texture: Texture2D
	# Prefer the imported resource for desktop/export. Use the decoded PNG
	# only when standalone --script tests have no Godot import cache.
	if ResourceLoader.exists(path):
		texture = load(path) as Texture2D
	if texture == null:
		var image := Image.new()
		if image.load(path) != OK:
			return null
		texture = ImageTexture.create_from_image(image)
	if texture == null:
		return null
	_textures[index] = texture
	_touch(index)
	while _recent.size() > MAX_VISIBLE_PHOTOS:
		_textures.erase(_recent.pop_front())
	return texture

func _touch(index: int) -> void:
	_recent.erase(index)
	_recent.append(index)

func photo_for_item(item_definition_id: int, catalog) -> Texture2D:
	return photo_for_icon(int(catalog.icon_id_for(item_definition_id))) if catalog != null else null

func count() -> int:
	return _textures.size()

func clear() -> void:
	_textures.clear()
	_recent.clear()
