extends SceneTree

const WireCodec = preload("res://network/wire_codec.gd")
const MOVE_TARGET_HEX := "1C00000001000100090000000000000007000000D819000078120000"
const MOVE_RECEIPT_HEX := "1E0000000100020009000000000000000700000000002A00000000000000"

func _fail(message: String) -> void:
	push_error("WIRE_CODEC_FAIL " + message)
	quit(1)

func _initialize() -> void:
	var move := WireCodec.encode_move_target(9, 7, 6616, 4728)
	if move.hex_encode().to_upper() != MOVE_TARGET_HEX:
		_fail("MoveTarget golden " + move.hex_encode())
		return
	var decoded := WireCodec.decode_move_target(move)
	if decoded != {"map_epoch": 9, "movement_sequence": 7, "target_x": 6616, "target_y": 4728}:
		_fail("MoveTarget roundtrip %s" % decoded)
		return

	var receipt := MOVE_RECEIPT_HEX.hex_decode()
	var receipt_decoded := WireCodec.decode_move_receipt(receipt)
	if receipt_decoded != {"map_epoch": 9, "movement_sequence": 7, "status": 0, "server_tick": 42}:
		_fail("MoveReceipt decode %s" % receipt_decoded)
		return

	for bad in [
		PackedByteArray(),
		move.slice(0, move.size() - 1),
		_mutate_u16(move, 4, 2),
		_mutate_u16(move, 6, 999),
		_mutate_i64(move, 8, 0),
		_mutate_u32(move, 16, 0),
	]:
		if not WireCodec.decode_move_target(bad).is_empty():
			_fail("malformed MoveTarget accepted")
			return

	var bad_receipt := _mutate_u16(receipt, 20, 999)
	if not WireCodec.decode_move_receipt(bad_receipt).is_empty():
		_fail("unknown receipt status accepted")
		return

	print("WIRE_CODEC_GODOT_PASS move_target_golden=28 move_receipt_golden=30 malformed=PASS identity_fields_absent=true")
	quit(0)

func _mutate_u16(source: PackedByteArray, offset: int, value: int) -> PackedByteArray:
	var copy := source.duplicate()
	copy[offset] = value & 0xff
	copy[offset + 1] = (value >> 8) & 0xff
	return copy

func _mutate_u32(source: PackedByteArray, offset: int, value: int) -> PackedByteArray:
	var copy := source.duplicate()
	for i in range(4):
		copy[offset + i] = (value >> (i * 8)) & 0xff
	return copy

func _mutate_i64(source: PackedByteArray, offset: int, value: int) -> PackedByteArray:
	var copy := source.duplicate()
	for i in range(8):
		copy[offset + i] = (value >> (i * 8)) & 0xff
	return copy
