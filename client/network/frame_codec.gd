extends RefCounted

# Wire framing only: no opcode registry, packet meanings, gameplay DTOs,
# socket ownership or protocol-version constants live in this module.
# The owning WireCodec supplies the immutable V1 limits and version.
static func reader(frame: PackedByteArray, offset: int) -> StreamPeerBuffer:
	var result := StreamPeerBuffer.new()
	result.big_endian = false
	result.data_array = frame
	result.seek(offset)
	return result


static func writer(frame_bytes: int, version: int, opcode: int) -> StreamPeerBuffer:
	var result := StreamPeerBuffer.new()
	result.big_endian = false
	result.put_u32(frame_bytes)
	result.put_u16(version)
	result.put_u16(opcode)
	return result


static func length_prefix(buffer: PackedByteArray, header_bytes: int, max_frame_bytes: int) -> int:
	if buffer.size() < 4:
		return -1
	var source := reader(buffer.slice(0, 4), 0)
	var frame_bytes := source.get_u32()
	if frame_bytes < header_bytes or frame_bytes > max_frame_bytes:
		return -1
	return frame_bytes


static func valid_header(
		frame: PackedByteArray, version: int, header_bytes: int, max_frame_bytes: int,
		expected_opcode: int, expected_bytes: int) -> bool:
	return frame.size() == expected_bytes and valid_variable_header(
		frame, version, header_bytes, max_frame_bytes, expected_opcode,
		expected_bytes, expected_bytes)


static func valid_variable_header(
		frame: PackedByteArray, version: int, header_bytes: int, max_frame_bytes: int,
		expected_opcode: int, minimum_bytes: int, maximum_bytes: int) -> bool:
	if (
		frame.size() < minimum_bytes or frame.size() > maximum_bytes or
		frame.size() < header_bytes or frame.size() > max_frame_bytes
	):
		return false
	var source := reader(frame, 0)
	var advertised := source.get_u32()
	var encoded_version := source.get_u16()
	var encoded_opcode := source.get_u16()
	return advertised == frame.size() and encoded_version == version and encoded_opcode == expected_opcode
