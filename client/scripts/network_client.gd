extends RefCounted

# The worker exclusively owns WebSocketPeer. Main thread exchanges bounded
# immutable packet queues; rendering and resource loads cannot stop socket I/O.
const WIRE = preload("res://scripts/message_pack.gd")
var thread := Thread.new()
var mutex := Mutex.new()
var running := false
var connected := false
var failed := false
var incoming: Array = []
var outgoing: Array[PackedByteArray] = []
var received_packets := 0
var sent_packets := 0
var max_incoming_depth := 0
var drain_age_ms: Array[float] = []

func start(url: String) -> void:
	running = true
	thread.start(_run.bind(url))

func send(op: int, body: Array) -> void:
	var bytes := WIRE.new().encode(op,body)
	mutex.lock()
	if outgoing.size() >= 256:
		failed = true
		running = false
	else:
		outgoing.append(bytes)
	mutex.unlock()

func receive() -> Array[PackedByteArray]:
	var now := Time.get_ticks_usec()
	mutex.lock()
	var queued := incoming
	incoming = []
	var packets: Array[PackedByteArray] = []
	for item in queued:
		packets.append(item.bytes)
		drain_age_ms.append((now-int(item.received_usec))/1000.0)
		if drain_age_ms.size() > 2048:
			drain_age_ms.pop_front()
	mutex.unlock()
	return packets

func statistics() -> Dictionary:
	mutex.lock()
	var ages := drain_age_ms.duplicate()
	var result := {"received_packets":received_packets,"sent_packets":sent_packets,
		"incoming_depth":incoming.size(),"outgoing_depth":outgoing.size(),
		"max_incoming_depth":max_incoming_depth,"failed":failed}
	mutex.unlock()
	ages.sort()
	if ages.is_empty():
		result.age_p95_ms = 0.0
		result.age_p99_ms = 0.0
		result.age_max_ms = 0.0
	else:
		result.age_p95_ms = ages[mini(ages.size()-1,ceili(ages.size()*.95)-1)]
		result.age_p99_ms = ages[mini(ages.size()-1,ceili(ages.size()*.99)-1)]
		result.age_max_ms = ages[-1]
	return result

func is_open() -> bool:
	mutex.lock()
	var result := connected
	mutex.unlock()
	return result

func close() -> void:
	mutex.lock()
	running = false
	mutex.unlock()
	if thread.is_started():
		thread.wait_to_finish()

func _run(url: String) -> void:
	var socket := WebSocketPeer.new()
	socket.inbound_buffer_size = 1048576
	socket.outbound_buffer_size = 1048576
	socket.max_queued_packets = 4096
	var error := socket.connect_to_url(url)
	if error != OK:
		mutex.lock()
		failed = true
		running = false
		mutex.unlock()
	while error == OK:
		mutex.lock()
		var active := running
		mutex.unlock()
		if not active:
			break
		socket.poll()
		var state := socket.get_ready_state()
		mutex.lock()
		connected = state == WebSocketPeer.STATE_OPEN
		if state == WebSocketPeer.STATE_CLOSED:
			failed = true
			running = false
		if connected:
			for packet in outgoing:
				if socket.send(packet) != OK:
					failed = true
					running = false
				else:
					sent_packets += 1
			outgoing.clear()
			while socket.get_available_packet_count() > 0:
				var packet := socket.get_packet()
				if socket.was_string_packet() or packet.size() > 65536 or incoming.size() >= 4096:
					failed = true
					running = false
					break
				incoming.append({"bytes":packet,"received_usec":Time.get_ticks_usec()})
				received_packets += 1
				max_incoming_depth = maxi(max_incoming_depth,incoming.size())
		mutex.unlock()
		OS.delay_msec(2)
	socket.close()
	mutex.lock()
	connected = false
	mutex.unlock()
