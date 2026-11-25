extends SceneTree

# Real concurrently connected Godot ClientSession workers. The C# fixture
# runs one shared 30Hz authoritative World with AES/WorldAoiPump, not fake wire.
const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_MULTI_FAIL " + message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() not in [2, 3] or not str(args[0]).is_valid_int() or str(args[1]) not in ["observer", "actor", "rejoin"] or (args.size() == 3 and str(args[2]) not in ["crossmap", "portal", "reconnect"]):
		_fail("expected AES port, observer|actor|rejoin, optional crossmap|portal|reconnect")
		return
	var role := str(args[1])
	var mode := str(args[2]) if args.size() == 3 else ""
	var crossmap := mode in ["crossmap", "portal"]
	var portal := mode == "portal"
	var reconnect := mode == "reconnect"
	var prefix := "MULTI_RECONNECT" if reconnect else "PORTAL" if portal else "CROSSMAP" if crossmap else "MULTI"
	var username := "fixture_one" if role == "observer" else "fixture_two"
	var session = Session.new()
	var started := session.connect_secure("127.0.0.1", int(args[0]), username,
		PASSWORD)
	if started != OK:
		_fail("AES connect rejected %d" % started, session)
		return

	var selecting := _wait_character_list(session, 7000)
	if selecting.is_empty() or selecting.characters.size() != 1:
		_fail("%s character list %s" % [role, session.snapshot()], session)
		return
	var self_id := int(selecting.characters[0].character_id)
	if not FixtureLogin.provision_and_choose(session, self_id):
		_fail("%s character choose rejected" % role, session)
		return
	var ready := _wait_ready(session, 7000)
	if ready.is_empty() or int(ready.map_id) != 15 or not bool(ready.world_ready) or int(ready.actor_id) != self_id:
		_fail("%s WorldReady missing %s" % [role, session.snapshot()], session)
		return
	if role == "observer":
		print("CLIENT_%s_READY role=observer actor=%d epoch=%d" % [prefix, self_id, int(ready.map_epoch)])
		_observe(session, self_id, crossmap, portal, reconnect)
	elif role == "rejoin":
		_rejoin(session, self_id)
	else:
		_move(session, self_id, crossmap, portal)

