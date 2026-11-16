extends SceneTree

const COMPACT_HEX := "39300000D8190000781200002A0000000300010007000000"
const MESSAGEPACK_HEX := "98CD3039CD19D8CD12782A03000107"
const PROTOBUF_HEX := "08B96010B06718F049202A280338014007"
const RUNS := 30000

func _fail(message: String) -> void:
	push_error(message)
	quit(1)

func _compact_encode() -> PackedByteArray:
	var b := StreamPeerBuffer.new()
	b.big_endian = false
	b.put_32(12345)
	b.put_32(6616)
	b.put_32(4728)
	b.put_u32(42)
	b.put_u8(3)
	b.put_u8(0)
	b.put_u16(1)
	b.put_u32(7)
	return b.data_array

func _compact_decode(data: PackedByteArray) -> Array:
	var b := StreamPeerBuffer.new()
	b.big_endian = false
	b.data_array = data
	return [b.get_32(), b.get_32(), b.get_32(), b.get_u32(), b.get_u8(), b.get_u8(), b.get_u16(), b.get_u32()]

func _mp_append_uint(out: PackedByteArray, value: int) -> void:
	if value <= 0x7f:
		out.append(value)
	elif value <= 0xff:
		out.append(0xcc)
		out.append(value)
	elif value <= 0xffff:
		out.append(0xcd)
		out.append((value >> 8) & 0xff)
		out.append(value & 0xff)
	else:
		out.append(0xce)
		out.append((value >> 24) & 0xff)
		out.append((value >> 16) & 0xff)
		out.append((value >> 8) & 0xff)
		out.append(value & 0xff)

func _mp_encode() -> PackedByteArray:
	var out := PackedByteArray()
	out.append(0x98)
	for value in [12345, 6616, 4728, 42, 3, 0, 1, 7]:
		_mp_append_uint(out, value)
	return out

func _mp_read_uint(data: PackedByteArray, state: Array) -> int:
	if state[0] >= data.size():
		return -1
	var token: int = data[state[0]]
	state[0] += 1
	if token <= 0x7f:
		return token
	if token == 0xcc:
		if state[0] >= data.size(): return -1
		var value: int = data[state[0]]
		state[0] += 1
		return value
	if token == 0xcd:
		if state[0] + 1 >= data.size(): return -1
		var value: int = (int(data[state[0]]) << 8) | int(data[state[0] + 1])
		state[0] += 2
		return value
	if token == 0xce:
		if state[0] + 3 >= data.size(): return -1
		var value: int = (int(data[state[0]]) << 24) | (int(data[state[0] + 1]) << 16) | (int(data[state[0] + 2]) << 8) | int(data[state[0] + 3])
		state[0] += 4
		return value
	return -1

func _mp_decode(data: PackedByteArray) -> Array:
	if data.is_empty() or data[0] != 0x98:
		return []
	var state: Array = [1]
	var values: Array = []
	for _i in range(8):
		var value := _mp_read_uint(data, state)
		if value < 0:
			return []
		values.append(value)
	if state[0] != data.size():
		return []
	return values

func _pb_append_varint(out: PackedByteArray, value: int) -> void:
	var current := value
	while current >= 0x80:
		out.append((current & 0x7f) | 0x80)
		current >>= 7
	out.append(current)

func _pb_append_field(out: PackedByteArray, field_number: int, value: int) -> void:
	if value == 0:
		return
	_pb_append_varint(out, field_number << 3)
	_pb_append_varint(out, value)

func _pb_zigzag(value: int) -> int:
	return (value << 1) ^ (value >> 63)

func _pb_unzigzag(value: int) -> int:
	return (value >> 1) ^ -(value & 1)

func _pb_encode() -> PackedByteArray:
	var out := PackedByteArray()
	_pb_append_field(out, 1, 12345)
	_pb_append_field(out, 2, _pb_zigzag(6616))
	_pb_append_field(out, 3, _pb_zigzag(4728))
	_pb_append_field(out, 4, 42)
	_pb_append_field(out, 5, 3)
	_pb_append_field(out, 6, 0)
	_pb_append_field(out, 7, 1)
	_pb_append_field(out, 8, 7)
	return out

func _pb_read_varint(data: PackedByteArray, state: Array) -> int:
	var result := 0
	var shift := 0
	while shift <= 63:
		if state[0] >= data.size():
			return -1
		var value: int = data[state[0]]
		state[0] += 1
		result |= (value & 0x7f) << shift
		if (value & 0x80) == 0:
			return result
		shift += 7
	return -1

func _pb_decode(data: PackedByteArray) -> Array:
	var state: Array = [0]
	var values: Array = [0, 0, 0, 0, 0, 0, 0, 0]
	while state[0] < data.size():
		var key := _pb_read_varint(data, state)
		if key < 0 or (key & 7) != 0:
			return []
		var field: int = key >> 3
		if field < 1 or field > 8:
			return []
		var value := _pb_read_varint(data, state)
		if value < 0:
			return []
		if field == 2 or field == 3:
			values[field - 1] = _pb_unzigzag(value)
		else:
			values[field - 1] = value
	return values

func _bench(name: String, encode: Callable, decode: Callable, fixture: PackedByteArray) -> int:
	var checksum := 0
	for _i in range(1000):
		checksum += encode.call().size()
		checksum += int(decode.call(fixture)[0])
	var start := Time.get_ticks_usec()
	for _i in range(RUNS):
		checksum += encode.call().size()
	var encode_us := float(Time.get_ticks_usec() - start) / RUNS
	start = Time.get_ticks_usec()
	for _i in range(RUNS):
		checksum += int(decode.call(fixture)[0])
	var decode_us := float(Time.get_ticks_usec() - start) / RUNS
	print("GODOT_CODEC_BENCH name=%s encode_us=%.3f decode_us=%.3f runs=%d checksum=%d scope=test_specific_fixture_codec" % [name, encode_us, decode_us, RUNS, checksum])
	return checksum

func _initialize() -> void:
	var expected: Array = [12345, 6616, 4728, 42, 3, 0, 1, 7]
	var compact := _compact_encode()
	var messagepack := _mp_encode()
	var protobuf := _pb_encode()
	if compact.hex_encode().to_upper() != COMPACT_HEX or _compact_decode(compact) != expected:
		_fail("GODOT_CODEC_FAIL compact")
		return
	if messagepack.hex_encode().to_upper() != MESSAGEPACK_HEX or _mp_decode(messagepack) != expected:
		_fail("GODOT_CODEC_FAIL messagepack")
		return
	if protobuf.hex_encode().to_upper() != PROTOBUF_HEX or _pb_decode(protobuf) != expected:
		_fail("GODOT_CODEC_FAIL protobuf")
		return
	print("GODOT_CODEC_FIXTURE_PASS compact=%d messagepack=%d protobuf=%d" % [compact.size(), messagepack.size(), protobuf.size()])
	var checksum := 0
	checksum += _bench("compact_binary", _compact_encode, _compact_decode, compact)
	checksum += _bench("messagepack", _mp_encode, _mp_decode, messagepack)
	checksum += _bench("protobuf", _pb_encode, _pb_decode, protobuf)
	print("GODOT_CODEC_COMPARE_RESULT candidates=3 cross_runtime_fixture=PASS checksum=%d" % checksum)
	quit(0)
