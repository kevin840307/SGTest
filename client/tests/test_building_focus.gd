extends SceneTree
const Visibility = preload("res://map/building_visibility.gd")
var failures: Array = []


func _initialize() -> void:
	var world := {"scenery_schema":"sgtest.scenery/v1","scenery_order":{"version":1,"groups":[[],[0],[]]},
		"scenery_definitions":{"room":{"sorting_mode":0,"draw_group":1,"front":[-20,-10,20,-10],
			"inside_cells":[[0,0]],"outside_cells":[[0,0]]}},
		"objects":[{"p":[0,0],"parent_index":null,"definition":"room"}]}
	var baseline: Array = []
	for fps in [30,60,120,0]:
		var state := Visibility.new()
		if state.configure(world,1) != OK:
			failures.append("configure")
		var trace: Array = []
		for tick in 100:
			var point := Vector2i(0,-10) if tick >= 5 and tick < 50 else Vector2i(0,100)
			var visible: Array = [] if tick >= 18 and tick < 21 else [0]
			if state.advance(1,tick,point,visible) != OK:
				failures.append("fixed step")
			# Presentation reads at a different cadence; the state update trace is
			# unchanged, including Render=0 with no presentation reads (the trace
			# still records snapshots below for the invariant assertion).
			var reads := int((tick+1)*fps/50.0)-int(tick*fps/50.0)
			for unused in reads:
				state.snapshot()
			trace.append(state.snapshot())
		if fps == 30:
			baseline = trace
		elif trace != baseline:
			failures.append("render sampling changed fixed state")
	var result := {"status":"PASS" if failures.is_empty() else "FAIL","fixed_updates":400,"render_schedules":[30,60,120,0],"failures":failures}
	var args := OS.get_cmdline_user_args()
	if not args.is_empty():
		var file := FileAccess.open(args[0],FileAccess.WRITE)
		file.store_string(JSON.stringify(result))
		file.close()
	print("BUILDING_FOCUS_RESULT ",JSON.stringify(result))
	quit(0 if failures.is_empty() else 1)
