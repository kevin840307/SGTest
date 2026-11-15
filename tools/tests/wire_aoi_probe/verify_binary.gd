extends SceneTree

const HEX: String = "200000000100010039300000D8190000781200002A0000000300010007000000"

func _initialize() -> void:
	var data: PackedByteArray = HEX.hex_decode()
	var reader: StreamPeerBuffer = StreamPeerBuffer.new()
	reader.big_endian = false
	reader.data_array = data
	var fields: Array = [
		reader.get_u32(), reader.get_u16(), reader.get_u16(),
		reader.get_32(), reader.get_32(), reader.get_32(), reader.get_32(),
		reader.get_u8(), reader.get_u8(), reader.get_u16(), reader.get_u32()
	]
	var expected: Array = [32,1,1,12345,6616,4728,42,3,0,1,7]
	if fields != expected:
		push_error("WIRE_GODOT_DECODE_FAIL %s" % [fields])
		quit(1)
		return
	var writer: StreamPeerBuffer = StreamPeerBuffer.new()
	writer.big_endian = false
	writer.put_u32(32)
	writer.put_u16(1)
	writer.put_u16(1)
	writer.put_32(12345)
	writer.put_32(6616)
	writer.put_32(4728)
	writer.put_32(42)
	writer.put_u8(3)
	writer.put_u8(0)
	writer.put_u16(1)
	writer.put_u32(7)
	if writer.data_array.hex_encode().to_upper() != HEX:
		push_error("WIRE_GODOT_ENCODE_FAIL %s" % writer.data_array.hex_encode())
		quit(1)
		return
	print("WIRE_GODOT_GOLDEN_PASS CSharp_fixture_32_bytes_roundtrip")
	var sample: Dictionary = {"type":"MoveEntity","entityId":12345,"x":6616,"y":4728,"serverTick":42,"facing":3,"mapEpoch":1,"revision":7}
	var js: String = JSON.stringify(sample)
	const RUNS: int = 20000
	var checksum: int = 0
	var start: int = Time.get_ticks_usec()
	for i in range(RUNS):
		checksum += JSON.stringify(sample).length()
	var json_encode_us: float = float(Time.get_ticks_usec()-start)/RUNS
	start = Time.get_ticks_usec()
	for i in range(RUNS):
		var decoded: Variant = JSON.parse_string(js)
		checksum += int(decoded["entityId"])
	var json_decode_us: float = float(Time.get_ticks_usec()-start)/RUNS
	start = Time.get_ticks_usec()
	for i in range(RUNS):
		var b: StreamPeerBuffer = StreamPeerBuffer.new()
		b.put_u32(32)
		b.put_u16(1)
		b.put_u16(1)
		b.put_32(12345)
		b.put_32(6616)
		b.put_32(4728)
		b.put_32(42)
		b.put_u8(3)
		b.put_u8(0)
		b.put_u16(1)
		b.put_u32(7)
		checksum += b.data_array.size()
	var binary_encode_us: float = float(Time.get_ticks_usec()-start)/RUNS
	start = Time.get_ticks_usec()
	for i in range(RUNS):
		var b: StreamPeerBuffer = StreamPeerBuffer.new()
		b.data_array = data
		checksum += b.get_u32()
		checksum += b.get_u16()
		checksum += b.get_u16()
		checksum += b.get_32()
		checksum += b.get_32()
		checksum += b.get_32()
		checksum += b.get_32()
		checksum += b.get_u8()
		checksum += b.get_u8()
		checksum += b.get_u16()
		checksum += b.get_u32()
	var binary_decode_us: float = float(Time.get_ticks_usec()-start)/RUNS
	print("GODOT_BENCH json_encode_us=%.3f json_decode_us=%.3f binary_encode_us=%.3f binary_decode_us=%.3f runs=%d checksum=%d" % [json_encode_us,json_decode_us,binary_encode_us,binary_decode_us,RUNS,checksum])
	quit(0)
