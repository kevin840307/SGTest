extends RefCounted
## Shared loader for small hand-authored UI PNG files. Editor import cache
## is optional: pure Godot --script CI tests can decode sources directly.
static var _textures: Dictionary = {}

static func clear_cache() -> void:
	# Called after the only UI root exits; do not retain global GPU textures.
	_textures.clear()


static func get_texture(path: String) -> Texture2D:
	if path.is_empty() or not path.begins_with("res://"):
		return null
	if _textures.has(path):
		return _textures[path]
	var texture: Texture2D
	if ResourceLoader.exists(path):
		texture = load(path)
	elif FileAccess.file_exists(path):
		var bitmap := Image.new()
		var err := bitmap.load(path)
		if err == OK:
			texture = ImageTexture.create_from_image(bitmap)
	_textures[path] = texture
	return texture
