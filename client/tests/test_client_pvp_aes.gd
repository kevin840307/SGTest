extends SceneTree

const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_PVP_AES_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args:=OS.get_cmdline_user_args()
	if args.size()!=4 or not str(args[0]).is_valid_int() or str(args[1]) not in ["victim","actor"]:
		_fail("expected port role actor_a actor_b")
		return
	var role:=str(args[1])
	var actor_a:=int(args[2])
	var actor_b:=int(args[3])
	var self_id:=actor_a if role=="actor" else actor_b
	var peer_id:=actor_b if role=="actor" else actor_a
	var username:="pvp_one" if role=="actor" else "pvp_two"

	var session=Session.new()
	if session.connect_secure(
		"127.0.0.1",int(args[0]),username,PASSWORD)!=OK:
		_fail("connect",session)
		return
	var selecting:=_wait(session,func(v):
		return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,7000)
	if selecting.is_empty() or selecting.characters.size()!=1 or int(selecting.characters[0].character_id)!=self_id:
		_fail("character list "+str(session.snapshot()),session)
		return
	if not FixtureLogin.provision_and_choose(session, self_id):
		_fail("choose",session)
		return
	var ready:=_wait(session,func(v):
		return (int(v.state)==WorldState.STATE_IN_WORLD and bool(v.world_ready) and
			bool(v.combat_available) and bool(v.pvp_known) and
			bool(v.pvp_map_allows) and int(v.pvp_mode)==1),7000)
	if ready.is_empty():
		_fail("world/PvP ready "+str(session.snapshot()),session)
		return
	if role=="actor" and not bool(ready.get("gm_authorized",false)):
		_fail("allowlisted actor did not receive server GM capability "+str(ready),session)
		return
	if role=="victim" and bool(ready.get("gm_authorized",false)):
		_fail("non-allowlisted victim received GM capability",session)
		return

	if role=="victim":
		print("CLIENT_PVP_READY role=victim actor=%d" % self_id)
		_run_victim(session,self_id,peer_id)
	else:
		_run_actor(session,self_id,peer_id)

func _run_victim(session,self_id:int,peer_id:int)->void:
	var visible:=_wait(session,func(v):
		return (v.entities as Dictionary).has(peer_id),7000)
	if visible.is_empty():
		_fail("victim never saw attacker "+str(session.snapshot()),session)
		return
	var named:=_wait(session,func(v):
		var identities:Dictionary=v.get("player_identities",{})
		return identities.has(peer_id) and str((identities[peer_id] as Dictionary).get("display_name",""))=="PvpTlsAttacker",7000)
	if named.is_empty():
		_fail("victim never received attacker identity "+str(session.snapshot()),session)
		return
	var first:=_wait(session,func(v):
		return int(v.player_damage_sequence)>=1,7000)
	if (first.is_empty() or int(first.player_damage_sequence)!=1 or
		int(first.player_current_hp)!=42 or bool(first.player_dead) or
		int(first.last_player_damage.get("attacker_actor_id",0))!=peer_id or
		int(first.last_player_damage.get("damage",0))!=58 or
		str(first.last_player_damage.get("source_kind",""))!="pvp"):
		_fail("first PvP damage "+str(first),session)
		return
	var pink:=_wait(session,func(v):
		var identities:Dictionary=v.get("player_identities",{})
		if not identities.has(peer_id):
			return false
		var identity:Dictionary=identities[peer_id]
		return not bool(identity.get("red_man",false)) and int(identity.get("pink_until_unix_ms",0))>int(identity.get("server_unix_ms",0)) and int(identity.get("pvp_updated_unix_ms",0))>0,7000)
	if pink.is_empty():
		_fail("attacker did not become pink on observer "+str(session.snapshot()),session)
		return
	var first_visual:=_wait(session,func(v):
		for event in v.get("pvp_combat_visual_events",[]):
			if (int(event.get("attacker_actor_id",0))==peer_id and
				int(event.get("victim_actor_id",0))==self_id and
				int(event.get("attack_sequence",0))==1 and
				int(event.get("damage_sequence",0))==1 and
				int(event.get("damage",0))==58):
				return true
		return false,7000)
	if first_visual.is_empty():
		_fail("victim missing first PvP combat visual "+str(session.snapshot()),session)
		return
	print("CLIENT_PVP_FIRST_HIT role=victim hp=42 damage=58 name=Pink visual=true")
	var dead:=_wait(session,func(v):
		return int(v.player_damage_sequence)>=2 and bool(v.player_dead),7000)
	if (dead.is_empty() or int(dead.player_damage_sequence)!=2 or
		int(dead.player_current_hp)!=0 or
		int(dead.last_player_damage.get("damage",0))!=42 or
		int(dead.last_player_damage.get("attacker_actor_id",0))!=peer_id):
		_fail("lethal PvP skill damage "+str(dead),session)
		return
	var skill_visual:=_wait(session,func(v):
		for event in v.get("pvp_skill_visual_events",[]):
			if (int(event.get("attacker_actor_id",0))==peer_id and
				int(event.get("victim_actor_id",0))==self_id and
				int(event.get("skill_sequence",0))==1 and
				int(event.get("skill_id",0))==99003 and
				int(event.get("damage_sequence",0))==2 and
				int(event.get("damage",0))==42 and
				bool(event.get("died",false))):
				return true
		return false,7000)
	if skill_visual.is_empty():
		_fail("victim missing lethal PvP skill visual "+str(session.snapshot()),session)
		return
	var red:=_wait(session,func(v):
		var identities:Dictionary=v.get("player_identities",{})
		return identities.has(peer_id) and bool((identities[peer_id] as Dictionary).get("red_man",false)),7000)
	if red.is_empty():
		_fail("attacker did not become red on observer "+str(session.snapshot()),session)
		return
	var gm_blue:=_wait(session,func(v):
		var identities:Dictionary=v.get("player_identities",{})
		if not identities.has(self_id):
			return false
		var identity:Dictionary=identities[self_id]
		return (
			int(identity.get("mode",-1))==0 and
			not bool(identity.get("red_man",true)) and
			int(identity.get("pvp_updated_unix_ms",0))>0
		),7000)
	if gm_blue.is_empty():
		_fail("victim did not receive audited GM blue identity "+str(session.snapshot()),session)
		return
	session.stop()
	print("CLIENT_PVP_PASS role=victim actor=%d hp=0 sequence=2 death=true name=Pink->Red gm_target_blue=true attack_visual=true skill_visual=true" % self_id)
	quit(0)

