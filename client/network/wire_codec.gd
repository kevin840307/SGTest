extends RefCounted

const VERSION := 1
const HEADER_BYTES := 8
const MAX_FRAME_BYTES := 65536
const OPCODE_MOVE_TARGET := 1
const OPCODE_MOVE_RECEIPT := 2
const MOVE_TARGET_FRAME_BYTES := 28
const MOVE_RECEIPT_FRAME_BYTES := 30

static func encode_move_target(map_epoch: int, movement_sequence: int, target_x: int, target_y: int) -> PackedByteArray:
	if map_epoch <= 0 or movement_sequence <= 0 or movement_sequence > 0xffffffff:
		return PackedByteArray()
	var writer := StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(MOVE_TARGET_FRAME_BYTES)
	writer.put_u16(VERSION)
	writer.put_u16(OPCODE_MOVE_TARGET)
	writer.put_64(map_epoch)
	writer.put_u32(movement_sequence)
	writer.put_32(target_x)
	writer.put_32(target_y)
	return writer.data_array

static func decode_move_target(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_MOVE_TARGET, MOVE_TARGET_FRAME_BYTES):
		return {}
	var reader := StreamPeerBuffer.new()
	reader.big_endian = false
	reader.data_array = frame
	reader.seek(8)
	var map_epoch := reader.get_64()
	var sequence := reader.get_u32()
	var target_x := reader.get_32()
	var target_y := reader.get_32()
	if map_epoch <= 0 or sequence == 0:
		return {}
	return {
		"map_epoch": map_epoch,
		"movement_sequence": sequence,
		"target_x": target_x,
		"target_y": target_y,
	}

static func decode_move_receipt(frame: PackedByteArray) -> Dictionary:
	if not _valid_header(frame, OPCODE_MOVE_RECEIPT, MOVE_RECEIPT_FRAME_BYTES):
		return {}
	var reader := StreamPeerBuffer.new()
	reader.big_endian = false
	reader.data_array = frame
	reader.seek(8)
	var map_epoch := reader.get_64()
	var sequence := reader.get_u32()
	var status := reader.get_u16()
	var server_tick := reader.get_64()
	if map_epoch <= 0 or sequence == 0 or status > 10 or server_tick < 0:
		return {}
	return {
		"map_epoch": map_epoch,
		"movement_sequence": sequence,
		"status": status,
		"server_tick": server_tick,
	}

static func _valid_header(frame: PackedByteArray, expected_opcode: int, expected_bytes: int) -> bool:
	if frame.size() != expected_bytes or frame.size() < HEADER_BYTES or frame.size() > MAX_FRAME_BYTES:
		return false
	var reader := StreamPeerBuffer.new()
	reader.big_endian = false
	reader.data_array = frame
	var advertised := reader.get_u32()
	var version := reader.get_u16()
	var opcode := reader.get_u16()
	return advertised == frame.size() and version == VERSION and opcode == expected_opcode
