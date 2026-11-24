extends RefCounted

const WireCodec = preload("res://network/wire_codec.gd")
const STATE_STOPPED := 0
const STATE_CONNECTED := 3
const STATE_FAILED := 4

const AUTH_RESULT := "0900000001000B0000"
const CHARACTER_LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE_RESULT := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D00000001001400010000000000000028000000000000000B000000000000000F000000000000410000004101"
const MONSTER_ID := 1001500001

var _mutex := Mutex.new()
var _state := STATE_STOPPED
var _events: Array[Dictionary] = []
var _sent: Array[PackedByteArray] = []
var _error := OK
var _hold_first_attack := false
var _simulate_resync_gap := false
var _hold_first_move_stop := false
var _pending_first_attack := {}
var _monster_seeded := false
var _resync_requested := false
var _ready_ack_count := 0
var _attack_count := 0
var _move_count := 0
var _move_targets: Array[Vector2i] = []

func _init(hold_first_attack: bool = false, simulate_resync_gap: bool = false, hold_first_move_stop: bool = false) -> void:
	_hold_first_attack = hold_first_attack
	_simulate_resync_gap = simulate_resync_gap
	_hold_first_move_stop = hold_first_move_stop

func start_encrypted(_host: String, _port: int, ) -> Error:
	_mutex.lock()
	_state = STATE_CONNECTED
	_error = OK
	_events.clear()
	_sent.clear()
	_pending_first_attack.clear()
	_monster_seeded = false
	_resync_requested = false
	_ready_ack_count = 0
	_attack_count = 0
	_move_count = 0
	_move_targets.clear()
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
			_push(AUTH_RESULT.hex_decode())
			_push(CHARACTER_LIST.hex_decode())
		WireCodec.OPCODE_CHARACTER_CHOOSE:
			_push(CHOOSE_RESULT.hex_decode())
			_push(READY.hex_decode())
		WireCodec.OPCODE_WORLD_READY_ACK:
			_ready_ack_count += 1
			if not _monster_seeded:
				_monster_seeded = true
				_push(_monster_state(WireCodec.OPCODE_MONSTER_SPAWN,41,1,100,0))
				if _simulate_resync_gap:
					_push(_self_state(41,1,Vector2i(8,8),false,0))
			elif _resync_requested:
				_resync_requested = false
				# Queue the replacement baseline only after the ACK. ClientSession
				# therefore gets one logic-loop gap with WorldReady ACKed but the
				# visible monster not yet drained.
				_push(_self_state(50,5,Vector2i(40,8),false,1))
				_push(_monster_state(WireCodec.OPCODE_MONSTER_SPAWN,50,4,100,3))
		WireCodec.OPCODE_PVP_MODE_COMMAND:
			pass # independent query does not alter monster combat
		WireCodec.OPCODE_NORMAL_ATTACK:
			var attack := WireCodec.decode_normal_attack(frame)
			if attack.is_empty():
				_fail_locked()
			else:
				_attack_count += 1
				if _attack_count == 1:
					if _hold_first_attack:
						_pending_first_attack = attack.duplicate(true)
					else:
						_push(_attack_result(int(attack.attack_sequence),4,42,0,100,1))
				else:
					var revision := 5 if _ready_ack_count > 1 else 2
					var tick := 51 if _ready_ack_count > 1 else 47
					_push(_attack_result(int(attack.attack_sequence),0,tick,30,70,revision))
					_push(_monster_state(WireCodec.OPCODE_MONSTER_DELTA,tick,revision,70,0))
		WireCodec.OPCODE_MOVE_TARGET:
			var move := WireCodec.decode_move_target(frame)
			if move.is_empty():
				_fail_locked()
			else:
				_move_count += 1
				var seq := int(move.movement_sequence)
				var target := Vector2i(int(move.target_x),int(move.target_y))
				_move_targets.append(target)
				_push(_move_receipt(seq,43 + _move_count))
				if _hold_first_move_stop and _move_count == 1:
					_push(_self_state(44,2,target,true,seq))
				elif _simulate_resync_gap:
					# Revision 1 was seeded. Jumping to 3 forces the real reducer
					# through its same-connection AOI resync path.
					_push(_self_state(44,3,target,true,seq))
				else:
					var base_revision := 1 + _move_count
					_push(_self_state(44 + _move_count,base_revision,target,true,seq))
					_push(_self_state(45 + _move_count,base_revision + 1,target,false,seq))
		WireCodec.OPCODE_WORLD_RESYNC_REQUEST:
			if _simulate_resync_gap:
				_resync_requested = true
				_push(_world_ready(49))
		_:
			_fail_locked()
	var ok := _state == STATE_CONNECTED
	_mutex.unlock()
	return ok

