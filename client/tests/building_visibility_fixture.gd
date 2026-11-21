extends RefCounted
const Visibility = preload("res://map/building_visibility.gd")


static func run(check: Callable) -> void:
	for x in range(-30,31):
		for y in range(-30,11):
			var expected: bool = 2*y <= -absi(x) and 2*y > -40+absi(x)
			check.call(Visibility.contains(Vector2i(100,200),[-20,-10,20,-10],Vector2i(100+x,200+y)) == expected,"Inside diamond boundary mismatch")
	check.call(Visibility.entrance(Vector2i.ZERO,[-2000,-1000,2000,-1000],Vector2i(0,-1000)),"Distance 1000 rejected")
	check.call(not Visibility.entrance(Vector2i.ZERO,[-2000,-1000,2000,-1000],Vector2i(0,-1001)),"Distance 1001 accepted")
	check.call(Visibility.contains(Vector2.ZERO,[-20,-10,20,-10],Vector2(0,-0.25)) and not Visibility.contains(Vector2.ZERO,[-20,-10,20,-10],Vector2(0,0.25)),"Interpolated actor coordinates were truncated")
	var eligible := {"sorting_mode":0,"draw_group":1,"front":[-20,-10,20,-10],"inside_cells":[[999999,999999]],"outside_cells":[[-999999,-999999]]}
	var no_entry := eligible.duplicate(true)
	no_entry.outside_cells = []
	var world := {"scenery_schema":"sgtest.scenery/v1","scenery_order":{"version":1,"groups":[[],[0,1,2],[]]},
		"scenery_definitions":{"a":eligible,"b":no_entry},"objects":[
		{"p":[0,0],"parent_index":null,"definition":"a"},
		{"p":[100,0],"parent_index":null,"definition":"a"},
		{"p":[0,0],"parent_index":null,"definition":"b"}]}
	var state := Visibility.new()
	check.call(state.configure(world,1) == OK,"Focus configure failed")
	check.call(state.advance(1,0,Vector2i(0,-10),[0,1,2]) == OK,"Focus enter failed")
	var snap: Dictionary = state.snapshot()
	check.call(snap.selected == 0 and snap.transitions[0] == {"inside":true,"updates":1} and not snap.transitions.has(2),"Entry/count gates failed")
	for invalid in [[0,1,[0,1]], [1,0,[0,1]], [1,2,[0,1]], [1,1,[1,0]], [1,1,[0,0]], [1,1,[9]]]:
		check.call(state.advance(invalid[0],invalid[1],Vector2i.ZERO,invalid[2]) != OK and state.snapshot() == snap,"Rejected focus input changed state")
	# Render callbacks are absent: fixed state still reaches its stable pose.
	for tick in range(1,16):
		check.call(state.advance(1,tick,Vector2i(0,-10),[0,1]) == OK,"Fixed focus step failed")
	check.call(state.snapshot().transitions[0].updates == 16,"Repeated inside restarted fade")
	check.call(state.advance(1,16,Vector2i(100,-10),[0,1]) == OK,"Direct building switch failed")
	snap = state.snapshot()
	check.call(snap.selected == 1 and not snap.transitions[0].inside and snap.transitions[0].updates == 16 and snap.transitions[1].updates == 1,"ShowInside and SetInOut incorrectly merged")
	check.call(state.advance(1,17,Vector2i(500,500),[]) == OK,"Invisible exit failed")
	snap = state.snapshot()
	check.call(snap.selected == -1 and not snap.transitions[1].inside and snap.transitions[1].updates == 0,"Invisible exit counter was stepped")
	check.call(state.advance(1,18,Vector2i(500,500),[0,1]) == OK and state.snapshot().transitions[1].updates == 1,"Visible counter did not resume")
	snap.transitions[1].inside = true
	check.call(not state.snapshot().transitions[1].inside,"Snapshot leaked mutable state")
	var before := state.snapshot()
	var invalid := world.duplicate(true)
	invalid.scenery_order.groups[1].append(0)
	check.call(state.configure(invalid,2) == ERR_INVALID_DATA and state.snapshot() == before,"Failed configure destroyed active state")
	state.clear()
	check.call(state.advance(1,0,Vector2i.ZERO,[]) != OK and state.configure(world,1) != OK,"Clear accepted stale epoch")
	check.call(state.configure(world,2,50) == OK and state.advance(2,50,Vector2i(0,-10),[0,1]) == OK,"New epoch did not start cleanly")
	# Overlap: ShowInside sets both flags, but only the last matching root resets.
	world.objects[1].p = [0,0]
	check.call(state.configure(world,3) == OK and state.advance(3,0,Vector2i(0,-10),[0,1]) == OK,"Overlap setup failed")
	snap = state.snapshot()
	check.call(snap.selected == 1 and snap.transitions[0].inside and snap.transitions[0].updates == 16 and snap.transitions[1].updates == 1,"Overlap last-wins contract changed")
