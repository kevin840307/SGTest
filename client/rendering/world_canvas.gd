extends Node2D
## Draw an already ordered presentation snapshot; texture references are shared.
var _commands: Array = []


func present(commands: Array) -> void:
	_commands = commands
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	queue_redraw()


func clear() -> void:
	_commands = []
	queue_redraw()


func _draw() -> void:
	for command in _commands:
		draw_texture_rect_region(command.texture, command.destination, command.uv, command.modulate, false, true)
