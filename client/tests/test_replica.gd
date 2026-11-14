extends SceneTree

var failures := 0
const REPLICA = preload("res://scripts/client_replica.gd")

func check(condition: bool, message: String) -> void:
	if not condition:
		failures += 1
		push_error(message)

func row(id: int, x: float, revision: int = 0, pose: int = 1, hp: int = 30) -> Array:
	return [id,x,10.0,hp,6,pose,1.0,revision]

func _initialize() -> void:
	var replica := REPLICA.new()
	check(replica.snapshot(1,100,row(1,10)) == 2, "first independently received entity spawns")
	check(replica.snapshot(1,103,row(2,20)) == 2, "another packet need not wait for missing first packet")
	check(replica.monsters.size() == 2, "packet loss does not clear unaffected entities")
	replica.snapshot(1,102,row(1,12))
	check(replica.monsters[1].p.x == 12, "reordered chunks may still advance a different entity")
	replica.snapshot(1,101,row(1,11))
	check(replica.monsters[1].p.x == 12, "older packet cannot rewind entity position")
	replica.event(1,1,104,Vector2(14,10),0,6,4,1.2,2)
	replica.snapshot(1,105,row(1,15,1,3,10))
	check(replica.monsters[1].hp == 0 and replica.monsters[1].p.x == 14, "older combat revision cannot resurrect or move corpse")
	replica.snapshot(1,106,row(1,16,2,4,0))
	check(replica.monsters[1].p.x == 16, "same combat revision accepts newer movement snapshot")
	replica.event(1,1,104,Vector2(14,10),0,6,4,1.2,2)
	check(replica.monsters[1].p.x == 16, "duplicate reliable event cannot rewind newer snapshot")
	replica.erase_interest(1,107,PackedInt32Array([1]))
	replica.snapshot(1,106,row(1,17,2,4,0))
	check(not replica.monsters.has(1), "late snapshot cannot resurrect entity removed from view")
	replica.snapshot(1,109,row(1,19,3,0,30))
	replica.erase_interest(1,108,PackedInt32Array([1]))
	check(replica.monsters.has(1), "late old removal cannot erase re-entered entity")
	check(replica.event(1,1,110,Vector2(20,10),20,6,3,2,4) == 1, "new damage accepted")
	check(replica.event(1,1,110,Vector2(20,10),10,6,3,2,5) == 1, "same-tick second hit has a newer revision")
	check(replica.monsters[1].hp == 10, "same-tick health remains latest")
	for tick in range(111,300):
		replica.erase_interest(0,tick,PackedInt32Array([10000+tick]))
	check(replica.removed[0].size() <= 121, "old interest tombstones are bounded")
	replica.snapshot(0,100,row(10111,0))
	check(not replica.players.has(10111), "ancient packet stays rejected after tombstone eviction")
	var smooth := REPLICA.new()
	for t in [30,33,36,39]:
		smooth.snapshot(1,t,row(1,(t-30)*2.0))
		smooth.snapshot(1,t,row(2,100+(t-30)*-1.0))
	var prior := smooth.position_at(smooth.monsters[1],1.22)
	for i in range(1,7):
		var at := smooth.position_at(smooth.monsters[1],1.22+i/60.0)
		check(absf(at.x-prior.x-1.0) < 0.001,"remote velocity remains uniform between packets")
		prior = at
	check(smooth.position_at(smooth.monsters[2],1.32).x < 100,"independent actor can move in opposite direction")
	check(smooth.position_at(smooth.monsters[1],10).x <= 24.001,"lost stream extrapolation is bounded")
	smooth.event(1,1,40,Vector2(900,10),0,6,4,1.333,1)
	check(smooth.position_at(smooth.monsters[1],1.34).x == 900,"death snaps immediately without a cross-map streak")
	var clock := preload("res://scripts/server_clock.gd").new()
	clock.observe_snapshot(10,0)
	var previous := clock.time
	for i in 600:
		var local := (i+1)/60.0
		if i%60 == 0:
			clock.observe_pong(local-0.12,local,10+local-0.06)
		if i%17 == 0:
			clock.observe_snapshot(10+local-0.4,local)
		var current := clock.advance(local)
		check(current >= previous, "clock cannot rewind on late snapshots")
		previous = current
	check(absf(clock.time-20.0) < 0.02, "RTT-compensated clock converges")
	print("replica/clock checks: %d failures" % failures)
	quit(1 if failures else 0)
