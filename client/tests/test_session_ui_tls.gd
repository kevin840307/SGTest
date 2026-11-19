extends SceneTree
## Exercises the actual entry's UI callbacks; no private textures required.
const Main = preload("res://app/main.gd")
const State = preload("res://network/world_state.gd")
const Session = preload("res://network/client_session.gd")
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
	func create_character(slot: int, character_name: String) -> bool:
		if slot < 0 or character_name.is_empty():
			return false
		creates += 1
		last_slot = slot
		last_name = character_name
		return true
	func stop() -> void:
		pass

class FailedSession extends RefCounted:
	func connect_secure(_host, _port, _user, _password, _name, _trust):
		return OK
	func snapshot():
		return {"state":State.STATE_FAILED}
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
	if args.size() < 2 or args.size() > 3:
		fail("Expected port and certificate")
		return
	app = Main.new()
	app.auto_start = false
	root.add_child(app)
	var panel = app.session_panel
	panel.show()
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
	var cert := X509Certificate.new()
	if cert.load(args[1]) != OK:
		fail("Certificate load")
		return
	panel.trusted_chain = cert
	panel.tls_name = "localhost"
	panel.host_input.text = "127.0.0.1"
	panel.port_input.value = int(args[0])
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
	if args.size() == 3:
		await RenderingServer.frame_post_draw
		if root.get_texture().get_image().save_png(args[2]) != OK:
			fail("UI screenshot save")
			return
	panel.characters.select(0)
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
	if not bool(entered.get("inventory_ready",false)) or not (entered.get("inventory_items",{}) as Dictionary).is_empty() or not panel.inventory_button.visible:
		fail("Real TLS login did not supply complete empty durable inventory baseline")
		return
	panel.inventory_button.pressed.emit()
	if not panel.inventory_preview.visible or not panel.inventory_preview.text.contains("沒有物品"):
		fail("Inventory UI failed to render authoritative zero-item baseline")
		return
	panel.inventory_button.pressed.emit()
	if panel.inventory_preview.visible:
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
	if panel.inventory_button.visible or panel.inventory_preview.visible:
		fail("Logout left previous character inventory visible")
		return
	if panel.active or panel.login_button.disabled or app._map_buttons[0].disabled or not panel.characters.item_count == 0:
		fail("Logout left stale UI or binding")
		return
	# Character creation controls must expose exactly the authoritative Session
	# intent while selecting; the real TLS creation/store lifecycle is covered
	# by the C# authenticated E2E.
	var create_stub := CharacterCreateSession.new()
	panel.session = create_stub
	panel.active = true
	panel._latest = {"state":State.STATE_SELECTING_CHARACTER,
		"characters":[],"character_create_status":-1}
	panel._set_controls()
	if not panel.create_character_button.visible or panel.create_character_button.disabled:
		fail("Character creation controls unavailable while selecting")
		return
	panel.character_slot_input.value = 2
	panel.character_name_input.text = "UiCreatedHero"
	panel.create_character_button.pressed.emit()
	if create_stub.creates != 1 or create_stub.last_slot != 2 or 		create_stub.last_name != "UiCreatedHero":
		fail("Character creation UI did not send exact slot/name once")
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
	panel._update_inventory_ui()
	if not panel.inventory_preview.text.contains("2701 ×2") or not panel.inventory_button.text.contains("1 件"):
		fail("Durable inventory items not reflected in UI")
		return
	panel._latest = {"state":State.STATE_IN_WORLD,"world_ready":true,
		"inventory_ready":true,"combat_available":true,
		"actor_id":22,"reconnect_count":2,
		"inventory_revision":1,"inventory_items":{7002:{
			"item_uid":7002,"item_definition_id":3701,
			"quantity":3,"drop_id":502}}}
	panel._update_inventory_ui()
	if panel.inventory_preview.text.contains("2701") or not panel.inventory_preview.text.contains("3701 ×3"):
		fail("Same inventory revision across new character retained stale UI")
		return
	panel.inventory_button.pressed.emit()
	if not panel.inventory_preview.visible:
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
			"quantity":3 if index == 0 else 1,"drop_id":50000+index}
	panel._latest.inventory_items = many
	panel._latest.inventory_revision = 2
	panel._update_inventory_ui()
	if not panel.inventory_preview.text.contains("測試素材原名") or not panel.inventory_page_label.text.contains("1 / 2") or panel.inventory_preview.text.contains("UID 10080"):
		fail("Inventory page one did not use source-derived name and 80-row cap")
		return
	panel.inventory_next.pressed.emit()
	if not panel.inventory_preview.text.contains("UID 10080") or panel.inventory_preview.text.contains("UID 10000") or not panel.inventory_page_label.text.contains("2 / 2"):
		fail("Inventory next page did not display remaining five items")
		return
	panel.inventory_search.text = "2701"
	panel.inventory_search.text_changed.emit("2701")
	if not panel.inventory_preview.text.contains("測試素材原名") or panel.inventory_preview.text.contains("UID 10080") or not panel.inventory_page_label.text.contains("1 / 1"):
		fail("Inventory search failed to filter on authoritative item definition")
		return
	if panel.recovery_picker.item_count != 1 or panel.recovery_button.disabled:
		fail("Inventory recovery UID selector did not resolve visible page")
		return
	panel.recovery_button.pressed.emit()
	if stub.uses != 1 or stub.last_use_uid != 10000 or not panel.recovery_button.disabled:
		fail("Recovery UI did not send one chosen UID or left double-click enabled")
		return
	panel._latest.pending_recovery_sequence = 1
	panel._update_inventory_ui()
	if not panel.recovery_button.disabled:
		fail("Pending authoritative recovery result allowed another click")
		return
	panel._latest.pending_recovery_sequence = 0
	panel._latest.last_recovery_result = {
		"status":0,"current_hp":90,"max_hp":100,"remaining_quantity":1}
	panel._latest.inventory_revision = 3
	panel._update_inventory_ui()
	if not panel.recovery_status.text.contains("90 / 100"):
		fail("Recovery status did not display authoritative committed HP")
		return
	if panel.discard_button.disabled:
		fail("Loaded inventory did not expose server-authoritative discard")
		return
	if int(panel.discard_quantity.max_value) != 3 or int(panel.discard_quantity.value) != 1:
		fail("Discard quantity did not bind to selected authoritative stack")
		return
	panel.discard_all_button.pressed.emit()
	if int(panel.discard_quantity.value) != 3:
		fail("Discard-all did not select full stack")
		return
	panel.discard_button.pressed.emit()
	if stub.discards != 0 or not panel.discard_confirm.dialog_text.contains("UID 10000"):
		fail("Destructive discard bypassed confirmation")
		return
	panel.discard_confirm.canceled.emit()
	if not panel._pending_discard_confirmation.is_empty() or stub.discards != 0:
		fail("Cancelled permanent discard left a stale confirmation lock")
		return
	panel.discard_button.pressed.emit()
	panel._latest.actor_id = 99
	panel.discard_confirm.confirmed.emit()
	if stub.discards != 0:
		fail("Confirmed discard crossed character identity")
		return
	panel._latest.actor_id = 22
	panel.discard_button.pressed.emit()
	panel.discard_confirm.confirmed.emit()
	if stub.discards != 1 or stub.last_discard_uid != 10000 or stub.last_discard_quantity != 3 or not panel.discard_button.disabled:
		fail("Confirmed full-stack discard did not send exact UID and quantity once")
		return
	panel._latest.pending_discard_sequence = 1
	panel._update_inventory_ui()
	if not panel.discard_button.disabled or not panel.recovery_button.disabled:
		fail("Pending discard allowed another inventory mutation")
		return
	panel._latest.pending_discard_sequence = 0
	panel._latest.last_discard_result = {
		"status":0,"discarded_quantity":3,"remaining_quantity":0}
	panel._latest.inventory_revision = 4
	panel._update_inventory_ui()
	if not panel.discard_status.text.contains("已移除 3"):
		fail("Discard status did not display authoritative committed quantity")
		return

	# Shop UI is only an adapter to server-owned catalog/money/inventory.
	panel._latest = {"state":State.STATE_IN_WORLD,"world_ready":true,
		"inventory_ready":true,"player_dead":false,"reconnecting":false,
		"resync_pending":false,"active_shop_npc":0,"pending_shop_sequence":0,
		"shop_sequence":0,"shop_money":0,"shop_items":{},
		"inventory_revision":4,"inventory_items":many}
	panel._update_shop_ui()
	panel.shop_npc_input.value = 99
	panel.shop_open_button.pressed.emit()
	if stub.shop_opens != 1 or stub.last_shop_npc != 99 or not panel.shop_open_button.disabled:
		fail("Shop open UI did not send exactly one NPC intent")
		return
	panel._latest.active_shop_npc = 99
	panel._latest.shop_sequence = 1
	panel._latest.shop_money = 25
	panel._latest.shop_items = {2701:{
		"item_definition_id":2701,"buy_price":10,"sell_price":5}}
	panel._latest.pending_shop_sequence = 0
	panel._update_shop_ui()
	if not panel.shop_money_label.text.contains("25") or panel.shop_item_picker.item_count != 1 or panel.shop_sell_picker.item_count != 1 or panel.shop_buy_button.disabled or panel.shop_sell_button.disabled:
		fail("Server shop snapshot was not rendered as playable controls")
		return
	# Switching sell UID or refreshing owned stack must never alter buy quantity.
	panel.shop_quantity.value = 7
	panel.shop_sell_quantity.value = 3
	panel._update_shop_sell_quantity()
	if int(panel.shop_quantity.value) != 7 or int(panel.shop_sell_quantity.max_value) != 3:
		fail("Sell stack selection modified independent buy quantity or sell limit")
		return
	panel.shop_quantity.value = 2
	panel.shop_buy_button.pressed.emit()
	if stub.shop_buys != 1 or stub.last_shop_item != 2701 or stub.last_shop_quantity != 2 or not panel.shop_buy_button.disabled:
		fail("Shop buy UI did not send exact authoritative item/quantity once")
		return
	panel._latest.pending_shop_sequence = 2
	panel._update_shop_ui()
	if not panel.shop_buy_button.disabled or not panel.shop_sell_button.disabled:
		fail("Pending shop transaction allowed another economy mutation")
		return
	panel._latest.pending_shop_sequence = 0
	panel._latest.shop_sequence = 2
	panel._latest.inventory_revision = 5
	panel._update_shop_ui()
	panel.shop_quantity.value = 9
	panel.shop_sell_quantity.value = 1
	panel.shop_sell_button.pressed.emit()
	if stub.shop_sells != 1 or stub.last_shop_uid != 10000 or stub.last_shop_quantity != 1 or int(panel.shop_quantity.value) != 9:
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
	panel._update_storage_ui()
	panel.storage_npc_input.value = 88
	panel.storage_open_button.pressed.emit()
	if stub.storage_opens != 1 or stub.last_storage_npc != 88 or not panel.storage_open_button.disabled:
		fail("Storage open UI did not send exactly one NPC intent")
		return
	panel._latest.active_storage_npc = 88
	panel._latest.storage_ready = true
	panel._latest.storage_sequence = 1
	panel._latest.storage_revision = 1
	panel._latest.storage_items = {20000:{
		"item_uid":20000,"item_definition_id":4001,"quantity":2,"drop_id":0}}
	panel._update_storage_ui()
	if panel.storage_deposit_picker.item_count == 0 or panel.storage_withdraw_picker.item_count != 1 or panel.storage_deposit_button.disabled or panel.storage_withdraw_button.disabled:
		fail("Authoritative storage baseline was not rendered as playable controls")
		return
	panel.storage_deposit_button.pressed.emit()
	if stub.storage_deposits != 1 or stub.last_storage_uid != 10000:
		fail("Storage deposit UI did not use selected owned Item UID")
		return
	panel._update_storage_ui()
	panel.storage_withdraw_button.pressed.emit()
	if stub.storage_withdrawals != 1 or stub.last_storage_uid != 20000:
		fail("Storage withdraw UI did not use selected stored Item UID")
		return
	panel._latest.pending_storage_sequence = 2
	panel._update_storage_ui()
	if not panel.storage_deposit_button.disabled or not panel.storage_withdraw_button.disabled:
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
	panel._update_equipment_ui()
	if not panel.equipment_equip_button.disabled or not panel.equipment_unequip_button.disabled or not panel.equipment_status.text.contains("尚未由伺服器啟用"):
		fail("Equipment UI was usable without authoritative baseline")
		return
	panel._latest.equipment_ready = true
	panel._latest.equipment_revision = 1
	panel._latest.equipment_items = {30000:{
		"item_uid":30000,"slot_id":4,"item_definition_id":4001,"drop_id":0}}
	panel._update_equipment_ui()
	if panel.equipment_inventory_picker.item_count == 0 or panel.equipment_equipped_picker.item_count != 1 or panel.equipment_equip_button.disabled or panel.equipment_unequip_button.disabled:
		fail("Authoritative equipment baseline was not rendered as playable controls")
		return
	if not panel.equipment_equipped_picker.get_item_text(0).contains("裝備欄位 4"):
		fail("Equipment UI guessed or hid opaque server slot id")
		return
	panel.equipment_equip_button.pressed.emit()
	if stub.equipment_equips != 1 or stub.last_equipment_uid != 10000:
		fail("Equipment equip UI did not use selected inventory UID")
		return
	panel._update_equipment_ui()
	panel.equipment_unequip_button.pressed.emit()
	if stub.equipment_unequips != 1 or stub.last_equipment_uid != 30000:
		fail("Equipment unequip UI did not use selected equipped UID")
		return
	panel._latest.pending_equipment_sequence = 1
	panel._update_equipment_ui()
	if not panel.equipment_equip_button.disabled or not panel.equipment_unequip_button.disabled:
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
	panel._update_bank_ui()
	if panel.bank_open_button.get_parent() == null or panel.equipment_equip_button.get_parent() == null:
		fail("Equipment/bank controls were not attached to the visible SessionPanel")
		return
	panel.bank_npc_input.value = 77
	panel.bank_open_button.pressed.emit()
	if stub.bank_opens != 1 or stub.last_bank_npc != 77 or not panel.bank_open_button.disabled:
		fail("Bank open UI did not send exactly one NPC intent")
		return
	panel._latest.active_bank_npc = 77
	panel._latest.bank_scope = 2
	panel._latest.bank_wallet_money = 1000
	panel._latest.bank_money = 400
	panel._latest.bank_sequence = 1
	panel.bank_amount_input.value = 300
	panel._update_bank_ui()
	if not panel.bank_scope_label.text.contains("帳號共用") or not panel.bank_wallet_label.text.contains("1000") or not panel.bank_balance_label.text.contains("400") or panel.bank_deposit_button.disabled or panel.bank_withdraw_button.disabled:
		fail("Authoritative bank scope/balances were not rendered as playable controls")
		return
	panel.bank_deposit_button.pressed.emit()
	if stub.bank_deposits != 1 or stub.last_bank_amount != 300:
		fail("Bank deposit UI did not send selected amount")
		return
	panel._update_bank_ui()
	panel.bank_withdraw_button.pressed.emit()
	if stub.bank_withdrawals != 1 or stub.last_bank_amount != 300:
		fail("Bank withdraw UI did not send selected amount")
		return
	panel.bank_amount_input.value = 500
	panel._update_bank_ui()
	if panel.bank_withdraw_button.disabled != true or panel.bank_deposit_button.disabled:
		fail("Bank UI did not apply current authoritative balance bounds")
		return
	panel._latest.pending_bank_sequence = 2
	panel.bank_amount_input.value = 100
	panel._update_bank_ui()
	if not panel.bank_deposit_button.disabled or not panel.bank_withdraw_button.disabled:
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
	panel._update_repair_ui()
	if panel.repair_open_button.get_parent() == null or not panel.repair_button.disabled:
		fail("Repair controls were not attached or were usable before server preview")
		return
	panel.repair_npc_input.value = 66
	panel.repair_open_button.pressed.emit()
	if stub.repair_opens != 1 or stub.last_repair_npc != 66 or not panel.repair_open_button.disabled:
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
	panel._update_repair_ui()
	if panel.repair_item_picker.item_count != 1 or not panel.repair_item_picker.get_item_text(0).contains("耐久 80/100") or not panel.repair_item_picker.get_item_text(0).contains("費用 37") or not panel.repair_wallet_label.text.contains("100") or panel.repair_button.disabled:
		fail("Repair UI did not render exact authoritative preview")
		return
	panel.repair_button.pressed.emit()
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
	panel._update_repair_ui()
	if not panel.repair_item_picker.get_item_text(0).contains("耐久 100/100") or not panel.repair_item_picker.get_item_text(0).contains("費用 0") or not panel.repair_wallet_label.text.contains("63") or not panel.repair_button.disabled:
		fail("Repair UI locally recomputed or failed to apply server settlement")
		return

	panel._latest.player_dead = true
	panel._update_inventory_ui()
	if not panel.recovery_button.disabled or panel.submit_recovery_use(10000):
		fail("Dead character could submit recovery use")
		return
	panel._latest.player_dead = false
	panel._latest.inventory_ready = false
	panel._update_inventory_ui()
	if panel.inventory_preview.visible or not panel.inventory_button.disabled:
		fail("Incomplete inventory leaked prior character item list")
		return
	panel.active = false
	panel._set_respawn_controls()
	app.queue_free()
	await process_frame
	print("CLIENT_TLS_LOGIN_PASS ui=PASS auth=PASS choose=PASS receipt=PASS world_ready=PASS password_cleared=PASS logout=PASS respawn_ui=PASS click_attack_ui=PASS click_pickup_ui=PASS persistent_inventory_ui=PASS item_metadata=PASS inventory_pagination=PASS recovery_item_button=PASS inventory_discard_button=PASS inventory_discard_quantity_confirm=PASS shop_ui=PASS storage_ui=PASS equipment_ui=PASS bank_ui=PASS repair_ui=PASS skill_cast_ui=PASS skill_singleflight=PASS combat_mp_ui=PASS")
	quit(0)
