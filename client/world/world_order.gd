extends RefCounted
## Original swap predicate; never pass this to sort_custom (not a total order).
## DIRECT Paradise.exe CheckPointInFront 0x00549560, SortMapObject 0x00529CAA.


static func point_in_front(position: Vector2, front: Array, point: Vector2) -> bool:
	var local := point - position
	if local.x <= 0 and local.x <= front[0]:
		return local.y > front[1]
	if local.x > 0 and local.x >= front[2]:
		return local.y > front[3]
	return 2 * (front[1] - local.y) - (front[0] - local.x) < 0 or 2 * (front[3] - local.y) + (front[2] - local.x) < 0


static func swap_required(first: Dictionary, second: Dictionary) -> bool:
	var a := int(first.sorting_mode) == 0
	var b := int(second.sorting_mode) == 0
	if a and b:
		var edge := 2 if second.p.x < first.p.x else 0
		var point: Vector2 = second.p + Vector2(second.front[edge], second.front[edge + 1])
		var threshold: float = first.p.y + first.front[1 if point.x <= first.p.x else 3]
		return point.y < threshold
	if a:
		return not point_in_front(first.p, first.front, second.p)
	if b:
		return point_in_front(second.p, second.front, first.p)
	return second.p.y < first.p.y


static func insert_record(records: Array, record: Dictionary) -> void:
	# DIRECT InsertObjectToDrawList 0x0052A6CC: first positive swap, else append.
	for index in records.size():
		if swap_required(records[index], record):
			records.insert(index, record)
			return
	records.append(record)
