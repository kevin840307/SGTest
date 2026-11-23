extends SceneTree
## Standalone Windows screenshot and source-image/icon contract for the
## Recovered2 world HUD. This never opens a network connection.
## Private appearance files are optional on CI and are not distributed.
const App = preload("res://app/main.gd")
const SkinCatalog = preload("res://ui/ui_skin_catalog.gd")
const Layout = preload("res://ui/session_layout.gd")
const State = preload("res://world/world_state.gd")

class IntentStub:
	extends RefCounted
	var recovery_uid := 0
	var equip_uid := 0
	var unequip_uid := 0
	var discard_uid := 0
	var discard_quantity := 0
	var buy_item_id := 0
	var buy_quantity := 0
	var sell_uid := 0
	var sell_quantity := 0
	var quest_choice := 0
	var party_invite_target := 0
	var party_left := 0
	var clan_create_name := ""
	var clan_join_id := 0
	var clan_left := 0
	var clan_refreshed := 0
	var clan_announcement := ""
	var clan_kick_target := 0
	var clan_transfer_target := 0
	func use_recovery_item(uid: int) -> bool:
		recovery_uid = uid
		return true
	func equip_inventory_item(uid: int) -> bool:
		equip_uid = uid
		return true
	func unequip_item(uid: int) -> bool:
		unequip_uid = uid
		return true
	func discard_inventory_item(uid: int,quantity: int) -> bool:
		discard_uid = uid
		discard_quantity = quantity
		return true
	func buy_shop_item(item_id: int,quantity: int) -> bool:
		buy_item_id = item_id
		buy_quantity = quantity
		return true
	func sell_shop_item(uid: int,quantity: int) -> bool:
		sell_uid = uid
		sell_quantity = quantity
		return true
	func choose_quest_choice(choice_id: int) -> bool:
		quest_choice = choice_id
		return true
	func party_invite(actor_id: int) -> bool:
		party_invite_target = actor_id
		return true
	func party_invite_name(_name: String) -> bool:
		return false
	func party_accept() -> bool:
		return true
	func party_reject() -> bool:
		return true
	func party_leave() -> bool:
		party_left += 1
		return true
	func party_kick(_actor_id: int) -> bool:
		return true
	func party_promote(_actor_id: int) -> bool:
		return true
	func send_party_chat(_message: String,_channel: int) -> bool:
		return true
	func clan_create(name: String) -> bool:
		clan_create_name = name
		return true
	func clan_join(id: int) -> bool:
		clan_join_id = id
		return true
	func clan_leave() -> bool:
		clan_left += 1
		return true
	func clan_refresh() -> bool:
		clan_refreshed += 1
		return true
	func clan_set_announcement(text: String) -> bool:
		clan_announcement = text
		return true
	func clan_kick(actor_id: int) -> bool:
		clan_kick_target = actor_id
		return true
	func clan_transfer_leadership(actor_id: int) -> bool:
		clan_transfer_target = actor_id
		return true
	func stop() -> void:
		pass

func _initialize() -> void:
	root.size = Vector2i(1152,620)
	_run.call_deferred()

func _fail(reason: String) -> void:
	push_error("RECOVERED2_WORLD_UI_FAIL "+reason)
	quit(1)

func _capture(file: String) -> void:
	if OS.get_cmdline_user_args().is_empty() or DisplayServer.get_name() == "headless":
		return
	await process_frame
	await RenderingServer.frame_post_draw
	if root.get_texture().get_image().save_png(OS.get_cmdline_user_args()[0].path_join(file+".png")) != OK:
		_fail("capture failed: "+file)
	else:
		print("RECOVERED2_WORLD_CAPTURE "+file)

