extends RefCounted
## Lossless adapter for Converter world.json + original WH/u16 tiles.bin.

var texture: Texture2D
var tile_side := 0
var width := 0
var height := 0
var cells := PackedInt32Array()
var regions: Array[Rect2] = []


func configure(asset: Dictionary) -> Error:
	if not asset.has_all(["manifest", "extra", "pages"]) or asset.pages.size() != 1:
		return ERR_INVALID_DATA
	var world: Dictionary = asset.manifest
	var bytes: PackedByteArray = asset.extra.get("tiles", PackedByteArray())
	if world.get("version") != 3 or world.get("tile_source_rule") != "int16_plus_one_wrap" or bytes.size() < 8:
		return ERR_INVALID_DATA
	var w := int(world.get("tile_width", 0))
	var h := int(world.get("tile_height", 0))
	var side := int(world.get("tile_side", 0))
	if w <= 0 or h <= 0 or side <= 0 or side > 256 or bytes.decode_u32(0) != w or bytes.decode_u32(4) != h or bytes.size() != 8 + w * h * 2:
		return ERR_INVALID_DATA
	var grid = world.get("atlas_grid", [])
	if grid.size() != 2 or int(grid[0]) <= 0 or int(grid[1]) <= 0:
		return ERR_INVALID_DATA
	var page: Texture2D = asset.pages[0]
	if page.get_width() != int(grid[0]) * side or page.get_height() != int(grid[1]) * side:
		return ERR_INVALID_DATA
	var remap := PackedInt32Array()
	remap.resize(65536)
	remap.fill(-1)
	var rects: Array[Rect2] = []
	for original_id in world.get("tile_ids", []):
		var id := int(original_id)
		if id < 0 or id > 65535 or remap[id] >= 0 or rects.size() >= int(grid[0]) * int(grid[1]):
			return ERR_INVALID_DATA
		var index := rects.size()
		remap[id] = index
		rects.append(Rect2((index % int(grid[0])) * side, (index / int(grid[0])) * side, side, side))
	var indices := PackedInt32Array()
	indices.resize(w * h)
	for index in indices.size():
		var original := bytes.decode_u16(8 + index * 2)
		if remap[original] < 0:
			return ERR_INVALID_DATA
		indices[index] = remap[original]
	texture = page
	width = w
	height = h
	tile_side = side
	cells = indices
	regions = rects
	return OK


func visible_cells(view: Rect2) -> Rect2i:
	var first := Vector2i(clampi(int(floor(view.position.x / tile_side)) - 1, 0, width),
		clampi(int(floor(view.position.y / tile_side)) - 1, 0, height))
	var end := Vector2i(clampi(int(ceil(view.end.x / tile_side)) + 1, 0, width),
		clampi(int(ceil(view.end.y / tile_side)) + 1, 0, height))
	return Rect2i(first, Vector2i(maxi(0, end.x - first.x), maxi(0, end.y - first.y)))
