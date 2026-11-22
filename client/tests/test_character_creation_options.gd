extends SceneTree
const Options = preload("res://player/character_creation_options.gd")
const Picker = preload("res://ui/character_appearance_picker.gd")
var failures: Array = []

func check(ok: bool, reason: String) -> void:
	if not ok:
		failures.append(reason)
		push_error("CHARACTER_CREATION_OPTIONS_FAIL "+reason)

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var options := Options.new()
	var choices := [
		{"race":0,"hair_index":0,"hair_color":0,"body_index":0,"leg_index":0},
		{"race":0,"hair_index":0,"hair_color":0,"body_index":1,"leg_index":0},
		{"race":0,"hair_index":0,"hair_color":0,"body_index":1,"leg_index":2},
		{"race":0,"hair_index":1,"hair_color":2,"body_index":0,"leg_index":0},
		{"race":0,"hair_index":1,"hair_color":5,"body_index":0,"leg_index":0},
		{"race":1,"hair_index":3,"hair_color":7,"body_index":2,"leg_index":1},
	]
	options.set_choices(choices)
	options.set_editable(true)
	choices[0].hair_index = 99
	var first := Picker.new()
	var second := Picker.new()
	root.add_child(first)
	root.add_child(second)
	first.bind_options(options)
	second.bind_options(options)
	check(options.selection().hair_index==0,"source array mutated model")
	check(
		options.values("race")==[0,1] and
		options.values("hair_index")==[0,1] and
		options.values("hair_color")==[0] and
		options.values("body_index")==[0,1] and
		options.values("leg_index")==[0,2],
		"dependent option sets wrong")
	options.select_value("body_index",1)
	options.select_value("leg_index",2)
	check(
		options.selection().body_index==1 and options.selection().leg_index==2,
		"original upper/lower armor choices are not independently selectable")
	first._pickers.hair_index.item_selected.emit(1)
	check(
		options.selection()=={
			"race":0,"hair_index":1,"hair_color":2,
			"body_index":0,"leg_index":0} and
		second._pickers.hair_index.get_selected_id()==1,
		"views do not share selection or dependent fallback invalid")
	options.cycle("hair_color",1)
	check(options.selection().hair_color==5,"hair color did not follow allowed values")
	options.cycle("race",1)
	check(
		options.selection()=={
			"race":1,"hair_index":3,"hair_color":7,
			"body_index":2,"leg_index":1},
		"race switch created an unlisted combination")
	check(
		first._pickers.hair_index.disabled and
		first._pickers.hair_color.disabled and
		first._pickers.body_index.disabled and
		first._pickers.leg_index.disabled,
		"single-option fields still clickable")
	options.set_editable(false)
	check(not options.select_value("race",0) and first._pickers.race.disabled,"pending UI still permits edits")
	options.set_choices([])
	check(options.selection().is_empty() and not first.visible and not second.visible,"empty options retained stale UI")
	first.queue_free()
	second.queue_free()
	await process_frame
	if failures.is_empty():
		print("CHARACTER_CREATION_OPTIONS_PASS shared_views=true dependent_exact_choices=true clothing_independent=true source_copy=true pending_disabled=true clear_releases=true")
	quit(0 if failures.is_empty() else 1)
