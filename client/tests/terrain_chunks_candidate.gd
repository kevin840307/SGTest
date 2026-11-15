extends "res://tests/terrain_visible_chunks_candidate.gd"
## Test-only full-map policy; shares the exact visible-cache Mesh builder.

func configure(data: Data) -> void:
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	z_index = -100
	for cy in range(0, data.height, CHUNK):
		for cx in range(0, data.width, CHUNK):
			var item := _build_chunk(data, cx, cy)
			if item != null:
				add_child(item)

func set_view_rect(_view: Rect2) -> Error:
	return OK