func _run() -> void:
	if not SkinCatalog.valid("recovered2_local"):
		print("RECOVERED2_WORLD_UI_SKIP private UI assets missing")
		quit(0)
		return
	var args := OS.get_cmdline_user_args()
	if args.size() > 0:
		DirAccess.make_dir_recursive_absolute(args[0])
	var app := App.new()
	app.auto_start = false
	app.preview_auth_offline = true
	root.add_child(app)
	# Local Windows can verify the REAL original Map15 texture pipeline beneath
	# the visual shell. CI omits the private pack and still checks controls.
	if args.size() > 1:
		app.world_view.show_players = true
		if app.world_view.open_pack(str(args[1])) != OK or app.world_view.request_map(15) != OK:
			_fail("private map assets cannot mount")
			return
		var deadline := Time.get_ticks_msec()+30000
		while app.world_view.phase in ["map","palette"] and Time.get_ticks_msec() < deadline:
			await process_frame
		if app.world_view.phase != "ready":
			_fail("Map15 original textures not ready")
			return
	# Emulate the UI phase of a successfully entered world without opening
	# a TLS connection. This exercises the real Main visibility policy,
	# rather than a manually visible shell layered over debug toolbar.
	app.set_process(false)
	app.session_panel.set_process(false)
	app.auto_start = true
	app._current_skin = "recovered2_local"
	app.session_panel.visible = true
	app.session_panel.active = true
	app.session_panel._layout.show_page(Layout.Page.WORLD)
	app._update_ui_stage(true)
	var shell = app.recovered2_world
	shell.resize_view(Vector2(root.size))
	var fixtures := {
		"state":State.STATE_IN_WORLD,
		"world_ready":true,"actor_id":101,"map_epoch":2,"map_id":15,
		"characters":[{"character_id":101,"name":"測試玩家","slot":0}],
		"x":6674,"y":4802,"player_current_hp":720,"player_max_hp":1000,
		"player_current_mp":210,"player_max_mp":500,"player_stamina":90,
		"player_level":22,"player_strength":18,"player_constitution":16,
		"player_dexterity":20,"player_intelligence":24,"player_attack":188,
		"player_defense":142,"player_hit":96,"player_evasion":35,
		"player_move_speed":240,"player_attack_speed":100,"player_job":"聖靈使",
		"player_race":"人族","wallet":105500,"player_money":105500,
		"inventory_ready":true,"equipment_ready":true,"combat_available":true,
		"inventory_revision":12,"equipment_revision":12,
		"reconnecting":false,"resync_pending":false,"waiting_for_new_map":false,
		"player_dead":false,"pending_world_ready_ack_epoch":0,"last_world_ready_ack_epoch":2,
		"pending_recovery_sequence":0,"pending_discard_sequence":0,
		"pending_equipment_sequence":0,"pending_shop_sequence":0,"pending_quest_sequence":0,
		"inventory_items":{
			10001:{"item_uid":10001,"item_definition_id":1001,"quantity":12,"enhancement_level":0},
			10002:{"item_uid":10002,"item_definition_id":2000,"quantity":3,"enhancement_level":0},
			10003:{"item_uid":10003,"item_definition_id":30001,"quantity":1,"enhancement_level":3},
			10004:{"item_uid":10004,"item_definition_id":15001,"quantity":1,"enhancement_level":0},
		},
		"equipment_items":{
			10005:{"item_uid":10005,"item_definition_id":30002,"quantity":1,"enhancement_level":2},
		},
		"learned_skills":{99001:1,99002:2},
		"party_id":77,"party_revision":4,"party_leader_actor_id":101,
		"party_members":{
			101:{"name":"測試玩家","level":22,"map_id":15},
			102:{"name":"補師隊友","level":21,"map_id":15},
		},
		"pending_party_sequence":0,"pending_whisper_sequence":0,
		"pending_party_invite":{},
		"clan_id":501,"clan_revision":3,"clan_leader_actor_id":101,
		"clan_name":"星門先遣隊","clan_announcement":"今晚九點集合","pending_clan_sequence":0,
		"last_clan_result_status":0,
		"clan_members":{
			101:{"actor_id":101,"role":1,"name":"測試玩家"},
			103:{"actor_id":103,"role":0,"name":"社團隊友"},
		},
		"active_shop_npc":500,"shop_money":105500,
		"shop_items":{
			1001:{"buy_price":120,"sell_price":60},
			2000:{"buy_price":300,"sell_price":150},
		},
		"quest_view":{
			"quest_id":9001,"title":"失落的星鑰","text":"前往廣場尋找遺失的星鑰並回報。",
			"state":1,"stage":2,"progress_revision":7,"reward_text":"經驗 1200、金錢 350",
			"choices":[
				{"choice_id":11,"label":"提交星鑰"},
				{"choice_id":12,"label":"稍後再說"},
			],
		},
	}
	var intents := IntentStub.new()
	app.session_panel.session = intents
	app.session_panel._latest = fixtures.duplicate(true)
	app.session_panel.inventory_panel.apply_snapshot(
		fixtures,true,true,intents,app.session_panel.item_catalog)
	app.session_panel.party_panel.apply_snapshot(fixtures,true,intents)
	shell.apply_snapshot(fixtures)
	if not shell.visible or shell._hp.value != 720 or shell._mp.value != 210 or not shell.stage.visible:
		_fail("basic HUD HP/MP from snapshot not presented")
		return
	if shell.menu.size.x != 50 or shell.menu.get_node_or_null("Menu0") == null:
		_fail("Recovered2 right menu layout/buttons missing")
		return
	if app.toolbar.visible or app._map_buttons.any(func(button): return button.visible):
		_fail("development toolbar Map13/Map15 still visible after world entry")
		return
	await _capture("hud")
	shell._menu_action(1)
	if not shell.inventory.visible or shell.character.visible:
		_fail("inventory menu did not open independent inventory window")
		return
	shell.inventory._change_tab(0)
	shell.inventory.apply_snapshot(fixtures)
	if shell.inventory._rows.get_child_count() != 2:
		_fail("recovery and general items missing in first inventory tab")
		return
	var first = shell.inventory._rows.get_child(0)
	var icon = first.find_child("Photo",true,false)
	if icon == null:
		_fail("inventory item image widget missing")
		return
	if FileAccess.file_exists("res://ui/private_recovered2/item_photos/Photo0398.png") and icon.texture == null:
		_fail("known recovery item should render Recovered2 Photo0398.png")
		return
	if shell.inventory.custom_minimum_size != Vector2(300,520) or shell.inventory._search.visible or shell.inventory._page_bar.visible:
		_fail("inventory no longer matches Recovered2 300x520 design contract/single-page presentation")
		return
	var recovery_row = shell.inventory.find_child("ItemUid10001",true,false)
	if not recovery_row is Button:
		_fail("recovery item row missing")
		return
	(recovery_row as Button).pressed.emit()
	shell.inventory._use.pressed.emit()
	if intents.recovery_uid != 10001:
		_fail("inventory recovery action did not preserve authoritative item UID")
		return
	shell.inventory._discard_selected()
	if intents.discard_uid != 10001 or intents.discard_quantity != 1:
		_fail("inventory discard action did not preserve UID/quantity")
		return
	await _capture("inventory")
	# A 130-slot inventory must never instantiate 130 Control/TextureRects
	# on a single UI refresh. Only the visible 40 rows are mounted.
	var bulk := fixtures.duplicate(true)
	bulk.inventory_revision = 13
	bulk.inventory_items = {}
	for i in 130:
		var uid := 20000+i
		bulk.inventory_items[uid] = {"item_uid":uid,"item_definition_id":1001,"quantity":1}
	shell.inventory.apply_snapshot(bulk)
	if shell.inventory._rows.get_child_count() != shell.inventory.PAGE_SIZE or shell.inventory._page_next.disabled:
		_fail("unbounded inventory rows or pagination missing at 130 items")
		return
	shell.inventory._navigate_page(1)
	if shell.inventory._rows.get_child_count() != shell.inventory.PAGE_SIZE or shell.inventory._page != 1:
		_fail("inventory page two should render at most the visible page")
		return
	shell.inventory._navigate_page(10)
	if shell.inventory._rows.get_child_count() != 10 or not shell.inventory._page_next.disabled:
		_fail("inventory final page not clamped to remaining ten items")
		return
	shell.inventory.apply_snapshot(fixtures)
	shell.inventory._change_tab(1)
	if shell.inventory._rows.get_child_count() != 3:
		_fail("weapon/equipment tab should include owned and equipped items")
		return
	var equip_row = shell.inventory.find_child("ItemUid10003",true,false)
	if equip_row is Button:
		(equip_row as Button).pressed.emit()
		shell.inventory._equip.pressed.emit()
		if intents.equip_uid != 10003:
			_fail("inventory equip action lost selected item UID")
			return
	var equipped_row = shell.inventory.find_child("ItemUid10005",true,false)
	if equipped_row is Button:
		(equipped_row as Button).pressed.emit()
		shell.inventory._equip.pressed.emit()
		if intents.unequip_uid != 10005:
			_fail("inventory unequip action lost equipped UID")
			return
	shell._menu_action(0)
	if not shell.character.visible or shell.inventory.visible:
		_fail("character window and inventory are not mutually exclusive")
		return
	if shell.character.size != Vector2(560,610) or shell.character.find_child("IdentityPanel",true,false) == null or shell.character.find_child("CombatPanel",true,false) == null:
		_fail("character source geometry did not match Recovered2 560x610 sections")
		return
	var strength = shell.character.find_child("Field力量",true,false)
	var name_field = shell.character.find_child("Field名稱",true,false)
	if not strength is Label or (strength as Label).text != "18" or not name_field is Label or (name_field as Label).text != "測試玩家":
		_fail("character information did not bind live snapshot fields")
		return
	await _capture("character")
	var aux_cases := [
		[2,"skill",shell.skill_window],
		[3,"party",shell.party_window],
		[4,"clan",shell.clan_window],
		[5,"shop",shell.shop_window],
		[6,"quest",shell.quest_window],
		[7,"mall",shell.mall_window],
		[8,"settings",shell.settings_window],
	]
	for row in aux_cases:
		shell._menu_action(int(row[0]))
		var window: Control = row[2]
		if not window.visible or shell.inventory.visible or shell.character.visible:
			_fail("%s menu did not open its independent Recovered2 window" % str(row[1]))
			return
		await _capture(str(row[1]))

	shell._menu_action(2)
	var skill_summary = shell.skill_window.find_child("SkillSummary",true,false)
	if not skill_summary is Label or not (skill_summary as Label).text.contains("99001") or not (skill_summary as Label).text.contains("99002"):
		_fail("skill window did not present learned server skills")
		return

	shell._menu_action(3)
	var party_summary = shell.party_window.find_child("PartyMembersSummary",true,false)
	if not party_summary is Label or not (party_summary as Label).text.contains("測試玩家") or not (party_summary as Label).text.contains("補師隊友"):
		_fail("party window did not present real member snapshot")
		return
	var party_target = shell.party_window.find_child("PartyInviteTarget",true,false)
	var party_invite = shell.party_window.find_child("PartyInviteButton",true,false)
	var party_leave = shell.party_window.find_child("PartyLeaveButton",true,false)
	if not party_target is LineEdit or not party_invite is Button or not party_leave is Button:
		_fail("party action controls missing")
		return
	(party_target as LineEdit).text = "102"
	(party_invite as Button).pressed.emit()
	if intents.party_invite_target != 102:
		_fail("party invite did not preserve target actor")
		return
	(party_leave as Button).pressed.emit()
	if intents.party_left != 1:
		_fail("party leave action was not delegated")
		return

	shell._menu_action(5)
	var buy_picker = shell.shop_window.find_child("ShopBuyPicker",true,false)
	var buy_qty = shell.shop_window.find_child("ShopBuyQuantity",true,false)
	var buy_button = shell.shop_window.find_child("ShopBuyButton",true,false)
	var sell_picker = shell.shop_window.find_child("ShopSellPicker",true,false)
	var sell_qty = shell.shop_window.find_child("ShopSellQuantity",true,false)
	var sell_button = shell.shop_window.find_child("ShopSellButton",true,false)
	if not buy_picker is OptionButton or (buy_picker as OptionButton).item_count != 2 or not sell_picker is OptionButton or (sell_picker as OptionButton).item_count < 2:
		_fail("NPC shop did not bind server products and sellable inventory")
		return
	(buy_picker as OptionButton).select(0)
	(buy_qty as SpinBox).value = 2
	(buy_button as Button).pressed.emit()
	if intents.buy_item_id <= 0 or intents.buy_quantity != 2:
		_fail("NPC shop buy did not delegate item/quantity")
		return
	(sell_picker as OptionButton).select(0)
	(sell_qty as SpinBox).value = 1
	(sell_button as Button).pressed.emit()
	if intents.sell_uid <= 0 or intents.sell_quantity != 1:
		_fail("NPC shop sell did not delegate inventory UID")
		return

	shell._menu_action(6)
	var progress_list = shell.quest_window.find_child("QuestInProgressList",true,false)
	var quest_choices = shell.quest_window.find_child("QuestChoicePicker",true,false)
	var quest_choose = shell.quest_window.find_child("QuestChooseButton",true,false)
	var quest_source = shell.quest_window.find_child("QuestSourceLayout",true,false)
	if not progress_list is Label or not (progress_list as Label).text.contains("失落的星鑰") or not quest_choices is OptionButton or (quest_choices as OptionButton).item_count != 2 or quest_source == null:
		_fail("quest window did not present server quest state/choices")
		return
	(quest_choices as OptionButton).select(0)
	(quest_choose as Button).pressed.emit()
	if intents.quest_choice != 11:
		_fail("quest choice action did not preserve choice ID")
		return

	shell._menu_action(4)
	var clan_identity = shell.clan_window.find_child("ClanIdentitySummary",true,false)
	var clan_member = shell.clan_window.find_child("ClanMember103",true,false)
	var clan_leave = shell.clan_window.find_child("ClanLeaveButton",true,false)
	var clan_refresh = shell.clan_window.find_child("ClanRefreshButton",true,false)
	var clan_management = shell.clan_window.find_child("ClanManagementSection",true,false)
	var clan_announcement_edit = shell.clan_window.find_child("ClanAnnouncementEdit",true,false)
	var clan_announcement_update = shell.clan_window.find_child("ClanAnnouncementUpdateButton",true,false)
	var clan_kick = shell.clan_window.find_child("ClanKick103",true,false)
	var clan_transfer = shell.clan_window.find_child("ClanTransfer103",true,false)
	if (
		not clan_identity is Label or not (clan_identity as Label).text.contains("星門先遣隊") or
		not (clan_identity as Label).text.contains("今晚九點集合") or
		not clan_member is Label or not (clan_member as Label).text.contains("社團隊友") or
		not clan_leave is Button or not clan_refresh is Button or
		not clan_management is Control or not (clan_management as Control).visible or
		not clan_announcement_edit is LineEdit or (clan_announcement_edit as LineEdit).text!="今晚九點集合" or
		not clan_announcement_update is Button or not clan_kick is Button or not clan_transfer is Button
	):
		_fail("clan window did not render authoritative clan/member management snapshot")
		return
	(clan_announcement_edit as LineEdit).text = "明晚八點集合"
	(clan_announcement_update as Button).pressed.emit()
	(clan_kick as Button).pressed.emit()
	(clan_transfer as Button).pressed.emit()
	(clan_refresh as Button).pressed.emit()
	(clan_leave as Button).pressed.emit()
	if intents.clan_announcement != "明晚八點集合" or intents.clan_kick_target != 103 or intents.clan_transfer_target != 103 or intents.clan_refreshed != 1 or intents.clan_left != 1:
		_fail("clan management/refresh/leave did not delegate to authenticated session")
		return

	var no_clan := fixtures.duplicate(true)
	no_clan["clan_id"] = 0
	no_clan["clan_revision"] = 0
	no_clan["clan_leader_actor_id"] = 0
	no_clan["clan_name"] = ""
	no_clan["clan_announcement"] = ""
	no_clan["clan_members"] = {}
	no_clan["last_clan_result_status"] = -1
	app.session_panel._latest = no_clan.duplicate(true)
	shell.apply_snapshot(no_clan)
	shell._menu_action(4)
	var clan_create_name = shell.clan_window.find_child("ClanCreateName",true,false)
	var clan_create = shell.clan_window.find_child("ClanCreateButton",true,false)
	var clan_join_id = shell.clan_window.find_child("ClanJoinId",true,false)
	var clan_join = shell.clan_window.find_child("ClanJoinButton",true,false)
	if not clan_create_name is LineEdit or not clan_create is Button or not clan_join_id is SpinBox or not clan_join is Button:
		_fail("clan create/join controls missing for non-member")
		return
	(clan_create_name as LineEdit).text = "新的星門戰團"
	(clan_create as Button).pressed.emit()
	if intents.clan_create_name != "新的星門戰團":
		_fail("clan create name was not delegated")
		return
	(clan_join_id as SpinBox).value = 777
	(clan_join as Button).pressed.emit()
	if intents.clan_join_id != 777:
		_fail("clan join ID was not delegated")
		return

	var settings_picker = shell.settings_window.find_child("UiProfilePickerSettings",true,false)
	if not settings_picker is OptionButton or (settings_picker as OptionButton).item_count < 2:
		_fail("settings window must expose switchable UI profiles")
		return
	shell.set_world_active(false)
	if shell.visible or shell.inventory.visible or shell.character.visible or shell._aux_windows.any(func(window): return window.visible):
		_fail("world exit should close all persistent floating windows")
		return
	app.queue_free()
	await process_frame
	print("RECOVERED2_WORLD_UI_PASS hud=true right_menu=true inventory_icons=true equipment_tab=true aux_windows=true skins_switchable=true")
	quit(0)
