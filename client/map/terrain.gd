extends Node2D
## Only rebuild Canvas commands when the visible tile rectangle changes.

const Data = preload("res://map/terrain_data.gd")
var _data: Data
var _cells := Rect2i()


func configure(data: Data) -> void:
	_data = data
	_cells = Rect2i()
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	z_index = -100
	queue_redraw()


func set_view_rect(view: Rect2) -> Error:
	var cells := _data.visible_cells(view)
	if cells != _cells:
		_cells = cells
		queue_redraw()
	return OK


func _draw() -> void:
	if _data == null:
		return
	var side := _data.tile_side
	for y in range(_cells.position.y, _cells.end.y):
		for x in range(_cells.position.x, _cells.end.x):
			var index := _data.cells[y * _data.width + x]
			if index >= 0:
				draw_texture_rect_region(_data.texture, Rect2(x * side, y * side, side, side), _data.regions[index], Color.WHITE, false, true)
