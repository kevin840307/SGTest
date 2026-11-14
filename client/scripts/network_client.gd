extends RefCounted

# The worker exclusively owns WebSocketPeer. Main thread exchanges bounded
# immutable packet queues; rendering and resource loads cannot stop socket I/O.
const WIRE = preload("res://scripts/message_pack.gd")
var thread := Thread.new()
var mutex := Mutex.new()
var running := false
var connected := false
var failed := false
var incoming: Array[PackedByteArray] = []
var outgoing: Array[PackedByteArray] = []

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
	mutex.lock()
	var packets := incoming
	incoming = []
	mutex.unlock()
	return packets

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
			outgoing.clear()
			while socket.get_available_packet_count() > 0:
				var packet := socket.get_packet()
				if socket.was_string_packet() or packet.size() > 65536 or incoming.size() >= 4096:
					failed = true
					running = false
					break
				incoming.append(packet)
		mutex.unlock()
		OS.delay_msec(2)
	socket.close()
	mutex.lock()
	connected = false
	mutex.unlock()
