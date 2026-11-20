extends SceneTree

const Wire = preload("res://network/wire_codec.gd")

func _fail(message:String)->void:
	push_error("QUEST_WIRE_FAIL "+message)
	quit(1)

func _initialize()->void:
	var open:=Wire.encode_quest_open(9,7,88)
	var expected_open:=PackedByteArray([
		24,0,0,0,1,0,73,0,9,0,0,0,0,0,0,0,7,0,0,0,88,0,0,0])
	if open!=expected_open or Wire.frame_opcode(open)!=Wire.OPCODE_QUEST_OPEN:
		_fail("open golden mismatch")
		return

	var choose:=Wire.encode_quest_choose(9,8,88,91001,2)
	var expected_choose:=PackedByteArray([
		32,0,0,0,1,0,75,0,9,0,0,0,0,0,0,0,8,0,0,0,88,0,0,0,
		121,99,1,0,2,0,0,0])
	if choose!=expected_choose or Wire.frame_opcode(choose)!=Wire.OPCODE_QUEST_CHOOSE:
		_fail("choose golden mismatch")
		return

	var view_fixture:=PackedByteArray([
		110,0,0,0,1,0,74,0,9,0,0,0,0,0,0,0,8,0,0,0,1,88,0,0,0,
		121,99,1,0,3,0,0,0,1,1,0,0,0,4,0,0,0,12,0,21,0,2,
		228,187,187,229,139,153,230,168,153,233,161,140,
		232,171,139,233,129,184,230,147,135,228,184,139,228,184,128,
		230,173,165,227,128,130,
		2,0,0,0,6,0,229,174,140,230,136,144,
		3,0,0,0,12,0,231,168,141,229,190,140,229,134,141,232,170,170])
	var decoded:=Wire.decode_quest_view(view_fixture)
	if (
		decoded.is_empty() or int(decoded.map_epoch)!=9 or
		int(decoded.quest_sequence)!=8 or int(decoded.status)!=1 or
		int(decoded.npc_spawn_id)!=88 or int(decoded.quest_id)!=91001 or
		int(decoded.rule_revision)!=3 or int(decoded.state)!=1 or
		int(decoded.stage)!=1 or int(decoded.progress_revision)!=4 or
		str(decoded.title)!="任務標題" or str(decoded.text)!="請選擇下一步。" or
		(decoded.choices as Array).size()!=2 or
		int(decoded.choices[0].choice_id)!=2 or str(decoded.choices[0].label)!="完成" or
		int(decoded.choices[1].choice_id)!=3 or str(decoded.choices[1].label)!="稍後再說" or
		Wire.frame_opcode(view_fixture)!=Wire.OPCODE_QUEST_VIEW
	):
		_fail("view fixed fixture mismatch "+str(decoded))
		return

	var rejection:=_rejection_view(9,9,5,88)
	var rejected:=Wire.decode_quest_view(rejection)
	if rejected.is_empty() or int(rejected.status)!=5 or int(rejected.quest_id)!=0:
		_fail("no-view rejection mismatch")
		return

	var duplicate_choice:=view_fixture.duplicate()
	duplicate_choice[92]=2
	duplicate_choice[93]=0
	duplicate_choice[94]=0
	duplicate_choice[95]=0
	if not Wire.decode_quest_view(duplicate_choice).is_empty():
		_fail("duplicate choice id admitted")
		return

	var completed_with_choices:=view_fixture.duplicate()
	completed_with_choices[33]=2
	if not Wire.decode_quest_view(completed_with_choices).is_empty():
		_fail("completed view retained choices")
		return

	var invalid_utf8:=view_fixture.duplicate()
	invalid_utf8[47]=255
	if not Wire.decode_quest_view(invalid_utf8).is_empty():
		_fail("invalid UTF8 admitted")
		return

	if (
		not Wire.encode_quest_open(0,1,88).is_empty() or
		not Wire.encode_quest_open(9,0,88).is_empty() or
		not Wire.encode_quest_choose(9,1,88,0,2).is_empty() or
		not Wire.encode_quest_choose(9,1,88,91001,0).is_empty()
	):
		_fail("invalid command admitted")
		return

	print("QUEST_WIRE_PASS open=24 choose=32 view=110 utf8=true choices=2 malformed=FAIL_CLOSED")
	quit(0)

func _rejection_view(epoch:int,sequence:int,status:int,npc:int)->PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(Wire.QUEST_VIEW_HEADER_BYTES)
	writer.put_u16(Wire.VERSION)
	writer.put_u16(Wire.OPCODE_QUEST_VIEW)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(status)
	writer.put_32(npc)
	writer.put_32(0)
	writer.put_32(0)
	writer.put_u8(0)
	writer.put_32(0)
	writer.put_32(0)
	writer.put_u16(0)
	writer.put_u16(0)
	writer.put_u8(0)
	return writer.data_array
