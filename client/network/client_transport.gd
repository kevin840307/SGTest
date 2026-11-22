extends RefCounted

const WireCodec = preload("res://network/wire_codec.gd")
const AesPacket = preload("res://network/aes_cbc_frame.gd")

const STATE_STOPPED := 0
const STATE_CONNECTING := 1
const STATE_CONNECTED := 3
const STATE_FAILED := 4

const MAX_OUTBOUND_FRAMES := 256
const MAX_OUTBOUND_BYTES := 262144
const MAX_EVENT_FRAMES := 256
const MAX_EVENT_BYTES := 262144
const MAX_RECEIVE_BUFFER_BYTES := 262144
const CONNECT_TIMEOUT_MS := 5000
const WORKER_SLEEP_MS := 1

var _thread := Thread.new()
var _mutex := Mutex.new()
var _state := STATE_STOPPED
var _terminal_error := OK
var _stop_requested := false
var _outbound: Array[PackedByteArray] = []
var _outbound_bytes := 0
var _events: Array[Dictionary] = []
var _event_bytes := 0
var _frames_sent := 0
var _frames_received := 0
var _bytes_sent := 0
var _bytes_received := 0

func start_encrypted(host: String, port: int) -> Error:
	if host.is_empty() or port < 1 or port > 65535:
		return ERR_INVALID_PARAMETER
	var key: PackedByteArray = AesPacket.read_key_from_environment()
	var iv: PackedByteArray = AesPacket.read_iv_from_environment()
	if key.size() != 32 or iv.size() != 16:
		return ERR_INVALID_DATA
	_mutex.lock()
	if _thread.is_started() or _state != STATE_STOPPED:
		_mutex.unlock()
		return ERR_ALREADY_IN_USE
	_stop_requested = false
	_terminal_error = OK
	_outbound.clear()
	_outbound_bytes = 0
	_events.clear()
	_event_bytes = 0
	_frames_sent = 0
	_frames_received = 0
	_bytes_sent = 0
	_bytes_received = 0
	_state = STATE_CONNECTING
	_mutex.unlock()
	_thread = Thread.new()
	var error := _thread.start(_worker.bind(host, port, key, iv))
	if error != OK:
		_set_failed(error)
	return error

func send_frame(frame: PackedByteArray) -> bool:
	if frame.is_empty() or WireCodec.frame_length_prefix(frame) != frame.size():
		return false
	_mutex.lock()
	var accepted := _state in [STATE_CONNECTING, STATE_CONNECTED] and not _stop_requested and _outbound.size() < MAX_OUTBOUND_FRAMES and frame.size() <= MAX_OUTBOUND_BYTES - _outbound_bytes
	if accepted:
		var copy := frame.duplicate()
		_outbound.append(copy)
		_outbound_bytes += copy.size()
	_mutex.unlock()
	return accepted

func drain_events(max_frames: int = 64) -> Array[Dictionary]:
	if max_frames < 1:
		return []
	var result: Array[Dictionary] = []
	_mutex.lock()
	var count := mini(max_frames, _events.size())
	for _i in range(count):
		var event: Dictionary = _events.pop_front()
		_event_bytes -= int(event.get("bytes", 0))
		result.append(event)
	_mutex.unlock()
	return result

func get_status() -> Dictionary:
	_mutex.lock()
	var result := {
		"state": _state,
		"terminal_error": _terminal_error,
		"outbound_frames": _outbound.size(),
		"outbound_bytes": _outbound_bytes,
		"event_frames": _events.size(),
		"event_bytes": _event_bytes,
		"frames_sent": _frames_sent,
		"frames_received": _frames_received,
		"bytes_sent": _bytes_sent,
		"bytes_received": _bytes_received,
	}
	_mutex.unlock()
	return result

func stop() -> void:
	_mutex.lock()
	_stop_requested = true
	_mutex.unlock()
	if _thread.is_started():
		_thread.wait_to_finish()
	_mutex.lock()
	_state = STATE_STOPPED
	_outbound.clear()
	_outbound_bytes = 0
	_mutex.unlock()