func _observe(session, self_id: int, crossmap: bool = false, portal: bool = false, reconnect: bool = false) -> void:
	var first := _wait_remote(session, 7000)
	if first.is_empty():
		_fail("observer never received another player's Spawn %s" % session.snapshot(), session)
		return
	var remote_id := int(first.entities.keys()[0])
	if remote_id == self_id:
		_fail("observer received itself as remote entity", session)
		return
	if not crossmap:
		var chat := _wait_nearby_chat(
			session,remote_id,"MultiHeroTwo","附近 E2E 測試",7000)
		if chat.is_empty() or int(chat.get("chat_sequence",0))!=1 or \
				int(chat.get("channel",-1))!=0 or int(chat.get("bubble_type",-1))!=2:
			_fail("observer did not receive authenticated nearby chat %s" %
				session.snapshot(),session)
			return
		var world_chat := _wait_world_chat(
			session,remote_id,"MultiHeroTwo","世界 E2E 測試",7000)
		if world_chat.is_empty() or int(world_chat.get("chat_sequence",0))!=2 or \
				int(world_chat.get("channel",-1))!=1 or int(world_chat.get("bubble_type",-1))!=4:
			_fail("observer did not receive authoritative World chat %s" %
				session.snapshot(),session)
			return
	var crossmap_chat_stale_before := -1
	if crossmap and not portal:
		if not session.party_invite_name("MultiHeroTwo"):
			_fail("observer party name invite intent rejected",session)
			return
		var invite_result:=_wait_party_result(session,1,1,remote_id,7000)
		if invite_result.is_empty() or int(invite_result.get("status",-1))!=0:
			_fail("observer party invite result missing %s" % session.snapshot(),session)
			return
		var party_id:=int(invite_result.get("party_id",0))
		var joined:=_wait_party_members(session,party_id,2,7000)
		if joined.is_empty() or int(joined.get("party_leader_actor_id",0))!=self_id:
			_fail("observer did not receive two-member party snapshot %s" %
				session.snapshot(),session)
			return
		print("CLIENT_CROSSMAP_PARTY_READY role=observer party=%d leader=%d" %
			[party_id,self_id])
		var map15_chat := _wait_nearby_chat(
			session,remote_id,"MultiHeroTwo","跨圖前附近訊息",7000)
		if map15_chat.is_empty() or int(map15_chat.get("chat_sequence",0))!=1:
			_fail("observer did not receive pre-transfer nearby chat %s" %
				session.snapshot(),session)
			return
		crossmap_chat_stale_before = int(
			session.snapshot().get("stale_chat_deliveries",0))
	var initial_revision := int(first.entities[remote_id].revision)
	var initial_x := float(first.entities[remote_id].x)
	var moved := {}
	var deadline := Time.get_ticks_msec() + 7000
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_FAILED:
			break
		if value.entities.has(remote_id) and int(value.entities[remote_id].revision) > initial_revision and float(value.entities[remote_id].x) > initial_x:
			moved = value
			break
		OS.delay_msec(2)
	if moved.is_empty():
		_fail("observer did not receive authoritative Delta %s" % session.snapshot(), session)
		return
	if crossmap:
		print("CLIENT_%s_DELTA_SEEN role=observer remote=%d" % ["PORTAL" if portal else "CROSSMAP", remote_id])
	var gone := false
	deadline = Time.get_ticks_msec() + 7000
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_FAILED:
			break
		if value.entities.is_empty():
			gone = true
			break
		OS.delay_msec(2)
	if not gone:
		_fail("observer still sees disconnected player / missing Despawn %s" % session.snapshot(), session)
		return
	if reconnect:
		var prior_connection := int(first.entities[remote_id].connection_epoch)
		var respawn := {}
		deadline = Time.get_ticks_msec() + 7000
		while Time.get_ticks_msec() < deadline:
			var value: Dictionary = session.snapshot()
			if int(value.state) == WorldState.STATE_FAILED:
				break
			if value.entities.has(remote_id) and int(value.entities[remote_id].connection_epoch) > prior_connection:
				respawn = value
				break
			OS.delay_msec(2)
		if respawn.is_empty():
			_fail("same actor did not respawn with a new connection generation %s" % session.snapshot(), session)
			return
		var new_connection := int(respawn.entities[remote_id].connection_epoch)
		var moved_again := {}
		deadline = Time.get_ticks_msec() + 7000
		while Time.get_ticks_msec() < deadline:
			var value: Dictionary = session.snapshot()
			if int(value.state) == WorldState.STATE_FAILED:
				break
			if value.entities.has(remote_id) and int(value.entities[remote_id].connection_epoch) == new_connection and float(value.entities[remote_id].x) <= 24.0:
				moved_again = value
				break
			OS.delay_msec(2)
		if moved_again.is_empty():
			_fail("rejoined actor did not publish authoritative movement on new generation %s" % session.snapshot(), session)
			return
		session.stop()
		print("CLIENT_MULTI_RECONNECT_PASS role=observer despawn=PASS same_actor_respawn=PASS new_connection_epoch=PASS post_rejoin_delta=PASS remote=%d old_connection=%d new_connection=%d" % [remote_id, prior_connection, new_connection])
		quit(0)
		return
	if crossmap:
		print("CLIENT_%s_DESPAWN_SEEN role=observer remote=%d" % ["PORTAL" if portal else "CROSSMAP", remote_id])
		if not portal:
			var party_chat:=_wait_party_chat(
				session,remote_id,"MultiHeroTwo","Map13 跨圖隊伍聊天",7000)
			if (
				party_chat.is_empty() or int(party_chat.get("chat_sequence",0))!=3 or
				int(party_chat.get("channel",-1))!=3
			):
				_fail("Map13 cross-map party chat was not delivered to Map15 observer %s" %
					session.snapshot(),session)
				return
			print("CLIENT_CROSSMAP_PARTY_CHAT_RECEIVED role=observer sequence=3 remote=%d" % remote_id)
			var whisper:=_wait_whisper(
				session,remote_id,"MultiHeroTwo","Map13 跨圖私訊",7000)
			if (
				whisper.is_empty() or int(whisper.get("chat_sequence",0))!=4 or
				int(whisper.get("channel",-1))!=4 or
				str(whisper.get("direction",""))!="incoming"
			):
				_fail("Map13 cross-map whisper was not delivered to Map15 observer %s" %
					session.snapshot(),session)
				return
			print("CLIENT_CROSSMAP_WHISPER_RECEIVED role=observer sequence=4 remote=%d" % remote_id)
		var returned := {}
		deadline = Time.get_ticks_msec() + 7000
		while Time.get_ticks_msec() < deadline:
			var value: Dictionary = session.snapshot()
			if int(value.state) == WorldState.STATE_FAILED:
				break
			if value.entities.has(remote_id) and int(value.entities[remote_id].entity_map_epoch) > int(first.entities[remote_id].entity_map_epoch):
				returned = value
				break
			OS.delay_msec(2)
		if returned.is_empty() or int(returned.map_id) != 15 or int(returned.map_epoch) != int(first.map_epoch):
			_fail("returning Map15 player did not respawn with a new generation %s" % session.snapshot(), session)
			return
		if not portal:
			var after_isolation:Dictionary=session.snapshot()
			if (
				_has_nearby_chat(after_isolation,"Map13 隔離訊息") or
				int(after_isolation.get("stale_chat_deliveries",0))!=
					crossmap_chat_stale_before
			):
				_fail("Map13 nearby chat leaked into Map15 route %s" %
					after_isolation,session)
				return
		# Keep the observer alive briefly so the returning actor can
		# also reduce its authoritative peer Spawn, not only WorldReady.
		OS.delay_msec(650)
		if not portal and int(session.snapshot().get("party_id",0))<=0:
			_fail("observer party membership did not survive actor map transfer",session)
			return
		session.stop()
		print("CLIENT_%s_PASS role=observer map15=PASS delta=PASS despawn=PASS respawn=PASS nearby_chat_isolation=%s party_crossmap=%s whisper_crossmap=%s remote=%d" % ["PORTAL" if portal else "CROSSMAP","NOT_TESTED" if portal else "PASS","NOT_TESTED" if portal else "PASS","NOT_TESTED" if portal else "PASS",remote_id])
		quit(0)
		return
	session.stop()
	print("CLIENT_MULTI_PASS role=observer world_ready=PASS spawn=PASS nearby_chat=PASS world_chat=PASS delta=PASS despawn=PASS other_actor=%d" % remote_id)
	quit(0)

