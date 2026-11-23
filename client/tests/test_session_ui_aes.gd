extends SceneTree
## Exercises the actual entry's UI callbacks; no private textures required.
const Main = preload("res://app/main.gd")
const State = preload("res://world/world_state.gd")
const Session = preload("res://session/client_session.gd")
var app: Node

class RespawnSession extends RefCounted:
	var requests := 0
	var attacks := 0
	var target_skills := 0
	var area_skills := 0
	var last_skill_id := 0
	var last_skill_target := -1
	var pickups := 0
	var uses := 0
	var last_use_uid := 0
	var discards := 0
	var last_discard_uid := 0
	var last_discard_quantity := 0
	var shop_opens := 0
	var shop_buys := 0
	var shop_sells := 0
	var last_shop_npc := 0
	var last_shop_item := 0
	var last_shop_uid := 0
	var last_shop_quantity := 0
	var storage_opens := 0
	var storage_deposits := 0
	var storage_withdrawals := 0
	var last_storage_npc := 0
	var last_storage_uid := 0
	var equipment_equips := 0
	var equipment_unequips := 0
	var last_equipment_uid := 0
	var bank_opens := 0
	var bank_deposits := 0
	var bank_withdrawals := 0
	var last_bank_npc := 0
	var last_bank_amount := 0
	var repair_opens := 0
	var repairs := 0
	var last_repair_npc := 0
	var last_repair_uid := 0
	var enhancement_opens := 0
	var enhancements := 0
	var last_enhancement_npc := 0
	var last_enhancement_uid := 0
	var trade_requests := 0
	var trade_accepts := 0
	var trade_rejects := 0
	var trade_adds := 0
	var trade_removes := 0
	var trade_money_sets := 0
	var trade_locks := 0
	var trade_confirms := 0
	var trade_cancels := 0
	var last_trade_target := 0
	var last_trade_uid := 0
	var last_trade_money := 0
	var player_shop_opens := 0
	var player_shop_closes := 0
	var player_shop_inspects := 0
	var player_shop_buys := 0
	var last_player_shop_uid := 0
	var last_player_shop_price := 0
	var last_player_shop_seller := 0
	var last_player_shop_listing := 0
	var nearby_chats := 0
	var last_nearby_message := ""
	var last_nearby_bubble := -1
	var whispers := 0
	var whisper_name_targets := 0
	var last_whisper_target := 0
	var last_whisper_target_name := ""
	var last_whisper_message := ""
	var last_whisper_bubble := -1
	var party_invites := 0
	var party_name_invites := 0
	var party_accepts := 0
	var party_rejects := 0
	var party_leaves := 0
	var party_kicks := 0
	var party_promotes := 0
	var party_chats := 0
	var last_party_target := 0
	var last_party_target_name := ""
	var last_party_message := ""
	var quest_opens := 0
	var quest_choices := 0
	var last_quest_npc := 0
	var last_quest_choice := 0
	var pvp_requests := 0
	var last_pvp_action := -1
	var pvp_attacks := 0
	var last_pvp_target := 0
	var pvp_skill_attacks := 0
	var last_pvp_skill_id := 0
	var last_pvp_skill_target := 0
	var gm_commands := 0
	var last_gm_target := 0
	var last_gm_state := -1
	func gm_set_pvp_name(target_actor_id:int,name_state:int)->bool:
		if target_actor_id<=0 or name_state<0 or name_state>4:
			return false
		gm_commands+=1
		last_gm_target=target_actor_id
		last_gm_state=name_state
		return true
	func attack_player(target_actor_id: int) -> bool:
		if target_actor_id <= 0:
			return false
		pvp_attacks += 1
		last_pvp_target = target_actor_id
		return true
	func cast_player_skill(skill_id: int, target_actor_id: int) -> bool:
		if skill_id <= 0 or target_actor_id <= 0:
			return false
		pvp_skill_attacks += 1
		last_pvp_skill_id = skill_id
		last_pvp_skill_target = target_actor_id
		return true
	func request_pvp_mode(action: int) -> bool:
		if action not in [0,1,2]:
			return false
		pvp_requests += 1
		last_pvp_action = action
		return true
	func use_recovery_item(item_uid: int) -> bool:
		if item_uid <= 0:
			return false
		uses += 1
		last_use_uid = item_uid
		return true
	func discard_inventory_item(item_uid: int, quantity: int = 1) -> bool:
		if item_uid <= 0 or quantity <= 0:
			return false
		discards += 1
		last_discard_uid = item_uid
		last_discard_quantity = quantity
		return true
	func pickup_ground_drop(drop_id: int) -> bool:
		if drop_id <= 0:
			return false
		pickups += 1
		return true
	func open_shop(npc_spawn_id: int) -> bool:
		if npc_spawn_id <= 0:
			return false
		shop_opens += 1
		last_shop_npc = npc_spawn_id
		return true
	func buy_shop_item(item_definition_id: int, quantity: int = 1) -> bool:
		if item_definition_id <= 0 or quantity <= 0:
			return false
		shop_buys += 1
		last_shop_item = item_definition_id
		last_shop_quantity = quantity
		return true
	func sell_shop_item(item_uid: int, quantity: int = 1) -> bool:
		if item_uid <= 0 or quantity <= 0:
			return false
		shop_sells += 1
		last_shop_uid = item_uid
		last_shop_quantity = quantity
		return true
	func open_storage(npc_spawn_id: int) -> bool:
		if npc_spawn_id <= 0:
			return false
		storage_opens += 1
		last_storage_npc = npc_spawn_id
		return true
	func deposit_storage_item(item_uid: int) -> bool:
		if item_uid <= 0:
			return false
		storage_deposits += 1
		last_storage_uid = item_uid
		return true
	func withdraw_storage_item(item_uid: int) -> bool:
		if item_uid <= 0:
			return false
		storage_withdrawals += 1
		last_storage_uid = item_uid
		return true
	func equip_inventory_item(item_uid: int) -> bool:
		if item_uid <= 0:
			return false
		equipment_equips += 1
		last_equipment_uid = item_uid
		return true
	func unequip_item(item_uid: int) -> bool:
		if item_uid <= 0:
			return false
		equipment_unequips += 1
		last_equipment_uid = item_uid
		return true
	func open_bank(npc_spawn_id: int) -> bool:
		if npc_spawn_id <= 0:
			return false
		bank_opens += 1
		last_bank_npc = npc_spawn_id
		return true
	func deposit_bank_money(amount: int) -> bool:
		if amount <= 0:
			return false
		bank_deposits += 1
		last_bank_amount = amount
		return true
	func withdraw_bank_money(amount: int) -> bool:
		if amount <= 0:
			return false
		bank_withdrawals += 1
		last_bank_amount = amount
		return true
	func open_repair(npc_spawn_id: int) -> bool:
		if npc_spawn_id <= 0:
			return false
		repair_opens += 1
		last_repair_npc = npc_spawn_id
		return true
	func repair_item(item_uid: int) -> bool:
		if item_uid <= 0:
			return false
		repairs += 1
		last_repair_uid = item_uid
		return true
	func open_enhancement(npc_spawn_id: int) -> bool:
		if npc_spawn_id <= 0:
			return false
		enhancement_opens += 1
		last_enhancement_npc = npc_spawn_id
		return true
	func enhance_item(item_uid: int) -> bool:
		if item_uid <= 0:
			return false
		enhancements += 1
		last_enhancement_uid = item_uid
		return true
	func request_trade(target_actor_id: int) -> bool:
		if target_actor_id <= 0:
			return false
		trade_requests += 1
		last_trade_target = target_actor_id
		return true
	func accept_trade() -> bool:
		trade_accepts += 1
		return true
	func reject_trade() -> bool:
		trade_rejects += 1
		return true
	func trade_add_item(item_uid: int) -> bool:
		if item_uid <= 0:
			return false
		trade_adds += 1
		last_trade_uid = item_uid
		return true
	func trade_remove_item(item_uid: int) -> bool:
		if item_uid <= 0:
			return false
		trade_removes += 1
		last_trade_uid = item_uid
		return true
	func trade_set_money(money: int) -> bool:
		if money < 0:
			return false
		trade_money_sets += 1
		last_trade_money = money
		return true
	func trade_lock() -> bool:
		trade_locks += 1
		return true
	func trade_final_confirm() -> bool:
		trade_confirms += 1
		return true
	func cancel_trade() -> bool:
		trade_cancels += 1
		return true
	func open_player_shop(item_uid: int,price: int) -> bool:
		if item_uid <= 0 or price <= 0:
			return false
		player_shop_opens += 1
		last_player_shop_uid = item_uid
		last_player_shop_price = price
		return true
	func close_player_shop(listing_id: int) -> bool:
		if listing_id <= 0:
			return false
		player_shop_closes += 1
		last_player_shop_listing = listing_id
		return true
	func inspect_player_shop(seller_actor_id: int) -> bool:
		if seller_actor_id <= 0:
			return false
		player_shop_inspects += 1
		last_player_shop_seller = seller_actor_id
		return true
	func buy_player_shop(seller_actor_id: int,listing_id: int) -> bool:
		if seller_actor_id <= 0 or listing_id <= 0:
			return false
		player_shop_buys += 1
		last_player_shop_seller = seller_actor_id
		last_player_shop_listing = listing_id
		return true
	func send_nearby_chat(message:String,bubble_type:int=0)->bool:
		if message.strip_edges().is_empty() or bubble_type<0 or bubble_type>5:
			return false
		nearby_chats += 1
		last_nearby_message = message
		last_nearby_bubble = bubble_type
		return true
	func send_whisper(target_actor_id:int,message:String,bubble_type:int=0)->bool:
		if target_actor_id<=0 or message.strip_edges().is_empty() or bubble_type<0 or bubble_type>5:
			return false
		whispers+=1
		last_whisper_target=target_actor_id
		last_whisper_message=message
		last_whisper_bubble=bubble_type
		return true
	func send_whisper_name(target_name:String,message:String,bubble_type:int=0)->bool:
		var clean:=target_name.strip_edges()
		if clean.is_empty() or message.strip_edges().is_empty() or bubble_type<0 or bubble_type>5:
			return false
		whisper_name_targets+=1
		last_whisper_target_name=clean
		last_whisper_message=message
		last_whisper_bubble=bubble_type
		return true
	func party_invite(target_actor_id:int)->bool:
		if target_actor_id<=0:
			return false
		party_invites+=1
		last_party_target=target_actor_id
		return true
	func party_invite_name(target_name:String)->bool:
		var clean:=target_name.strip_edges()
		if clean.is_empty():
			return false
		party_name_invites+=1
		last_party_target_name=clean
		return true
	func party_accept()->bool:
		party_accepts+=1
		return true
	func party_reject()->bool:
		party_rejects+=1
		return true
	func party_leave()->bool:
		party_leaves+=1
		return true
	func party_kick(target_actor_id:int)->bool:
		if target_actor_id<=0:
			return false
		party_kicks+=1
		last_party_target=target_actor_id
		return true
	func party_promote(target_actor_id:int)->bool:
		if target_actor_id<=0:
			return false
		party_promotes+=1
		last_party_target=target_actor_id
		return true
	func send_party_chat(message:String,bubble_type:int=0)->bool:
		if message.strip_edges().is_empty() or bubble_type<0 or bubble_type>5:
			return false
		party_chats+=1
		last_party_message=message
		return true
	func open_quest(npc_spawn_id:int)->bool:
		if npc_spawn_id<=0:
			return false
		quest_opens+=1
		last_quest_npc=npc_spawn_id
		return true
	func choose_quest_choice(choice_id:int)->bool:
		if choice_id<=0:
			return false
		quest_choices+=1
		last_quest_choice=choice_id
		return true
	func attack_monster(entity_id: int) -> bool:
		if entity_id <= 0:
			return false
		attacks += 1
		return true
	func cast_skill(skill_id: int, entity_id: int) -> bool:
		if skill_id <= 0 or entity_id <= 0:
			return false
		target_skills += 1
		last_skill_id = skill_id
		last_skill_target = entity_id
		return true
	func cast_area_skill(skill_id: int) -> bool:
		if skill_id <= 0:
			return false
		area_skills += 1
		last_skill_id = skill_id
		last_skill_target = 0
		return true
	func respawn() -> bool:
		requests += 1
		return true
	func stop() -> void:
		pass



