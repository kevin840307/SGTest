extends SceneTree
## Cross-screen skin switch is presentation-only and preserves command widgets.
const Main = preload("res://app/main.gd")
const Skins = preload("res://ui/ui_skin_catalog.gd")
const State = preload("res://world/world_state.gd")

func _fail(app: Node, reason: String) -> void:
	if app != null:
		app.queue_free()
	push_error("UI_SKIN_LAYOUT_FAIL " + reason)
	quit(1)

func _initialize() -> void:
	root.size = Vector2i(1280,720)
	_run.call_deferred()

func _run() -> void:
	var app = Main.new()
	app.auto_start = false
	root.add_child(app)
	var panel = app.session_panel
	panel.show()
	# Enable the normal app layout after scene setup; the offline test does
	# not require a private PCK or a network Server.
	app.auto_start = true
	app._update_ui_stage(true)
	# Headless Godot may force a 64px viewport: check the layout policy,
	# rather than assuming a desktop-sized window in automated CI.
	if not app._screen_backdrop.visible or app.toolbar.visible or panel.position.y < 24 or panel.custom_minimum_size.x < 360:
		_fail(app,"login/select/create screens must be full-viewport centered")
		return
	var layout = panel._layout
	var session = panel.session
	var controls = [panel.inventory_panel, panel.chat_panel, panel.npc_shop_panel,
		panel.trade_panel, panel.quest_panel, panel.skill_area_button]
	if app.skin_picker.item_count != 3 or layout.group_count() != 4:
		_fail(app,"skin picker or gameplay group registry missing")
		return
	var palette_colors := []
	for skin in Skins.available():
		if not Skins.apply(str(skin.id),app.toolbar,panel):
			_fail(app,"failed to apply skin " + str(skin.id))
			return
		var surface: StyleBoxFlat = panel.get_theme_stylebox("panel")
		palette_colors.append(surface.bg_color)
		if layout.layout_id != str(skin.layout) or panel.session != session:
			_fail(app,"layout did not change atomically or session was replaced")
			return
		var expected_parent: Control = layout.world_tabs if layout.layout_id == "tabs" else layout.world_stack
		for control in controls:
			if control.get_parent().get_parent() != expected_parent:
				_fail(app,"gameplay control lost original parent binding")
				return
	if palette_colors[0] == palette_colors[1] or palette_colors[1] == palette_colors[2] or palette_colors[0] == palette_colors[2]:
		_fail(app,"all palettes must be distinct")
		return
	if Skins.apply("unknown",app.toolbar,panel):
		_fail(app,"unknown skin unexpectedly accepted")
		return

	# The same actual controls are used by four independent screens.
	if not layout.login_page.visible or layout.select_page.visible or layout.create_page.visible or layout.world_page.visible:
		_fail(app,"initial login screen not isolated")
		return
	panel.active = true
	panel._characters = [{"character_id":3,"name":"Hero","slot":0}]
	panel._latest = {"state": State.STATE_SELECTING_CHARACTER}
	panel._set_controls()
	if not layout.select_page.visible or layout.create_page.visible or not panel.new_character_button.visible:
		_fail(app,"character select page not available")
		return
	panel.new_character_button.pressed.emit()
	if not layout.create_page.visible or layout.select_page.visible or not panel.character_name_input.is_visible_in_tree():
		_fail(app,"separate character creation page not accessible")
		return
	panel.back_to_character_button.pressed.emit()
	if not layout.select_page.visible:
		_fail(app,"back from creation did not restore selection")
		return
	panel._latest = {"state": State.STATE_IN_WORLD}
	panel._set_controls()
	if not layout.world_page.visible or layout.login_page.visible or layout.create_page.visible:
		_fail(app,"world HUD/tools not isolated")
		return
	app._update_ui_stage(true)
	if app._screen_backdrop.visible or not app.toolbar.visible or panel.position.x != 16.0:
		_fail(app,"world must restore map viewport and docked game UI")
		return
	panel.gameplay_hud.apply_snapshot({"world_ready":true,"actor_id":3,"map_id":15,"map_epoch":2,
		"player_max_hp":100,"player_current_hp":75,"player_max_mp":60,"player_current_mp":40})
	if not panel.gameplay_hud.hp.visible or panel.gameplay_hud.hp.value != 75 or panel.gameplay_hud.mp.value != 40:
		_fail(app,"world HUD not bound to authoritative HP/MP snapshot")
		return
	if not Skins.apply("amber",app.toolbar,panel) or panel.gameplay_hud.hp.value != 75:
		_fail(app,"changing world layout reset the live HUD")
		return
	if not Skins.apply("tactical",app.toolbar,panel):
		_fail(app,"tactical skin not applicable")
		return
	app._current_skin = "tactical"
	app._update_ui_stage(true)
	if Skins.dock("tactical") != "right" or panel.position.x != maxf(16.0,root.size.x-panel.custom_minimum_size.x-16.0):
		_fail(app,"tactical world UI must support alternate dock arrangement")
		return
	panel.active = false
	panel._set_controls()
	if not layout.login_page.visible:
		_fail(app,"logout did not return login screen")
		return
	app.queue_free()
	await process_frame
	print("UI_SKIN_LAYOUT_PASS four_screens=true three_palettes=true two_layouts=true no_session_rebuild=true gameplay_panels_preserved=true")
	quit(0)
