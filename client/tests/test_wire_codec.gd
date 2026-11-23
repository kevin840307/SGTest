extends SceneTree

const WireCodec = preload("res://network/wire_codec.gd")
const FrameCodec = preload("res://network/frame_codec.gd")
const MOVE_TARGET_HEX := "1C00000001000100090000000000000007000000D819000078120000"
const MOVE_RECEIPT_HEX := "1E0000000100020009000000000000000700000000002A00000000000000"
const AUTH_REQUEST_HEX := "2300000001000A0007001000757365725F303150617373776F72642D31323334353621"
const AUTH_RESULT_HEX := "0900000001000B0000"
const ACCOUNT_REGISTER_REQUEST_HEX := "230000000100110007001000757365725F303150617373776F72642D31323334353621"
const ACCOUNT_REGISTER_RESULT_HEX := "090000000100120000"
const CHARACTER_LIST_HEX := "2A00000001000C00020B000000000000000004004865726F16000000000000000107004865726F54776F"
const CHARACTER_CHOOSE_HEX := "1000000001000D001600000000000000"
const CHARACTER_CHOOSE_RESULT_HEX := "2D00000001000E000016000000000000000F0000000000000000C0BE40000000000010C6400300000000000000"
const CHARACTER_CREATE_HEX := "1200000001000F000207004E65774865726F"
const CHARACTER_CREATE_RESULT_HEX := "1B000000010010000021000000000000000207004E65774865726F"
const PLAYER_PROFILE_STATE_HEX := "49000000010085000B0000000000000002000000030000000400000040E2010000000000050000000600000007000000080000005A00000050000000460000003C0000000200000001"

func _fail(message: String) -> void:
	push_error("WIRE_CODEC_FAIL " + message)
	quit(1)

