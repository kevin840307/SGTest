extends MultiMeshInstance2D

# One texture and one instanced draw, containing only viewport-adjacent cells.
# The original 230,400-cell map never becomes 230,400 scene nodes.
var data: Dictionary = {}
var tiles := PackedByteArray()
var remap: Dictionary = {}
var current_rect := Rect2i()

func open(root: String, map_id: int) -> Error:
	var folder := root.path_join("maps/%d" % map_id)
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(folder.path_join("world.json")))
	if not parsed is Dictionary or int(parsed.get("version",0)) != 2:
		return ERR_FILE_CORRUPT
	data = parsed
	for entry in [["tiles.bin","tiles_sha256"],["blocking.bin","collision_sha256"],["terrain.png","terrain_sha256"]]:
		if FileAccess.get_sha256(folder.path_join(entry[0])) != str(data[entry[1]]):
			return ERR_FILE_CORRUPT
	tiles = FileAccess.get_file_as_bytes(folder.path_join("tiles.bin"))
	if tiles.size() != 8+int(data.tile_width)*int(data.tile_height)*2:
		return ERR_FILE_CORRUPT
	var image := Image.load_from_file(folder.path_join("terrain.png"))
	if image == null:
		return ERR_FILE_CANT_READ
	texture = ImageTexture.create_from_image(image)
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	for index in data.tile_ids.size():
		remap[int(data.tile_ids[index])] = index
	multimesh = MultiMesh.new()
	multimesh.transform_format = MultiMesh.TRANSFORM_2D
	multimesh.use_custom_data = true
	var quad := QuadMesh.new()
	quad.size = Vector2(float(data.tile_side),float(data.tile_side))
	multimesh.mesh = quad
	var shader_material := ShaderMaterial.new()
	shader_material.shader = preload("res://shaders/terrain.gdshader")
	shader_material.set_shader_parameter("atlas_grid",Vector2(data.atlas_grid[0],data.atlas_grid[1]))
	material = shader_material
	return OK

func update_view(view: Rect2) -> void:
	if data.is_empty():
		return
	var side := float(data.tile_side)
	var first := Vector2i(clampi(floori(view.position.x/side)-1,0,int(data.tile_width)),clampi(floori(view.position.y/side)-1,0,int(data.tile_height)))
	var last := Vector2i(clampi(ceili(view.end.x/side)+1,first.x,int(data.tile_width)),clampi(ceili(view.end.y/side)+1,first.y,int(data.tile_height)))
	var rectangle := Rect2i(first,last-first)
	if rectangle == current_rect:
		return
	current_rect = rectangle
	multimesh.instance_count = maxi(0,rectangle.size.x*rectangle.size.y)
	var count := 0
	var width := int(data.tile_width)
	var columns := int(data.atlas_grid[0])
	for y in range(first.y,last.y):
		for x in range(first.x,last.x):
			var tile := tiles.decode_u16(8+(y*width+x)*2)
			if not remap.has(tile):
				continue
			var index: int = remap[tile]
			multimesh.set_instance_transform_2d(count,Transform2D(0,Vector2((x+0.5)*side,(y+0.5)*side)))
			multimesh.set_instance_custom_data(count,Color(index%columns,index/columns,0,0))
			count += 1
	multimesh.visible_instance_count = count
