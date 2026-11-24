extends SceneTree
## HK 004_Select_Character.itf slot-art regression.
## Verifies source SPR geometry/visibility without depending on the protected
## login-focus WIP in test_classic_ie_ui.gd.
const Main = preload("res://app/main.gd")
const Layout = preload("res://ui/session_layout.gd")
const State = preload("res://world/world_state.gd")
const Skins = preload("res://ui/ui_skin_catalog.gd")

func _fail(reason: String) -> void:
	push_error("CHARACTER_SELECT_HK_SLOT_ART_FAIL "+reason)
	quit(1)

func _initialize() -> void:
	root.size = Vector2i(800,600)
	_run.call_deferred()

func _run() -> void:
	if not Skins.valid("original_ie_hk2"):
		print("CHARACTER_SELECT_HK_SLOT_ART_SKIP original IE HK2 local assets unavailable")
		quit(0)
		return
	var app = Main.new()
	app.auto_start = false
	root.add_child(app)
	# The project defaults to 1152x620. Lock this visual gate to the HK source
	# resolution after Main has initialized the primary Window, then reflow once.
	root.size = Vector2i(800,600)
	await process_frame
	app._resize()
	var shell = app.classic_auth
	if not FileAccess.file_exists("res://ui/private_ie_hk2/HK_SHARED_UI_FONT.fnt"):
		_fail("private HK shared GDI bitmap font was not generated")
		return
	if shell._shared_ui_font == null:
		_fail("Character Select did not load the HK shared font")
		return
	if abs(shell._shared_ui_font.get_height(12)-12.0) > 0.01 or abs(shell._shared_ui_font.get_ascent(12)-10.0) > 0.01:
		_fail("HK shared font metrics drifted from height=12/ascent=10")
		return
	if not shell._shared_ui_font.has_char("密".unicode_at(0)):
		_fail("HK shared font lost CP950 Chinese glyph coverage")
		return
	app.session_panel.set_process(false)
	app.session_panel.active = true
	app.session_panel.show()
	app.session_panel._latest = {"state":State.STATE_SELECTING_CHARACTER}
	app.auto_start = true
	app._current_skin = "original_ie_hk2"
	app.session_panel._layout.show_page(Layout.Page.CHARACTER_SELECT)
	app.session_panel._characters = [{
		"character_id":42,
		"name":"SlotOne",
		"slot":1,
		"appearance":{"race":0,"hair_index":0,"hair_color":0},
	}]
	app._update_ui_stage(true)
	await process_frame

	if shell._slot_empty_art.size() != 3 or shell._slot_play_art.size() != 3:
		_fail("three HK slot art layers were not built")
		return

	var slot_x := [169.0,358.0,544.0]
	var name_x := [150.0,338.0,525.0]
	if shell._slot_names.size() != 3:
		_fail("three Character Select name text controls were not built")
		return
	for i in 3:
		var name_text = shell._slot_names[i]
		if name_text.position != Vector2(name_x[i],260) or name_text.size != Vector2(124,16):
			_fail("character-name ITF rect drifted at slot %d" % i)
			return
		if name_text.text_font != shell._shared_ui_font or name_text.font_size != 12 or name_text.baseline != 10.0:
			_fail("character-name shared font/baseline drifted at slot %d" % i)
			return

	var expected_fixed := [
		["密碼設定",Vector2(187,424),Vector2(48,12)],
		["進入遊戲",Vector2(187,448),Vector2(48,12)],
		["刪除角色",Vector2(187,472),Vector2(48,12)],
		["密碼設定",Vector2(377,424),Vector2(48,12)],
		["進入遊戲",Vector2(377,448),Vector2(48,12)],
		["刪除角色",Vector2(377,472),Vector2(48,12)],
		["密碼設定",Vector2(563,424),Vector2(48,12)],
		["進入遊戲",Vector2(563,448),Vector2(48,12)],
		["刪除角色",Vector2(563,472),Vector2(48,12)],
		["上一頁",Vector2(569,504),Vector2(36,12)],
	]
	if shell._select_fixed_texts.size() != expected_fixed.size():
		_fail("Character Select fixed text control count drifted")
		return
	for i in expected_fixed.size():
		var label = shell._select_fixed_texts[i]
		var expected = expected_fixed[i]
		if label.text != expected[0] or label.position != expected[1] or label.size != expected[2]:
			_fail("Character Select fixed text rect drifted at index %d" % i)
			return
		if label.text_font != shell._shared_ui_font or label.font_size != 12 or label.baseline != 10.0:
			_fail("Character Select fixed text font/baseline drifted at index %d" % i)
			return

	var expected_modal_fixed := [
		["角色密碼",Vector2(274,308),Vector2(48,12)],
		["確認",Vector2(418,331),Vector2(24,12)],
		["取消",Vector2(513,331),Vector2(24,12)],
		["原有密碼",Vector2(284,272),Vector2(48,12)],
		["新的密碼",Vector2(284,296),Vector2(48,12)],
		["確認密碼",Vector2(283,320),Vector2(48,12)],
		["確認",Vector2(402,349),Vector2(24,12)],
		["取消",Vector2(494,349),Vector2(24,12)],
	]
	if shell._modal_fixed_texts.size() != expected_modal_fixed.size():
		_fail("password modal HK fixed text control count drifted")
		return
	for i in expected_modal_fixed.size():
		var label = shell._modal_fixed_texts[i]
		var expected = expected_modal_fixed[i]
		if label.text != expected[0] or label.position != expected[1] or label.size != expected[2]:
			_fail("password modal fixed text rect drifted at index %d" % i)
			return
		if label.text_font != shell._shared_ui_font or label.font_size != 12 or label.baseline != 10.0:
			_fail("password modal fixed text shared font/baseline drifted at index %d" % i)
			return

	var expected_create_fixed := [
		["名稱",Vector2(169,261),Vector2(24,12)],
		["種族",Vector2(169,285),Vector2(24,12)],
		["頭部",Vector2(169,309),Vector2(24,12)],
		["頭部顏色",Vector2(157,333),Vector2(48,12)],
		["上身顏色",Vector2(157,357),Vector2(48,12)],
		["手袖顏色",Vector2(157,381),Vector2(48,12)],
		["下身顏色",Vector2(157,405),Vector2(48,12)],
		["剩餘點數",Vector2(498,357),Vector2(48,12)],
		["力量",Vector2(510,261),Vector2(24,12)],
		["體質",Vector2(510,309),Vector2(24,12)],
		["速度",Vector2(510,285),Vector2(24,12)],
		["智慧",Vector2(510,333),Vector2(24,12)],
		["角色密碼",Vector2(498,391),Vector2(48,12)],
		["確認密碼",Vector2(498,415),Vector2(48,12)],
		["建立角色",Vector2(497,475),Vector2(48,12)],
		["上一頁",Vector2(608,475),Vector2(36,12)],
	]
	if shell._create_fixed_texts.size() != expected_create_fixed.size():
		_fail("Create Character HK fixed text control count drifted")
		return
	for i in expected_create_fixed.size():
		var label = shell._create_fixed_texts[i]
		var expected = expected_create_fixed[i]
		if label.text != expected[0] or label.position != expected[1] or label.size != expected[2]:
			_fail("Create Character fixed text rect drifted at index %d" % i)
			return
		if label.text_font != shell._shared_ui_font or label.font_size != 12 or label.baseline != 10.0:
			_fail("Create Character fixed text shared font/baseline drifted at index %d" % i)
			return

	for i in 3:
		var empty_art: TextureRect = shell._slot_empty_art[i]
		var play_art: TextureRect = shell._slot_play_art[i]
		if empty_art.position != Vector2(slot_x[i],284) or empty_art.size != Vector2(85,132):
			_fail("NEW_CHARACTER frame geometry drifted at slot %d: %s %s" % [i,str(empty_art.position),str(empty_art.size)])
			return
		if empty_art.texture == null or empty_art.texture.get_size() != Vector2(85,132):
			_fail("NEW_CHARACTER source frame dimensions drifted at slot %d" % i)
			return
		if play_art.position != Vector2(slot_x[i]+8,385) or play_art.size != Vector2(67,23):
			_fail("PLAY_GAME origin geometry drifted at slot %d: %s %s" % [i,str(play_art.position),str(play_art.size)])
			return
		if play_art.texture == null or play_art.texture.get_size() != Vector2(67,23):
			_fail("PLAY_GAME source frame dimensions drifted at slot %d" % i)
			return
		var occupied := i == 1
		if play_art.visible != occupied or empty_art.visible == occupied:
			_fail("occupied/empty slot art visibility drifted at slot %d" % i)
			return

	var args := OS.get_cmdline_user_args()
	if not args.is_empty():
		await RenderingServer.frame_post_draw
		var capture_path := str(args[0])
		if root.get_texture().get_image().save_png(capture_path) != OK:
			_fail("800x600 capture write failed: "+capture_path)
			return
	print("CHARACTER_SELECT_HK_SLOT_ART_PASS itf_hitbox=84x130 new_character=85x132 play_game=67x23 origin=-8,-101 font=新細明體 height=12 weight=500 baseline=10 modal_create_fixed_text=true")
	quit(0)
