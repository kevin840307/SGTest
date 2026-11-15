extends Node2D
## Comparison only; consumes exactly the same commands as world_canvas.gd.


func present(commands: Array) -> void:
	while get_child_count() > commands.size():
		var child := get_child(get_child_count() - 1)
		remove_child(child)
		child.free()
	while get_child_count() < commands.size():
		var sprite := Sprite2D.new()
		add_child(sprite)
	for index in commands.size():
		var command: Dictionary = commands[index]
		var sprite := get_child(index) as Sprite2D
		sprite.texture = command.texture
		sprite.centered = false
		sprite.region_enabled = true
		sprite.region_filter_clip_enabled = true
		sprite.region_rect = command.uv
		sprite.position = command.destination.position
		sprite.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST


func clear() -> void:
	for child in get_children():
		remove_child(child)
		child.free()