func _move(session, self_id: int, crossmap: bool = false, portal: bool = false) -> void:
	var seen := _wait_remote(session, 7000)
	if seen.is_empty():
		_fail("actor never received observer's Spawn %s" % session.snapshot(), session)
		return
	var remote_id:=int(seen.entities.keys()[0])
	if remote_id == self_id:
		_fail("actor received itself as remote entity", session)
		return
	if not crossmap:
		if not (seen.get("chat_messages",[]) as Array).is_empty():
			_fail("actor had unexpected chat before sending",session)
			return
		if not session.send_nearby_chat("附近 E2E 測試",2):
			_fail("actor nearby chat intent rejected",session)
			return
		var echoed := _wait_nearby_chat(
			session,self_id,"MultiHeroTwo","附近 E2E 測試",7000)
		if echoed.is_empty() or int(echoed.get("chat_sequence",0))!=1 or \
				int(echoed.get("channel",-1))!=0 or int(echoed.get("bubble_type",-1))!=2:
			_fail("actor did not receive authoritative nearby chat echo %s" %
				session.snapshot(),session)
			return
		if int(session.snapshot().get("player_level",0))!=10 or \
				not bool(session.snapshot().get("player_profile_ready",false)):
			_fail("world-chat fixture did not receive authoritative level-10 profile",session)
			return
		if not session.send_world_chat("世界 E2E 測試",4):
			_fail("actor World chat intent rejected",session)
			return
		var world_echo := _wait_world_chat(
			session,self_id,"MultiHeroTwo","世界 E2E 測試",7000)
		if world_echo.is_empty() or int(world_echo.get("chat_sequence",0))!=2 or \
				int(world_echo.get("channel",-1))!=1 or int(world_echo.get("bubble_type",-1))!=4:
			_fail("actor did not receive authoritative World chat echo %s" %
				session.snapshot(),session)
			return
	elif not portal:
		var invite:=_wait_party_invite(session,7000)
		if invite.is_empty() or int(invite.get("inviter_actor_id",0))!=remote_id:
			_fail("actor did not receive crossmap party invite %s" % session.snapshot(),session)
			return
		var party_id:=int(invite.get("party_id",0))
		if not session.party_accept():
			_fail("actor party accept intent rejected",session)
			return
		var accept_result:=_wait_party_result(session,1,2,remote_id,7000)
		if (
			accept_result.is_empty() or int(accept_result.get("status",-1))!=0 or
			int(accept_result.get("party_id",0))!=party_id
		):
			_fail("actor party accept result missing %s" % session.snapshot(),session)
			return
		var joined:=_wait_party_members(session,party_id,2,7000)
		if joined.is_empty():
			_fail("actor did not receive two-member party snapshot %s" %
				session.snapshot(),session)
			return
		print("CLIENT_CROSSMAP_PARTY_READY role=actor party=%d leader=%d" %
			[party_id,int(joined.get("party_leader_actor_id",0))])
		if not session.send_nearby_chat("跨圖前附近訊息",1):
			_fail("crossmap Map15 nearby chat intent rejected",session)
			return
		var pre_transfer_echo:=_wait_nearby_chat(
			session,self_id,"MultiHeroTwo","跨圖前附近訊息",7000)
		if pre_transfer_echo.is_empty() or \
				int(pre_transfer_echo.get("chat_sequence",0))!=1:
			_fail("crossmap actor missing Map15 authoritative chat echo %s" %
				session.snapshot(),session)
			return
	if not session.move_target(40, 8):
		_fail("actor MoveTarget intent rejected", session)
		return
	var ack := {}
	var deadline := Time.get_ticks_msec() + 7000
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_FAILED:
			break
		if int(value.last_receipt_sequence) == 1:
			ack = value
			break
		OS.delay_msec(2)
	if ack.is_empty() or int(ack.last_receipt_status) != 0 or not bool(ack.world_ready):
		_fail("actor did not receive accepted MoveReceipt %s" % session.snapshot(), session)
		return
	if crossmap:
		# These calls are ordinary Godot ClientSession intents. In portal mode
		# NO external test-only teleport or client destination coordinates exist.
		if portal:
			if not session.npc_interact(999):
				_fail("unknown NPC interaction refused by local intent", session)
				return
			var rejected := _await_npc_result(session, 3, 1, 7000)
			if rejected.is_empty() or int(rejected.map_id) != 15:
				_fail("server did not deny unknown NPC without transfer %s" % session.snapshot(), session)
				return
			# A few authoritative ticks ensure the spectator sees the first
			# Move Delta before the legitimate NPC action changes AOI map.
			OS.delay_msec(250)
			if not session.npc_interact(46):
				_fail("Map15 NPC portal click rejected by session", session)
				return
		var original_epoch := int(ack.map_epoch)
		var in_map13 := {}
		deadline = Time.get_ticks_msec() + 7000
		while Time.get_ticks_msec() < deadline:
			var value: Dictionary = session.snapshot()
			if int(value.state) == WorldState.STATE_FAILED:
				break
			if bool(value.world_ready) and int(value.map_id) == 13 and int(value.map_epoch) > original_epoch and value.entities.is_empty():
				in_map13 = value
				break
			OS.delay_msec(2)
		if in_map13.is_empty():
			_fail("Map13 WorldReady/old AOI clear not observed %s" % session.snapshot(), session)
			return
		if not portal:
			if not session.send_nearby_chat("Map13 隔離訊息",2):
				_fail("Map13 nearby chat intent rejected",session)
				return
			var isolated_echo:=_wait_nearby_chat(
				session,self_id,"MultiHeroTwo","Map13 隔離訊息",7000)
			if isolated_echo.is_empty() or \
					int(isolated_echo.get("chat_sequence",0))!=2:
				_fail("Map13 authoritative self chat echo missing %s" %
					session.snapshot(),session)
				return
			print("CLIENT_CROSSMAP_MAP13_CHAT_SELF role=actor sequence=2")
			if int(session.snapshot().get("party_id",0))<=0:
				_fail("actor party membership lost on Map13 transfer",session)
				return
			if not session.send_party_chat("Map13 跨圖隊伍聊天",2):
				_fail("Map13 cross-map party chat intent rejected",session)
				return
			var party_echo:=_wait_party_chat(
				session,self_id,"MultiHeroTwo","Map13 跨圖隊伍聊天",7000)
			if party_echo.is_empty() or int(party_echo.get("chat_sequence",0))!=3:
				_fail("Map13 authoritative party self echo missing %s" %
					session.snapshot(),session)
				return
			print("CLIENT_CROSSMAP_PARTY_CHAT_SELF role=actor sequence=3")
			if not session.send_whisper_name("MultiHeroOne","Map13 跨圖私訊",3):
				_fail("Map13 cross-map whisper-by-name intent rejected",session)
				return
			var whisper_result:=_wait_whisper_result(session,4,remote_id,7000)
			if (
				whisper_result.is_empty() or
				int(whisper_result.get("status",-1))!=0 or
				str(whisper_result.get("target_name",""))!="MultiHeroOne"
			):
				_fail("Map13 whisper sender result missing %s" %
					session.snapshot(),session)
				return
			var outgoing:=_wait_outgoing_whisper(
				session,remote_id,"MultiHeroOne","Map13 跨圖私訊",7000)
			if outgoing.is_empty():
				_fail("authoritative whisper result did not publish outgoing history",session)
				return
			print("CLIENT_CROSSMAP_WHISPER_SENT role=actor sequence=4 target=%d" % remote_id)
			if session.send_whisper(self_id,"不能私訊自己",0):
				_fail("client admitted self whisper",session)
				return
			var history_before:=(session.snapshot().get("chat_messages",[]) as Array).size()
			const OFFLINE_ACTOR := 999999999
			if not session.send_whisper(OFFLINE_ACTOR,"不存在玩家私訊",0):
				_fail("offline-target whisper intent was rejected locally",session)
				return
			var unavailable:=_wait_whisper_result(
				session,5,OFFLINE_ACTOR,7000)
			if (
				unavailable.is_empty() or
				int(unavailable.get("status",-1))!=1 or
				int(unavailable.get("target_connection_epoch",-1))!=0 or
				not str(unavailable.get("target_name","")).is_empty()
			):
				_fail("offline-target whisper result mismatch %s" %
					session.snapshot(),session)
				return
			if (session.snapshot().get("chat_messages",[]) as Array).size()!=history_before:
				_fail("failed whisper created optimistic chat history",session)
				return
			print("CLIENT_CROSSMAP_WHISPER_UNAVAILABLE role=actor sequence=5 target=%d" % OFFLINE_ACTOR)
		if portal:
			var portal_result := _await_npc_result(session, 0, 2, 7000)
			if portal_result.is_empty() or int(portal_result.last_npc_result_map_epoch) != int(in_map13.map_epoch):
				_fail("Map15 NPC accepted receipt missing %s" % session.snapshot(), session)
				return
		print("CLIENT_%s_MAP13_READY role=actor epoch=%d" % ["PORTAL" if portal else "CROSSMAP", int(in_map13.map_epoch)])
		if not session.move_target(40, 8):
			_fail("Map13 MoveTarget rejected", session)
			return
		var map13_ack := {}
		deadline = Time.get_ticks_msec() + 7000
		while Time.get_ticks_msec() < deadline:
			var value: Dictionary = session.snapshot()
			if int(value.state) == WorldState.STATE_FAILED:
				break
			if int(value.map_id) == 13 and int(value.last_receipt_sequence) == 1 and int(value.last_receipt_status) == 0:
				map13_ack = value
				break
			OS.delay_msec(2)
		if map13_ack.is_empty():
			_fail("Map13 move ACK failed after epoch reset %s" % session.snapshot(), session)
			return
		print("CLIENT_%s_MAP13_MOVE_ACK role=actor epoch=%d" % ["PORTAL" if portal else "CROSSMAP", int(map13_ack.map_epoch)])
		if portal:
			if not session.npc_interact(1):
				_fail("Map13 NPC return click rejected by session", session)
				return
		var returned := {}
		deadline = Time.get_ticks_msec() + 7000
		while Time.get_ticks_msec() < deadline:
			var value: Dictionary = session.snapshot()
			if int(value.state) == WorldState.STATE_FAILED:
				break
			if bool(value.world_ready) and int(value.map_id) == 15 and int(value.map_epoch) > int(in_map13.map_epoch) and value.entities.size() == 1:
				returned = value
				break
			OS.delay_msec(2)
		if returned.is_empty() or int(returned.entities.keys()[0]) == self_id:
			_fail("return Map15 WorldReady and peer Spawn missing %s" % session.snapshot(), session)
			return
		if portal:
			var return_result := _await_npc_result(session, 0, 1, 7000)
			if return_result.is_empty() or int(return_result.last_npc_result_map_epoch) != int(returned.map_epoch):
				_fail("Map13 NPC accepted return receipt missing %s" % session.snapshot(), session)
				return
		# Regression for the player's reported second-transfer freeze: a new
		# MapEpoch is not usable until the WorldReady ACK barrier has cleared.
		# Exercise a normal input immediately after the return transition.
		if not session.move_target(40,8):
			_fail("return Map15 immediate MoveTarget rejected %s" % session.snapshot(),session)
			return
		var return_move_ack:={}
		deadline=Time.get_ticks_msec()+7000
		while Time.get_ticks_msec()<deadline:
			var value:Dictionary=session.snapshot()
			if int(value.state)==WorldState.STATE_FAILED:
				break
			if (
				int(value.map_id)==15 and
				int(value.map_epoch)==int(returned.map_epoch) and
				int(value.last_receipt_sequence)==1 and
				int(value.last_receipt_status)==0
			):
				return_move_ack=value
				break
			OS.delay_msec(2)
		if return_move_ack.is_empty():
			_fail("return Map15 movement remained blocked after second transfer %s"%session.snapshot(),session)
			return
		print("CLIENT_%s_RETURN_MOVE_ACK role=actor epoch=%d"%[
			"PORTAL" if portal else "CROSSMAP",int(returned.map_epoch)])
		# The spectator must see re-entry before this actor logs out.
		OS.delay_msec(700)
		session.stop()
		print("CLIENT_%s_PASS role=actor map15_map13_map15=PASS new_epochs=PASS world_ready=PASS old_aoi_clear=PASS npc_authority=%s nearby_chat_isolation=%s party_crossmap=%s whisper_crossmap=%s whisper_unavailable=%s" % ["PORTAL" if portal else "CROSSMAP","PASS" if portal else "NOT_TESTED","NOT_TESTED" if portal else "PASS","NOT_TESTED" if portal else "PASS","NOT_TESTED" if portal else "PASS","NOT_TESTED" if portal else "PASS"])
		quit(0)
		return
	# Allow a couple of 30Hz World ticks for authoritative movement,
	# independent of the headless test's render cadence.
	OS.delay_msec(180)
	session.stop()
	print("CLIENT_MULTI_PASS role=actor world_ready=PASS spawn=PASS nearby_chat=PASS world_chat=PASS move_ack=PASS")
	quit(0)


