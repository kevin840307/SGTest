extends SceneTree

const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_COMBAT_OBSERVER_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()!=3 or not str(args[0]).is_valid_int() or str(args[2]) not in ["observer","actor"]:
		_fail("expected AES port, certificate path, observer|actor")
		return
	var role := str(args[2])
	var cert = null
	var username := "fixture_one" if role=="observer" else "fixture_two"
	var session = Session.new()
	if session.connect_secure("127.0.0.1",int(args[0]),username,PASSWORD,"localhost",cert) != OK:
		_fail("connect",session)
		return
	var selecting := _wait(session,func(v):
		return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,7000)
	if selecting.is_empty() or selecting.characters.size()!=1:
		_fail("character list "+str(session.snapshot()),session)
		return
	var self_id := int(selecting.characters[0].character_id)
	if not session.choose_character(self_id):
		_fail("choose",session)
		return
	var ready := _wait(session,func(v):
		return (
			int(v.state)==WorldState.STATE_IN_WORLD
			and bool(v.world_ready)
			and int(v.pending_world_ready_ack_epoch)==0
			and int(v.last_world_ready_ack_epoch)==int(v.map_epoch)
			and v.monsters.size()==1
		),7000)
	if ready.is_empty():
		_fail("own WorldReady/monster baseline "+str(session.snapshot()),session)
		return
	if role=="observer":
		# The actor process is intentionally started only after this marker.
		# Do not require its Spawn here or the two processes deadlock.
		print("CLIENT_COMBAT_OBSERVER_READY role=observer actor=%d" % self_id)
		_observe(session,self_id)
	else:
		var shared := _wait(session,func(v):
			return v.entities.size()==1 and v.monsters.size()==1,7000)
		if shared.is_empty():
			_fail("actor did not receive observer Spawn "+str(session.snapshot()),session)
			return
		_attack(session,self_id)

func _observe(session, self_id: int) -> void:
	# The observer deliberately announces readiness before the actor process is
	# started. Wait for that actor's authoritative AOI Spawn instead of indexing
	# an empty entities array during the expected startup gap.
	var first := _wait(session,func(v):
		return v.entities.size()==1 and v.monsters.size()==1,7000)
	if first.is_empty():
		_fail("observer did not receive actor Spawn "+str(session.snapshot()),session)
		return
	var remote_id := int(first.entities.keys()[0])
	var monster_id := int(first.monsters.keys()[0])
	if remote_id==self_id:
		_fail("observer remote actor identity collision",session)
		return

	# Presentation event arrays are intentionally ephemeral and cleared by a
	# same-epoch resync. The per-generation sequence memory is retained, so use
	# it as durable evidence that attack visual #1 was observed.
	var attacked := _wait(session,func(v):
		if not v.player_attack_sequences.has(remote_id) or v.player_damage_visual_events.is_empty() or not v.monsters.has(monster_id):
			return false
		var attack_seq: Dictionary=v.player_attack_sequences[remote_id]
		var hit: Dictionary=v.player_damage_visual_events[-1]
		return (
			int(attack_seq.sequence)==1
			and int(v.monsters[monster_id].current_hp)==40
			and int(hit.victim_actor_id)==remote_id
			and int(hit.damage)==18
			and not bool(hit.died)
		),10000)
	if attacked.is_empty():
		_fail("observer did not receive remote attack sequence + monster delta "+str(session.snapshot()),session)
		return
	if int((attacked.player_attack_sequences[remote_id] as Dictionary).connection_epoch) != int(attacked.entities[remote_id].connection_epoch):
		_fail("observer attack visual generation mismatch",session)
		return

	var skilled := _wait(session,func(v):
		if v.player_skill_events.is_empty() or not v.monsters.has(monster_id):
			return false
		var skill: Dictionary=v.player_skill_events[-1]
		return (
			int(skill.attacker_actor_id)==remote_id
			and int(skill.skill_id)==99001
			and int(skill.target_entity_id)==monster_id
			and int(skill.status)==0
			and int(skill.damage)==40
			and int(v.monsters[monster_id].revision)>=int(skill.target_revision)
			and int(v.monsters[monster_id].current_hp)==0
		),10000)
	if skilled.is_empty():
		_fail("observer did not receive remote skill visual + death delta "+str(session.snapshot()),session)
		return
	session.stop()
	print("CLIENT_COMBAT_OBSERVER_PASS role=observer attacker=%d target=%d normal_damage=60 skill_id=99001 skill_damage=40 monster_hp=0 remote_visual=true remote_skill_visual=true remote_hit_visual=true monster_damage=18 observer_has_no_remote_hp_authority=true" % [remote_id,monster_id])
	quit(0)

