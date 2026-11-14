extends SceneTree

func _initialize() -> void:
	var wire := preload("res://scripts/message_pack.gd").new()
	var path := ProjectSettings.globalize_path("res://").path_join("../reports/wire_fixture.json")
	var fixture: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(path))
	var bytes: PackedByteArray = str(fixture.hex).hex_decode()
	var packet: Array = wire.decode(bytes)
	assert(packet.size() == 3 and packet[1] == 99)
	assert(packet[2][0] == 4294967295 and packet[2][1] == -1)
	assert(packet[2][2] == 1.25 and packet[2][3] == "方向")
	assert(packet[2][4] == PackedByteArray([1,254]))
	assert(wire.encode(99,packet[2]) == bytes)
	assert(wire.decode(bytes.slice(0,-1)).is_empty())
	assert(wire.decode(PackedByteArray([0xdd,255,255,255,255])).is_empty())
	print("C# / Godot MessagePack fixture passed")
	quit()
