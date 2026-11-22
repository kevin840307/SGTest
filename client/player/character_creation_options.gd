extends RefCounted
## One UI selection shared by every skin. Options come from the Server snapshot;
## this model neither grants appearances nor modifies authoritative WorldState.
signal changed
const FIELDS := ["race","hair_index","hair_color","body_index","leg_index","sleeve_color"]
var editable := false
var _choices: Array = []
var _selected: Dictionary = {}

func set_choices(choices: Array) -> void:
	if choices == _choices:
		return
	_choices = choices.duplicate(true)
	_selected = _selected.duplicate() if _choices.has(_selected) else ({} if _choices.is_empty() else _choices[0].duplicate())
	changed.emit()

func set_editable(value: bool) -> void:
	if editable != value:
		editable = value
		changed.emit()

func selection() -> Dictionary:
	return _selected.duplicate()

func values(field: String) -> Array:
	if not field in FIELDS:
		return []
	var found := {}
	for choice in _choices:
		if field != "race" and choice.race != _selected.get("race",-1):
			continue
		if field == "hair_color" and choice.hair_index != _selected.get("hair_index",-1):
			continue
		# Clothing selectors are source-owned independent creation choices.
		# Server rules still decide the available values for each race.
		found[int(choice.get(field,0))] = true
	var result := found.keys()
	result.sort()
	return result

func select_value(field: String, value: int) -> bool:
	if not editable or not values(field).has(value):
		return false
	var desired := _selected.duplicate()
	desired[field] = value
	var next := {}
	if _choices.has(desired):
		next = desired
	else:
		for choice in _choices:
			if choice[field] == value and (field == "race" or choice.race == _selected.race):
				next = choice.duplicate()
				break
	if next.is_empty():
		return false
	if next != _selected:
		_selected = next
		changed.emit()
	return true

func cycle(field: String, delta: int) -> void:
	var available := values(field)
	if available.size() < 2:
		return
	var index := available.find(int(_selected.get(field,-1)))
	select_value(field,int(available[wrapi(index+delta,0,available.size())]))