func _rejoin(session, self_id: int) -> void:
	var ready: Dictionary = session.snapshot()
	if absf(float(ready.x) - 40.0) > 0.001 or absf(float(ready.y) - 8.0) > 0.001:
		_fail("rejoined actor did not restore persisted disconnect position %s" % ready, session)
		return
	var peer := _wait_remote(session, 7000)
	if peer.is_empty() or int(peer.entities.keys()[0]) == self_id:
		_fail("rejoined actor did not receive observer Spawn %s" % session.snapshot(), session)
		return
	if not session.move_target(24, 8):
		_fail("rejoined actor MoveTarget intent rejected", session)
		return
	var acknowledged := {}
	var deadline := Time.get_ticks_msec() + 7000
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_FAILED:
			break
		if int(value.last_receipt_sequence) == 1 and int(value.last_receipt_status) == 0 and not value.self_state.is_empty() and not bool(value.self_state.moving) and absf(float(value.x) - 24.0) <= 0.001 and absf(float(value.y) - 8.0) <= 0.001:
			acknowledged = value
			break
		OS.delay_msec(2)
	if acknowledged.is_empty():
		_fail("rejoined actor did not complete authoritative movement %s" % session.snapshot(), session)
		return
	OS.delay_msec(180)
	session.stop()
	print("CLIENT_MULTI_RECONNECT_PASS role=rejoin persisted_spawn=40,8 move=24,8 peer_spawn=PASS authority=PASS")
	quit(0)


