extends RefCounted

var elapsed := 0.0
var intervals: Array[float] = []
var finished := false
var started_usec := 0
var previous_usec := 0

func sample(_delta: float) -> void:
	if finished:
		return
	var now := Time.get_ticks_usec()
	if started_usec == 0:
		started_usec = now
		previous_usec = now
		return
	var frame_ms := (now - previous_usec) / 1000.0
	previous_usec = now
	elapsed = (now-started_usec) / 1000000.0
	if elapsed < 2.0:
		return
	intervals.append(frame_ms)
	if elapsed < 12.0:
		return
	finished = true
	intervals.sort()
	var total := 0.0
	for value in intervals:
		total += value
	var report := {"renderer": RenderingServer.get_video_adapter_name(),
		"warmup_seconds": 2, "sample_seconds": 10, "frames": intervals.size(),
		"mean_fps": 1000.0 / (total / intervals.size()),
		"frame_ms_p50": intervals[intervals.size()/2],
		"frame_ms_p95": intervals[mini(intervals.size()-1, ceili(intervals.size()*0.95)-1)],
		"draw_calls_last_frame": Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),
		"static_memory_bytes": OS.get_static_memory_usage(),
		"resolution": [DisplayServer.window_get_size().x, DisplayServer.window_get_size().y]}
	var output := FileAccess.open(ProjectSettings.globalize_path("res://").path_join("../reports/rendered_load.json"), FileAccess.WRITE)
	if output != null:
		output.store_string(JSON.stringify(report, "  "))
	print("rendered_load " + JSON.stringify(report))
