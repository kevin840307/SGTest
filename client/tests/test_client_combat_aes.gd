extends SceneTree
const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_COMBAT_AES_FAIL "+message)
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
	var selecting := _wait(session,func(v): return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,5000)
	if selecting.is_empty() or selecting.characters.size()!=1:
		_fail("character list "+str(session.snapshot()),session)
		return
	var character_id := int(selecting.characters[0].character_id)
	if not FixtureLogin.provision_and_choose(session, character_id):
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
			and int(v.player_current_mp)==2
			and int(v.player_max_mp)==2
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
	if not session.cast_skill(99001,target):
		_fail("skill intent refused",session)
		return
	var skilled := _wait(session,func(v):
		return (
			int(v.last_skill_result_sequence)==1
			and int(v.last_skill_result_status)==0
			and int(v.last_skill_result.skill_id)==99001
			and int(v.last_skill_result.target_entity_id)==target
			and int(v.last_skill_result.damage)==40
			and int(v.last_skill_result.current_hp)==0
			and int(v.player_current_mp)==1
			and int(v.player_max_mp)==2
			and int(v.player_damage_sequence)==1
			and v.monsters.has(target)
			and int(v.monsters[target].current_hp)==0
			and int(v.monsters[target].revision)>=int(v.last_skill_result.target_revision)
		),7000)
	if skilled.is_empty():
		_fail("authoritative skill settlement "+str(session.snapshot()),session)
		return

	var experienced := _wait(session,func(v):
		return (
			int(v.player_experience)==1
			and int(v.player_experience_percent)==10
		),7000)
	if experienced.is_empty():
		_fail("authoritative EXP profile/HUD state "+str(session.snapshot()),session)
		return

	var dropped := _wait(session,func(v):
		return (
			v.ground_drops.size()==1
			and int(v.last_skill_result.current_hp)==0
		),7000)
	if dropped.is_empty():
		_fail("authoritative v2 loot did not enter client AOI "+str(session.snapshot()),session)
		return
	var drop_ids: Array = dropped.ground_drops.keys()
	var drop_id := int(drop_ids[0])
	var drop: Dictionary = dropped.ground_drops[drop_id]
	if int(drop.item_definition_id)!=2701 or int(drop.quantity)!=2:
		_fail("unexpected v2 loot payload "+str(drop),session)
		return
	if not session.pickup_ground_drop(drop_id):
		_fail("pickup intent refused for authoritative ground UID",session)
		return
	var picked := _wait(session,func(v):
		return (
			int(v.last_pickup_result_sequence)==1
			and int(v.last_pickup_result_status)==0
			and int(v.last_pickup_result.drop_id)==drop_id
			and int(v.last_pickup_result.item_definition_id)==2701
			and int(v.last_pickup_result.quantity)==2
			and v.received_item_grants.size()==1
			and v.ground_drops.is_empty()
		),7000)
	if picked.is_empty():
		_fail("SQLite pickup result/AOI despawn did not converge "+str(session.snapshot()),session)
		return

	# Wait for the same authoritative monster generation to respawn, then try a
	# synthetic rule whose MP cost exceeds the remaining durable MP.
	var respawned_monster := _wait(session,func(v):
		return (
			v.monsters.has(target)
			and int(v.monsters[target].current_hp)==100
			and int(v.monsters[target].max_hp)==100
			and int(v.monsters[target].revision)>int(skilled.monsters[target].revision)
		),7000)
	if respawned_monster.is_empty():
		_fail("monster did not respawn for insufficient-MP gate "+str(session.snapshot()),session)
		return
	var visual_count := int(respawned_monster.player_skill_events.size())
	if not session.cast_skill(99002,target):
		_fail("insufficient-MP skill intent refused before server authority",session)
		return
	var insufficient := _wait(session,func(v):
		return (
			int(v.last_skill_result_sequence)==2
			and int(v.last_skill_result_status)==11
			and int(v.last_skill_result.skill_id)==99002
			and int(v.player_current_mp)==1
			and int(v.player_max_mp)==2
			and v.monsters.has(target)
			and int(v.monsters[target].current_hp)==100
		),7000)
	if insufficient.is_empty():
		_fail("insufficient-MP result "+str(session.snapshot()),session)
		return
	if int(insufficient.player_skill_events.size())!=visual_count:
		_fail("rejected MP skill emitted presentation damage",session)
		return
	session.stop()
	OS.delay_msec(50)

	var relog = Session.new()
	if relog.connect_secure("127.0.0.1",int(args[0]),USERNAME,PASSWORD) != OK:
		_fail("relog connect",relog)
		return
	var relog_selecting := _wait(relog,func(v):
		return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,5000)
	if relog_selecting.is_empty() or relog_selecting.characters.size()!=1:
		_fail("relog character list "+str(relog.snapshot()),relog)
		return
	if not FixtureLogin.provision_and_choose(relog, character_id):
		_fail("relog choose",relog)
		return
	var relog_ready := _wait(relog,func(v):
		if (
			int(v.state)!=WorldState.STATE_IN_WORLD
			or not bool(v.world_ready)
			or not bool(v.inventory_ready)
			or int(v.player_experience)!=1
			or int(v.player_experience_percent)!=10
			or v.inventory_items.size()!=1
			or not v.ground_drops.is_empty()
			or not v.received_item_grants.is_empty()
		):
			return false
		var values: Array = v.inventory_items.values()
		var item: Dictionary = values[0]
		return (
			int(item.item_definition_id)==2701
			and int(item.quantity)==2
			and int(item.drop_id)==drop_id
		),7000)
	if relog_ready.is_empty():
		_fail("relog inventory/ground-drop persistence "+str(relog.snapshot()),relog)
		return
	relog.stop()
	print("CLIENT_COMBAT_AES_PASS target=%d player_damage=60 normal_attack_hp=40 skill_id=99001 skill_damage=40 skill_mp=2_to_1 experience=1 experience_percent=10 exp_profile_live=true exp_profile_relog=true loot_v2=true ground_uid=true item=2701 quantity=2 pickup=true atomic_inventory=true aoidespawn=true relog_inventory=true ground_drop_absent_after_relog=true insufficient_mp_status=11 rejected_skill_no_damage=true monster_respawn_hp=100 monster_damage=18 death_hp=0 respawn_hp=100 dead=false skill_request_has_no_damage=true skill_receipt_does_not_mutate_monster=true player_damage_visual=true player_respawn_visual=true visual_after_db_commit=true public_intents_blocked_while_dead=true move_after_respawn=true damage_sequence_continuous=true request_has_no_damage=true receipt_does_not_mutate_monster=true delta_owns_hp=true damage_after_db_commit=true" % target)
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
