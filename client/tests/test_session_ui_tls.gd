extends SceneTree
## Exercises the actual entry's UI callbacks; no private textures required.
const Main = preload("res://app/main.gd")
const State = preload("res://network/world_state.gd")
const Session = preload("res://network/client_session.gd")
var app: Node

class RespawnSession extends RefCounted:
	var requests := 0
	var attacks := 0
	var pickups := 0
	func pickup_ground_drop(drop_id: int) -> bool:
		if drop_id <= 0:
			return false
		pickups += 1
		return true
	func attack_monster(entity_id: int) -> bool:
		if entity_id <= 0:
			return false
		attacks += 1
		return true
	func respawn() -> bool:
		requests += 1
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
		"inventory_ready":true,"actor_id":22,"reconnect_count":2,
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
			"quantity":1,"drop_id":50000+index}
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
	panel._latest.inventory_ready = false
	panel._update_inventory_ui()
	if panel.inventory_preview.visible or not panel.inventory_button.disabled:
		fail("Incomplete inventory leaked prior character item list")
		return
	panel.active = false
	panel._set_respawn_controls()
	app.queue_free()
	await process_frame
	print("CLIENT_TLS_LOGIN_PASS ui=PASS auth=PASS choose=PASS receipt=PASS world_ready=PASS password_cleared=PASS logout=PASS respawn_ui=PASS click_attack_ui=PASS click_pickup_ui=PASS persistent_inventory_ui=PASS item_metadata=PASS inventory_pagination=PASS")
	quit(0)