func _initialize() -> void:
	var move := WireCodec.encode_move_target(9, 7, 6616, 4728)
	if move.hex_encode().to_upper() != MOVE_TARGET_HEX:
		_fail("MoveTarget golden " + move.hex_encode())
		return
	if WireCodec.decode_move_target(move) != {"map_epoch": 9, "movement_sequence": 7, "target_x": 6616, "target_y": 4728}:
		_fail("MoveTarget roundtrip")
		return
	# Header framing is shared by all feature payloads but V1 values are
	# exclusively declared by WireCodec, not copied into FrameCodec.
	var header := FrameCodec.writer(
		WireCodec.MOVE_TARGET_FRAME_BYTES, WireCodec.VERSION, WireCodec.OPCODE_MOVE_TARGET)
	if header.data_array != move.slice(0, WireCodec.HEADER_BYTES):
		_fail("shared header writer changed golden V1 bytes")
		return
	var decoded_header := FrameCodec.reader(move, 8)
	if decoded_header.get_64() != 9 or decoded_header.get_u32() != 7:
		_fail("shared little-endian reader changed payload alignment")
		return
	if (
		not FrameCodec.valid_header(
			move, WireCodec.VERSION, WireCodec.HEADER_BYTES, WireCodec.MAX_FRAME_BYTES,
			WireCodec.OPCODE_MOVE_TARGET, WireCodec.MOVE_TARGET_FRAME_BYTES) or
		FrameCodec.valid_header(
			_mutate_u16(move, 4, 2), WireCodec.VERSION, WireCodec.HEADER_BYTES,
			WireCodec.MAX_FRAME_BYTES, WireCodec.OPCODE_MOVE_TARGET,
			WireCodec.MOVE_TARGET_FRAME_BYTES) or
		FrameCodec.valid_header(
			_mutate_u16(move, 6, 999), WireCodec.VERSION, WireCodec.HEADER_BYTES,
			WireCodec.MAX_FRAME_BYTES, WireCodec.OPCODE_MOVE_TARGET,
			WireCodec.MOVE_TARGET_FRAME_BYTES) or
		FrameCodec.valid_header(
			_mutate_u32(move, 0, 27), WireCodec.VERSION, WireCodec.HEADER_BYTES,
			WireCodec.MAX_FRAME_BYTES, WireCodec.OPCODE_MOVE_TARGET,
			WireCodec.MOVE_TARGET_FRAME_BYTES) or
		FrameCodec.valid_header(
			move.slice(0, move.size() - 1), WireCodec.VERSION, WireCodec.HEADER_BYTES,
			WireCodec.MAX_FRAME_BYTES, WireCodec.OPCODE_MOVE_TARGET,
			WireCodec.MOVE_TARGET_FRAME_BYTES)
	):
		_fail("shared header gate did not reject invalid V1 framing")
		return
	var receipt := MOVE_RECEIPT_HEX.hex_decode()
	if WireCodec.decode_move_receipt(receipt) != {"map_epoch": 9, "movement_sequence": 7, "status": 0, "server_tick": 42}:
		_fail("MoveReceipt decode")
		return
	var profile := WireCodec.decode_player_profile_state(PLAYER_PROFILE_STATE_HEX.hex_decode())
	if profile != {"character_id":11,"race":2,"career":3,"level":4,"experience":123456,
		"strength":5,"dexterity":6,"constitution":7,"wisdom":8,
		"attack":90,"defense":80,"hit":70,"dodge":60,"available_attribute_points":2,
		"combat_stats_verified":true}:
		_fail("PlayerProfileState decode "+str(profile))
		return
	var allocate:=WireCodec.encode_player_attribute_allocate(1)
	if allocate.hex_encode().to_upper()!="090000000100870001":
		_fail("PlayerAttributeAllocate golden "+allocate.hex_encode())
		return
	if not WireCodec.encode_player_attribute_allocate(0).is_empty():
		_fail("PlayerAttributeAllocate accepted invalid attribute")
		return
	var auth := WireCodec.encode_auth_request("user_01", "Password-123456!")
	if auth.hex_encode().to_upper() != AUTH_REQUEST_HEX:
		_fail("AuthRequest golden " + auth.hex_encode())
		return
	if WireCodec.decode_auth_result(AUTH_RESULT_HEX.hex_decode()) != {"status": 0}:
		_fail("AuthResult decode")
		return
	var registration:=WireCodec.encode_account_register_request(
		"user_01","Password-123456!")
	if registration.hex_encode().to_upper()!=ACCOUNT_REGISTER_REQUEST_HEX:
		_fail("AccountRegisterRequest golden "+registration.hex_encode())
		return
	if WireCodec.decode_account_register_result(
		ACCOUNT_REGISTER_RESULT_HEX.hex_decode())!={"status":0}:
		_fail("AccountRegisterResult decode")
		return
	var invalid_registration:=ACCOUNT_REGISTER_RESULT_HEX.hex_decode()
	invalid_registration[8]=9
	if not WireCodec.decode_account_register_result(invalid_registration).is_empty():
		_fail("unknown AccountRegisterResult status accepted")
		return
	var list := WireCodec.decode_character_list(CHARACTER_LIST_HEX.hex_decode())
	if list != {"characters": [{"character_id": 11, "slot": 0, "name": "Hero"}, {"character_id": 22, "slot": 1, "name": "HeroTwo"}]}:
		_fail("CharacterList decode %s" % list)
		return
	var create := WireCodec.encode_character_create(2,"NewHero")
	if create.hex_encode().to_upper() != CHARACTER_CREATE_HEX:
		_fail("CharacterCreate golden " + create.hex_encode())
		return
	var create_result := WireCodec.decode_character_create_result(
		CHARACTER_CREATE_RESULT_HEX.hex_decode())
	if create_result != {"status":0,"character_id":33,"slot":2,"name":"NewHero"}:
		_fail("CharacterCreateResult decode %s" % create_result)
		return
	var choose := WireCodec.encode_character_choose(22)
	if choose.hex_encode().to_upper() != CHARACTER_CHOOSE_HEX:
		_fail("CharacterChoose golden " + choose.hex_encode())
		return
	var chosen := WireCodec.decode_character_choose_result(CHARACTER_CHOOSE_RESULT_HEX.hex_decode())
	if chosen != {"status": 0, "character_id": 22, "map_id": 15, "x": 7872.0, "y": 11296.0, "map_epoch": 3}:
		_fail("CharacterChooseResult decode %s" % chosen)
		return
	for bad in [PackedByteArray(), move.slice(0, move.size() - 1), _mutate_u16(move, 4, 2), _mutate_u16(move, 6, 999), _mutate_i64(move, 8, 0), _mutate_u32(move, 16, 0)]:
		if not WireCodec.decode_move_target(bad).is_empty():
			_fail("malformed MoveTarget accepted")
			return
	if not WireCodec.decode_move_receipt(_mutate_u16(receipt, 20, 999)).is_empty():
		_fail("unknown receipt status accepted")
		return
	if not WireCodec.decode_auth_result(_mutate_u8(AUTH_RESULT_HEX.hex_decode(), 8, 9)).is_empty():
		_fail("unknown auth status accepted")
		return
	if not WireCodec.decode_character_list(CHARACTER_LIST_HEX.hex_decode().slice(0, 30)).is_empty():
		_fail("truncated CharacterList accepted")
		return
	if not WireCodec.decode_character_choose_result(_mutate_u8(CHARACTER_CHOOSE_RESULT_HEX.hex_decode(), 8, 9)).is_empty():
		_fail("unknown choose status accepted")
		return
	if not WireCodec.decode_character_create_result(
		_mutate_u8(CHARACTER_CREATE_RESULT_HEX.hex_decode(),8,9)).is_empty():
		_fail("unknown create status accepted")
		return
	if WireCodec.frame_length_prefix(PackedByteArray([1, 2, 3])) != -1 or WireCodec.frame_length_prefix(_mutate_u32(move, 0, 70000)) != -1:
		_fail("invalid frame length accepted")
		return
	print("WIRE_CODEC_GODOT_PASS move=28 receipt=30 auth=35 register=35 register_result=9 character_create=18 create_result=27 character_list=42 choose=16 choose_result=45 malformed=PASS identity_server_owned=true")
	quit(0)

func _mutate_u8(source: PackedByteArray, offset: int, value: int) -> PackedByteArray:
	var copy := source.duplicate()
	copy[offset] = value & 0xff
	return copy

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
