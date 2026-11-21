extends RefCounted
## Single-owner presentation state. Inputs are fixed updates, never Render delta.
## Focus is a world pixel point (legacy camera center), not a collision decision.
var _epoch := 0
var _tick := -1
var _rank: Dictionary = {}
var _buildings: Dictionary = {}
var _states: Dictionary = {}
var _selected := -1
var _configured := false


func clear() -> void:
	_tick = -1
	_rank.clear()
	_buildings.clear()
	_states.clear()
	_selected = -1
	_configured = false


func configure(world: Dictionary, epoch: int, first_update: int = 0) -> Error:
	if epoch <= _epoch or first_update < 0:
		return ERR_INVALID_PARAMETER
	if world.get("scenery_schema") != "sgtest.scenery/v1" or world.get("scenery_order",{}).get("version") != 1:
		return ERR_INVALID_DATA
	var groups = world.scenery_order.get("groups",[])
	if not groups is Array or groups.size() != 3:
		return ERR_INVALID_DATA
	var rank := {}
	var buildings := {}
	var states := {}
	for raw in groups[1]:
		var index := int(raw)
		if index < 0 or index >= world.objects.size() or rank.has(index):
			return ERR_INVALID_DATA
		var placement: Dictionary = world.objects[index]
		if placement.parent_index != null or not world.scenery_definitions.has(placement.definition):
			return ERR_INVALID_DATA
		var definition: Dictionary = world.scenery_definitions[placement.definition]
		if int(definition.draw_group) != 1:
			return ERR_INVALID_DATA
		rank[index] = rank.size()
		if int(definition.sorting_mode) != 0:
			continue
		# DIRECT CheckBldgEntrance only tests these counts in its reachable path.
		if definition.get("inside_cells",[]).is_empty() or definition.get("outside_cells",[]).is_empty():
			continue
		if definition.get("front",[]).size() != 4 or placement.p.size() != 2:
			return ERR_INVALID_DATA
		buildings[index] = {"p":Vector2i(placement.p[0],placement.p[1]),"front":definition.front.duplicate()}
		states[index] = {"inside":false,"counter":0}
	_epoch = epoch
	_tick = first_update - 1
	_rank = rank
	_buildings = buildings
	_states = states
	_selected = -1
	_configured = true
	return OK


static func contains(position, front: Array, focus) -> bool:
	# DIRECT CheckPointInsideBuilding 0x00549879. Lower edges closed, upper open.
	var x = focus.x - position.x
	var y = focus.y - position.y
	var lx = int(front[0]) - x
	var ly = int(front[1]) - y
	var rx = int(front[2]) - x
	var ry = int(front[3]) - y
	return 2 * ly - lx >= 0 and 2 * ry + rx >= 0 and 2 * ly + lx < 0 and 2 * ry - rx < 0


static func entrance(position, front: Array, focus) -> bool:
	return abs(focus.x-position.x) + abs(focus.y-position.y) <= 1000 and contains(position,front,focus)


func advance(epoch: int, update: int, focus: Vector2i, visible_main: Array) -> Error:
	if not _configured or epoch != _epoch or update != _tick + 1:
		return ERR_INVALID_PARAMETER
	var previous_rank := -1
	for raw in visible_main:
		var index := int(raw)
		if not _rank.has(index) or int(_rank[index]) <= previous_rank:
			return ERR_INVALID_DATA
		previous_rank = int(_rank[index])
	var selected := -1
	for raw in visible_main:
		var index := int(raw)
		if not _buildings.has(index):
			continue
		var building: Dictionary = _buildings[index]
		var inside := entrance(building.p,building.front,focus)
		_states[index].inside = inside  # ShowInside does not reset the counter.
		if inside:
			selected = index  # Last matching building in the baked visible order.
	if selected != _selected:
		if selected >= 0:
			_states[selected].counter = 256
		elif _selected >= 0:
			_states[_selected].inside = false
			_states[_selected].counter = 256
	_selected = selected
	for raw in visible_main:
		var index := int(raw)
		if _states.has(index):
			_states[index].counter = maxi(0,int(_states[index].counter)-16)
	_tick = update
	return OK


func snapshot() -> Dictionary:
	var transitions := {}
	for index in _states:
		transitions[index] = {"inside":_states[index].inside,"updates":(256-int(_states[index].counter))/16}
	return {"active":_configured,"epoch":_epoch,"update":_tick,"selected":_selected,"transitions":transitions}