class CharacterCreateSession extends RefCounted:
	var creates := 0
	var last_slot := -1
	var last_name := ""
	var last_creation: Dictionary = {}
	func create_character(slot: int, character_name: String, _appearance: Dictionary = {}, creation: Dictionary = {}) -> bool:
		if slot < 0 or character_name.is_empty():
			return false
		creates += 1
		last_slot = slot
		last_name = character_name
		last_creation = creation.duplicate(true)
		return true
	func stop() -> void:
		pass

class RegistrationSession extends RefCounted:
	var registrations:=0
	var last_username:=""
	func register_secure(
			_host:String,_port:int,username:String,password:String)->Error:
		if username.is_empty() or password.is_empty():
			return ERR_INVALID_PARAMETER
		registrations+=1
		last_username=username
		return OK
	func stop()->void:
		pass


class FailedSession extends RefCounted:
	func connect_secure(_host, _port, _user, _password):
		return OK
	func snapshot():
		return {"state":State.STATE_FAILED}
	func snapshot_since(_last_revision: int) -> Dictionary:
		return {"revision":0, "snapshot":snapshot()}
	func stop():
		pass


func _initialize() -> void:
	run.call_deferred()


func fail(message: String) -> void:
	if app != null:
		app.session_panel.disconnect_session()
	push_error("CLIENT_SESSION_UI_FAIL " + message)
	quit(1)


func check_attack_ui(panel,stub) -> void:
	if not panel.submit_attack_monster(1001) or stub.attacks != 1:
		fail("Visible monster attack UI did not forward intent")
		return
	panel._latest.player_dead = true
	if panel.submit_attack_monster(1001) or stub.attacks != 1:
		fail("Dead character attack UI bypassed death gate")
		return
	panel._latest.player_dead = false
	panel._latest.resync_pending = true
	if panel.submit_attack_monster(1001) or stub.attacks != 1:
		fail("AOI resync allowed a new attack intent")
		return
	panel._latest.resync_pending = false
	if not panel.submit_pickup_drop(501) or stub.pickups != 1:
		fail("Ground pickup UI did not send existing ClientSession intent")
		return
	panel._latest.pending_pickup_sequence = 2
	if panel.submit_pickup_drop(501) or stub.pickups != 1:
		fail("Pickup UI accepted a second outstanding request")
		return
	panel._latest.pending_pickup_sequence = 0
	panel._latest.resync_pending = true
	if panel.submit_pickup_drop(501) or stub.pickups != 1:
		fail("Pickup UI escaped AOI resync gate")
		return
	panel._latest.resync_pending = false
	panel._latest.player_dead = true
	if panel.submit_pickup_drop(501) or stub.pickups != 1:
		fail("Dead player pickup UI bypassed death gate")
		return
	panel._latest.player_dead = false


func check_skill_ui(panel, stub) -> void:
	panel._set_skill_controls()
	if not panel.skill_area_button.visible or panel.skill_area_button.disabled:
		fail("WorldReady combat player has no skill controls")
		return
	panel.skill_id_input.value = 99001
	panel.skill_area_button.pressed.emit()
	panel.skill_area_button.pressed.emit()
	if stub.area_skills != 1 or stub.last_skill_id != 99001 or stub.last_skill_target != 0 or not panel.skill_area_button.disabled:
		fail("Area skill button did not send exactly one server-authoritative intent")
		return
	panel._latest.skill_sequence = 1
	panel._latest.pending_skill_sequence = 1
	panel._skill_queued_at_ms = Time.get_ticks_msec()-11000
	panel._set_skill_controls()
	if not panel.skill_area_button.disabled or not panel.skill_status.text.contains("請勿重複施放") or stub.area_skills != 1:
		fail("Unconfirmed skill timeout allowed a second MP debit")
		return
	panel._latest.pending_skill_sequence = 0
	panel._latest.last_skill_result_sequence = 1
	panel._latest.last_skill_result_status = 0
	panel._set_skill_controls()
	if panel.skill_area_button.disabled or not panel.skill_status.text.contains("成功（代碼 0）"):
		fail("Durable skill receipt did not release the UI gate")
		return
	panel.skill_target_mode.button_pressed = true
	if not panel.submit_monster_action(1001) or stub.target_skills != 1 or stub.last_skill_target != 1001:
		fail("Monster click skill mode did not reuse existing cast_skill")
		return
	if panel.submit_monster_action(1001) or stub.target_skills != 1:
		fail("Queued skill allowed duplicate monster click")
		return
	panel._latest.player_dead = true
	panel._set_skill_controls()
	if panel.submit_monster_action(1001) or panel.submit_area_skill(99001):
		fail("Dead character bypassed skill UI authority gate")
		return
	panel._latest.player_dead = false
	panel._latest.resync_pending = true
	panel._latest.pending_skill_sequence = 2
	panel._skill_queued_at_ms = Time.get_ticks_msec()-11000
	panel._set_skill_controls()
	if panel._skill_queued_after < 0 or not panel.skill_area_button.disabled:
		fail("Same-epoch resync cleared an unsettled skill")
		return
	if panel.submit_monster_action(1001) or panel.submit_area_skill(99001):
		fail("Resync allowed skill intent")
		return
	panel._latest.resync_pending = false
	panel._latest.pending_skill_sequence = 0
	panel._latest.reconnecting = true
	panel._set_skill_controls()
	if panel.submit_area_skill(99001):
		fail("Reconnect allowed skill intent")
		return
	panel._latest.reconnecting = false
	panel._latest.pending_world_ready_ack_epoch = 3
	panel._set_skill_controls()
	if panel.submit_area_skill(99001):
		fail("Unacknowledged WorldReady allowed skill intent")
		return
	panel._latest.pending_world_ready_ack_epoch = 0
	panel._latest.pending_skill_sequence = 0
	panel.skill_target_mode.button_pressed = false
	panel._set_skill_controls()
	if not panel.submit_monster_action(1001) or stub.attacks != 2:
		fail("Turning off skill mode did not restore ordinary monster attacks")


