extends SceneTree
## Independent classic IE HK2 source SPR verification; same authenticated
## SessionPanel and no alternative networking, inventory or gameplay state.
const Main = preload("res://app/main.gd")
const Layout = preload("res://ui/session_layout.gd")
const State = preload("res://world/world_state.gd")
const Skins = preload("res://ui/ui_skin_catalog.gd")

func _fail(reason: String) -> void:
	push_error("CLASSIC_IE_UI_FAIL "+reason)
	quit(1)

func _initialize() -> void:
	root.size = Vector2i(1152,620)
	_run.call_deferred()

func _capture(path: String) -> void:
	await process_frame
	await RenderingServer.frame_post_draw
	var error := root.get_texture().get_image().save_png(path)
	if error != OK:
		_fail("native screenshot write failed %d" % error)

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var directory := str(args[0]) if args.size() > 0 else ""
	if not Skins.valid("original_ie_hk2"):
		print("CLASSIC_IE_UI_SKIP original IE HK2 local assets unavailable")
		quit(0)
		return
	var app = Main.new()
	app.auto_start = false
	root.add_child(app)
	var shell = app.classic_auth
	app.session_panel.set_process(false)
	app.session_panel.active = true
	app.session_panel.show()
	app.session_panel._latest = {"state":State.STATE_SELECTING_CHARACTER}
	app.auto_start = true
	app._current_skin = "original_ie_hk2"
	app.session_panel._layout.show_page(Layout.Page.LOGIN)
	app._update_ui_stage(true)
	if not shell.visible or app.recovered2_auth.visible or app.toolbar.visible:
		_fail("classic login did not replace R2 source presentation")
		return
	if shell.login_page.get_child(0).texture == null:
		_fail("original 800x600 ACCOUNT_LOGIN.spr source was not imported")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_login.png"))
	app.session_panel._layout.show_page(Layout.Page.CHARACTER_SELECT)
	app.session_panel._characters = [{"character_id":42,"name":"TestA","slot":0,"appearance":{"race":0,"hair_index":0,"hair_color":0}}]
	app._update_ui_stage(true)
	if not shell.select_page.visible or shell._slots[0].text.find("TestA") < 0:
		_fail("original 3-slot chooser did not reflect source names")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_select.png"))
	app.session_panel._layout.show_page(Layout.Page.CHARACTER_CREATE)
	app._update_ui_stage(true)
	if not shell.create_page.visible or shell._name.placeholder_text != "角色名稱":
		_fail("original creation source layout missing")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_create.png"))
	app.session_panel._layout.show_page(Layout.Page.WORLD)
	app._update_ui_stage(true)
	var world = app.classic_world
	if (
		not world.visible or app.recovered2_world.visible or
		world.menu.size != Vector2(145,28) or
		world.inventory.size != Vector2(259,506) or
		world.character.size != Vector2(263,397) or
		world.skill_window.size != Vector2(223,506) or
		world.party_window.size != Vector2(139,144) or
		world.clan_window.size != Vector2(263,360) or
		world.shop_window.size != Vector2(770,506) or
		world.quest_window.size != Vector2(527,350) or
		world.player_shop_window.size != Vector2(770,506) or
		world.storage_window.size != Vector2(770,506) or
		world.bank_window.size != Vector2(399,172) or
		world.repair_window.size != Vector2(223,506) or
		world.trade_window.size != Vector2(770,506) or
		world.dialog_window.size != Vector2(570,428) or
		world.settings_window.size != Vector2(291,207)
	):
		_fail("original SPR world geometry not applied by independent ClassicWorldShell")
		return
	if (
		(world.shop_window.get_node("BG_SHOPBackground") as TextureRect).texture != null or
		(world.storage_window.get_node("BG_BANKBackground") as TextureRect).texture != null or
		(world.trade_window.get_node("BG_TRADEBackground") as TextureRect).texture != null
	):
		_fail("hidden large classic windows were eagerly decoded instead of lazy loaded")
		return
	var fixture := {
		"world_ready":true,"actor_id":42,"map_id":15,"map_epoch":1,
		"x":100.0,"y":200.0,"player_current_hp":75,"player_max_hp":100,
		"player_current_mp":30,"player_max_mp":40,"player_level":22,
		"character_name":"TestA","player_job":"聖靈使",
		"player_strength":18,"player_dexterity":20,"player_constitution":16,
		"player_intelligence":24,"player_attack":188,"player_defense":142,
		"player_hit":96,"player_evasion":35,"player_move_speed":240,
		"player_attack_speed":100,"wallet":105500,"inventory_ready":true,
		"inventory_revision":1,
		"inventory_items":{
			10001:{"item_uid":10001,"item_definition_id":1001,"quantity":12},
			10002:{"item_uid":10002,"item_definition_id":2000,"quantity":3},
			10003:{"item_uid":10003,"item_definition_id":30001,"quantity":1},
		},
		"equipment_ready":true,"equipment_revision":1,
		"equipment_items":{
			10005:{"item_uid":10005,"slot_id":4,"item_definition_id":30002,"quantity":1},
		},
		"learned_skills":{99001:1,99002:2},
		"party_id":77,"party_leader_actor_id":42,
		"party_members":{
			42:{"name":"TestA","level":22,"map_id":15},
			43:{"name":"隊友","level":21,"map_id":15},
		},
		"clan_id":501,"clan_revision":3,"clan_leader_actor_id":42,
		"clan_name":"星門先遣隊","clan_announcement":"今晚九點集合","clan_members":{
			42:{"actor_id":42,"role":1,"name":"TestA"},
			44:{"actor_id":44,"role":0,"name":"社團隊友"},
		},
		"active_shop_npc":500,"shop_money":105500,
		"shop_items":{
			1001:{"buy_price":120,"sell_price":60},
			2000:{"buy_price":300,"sell_price":150},
		},
		"last_quest_response_sequence":1,
		"quest_view":{
			"quest_id":301,"progress_revision":1,"state":1,"stage":2,
			"title":"失落的星鑰","text":"前往廣場尋找遺失的星鑰並回報。",
			"choices":[{"choice_id":11,"label":"提交星鑰"},{"choice_id":12,"label":"稍後再說"}],
		},
		"entities":{
			43:{"name":"附近玩家","x":120.0,"y":205.0},
			44:{"name":"另一玩家","x":140.0,"y":210.0},
		},
		"friends":{
			43:{"actor_id":43,"name":"附近玩家","online":true},
			44:{"actor_id":44,"name":"離線好友","online":false},
		},
		"pending_friend_sequence":0,
		"chat_messages":[
			{"channel":0,"sender_actor_id":43,"sender_name":"附近玩家","message":"你好"},
			{"channel":3,"sender_actor_id":44,"sender_name":"隊友","message":"出發吧"},
		],
		"player_shop_own_listing":{},
		"player_shop_inspected":{
			"seller_actor_id":43,"listing_id":88,"item_definition_id":1001,
			"item_uid":9001,"quantity":2,"price":777,
		},
		"pending_player_shop_sequence":0,
		"active_storage_npc":700,"storage_ready":true,"storage_revision":4,"storage_sequence":4,
		"storage_items":{
			50001:{"item_uid":50001,"item_definition_id":1001,"quantity":9},
		},
		"active_bank_npc":701,"bank_scope":2,"bank_wallet_money":105500,"bank_money":25000,
		"pending_bank_sequence":0,
		"active_repair_npc":702,"repair_ready":true,"repair_wallet_money":105500,
		"repair_items":{
			10003:{"item_definition_id":30001,"current_durability":50,"max_durability":100,"cost":20},
		},
		"trade_id":0,"trade_state":0,
	}
	for i in range(20):
		var uid:=20000+i
		fixture.inventory_items[uid]={"item_uid":uid,"item_definition_id":90000+i,"quantity":i+1}
	world.apply_snapshot(fixture)
	if not world.dialog_window.visible or world._dialog_title.text != "失落的星鑰" or world._dialog_choices.get_child_count() != 2:
		_fail("classic BG_NPC_MESSAGE_06 did not bind server-owned NPC quest dialogue")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_dialog.png"))
	world.dialog_window.hide()
	if world._hp_label.text != "75 / 100" or world._mp_label.text != "30 / 40" or world._level_label.text != "22":
		_fail("classic HPBAR did not bind authoritative Server values")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_world.png"))
	if (
		world._chat_history.text.find("你好") < 0 or
		world._friend_rows.get_child_count() != 2 or
		not str(world._friend_rows.get_child(0).text).begins_with("● ") or
		not str(world._friend_rows.get_child(1).text).begins_with("○ ")
	):
		_fail("classic persistent chat/friend panels did not bind authoritative friend presence")
		return
	world.menu.get_node("Menu2").pressed.emit()
	if (
		not world.inventory.visible or
		world.inventory.get_node_or_null("BG_ITEMBackground") == null or
		world._inventory_tab_buttons.size() != 5 or
		world._inventory_rows.get_child_count() != 17
	):
		_fail("original inventory window did not bind multi-item rows/tabs on BG_ITEM")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_inventory.png"))
	world._change_inventory_tab(1)
	if not world._inventory_equip_bg.visible or world._inventory_item_bg.visible or world._equipment_rows.get_child_count() != 1:
		_fail("classic equipment tab did not switch to BG_Equip and bind equipped UID")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_equipment.png"))
	world._change_inventory_tab(0)
	world.menu.get_node("Menu0").pressed.emit()
	if not world.clan_window.visible or world._clan_rows.get_child_count() != 2:
		_fail("classic BG_CLUB did not bind clan members")
		return
	world.secondary_menu.get_node("QuestCommand").pressed.emit()
	if not world.quest_window.visible or world._quest_title.text != "失落的星鑰" or world._quest_choices.get_child_count() != 2:
		_fail("classic BG_QUEST_LIST did not bind quest choices")
		return
	world.menu.get_node("Menu3").pressed.emit()
	if not world.player_shop_window.visible or world._player_shop_rows.get_child_count() < 3 or world._player_shop_buy.disabled:
		_fail("classic player-shop presentation is not backed by inventory/nearby/listing snapshot")
		return
	world.secondary_menu.get_node("StorageCommand").pressed.emit()
	if (
		not world.storage_window.visible or world._storage_rows.get_child_count() != 1 or
		world._storage_inventory_rows.get_child_count() != 17 or
		(world.storage_window.get_node("BG_BANKBackground") as TextureRect).texture == null
	):
		_fail("classic BG_BANK warehouse did not bind storage plus multi-item inventory")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_storage.png"))
	world.secondary_menu.get_node("BankCommand").pressed.emit()
	if not world.bank_window.visible or world._bank_status.text.find("25000") < 0:
		_fail("classic bank window did not bind authoritative balance")
		return
	world.secondary_menu.get_node("RepairCommand").pressed.emit()
	if not world.repair_window.visible or world._repair_rows.get_child_count() != 1:
		_fail("classic repair window did not bind repairable item")
		return
	app._recovered2_local_ready = app.recovered2_auth.attach(app.session_panel)
	app._current_skin = "recovered2_local"
	app._update_ui_stage(true)
	if world.visible or not app.recovered2_world.visible or app.session_panel.session == null:
		_fail("runtime Classic -> Recovered2 switch discarded shared session or left classic visible")
		return
	app.session_panel.active = false
	app._update_ui_stage(true)
	app.queue_free()
	await process_frame
	print("CLASSIC_IE_UI_PASS 800x600_original_spr=true main_session_shared=true login_select_create_world=true inventory_equipment_storage_dialog=true lazy_large_windows=true runtime_switch=true")
	quit(0)
