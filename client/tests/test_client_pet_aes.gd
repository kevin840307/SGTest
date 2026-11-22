extends SceneTree

const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

var _last_error := ""

func _fail(message:String, session=null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_PET_AES_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args:=OS.get_cmdline_user_args()
	if args.size()!=1 or not str(args[0]).is_valid_int():
		_fail("expected port")
		return
	var port:=int(args[0])

	var first=_connect_world(port)
	if first==null:
		_fail("first login "+_last_error)
		return
	var baseline:Dictionary=first.snapshot()
	var pet_a:=_find_pet(baseline,5001,2,4,88)
	var pet_b:=_find_pet(baseline,5002,7,7,1234)
	if pet_a<=0 or pet_b<=0 or pet_a==pet_b or int(baseline.active_pet_uid)!=0:
		_fail("unexpected pet roster "+str(baseline.get("pet_instances",{})),first)
		return
	if not baseline.get("pet_entities",{}).is_empty():
		_fail("inactive pet unexpectedly present in AOI",first)
		return

	if not first.pet_summon(pet_a):
		_fail("summon intent refused",first)
		return
	var summoned:=_wait(first,func(v):
		var actor:=int(v.get("actor_id",0))
		var entities:Dictionary=v.get("pet_entities",{})
		if (
			int(v.get("pending_pet_sequence",0))!=0 or
			int(v.get("last_pet_result_sequence",0))!=1 or
			int(v.get("last_pet_result_status",-1))!=0 or
			int(v.get("active_pet_uid",0))!=pet_a or
			not entities.has(actor)
		):
			return false
		var entity:Dictionary=entities[actor]
		return (
			int(entity.get("pet_uid",0))==pet_a and
			int(entity.get("definition_id",0))==5001 and
			int(entity.get("evolution_id",-1))==2 and
			int(entity.get("level",0))==4
		),7000)
	if summoned.is_empty():
		_fail("summon settlement "+str(first.snapshot()),first)
		return
	first.stop()
	OS.delay_msec(30)

	var second=_connect_world(port)
	if second==null:
		_fail("relogin "+_last_error)
		return
	var restored:=_wait(second,func(v):
		var actor:=int(v.get("actor_id",0))
		var entities:Dictionary=v.get("pet_entities",{})
		if int(v.get("active_pet_uid",0))!=pet_a or not entities.has(actor):
			return false
		var entity:Dictionary=entities[actor]
		return (
			int(entity.get("pet_uid",0))==pet_a and
			int(entity.get("definition_id",0))==5001 and
			int(entity.get("evolution_id",-1))==2 and
			int(entity.get("level",0))==4
		),7000)
	if restored.is_empty():
		_fail("active pet not restored "+str(second.snapshot()),second)
		return
	if _find_pet(restored,5001,2,4,88)!=pet_a or _find_pet(restored,5002,7,7,1234)!=pet_b:
		_fail("relogin roster identity changed "+str(restored.get("pet_instances",{})),second)
		return

	if not second.pet_recall():
		_fail("recall intent refused",second)
		return
	var recalled:=_wait(second,func(v):
		var actor:=int(v.get("actor_id",0))
		return (
			int(v.get("pending_pet_sequence",0))==0 and
			int(v.get("last_pet_result_sequence",0))==1 and
			int(v.get("last_pet_result_status",-1))==0 and
			int(v.get("active_pet_uid",0))==0 and
			not v.get("pet_entities",{}).has(actor)
		),7000)
	if recalled.is_empty():
		_fail("recall settlement "+str(second.snapshot()),second)
		return

	second.stop()
	print("CLIENT_PET_AES_PASS pet_a=%d pet_b=%d summon=true reconnect_active=true recall=true definition_evolution_identity=true aoi_spawn_despawn=true" % [pet_a,pet_b])
	quit(0)

func _connect_world(port:int):
	_last_error=""
	var session=Session.new()
	if session.connect_secure("127.0.0.1",port,USERNAME,PASSWORD)!=OK:
		_last_error="connect"
		session.stop()
		return null
	var selecting:=_wait(session,func(v):
		return int(v.get("state",-1))==WorldState.STATE_SELECTING_CHARACTER,5000)
	if selecting.is_empty() or selecting.get("characters",[]).size()!=1:
		_last_error="character list "+str(session.snapshot())
		session.stop()
		return null
	if not FixtureLogin.provision_and_choose(
		session,int(selecting.characters[0].character_id)):
		_last_error="choose"
		session.stop()
		return null
	var ready:=_wait(session,func(v):
		return (
			int(v.get("state",-1))==WorldState.STATE_IN_WORLD and
			bool(v.get("world_ready",false)) and
			bool(v.get("pet_snapshot_received",false)) and
			int(v.get("pending_world_ready_ack_epoch",0))==0 and
			int(v.get("last_world_ready_ack_epoch",0))==int(v.get("map_epoch",0)) and
			v.get("pet_instances",{}).size()==2
		),7000)
	if ready.is_empty():
		_last_error="world/pet baseline "+str(session.snapshot())
		session.stop()
		return null
	return session

func _find_pet(snapshot:Dictionary, definition_id:int, evolution_id:int, level:int, experience:int)->int:
	var pets:Dictionary=snapshot.get("pet_instances",{})
	for raw_uid in pets:
		var pet:Dictionary=pets[raw_uid]
		if (
			int(pet.get("definition_id",0))==definition_id and
			int(pet.get("evolution_id",-1))==evolution_id and
			int(pet.get("level",0))==level and
			int(pet.get("experience",-1))==experience
		):
			return int(raw_uid)
	return 0

func _wait(session, predicate:Callable, timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		if int(value.get("state",-1))==WorldState.STATE_FAILED and not bool(value.get("reconnecting",false)):
			return {}
		OS.delay_msec(2)
	return {}
