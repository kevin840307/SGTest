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
		var destination: Rect2 = command.destination
		if destination.size.x < 0:
			# draw_texture_rect_region normalizes negative destination sizes;
			# use a Canvas transform to mirror UVs as well as SPR geometry.
			draw_set_transform(Vector2(destination.position.x*2,0),0,Vector2(-1,1))
			destination.size.x = -destination.size.x
			draw_texture_rect_region(command.texture,destination,command.uv,command.modulate,false,true)
			draw_set_transform(Vector2.ZERO)
		else:
			draw_texture_rect_region(command.texture,destination,command.uv,command.modulate,false,true)
