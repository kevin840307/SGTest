extends SceneTree

func _initialize() -> void:
	var client := preload("res://scripts/network_client.gd").new()
	var wire := preload("res://scripts/message_pack.gd").new()
	var packet: PackedByteArray = wire.encode(24,[1.0])
	var now := Time.get_ticks_usec()
	for index in 128:
		client.incoming.append({"bytes":packet,"received_usec":now-1000-index})
	client.received_packets = 128
	client.max_incoming_depth = 128
	var started := Time.get_ticks_usec()
	var packets: Array[PackedByteArray] = client.receive()
	assert(packets.size() == 128)
	var decoded := 0
	for bytes in packets:
		var value: Array = wire.decode(bytes)
		assert(value.size() == 3 and int(value[1]) == 24)
		decoded += 1
	var elapsed_ms := (Time.get_ticks_usec()-started)/1000.0
	var stats := client.statistics()
	assert(decoded == 128)
	assert(int(stats.incoming_depth) == 0)
	assert(int(stats.max_incoming_depth) == 128)
	assert(float(stats.age_p99_ms) >= 1.0)
	assert(elapsed_ms < 2000.0,"128 packet burst must drain/decode in <2s")
	print("network 128-packet burst drain/decode passed in %.3f ms" % elapsed_ms)
	quit()