func release_first_attack_result() -> void:
	_mutex.lock()
	if not _pending_first_attack.is_empty():
		_push(_attack_result(int(_pending_first_attack.attack_sequence),4,42,0,100,1))
		_pending_first_attack.clear()
	_mutex.unlock()

func despawn_monster() -> void:
	_mutex.lock()
	_push(_monster_despawn(46,2))
	_mutex.unlock()

func kill_monster() -> void:
	_mutex.lock()
	_push(_monster_state(WireCodec.OPCODE_MONSTER_DELTA,46,2,0,4))
	_mutex.unlock()

func drain_events(max_frames: int = 64) -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	_mutex.lock()
	var count := mini(max_frames,_events.size())
	for _i in range(count):
		result.append(_events.pop_front())
	_mutex.unlock()
	return result

func get_status() -> Dictionary:
	_mutex.lock()
	var result := {"state":_state,"terminal_error":_error}
	_mutex.unlock()
	return result

func stop() -> void:
	_mutex.lock()
	_state = STATE_STOPPED
	_mutex.unlock()

func sent_opcodes() -> Array[int]:
	var result: Array[int] = []
	_mutex.lock()
	for frame in _sent:
		result.append(WireCodec.frame_opcode(frame))
	_mutex.unlock()
	return result

func attack_count() -> int:
	_mutex.lock()
	var value := _attack_count
	_mutex.unlock()
	return value

func move_targets() -> Array[Vector2i]:
	_mutex.lock()
	var result := _move_targets.duplicate()
	_mutex.unlock()
	return result

func _push(frame: PackedByteArray) -> void:
	_events.append({"type":"frame","frame":frame,"bytes":frame.size()})

func _fail_locked() -> void:
	_state = STATE_FAILED
	_error = ERR_INVALID_DATA

func _header(writer: StreamPeerBuffer,size: int,opcode: int) -> void:
	writer.big_endian = false
	writer.put_u32(size)
	writer.put_u16(1)
	writer.put_u16(opcode)

func _world_ready(tick: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	_header(writer,45,WireCodec.OPCODE_WORLD_READY)
	writer.put_64(1)
	writer.put_64(tick)
	writer.put_64(11)
	writer.put_32(15)
	writer.put_float(40.0)
	writer.put_float(8.0)
	writer.put_u8(6)
	return writer.data_array

func _monster_state(opcode: int,tick: int,revision: int,hp: int,state: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	_header(writer,78,opcode)
	writer.put_64(1)
	writer.put_64(tick)
	writer.put_64(MONSTER_ID)
	writer.put_32(1)
	writer.put_32(0)
	writer.put_32(15)
	writer.put_float(40.0)
	writer.put_float(8.0)
	writer.put_64(revision)
	writer.put_32(hp)
	writer.put_32(100)
	writer.put_u8(state)
	writer.put_u8(1)
	writer.put_64(0)
	return writer.data_array

func _monster_despawn(tick: int,revision: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	_header(writer,40,WireCodec.OPCODE_MONSTER_DESPAWN)
	writer.put_64(1)
	writer.put_64(tick)
	writer.put_64(MONSTER_ID)
	writer.put_64(revision)
	return writer.data_array

func _attack_result(sequence: int,status: int,tick: int,damage: int,hp: int,revision: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	_header(writer,57,WireCodec.OPCODE_NORMAL_ATTACK_RESULT)
	writer.put_64(1)
	writer.put_u32(sequence)
	writer.put_u8(status)
	writer.put_64(tick)
	writer.put_64(MONSTER_ID)
	writer.put_32(damage)
	writer.put_32(hp)
	writer.put_32(100)
	writer.put_64(revision)
	return writer.data_array

func _move_receipt(sequence: int,tick: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	_header(writer,30,WireCodec.OPCODE_MOVE_RECEIPT)
	writer.put_64(1)
	writer.put_u32(sequence)
	writer.put_u16(0)
	writer.put_64(tick)
	return writer.data_array

func _self_state(tick: int,revision: int,target: Vector2i,moving: bool,action_sequence: int) -> PackedByteArray:
	var writer := StreamPeerBuffer.new()
	_header(writer,74,WireCodec.OPCODE_SELF_STATE)
	writer.put_64(1)
	writer.put_64(tick)
	writer.put_64(11)
	writer.put_64(2)
	writer.put_64(1)
	writer.put_32(15)
	writer.put_64(revision)
	writer.put_float(float(target.x))
	writer.put_float(float(target.y))
	writer.put_u8(1 if moving else 0)
	writer.put_u32(action_sequence)
	writer.put_u8(1)
	return writer.data_array
