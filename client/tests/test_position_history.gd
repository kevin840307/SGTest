extends SceneTree
const History = preload("res://rendering/position_history.gd")
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

func run() -> void:
	var one := feed_trace(1.0)
	var two := feed_trace(2.0)
	var two_half := feed_trace(2.5)
	check(int(two.negative)==0,"2-tick interpolation rewound presentation time")
	check(int(two.holds)<int(one.holds),"2-tick buffer did not reduce jitter holds")
	check(float(two.max_step)<float(one.max_step),"2-tick buffer did not reduce correction tail")
	check(int(two_half.holds)==int(two.holds) and absf(float(two_half.max_step)-float(two.max_step))<0.001,
		"2.5-tick trace unexpectedly improves smoothness; candidate selection must be reviewed")
	var bounded := History.new()
	for tick in 20:
		check(bounded.push(tick,Vector2(tick*8,0),tick*33333),"valid sample rejected")
	check(bounded.size()==History.MAX_SAMPLES,"history is not bounded")
	var before := bounded.sample(700000)
	check(not bounded.push(18,Vector2.ZERO,710000),"stale server tick accepted")
	check(bounded.sample(700000)==before,"stale tick changed presentation")
	check(bounded.push(30,Vector2(9000,9000),720000),"large authoritative correction rejected")
	check(bounded.size()==1 and bounded.sample(720000)==Vector2(9000,9000),"large correction was interpolated instead of snapped")
	if failures.is_empty():
		print("POSITION_HISTORY_PASS selected_delay_ticks=2 selected_delay_ms=66.7 max_samples=8 no_extrapolation=true delay1_holds=%d delay2_holds=%d delay1_max_step=%.3f delay2_max_step=%.3f delay2_5_no_gain=true" % [one.holds,two.holds,one.max_step,two.max_step])
	quit(0 if failures.is_empty() else 1)
