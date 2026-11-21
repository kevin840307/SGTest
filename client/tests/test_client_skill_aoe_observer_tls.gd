extends SceneTree

const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_SKILL_AOE_OBSERVER_FAIL "+message)
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
			and v.monsters.size()==3
		),7000)
	if ready.is_empty():
		_fail("three-monster baseline "+str(session.snapshot()),session)
		return
	if role=="observer":
		print("CLIENT_SKILL_AOE_OBSERVER_READY role=observer actor=%d" % self_id)
		_observe(session,self_id)
	else:
		var shared := _wait(session,func(v):
			return (
				v.entities.size()==1
				and v.monsters.size()==3
				and bool(v.combat_available)
				and int(v.player_current_mp)==2
				and int(v.player_max_mp)==2
			),7000)
		if shared.is_empty():
			_fail("actor did not receive observer/combat baseline "+str(session.snapshot()),session)
			return
		_cast(session,self_id)

func _partition(v: Dictionary) -> Dictionary:
	var near: Array[int]=[]
	var far_id:=0
	for raw_id in v.monsters.keys():
		var entity_id:=int(raw_id)
		var monster: Dictionary=v.monsters[raw_id]
		var dx:=float(monster.x)-float(v.x)
		var dy:=float(monster.y)-float(v.y)
		if sqrt(dx*dx+dy*dy)<=18.0:
			near.append(entity_id)
		else:
			far_id=entity_id
	near.sort()
	return {"near":near,"far":far_id}

func _observe(session, self_id: int) -> void:
	var shared := _wait(session,func(v):
		return (v.entities.size()==1 and v.monsters.size()==3)
	,7000)
	if shared.is_empty():
		_fail("observer did not receive actor Spawn "+str(session.snapshot()),session)
		return
	var remote_id:=int(shared.entities.keys()[0])
	if remote_id==self_id:
		_fail("observer remote actor identity collision",session)
		return
	var actor_state: Dictionary=shared.entities[remote_id]
	var pseudo:=shared.duplicate(true)
	pseudo["x"]=float(actor_state.x)
	pseudo["y"]=float(actor_state.y)
	var split:=_partition(pseudo)
	var near: Array=split.near
	var far_id:=int(split.far)
	if near.size()!=2 or far_id<=0:
		_fail("observer radius partition "+str(shared.monsters),session)
		return

	var settled:=_wait(session,func(v):
		if not v.player_skill_sequences.has(remote_id):
			return false
		var memory: Dictionary=v.player_skill_sequences[remote_id]
		if int(memory.sequence)!=1 or int(memory.connection_epoch)!=int(v.entities[remote_id].connection_epoch):
			return false
		var targets: Dictionary=memory.get("targets",{})
		if targets.size()!=2 or not targets.has(near[0]) or not targets.has(near[1]) or targets.has(far_id):
			return false
		for id in near:
			if not v.monsters.has(id) or int(v.monsters[id].current_hp)!=60:
				return false
		return v.monsters.has(far_id) and int(v.monsters[far_id].current_hp)==100
	,10000)
	if settled.is_empty():
		_fail("observer did not receive both AoE target visuals/deltas "+str(session.snapshot()),session)
		return
	if bool(settled.combat_available):
		# Observer has no combat profile and must not gain the actor's MP state.
		_fail("observer incorrectly owns actor combat resources",session)
		return
	session.stop()
	print("CLIENT_SKILL_AOE_OBSERVER_PASS role=observer attacker=%d hits=2 near_hp=60,60 far_hp=100 target_memory=2 remote_mp_authority=false" % remote_id)
	quit(0)

func _cast(session, self_id: int) -> void:
	var ready: Dictionary=session.snapshot()
	var split:=_partition(ready)
	var near: Array=split.near
	var far_id:=int(split.far)
	if near.size()!=2 or far_id<=0:
		_fail("actor radius partition "+str(ready.monsters),session)
		return
	if not session.cast_area_skill(99001):
		_fail("area skill intent refused",session)
		return
	var settled:=_wait(session,func(v):
		if int(v.last_skill_result_sequence)!=1 or int(v.last_skill_result_status)!=0:
			return false
		if int(v.last_skill_result.target_entity_id)!=0 or int(v.last_skill_result.damage)!=0:
			return false
		if int(v.player_current_mp)!=1 or int(v.player_max_mp)!=2:
			return false
		if not v.player_skill_sequences.has(self_id):
			return false
		var memory: Dictionary=v.player_skill_sequences[self_id]
		var targets: Dictionary=memory.get("targets",{})
		if int(memory.sequence)!=1 or targets.size()!=2 or not targets.has(near[0]) or not targets.has(near[1]):
			return false
		for id in near:
			if not v.monsters.has(id) or int(v.monsters[id].current_hp)!=60:
				return false
		return v.monsters.has(far_id) and int(v.monsters[far_id].current_hp)==100
	,10000)
	if settled.is_empty():
		_fail("actor AoE settlement "+str(session.snapshot()),session)
		return
	OS.delay_msec(350)
	session.stop()
	print("CLIENT_SKILL_AOE_OBSERVER_PASS role=actor actor=%d hits=2 near_hp=60,60 far_hp=100 mp=2_to_1 target_memory=2" % self_id)
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
