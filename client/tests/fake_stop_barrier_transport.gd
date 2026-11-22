extends RefCounted
const WireCodec = preload("res://network/wire_codec.gd")
const STATE_STOPPED := 0
const STATE_CONNECTED := 3
const STATE_FAILED := 4

const AUTH_RESULT := "0900000001000B0000"
const CHARACTER_LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE_RESULT := "2D00000001000E00000B000000000000000F000000000000000000384000000000000038400100000000000000"
const READY := "2D00000001001400010000000000000028000000000000000B000000000000000F0000000000C0410000C04101"

var _mutex:=Mutex.new()
var _state:=STATE_STOPPED
var _events:Array[Dictionary]=[]
var _moves:Array[Vector2i]=[]
var _revision:=1
var _error:=OK

func start_encrypted(_host:String,_port:int,)->Error:
	_mutex.lock()
	_state=STATE_CONNECTED
	_events.clear()
	_moves.clear()
	_revision=1
	_error=OK
	_mutex.unlock()
	return OK

func send_frame(frame:PackedByteArray)->bool:
	var opcode:=WireCodec.frame_opcode(frame)
	if opcode<0:
		return false
	_mutex.lock()
	if _state!=STATE_CONNECTED:
		_mutex.unlock()
		return false
	match opcode:
		WireCodec.OPCODE_AUTH_REQUEST:
			_push(AUTH_RESULT.hex_decode())
			_push(CHARACTER_LIST.hex_decode())
		WireCodec.OPCODE_CHARACTER_CHOOSE:
			_push(CHOOSE_RESULT.hex_decode())
			_push(READY.hex_decode())
		WireCodec.OPCODE_WORLD_READY_ACK:
			_push(_self_state(41,24,24,false,0))
		WireCodec.OPCODE_PVP_MODE_COMMAND:
			pass # unrelated to ordered movement
		WireCodec.OPCODE_MOVE_TARGET:
			var move:=WireCodec.decode_move_target(frame)
			if move.is_empty():
				_fail()
			else:
				var seq:=int(move.movement_sequence)
				var target:=Vector2i(int(move.target_x),int(move.target_y))
				_moves.append(target)
				_push(_move_receipt(seq,41+seq))
				_push(_self_state(41+seq,target.x,target.y,false,seq))
		WireCodec.OPCODE_WORLD_RESYNC_REQUEST:
			pass
		_:
			_fail()
	var ok:=_state==STATE_CONNECTED
	_mutex.unlock()
	return ok

func drain_events(max_frames:int=64)->Array[Dictionary]:
	var out:Array[Dictionary]=[]
	_mutex.lock()
	for _i in range(mini(max_frames,_events.size())):
		out.append(_events.pop_front())
	_mutex.unlock()
	return out

func get_status()->Dictionary:
	_mutex.lock()
	var result={"state":_state,"terminal_error":_error}
	_mutex.unlock()
	return result

func stop()->void:
	_mutex.lock()
	_state=STATE_STOPPED
	_mutex.unlock()

func move_targets()->Array[Vector2i]:
	_mutex.lock()
	var result:=_moves.duplicate()
	_mutex.unlock()
	return result

func _push(frame:PackedByteArray)->void:
	_events.append({"type":"frame","frame":frame,"bytes":frame.size()})

func _fail()->void:
	_state=STATE_FAILED
	_error=ERR_INVALID_DATA

func _header(writer:StreamPeerBuffer,size:int,opcode:int)->void:
	writer.big_endian=false
	writer.put_u32(size)
	writer.put_u16(1)
	writer.put_u16(opcode)

func _move_receipt(sequence:int,tick:int)->PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	_header(writer,30,WireCodec.OPCODE_MOVE_RECEIPT)
	writer.put_64(1)
	writer.put_u32(sequence)
	writer.put_u16(0)
	writer.put_64(tick)
	return writer.data_array

func _self_state(tick:int,x:int,y:int,moving:bool,sequence:int)->PackedByteArray:
	_revision+=1
	var writer:=StreamPeerBuffer.new()
	_header(writer,74,WireCodec.OPCODE_SELF_STATE)
	writer.put_64(1)
	writer.put_64(tick)
	writer.put_64(11)
	writer.put_64(2)
	writer.put_64(1)
	writer.put_32(15)
	writer.put_64(_revision)
	writer.put_float(float(x))
	writer.put_float(float(y))
	writer.put_u8(1 if moving else 0)
	writer.put_u32(sequence)
	writer.put_u8(1)
	return writer.data_array
