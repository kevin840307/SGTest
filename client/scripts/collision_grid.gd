extends RefCounted

const CELL := 16.0
var width := 0
var height := 0
var cells := PackedByteArray()

func open(path: String) -> Error:
	var raw := FileAccess.get_file_as_bytes(path)
	if raw.size() < 8:
		return ERR_FILE_CORRUPT
	var w := raw.decode_u32(0)
	var h := raw.decode_u32(4)
	if w <= 0 or h <= 0 or w*h > 16000000 or raw.size() != 8+w*h:
		return ERR_FILE_CORRUPT
	width = w
	height = h
	cells = raw.slice(8)
	return OK

func walkable_cell(cell: Vector2i) -> bool:
	return cell.x >= 0 and cell.y >= 0 and cell.x < width and cell.y < height and (cells[cell.y*width+cell.x]&7) == 0

func walkable(point: Vector2) -> bool:
	return point.is_finite() and walkable_cell(Vector2i(floori(point.x/CELL),floori(point.y/CELL)))

func allows(from: Vector2, to: Vector2) -> bool:
	if not walkable(from) or not walkable(to):
		return false
	var steps := maxi(1,ceili(from.distance_to(to)/(CELL*0.5)))
	var previous := Vector2i(floori(from.x/CELL),floori(from.y/CELL))
	for index in range(1,steps+1):
		var point := from.lerp(to,float(index)/steps)
		var cell := Vector2i(floori(point.x/CELL),floori(point.y/CELL))
		if not walkable_cell(cell):
			return false
		if cell.x != previous.x and cell.y != previous.y:
			if not walkable_cell(Vector2i(cell.x,previous.y)) or not walkable_cell(Vector2i(previous.x,cell.y)):
				return false
		previous = cell
	return true

func slide(from: Vector2, to: Vector2) -> Vector2:
	if allows(from,to):
		return to
	var horizontal := Vector2(to.x,from.y)
	if allows(from,horizontal):
		return horizontal
	var vertical := Vector2(from.x,to.y)
	return vertical if allows(from,vertical) else from

func nearest(point: Vector2, radius: int = 64) -> Vector2:
	if walkable(point):
		return point
	var origin := Vector2i(floori(point.x/CELL),floori(point.y/CELL))
	for ring in range(1,radius+1):
		for y in range(-ring,ring+1):
			for x in range(-ring,ring+1):
				if absi(x) != ring and absi(y) != ring:
					continue
				var cell := origin+Vector2i(x,y)
				if walkable_cell(cell):
					return (Vector2(cell)+Vector2.ONE*0.5)*CELL
	return Vector2(-1,-1)
