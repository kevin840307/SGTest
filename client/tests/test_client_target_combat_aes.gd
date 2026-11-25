extends SceneTree

const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_TARGET_COMBAT_AES_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()!=1 or not str(args[0]).is_valid_int():
		_fail("expected port")
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
	var character_id := int(selecting.characters[0].character_id)
	if not FixtureLogin.provision_and_choose(session,character_id):
		_fail("choose",session)
		return

	var ready := _wait(session,func(v):
		return (
			int(v.state)==WorldState.STATE_IN_WORLD
			and bool(v.world_ready)
			and int(v.pending_world_ready_ack_epoch)==0
			and int(v.last_world_ready_ack_epoch)==int(v.map_epoch)
			and bool(v.combat_available)
			and v.monsters.size()==1
		),7000)
	if ready.is_empty():
		_fail("WorldReady/monster baseline "+str(session.snapshot()),session)
		return
	var target := int(ready.monsters.keys()[0])
	if int(ready.monsters[target].current_hp)!=100:
		_fail("unexpected initial monster HP "+str(ready.monsters[target]),session)
		return
	if not session.attack_monster(target):
		_fail("attack intent refused",session)
		return

	var settled := _wait(session,func(v):
		if (
			not v.monsters.has(target)
			or not v.player_attack_sequences.has(character_id)
			or v.player_attack_events.is_empty()
			or int(v.last_attack_result_sequence)!=1
		):
			return false
		var result: Dictionary=v.last_attack_result
		var attack_sequence: Dictionary=v.player_attack_sequences[character_id]
		var attack_visual: Dictionary=v.player_attack_events[-1]
		return (
			int(v.last_attack_result_status)==0
			and int(result.get("target_entity_id",0))==target
			and int(result.get("damage",-1))==40
			and int(result.get("current_hp",-1))==60
			and int(v.monsters[target].current_hp)==60
			and int(v.monsters[target].revision)>=int(result.get("target_revision",0))
			and int(attack_sequence.get("sequence",0))==1
			and int(attack_visual.get("target_entity_id",0))==target
			and int(v.player_damage_sequence)==1
			and int(v.last_player_damage.get("damage",-1))==8
			and int(v.player_current_hp)==92
			and int(v.player_max_hp)==100
			and not bool(v.player_dead)
			and v.player_damage_visual_events.size()>=1
			and int(v.player_damage_visual_events[-1].victim_actor_id)==character_id
			and int(v.player_damage_visual_events[-1].damage_sequence)==1
			and int(v.player_damage_visual_events[-1].damage)==8
			and not bool(v.player_damage_visual_events[-1].died)
		),7000)
	if settled.is_empty():
		_fail("authoritative target combat settlement "+str(session.snapshot()),session)
		return
	session.stop()
	print("CLIENT_TARGET_COMBAT_AES_PASS actor=%d target=%d damage=40 monster_hp=60 retaliation_damage=8 player_hp=92 player_damage_visual=true own_attack_visual=true monster_delta_authority=true" % [character_id,target])
	quit(0)

func _wait(session, predicate: Callable, timeout_ms: int) -> Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value: Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		OS.delay_msec(10)
	return {}
