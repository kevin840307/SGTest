extends Node2D
## Test-only visible chunk policy, no cross-asset texture cache.
const Data = preload("res://rendering/terrain_data.gd")
const CHUNK := 32
const CORNERS := [Vector2(0, 0), Vector2(1, 0), Vector2(1, 1), Vector2(0, 1)]
const TRIANGLES := [0, 1, 2, 0, 2, 3]
var max_cached_chunks := 32
var _data: Data
var _chunks: Dictionary = {}
var _used: Dictionary = {}
var _stamp := 0

func configure(data: Data) -> void:
	_data = data
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	z_index = -100

func set_view_rect(view: Rect2) -> Error:
	var cells := _data.visible_cells(view)
	var wanted := {}
	_stamp += 1
	if cells.has_area():
		for y in range(cells.position.y / CHUNK, (cells.end.y - 1) / CHUNK + 1):
			for x in range(cells.position.x / CHUNK, (cells.end.x - 1) / CHUNK + 1):
				wanted[Vector2i(x,y)] = true
	if wanted.size() > max_cached_chunks:
		return ERR_OUT_OF_MEMORY
	for key in _chunks:
		if _chunks[key] != null:
			_chunks[key].visible = wanted.has(key)
	var missing := 0
	for key in wanted:
		if not _chunks.has(key):
			missing += 1
	# Evict before allocation, so even a fast jump does not overshoot the bound.
	while _chunks.size() + missing > max_cached_chunks:
		var oldest := Vector2i(-1,-1)
		var oldest_stamp := _stamp + 1
		for key in _used:
			if not wanted.has(key) and _used[key] < oldest_stamp:
				oldest = key
				oldest_stamp = _used[key]
		if oldest.x < 0:
			break
		if _chunks[oldest] != null:
			_chunks[oldest].free()
		_chunks.erase(oldest)
		_used.erase(oldest)
	for key in wanted:
		if not _chunks.has(key):
			var node := _build_chunk(_data, key.x * CHUNK, key.y * CHUNK)
			_chunks[key] = node
			if node != null:
				add_child(node)
		if _chunks[key] != null:
			_chunks[key].visible = true
		_used[key] = _stamp
	return OK

func _build_chunk(data: Data, cx: int, cy: int) -> MeshInstance2D:
	var vertices := PackedVector2Array()
	var uvs := PackedVector2Array()
	var indices := PackedInt32Array()
	var inverse_texture_size := Vector2.ONE / data.texture.get_size()
	for y in range(cy, mini(cy + CHUNK, data.height)):
		for x in range(cx, mini(cx + CHUNK, data.width)):
			var tile := data.cells[y * data.width + x]
			if tile < 0:
				continue
			var rect := data.regions[tile]
			var first := vertices.size()
			for corner in CORNERS:
				vertices.append(Vector2((x - cx) * data.tile_side, (y - cy) * data.tile_side) + corner * data.tile_side)
				uvs.append((rect.position + corner * rect.size) * inverse_texture_size)
			for index in TRIANGLES:
				indices.append(first + index)
	if vertices.is_empty():
		return null
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays, [], {}, Mesh.ARRAY_FLAG_USE_2D_VERTICES)
	var item := MeshInstance2D.new()
	item.position = Vector2(cx * data.tile_side, cy * data.tile_side)
	item.mesh = mesh
	item.texture = data.texture
	return item
