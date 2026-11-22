extends SceneTree
const History = preload("res://movement/position_history.gd")
var failures: Array[String] = []

func check(ok: bool, message: String) -> void:
	if not ok:
		failures.append(message)
		push_error(message)

func _initialize() -> void:
	run.call_deferred()

func feed_trace(delay: float) -> Dictionary:
	var history := History.new()
	var jitter_ms := [0,10,45,5,35,15,50,0,30,8,40,12]
	var arrivals: Array[float] = []
	var previous := -1.0
	for tick in 80:
		var arrival := float(tick)*History.SERVER_TICK_USEC + float(jitter_ms[tick%jitter_ms.size()])*1000.0
		arrival = maxf(arrival,previous+1000.0)
		arrivals.append(arrival)
		previous = arrival
	var next := 0
	var now := 0.0
	var frame_usec := 1000000.0/120.0
	var values: Array[float] = []
	while now <= arrivals[75]:
		while next < arrivals.size() and arrivals[next] <= now:
			history.push(next,Vector2(float(next)*8.0,0.0),int(arrivals[next]))
			next += 1
		if history.size() > 0 and now >= arrivals[5]:
			values.append(history.sample(int(now),delay).x)
		now += frame_usec
	var holds := 0
	var negative := 0
	var max_step := 0.0
	for i in range(1,values.size()):
		var step := values[i]-values[i-1]
		if absf(step) < 0.0001:
			holds += 1
		if step < -0.0001:
			negative += 1
		max_step = maxf(max_step,step)
	return {"holds":holds,"negative":negative,"max_step":max_step}

func feed_ui_20hz_trace() -> Dictionary:
	var history := History.new()
	var next_publish_usec := 0
	var last_position := -INF
	var holds := 0
	var backwards := 0
	var largest_step := 0.0
	for render_frame in 91: # 60fps for 1.5s, 20Hz input with 30Hz server ticks.
		var now_usec := roundi(float(render_frame) * (1000000.0 / 60.0))
		while next_publish_usec <= now_usec:
			var server_tick := roundi(float(next_publish_usec) / History.SERVER_TICK_USEC)
			history.push(server_tick,Vector2(float(server_tick)*8.0,0.0),next_publish_usec)
			next_publish_usec += 50000
		var position := history.sample(now_usec).x
		if now_usec >= 300000 and is_finite(last_position):
			var step := position-last_position
			if step < -0.001:
				backwards += 1
			elif step <= 0.001:
				holds += 1
			largest_step = maxf(largest_step,step)
		last_position = position
	return {"holds":holds,"backwards":backwards,"max_step":largest_step}

func run() -> void:
	var cadence := feed_ui_20hz_trace()
	print("POSITION_HISTORY_20HZ_UI_60HZ_RENDER ",cadence)
	check(int(cadence.backwards)==0 and int(cadence.holds)==0 and float(cadence.max_step) < 4.1,
		"20Hz UI publication made 60Hz camera hold or jump")
	var one := feed_trace(1.0)
	var two := feed_trace(2.0)
	var two_half := feed_trace(2.5)
	print("POSITION_HISTORY_CANDIDATES delay1=",one," delay2=",two," delay2.5=",two_half)
	check(int(two.negative)==0,"2-tick interpolation rewound presentation time")
	check(int(two.holds)<int(one.holds),"2-tick buffer did not reduce jitter holds")
	check(float(two.max_step)<float(one.max_step),"2-tick buffer did not reduce correction tail")
	check(int(two_half.holds)<int(two.holds) and float(two_half.max_step)<float(two.max_step),
		"2.5-tick continuous clock did not improve 20Hz-poll jitter")
	var bounded := History.new()
	for tick in 20:
		check(bounded.push(tick,Vector2(tick*8,0),tick*33333),"valid sample rejected")
	check(bounded.size()==History.MAX_SAMPLES,"history is not bounded")
	var before := bounded.sample(700000)
	check(not bounded.push(18,Vector2.ZERO,710000),"stale server tick accepted")
	check(bounded.sample(700000)==before,"stale tick changed presentation")
	check(bounded.push(30,Vector2(9000,9000),720000),"large authoritative correction rejected")
	check(bounded.size()==1 and bounded.sample(720000)==Vector2(9000,9000),"large correction was interpolated instead of snapped")
	var paused := History.new()
	check(paused.push(10,Vector2.ZERO,0) and paused.push(11,Vector2(8,0),33333),"pause baseline rejected")
	check(paused.sample(700000)==Vector2(8,0) and is_equal_approx(paused.target_tick(),11.0),
		"network stall accumulated imaginary future ticks")
	check(paused.push(12,Vector2(16,0),701000) and paused.sample(701000)==Vector2(16,0),
		"resuming Server updates left presentation frozen")
	var predicted := History.new()
	check(predicted.push(10,Vector2.ZERO,0,true) and
		predicted.push(11,Vector2(8,0),33333,true),"moving prediction samples rejected")
	var ahead := predicted.sample(300000)
	check(ahead.distance_to(Vector2(8,0)) <= History.MAX_PREDICTION_PIXELS+0.001 and
		ahead.x > 8.0 and ahead.x <= 20.001,
		"visual prediction did not bridge delayed packet or exceeded max 12px")
	check(predicted.sample(1000000).distance_to(Vector2(8,0)) <= 12.001,
		"prediction ran forever across long missing server updates")
	check(predicted.push(12,Vector2(16,0),1000100,false) and
		predicted.sample(1000100) == Vector2(16,0),
		"authoritative idle correction did not stop visual prediction")
	var same_tick := History.new()
	same_tick.push(10,Vector2.ZERO,0,true)
	same_tick.push(11,Vector2(8,0),33333,true)
	same_tick.push(11,Vector2(8,0),33334,false)
	check(same_tick.sample(300000)==Vector2(8,0),
		"same-tick idle transition left a stale moving state")
	if failures.is_empty():
		print("POSITION_HISTORY_PASS selected_delay_ticks=2.5 selected_delay_ms=83.3 max_samples=8 bounded_prediction_ticks=1.5 bounded_prediction_px=12 delay1_holds=%d delay2_holds=%d delay2_5_holds=%d delay1_max_step=%.3f delay2_max_step=%.3f delay2_5_max_step=%.3f" % [one.holds,two.holds,two_half.holds,one.max_step,two.max_step,two_half.max_step])
	quit(0 if failures.is_empty() else 1)
