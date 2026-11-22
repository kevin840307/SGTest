extends GridContainer
## Default skin view; Recovered2 delegates its source-position arrow controls
## to the same CharacterCreationOptions instance owned by SessionPanel.
const RACE_NAMES := ["人族男","人族女","米古族","機動靈","亞斯人"]
var _model: RefCounted
var _pickers: Dictionary = {}

static func value_label(field: String, value: int) -> String:
	match field:
		"race": return str(RACE_NAMES[value]) if value >= 0 and value < RACE_NAMES.size() else "未知種族"
		"hair_index": return "髮型 %d" % (value+1)
		"hair_color": return "顏色 %d" % (value+1)
		"body_index": return "顏色 %d" % (value+1)
		"leg_index": return "顏色 %d" % (value+1)
		"sleeve_color": return "顏色 %d" % (value+1)
	return ""

func _init() -> void:
	name = "CharacterAppearancePicker"
	columns = 2
	for field in ["race","hair_index","hair_color","body_index","leg_index","sleeve_color"]:
		var label := Label.new()
		label.text = {
			"race":"種族","hair_index":"髮型","hair_color":"髮色",
			"body_index":"上衣顏色","leg_index":"短褲顏色",
			"sleeve_color":"手袖顏色"}[field]
		add_child(label)
		var picker := OptionButton.new()
		picker.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		add_child(picker)
		_pickers[field] = picker
		picker.item_selected.connect(_selected.bind(field))
	hide()

func bind_options(model: RefCounted) -> void:
	if _model != null and _model.changed.is_connected(_refresh):
		_model.changed.disconnect(_refresh)
	_model = model
	_model.changed.connect(_refresh)
	_refresh()

func _refresh() -> void:
	var selected: Dictionary = _model.selection()
	visible = not selected.is_empty()
	for field in _pickers:
		var picker: OptionButton = _pickers[field]
		picker.clear()
		for value in _model.values(field):
			picker.add_item(value_label(field,int(value)),int(value))
			if int(value) == int(selected.get(field,-1)):
				picker.select(picker.item_count-1)
		picker.disabled = not _model.editable or picker.item_count < 2

func _selected(index: int, field: String) -> void:
	_model.select_value(field,_pickers[field].get_item_id(index))