func _wait_nearby_chat(
		session,sender_id:int,sender_name:String,message:String,
		timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if int(value.state)==WorldState.STATE_FAILED:
			break
		for raw in value.get("chat_messages",[]):
			var row:Dictionary=raw
			if (
				int(row.get("sender_actor_id",0))==sender_id and
				str(row.get("sender_name",""))==sender_name and
				str(row.get("message",""))==message
			):
				return row
		OS.delay_msec(2)
	return {}


func _wait_world_chat(
		session,sender_id:int,sender_name:String,message:String,
		timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if int(value.state)==WorldState.STATE_FAILED:
			break
		for raw in value.get("chat_messages",[]):
			var row:Dictionary=raw
			if (
				int(row.get("channel",-1))==1 and
				int(row.get("sender_actor_id",0))==sender_id and
				str(row.get("sender_name",""))==sender_name and
				str(row.get("message",""))==message
			):
				return row
		OS.delay_msec(2)
	return {}


func _wait_party_invite(session,timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if int(value.state)==WorldState.STATE_FAILED:
			break
		var invite:Dictionary=value.get("pending_party_invite",{})
		if not invite.is_empty():
			return invite
		OS.delay_msec(2)
	return {}


func _wait_party_result(
		session,sequence:int,action:int,target_actor_id:int,
		timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if int(value.state)==WorldState.STATE_FAILED:
			break
		if (
			int(value.get("last_party_result_sequence",0))==sequence and
			int(value.get("pending_party_sequence",0))==0
		):
			var result:Dictionary=value.get("last_party_result",{})
			if (
				int(result.get("action",0))==action and
				int(result.get("target_actor_id",0))==target_actor_id
			):
				return result
		OS.delay_msec(2)
	return {}


func _wait_party_members(
		session,party_id:int,count:int,timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if int(value.state)==WorldState.STATE_FAILED:
			break
		if (
			int(value.get("party_id",0))==party_id and
			(value.get("party_members",{}) as Dictionary).size()==count
		):
			return value
		OS.delay_msec(2)
	return {}


func _wait_party_chat(
		session,sender_id:int,sender_name:String,message:String,
		timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if int(value.state)==WorldState.STATE_FAILED:
			break
		for raw in value.get("chat_messages",[]):
			var row:Dictionary=raw
			if (
				int(row.get("channel",-1))==3 and
				int(row.get("sender_actor_id",0))==sender_id and
				str(row.get("sender_name",""))==sender_name and
				str(row.get("message",""))==message
			):
				return row
		OS.delay_msec(2)
	return {}


func _wait_whisper(
		session,sender_id:int,sender_name:String,message:String,
		timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if int(value.state)==WorldState.STATE_FAILED:
			break
		for raw in value.get("chat_messages",[]):
			var row:Dictionary=raw
			if (
				int(row.get("channel",-1))==4 and
				str(row.get("direction",""))=="incoming" and
				int(row.get("sender_actor_id",0))==sender_id and
				str(row.get("sender_name",""))==sender_name and
				str(row.get("message",""))==message
			):
				return row
		OS.delay_msec(2)
	return {}


func _wait_whisper_result(
		session,sequence:int,target_actor_id:int,timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if int(value.state)==WorldState.STATE_FAILED:
			break
		if (
			int(value.get("last_whisper_result_sequence",0))==sequence and
			int(value.get("pending_whisper_sequence",0))==0
		):
			var result:Dictionary=value.get("last_whisper_result",{})
			if int(result.get("target_actor_id",0))==target_actor_id:
				return result
		OS.delay_msec(2)
	return {}


func _wait_outgoing_whisper(
		session,target_actor_id:int,target_name:String,message:String,
		timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=session.snapshot()
		if int(value.state)==WorldState.STATE_FAILED:
			break
		for raw in value.get("chat_messages",[]):
			var row:Dictionary=raw
			if (
				int(row.get("channel",-1))==4 and
				str(row.get("direction",""))=="outgoing" and
				int(row.get("target_actor_id",0))==target_actor_id and
				str(row.get("target_name",""))==target_name and
				str(row.get("message",""))==message
			):
				return row
		OS.delay_msec(2)
	return {}


func _has_nearby_chat(snapshot:Dictionary,message:String)->bool:
	for raw in snapshot.get("chat_messages",[]):
		if str((raw as Dictionary).get("message",""))==message:
			return true
	return false


func _await_npc_result(session, wanted_status: int, wanted_sequence: int, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_FAILED:
			break
		if int(value.last_npc_result_sequence) == wanted_sequence and int(value.last_npc_result_status) == wanted_status and int(value.pending_npc_sequence) == 0:
			return value
		OS.delay_msec(2)
	return {}

func _wait_character_list(session, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_SELECTING_CHARACTER:
			return value
		if int(value.state) == WorldState.STATE_FAILED:
			break
		OS.delay_msec(2)
	return {}

func _wait_ready(session, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_IN_WORLD and bool(value.world_ready):
			return value
		if int(value.state) == WorldState.STATE_FAILED:
			break
		OS.delay_msec(2)
	return {}

func _wait_remote(session, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.state) == WorldState.STATE_IN_WORLD and value.entities.size() == 1:
			return value
		if int(value.state) == WorldState.STATE_FAILED:
			break
		OS.delay_msec(2)
	return {}
