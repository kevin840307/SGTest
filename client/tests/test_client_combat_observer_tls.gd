extends SceneTree

const Session = preload("res://network/client_session.gd")
const WorldState = preload("res://network/world_state.gd")
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_COMBAT_OBSERVER_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()!=3 or not str(args[0]).is_valid_int() or str(args[2]) not in ["observer","actor"]:
		_fail("expected TLS port, certificate path, observer|actor")
		return
	var role := str(args[2])
	var cert := X509Certificate.new()
	if cert.load(str(args[1])) != OK:
		_fail("certificate load")
		return
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
			and not v.self_state.is_empty()
			and v.entities.size()==1
			and v.monsters.size()==1
		),7000)
	if ready.is_empty():
		_fail("shared AOI baseline "+str(session.snapshot()),session)
		return
	if role=="observer":
		print("CLIENT_COMBAT_OBSERVER_READY role=observer actor=%d" % self_id)
		_observe(session,self_id)
	else:
		_attack(session,self_id)

func _observe(session, self_id: int) -> void:
	var first: Dictionary = session.snapshot()
	var remote_id := int(first.entities.keys()[0])
	var monster_id := int(first.monsters.keys()[0])
	if remote_id==self_id:
		_fail("observer remote actor identity collision",session)
		return
	var settled := _wait(session,func(v):
		if v.player_attack_events.is_empty() or not v.monsters.has(monster_id):
			return false
		var event: Dictionary=v.player_attack_events[-1]
		return (
			int(event.attacker_actor_id)==remote_id
			and int(event.target_entity_id)==monster_id
			and int(event.status)==0
			and int(event.damage)==60
			and int(event.target_revision)==2
			and int(v.monsters[monster_id].revision)>=2
			and int(v.monsters[monster_id].current_hp)==40
		),10000)
	if settled.is_empty():
		_fail("observer did not receive remote attack visual + monster delta "+str(session.snapshot()),session)
		return
	var event: Dictionary=settled.player_attack_events[-1]
	if int(event.attacker_connection_epoch) != int(settled.entities[remote_id].connection_epoch):
		_fail("observer attack visual generation mismatch",session)
		return
	session.stop()
	print("CLIENT_COMBAT_OBSERVER_PASS role=observer attacker=%d target=%d damage=60 monster_hp=40 remote_visual=true" % [remote_id,monster_id])
	quit(0)

func _attack(session, self_id: int) -> void:
	var ready: Dictionary = session.snapshot()
	var remote_id := int(ready.entities.keys()[0])
	var monster_id := int(ready.monsters.keys()[0])
	if remote_id==self_id or not bool(ready.combat_available):
		_fail("actor baseline identity/combat unavailable "+str(ready),session)
		return
	if not session.attack_monster(monster_id):
		_fail("attack intent refused",session)
		return
	var settled := _wait(session,func(v):
		if v.player_attack_events.is_empty() or not v.monsters.has(monster_id):
			return false
		var event: Dictionary=v.player_attack_events[-1]
		return (
			int(v.last_attack_result_sequence)==1
			and int(v.last_attack_result_status)==0
			and int(v.last_attack_result.damage)==60
			and int(event.attacker_actor_id)==self_id
			and int(event.target_entity_id)==monster_id
			and int(event.status)==0
			and int(event.damage)==60
			and int(v.monsters[monster_id].revision)>=2
			and int(v.monsters[monster_id].current_hp)==40
		),10000)
	if settled.is_empty():
		_fail("actor did not receive own result/visual/delta "+str(session.snapshot()),session)
		return
	# Keep the connection alive briefly so the observer drains the same reliable
	# visual and following MonsterDelta before this actor despawns.
	OS.delay_msec(350)
	session.stop()
	print("CLIENT_COMBAT_OBSERVER_PASS role=actor actor=%d target=%d damage=60 monster_hp=40 own_visual=true" % [self_id,monster_id])
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
