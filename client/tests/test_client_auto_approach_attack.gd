extends SceneTree
const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")
const WireCodec = preload("res://wire/wire_codec.gd")
const FakeAutoAttackTransport = preload("res://tests/fake_auto_attack_transport.gd")

const MONSTER_ID := 1001500001

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_AUTO_APPROACH_ATTACK_FAIL "+message)
	quit(1)

func _initialize() -> void:
	if not _auto_approach_then_attack():
		return
	if not _resync_gap_preserves_chase():
		return
	if not _manual_move_cancels_chase():
		return
	print("CLIENT_AUTO_APPROACH_ATTACK_PASS server_range_authoritative=true out_of_range_triggers_existing_move=true route_stop_retries_attack=true bounded_chase=true resync_baseline_gap=true manual_move_cancels=true target_switch_replaces_navigation=true")
	quit(0)

func _auto_approach_then_attack() -> bool:
	var fake = FakeAutoAttackTransport.new(false)
	var session = Session.new(fake)
	if session.connect_secure("fixture",443,"user_01","Password-123456!","fixture",null) != OK:
		_fail("connect",session)
		return false
	var ready := _enter_world(session)
	if ready.is_empty():
		_fail("world/monster baseline "+str(session.snapshot()),session)
		return false
	if not session.attack_monster(MONSTER_ID):
		_fail("attack intent refused",session)
		return false
	var settled := _wait(session,func(v):
		return (
			int(v.last_attack_result_sequence)==2
			and int(v.last_attack_result_status)==0
			and int(v.attack_sequence)==2
			and int(v.movement_sequence)==1
			and int(v.last_receipt_sequence)==1
			and v.monsters.has(MONSTER_ID)
			and int(v.monsters[MONSTER_ID].current_hp)==70
		),2500)
	if settled.is_empty():
		_fail("auto approach did not settle "+str(session.snapshot()),session)
		return false
	var ops := fake.sent_opcodes()
	var moves := fake.move_targets()
	session.stop()
	var attack_ops := ops.filter(func(op): return int(op)==WireCodec.OPCODE_NORMAL_ATTACK)
	var move_ops := ops.filter(func(op): return int(op)==WireCodec.OPCODE_MOVE_TARGET)
	if attack_ops.size()!=2 or move_ops.size()!=1 or moves.size()!=1 or moves[0]!=Vector2i(40,8):
		_fail("unexpected attack/move sequence ops="+str(ops)+" moves="+str(moves))
		return false
	return true

func _resync_gap_preserves_chase() -> bool:
	var fake = FakeAutoAttackTransport.new(false,true)
	var session = Session.new(fake)
	if session.connect_secure("fixture",443,"user_01","Password-123456!","fixture",null) != OK:
		_fail("resync connect",session)
		return false
	var ready := _enter_world(session)
	if ready.is_empty():
		_fail("resync world/monster baseline "+str(session.snapshot()),session)
		return false
	if not session.attack_monster(MONSTER_ID):
		_fail("resync attack intent refused",session)
		return false
	var settled := _wait(session,func(v):
		return (
			int(v.resync_requests)==1
			and int(v.resync_completions)==1
			and int(v.attack_sequence)==2
			and int(v.last_attack_result_sequence)==2
			and int(v.last_attack_result_status)==0
			and int(v.movement_sequence)==1
			and v.monsters.has(MONSTER_ID)
			and int(v.monsters[MONSTER_ID].current_hp)==70
		),2500)
	if settled.is_empty():
		_fail("resync baseline gap cancelled chase "+str(session.snapshot()),session)
		return false
	session.stop()
	return true


func _manual_move_cancels_chase() -> bool:
	var fake = FakeAutoAttackTransport.new(true)
	var session = Session.new(fake)
	if session.connect_secure("fixture",443,"user_01","Password-123456!","fixture",null) != OK:
		_fail("cancel connect",session)
		return false
	var ready := _enter_world(session)
	if ready.is_empty():
		_fail("cancel world/monster baseline "+str(session.snapshot()),session)
		return false
	if not session.attack_monster(MONSTER_ID):
		_fail("cancel attack intent refused",session)
		return false
	var sent := _wait(session,func(v):
		return int(v.attack_sequence)==1 and fake.attack_count()==1,1500)
	if sent.is_empty():
		_fail("held attack was not sent",session)
		return false
	if not session.move_target(16,8):
		_fail("manual move did not replace chase",session)
		return false
	fake.release_first_attack_result()
	var moved := _wait(session,func(v):
		return int(v.movement_sequence)==1 and int(v.last_receipt_sequence)==1,1800)
	if moved.is_empty():
		_fail("manual replacement move did not settle "+str(session.snapshot()),session)
		return false
	OS.delay_msec(80)
	var attacks := fake.attack_count()
	var moves := fake.move_targets()
	session.stop()
	if attacks!=1 or moves.size()!=1 or moves[0]!=Vector2i(16,8):
		_fail("manual move failed to cancel auto chase attacks=%d moves=%s" % [attacks,str(moves)])
		return false
	return true

func _enter_world(session) -> Dictionary:
	var selecting := _wait(session,func(v):
		return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,1200)
	if selecting.is_empty() or selecting.characters.size()!=1:
		return {}
	if not session.choose_character(int(selecting.characters[0].character_id)):
		return {}
	return _wait(session,func(v):
		return (
			int(v.state)==WorldState.STATE_IN_WORLD
			and bool(v.world_ready)
			and int(v.pending_world_ready_ack_epoch)==0
			and int(v.last_world_ready_ack_epoch)==int(v.map_epoch)
			and v.monsters.has(MONSTER_ID)
		),1800)

func _wait(session, predicate: Callable, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value: Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		if int(value.state)==WorldState.STATE_FAILED:
			return {}
		OS.delay_msec(2)
	return {}
