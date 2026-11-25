extends SceneTree
## Independent classic IE HK2 source SPR verification; same authenticated
## SessionPanel and no alternative networking, inventory or gameplay state.
const Main = preload("res://app/main.gd")
const Layout = preload("res://ui/session_layout.gd")
const State = preload("res://world/world_state.gd")
const Skins = preload("res://ui/ui_skin_catalog.gd")

class FriendMessageSession:
	extends RefCounted
	var sends:=0
	var target:=0
	var message:=""
	func send_friend_message(target_actor_id:int,value:String)->bool:
		if target_actor_id<=0 or value.strip_edges().is_empty():
			return false
		sends+=1
		target=target_actor_id
		message=value
		return true

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
	if Main.HK_WINDOW_TITLE != "Stargate Online IE HK":
		_fail("native HK WindowName drifted from SGOClient.exe sub_5237F0")
		return
	if Skins.DEFAULT_SKIN != "original_ie_hk2" or Skins.SKIN_SCHEMA < 4:
		_fail("decoded HK source UI is not the product default/migration baseline")
		return
	if not Skins.valid("original_ie_hk2"):
		print("CLASSIC_IE_UI_SKIP original IE HK2 local assets unavailable")
		quit(0)
		return
	var app = Main.new()
	app.auto_start = false
	root.add_child(app)
	if app._current_skin != "original_ie_hk2" or app.skin_picker.visible:
		_fail("player-facing build must start HK-only with the generic skin picker hidden")
		return
	if app.classic_world._settings_picker.visible or app.classic_world._settings_skin_label.visible or app.classic_world._settings_hint.text != "介面：HK 原版":
		_fail("HK system settings still expose alternative UI profiles")
		return
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
	var native_captions := shell.login_page.get_node_or_null("ACCOUNT_LOGIN_HK_CAPTIONS") as TextureRect
	if FileAccess.file_exists("res://ui/private_ie_hk2/ACCOUNT_LOGIN_HK_CAPTIONS.png") and (
		native_captions == null or native_captions.texture == null or
		native_captions.texture.get_size() != Vector2(800,600) or
		native_captions.mouse_filter != Control.MOUSE_FILTER_IGNORE or
		shell._login_confirm_label.visible or shell._login_exit_label.visible or
		shell._login_account_label.visible or shell._login_password_label.visible
	):
		_fail("original-rendered HK login captions must share the 800×600 canvas and allow input through")
		return
	if FileAccess.file_exists("res://ui/private_ie_hk2/ACCOUNT_LOGIN_HK_INPUT_FONT.fnt") and (
		not (shell._login_input_font is FontFile) or
		absf(shell._login_input_font.get_string_size("TEST01",HORIZONTAL_ALIGNMENT_LEFT,-1,12).x - 39.0) > 0.01 or
		shell._username.get_theme_color("font_color") != Color8(241,245,241) or
		shell._password.get_theme_color("font_color") != Color8(241,245,241) or
		shell._username.get_theme_stylebox("normal").content_margin_left != 0.0 or
		shell._username.get_theme_stylebox("normal").content_margin_bottom != 4.0 or
		shell._password.get_theme_stylebox("normal").content_margin_left != 0.0 or
		shell._password.get_theme_stylebox("normal").content_margin_bottom != 4.0
	):
		_fail("HK login private PMingLiU bitmap-font metrics/color/margins drifted")
		return
	if (
		shell._username.position != Vector2(373,256) or shell._username.size != Vector2(170,16) or
		shell._password.position != Vector2(373,280) or shell._password.size != Vector2(170,16) or
		shell._login_confirm.position != Vector2(368,314) or shell._login_confirm.size != Vector2(85,21) or
		shell._login_exit.position != Vector2(463,314) or shell._login_exit.size != Vector2(85,21) or
		shell._login_account_label.position != Vector2(281,257) or shell._login_account_label.size != Vector2(48,12) or shell._login_account_label.text != "帳戶名稱" or
		shell._login_password_label.position != Vector2(281,281) or shell._login_password_label.size != Vector2(48,12) or shell._login_password_label.text != "帳戶密碼" or
		shell._login_confirm_label.position != Vector2(399,318) or shell._login_confirm_label.size != Vector2(24,12) or shell._login_confirm_label.text != "確定" or
		shell._login_exit_label.position != Vector2(494,318) or shell._login_exit_label.size != Vector2(24,12) or shell._login_exit_label.text != "離開" or
		not shell._username.placeholder_text.is_empty() or shell._username.max_length != 12 or not shell._password.placeholder_text.is_empty() or
		not shell._password.secret or shell._password.secret_character != "*"
	):
		_fail("001_ACCOUNT_LOGIN.ITF geometry/labels drifted from HK source user=%s/%s min=%s pass=%s/%s min=%s ok=%s/%s exit=%s/%s labels=%s,%s,%s,%s" % [
			str(shell._username.position),str(shell._username.size),str(shell._username.get_combined_minimum_size()),str(shell._password.position),str(shell._password.size),str(shell._password.get_combined_minimum_size()),
			str(shell._login_confirm.position),str(shell._login_confirm.size),str(shell._login_exit.position),str(shell._login_exit.size),
			str(shell._login_account_label.position),str(shell._login_password_label.position),
			str(shell._login_confirm_label.position),str(shell._login_exit_label.position)])
		return
	shell._username.grab_focus()
	await process_frame
	var login_tab := InputEventKey.new()
	login_tab.keycode = KEY_TAB
	login_tab.physical_keycode = KEY_TAB
	login_tab.pressed = true
	Input.parse_input_event(login_tab)
	await process_frame
	login_tab.pressed = false
	Input.parse_input_event(login_tab)
	await process_frame
	if root.gui_get_focus_owner() != shell._password:
		_fail("HK login forward Tab focus drifted from L01 account -> L02 password")
		return
	# Restore the original login-entry focus before any optional visual capture.
	shell._username.grab_focus()
	await process_frame
	if not directory.is_empty():
		await _capture(directory.path_join("classic_login.png"))
	app.session_panel._layout.show_page(Layout.Page.CHARACTER_SELECT)
	app.session_panel._characters = [{"character_id":42,"name":"TestA","slot":0,"appearance":{"race":0,"hair_index":0,"hair_color":0}}]
	app._update_ui_stage(true)
	if not shell.select_page.visible or shell._slots[0].text.find("TestA") < 0:
		_fail("original 3-slot chooser did not reflect source names")
		return
	app.session_panel._characters = [{"character_id":43,"name":"TestB","slot":2}]
	shell._update_characters()
	if not shell._slots[0].disabled or shell._slots[2].disabled or shell._slot_names[2].text != "TestB":
		_fail("the HK chooser shifted a character into an empty slot")
		return
	if (
		shell._password_modal_input.position != Vector2(358,308) or
		shell._password_modal_input.size != Vector2(170,12) or
		shell._change_old_password.position != Vector2(373,271) or
		shell._change_old_password.size != Vector2(170,12) or
		shell._change_new_password.size != Vector2(170,12) or
		shell._change_confirm_password.size != Vector2(170,12) or
		not shell._password_modal_input.secret or shell._password_modal_input.max_length != 4 or
		shell._password_modal_input.get_theme_font_size("font_size") != 12 or
		abs(shell._password_modal_input.get_theme_stylebox("normal").content_margin_bottom) > 0.01
	):
		_fail("the HK character password fields drifted from 081/082 ITF")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_select.png"))
	app.session_panel._layout.show_page(Layout.Page.CHARACTER_CREATE)
	app._update_ui_stage(true)
	var create_labels := {}
	for child in shell.create_page.get_children():
		if child is Label and not child.text.is_empty():
			create_labels[child.text] = child.position
	var required_create_labels := {
		"名稱":Vector2(169,261),"種族":Vector2(169,285),"頭部":Vector2(169,309),
		"頭部顏色":Vector2(157,333),"上身顏色":Vector2(157,357),
		"手袖顏色":Vector2(157,381),"下身顏色":Vector2(157,405),
		"力量":Vector2(510,261),"速度":Vector2(510,285),"體質":Vector2(510,309),
		"智慧":Vector2(510,333),"剩餘點數":Vector2(498,357),
		"角色密碼":Vector2(498,391),"確認密碼":Vector2(498,415),
		"建立角色":Vector2(497,475),"上一頁":Vector2(608,475),
	}
	if (
		not shell.create_page.visible or
		shell._name.position != Vector2(233,260) or shell._name.size != Vector2(125,15) or
		not shell._name.placeholder_text.is_empty() or
		shell._create_password.position != Vector2(596,391) or shell._create_password.size != Vector2(43,16) or
		shell._create_password_confirm.position != Vector2(596,415) or shell._create_password_confirm.size != Vector2(43,16) or
		not shell._create_password.secret or not shell._create_password_confirm.secret or
		shell._create_password.max_length != 4 or shell._create_password_confirm.max_length != 4 or
		shell._create_button.position != Vector2(478,471) or shell._create_button.size != Vector2(85,21) or
		shell._create_back_button.position != Vector2(583,471) or shell._create_back_button.size != Vector2(85,21)
	):
		_fail("005_CREATE_CHARACTER.ITF edit/button geometry drifted from HK source")
		return
	for text in required_create_labels:
		if not create_labels.has(text) or create_labels[text] != required_create_labels[text]:
			_fail("005_CREATE_CHARACTER.ITF label missing/drifted: %s" % text)
			return
	var stat_positions := {
		"strength":Vector2(596,261),"dexterity":Vector2(596,285),
		"constitution":Vector2(596,309),"wisdom":Vector2(596,333),
	}
	for field in stat_positions:
		if not shell._create_stat_values.has(field) or shell._create_stat_values[field].position != stat_positions[field]:
			_fail("005_CREATE_CHARACTER.ITF stat value missing/drifted: %s" % field)
			return
	if shell._create_remaining.position != Vector2(596,357) or shell._create_remaining.text != "4":
		_fail("HK creation remaining-points control missing")
		return
	shell._change_creation_stat("strength",1)
	if int(app.session_panel.character_strength_input.value) != 2 or shell._create_remaining.text != "3":
		_fail("HK creation stat arrows do not update shared authoritative request state")
		return
	shell._change_creation_stat("strength",-1)
	if int(app.session_panel.character_strength_input.value) != 1 or shell._create_remaining.text != "4":
		_fail("HK creation stat decrement did not restore shared request state")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_create.png"))
	app.session_panel._layout.show_page(Layout.Page.WORLD)
	app._update_ui_stage(true)
	var world = app.classic_world
	var command_expected := {
		"PlayerCommand":[Vector2(3,5),Vector2(24,23)],
		"ItemCommand":[Vector2(3,32),Vector2(24,23)],
		"SkillCommand":[Vector2(3,59),Vector2(24,23)],
		"PartyCommand":[Vector2(2,86),Vector2(24,23)],
		"GuildCommand":[Vector2(3,113),Vector2(24,23)],
		"PlayerShopCommand":[Vector2(4,142),Vector2(21,19)],
		"QuestCommand":[Vector2(2,169),Vector2(25,22)],
		"GmCommand":[Vector2(2,196),Vector2(25,22)],
		"SystemCommand":[Vector2(2,222),Vector2(24,23)],
	}
	if world.command_panel.position != Vector2(770,0) or world.command_panel.size != Vector2(30,249) or world.menu.visible:
		_fail("HK 021_COMMAND global rail geometry drifted or obsolete bottom command bar became visible")
		return
	for control_name in command_expected:
		var command_control := world.secondary_menu.get_node_or_null(control_name) as Control
		var expected: Array = command_expected[control_name]
		if command_control == null or command_control.position != expected[0] or command_control.size != expected[1]:
			_fail("HK 021_COMMAND control rect drifted: "+str(control_name))
			return
	var hotkey_expected:=[
		Rect2(4,18,12,12),Rect2(4,47,12,12),Rect2(4,72,12,12),Rect2(4,99,12,12),
		Rect2(4,126,12,12),Rect2(4,153,12,12),Rect2(4,180,12,12),Rect2(4,207,12,12),
		Rect2(4,234,12,12),Rect2(4,261,18,12),Rect2(4,288,18,12),Rect2(4,315,18,12)]
	if world.hotkey_panel.position != Vector2(2,17) or world.hotkey_panel.size != Vector2(30,358):
		_fail("HK 007_HOTKEY_Yl global geometry drifted")
		return
	for i in 12:
		var label:=world.hotkey_panel.get_node_or_null("Hotkey%d"%i) as RichTextLabel
		if label==null or label.position!=hotkey_expected[i].position or label.size!=hotkey_expected[i].size or label.text!="F%d"%(i+1):
			_fail("HK 007_HOTKEY_Yl F-key text rect drifted at F%d"%(i+1))
			return
	if world.status_panel.position!=Vector2(0,510) or world.status_panel.size!=Vector2(128,90) or \
		world._hud_hp_clip.position!=Vector2(24,20) or world._hud_hp_clip.size.y!=10 or \
		world._hud_mp_clip.position!=Vector2(24,34) or world._hud_wp_clip.position!=Vector2(24,48) or \
		world._hud_exp_clip.position!=Vector2(24,80):
		_fail("HK 006_StatusLite progress/control geometry drifted")
		return
	if (world.dialog_window.get_node("BG_NPC_MESSAGEBackground") as TextureRect).texture==null or \
		world._dialog_text.position!=Vector2(25,10) or world._dialog_text.size!=Vector2(345,80) or \
		world._dialog_choices.position!=Vector2(26,108) or world._dialog_choices.size!=Vector2(345,58):
		_fail("HK 047_NPC_message image/control geometry drifted")
		return
	if (
		not world.visible or app.recovered2_world.visible or
		world.inventory.size != Vector2(259,506) or
		world.character.size != Vector2(263,397) or
		world.skill_window.size != Vector2(223,288) or
		world.party_window.size != Vector2(139,144) or
		world.clan_window.size != Vector2(263,360) or
		world.shop_window.size != Vector2(770,506) or
		world.quest_window.size != Vector2(527,350) or
		world.player_shop_window.size != Vector2(770,506) or
		world.storage_window.size != Vector2(770,506) or
		world.bank_window.size != Vector2(399,172) or
		world.repair_window.size != Vector2(223,506) or
		world.trade_window.size != Vector2(770,506) or
		world.dialog_window.size != Vector2(399,172) or
		world.settings_window.size != Vector2(291,207)
	):
		_fail("original SPR world geometry not applied by independent ClassicWorldShell")
		return
	var system_controls := {
		"CloseWindow":[Vector2(270,1),Vector2(16,16)],
		"QualityDown":[Vector2(144,34),Vector2(17,17)],
		"QualityUp":[Vector2(198,34),Vector2(16,16)],
		"BgmDown":[Vector2(121,69),Vector2(17,17)],
		"BgmUp":[Vector2(175,69),Vector2(16,16)],
		"SfxDown":[Vector2(94,102),Vector2(17,17)],
		"SfxUp":[Vector2(148,102),Vector2(16,16)],
		"BackToServer":[Vector2(27,168),Vector2(85,21)],
		"ExitGame":[Vector2(125,168),Vector2(85,21)],
	}
	for control_name in system_controls:
		var control:=world.settings_window.get_node_or_null(control_name) as Control
		var expected:Array=system_controls[control_name]
		if control==null or control.position!=expected[0] or control.size!=expected[1]:
			_fail("HK 029_SYSTEM control rect drifted: "+str(control_name))
			return
	if (
		(world.settings_window.get_node("BgmLabel") as RichTextLabel).text!="背景音樂音量" or
		(world.settings_window.get_node("SfxLabel") as RichTextLabel).text!="音效音量" or
		(world.settings_window.get_node("QualityLabel") as RichTextLabel).text!="高畫質流暢度" or
		(world.settings_window.get_node("BattleModeLabel") as RichTextLabel).text!="戰鬥模式" or
		(world.settings_window.get_node("BackToServerText") as RichTextLabel).text!="回選擇伺服器" or
		(world.settings_window.get_node("ExitGameText") as RichTextLabel).text!="離開遊戲"
	):
		_fail("HK 029_SYSTEM fixed labels drifted from original text")
		return
	# 018_EMOTION_BIG.ITF is a 242x30 floating two-page picker. The 12
	# selection icons are original 24x24 EMOTIONAL_ICON frames; no substitute art.
	var emotion_bg:=world._chat_emotion_menu.get_node_or_null("BG_EMOTIONBackground") as TextureRect
	var emotion_close:=world._chat_emotion_menu.get_node_or_null("EmotionClose") as Control
	if (
		world._chat_emotion.disabled or world._chat_emotion.pressed.get_connections().is_empty() or
		world._chat_emotion_menu.size!=Vector2(242,30) or emotion_bg==null or emotion_bg.texture==null or
		world._chat_emotion_back.position!=Vector2(3,3) or world._chat_emotion_back.size!=Vector2(14,24) or
		world._chat_emotion_next.position!=Vector2(18,3) or world._chat_emotion_next.size!=Vector2(14,24) or
		emotion_close==null or emotion_close.position!=Vector2(222,6) or emotion_close.size!=Vector2(16,16) or
		world._chat_emotion_choices.size()!=7
	):
		_fail("018_EMOTION_BIG.ITF frame/navigation geometry or trigger wiring drifted")
		return
	for slot in 7:
		var choice:TextureButton=world._chat_emotion_choices[slot]
		if (
			choice.position!=Vector2(33+slot*27,3) or choice.size!=Vector2(24,24) or
			choice.texture_normal==null or choice.pressed.get_connections().is_empty()
		):
			_fail("018_EMOTION_BIG.ITF page-one slot drifted: %d"%slot)
			return
	world._open_chat_emotion_menu()
	if not world._chat_emotion_menu.visible or world._chat_balloon_menu.visible:
		_fail("emotion picker did not open exclusively from chat trigger")
		return
	world._change_chat_emotion_page(1)
	if world._chat_emotion_page!=1 or world._chat_emotion_back.disabled or not world._chat_emotion_next.disabled:
		_fail("emotion picker second-page navigation state drifted")
		return
	for slot in 7:
		var expected_visible:=slot<5
		if world._chat_emotion_choices[slot].visible!=expected_visible:
			_fail("emotion picker second-page 8..12 visibility drifted at slot %d"%slot)
			return
	world._change_chat_emotion_page(-1)
	if world._chat_emotion_page!=0 or not world._chat_emotion_back.disabled or world._chat_emotion_next.disabled:
		_fail("emotion picker first-page navigation state drifted")
		return
	world._close_chat_emotion_menu()
	if world._chat_emotion_menu.visible:
		_fail("emotion picker close handler did not hide source panel")
		return
	app.world_view._chat_balloon_seen.clear()
	app.world_view._chat_balloons.clear()
	app.world_view._consume_chat_balloons({
		"chat_messages":[
			{"channel":0,"sender_actor_id":42,"sender_connection_epoch":3,"chat_sequence":1,
			 "bubble_type":4,"message":"1234567890123456789012345"},
			{"channel":1,"sender_actor_id":43,"sender_connection_epoch":7,"chat_sequence":2,
			 "bubble_type":2,"message":"world"},
			{"channel":2,"sender_actor_id":43,"sender_connection_epoch":7,"chat_sequence":3,
			 "bubble_type":3,"message":"warband"},
			{"channel":4,"sender_actor_id":42,"sender_connection_epoch":3,"chat_sequence":4,
			 "bubble_type":1,"message":"private"},
		]
	},1_000_000)
	app.world_view._consume_chat_balloons({
		"chat_messages":[
			{"channel":0,"sender_actor_id":42,"sender_connection_epoch":3,"chat_sequence":1,
			 "bubble_type":4,"message":"duplicate"}
		]
	},2_000_000)
	if (
		app.world_view._chat_balloons.size()!=3 or
		str(app.world_view._chat_balloons[0].text)!="12345678901234567890…" or
		int(app.world_view._chat_balloons[0].bubble_type)!=4 or
		str(app.world_view._chat_balloons[1].text)!="world" or
		int(app.world_view._chat_balloons[1].bubble_type)!=2 or
		str(app.world_view._chat_balloons[2].text)!="warband" or
		int(app.world_view._chat_balloons[2].bubble_type)!=3
	):
		_fail("original speaking-balloon delivery/truncation/dedupe drifted")
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
		"character_name":"TestA","player_job":"聖靈使","player_profile_ready":true,
		"player_verified_stats_mask":15,
		"player_experience":1234,"player_wisdom":24,"player_dodge":35,
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
		"mecha_snapshot_received":true,"mecha_equipment_snapshot_received":true,
		"mecha_sequence":2,"pending_mecha_sequence":0,"mounted_mecha_uid":0,
		"mecha_instances":{
			70001:{"mecha_uid":70001,"definition_id":8801,"level":3,"experience":44},
			70002:{"mecha_uid":70002,"definition_id":8802,"level":5,"experience":900},
		},
		"mecha_equipment_items":{
			80001:{"mecha_uid":70001,"item_uid":80001,"slot_id":3,"item_definition_id":30002,"durability_raw":77},
		},
		"last_mecha_result":{},
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
		"ignored_friends":{
			45:{"actor_id":45,"name":"黑名單玩家","online":false,"ignored":true},
		},
		"pending_friend_sequence":0,
		"friend_messages":[
			{"channel":6,"peer_actor_id":43,"peer_name":"附近玩家","direction":"incoming",
			 "message":"第一則","local_timestamp":"[ Y2026/M11/D26 06:00:00 ]"},
			{"channel":6,"peer_actor_id":43,"peer_name":"附近玩家","direction":"outgoing",
			 "message":"回覆","local_timestamp":"[ Y2026/M11/D26 06:01:00 ]"},
		],
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
	if world._hp_label.text != "75/100" or world._mp_label.text != "30/40" or world._level_label.text != "22":
		_fail("classic HPBAR did not bind authoritative Server values")
		return
	for spec in [
		["Caption名　稱",Rect2(11,37,36,12),"名　稱"],
		["Field名稱",Rect2(50,37,118,12),"TestA"],
		["Field等級",Rect2(223,31,33,12),"22"],
		["Field智慧",Rect2(40,162,47,10),"24"],
		["Field迴避",Rect2(189,188,62,10),"35"],
		["FieldHP",Rect2(30,268,92,10),"75/100"],
		["FieldMP",Rect2(30,285,92,10),"30/40"],
		["FieldEXP",Rect2(166,285,92,10),"1234 EXP"],
		["Field配點",Rect2(166,268,92,12),"0"],
	]:
		var cell = world.character.get_node_or_null(spec[0])
		if cell == null or cell.get_rect() != spec[1] or cell.text != spec[2]:
			_fail("013_Character_INFO.ITF character row drift: %s"%str(spec[0]))
			return
	var profile_font = world._character_labels["智慧"].text_font as SystemFont
	if (
		world._character_labels["智慧"].font_size != 12 or
		world._character_labels["智慧"].baseline != 10.0 or
		profile_font == null or
		profile_font.font_names != PackedStringArray(["新細明體"]) or
		profile_font.font_weight != 500
	):
		_fail("HK character profile must use SGOClient GDI font geometry")
		return
	if world._character_hp_fill.get_rect() != Rect2(30,268,69,10) or world._character_mp_fill.get_rect() != Rect2(30,285,69,10):
		_fail("013_Character_INFO.ITF HP and MP must fill from server ratios")
		return
	fixture["player_verified_stats_mask"]=0
	world.apply_snapshot(fixture)
	for field in ["攻擊","防禦","命中","迴避"]:
		if str(world._character_labels[field].text)!="—":
			_fail("unverified effective combat field leaked into profile "+field)
			return
	fixture["equipment_items"]={}
	fixture["player_verified_stats_mask"]=13
	world.apply_snapshot(fixture)
	if str(world._character_labels["攻擊"].text)!="188" or \
			str(world._character_labels["命中"].text)!="96" or \
			str(world._character_labels["迴避"].text)!="35" or \
			str(world._character_labels["防禦"].text)!="—":
		_fail("partial verified profile stat mask did not keep Defense fail-closed")
		return
	fixture["equipment_items"]={999:{"item_definition_id":999}}
	world.apply_snapshot(fixture)
	for field in ["攻擊","防禦","命中","迴避"]:
		if field!="防禦" and str(world._character_labels[field].text)!="—":
			_fail("partial verified stat remained visible after unintegrated equipment "+field)
			return
	fixture["equipment_items"]={}
	fixture["player_verified_stats_mask"]=15
	world.apply_snapshot(fixture)
	if not directory.is_empty():
		await _capture(directory.path_join("classic_world.png"))
		world.secondary_menu.get_node("PlayerCommand").pressed.emit()
		await _capture(directory.path_join("classic_character_info.png"))
		world.secondary_menu.get_node("PlayerCommand").pressed.emit()
	if (
		world._chat_history.text.find("你好") < 0 or
		world._friend_rows.get_child_count() != 2 or
		not str(world._friend_rows.get_child(0).text).begins_with("● ") or
		not str(world._friend_rows.get_child(1).text).begins_with("○ ")
	):
		_fail("classic persistent chat/friend panels did not bind authoritative friend presence")
		return
	var mail0:=world.friend_panel.get_node("FriendMail0") as TextureButton
	var mail1:=world.friend_panel.get_node("FriendMail1") as TextureButton
	var mail2:=world.friend_panel.get_node("FriendMail2") as TextureButton
	if (
		mail0==null or mail0.get_rect()!=Rect2(163,36,14,15) or
		mail1==null or mail1.get_rect()!=Rect2(163,53,14,15) or
		mail2==null or mail2.get_rect()!=Rect2(163,70,14,15) or
		mail0.texture_normal==null or not mail0.visible or not mail1.visible or mail2.visible
	):
		_fail("020_FRIENDLIST B_GET_MAIL source rects/art/contact mapping drifted r0=%s r1=%s r2=%s tex=%s vis=%s/%s/%s" % [
			str(mail0.get_rect()) if mail0!=null else "null",
			str(mail1.get_rect()) if mail1!=null else "null",
			str(mail2.get_rect()) if mail2!=null else "null",
			str(mail0.texture_normal!=null) if mail0!=null else "false",
			str(mail0.visible) if mail0!=null else "false",
			str(mail1.visible) if mail1!=null else "false",
			str(mail2.visible) if mail2!=null else "false"])
		return
	mail0.pressed.emit()
	if (
		not world._friend_message_panel.visible or
		world._friend_message_panel.size!=Vector2(256,175) or
		world._friend_message_title.get_rect()!=Rect2(12,4,200,13) or
		world._friend_message_timestamp.get_rect()!=Rect2(12,28,218,13) or
		world._friend_message_counter.get_rect()!=Rect2(129,28,100,13) or
		world._friend_message_direction.get_rect()!=Rect2(12,50,218,13) or
		world._friend_message_history.get_rect()!=Rect2(12,65,218,70) or
		world._friend_message_input.get_rect()!=Rect2(9,155,215,15) or
		world._friend_message_input_aux.get_rect()!=Rect2(227,155,18,15) or
		world._friend_message_up.get_rect()!=Rect2(233,24,17,36) or
		world._friend_message_down.get_rect()!=Rect2(233,111,17,36) or
		str(world._friend_message_title.text)!="傳送訊息到．．．附近玩家 [在線]" or
		str(world._friend_message_timestamp.text)!="[ Y2026/M11/D26 06:01:00 ]" or
		str(world._friend_message_counter.text)!="2/2" or
		str(world._friend_message_direction.text)!="TestA" or
		str(world._friend_message_history.text)!="回覆"
	):
		_fail("043_FRIEND_MSG source geometry/text/latest-message binding drifted")
		return
	world._friend_message_up.pressed.emit()
	if (
		str(world._friend_message_counter.text)!="1/2" or
		str(world._friend_message_direction.text)!="附近玩家" or
		str(world._friend_message_history.text)!="第一則" or
		not world._friend_message_up.disabled or world._friend_message_down.disabled
	):
		_fail("043_FRIEND_MSG message-by-message history navigation drifted")
		return
	world._friend_message_down.pressed.emit()
	if str(world._friend_message_counter.text)!="2/2" or not world._friend_message_down.disabled:
		_fail("043_FRIEND_MSG next-message navigation did not return to latest")
		return
	world._friend_message_input.text="測".repeat(40)
	world._limit_friend_message_input(world._friend_message_input.text)
	if (
		world._friend_message_input.text.to_utf8_buffer().size()>
			world.WireCodec.FRIEND_MESSAGE_MAX_MESSAGE_BYTES or
		world._friend_message_input.text.is_empty()
	):
		_fail("043_FRIEND_MSG did not enforce original 0x65-byte input bound")
		return
	var real_session=app.session_panel.session
	var friend_send:=FriendMessageSession.new()
	app.session_panel.session=friend_send
	world._friend_message_input.text=" 由043送出 "
	world._friend_message_input.text_submitted.emit(world._friend_message_input.text)
	if (
		friend_send.sends!=1 or friend_send.target!=43 or
		friend_send.message!=" 由043送出 " or not world._friend_message_input.text.is_empty()
	):
		_fail("043_FRIEND_MSG Enter did not invoke friend-message session flow")
		return
	app.session_panel.session=real_session
	world._close_friend_message()
	if world._friend_message_panel.visible:
		_fail("043_FRIEND_MSG close handler did not hide source panel")
		return
	var ignore_button:=world.friend_panel.get_node("FriendIgnore") as TextureButton
	var ignore_list_button:=world.friend_panel.get_node("IgnoreList") as TextureButton
	if (
		ignore_button==null or ignore_button.get_rect()!=Rect2(31,7,24,24) or
		ignore_list_button==null or ignore_list_button.get_rect()!=Rect2(154,7,24,24) or ignore_list_button.disabled
	):
		_fail("020_FRIENDLIST original ignore controls are not live at source rects")
		return
	(world._friend_rows.get_child(0) as Button).pressed.emit()
	if ignore_button.disabled or ignore_button.tooltip_text!="移至黑名單":
		_fail("020_FRIENDLIST 2812 did not arm after selecting an authoritative friend")
		return
	ignore_list_button.pressed.emit()
	if (
		world._friend_rows.get_child_count()!=1 or
		str(world._friend_rows.get_child(0).text)!="黑名單玩家" or
		ignore_button.tooltip_text!="解除黑名單"
	):
		_fail("020_FRIENDLIST ignore-list view did not bind authoritative ignored_friends")
		return
	world.friend_panel.get_node("FriendList").pressed.emit()
	if world._friend_rows.get_child_count()!=2 or ignore_button.tooltip_text!="移至黑名單":
		_fail("020_FRIENDLIST friend-list view did not restore source-backed contact view")
		return
	var edit_button:=world.friend_panel.get_node("FriendEdit") as TextureButton
	(world._friend_rows.get_child(0) as Button).pressed.emit()
	if edit_button==null or edit_button.get_rect()!=Rect2(97,7,24,24) or edit_button.disabled:
		_fail("020_FRIENDLIST 2269 rename control is not live at source rect")
		return
	edit_button.pressed.emit()
	if (
		not world._friend_rename.visible or world._friend_rename.size!=Vector2(256,100) or
		world._friend_rename_input.get_rect()!=Rect2(14,53,236,12) or
		world._friend_rename_input.max_length!=10 or
		str(world._friend_rename.get_node("FriendRenamePrompt").text)!="請輸入最新的名字" or
		(world._friend_rename.get_node("FriendRenameConfirm") as TextureButton).get_rect()!=Rect2(163,73,85,21)
	):
		_fail("046_Rename source geometry/text was not opened from 020_FRIENDLIST")
		return
	world._friend_rename.get_node("FriendRenameClose").pressed.emit()
	if world._friend_rename.visible:
		_fail("046_Rename cancel did not close without submitting")
		return
	world.secondary_menu.get_node("ItemCommand").pressed.emit()
	if (
		not world.inventory.visible or
		world.inventory.get_node_or_null("BG_ITEMBackground") == null or
		world._inventory_tab_buttons.size() != 5 or
		world._inventory_rows.get_child_count() != 16
	):
		_fail("original inventory window did not bind 16 HK item rows/tabs on BG_ITEM")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_inventory.png"))
	world._change_inventory_tab(1)
	if not world._inventory_equip_bg.visible or world._inventory_item_bg.visible or world._equipment_rows.get_child_count() != 1:
		_fail("classic equipment tab did not switch to BG_Equip and bind equipped UID")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_equipment.png"))
	world._change_inventory_tab(2)
	var mecha_rows := ""
	for row in world._inventory_rows.get_children():
		if row is Button:
			mecha_rows += (row as Button).text + "\n"
	if world._classic_mecha_uid != 70001 or mecha_rows.find("機甲 8801 Lv.3") < 0 or mecha_rows.find("[強化卡 1]") < 0 or mecha_rows.find("騎乘選中機甲") < 0:
		_fail("classic B_ITEM_ARMOR tab did not bind authoritative mecha roster/equipment/actions")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_mecha.png"))
	world._change_inventory_tab(0)
	world.secondary_menu.get_node("GuildCommand").pressed.emit()
	if not world.clan_window.visible or world._clan_rows.get_child_count() != 2:
		_fail("classic BG_CLUB did not bind clan members")
		return
	world.secondary_menu.get_node("QuestCommand").pressed.emit()
	if not world.quest_window.visible or world._quest_title.text != "失落的星鑰" or world._quest_choices.get_child_count() != 2:
		_fail("classic BG_QUEST_LIST did not bind quest choices")
		return
	world.secondary_menu.get_node("PlayerShopCommand").pressed.emit()
	if not world.player_shop_window.visible or world._player_shop_rows.get_child_count() < 3 or world._player_shop_buy.disabled:
		_fail("classic player-shop presentation is not backed by inventory/nearby/listing snapshot")
		return
	world._toggle(world.storage_window)
	if (
		not world.storage_window.visible or world._storage_rows.get_child_count() != 1 or
		world._storage_inventory_rows.get_child_count() != 17 or
		(world.storage_window.get_node("BG_BANKBackground") as TextureRect).texture == null
	):
		_fail("classic BG_BANK warehouse did not bind storage plus multi-item inventory")
		return
	if not directory.is_empty():
		await _capture(directory.path_join("classic_storage.png"))
	world._toggle(world.bank_window)
	if not world.bank_window.visible or world._bank_status.text.find("25000") < 0:
		_fail("classic bank window did not bind authoritative balance")
		return
	world._toggle(world.repair_window)
	if not world.repair_window.visible or world._repair_rows.get_child_count() != 1:
		_fail("classic repair window did not bind repairable item")
		return
	# Alternative profiles stay installed and can be exposed explicitly for
	# development; HK-only is a player-facing policy, not deletion of switching.
	app.classic_world.set_skin_switch_visible(true)
	if not app.classic_world._settings_picker.visible or app.classic_world._settings_picker.item_count < 2:
		_fail("runtime UI switch capability was removed instead of hidden")
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
	print("CLASSIC_IE_UI_PASS 800x600_original_spr=true hk_only_player_surface=true alternative_picker_hidden=true main_session_shared=true login_select_create_world=true inventory_equipment_mecha_storage_dialog=true lazy_large_windows=true runtime_switch_retained=true")
	quit(0)
