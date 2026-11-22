extends RefCounted

const WireCodec = preload("res://network/wire_codec.gd")
const STATE_STOPPED := 0
const STATE_CONNECTING := 1
const STATE_TLS_HANDSHAKING := 2
const STATE_CONNECTED := 3
const STATE_FAILED := 4

const AUTH_RESULT := "0900000001000B0000"
const CHARACTER_LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE_RESULT := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D00000001001400010000000000000028000000000000000B000000000000000F000000000000410000004101"
const SELF_STOP := "4A0000000100180001000000000000002B000000000000000B00000000000000020000000000000001000000000000000F00000002000000000000000000C04100000041000100000001"
const MOVE_RECEIPT := "1E0000000100020001000000000000000100000000002A00000000000000"

var _mutex := Mutex.new()
var _state := STATE_STOPPED
var _events: Array[Dictionary] = []
var _sent: Array[PackedByteArray] = []
var _error := OK

func start_encrypted(_host: String, _port: int, _common_name: String = "", _trusted_chain = null) -> Error:
	_mutex.lock()
	_state = STATE_CONNECTED
	_error = OK
	_events.clear()
	_sent.clear()
	_mutex.unlock()
	return OK

func send_frame(frame: PackedByteArray) -> bool:
	var opcode := WireCodec.frame_opcode(frame)
	if opcode < 0:
		return false
	_mutex.lock()
	if _state != STATE_CONNECTED:
		_mutex.unlock()
		return false
	_sent.append(frame.duplicate())
	match opcode:
		WireCodec.OPCODE_AUTH_REQUEST:
			_events.append({"type": "frame", "frame": AUTH_RESULT.hex_decode(), "bytes": 9})
			_events.append({"type": "frame", "frame": CHARACTER_LIST.hex_decode(), "bytes": 24})
		WireCodec.OPCODE_CHARACTER_CHOOSE:
			_events.append({"type": "frame", "frame": CHOOSE_RESULT.hex_decode(), "bytes": 45})
			_events.append({"type": "frame", "frame": READY.hex_decode(), "bytes": 45})
		WireCodec.OPCODE_WORLD_READY_ACK:
			pass # control frame: no gameplay response; keep transport connected
		WireCodec.OPCODE_WORLD_RESYNC_REQUEST:
			pass # not used by this happy-path fixture
		WireCodec.OPCODE_PVP_MODE_COMMAND:
			pass # independent auto-query; fixture checks WorldReady and movement
		WireCodec.OPCODE_MOVE_TARGET:
			_events.append({"type": "frame", "frame": MOVE_RECEIPT.hex_decode(), "bytes": 30})
			_events.append({"type": "frame", "frame": SELF_STOP.hex_decode(), "bytes": 74})
		_:
			_state = STATE_FAILED
			_error = ERR_INVALID_DATA
	_mutex.unlock()
	return true

func drain_events(max_frames: int = 64) -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	_mutex.lock()
	var count := mini(max_frames, _events.size())
	for _i in range(count):
		var event: Dictionary = _events.pop_front()
		result.append(event)
	_mutex.unlock()
	return result

func get_status() -> Dictionary:
	_mutex.lock()
	var result := {"state": _state, "terminal_error": _error}
	_mutex.unlock()
	return result

func stop() -> void:
	_mutex.lock()
	_state = STATE_STOPPED
	_mutex.unlock()

func sent_hex() -> Array[String]:
	var result: Array[String] = []
	_mutex.lock()
	for frame in _sent:
		result.append(frame.hex_encode())
	_mutex.unlock()
	return result
