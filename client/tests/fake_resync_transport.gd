extends RefCounted
const WireCodec = preload("res://wire/wire_codec.gd")
const STATE_STOPPED := 0
const STATE_CONNECTED := 3
const STATE_FAILED := 4

const AUTH_RESULT := "0900000001000B0000"
const CHARACTER_LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE_RESULT := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const DELTA_WITHOUT_SPAWN := "4A0000000100170001000000000000002C000000000000001600000000000000020000000000000001000000000000000F0000000800000000000000000060420000C041000500000001"
const SELF_BASE := "4A0000000100180001000000000000002C000000000000000B00000000000000020000000000000001000000000000000F0000000800000000000000000060420000C041000500000006"
const MOVE_RECEIPT := "1E0000000100020001000000000000000100000000002A00000000000000"

var _mutex := Mutex.new()
var _state := STATE_STOPPED
var _error := OK
var _events: Array[Dictionary] = []
var _sent: Array[int] = []
var _ready_acks := 0

func start_secure(_host: String, _port: int, _common_name: String = "", _trusted_chain = null) -> Error:
	_mutex.lock()
	_state = STATE_CONNECTED
	_error = OK
	_events.clear()
	_sent.clear()
	_ready_acks = 0
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
	_sent.append(opcode)
	match opcode:
		WireCodec.OPCODE_AUTH_REQUEST:
			_push(AUTH_RESULT.hex_decode())
			_push(CHARACTER_LIST.hex_decode())
		WireCodec.OPCODE_CHARACTER_CHOOSE:
			_push(CHOOSE_RESULT.hex_decode())
			_push(READY.hex_decode())
		WireCodec.OPCODE_WORLD_READY_ACK:
			_ready_acks += 1
			if _ready_acks == 1:
				# Deliberately omit Spawn and inject the next authoritative Delta.
				_push(DELTA_WITHOUT_SPAWN.hex_decode())
		WireCodec.OPCODE_WORLD_RESYNC_REQUEST:
			var replacement := READY.hex_decode()
			replacement.encode_s64(16,46)
			_push(replacement)
			var own := SELF_BASE.hex_decode()
			own.encode_s64(16,46)
			_push(own)
			var peer := DELTA_WITHOUT_SPAWN.hex_decode()
			peer.encode_u16(6,WireCodec.OPCODE_ENTITY_SPAWN)
			peer.encode_s64(16,47)
			_push(peer)
		WireCodec.OPCODE_PVP_MODE_COMMAND:
			pass # automatic query does not affect AOI recovery
		WireCodec.OPCODE_MOVE_TARGET:
			var receipt := MOVE_RECEIPT.hex_decode()
			receipt.encode_s64(22,48)
			_push(receipt)
		_:
			_state = STATE_FAILED
			_error = ERR_INVALID_DATA
	_mutex.unlock()
	return true

func _push(frame: PackedByteArray) -> void:
	_events.append({"type":"frame","frame":frame,"bytes":frame.size()})

func drain_events(max_frames: int = 64) -> Array[Dictionary]:
	var output: Array[Dictionary] = []
	_mutex.lock()
	for _i in range(mini(max_frames,_events.size())):
		output.append(_events.pop_front())
	_mutex.unlock()
	return output

func get_status() -> Dictionary:
	_mutex.lock()
	var value := {"state":_state,"terminal_error":_error}
	_mutex.unlock()
	return value

func stop() -> void:
	_mutex.lock()
	_state = STATE_STOPPED
	_mutex.unlock()

func sent_opcodes() -> Array[int]:
	_mutex.lock()
	var value := _sent.duplicate()
	_mutex.unlock()
	return value
