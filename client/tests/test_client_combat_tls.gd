extends SceneTree
const Session = preload("res://network/client_session.gd")
const WorldState = preload("res://network/world_state.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_COMBAT_TLS_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()!=2 or not str(args[0]).is_valid_int():
		_fail("expected port/certificate")
		return
	var cert := X509Certificate.new()
	if cert.load(str(args[1])) != OK:
		_fail("certificate load")
		return
	var session = Session.new()
	if session.connect_secure("127.0.0.1",int(args[0]),USERNAME,PASSWORD,"localhost",cert) != OK:
		_fail("connect",session)
		return
	var selecting := _wait(session,func(v): return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,5000)
	if selecting.is_empty() or selecting.characters.size()!=1:
		_fail("character list "+str(session.snapshot()),session)
		return
	var character_id := int(selecting.characters[0].character_id)
	if not session.choose_character(character_id):
		_fail("choose",session)
		return

	var ready := _wait(session,func(v):
		return (
			int(v.state)==WorldState.STATE_IN_WORLD
			and bool(v.world_ready)
			and int(v.pending_world_ready_ack_epoch)==0
			and int(v.last_world_ready_ack_epoch)==int(v.map_epoch)
			and bool(v.combat_available)
			and int(v.player_max_hp)==100
			and v.monsters.size()==1
		),7000)
	if ready.is_empty():
		_fail("monster baseline "+str(session.snapshot()),session)
		return
	var monster_ids: Array = ready.monsters.keys()
	var target := int(monster_ids[0])
	if int(ready.monsters[target].current_hp)!=100:
		_fail("unexpected initial monster HP "+str(ready.monsters[target]),session)
		return
	if not session.attack_monster(target):
		_fail("attack intent refused",session)
		return

	var settled := _wait(session,func(v):
		return (
			int(v.state)==WorldState.STATE_IN_WORLD
			and int(v.last_attack_result_sequence)==1
			and int(v.last_attack_result_status)==0
			and int(v.last_attack_result.target_entity_id)==target
			and int(v.last_attack_result.damage)==60
			and int(v.last_attack_result.current_hp)==40
			and v.monsters.has(target)
			and int(v.monsters[target].current_hp)==40
			and int(v.monsters[target].revision)>=int(v.last_attack_result.target_revision)
			and int(v.player_damage_sequence)==1
			and int(v.last_player_damage.damage)==18
			and int(v.player_current_hp)==0
			and bool(v.player_dead)
			and v.player_damage_visual_events.size()>=1
			and int(v.player_damage_visual_events[-1].victim_actor_id)==character_id
			and int(v.player_damage_visual_events[-1].damage_sequence)==1
			and int(v.player_damage_visual_events[-1].damage)==18
			and bool(v.player_damage_visual_events[-1].died)
		),7000)
	if settled.is_empty():
		_fail("authoritative attack settlement "+str(session.snapshot()),session)
		return
	if session.move_target(80,24) or session.attack_monster(target) or session.npc_interact(1):
		_fail("dead public gameplay APIs accepted intent",session)
		return
	if not session.respawn():
		_fail("respawn intent refused",session)
		return
	var revived := _wait(session,func(v):
		return (
			int(v.last_respawn_result_sequence)==1
			and int(v.last_respawn_result_status)==0
			and int(v.player_current_hp)==100
			and int(v.player_max_hp)==100
			and not bool(v.player_dead)
			and int(v.player_damage_sequence)==1
			and v.player_respawn_visual_events.size()>=1
			and int(v.player_respawn_visual_events[-1].actor_id)==character_id
			and int(v.player_respawn_visual_events[-1].respawn_sequence)==1
		),7000)
	if revived.is_empty():
		_fail("authoritative respawn "+str(session.snapshot()),session)
		return
	if not session.move_target(24,24):
		_fail("move remained client-gated after respawn",session)
		return
	var moved := _wait(session,func(v):
		return int(v.last_receipt_sequence)==1 and int(v.last_receipt_status)==0,5000)
	if moved.is_empty():
		_fail("server World remained dead-gated after respawn "+str(session.snapshot()),session)
		return
	session.stop()
	print("CLIENT_COMBAT_TLS_PASS target=%d player_damage=60 monster_hp=40 monster_damage=18 death_hp=0 respawn_hp=100 dead=false player_damage_visual=true player_respawn_visual=true visual_after_db_commit=true public_intents_blocked_while_dead=true move_after_respawn=true damage_sequence_continuous=true request_has_no_damage=true receipt_does_not_mutate_monster=true delta_owns_hp=true damage_after_db_commit=true" % target)
	quit(0)

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
