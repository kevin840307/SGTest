extends SceneTree
const WireCodec=preload("res://wire/wire_codec.gd")
const WorldState=preload("res://world/world_state.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const SKILLS := "21000000010068000B0000000000000000000000010200C56D000003211C000001"

func fail(message:String)->void:
	push_error("SKILL_BOOK_FAIL "+message)
	quit(1)

func new_world():
	var world=WorldState.new()
	if (
		not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or
		not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or
		not world.apply_frame(CHOOSE.hex_decode())
	):
		return null
	return world

func _initialize()->void:
	var decoded:=WireCodec.decode_skill_book_snapshot(SKILLS.hex_decode())
	if (
		decoded.is_empty() or int(decoded.character_id)!=11 or
		not bool(decoded.complete) or decoded.skills.size()!=2 or
		int(decoded.skills[0].skill_id)!=28101 or int(decoded.skills[0].level)!=3 or
		int(decoded.skills[1].skill_id)!=7201 or int(decoded.skills[1].level)!=1
	):
		fail("CSharp skill book golden mismatch")
		return
	var bad:=SKILLS.hex_decode()
	bad[20]=2
	if not WireCodec.decode_skill_book_snapshot(bad).is_empty():
		fail("non-boolean final flag accepted")
		return
	bad=SKILLS.hex_decode()
	bad[27]=0
	if not WireCodec.decode_skill_book_snapshot(bad).is_empty():
		fail("zero skill level accepted")
		return
	bad=SKILLS.hex_decode()
	bad.encode_s32(28,28101)
	if not WireCodec.decode_skill_book_snapshot(bad).is_empty():
		fail("duplicate SkillID accepted")
		return

	var world=new_world()
	if world==null:
		fail("auth fixture")
		return
	var first:=SKILLS.hex_decode()
	first.resize(28)
	first.encode_s32(0,28)
	first[20]=0
	first.encode_u16(21,1)
	var second:=SKILLS.hex_decode().slice(0,23)
	second.encode_s32(0,28)
	second.encode_s32(16,1)
	second[20]=1
	second.encode_u16(21,1)
	second.append_array(SKILLS.hex_decode().slice(28,33))
	if world.apply_frame(second):
		fail("out-of-order skill chunk accepted")
		return
	world=new_world()
	if (
		not world.apply_frame(first) or bool(world.snapshot().skill_book_ready) or
		int(world.snapshot().skill_book.get(28101,0))!=3
	):
		fail("partial skill baseline")
		return
	if (
		not world.apply_frame(second) or not bool(world.snapshot().skill_book_ready) or
		int(world.snapshot().skill_book.get(7201,0))!=1 or
		int(world.snapshot().skill_book_revision)!=1
	):
		fail("completed skill baseline")
		return
	if world.apply_frame(second):
		fail("post-complete skill chunk accepted")
		return

	var fresh=new_world()
	var empty:=SKILLS.hex_decode()
	empty.resize(23)
	empty.encode_s32(0,23)
	empty.encode_u16(21,0)
	if fresh==null or not fresh.apply_frame(empty) or not bool(fresh.snapshot().skill_book_ready) or not fresh.snapshot().skill_book.is_empty():
		fail("empty authoritative skill baseline")
		return
	print("SKILL_BOOK_PASS server_owned=true skill=28101 level=3 bounded=256/4095 empty=COMPLETE")
	quit(0)