func _attack(session, self_id: int) -> void:
	var ready: Dictionary = session.snapshot()
	if int(ready.player_current_mp)!=2 or int(ready.player_max_mp)!=2:
		_fail("actor MP baseline unavailable "+str(ready),session)
		return
	var remote_id := int(ready.entities.keys()[0])
	var monster_id := int(ready.monsters.keys()[0])
	if remote_id==self_id or not bool(ready.combat_available):
		_fail("actor baseline identity/combat unavailable "+str(ready),session)
		return
	if not session.attack_monster(monster_id):
		_fail("attack intent refused",session)
		return
	var settled := _wait(session,func(v):
		if not v.player_attack_sequences.has(self_id) or v.player_damage_visual_events.is_empty() or not v.monsters.has(monster_id):
			return false
		var attack_seq: Dictionary=v.player_attack_sequences[self_id]
		var hit: Dictionary=v.player_damage_visual_events[-1]
		return (
			int(v.last_attack_result_sequence)==1
			and int(v.last_attack_result_status)==0
			and int(v.last_attack_result.damage)==60
			and int(attack_seq.sequence)==1
			and int(v.monsters[monster_id].current_hp)==40
			and int(v.player_damage_sequence)==1
			and int(v.last_player_damage.damage)==18
			and int(v.player_current_hp)==82
			and int(hit.victim_actor_id)==self_id
			and int(hit.damage)==18
			and not bool(hit.died)
		),10000)
	if settled.is_empty():
		_fail("actor did not receive own attack result/visual sequence/delta "+str(session.snapshot()),session)
		return
	if not session.cast_skill(99001,monster_id):
		_fail("actor skill intent refused",session)
		return
	var skill_settled := _wait(session,func(v):
		return (
			int(v.last_skill_result_sequence)==1
			and int(v.last_skill_result_status)==0
			and int(v.last_skill_result.skill_id)==99001
			and int(v.last_skill_result.damage)==40
			and v.player_skill_events.size()>=1
			and int(v.player_skill_events[-1].attacker_actor_id)==self_id
			and int(v.player_skill_events[-1].skill_id)==99001
			and int(v.player_skill_events[-1].damage)==40
			and int(v.player_current_mp)==1
			and int(v.player_max_mp)==2
			and int(v.monsters[monster_id].current_hp)==0
		),10000)
	if skill_settled.is_empty():
		_fail("actor did not receive own skill result/visual/delta "+str(session.snapshot()),session)
		return
	# Keep the connection alive briefly so the observer drains the same reliable
	# visual and following MonsterDelta before this actor despawns.
	OS.delay_msec(350)
	session.stop()
	print("CLIENT_COMBAT_OBSERVER_PASS role=actor actor=%d target=%d normal_damage=60 skill_id=99001 skill_damage=40 skill_mp=2_to_1 monster_hp=0 own_visual=true own_skill_visual=true own_hit_visual=true monster_damage=18 player_hp=82" % [self_id,monster_id])
	quit(0)

func _wait(session, predicate: Callable, timeout_ms: int) -> Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value: Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		if int(value.state)==WorldState.STATE_FAILED and not bool(value.get("reconnecting",false)):
			return {}
		OS.delay_msec(2)
	return {}