func _worker(host: String, port: int, key: PackedByteArray, iv: PackedByteArray) -> void:
	var tcp := StreamPeerTCP.new()
	var error := tcp.connect_to_host(host, port)
	if error != OK:
		_set_failed(error)
		return
	var deadline := Time.get_ticks_msec() + CONNECT_TIMEOUT_MS
	while not _should_stop():
		error = tcp.poll()
		if error != OK:
			_set_failed(error)
			return
		if tcp.get_status() == StreamPeerTCP.STATUS_CONNECTED:
			break
		if tcp.get_status() == StreamPeerTCP.STATUS_ERROR:
			_set_failed(ERR_CONNECTION_ERROR)
			return
		if Time.get_ticks_msec() >= deadline:
			_set_failed(ERR_TIMEOUT)
			return
		OS.delay_msec(WORKER_SLEEP_MS)
	if _should_stop():
		tcp.disconnect_from_host()
		return
	tcp.set_no_delay(true)

	_set_state(STATE_CONNECTED)
	_push_event({"type": "connected", "bytes": 0})
	var receive_buffer := PackedByteArray()
	var pending_send := PackedByteArray()
	var pending_offset := 0
	while not _should_stop():
		error = tcp.poll()
		if error != OK or tcp.get_status() != StreamPeerTCP.STATUS_CONNECTED:
			_set_failed(ERR_CONNECTION_ERROR)
			break

		if pending_send.is_empty():
			var plain := _take_outbound()
			if not plain.is_empty():
				pending_send = AesPacket.encrypt_frame(plain, key, iv)
				if pending_send.is_empty():
					_set_failed(ERR_INVALID_DATA)
					break
			pending_offset = 0
		if not pending_send.is_empty():
			var remaining := pending_send.slice(pending_offset)
			var sent_result := tcp.put_partial_data(remaining)
			var send_error := int(sent_result[0])
			var sent := int(sent_result[1])
			if send_error != OK:
				_set_failed(send_error)
				break
			if sent > 0:
				pending_offset += sent
				_add_sent(sent)
			if pending_offset >= pending_send.size():
				_add_frame_sent()
				pending_send = PackedByteArray()
				pending_offset = 0

		var available := tcp.get_available_bytes()
		if available > 0:
			var read_result := tcp.get_partial_data(available)
			var read_error := int(read_result[0])
			if read_error != OK:
				_set_failed(read_error)
				break
			var chunk: PackedByteArray = read_result[1]
			if not chunk.is_empty():
				receive_buffer.append_array(chunk)
				_add_received(chunk.size())
				if receive_buffer.size() > MAX_RECEIVE_BUFFER_BYTES:
					_set_failed(ERR_OUT_OF_MEMORY)
					break
				if not _drain_receive_buffer(receive_buffer, key, iv):
					break
		OS.delay_msec(WORKER_SLEEP_MS)

	tcp.disconnect_from_host()

func _drain_receive_buffer(buffer: PackedByteArray, key: PackedByteArray, iv: PackedByteArray) -> bool:
	while buffer.size() >= 4:
		var cipher_size := int(buffer.decode_u32(0))
		if cipher_size < 16 or cipher_size > AesPacket.MAX_CIPHER_BYTES or cipher_size % 16 != 0:
			_set_failed(ERR_INVALID_DATA)
			return false
		if buffer.size() < 4 + cipher_size:
			return true
		var cipher := buffer.slice(4, 4 + cipher_size)
		var tail := buffer.slice(4 + cipher_size)
		buffer.clear()
		if not tail.is_empty():
			buffer.append_array(tail)
		var frame := AesPacket.decrypt_record(cipher, key, iv)
		if frame.is_empty() or WireCodec.frame_length_prefix(frame) != frame.size():
			_set_failed(ERR_INVALID_DATA)
			return false
		if not _push_event({"type": "frame", "frame": frame, "bytes": frame.size()}):
			_set_failed(ERR_OUT_OF_MEMORY)
			return false
		_add_frame_received()
	return true

func _take_outbound() -> PackedByteArray:
	_mutex.lock()
	var frame := PackedByteArray()
	if not _outbound.is_empty():
		frame = _outbound.pop_front()
		_outbound_bytes -= frame.size()
	_mutex.unlock()
	return frame

func _push_event(event: Dictionary) -> bool:
	var bytes := int(event.get("bytes", 0))
	_mutex.lock()
	var accepted := _events.size() < MAX_EVENT_FRAMES and bytes <= MAX_EVENT_BYTES - _event_bytes
	if accepted:
		_events.append(event)
		_event_bytes += bytes
	_mutex.unlock()
	return accepted

func _should_stop() -> bool:
	_mutex.lock()
	var value := _stop_requested
	_mutex.unlock()
	return value

func _set_state(value: int) -> void:
	_mutex.lock()
	_state = value
	_mutex.unlock()

func _set_failed(error: int) -> void:
	_mutex.lock()
	_terminal_error = error
	_state = STATE_FAILED
	_mutex.unlock()

func _add_sent(bytes: int) -> void:
	_mutex.lock()
	_bytes_sent += bytes
	_mutex.unlock()

func _add_received(bytes: int) -> void:
	_mutex.lock()
	_bytes_received += bytes
	_mutex.unlock()

func _add_frame_sent() -> void:
	_mutex.lock()
	_frames_sent += 1
	_mutex.unlock()

func _add_frame_received() -> void:
	_mutex.lock()
	_frames_received += 1
	_mutex.unlock()
