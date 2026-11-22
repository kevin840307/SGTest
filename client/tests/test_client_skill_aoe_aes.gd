extends SceneTree
const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_SKILL_AOE_AES_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()!=1 or not str(args[0]).is_valid_int():
		_fail("expected port/certificate")
		return
	var session = Session.new()
	if session.connect_secure("127.0.0.1",int(args[0]),USERNAME,PASSWORD) != OK:
		_fail("connect",session)
		return
	var selecting := _wait(session,func(v):
		return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,5000)
	if selecting.is_empty() or selecting.characters.size()!=1:
		_fail("character list "+str(session.snapshot()),session)
		return
	if not FixtureLogin.provision_and_choose(session, int(selecting.characters[0].character_id)):
		_fail("choose",session)
		return
	var ready := _wait(session,func(v):
		return (
			int(v.state)==WorldState.STATE_IN_WORLD
			and bool(v.world_ready)
			and int(v.pending_world_ready_ack_epoch)==0
			and int(v.last_world_ready_ack_epoch)==int(v.map_epoch)
			and bool(v.combat_available)
			and int(v.player_current_mp)==2
			and int(v.player_max_mp)==2
			and v.monsters.size()==3
		),7000)
	if ready.is_empty():
		_fail("three-monster baseline "+str(session.snapshot()),session)
		return

	var near_ids: Array[int] = []
	var far_id := 0
	for raw_id in ready.monsters.keys():
		var entity_id := int(raw_id)
		var monster: Dictionary = ready.monsters[raw_id]
		var dx := absf(float(monster.x)-float(ready.x))
		var dy := absf(float(monster.y)-float(ready.y))
		if sqrt(dx*dx+dy*dy) <= 18.0:
			near_ids.append(entity_id)
		else:
			far_id=entity_id
	if near_ids.size()!=2 or far_id<=0:
		_fail("fixture radius partition "+str(ready.monsters),session)
		return
	if not session.cast_area_skill(99001):
		_fail("area skill intent refused",session)
		return

	var settled := _wait(session,func(v): return _area_settled(v,near_ids,far_id),7000)
	if settled.is_empty():
		_fail("authoritative area settlement "+str(session.snapshot()),session)
		return
	session.stop()
	print("CLIENT_SKILL_AOE_AES_PASS skill_id=99001 target=0 hits=2 near_hp=60,60 far_hp=100 mp=2_to_1 summary_damage=0 per_hit_visuals=2 monster_delta_authority=true")
	quit(0)

func _area_settled(v: Dictionary, near_ids: Array[int], far_id: int) -> bool:
	if int(v.last_skill_result_sequence)!=1 or int(v.last_skill_result_status)!=0:
		return false
	if int(v.last_skill_result.skill_id)!=99001 or int(v.last_skill_result.target_entity_id)!=0:
		return false
	if int(v.last_skill_result.damage)!=0 or int(v.last_skill_result.current_hp)!=0 or int(v.last_skill_result.max_hp)!=0 or int(v.last_skill_result.target_revision)!=0:
		return false
	if int(v.player_current_mp)!=1 or int(v.player_max_mp)!=2:
		return false
	for id in near_ids:
		if not v.monsters.has(id) or int(v.monsters[id].current_hp)!=60:
			return false
	if not v.monsters.has(far_id) or int(v.monsters[far_id].current_hp)!=100:
		return false
	var hit_targets := {}
	for event in v.player_skill_events:
		if int(event.get("skill_id",0))==99001 and int(event.get("skill_sequence",0))==1 and int(event.get("damage",0))==40:
			hit_targets[int(event.get("target_entity_id",0))]=true
	return hit_targets.size()==2 and hit_targets.has(near_ids[0]) and hit_targets.has(near_ids[1]) and not hit_targets.has(far_id)

func _wait(session, predicate: Callable, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value: Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		if int(value.state)==WorldState.STATE_FAILED and not bool(value.get("reconnecting",false)):
			return {}
		OS.delay_msec(2)
	return {}
