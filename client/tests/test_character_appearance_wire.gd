extends SceneTree
const Codec = preload("res://network/wire_codec.gd")
const State = preload("res://world/world_state.gd")
const EMPTY_HEX := "1800000001000c0000020200000000000000010300070201"
const LIST_HEX := "4800000001000c00020b000000000000000006004c6567616379160000000000000001060043686f73656e0200000000000000010103000702010200000000000000010300070201"
const CREATE_HEX := "1700000001000f0001060043686f73656e010300070201"
const RESULT_HEX := "200000000100100000160000000000000001060043686f73656e010300070201"
const LEGACY_LIST_HEX := "4000000001000c00020b000000000000000006004c6567616379160000000000000001060043686f73656e010000000000010103000702000000000001030007"
const LEGACY_RESULT_HEX := "1e0000000100100000160000000000000001060043686f73656e01030007"
const LOOK := {"race":1,"hair_index":3,"hair_color":7,"body_index":2,"leg_index":1}
const LEGACY_LOOK := {"race":1,"hair_index":3,"hair_color":7}
var failed := false

func check(ok: bool, reason: String) -> void:
	if not ok:
		failed = true
		push_error("CHARACTER_APPEARANCE_WIRE_FAIL "+reason)

func _initialize() -> void:
	var empty := Codec.decode_character_list(EMPTY_HEX.hex_decode())
	check(empty.get("creation_appearances",[]).size()==2,"allowed choices missing")
	var list := Codec.decode_character_list(LIST_HEX.hex_decode())
	check(list.characters[0].appearance.is_empty() and list.characters[1].appearance==LOOK,"unknown/known appearance lost")
	check(Codec.encode_character_create(1,"Chosen",LOOK).hex_encode()==CREATE_HEX,"C# create golden mismatch")
	check(Codec.decode_character_create_result(RESULT_HEX.hex_decode()).get("appearance",{})==LOOK,"C# result golden mismatch")
	var legacy_list := Codec.decode_character_list(LEGACY_LIST_HEX.hex_decode())
	check(legacy_list.characters[1].appearance==LEGACY_LOOK,"legacy four-byte list no longer decodes")
	check(Codec.decode_character_create_result(LEGACY_RESULT_HEX.hex_decode()).get("appearance",{})==LEGACY_LOOK,"legacy four-byte result no longer decodes")
	for at in [0,8,10,11,12,13,14,15,16,17,18,19]:
		var short := EMPTY_HEX.hex_decode().slice(0,at)
		if at>=8:
			short.encode_u32(0,at)
		check(Codec.decode_character_list(short).is_empty(),"truncated menu accepted")
	for at in [9,12,15,18,21]:
		var bad := EMPTY_HEX.hex_decode()
		bad[at] = 255
		check(Codec.decode_character_list(bad).is_empty(),"invalid schema/race/color accepted")
	var duplicate := EMPTY_HEX.hex_decode()
	for i in 6:
		duplicate[18+i] = duplicate[12+i]
	check(Codec.decode_character_list(duplicate).is_empty(),"duplicate choices accepted")
	var unknown := LIST_HEX.hex_decode()
	unknown[45] = 1
	check(Codec.decode_character_list(unknown).is_empty(),"unknown look contained nonzero bytes")
	check(Codec.encode_character_create(1,"Chosen",{"race":1.0,"hair_index":3,"hair_color":7}).is_empty(),"float appearance silently coerced")
	var state := State.new()
	state.state = State.STATE_WAIT_CHARACTER_LIST
	check(state.apply_frame(EMPTY_HEX.hex_decode()),"reducer rejected menu")
	check(not state.mark_character_create_sent(1,"Chosen"),"appearance selection bypassed required menu")
	check(not state.mark_character_create_sent(1,"Chosen",{"race":2,"hair_index":3,"hair_color":7}),"unlisted choice admitted")
	var requested := LOOK.duplicate()
	check(state.mark_character_create_sent(1,"Chosen",requested),"valid choice not admitted")
	requested.hair_index = 99
	check(state.apply_frame(RESULT_HEX.hex_decode()) and state.apply_frame(LIST_HEX.hex_decode()),"result/list did not agree with copied request")
	var snap := state.snapshot()
	snap.character_creation_appearances[1].hair_index = 99
	snap.characters[1].appearance.hair_index = 99
	check(state.last_created_appearance==LOOK and state.character_creation_appearances[1]==LOOK and state.characters[1].appearance==LOOK,"UI snapshot mutated authoritative appearance")
	var forged := State.new()
	forged.state = State.STATE_WAIT_CHARACTER_LIST
	forged.apply_frame(EMPTY_HEX.hex_decode())
	forged.mark_character_create_sent(1,"Chosen",LOOK)
	var mismatch := RESULT_HEX.hex_decode()
	mismatch[mismatch.size()-2] = 0
	check(not forged.apply_frame(mismatch),"unrequested successful appearance accepted")
	var maximum := StreamPeerBuffer.new()
	maximum.big_endian = false
	maximum.put_u32(Codec.MAX_CHARACTER_LIST_FRAME_BYTES)
	maximum.put_u16(1)
	maximum.put_u16(12)
	maximum.put_u8(32)
	for i in 32:
		maximum.put_64(i+1)
		maximum.put_u8(i)
		maximum.put_u16(96)
		maximum.put_data("x".repeat(96).to_utf8_buffer())
	maximum.put_u8(2)
	for i in 32:
		maximum.put_u8(1)
		maximum.put_data(PackedByteArray([1,3,0,7,2,1]))
	maximum.put_u16(4096)
	for i in 4096:
		maximum.put_u8(0)
		maximum.put_u16(i)
		maximum.put_u8(0)
		maximum.put_u8(0)
		maximum.put_u8(0)
	var decoded_max := Codec.decode_character_list(maximum.data_array)
	check(maximum.data_array.size()==Codec.MAX_CHARACTER_LIST_FRAME_BYTES and decoded_max.get("characters",[]).size()==32 and decoded_max.get("creation_appearances",[]).size()==4096,"maximum bounded 32-character/4096-choice frame failed")
	if not failed:
		print("CHARACTER_APPEARANCE_WIRE_PASS CSharp_golden=true bounded_extension=true unknown_preserved=true exact_options=true request_copy=true result_correlation=true snapshot_copy=true")
	quit(1 if failed else 0)