func run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 1 or args.size() > 2:
		fail("Expected AES port and optional screenshot path")
		return
	app = Main.new()
	app.auto_start = false
	root.add_child(app)
	var panel = app.session_panel
	panel.show()
	var scroll = panel.get_node_or_null("SessionOptionsScroll")
	if scroll == null or not scroll is ScrollContainer or not scroll.clip_contents:
		fail("Long SessionPanel must clip gameplay controls in a scrollable form")
		return
	panel.username_input.text = "fixture_user"
	panel.password_input.text = "bad"
	panel.login_button.pressed.emit()
	if panel.active or not panel.password_input.text.is_empty():
		fail("Invalid credentials accepted or retained")
		return
	panel.session = FailedSession.new()
	panel.password_input.text = "Fixture-Password-123!"
	panel.login_button.pressed.emit()
	var failure_deadline := Time.get_ticks_msec()+2000
	while panel.active and Time.get_ticks_msec()<failure_deadline:
		await process_frame
	if panel.active or panel.login_button.disabled or app._map_buttons[0].disabled or not panel.password_input.text.is_empty():
		fail("Failed session did not return cleanly to login")
		return
	panel.session = Session.new()
	panel.server_host = "127.0.0.1"
	panel.server_port = int(args[0])
	panel.password_input.text = "Fixture-Password-123!"
	panel.login_button.pressed.emit()
	if not panel.active or not panel.password_input.text.is_empty() or not panel.login_button.disabled or not app._map_buttons[0].disabled:
		fail("Login did not clear secret/lock offline controls")
		return
	var deadline := Time.get_ticks_msec()+10000
	while panel.characters.item_count == 0 and Time.get_ticks_msec()<deadline and panel.active:
		await process_frame
	if panel.characters.item_count != 1 or panel.choose_button.disabled:
		fail("UI character list unavailable")
		return
	if args.size() == 2:
		await RenderingServer.frame_post_draw
		if root.get_texture().get_image().save_png(args[1]) != OK:
			fail("UI screenshot save")
			return
	panel.characters.select(0)
	# This login fixture predates mandatory per-character proof. Migrate it
	# through the same SessionPanel management action exposed to authenticated
	# UI shells, then exercise the actual choose-password field/button path.
	var character_id := int(panel._characters[0].character_id)
	var manage_before := int(panel._latest.get("character_manage_result_revision",0))
	if not panel.change_character_password(character_id,"","2468"):
		fail("UI secondary password migration intent rejected")
		return
	while int(panel._latest.get("character_manage_result_revision",0)) <= manage_before and Time.get_ticks_msec()<deadline and panel.active:
		await process_frame
	if int(panel._latest.get("character_manage_status",-1)) != 0:
		fail("UI secondary password migration failed")
		return
	panel.character_choose_password_input.text = "2468"
	panel.choose_button.pressed.emit()
	while not bool(panel._latest.get("world_ready",false)) and Time.get_ticks_msec()<deadline and panel.active:
		await process_frame
	var entered: Dictionary = panel._latest
	if not panel.active or int(entered.get("state",-1)) != State.STATE_IN_WORLD or not bool(entered.get("world_ready",false)):
		fail("UI did not observe WorldReady")
		return
	if app.world_view.phase != "closed":
		fail("Missing private assets must not fake a ready map")
		return
	# Real authenticated AES must automatically query the durable PvP mode
	# after WorldReady ACK. No click, Render FPS, or fake server result.
	while not bool(panel._latest.get("pvp_known",false)) and Time.get_ticks_msec()<deadline and panel.active:
		await process_frame
	var pvp: Dictionary = panel._latest
	if not bool(pvp.get("pvp_known",false)) or int(pvp.get("pvp_mode",-1)) != 0 or bool(pvp.get("pvp_map_allows",true)) or int(pvp.get("pvp_sequence",0)) != 1 or int(pvp.get("pending_pvp_sequence",-1)) != 0 or int(pvp.get("last_pvp_result",{}).get("status",-1)) != 0:
		fail("Real AES PvP query missing or Map15 NonPK authority incorrect: %s" % pvp)
		return
	if not bool(entered.get("inventory_ready",false)) or not (entered.get("inventory_items",{}) as Dictionary).is_empty() or not panel.inventory_panel.inventory_button.visible:
		fail("Real AES login did not supply complete empty durable inventory baseline")
		return
	panel.inventory_panel.inventory_button.pressed.emit()
	if not panel.inventory_panel.inventory_preview.visible or not panel.inventory_panel.inventory_preview.text.contains("沒有物品"):
		fail("Inventory UI failed to render authoritative zero-item baseline")
		return
	panel.inventory_panel.inventory_button.pressed.emit()
	if panel.inventory_panel.inventory_preview.visible:
		fail("Inventory UI did not collapse")
		return
	if not panel.submit_move(Vector2(24,8)):
		fail("UI Move intent rejected")
		return
	var acknowledged: Dictionary = panel.session.snapshot()
	while int(acknowledged.last_receipt_sequence) != 1 and Time.get_ticks_msec()<deadline:
		await process_frame
		acknowledged = panel.session.snapshot()
	if int(acknowledged.last_receipt_sequence) != 1 or int(acknowledged.last_receipt_status) != 0:
		fail("UI path did not receive authoritative receipt")
		return
	panel.logout_button.pressed.emit()
	if panel.inventory_panel.inventory_button.visible or panel.inventory_panel.inventory_preview.visible:
		fail("Logout left previous character inventory visible")
		return
	if panel.active or panel.login_button.disabled or app._map_buttons[0].disabled or not panel.characters.item_count == 0:
		fail("Logout left stale UI or binding")
		return

	var registration_stub:=RegistrationSession.new()
	panel.session=registration_stub
	panel.set_process(false)
	panel.username_input.text="ui_register"
	panel.password_input.text="Ui-Registration-Password-123!"
	panel.register_button.pressed.emit()
	if (
		registration_stub.registrations!=1 or
		registration_stub.last_username!="ui_register" or
		not panel.password_input.text.is_empty() or
		not panel.active
	):
		fail("Registration UI did not route explicit register_secure intent")
		return
	panel.disconnect_session()
	if panel.active or panel.register_button.disabled:
		fail("Registration UI did not return to reusable login state")
		return

	# Character creation controls must expose exactly the authoritative Session
	# intent while selecting; the real AES creation/store lifecycle is covered
	# by the C# authenticated E2E.
	var create_stub := CharacterCreateSession.new()
	panel.session = create_stub
	# The remaining checks intentionally use narrow command stubs and call each
	# UI reducer directly. Stop the real SessionPanel poll loop here so these
	# stubs are not required to impersonate the full ClientSession snapshot API.
	panel.set_process(false)
	panel.active = true
	panel._latest = {"state":State.STATE_SELECTING_CHARACTER,
		"characters":[],"character_create_status":-1}
	panel._set_controls()
	if not panel.create_character_button.visible or panel.create_character_button.disabled:
		fail("Character creation controls unavailable while selecting")
		return
	panel.character_slot_input.value = 2
	panel.character_name_input.text = "UiCreatedHero"
	panel.character_strength_input.value = 3
	panel.character_dexterity_input.value = 2
	panel.character_constitution_input.value = 2
	panel.character_wisdom_input.value = 1
	panel.character_password_input.text = "2468"
	panel.character_password_confirm_input.text = "2468"
	panel.create_character_button.pressed.emit()
	var expected_creation := {"strength":3,"dexterity":2,"constitution":2,"wisdom":1,"password":"2468"}
	if create_stub.creates != 1 or create_stub.last_slot != 2 or create_stub.last_name != "UiCreatedHero" or create_stub.last_creation != expected_creation:
		fail("Character creation UI did not forward slot/name/attributes/secondary password exactly once")
		return
	if not panel.character_password_input.text.is_empty() or not panel.character_password_confirm_input.text.is_empty():
		fail("Character creation UI kept secondary password after queuing")
		return
	panel._latest.state = State.STATE_CREATING_CHARACTER
	panel._set_controls()
	if not panel.create_character_button.disabled or panel.character_name_input.editable:
		fail("Character creation UI allowed duplicate while awaiting Server")
		return

	# Use a stub only for the button's state/intent boundary; the real
	# authenticated respawn wire and durable HP settlement have separate E2E.
	var stub := RespawnSession.new()
	panel.session = stub
	panel.active = true
	panel._latest = {"state":State.STATE_IN_WORLD,"world_ready":true,
		"combat_available":true,"player_dead":true,"map_epoch":3,
		"last_world_ready_ack_epoch":3,"respawn_sequence":0,
		"last_respawn_result_sequence":0}
	panel._set_respawn_controls()
	if not panel.respawn_button.visible or panel.respawn_button.disabled:
		fail("Dead player cannot access respawn UI")
		return
	panel.respawn_button.pressed.emit()
	panel.respawn_button.pressed.emit()
	if stub.requests != 1 or not panel.respawn_button.disabled:
		fail("Respawn UI emitted duplicate intent")
		return
	panel._latest.respawn_sequence = 1
	panel._set_respawn_controls()
	if not panel.respawn_button.disabled:
		fail("Pending authoritative respawn result re-enabled UI")
		return
	panel._latest.last_respawn_result_sequence = 1
	panel._set_respawn_controls()
	if panel.respawn_button.disabled:
		fail("Rejected respawn cannot be retried")
		return
	panel._latest.resync_pending = true
	panel._set_respawn_controls()
	if not panel.respawn_button.disabled:
		fail("Respawn enabled during AOI resync")
		return
	panel._latest.resync_pending = false
	panel._latest.player_dead = false
	panel._set_respawn_controls()
	if panel.respawn_button.visible:
		fail("Alive player retained respawn button")
		return
	panel._latest.player_dead = false
	check_attack_ui(panel,stub)
	check_skill_ui(panel,stub)
	panel._latest = {"combat_available":true,"player_current_hp":88,"player_max_hp":100,"player_current_mp":7,"player_max_mp":20}
	if panel._combat_status_suffix() != "｜HP 88/100｜MP 7/20":
		fail("UI failed to display server HP/MP without estimating local skill costs")
		return
	# Standalone visual binding from an authenticated snapshot: never use
	# unverified original item names, and refresh on reconnect generation.
	panel._latest = {"state":State.STATE_IN_WORLD,"world_ready":true,
		"inventory_ready":true,"actor_id":11,"reconnect_count":1,
		"inventory_revision":1,"inventory_items":{7001:{
			"item_uid":7001,"item_definition_id":2701,
			"quantity":2,"drop_id":501}}}
	panel._refresh_inventory_panel()
	var verified_name_2701: String = str(panel.item_catalog.name_for(2701))
	if not panel.inventory_panel.inventory_preview.text.contains(str(verified_name_2701)) or not panel.inventory_panel.inventory_preview.text.contains("×2") or not panel.inventory_panel.inventory_button.text.contains("1 件"):
		fail("Durable inventory items not reflected in UI")
		return
	panel._latest = {"state":State.STATE_IN_WORLD,"world_ready":true,
		"inventory_ready":true,"combat_available":true,
		"actor_id":22,"reconnect_count":2,
		"inventory_revision":1,"inventory_items":{7002:{
			"item_uid":7002,"item_definition_id":3701,
			"quantity":3,"drop_id":502}}}
	panel._refresh_inventory_panel()
	var verified_name_3701: String = str(panel.item_catalog.name_for(3701))
	if panel.inventory_panel.inventory_preview.text.contains("UID 7001") or not panel.inventory_panel.inventory_preview.text.contains(str(verified_name_3701)) or not panel.inventory_panel.inventory_preview.text.contains("×3"):
		fail("Same inventory revision across new character retained stale UI")
		return
	panel.inventory_panel.inventory_button.pressed.emit()
	if not panel.inventory_panel.inventory_preview.visible:
		fail("Inventory did not expand while loaded")
		return
	var metadata := JSON.stringify({
		"kind":"source_derived_item_catalog","version":1,
		"source_reference":"ci:synthetic converted item evidence",
		"item_count":1,
		"items":[{"item_id":2701,"name":"測試素材原名",
			"category":"general","icon_id":421,
			"source_path":"general/2701.tres",
			"source_sha256":"0123456789abcdef".repeat(4)}]})
	if not panel.item_catalog.load_json_text(metadata):
		fail("Item metadata fixture could not be injected for UI")
		return
	var many := {}
	for index in 85:
		var uid := 10000+index
		many[uid] = {"item_uid":uid,
			"item_definition_id":2701 if index == 0 else 40000+index,
			"quantity":3 if index == 0 else 1,"drop_id":50000+index,
			"enhancement_level":5 if index == 0 else 0}
	panel._latest.inventory_items = many
	panel._latest.inventory_revision = 2
	panel._refresh_inventory_panel()
	if not panel.inventory_panel.inventory_preview.text.contains("測試素材原名") or not panel.inventory_panel.inventory_page_label.text.contains("1 / 2") or panel.inventory_panel.inventory_preview.text.contains("UID 10080"):
		fail("Inventory page one did not use source-derived name and 80-row cap")
		return
	panel.inventory_panel.inventory_next.pressed.emit()
	if not panel.inventory_panel.inventory_preview.text.contains("UID 10080") or panel.inventory_panel.inventory_preview.text.contains("UID 10000") or not panel.inventory_panel.inventory_page_label.text.contains("2 / 2"):
		fail("Inventory next page did not display remaining five items")
		return
	panel.inventory_panel.inventory_search.text = "2701"
	panel.inventory_panel.inventory_search.text_changed.emit("2701")
	if not panel.inventory_panel.inventory_preview.text.contains("測試素材原名") or panel.inventory_panel.inventory_preview.text.contains("UID 10080") or not panel.inventory_panel.inventory_page_label.text.contains("1 / 1"):
		fail("Inventory search failed to filter on authoritative item definition")
		return
	if panel.inventory_panel.recovery_picker.item_count != 1 or panel.inventory_panel.recovery_button.disabled:
		fail("Inventory recovery UID selector did not resolve visible page")
		return
	panel.inventory_panel.recovery_button.pressed.emit()
	if stub.uses != 1 or stub.last_use_uid != 10000 or not panel.inventory_panel.recovery_button.disabled:
		fail("Recovery UI did not send one chosen UID or left double-click enabled")
		return
	panel._latest.pending_recovery_sequence = 1
	panel._refresh_inventory_panel()
	if not panel.inventory_panel.recovery_button.disabled:
		fail("Pending authoritative recovery result allowed another click")
		return
	panel._latest.pending_recovery_sequence = 0
	panel._latest.last_recovery_result = {
		"status":0,"current_hp":90,"max_hp":100,"remaining_quantity":1}
	panel._latest.inventory_revision = 3
	panel._refresh_inventory_panel()
	if not panel.inventory_panel.recovery_status.text.contains("90 / 100"):
		fail("Recovery status did not display authoritative committed HP")
		return
	if panel.inventory_panel.discard_button.disabled:
		fail("Loaded inventory did not expose server-authoritative discard")
		return
	if int(panel.inventory_panel.discard_quantity.max_value) != 3 or int(panel.inventory_panel.discard_quantity.value) != 1:
		fail("Discard quantity did not bind to selected authoritative stack")
		return
	panel.inventory_panel.discard_all_button.pressed.emit()
	if int(panel.inventory_panel.discard_quantity.value) != 3:
		fail("Discard-all did not select full stack")
		return
	panel.inventory_panel.discard_button.pressed.emit()
	if stub.discards != 0 or not panel.inventory_panel.discard_confirm.dialog_text.contains("UID 10000"):
		fail("Destructive discard bypassed confirmation")
		return
	panel.inventory_panel.discard_confirm.canceled.emit()
	if not panel.inventory_panel._pending_discard_confirmation.is_empty() or stub.discards != 0:
		fail("Cancelled permanent discard left a stale confirmation lock")
		return
	panel.inventory_panel.discard_button.pressed.emit()
	panel._latest.actor_id = 99
	panel.inventory_panel.discard_confirm.confirmed.emit()
	if stub.discards != 0:
		fail("Confirmed discard crossed character identity")
		return
	panel._latest.actor_id = 22
	panel.inventory_panel.discard_button.pressed.emit()
	panel.inventory_panel.discard_confirm.confirmed.emit()
	if stub.discards != 1 or stub.last_discard_uid != 10000 or stub.last_discard_quantity != 3 or not panel.inventory_panel.discard_button.disabled:
		fail("Confirmed full-stack discard did not send exact UID and quantity once")
		return
	panel._latest.pending_discard_sequence = 1
	panel._refresh_inventory_panel()
	if not panel.inventory_panel.discard_button.disabled or not panel.inventory_panel.recovery_button.disabled:
		fail("Pending discard allowed another inventory mutation")
		return
	panel._latest.pending_discard_sequence = 0
	panel._latest.last_discard_result = {
		"status":0,"discarded_quantity":3,"remaining_quantity":0}
	panel._latest.inventory_revision = 4
	panel._refresh_inventory_panel()
	if not panel.inventory_panel.discard_status.text.contains("已移除 3"):
		fail("Discard status did not display authoritative committed quantity")
		return

	# Shop UI is only an adapter to server-owned catalog/money/inventory.
	panel._latest = {"state":State.STATE_IN_WORLD,"world_ready":true,
		"inventory_ready":true,"player_dead":false,"reconnecting":false,
		"resync_pending":false,"active_shop_npc":0,"pending_shop_sequence":0,
		"shop_sequence":0,"shop_money":0,"shop_items":{},
		"inventory_revision":4,"inventory_items":many}
	panel.npc_shop_panel.apply_snapshot(panel._latest, panel.active)
	panel.npc_shop_panel.shop_npc_input.value = 99
	panel.npc_shop_panel.shop_open_button.pressed.emit()
	if stub.shop_opens != 1 or stub.last_shop_npc != 99 or not panel.npc_shop_panel.shop_open_button.disabled:
		fail("Shop open UI did not send exactly one NPC intent")
		return
	panel._latest.active_shop_npc = 99
	panel._latest.shop_sequence = 1
	panel._latest.shop_money = 25
	panel._latest.shop_items = {2701:{
		"item_definition_id":2701,"buy_price":10,"sell_price":5}}
	panel._latest.pending_shop_sequence = 0
	panel.npc_shop_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.npc_shop_panel.shop_money_label.text.contains("25") or panel.npc_shop_panel.shop_item_picker.item_count != 1 or panel.npc_shop_panel.shop_sell_picker.item_count != 1 or panel.npc_shop_panel.shop_buy_button.disabled or panel.npc_shop_panel.shop_sell_button.disabled:
		fail("Server shop snapshot was not rendered as playable controls")
		return
	# Switching sell UID or refreshing owned stack must never alter buy quantity.
	panel.npc_shop_panel.shop_quantity.value = 7
	panel.npc_shop_panel.shop_sell_quantity.value = 3
	panel.npc_shop_panel._update_shop_sell_quantity()
	if int(panel.npc_shop_panel.shop_quantity.value) != 7 or int(panel.npc_shop_panel.shop_sell_quantity.max_value) != 3:
		fail("Sell stack selection modified independent buy quantity or sell limit")
		return
	panel.npc_shop_panel.shop_quantity.value = 2
	panel.npc_shop_panel.shop_buy_button.pressed.emit()
	if stub.shop_buys != 1 or stub.last_shop_item != 2701 or stub.last_shop_quantity != 2 or not panel.npc_shop_panel.shop_buy_button.disabled:
		fail("Shop buy UI did not send exact authoritative item/quantity once")
		return
	panel._latest.pending_shop_sequence = 2
	panel.npc_shop_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.npc_shop_panel.shop_buy_button.disabled or not panel.npc_shop_panel.shop_sell_button.disabled:
		fail("Pending shop transaction allowed another economy mutation")
		return
	panel._latest.pending_shop_sequence = 0
	panel._latest.shop_sequence = 2
	panel._latest.inventory_revision = 5
	panel.npc_shop_panel.apply_snapshot(panel._latest, panel.active)
	panel.npc_shop_panel.shop_quantity.value = 9
	panel.npc_shop_panel.shop_sell_quantity.value = 1
	panel.npc_shop_panel.shop_sell_button.pressed.emit()
	if stub.shop_sells != 1 or stub.last_shop_uid != 10000 or stub.last_shop_quantity != 1 or int(panel.npc_shop_panel.shop_quantity.value) != 9:
		fail("Shop sell UI did not use independent sell quantity and owned Item UID")
		return
	if panel.submit_shop_sell(10000,4) or stub.shop_sells != 1:
		fail("Shop UI did not reject sell quantity exceeding authoritative owned stack")
		return

	# Personal storage UI only selects exact Item UIDs; no client quantity or
	# fee is invented. Whole-stack policy remains server-authoritative.
	panel._latest.active_storage_npc = 0
	panel._latest.pending_storage_sequence = 0
	panel._latest.storage_sequence = 0
	panel._latest.storage_ready = false
	panel._latest.storage_revision = 0
	panel._latest.storage_items = {}
	panel._latest.last_storage_open_status = -1
	panel._latest.last_storage_result = {}
	panel.storage_panel.apply_snapshot(panel._latest, panel.active)
	panel.storage_panel.storage_npc_input.value = 88
	panel.storage_panel.storage_open_button.pressed.emit()
	if stub.storage_opens != 1 or stub.last_storage_npc != 88 or not panel.storage_panel.storage_open_button.disabled:
		fail("Storage open UI did not send exactly one NPC intent")
		return
	panel._latest.active_storage_npc = 88
	panel._latest.storage_ready = true
	panel._latest.storage_sequence = 1
	panel._latest.storage_revision = 1
	panel._latest.storage_items = {20000:{
		"item_uid":20000,"item_definition_id":4001,"quantity":2,"drop_id":0}}
	panel.storage_panel.apply_snapshot(panel._latest, panel.active)
	if panel.storage_panel.storage_deposit_picker.item_count == 0 or panel.storage_panel.storage_withdraw_picker.item_count != 1 or panel.storage_panel.storage_deposit_button.disabled or panel.storage_panel.storage_withdraw_button.disabled:
		fail("Authoritative storage baseline was not rendered as playable controls")
		return
	panel.storage_panel.storage_deposit_button.pressed.emit()
	if stub.storage_deposits != 1 or stub.last_storage_uid != 10000:
		fail("Storage deposit UI did not use selected owned Item UID")
		return
	panel.storage_panel.apply_snapshot(panel._latest, panel.active)
	panel.storage_panel.storage_withdraw_button.pressed.emit()
	if stub.storage_withdrawals != 1 or stub.last_storage_uid != 20000:
		fail("Storage withdraw UI did not use selected stored Item UID")
		return
	panel._latest.pending_storage_sequence = 2
	panel.storage_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.storage_panel.storage_deposit_button.disabled or not panel.storage_panel.storage_withdraw_button.disabled:
		fail("Pending storage mutation allowed another item move")
		return

	# Equipment UI must render only the opaque slot id supplied by Server.
	# It may choose an exact UID, but it must not invent slot semantics or stats.
	panel._latest.pending_storage_sequence = 0
	panel._latest.pending_equipment_sequence = 0
	panel._latest.equipment_sequence = 0
	panel._latest.equipment_revision = 0
	panel._latest.equipment_ready = false
	panel._latest.equipment_items = {}
	panel._latest.last_equipment_result = {}
	panel._refresh_inventory_panel()
	if not panel.inventory_panel.equipment_equip_button.disabled or not panel.inventory_panel.equipment_unequip_button.disabled or not panel.inventory_panel.equipment_status.text.contains("尚未由伺服器啟用"):
		fail("Equipment UI was usable without authoritative baseline")
		return
	panel._latest.equipment_ready = true
	panel._latest.equipment_revision = 1
	panel._latest.equipment_items = {30000:{
		"item_uid":30000,"slot_id":4,"item_definition_id":4001,"drop_id":0}}
	panel._refresh_inventory_panel()
	if panel.inventory_panel.equipment_inventory_picker.item_count == 0 or panel.inventory_panel.equipment_equipped_picker.item_count != 1 or panel.inventory_panel.equipment_equip_button.disabled or panel.inventory_panel.equipment_unequip_button.disabled:
		fail("Authoritative equipment baseline was not rendered as playable controls")
		return
	if not panel.inventory_panel.equipment_equipped_picker.get_item_text(0).contains("裝備欄位 4"):
		fail("Equipment UI guessed or hid opaque server slot id")
		return
	panel.inventory_panel.equipment_equip_button.pressed.emit()
	if stub.equipment_equips != 1 or stub.last_equipment_uid != 10000:
		fail("Equipment equip UI did not use selected inventory UID")
		return
	panel._refresh_inventory_panel()
	panel.inventory_panel.equipment_unequip_button.pressed.emit()
	if stub.equipment_unequips != 1 or stub.last_equipment_uid != 30000:
		fail("Equipment unequip UI did not use selected equipped UID")
		return
	panel._latest.pending_equipment_sequence = 1
	panel._refresh_inventory_panel()
	if not panel.inventory_panel.equipment_equip_button.disabled or not panel.inventory_panel.equipment_unequip_button.disabled:
		fail("Pending equipment mutation allowed another item move")
		return
	panel._latest.pending_equipment_sequence = 0
	# Bank scope and balances are display-only values supplied by Server.
	panel._latest.pending_bank_sequence = 0
	panel._latest.bank_sequence = 0
	panel._latest.active_bank_npc = 0
	panel._latest.bank_scope = 0
	panel._latest.bank_wallet_money = 0
	panel._latest.bank_money = 0
	panel._latest.last_bank_open_status = -1
	panel._latest.last_bank_result = {}
	panel.bank_panel.apply_snapshot(panel._latest, panel.active)
	if panel.bank_panel.bank_open_button.get_parent() == null or panel.inventory_panel.equipment_equip_button.get_parent() == null:
		fail("Equipment/bank controls were not attached to the visible SessionPanel")
		return
	panel.bank_panel.bank_npc_input.value = 77
	panel.bank_panel.bank_open_button.pressed.emit()
	if stub.bank_opens != 1 or stub.last_bank_npc != 77 or not panel.bank_panel.bank_open_button.disabled:
		fail("Bank open UI did not send exactly one NPC intent")
		return
	panel._latest.active_bank_npc = 77
	panel._latest.bank_scope = 2
	panel._latest.bank_wallet_money = 1000
	panel._latest.bank_money = 400
	panel._latest.bank_sequence = 1
	panel.bank_panel.bank_amount_input.value = 300
	panel.bank_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.bank_panel.bank_scope_label.text.contains("帳號共用") or not panel.bank_panel.bank_wallet_label.text.contains("1000") or not panel.bank_panel.bank_balance_label.text.contains("400") or panel.bank_panel.bank_deposit_button.disabled or panel.bank_panel.bank_withdraw_button.disabled:
		fail("Authoritative bank scope/balances were not rendered as playable controls")
		return
	panel.bank_panel.bank_deposit_button.pressed.emit()
	if stub.bank_deposits != 1 or stub.last_bank_amount != 300:
		fail("Bank deposit UI did not send selected amount")
		return
	panel.bank_panel.apply_snapshot(panel._latest, panel.active)
	panel.bank_panel.bank_withdraw_button.pressed.emit()
	if stub.bank_withdrawals != 1 or stub.last_bank_amount != 300:
		fail("Bank withdraw UI did not send selected amount")
		return
	panel.bank_panel.bank_amount_input.value = 500
	panel.bank_panel.apply_snapshot(panel._latest, panel.active)
	if panel.bank_panel.bank_withdraw_button.disabled != true or panel.bank_panel.bank_deposit_button.disabled:
		fail("Bank UI did not apply current authoritative balance bounds")
		return
	panel._latest.pending_bank_sequence = 2
	panel.bank_panel.bank_amount_input.value = 100
	panel.bank_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.bank_panel.bank_deposit_button.disabled or not panel.bank_panel.bank_withdraw_button.disabled:
		fail("Pending bank mutation allowed another money move")
		return

	panel._latest.pending_bank_sequence = 0
	# Repair UI must display the exact Server preview and send only the chosen UID.
	panel._latest.pending_repair_sequence = 0
	panel._latest.repair_sequence = 0
	panel._latest.active_repair_npc = 0
	panel._latest.repair_ready = false
	panel._latest.repair_revision = 0
	panel._latest.repair_wallet_money = 0
	panel._latest.repair_items = {}
	panel._latest.last_repair_open_status = -1
	panel._latest.last_repair_result = {}
	panel.repair_panel.apply_snapshot(panel._latest, panel.active)
	if panel.repair_panel.repair_open_button.get_parent() == null or not panel.repair_panel.repair_button.disabled:
		fail("Repair controls were not attached or were usable before server preview")
		return
	panel.repair_panel.repair_npc_input.value = 66
	panel.repair_panel.repair_open_button.pressed.emit()
	if stub.repair_opens != 1 or stub.last_repair_npc != 66 or not panel.repair_panel.repair_open_button.disabled:
		fail("Repair open UI did not send exactly one NPC intent")
		return
	panel._latest.active_repair_npc = 66
	panel._latest.repair_ready = true
	panel._latest.repair_sequence = 1
	panel._latest.repair_revision = 1
	panel._latest.repair_wallet_money = 100
	panel._latest.repair_items = {10000:{
		"item_uid":10000,"item_definition_id":4001,"location":1,
		"current_durability":80,"max_durability":100,"cost":37}}
	panel.repair_panel.apply_snapshot(panel._latest, panel.active)
	if panel.repair_panel.repair_item_picker.item_count != 1 or not panel.repair_panel.repair_item_picker.get_item_text(0).contains("耐久 80/100") or not panel.repair_panel.repair_item_picker.get_item_text(0).contains("費用 37") or not panel.repair_panel.repair_wallet_label.text.contains("100") or panel.repair_panel.repair_button.disabled:
		fail("Repair UI did not render exact authoritative preview")
		return
	panel.repair_panel.repair_button.pressed.emit()
	if stub.repairs != 1 or stub.last_repair_uid != 10000:
		fail("Repair UI did not send selected UID only")
		return
	panel._latest.repair_revision = 2
	panel._latest.repair_wallet_money = 63
	panel._latest.repair_items[10000].current_durability = 100
	panel._latest.repair_items[10000].cost = 0
	panel._latest.last_repair_result = {
		"status":0,"item_uid":10000,"item_definition_id":4001,
		"location":1,"current_durability":100,"max_durability":100,
		"cost":37,"wallet_money":63}
	panel.repair_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.repair_panel.repair_item_picker.get_item_text(0).contains("耐久 100/100") or not panel.repair_panel.repair_item_picker.get_item_text(0).contains("費用 0") or not panel.repair_panel.repair_wallet_label.text.contains("63") or not panel.repair_panel.repair_button.disabled:
		fail("Repair UI locally recomputed or failed to apply server settlement")
		return

	# Enhancement UI must display only the Server preview and submit the UID.
	panel._latest.pending_enhancement_sequence = 0
	panel._latest.enhancement_sequence = 0
	panel._latest.active_enhancement_npc = 0
	panel._latest.enhancement_ready = false
	panel._latest.enhancement_revision = 0
	panel._latest.enhancement_wallet_money = 0
	panel._latest.enhancement_items = {}
	panel._latest.last_enhancement_open_status = -1
	panel._latest.last_enhancement_result = {}
	panel.enhancement_panel.apply_snapshot(panel._latest, panel.active)
	if panel.enhancement_panel.enhancement_open_button.get_parent() == null or not panel.enhancement_panel.enhancement_button.disabled:
		fail("Enhancement controls were not attached or were usable before server preview")
		return
	panel.enhancement_panel.enhancement_npc_input.value = 77
	panel.enhancement_panel.enhancement_open_button.pressed.emit()
	if stub.enhancement_opens != 1 or stub.last_enhancement_npc != 77 or not panel.enhancement_panel.enhancement_open_button.disabled:
		fail("Enhancement open UI did not send exactly one NPC intent")
		return
	panel._latest.active_enhancement_npc = 77
	panel._latest.enhancement_ready = true
	panel._latest.enhancement_sequence = 1
	panel._latest.enhancement_revision = 1
	panel._latest.enhancement_wallet_money = 100
	panel._latest.enhancement_items = {10001:{
		"item_uid":10001,"item_definition_id":4001,"location":2,
		"current_level":2,"target_level":3,"success_ppm":725000,
		"money_cost":37,"consume_money_on_failure":true}}
	panel.enhancement_panel.apply_snapshot(panel._latest, panel.active)
	var enhancement_text: String = panel.enhancement_panel.enhancement_item_picker.get_item_text(0)
	if panel.enhancement_panel.enhancement_item_picker.item_count != 1 or not enhancement_text.contains("+2 → +3") or not enhancement_text.contains("72.50%") or not enhancement_text.contains("費用 37") or not enhancement_text.contains("失敗扣款") or not panel.enhancement_panel.enhancement_wallet_label.text.contains("100") or panel.enhancement_panel.enhancement_button.disabled:
		fail("Enhancement UI did not render exact authoritative preview")
		return
	panel.enhancement_panel.enhancement_button.pressed.emit()
	if stub.enhancements != 1 or stub.last_enhancement_uid != 10001:
		fail("Enhancement UI did not send selected UID only")
		return
	panel._latest.enhancement_revision = 2
	panel._latest.enhancement_wallet_money = 63
	panel._latest.enhancement_items.erase(10001)
	panel._latest.last_enhancement_result = {
		"status":0,"item_uid":10001,"item_definition_id":4001,
		"location":2,"previous_level":2,"target_level":3,
		"current_level":3,"succeeded":true,"money_cost":37,
		"money_charged":37,"wallet_money":63}
	panel.enhancement_panel.apply_snapshot(panel._latest, panel.active)
	if panel.enhancement_panel.enhancement_item_picker.item_count != 0 or not panel.enhancement_panel.enhancement_wallet_label.text.contains("63") or not panel.enhancement_panel.enhancement_status.text.contains("+2 → +3") or not panel.enhancement_panel.enhancement_status.text.contains("實扣 37") or not panel.enhancement_panel.enhancement_button.disabled:
		fail("Enhancement UI locally recomputed or failed to apply server settlement")
		return

	# Trade UI consumes authoritative peer/session state and exact ItemUid offers.
	panel._latest.entities = {22:{"actor_id":22}}
	panel._latest.trade_id = 0
	panel._latest.trade_revision = 0
	panel._latest.trade_state = 0
	panel._latest.trade_peer_actor_id = 0
	panel._latest.trade_my_money = 0
	panel._latest.trade_other_money = 0
	panel._latest.trade_my_locked = false
	panel._latest.trade_other_locked = false
	panel._latest.trade_my_confirmed = false
	panel._latest.trade_other_confirmed = false
	panel._latest.trade_my_items = {}
	panel._latest.trade_other_items = {}
	panel._latest.trade_sequence = 0
	panel._latest.last_trade_terminal = {}
	panel._refresh_trade_panel()
	for control in [panel.trade_panel.trade_target_picker,panel.trade_panel.trade_request_button,panel.trade_panel.trade_accept_button,panel.trade_panel.trade_offer_add_button,panel.trade_panel.trade_set_money_button,panel.trade_panel.trade_confirm_button,panel.trade_panel.trade_status]:
		if control.get_parent() == null or not panel.is_ancestor_of(control):
			fail("Trade controls are not descendants of SessionPanel")
			return
	if not panel.trade_panel.trade_target_picker.visible or not panel.trade_panel.trade_request_button.visible or not panel.trade_panel.trade_status.visible:
		fail("Trade invite controls are not visible when idle")
		return
	if panel.trade_panel.trade_target_picker.item_count != 1 or panel.trade_panel.trade_request_button.disabled:
		fail("Trade UI did not expose visible peer invite")
		return
	panel.trade_panel.trade_request_button.pressed.emit()
	if stub.trade_requests != 1 or stub.last_trade_target != 22:
		fail("Trade invite UI did not send selected ActorId")
		return

	panel._latest.trade_id = 7001
	panel._latest.trade_revision = 1
	panel._latest.trade_state = 1
	panel._latest.trade_peer_actor_id = 22
	panel._refresh_trade_panel()
	if not panel.trade_panel.trade_accept_button.visible or panel.trade_panel.trade_accept_button.disabled:
		fail("Trade invite did not expose accept")
		return
	panel.trade_panel.trade_accept_button.pressed.emit()
	if stub.trade_accepts != 1:
		fail("Trade accept UI did not use ClientSession")
		return

	panel._latest.trade_revision = 2
	panel._latest.trade_state = 3
	panel._refresh_trade_panel()
	if panel.trade_panel.trade_inventory_picker.item_count == 0 or panel.trade_panel.trade_offer_add_button.disabled:
		fail("Active trade did not expose offer controls")
		return
	panel.trade_panel.trade_offer_add_button.pressed.emit()
	if stub.trade_adds != 1 or stub.last_trade_uid != 10000:
		fail("Trade item offer did not use exact ItemUid")
		return

	panel._latest.trade_revision = 3
	panel._latest.trade_my_items = {10000:{"item_uid":10000,"item_definition_id":4001,"quantity":3,"drop_id":501,"enhancement_level":5,"durability_raw":80}}
	panel._latest.trade_other_items = {20000:{"item_uid":20000,"item_definition_id":4002,"quantity":1,"drop_id":502,"enhancement_level":7,"durability_raw":60}}
	panel._latest.trade_other_money = 50
	panel._refresh_trade_panel()
	if panel.trade_panel.trade_my_offer_picker.item_count != 1 or not panel.trade_panel.trade_my_offer_picker.get_item_text(0).contains("+5") or not panel.trade_panel.trade_my_offer_picker.get_item_text(0).contains("耐久 2") or not panel.trade_panel.trade_other_offer_label.text.contains("UID 20000") or not panel.trade_panel.trade_other_offer_label.text.contains("+7") or not panel.trade_panel.trade_other_offer_label.text.contains("耐久 2"):
		fail("Trade UI did not render authoritative offers/enhancement/display durability")
		return
	panel.trade_panel.trade_money_input.value = 200
	panel.trade_panel.trade_set_money_button.pressed.emit()
	panel.trade_panel.trade_offer_remove_button.pressed.emit()
	panel.trade_panel.trade_lock_button.pressed.emit()
	if stub.trade_money_sets != 1 or stub.last_trade_money != 200 or stub.trade_removes != 1 or stub.trade_locks != 1:
		fail("Trade mutation controls did not use ClientSession")
		return

	panel._latest.trade_revision = 4
	panel._latest.trade_state = 4
	panel._latest.trade_my_locked = true
	panel._latest.trade_other_locked = true
	panel._latest.trade_my_confirmed = false
	panel._latest.trade_other_confirmed = false
	panel._refresh_trade_panel()
	if not panel.trade_panel.trade_confirm_button.visible or panel.trade_panel.trade_confirm_button.disabled:
		fail("Dual lock did not expose final confirm")
		return
	panel.trade_panel.trade_confirm_button.pressed.emit()
	if stub.trade_confirms != 1:
		fail("Trade final confirm UI did not use ClientSession")
		return

	panel._latest.trade_id = 0
	panel._latest.trade_state = 0
	panel._latest.trade_peer_actor_id = 0
	panel._latest.trade_my_items = {}
	panel._latest.trade_other_items = {}
	panel._latest.last_trade_terminal = {"status":0,"wallet_money":850}
	panel._refresh_trade_panel()
	if not panel.trade_panel.trade_status.text.contains("交易成功") or panel.trade_panel.trade_cancel_button.visible:
		fail("Trade terminal UI did not show server outcome")
		return

	# Player-shop UI must route exact authoritative identifiers through ClientSession.
	# Pin the authenticated self identity explicitly for this synthetic state.
	panel._latest.actor_id = 1
	panel._latest.trade_id = 0
	panel._latest.entities = {22:{"actor_id":22}}
	panel._latest.player_shop_sequence = 0
	panel._latest.pending_player_shop_sequence = 0
	panel._latest.player_shop_own_listing = {}
	panel._latest.player_shop_own_listings = {}
	panel._latest.player_shop_inspected = {}
	panel._latest.player_shop_inspected_listings = {}
	panel._latest.player_shop_wallet = 100
	panel._latest.last_player_shop_result_sequence = 0
	panel._latest.last_player_shop_result_status = -1
	panel._latest.last_player_shop_result = {}
	panel._latest.last_player_shop_sold = {}
	panel._latest.last_player_shop_sold_listing_id = 0
	panel._update_player_shop_ui()
	for control in [panel.player_shop_panel.inventory_picker,panel.player_shop_panel.open_button,panel.player_shop_panel.seller_picker,panel.player_shop_panel.inspect_button,panel.player_shop_panel.status_label]:
		if control.get_parent() == null or not panel.is_ancestor_of(control):
			fail("Player-shop controls are not descendants of SessionPanel")
			return
	if panel.player_shop_panel.inventory_picker.item_count == 0 or panel.player_shop_panel.open_button.disabled or panel.player_shop_panel.seller_picker.item_count != 1 or panel.player_shop_panel.inspect_button.disabled:
		fail("Idle player-shop UI did not expose inventory and AOI seller")
		return
	panel.player_shop_panel.price_input.value = 300
	panel.player_shop_panel.open_button.pressed.emit()
	if stub.player_shop_opens != 1 or stub.last_player_shop_uid != 10000 or stub.last_player_shop_price != 300:
		fail("Player-shop open UI did not send exact UID and price")
		return
	if not (panel._latest.inventory_items as Dictionary).has(10000):
		fail("Player-shop open UI optimistically removed inventory UID")
		return

	panel._latest.player_shop_sequence = 1
	panel._latest.last_player_shop_result_sequence = 1
	panel._latest.last_player_shop_result_status = 0
	panel._latest.player_shop_own_listing = {
		"seller_actor_id":int(panel._latest.actor_id),"listing_id":91,
		"item_uid":10000,"item_definition_id":4001,"quantity":3,
		"source_drop_id":501,"durability_raw":80,"price":300,
		"enhancement_level":5}
	panel._latest.player_shop_own_listings = {
		91:(panel._latest.player_shop_own_listing as Dictionary).duplicate(true)}
	panel._latest.player_shop_wallet = 100
	panel._latest.last_player_shop_result = {"action":1,"status":0}
	panel._update_player_shop_ui()
	if (
		panel.player_shop_panel.close_button.disabled or
		panel.player_shop_panel.own_listing_picker.item_count != 1 or
		int(panel.player_shop_panel.own_listing_picker.get_item_metadata(0)) != 91 or
		not panel.player_shop_panel.own_listing_picker.get_item_text(0).contains("+5") or
		not panel.player_shop_panel.own_listing_label.text.contains("1 / 16")
	):
		fail("Authoritative multi-product own player-shop listing/enhancement was not rendered")
		return
	panel.player_shop_panel.close_button.pressed.emit()
	if stub.player_shop_closes != 1 or stub.last_player_shop_listing != 91:
		fail("Player-shop close UI did not send exact listing ID")
		return

	panel._latest.player_shop_own_listing = {}
	panel._latest.player_shop_own_listings = {}
	panel._latest.player_shop_sequence = 2
	panel._latest.last_player_shop_result_sequence = 2
	panel._latest.last_player_shop_result = {"action":2,"status":0}
	panel._update_player_shop_ui()
	panel.player_shop_panel.inspect_button.pressed.emit()
	if stub.player_shop_inspects != 1 or stub.last_player_shop_seller != 22:
		fail("Player-shop inspect UI did not send selected AOI ActorId")
		return

	panel._latest.player_shop_sequence = 3
	panel._latest.last_player_shop_result_sequence = 3
	panel._latest.player_shop_inspected = {
		"seller_actor_id":22,"listing_id":92,"item_uid":20000,
		"item_definition_id":4002,"quantity":1,"source_drop_id":502,
		"durability_raw":70,"price":300,"enhancement_level":7}
	panel._latest.player_shop_inspected_listings = {
		92:(panel._latest.player_shop_inspected as Dictionary).duplicate(true)}
	panel._latest.player_shop_wallet = 700
	panel._latest.last_player_shop_result = {"action":3,"status":0}
	panel._update_player_shop_ui()
	if (
		panel.player_shop_panel.buy_button.disabled or
		panel.player_shop_panel.inspected_listing_picker.item_count != 1 or
		int(panel.player_shop_panel.inspected_listing_picker.get_item_metadata(0)) != 92 or
		not panel.player_shop_panel.inspected_listing_picker.get_item_text(0).contains("+7") or
		not panel.player_shop_panel.inspected_listing_label.text.contains("1 件")
	):
		fail("Authoritative inspected multi-product player-shop listing/enhancement was not rendered")
		return
	panel.player_shop_panel.buy_button.pressed.emit()
	if stub.player_shop_buys != 1 or stub.last_player_shop_seller != 22 or stub.last_player_shop_listing != 92:
		fail("Player-shop buy UI did not send exact seller/listing IDs")
		return

	panel._latest.player_shop_inspected = {}
	panel._latest.player_shop_inspected_listings = {}
	panel._latest.player_shop_own_listing = {}
	panel._latest.player_shop_own_listings = {}
	panel._latest.inventory_items.erase(10000)
	panel._latest.player_shop_wallet = 400
	panel._latest.last_player_shop_sold_listing_id = 91
	panel._latest.last_player_shop_sold = {"buyer_actor_id":22,"item_uid":10000,"seller_wallet":400}
	panel._update_player_shop_ui()
	if (
		not panel.player_shop_panel.status_label.text.contains("已由伺服器售出") or
		not panel.player_shop_panel.own_listing_label.text.contains("0 / 16") or
		int(panel._latest.player_shop_wallet) != 400
	):
		fail("Player-shop sold push did not converge multi-product UI to authoritative wallet/listing")
		return
	# Nearby chat UI never locally echoes; only authoritative Server delivery
	# populates the visible history.
	panel._latest.state = State.STATE_IN_WORLD
	panel._latest.world_ready = true
	panel._latest.map_epoch = 3
	panel._latest.chat_sequence = 0
	panel._latest.chat_messages = []
	panel._latest.stale_chat_deliveries = 0
	panel._latest.pending_whisper_sequence = 0
	panel._latest.last_whisper_result_sequence = 0
	panel._latest.last_whisper_result_status = -1
	panel._latest.last_whisper_result = {}
	panel._latest.stale_whisper_results = 0
	panel._latest.party_sequence = 0
	panel._latest.pending_party_sequence = 0
	panel._latest.pending_party_action = 0
	panel._latest.pending_party_party_id = 0
	panel._latest.pending_party_target_actor_id = 0
	panel._latest.party_id = 0
	panel._latest.party_revision = 0
	panel._latest.party_leader_actor_id = 0
	panel._latest.party_members = {}
	panel._latest.pending_party_invite = {}
	panel._latest.last_party_result_sequence = 0
	panel._latest.last_party_result_status = -1
	panel._latest.last_party_result = {}
	panel._latest.stale_party_frames = 0
	panel._latest.reconnecting = false
	panel._latest.resync_pending = false
	panel._latest.waiting_for_new_map = false
	panel._latest.pending_world_ready_ack_epoch = 0
	panel._latest.last_world_ready_ack_epoch = panel._latest.map_epoch
	panel._update_nearby_chat_ui()
	for control in [panel.chat_panel.history,panel.chat_panel.nearby_input,panel.chat_panel.nearby_send_button,panel.chat_panel.nearby_status,panel.chat_panel.whisper_target_input,panel.chat_panel.whisper_input,panel.chat_panel.whisper_send_button,panel.chat_panel.whisper_status,panel.party_panel.target_input,panel.party_panel.invite_button,panel.party_panel.invite_label,panel.party_panel.accept_button,panel.party_panel.reject_button,panel.party_panel.members_label,panel.party_panel.leave_button,panel.party_panel.kick_button,panel.party_panel.promote_button,panel.party_panel.chat_input,panel.party_panel.chat_send_button,panel.party_panel.status_label]:
		if control.get_parent() == null or not panel.is_ancestor_of(control):
			fail("Chat/whisper/party controls are not descendants of SessionPanel")
			return
	if panel.chat_panel.nearby_send_button.disabled or not panel.chat_panel.history.text.is_empty():
		fail("Nearby chat idle controls/history incorrect")
		return
	panel.chat_panel.nearby_input.text = "  附近測試  "
	panel.chat_panel.nearby_send_button.pressed.emit()
	if stub.nearby_chats != 1 or stub.last_nearby_message != "附近測試" or stub.last_nearby_bubble != 0:
		fail("Nearby chat UI did not route sanitized message through ClientSession")
		return
	if not panel.chat_panel.nearby_input.text.is_empty() or not panel.chat_panel.history.text.is_empty():
		fail("Nearby chat UI locally echoed before Server delivery")
		return
	panel._latest.chat_messages = [{
		"receiver_map_epoch":int(panel._latest.map_epoch),
		"sender_actor_id":22,"sender_connection_epoch":8,"chat_sequence":1,
		"channel":0,"bubble_type":0,"sender_name":"NearbyHero",
		"message":"附近測試"}]
	panel._update_nearby_chat_ui()
	if not panel.chat_panel.history.text.contains("[附近] NearbyHero：附近測試"):
		fail("Nearby chat UI did not render authoritative Server delivery")
		return
	panel.chat_panel.whisper_target_input.text = "22"
	panel.chat_panel.whisper_input.text = "  私訊測試  "
	panel.chat_panel.whisper_send_button.pressed.emit()
	if (
		stub.whispers!=1 or stub.last_whisper_target!=22 or
		stub.last_whisper_message!="私訊測試" or stub.last_whisper_bubble!=0
	):
		fail("Whisper UI did not route sanitized target/message through ClientSession")
		return
	if not panel.chat_panel.whisper_input.text.is_empty() or panel.chat_panel.history.text.contains("私訊測試"):
		fail("Whisper UI locally echoed before Server result")
		return
	panel.chat_panel.whisper_target_input.text = "WhisperByName"
	panel.chat_panel.whisper_input.text = "  名稱私訊測試  "
	panel.chat_panel.whisper_send_button.disabled = false
	panel.chat_panel.whisper_send_button.pressed.emit()
	if (
		stub.whisper_name_targets!=1 or
		stub.last_whisper_target_name!="WhisperByName" or
		stub.last_whisper_message!="名稱私訊測試"
	):
		fail("Whisper UI did not route character name through ClientSession")
		return
	panel._latest.chat_messages.append({
		"receiver_map_epoch":3,"sender_actor_id":int(panel._latest.actor_id),
		"sender_connection_epoch":0,"chat_sequence":2,"channel":4,
		"bubble_type":0,"sender_name":"","message":"私訊測試",
		"direction":"outgoing","target_actor_id":22,
		"target_connection_epoch":8,"target_name":"WhisperTarget"})
	panel._latest.chat_messages.append({
		"receiver_map_epoch":3,"sender_actor_id":22,
		"sender_connection_epoch":8,"chat_sequence":3,"channel":4,
		"bubble_type":2,"sender_name":"WhisperTarget","message":"收到",
		"direction":"incoming"})
	panel._latest.last_whisper_result_sequence=2
	panel._latest.last_whisper_result_status=0
	panel._latest.last_whisper_result={
		"map_epoch":3,"chat_sequence":2,"status":0,
		"target_actor_id":22,"target_connection_epoch":8,
		"target_name":"WhisperTarget"}
	panel._update_nearby_chat_ui()
	if (
		not panel.chat_panel.history.text.contains("[私訊] 你 → WhisperTarget：私訊測試") or
		not panel.chat_panel.history.text.contains("[私訊] WhisperTarget → 你：收到") or
		not panel.chat_panel.whisper_status.text.contains("已送達 WhisperTarget")
	):
		fail("Whisper UI did not render authoritative result/delivery")
		return
	panel._latest.pending_whisper_sequence=4
	panel._update_nearby_chat_ui()
	if not panel.chat_panel.whisper_send_button.disabled or not panel.chat_panel.nearby_send_button.disabled:
		fail("Pending whisper did not serialize chat send controls")
		return
	panel._latest.pending_whisper_sequence=0

	# Party controls send only intents; membership and leader state render from
	# authoritative PartySnapshot/PartyResult fields.
	panel.party_panel.target_input.text="22"
	panel.party_panel.invite_button.pressed.emit()
	if stub.party_invites!=1 or stub.last_party_target!=22:
		fail("Party invite UI did not send exact target ActorId")
		return
	panel.party_panel.target_input.text="PartyByName"
	panel.party_panel.invite_button.pressed.emit()
	if stub.party_name_invites!=1 or stub.last_party_target_name!="PartyByName":
		fail("Party invite UI did not route character name")
		return
	panel._latest.pending_party_invite={
		"receiver_map_epoch":3,"party_id":9001,"revision":1,
		"inviter_actor_id":22,"inviter_connection_epoch":8,
		"inviter_name":"PartyLeader"}
	panel._update_nearby_chat_ui()
	if (
		not panel.party_panel.invite_label.text.contains("PartyLeader") or
		panel.party_panel.accept_button.disabled or panel.party_panel.reject_button.disabled
	):
		fail("Party invite UI did not render authoritative invite")
		return
	panel.party_panel.accept_button.pressed.emit()
	if stub.party_accepts!=1:
		fail("Party accept UI did not use ClientSession")
		return

	var self_actor:=int(panel._latest.actor_id)
	panel._latest.pending_party_invite={}
	panel._latest.party_id=9001
	panel._latest.party_revision=2
	panel._latest.party_leader_actor_id=self_actor
	panel._latest.party_members={
		self_actor:{"actor_id":self_actor,"connection_epoch":1,"name":"SelfHero"},
		22:{"actor_id":22,"connection_epoch":8,"name":"PartyMate"}}
	panel._latest.last_party_result_sequence=1
	panel._latest.last_party_result_status=0
	panel._latest.last_party_result={
		"map_epoch":3,"party_sequence":1,"action":2,"status":0,
		"party_id":9001,"target_actor_id":22}
	panel._latest.chat_messages.append({
		"receiver_map_epoch":3,"sender_actor_id":22,
		"sender_connection_epoch":8,"chat_sequence":4,"channel":3,
		"bubble_type":0,"sender_name":"PartyMate","message":"隊伍測試"})
	panel._update_nearby_chat_ui()
	if (
		not panel.party_panel.members_label.text.contains("PartyMate") or
		not panel.party_panel.members_label.text.contains("★ SelfHero") or
		not panel.chat_panel.history.text.contains("[隊伍] PartyMate：隊伍測試") or
		panel.party_panel.leave_button.disabled or panel.party_panel.kick_button.disabled or
		panel.party_panel.promote_button.disabled or panel.party_panel.chat_send_button.disabled
	):
		fail("Party UI did not render authoritative membership/leader/chat")
		return
	panel.party_panel.chat_input.text="  隊伍送出測試  "
	panel.party_panel.chat_send_button.pressed.emit()
	if stub.party_chats!=1 or stub.last_party_message!="隊伍送出測試":
		fail("Party chat UI did not route sanitized message")
		return
	panel.party_panel.target_input.text="22"
	panel.party_panel.kick_button.pressed.emit()
	panel.party_panel.promote_button.pressed.emit()
	if stub.party_kicks!=1 or stub.party_promotes!=1 or stub.last_party_target!=22:
		fail("Party leader controls did not route target actions")
		return
	panel.party_panel.leave_button.pressed.emit()
	if stub.party_leaves!=1:
		fail("Party leave UI did not use ClientSession")
		return
	panel._latest.pending_party_sequence=2
	panel._update_nearby_chat_ui()
	if (
		not panel.party_panel.invite_button.disabled or
		not panel.party_panel.leave_button.disabled or
		not panel.party_panel.kick_button.disabled
	):
		fail("Pending Party command did not serialize membership controls")
		return
	panel._latest.pending_party_sequence=0
	# Quest UI displays and submits only the authoritative Server View.
	panel._latest.quest_sequence = 0
	panel._latest.pending_quest_sequence = 0
	panel._latest.pending_quest_kind = 0
	panel._latest.pending_quest_npc = 0
	panel._latest.pending_quest_id = 0
	panel._latest.pending_quest_choice_id = 0
	panel._latest.quest_view = {}
	panel._latest.last_quest_response_sequence = 0
	panel._latest.last_quest_status = -1
	panel._latest.last_quest_result = {}
	panel._latest.stale_quest_results = 0
	panel._latest.player_dead = false
	panel._update_quest_ui()
	for control in [panel.quest_panel.npc_input,panel.quest_panel.open_button,panel.quest_panel.title_label,panel.quest_panel.text_display,panel.quest_panel.progress_label,panel.quest_panel.choice_picker,panel.quest_panel.choose_button,panel.quest_panel.status_label]:
		if control.get_parent() == null or not panel.is_ancestor_of(control):
			fail("Quest controls are not descendants of SessionPanel")
			return
	if panel.quest_panel.open_button.disabled or not panel.quest_panel.choose_button.disabled:
		fail("Quest idle controls did not expose only Server-validated open")
		return
	panel.quest_panel.npc_input.value = 88
	panel.quest_panel.open_button.pressed.emit()
	if stub.quest_opens != 1 or stub.last_quest_npc != 88:
		fail("Quest open UI did not send exact NPC SpawnId")
		return

	panel._latest.quest_sequence = 1
	panel._latest.pending_quest_sequence = 0
	panel._latest.last_quest_response_sequence = 1
	panel._latest.last_quest_status = 0
	panel._latest.quest_view = {
		"map_epoch":int(panel._latest.map_epoch),"quest_sequence":1,
		"status":0,"npc_spawn_id":88,"quest_id":91001,"rule_revision":1,
		"state":0,"stage":0,"progress_revision":0,
		"title":"Verified Quest Fixture","text":"這是一個合成的 verified Quest 測試。",
		"choices":[
			{"choice_id":1,"label":"接受"},
			{"choice_id":3,"label":"另一個合法起始選項"}]}
	panel._latest.last_quest_result = panel._latest.quest_view.duplicate(true)
	panel._update_quest_ui()
	if (
		not panel.quest_panel.title_label.text.contains("Verified Quest Fixture") or
		not panel.quest_panel.text_display.text.contains("verified Quest") or
		not panel.quest_panel.progress_label.text.contains("未開始") or
		panel.quest_panel.choice_picker.item_count != 2 or
		panel.quest_panel.choose_button.disabled
	):
		fail("Quest UI did not render authoritative initial View")
		return
	var stage_before:=int((panel._latest.quest_view as Dictionary).stage)
	var revision_before:=int((panel._latest.quest_view as Dictionary).progress_revision)
	panel.quest_panel.choice_picker.select(0)
	panel.quest_panel.choose_button.pressed.emit()
	if stub.quest_choices != 1 or stub.last_quest_choice != 1:
		fail("Quest choice UI did not send exact Server ChoiceId")
		return
	if (
		int((panel._latest.quest_view as Dictionary).stage)!=stage_before or
		int((panel._latest.quest_view as Dictionary).progress_revision)!=revision_before
	):
		fail("Quest UI optimistically advanced progress before Server View")
		return

	panel._latest.quest_sequence = 2
	panel._latest.last_quest_response_sequence = 2
	panel._latest.last_quest_status = 1
	panel._latest.quest_view = {
		"map_epoch":int(panel._latest.map_epoch),"quest_sequence":2,
		"status":1,"npc_spawn_id":88,"quest_id":91001,"rule_revision":1,
		"state":1,"stage":1,"progress_revision":1,
		"title":"Verified Quest Fixture","text":"是否完成這個已驗證步驟？",
		"choices":[{"choice_id":2,"label":"完成"}]}
	panel._latest.last_quest_result = panel._latest.quest_view.duplicate(true)
	panel._update_quest_ui()
	if (
		not panel.quest_panel.progress_label.text.contains("進行中") or
		not panel.quest_panel.progress_label.text.contains("階段 1") or
		panel.quest_panel.choice_picker.item_count != 1 or
		int(panel.quest_panel.choice_picker.get_item_metadata(0)) != 2
	):
		fail("Quest UI did not converge to authoritative active stage")
		return

	panel._latest.quest_sequence = 3
	panel._latest.last_quest_response_sequence = 3
	panel._latest.quest_view = {
		"map_epoch":int(panel._latest.map_epoch),"quest_sequence":3,
		"status":1,"npc_spawn_id":88,"quest_id":91001,"rule_revision":1,
		"state":2,"stage":2,"progress_revision":2,
		"title":"Verified Quest Fixture","text":"任務已完成。","choices":[]}
	panel._latest.last_quest_result = panel._latest.quest_view.duplicate(true)
	panel._update_quest_ui()
	if (
		not panel.quest_panel.progress_label.text.contains("已完成") or
		not panel.quest_panel.text_display.text.contains("任務已完成") or
		panel.quest_panel.choice_picker.item_count != 0 or
		not panel.quest_panel.choose_button.disabled
	):
		fail("Completed Quest View still exposed a Client-side choice")
		return

	# PvP mode UI reads only authoritative server results. Query is
	# allowed first; white/blue mutations stay locked until a server reply.
	panel._latest.pvp_known = false
	panel._latest.pvp_sequence = 0
	panel._latest.pending_pvp_sequence = 0
	panel._latest.pvp_mode = -1
	panel._latest.pvp_map_allows = false
	panel._latest.last_pvp_result = {}
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	if panel.pvp_panel.pvp_query_button.get_parent() == null or not panel.pvp_panel.pvp_query_button.visible or panel.pvp_panel.pvp_query_button.disabled or not panel.pvp_panel.pvp_white_button.disabled or not panel.pvp_panel.pvp_blue_button.disabled:
		fail("PvP query and locked mutation controls missing from SessionPanel")
		return
	panel.pvp_panel.pvp_query_button.pressed.emit()
	if stub.pvp_requests != 1 or stub.last_pvp_action != 0:
		fail("PvP query did not use ClientSession")
		return
	panel._latest.pending_pvp_sequence = 1
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.pvp_panel.pvp_query_button.disabled or not panel.pvp_panel.pvp_white_button.disabled or not panel.pvp_panel.pvp_blue_button.disabled:
		fail("Pending authoritative PvP response re-enabled controls")
		return
	panel._latest.pending_pvp_sequence = 0
	panel._latest.pvp_known = true
	panel._latest.pvp_mode = 0
	panel._latest.pvp_map_allows = false
	panel._latest.last_pvp_result = {"status":0}
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.pvp_panel.pvp_status.text.contains("藍名") or not panel.pvp_panel.pvp_white_button.disabled:
		fail("NonPK map allowed white PvP mode UI")
		return
	panel._latest.pvp_map_allows = true
	panel._latest.combat_available = true
	panel._latest.entities = {77:{"actor_id":77}}
	panel._latest.pending_pvp_attack_sequence = 0
	panel._latest.last_pvp_attack_result = {}
	panel._latest.gm_authorized = false
	panel._latest.gm_sequence = 0
	panel._latest.pending_gm_sequence = 0
	panel._latest.pending_gm_target_actor_id = 0
	panel._latest.pending_gm_name_state = -1
	panel._latest.last_gm_result = {}
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	if panel.pvp_panel.gm_pvp_apply_button.visible or panel.pvp_panel.gm_pvp_state_picker.visible:
		fail("Ordinary player UI inferred GM capability without Server frame")
		return
	if panel.pvp_panel.pvp_white_button.disabled:
		fail("Verified PvP map did not expose white mode")
		return
	if panel.pvp_panel.pvp_target_picker.get_parent() == null or panel.pvp_panel.pvp_target_picker.item_count != 1 or panel.pvp_panel.pvp_attack_button.disabled:
		fail("PvP attack target controls did not bind visible AOI player")
		return
	panel.pvp_panel.pvp_attack_button.pressed.emit()
	if stub.pvp_attacks != 1 or stub.last_pvp_target != 77 or not panel.pvp_panel.pvp_attack_button.disabled:
		fail("PvP attack button did not send exactly one visible target")
		return
	panel._latest.pending_pvp_attack_sequence = 1
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.pvp_panel.pvp_attack_button.disabled or stub.pvp_attacks != 1:
		fail("Pending PvP attack re-enabled duplicate submit")
		return
	panel._latest.pending_pvp_attack_sequence = 0
	panel._latest.last_pvp_attack_result = {"status":10,"target_actor_id":77,"damage":0,"died":false}
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.pvp_panel.pvp_attack_status.text.contains("距離過遠") or panel.pvp_panel.pvp_attack_button.disabled:
		fail("PvP attack did not display authoritative rejected result")
		return
	# A PvP skill reads the current Skill UI selection through the existing
	# SessionPanel guard, rather than bypassing it with a second ClientSession.
	panel._latest.pending_skill_sequence = 0
	panel.skill_id_input.value = 99001
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	if panel.pvp_panel.pvp_skill_button.disabled:
		fail("Verified AOI PvP skill could not be selected")
		return
	panel.pvp_panel.pvp_skill_button.pressed.emit()
	if stub.pvp_skill_attacks != 1 or stub.last_pvp_skill_id != 99001 or stub.last_pvp_skill_target != 77:
		fail("PvP skill panel bypassed the selected Skill ID or authoritative Session intent")
		return
	panel._latest.pending_skill_sequence = 1
	panel._latest.pending_skill_target_kind = 1
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.pvp_panel.pvp_skill_button.disabled or stub.pvp_skill_attacks != 1:
		fail("Pending PvP skill re-enabled a duplicate submit")
		return
	panel._latest.pending_skill_sequence = 0
	panel._latest.pending_skill_target_kind = 0
	panel._latest.last_skill_result = {"target_kind":"player","damage":7,"died":false}
	panel._latest.last_skill_result_status = 0
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.pvp_panel.pvp_skill_status.text.contains("傷害 7"):
		fail("PvP skill did not display the authoritative damage result")
		return

	print("CLIENT_AES_UI_CHECKPOINT gm_begin")
	panel._latest.gm_authorized = true
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	print("CLIENT_AES_UI_CHECKPOINT gm_visible_updated")
	if (
		not panel.pvp_panel.gm_pvp_apply_button.visible or panel.pvp_panel.gm_pvp_apply_button.disabled or
		not panel.pvp_panel.gm_pvp_state_picker.visible
	):
		fail("Server-issued GM capability did not expose bounded PvP-name controls")
		return
	panel.pvp_panel.gm_pvp_state_picker.select(2)
	print("CLIENT_AES_UI_CHECKPOINT gm_before_emit")
	panel.pvp_panel.gm_pvp_apply_button.pressed.emit()
	print("CLIENT_AES_UI_CHECKPOINT gm_after_emit")
	if stub.gm_commands!=1 or stub.last_gm_target!=77 or stub.last_gm_state!=2:
		fail("GM PvP-name UI did not send exact visible target/state")
		return
	panel._latest.pending_gm_sequence=1
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.pvp_panel.gm_pvp_apply_button.disabled:
		fail("Pending GM audit command re-enabled duplicate submit")
		return
	panel._latest.pending_gm_sequence=0
	panel._latest.last_gm_result={"status":0,"target_actor_id":77,"name_state":2}
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.pvp_panel.gm_pvp_status.text.contains("已套用"):
		fail("GM authoritative result not displayed")
		return
	panel._latest.gm_authorized=false
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	print("CLIENT_AES_UI_CHECKPOINT gm_revoked")
	if panel.pvp_panel.gm_pvp_apply_button.visible or panel.pvp_panel.gm_pvp_state_picker.visible:
		fail("Revoked GM capability left privileged controls visible")
		return

	panel.pvp_panel.pvp_white_button.pressed.emit()
	if stub.pvp_requests != 2 or stub.last_pvp_action != 2:
		fail("White PvP command did not use ClientSession")
		return
	panel._latest.player_dead = true
	panel.pvp_panel.apply_snapshot(panel._latest, panel.active)
	if not panel.pvp_panel.pvp_white_button.disabled or not panel.pvp_panel.pvp_blue_button.disabled or not panel.pvp_panel.pvp_attack_button.disabled:
		fail("Dead player could mutate PvP mode or attack")
		return
	panel._refresh_inventory_panel()
	if not panel.inventory_panel.recovery_button.disabled or panel.inventory_panel.submit_recovery_use(10000):
		fail("Dead character could submit recovery use")
		return
	panel._latest.player_dead = false
	panel._latest.inventory_ready = false
	panel._refresh_inventory_panel()
	if panel.inventory_panel.inventory_preview.visible or not panel.inventory_panel.inventory_button.disabled:
		fail("Incomplete inventory leaked prior character item list")
		return
	panel.active = false
	panel._set_respawn_controls()
	app.queue_free()
	await process_frame
	print("CLIENT_AES_LOGIN_PASS ui=PASS auth=PASS choose=PASS receipt=PASS world_ready=PASS password_cleared=PASS logout=PASS respawn_ui=PASS click_attack_ui=PASS click_pickup_ui=PASS persistent_inventory_ui=PASS item_metadata=PASS inventory_pagination=PASS recovery_item_button=PASS inventory_discard_button=PASS inventory_discard_quantity_confirm=PASS shop_ui=PASS storage_ui=PASS equipment_ui=PASS bank_ui=PASS repair_ui=PASS trade_ui=PASS trade_ui_parenting=PASS player_shop_ui=PASS nearby_chat_ui=PASS quest_ui=PASS pvp_ui=PASS pvp_attack_ui=PASS gm_pvp_ui=PASS pvp_tls_auto_query=PASS skill_cast_ui=PASS skill_singleflight=PASS combat_mp_ui=PASS")
	quit(0)
