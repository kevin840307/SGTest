extends SceneTree

# Candidate framing fixture ONLY; not the production Godot transport.
const HEX: String = "200000000100010039300000D8190000781200002A0000000300010007000000"
const MAX_FRAME: int = 64
var failures: int = 0

func _check(ok: bool, name: String) -> void:
	if not ok:
		failures += 1
		push_error("WIRE_GODOT_FRAMING_FAIL " + name)

func _read_le_u32(bytes: PackedByteArray) -> int:
	var reader: StreamPeerBuffer = StreamPeerBuffer.new()
	reader.big_endian = false
	reader.data_array = bytes
	return reader.get_u32()

func _parse_stream(chunks: Array) -> Dictionary:
	var pending: PackedByteArray = PackedByteArray()
	var frames: Array[String] = []
	for chunk in chunks:
		pending.append_array(chunk)
		while pending.size() >= 4:
			var length: int = _read_le_u32(pending)
			if length < 8 or length > MAX_FRAME:
				return {"ok": false, "frames": frames, "pending": 0}
			if pending.size() < length:
				break
			var header: StreamPeerBuffer = StreamPeerBuffer.new()
			header.big_endian = false
			header.data_array = pending.slice(0, length)
			header.seek(4)
			if header.get_u16() != 1 or header.get_u16() != 1 or length != 32:
				return {"ok": false, "frames": frames, "pending": 0}
			frames.append(pending.slice(0, length).hex_encode().to_upper())
			pending = pending.slice(length)
	return {"ok": true, "frames": frames, "pending": pending.size()}

func _prefix(length: int) -> PackedByteArray:
	var writer: StreamPeerBuffer = StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(length)
	return writer.data_array

func _initialize() -> void:
	var golden: PackedByteArray = HEX.hex_decode()
	for split in range(33):
		var parsed: Dictionary = _parse_stream([golden.slice(0, split), PackedByteArray(), golden.slice(split)])
		_check(parsed["ok"] and parsed["frames"] == [HEX] and parsed["pending"] == 0, "split %d" % split)

	var second: PackedByteArray = golden.duplicate()
	second.encode_s32(8, 54321)
	var joined: PackedByteArray = golden.duplicate()
	joined.append_array(second)
	joined.append_array(golden)
	var second_hex: String = second.hex_encode().to_upper()
	for seed in range(1, 101):
		var rng: RandomNumberGenerator = RandomNumberGenerator.new()
		rng.seed = seed
		var chunks: Array = []
		var offset: int = 0
		while offset < joined.size():
			var count: int = mini(rng.randi_range(1, 40), joined.size() - offset)
			chunks.append(joined.slice(offset, offset + count))
			offset += count
		var parsed: Dictionary = _parse_stream(chunks)
		_check(parsed["ok"] and parsed["frames"] == [HEX, second_hex, HEX] and parsed["pending"] == 0,
			"coalescing seed %d" % seed)

	for bad in [0, 1, 7, 65, 4294967295]:
		var parsed: Dictionary = _parse_stream([_prefix(bad)])
		_check(not parsed["ok"] and parsed["frames"].is_empty() and parsed["pending"] == 0,
			"invalid length %s" % bad)

	var short_move: PackedByteArray = golden.slice(0, 8)
	short_move[0] = 8
	_check(not _parse_stream([short_move])["ok"], "wrong Move payload length")
	var wrong_version: PackedByteArray = golden.duplicate()
	wrong_version[4] = 2
	var wrong_opcode: PackedByteArray = golden.duplicate()
	wrong_opcode[6] = 255
	_check(not _parse_stream([wrong_version])["ok"], "wrong protocol version")
	_check(not _parse_stream([wrong_opcode])["ok"], "wrong opcode")

	var pending: Dictionary = _parse_stream([golden.slice(0, 19)])
	_check(pending["ok"] and pending["frames"].is_empty() and pending["pending"] == 19,
		"truncated frame remains pending")
	# Reset between connections: do not feed previous connection bytes into a new stream.
	var restarted: Dictionary = _parse_stream([golden])
	_check(restarted["ok"] and restarted["frames"] == [HEX], "new connection starts clean")
	var malformed: PackedByteArray = golden.duplicate()
	malformed.append_array(_prefix(0))
	var combined: Dictionary = _parse_stream([malformed])
	_check(not combined["ok"] and combined["frames"] == [HEX], "valid then invalid prefix")
	if failures == 0:
		print("WIRE_GODOT_FRAMING_PASS splits=33 randomized_streams=100 invalid_prefixes=5 invalid_headers=3 reset=PASS")
		quit(0)
	else:
		quit(1)
