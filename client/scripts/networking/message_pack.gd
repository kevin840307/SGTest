extends RefCounted

# Same bounded MessagePack subset as server/Transport/Wire.cs.
var stream := StreamPeerBuffer.new()
var valid := true

func encode(op: int, body: Array) -> PackedByteArray:
	stream = StreamPeerBuffer.new()
	stream.big_endian = true
	_write([2,op,body])
	return stream.data_array

func _write(value: Variant) -> void:
	match typeof(value):
		TYPE_NIL: stream.put_u8(0xc0)
		TYPE_BOOL: stream.put_u8(0xc3 if value else 0xc2)
		TYPE_INT:
			stream.put_u8(0xd3)
			stream.put_64(value)
		TYPE_FLOAT:
			stream.put_u8(0xcb)
			stream.put_double(value)
		TYPE_STRING:
			var bytes: PackedByteArray = value.to_utf8_buffer()
			stream.put_u8(0xdb)
			stream.put_u32(bytes.size())
			stream.put_data(bytes)
		TYPE_PACKED_BYTE_ARRAY:
			stream.put_u8(0xc6)
			stream.put_u32(value.size())
			stream.put_data(value)
		TYPE_ARRAY:
			stream.put_u8(0xdd)
			stream.put_u32(value.size())
			for entry in value:
				_write(entry)

func decode(bytes: PackedByteArray) -> Array:
	valid = bytes.size() <= 65536
	stream = StreamPeerBuffer.new()
	stream.big_endian = true
	stream.data_array = bytes
	var result: Variant = _read(0)
	if not valid or stream.get_available_bytes() != 0 or not result is Array or result.size() != 3:
		return []
	return result if result[0] == 2 and result[1] is int and result[2] is Array else []

func _need(count: int) -> bool:
	if count < 0 or count > stream.get_available_bytes():
		valid = false
	return valid

func _binary(count: int) -> PackedByteArray:
	return stream.get_data(count)[1] if _need(count) else PackedByteArray()

func _array(count: int, depth: int) -> Array:
	var result: Array = []
	if count < 0 or count > 4096:
		valid = false
		return result
	for i in count:
		result.append(_read(depth+1))
		if not valid:
			return []
	return result

func _read(depth: int) -> Variant:
	if depth > 8 or not _need(1):
		valid = false
		return null
	var marker := stream.get_u8()
	if marker <= 0x7f: return marker
	if marker >= 0xe0: return marker-256
	if marker&0xf0 == 0x90: return _array(marker&15,depth)
	if marker&0xe0 == 0xa0: return _binary(marker&31).get_string_from_utf8()
	match marker:
		0xc0: return null
		0xc2: return false
		0xc3: return true
		0xcc: return stream.get_u8() if _need(1) else null
		0xcd: return stream.get_u16() if _need(2) else null
		0xce: return stream.get_u32() if _need(4) else null
		0xd0: return stream.get_8() if _need(1) else null
		0xd1: return stream.get_16() if _need(2) else null
		0xd2: return stream.get_32() if _need(4) else null
		0xd3: return stream.get_64() if _need(8) else null
		0xca: return stream.get_float() if _need(4) else null
		0xcb: return stream.get_double() if _need(8) else null
		0xd9: return _binary(stream.get_u8()).get_string_from_utf8() if _need(1) else null
		0xda: return _binary(stream.get_u16()).get_string_from_utf8() if _need(2) else null
		0xdb: return _binary(stream.get_u32()).get_string_from_utf8() if _need(4) else null
		0xc4: return _binary(stream.get_u8()) if _need(1) else null
		0xc5: return _binary(stream.get_u16()) if _need(2) else null
		0xc6: return _binary(stream.get_u32()) if _need(4) else null
		0xdc: return _array(stream.get_u16(),depth) if _need(2) else null
		0xdd: return _array(stream.get_u32(),depth) if _need(4) else null
	valid = false
	return null