func _run_actor(session,self_id:int,peer_id:int)->void:
	var visible:=_wait(session,func(v):
		return (v.entities as Dictionary).has(peer_id),7000)
	if visible.is_empty():
		_fail("actor never saw victim "+str(session.snapshot()),session)
		return
	var named:=_wait(session,func(v):
		var identities:Dictionary=v.get("player_identities",{})
		return identities.has(peer_id) and str((identities[peer_id] as Dictionary).get("display_name",""))=="PvpTlsVictim",7000)
	if named.is_empty():
		_fail("actor never received victim identity "+str(session.snapshot()),session)
		return
	if not session.attack_player(peer_id):
		_fail("first attack submit",session)
		return
	var first:=_wait(session,func(v):
		return int(v.last_pvp_attack_result_sequence)>=1,7000)
	if (first.is_empty() or int(first.last_pvp_attack_result_status)!=0 or
		int(first.last_pvp_attack_result.get("target_actor_id",0))!=peer_id or
		int(first.last_pvp_attack_result.get("damage",0))!=58 or
		bool(first.last_pvp_attack_result.get("died",true))):
		_fail("first attack result "+str(first),session)
		return
	var first_visual:=_wait(session,func(v):
		for event in v.get("pvp_combat_visual_events",[]):
			if (int(event.get("attacker_actor_id",0))==self_id and
				int(event.get("victim_actor_id",0))==peer_id and
				int(event.get("attack_sequence",0))==1 and
				int(event.get("damage",0))==58):
				return true
		return false,7000)
	if first_visual.is_empty():
		_fail("actor missing first PvP combat visual "+str(session.snapshot()),session)
		return
	OS.delay_msec(120)
	if not session.cast_player_skill(99003,peer_id):
		_fail("player skill submit",session)
		return
	var second:=_wait(session,func(v):
		return int(v.last_skill_result_sequence)>=1,7000)
	if (second.is_empty() or int(second.last_skill_result_status)!=0 or
		int(second.last_skill_result.get("target_actor_id",0))!=peer_id or
		int(second.last_skill_result.get("skill_id",0))!=99003 or
		str(second.last_skill_result.get("target_kind",""))!="player" or
		int(second.last_skill_result.get("damage",0))!=42 or
		not bool(second.last_skill_result.get("died",false)) or
		int(second.player_current_hp)!=100 or int(second.player_current_mp)!=1):
		_fail("player skill result "+str(second),session)
		return
	var skill_visual:=_wait(session,func(v):
		for event in v.get("pvp_skill_visual_events",[]):
			if (int(event.get("attacker_actor_id",0))==self_id and
				int(event.get("victim_actor_id",0))==peer_id and
				int(event.get("skill_sequence",0))==1 and
				int(event.get("skill_id",0))==99003 and
				int(event.get("damage",0))==42 and
				bool(event.get("died",false))):
				return true
		return false,7000)
	if skill_visual.is_empty():
		_fail("actor missing PvP skill visual "+str(session.snapshot()),session)
		return
	if not session.gm_set_pvp_name(peer_id,0):
		_fail("audited GM blue command submit "+str(session.snapshot()),session)
		return
	var gm_result:=_wait(session,func(v):
		return int(v.get("last_gm_result_sequence",0))>=1,7000)
	if gm_result.is_empty() or int(gm_result.get("last_gm_result_status",-1))!=0:
		_fail("audited GM result "+str(gm_result),session)
		return
	var peer_blue:=_wait(session,func(v):
		var identities:Dictionary=v.get("player_identities",{})
		if not identities.has(peer_id):
			return false
		var identity:Dictionary=identities[peer_id]
		return (
			int(identity.get("mode",-1))==0 and
			not bool(identity.get("red_man",true)) and
			int(identity.get("pvp_updated_unix_ms",0))>0
		),7000)
	if peer_blue.is_empty():
		_fail("actor did not receive GM target blue identity "+str(session.snapshot()),session)
		return
	session.stop()
	print("CLIENT_PVP_PASS role=actor actor=%d attack_damage=58 skill_damage=42 target_dead=true mp=2->1 gm_capability=true gm_target_blue=true attack_visual=true skill_visual=true" % self_id)
	quit(0)

func _wait(session,predicate:Callable,timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		if int(value.state)==WorldState.STATE_FAILED and not bool(value.get("reconnecting",false)):
			return {}
		OS.delay_msec(2)
	return {}
