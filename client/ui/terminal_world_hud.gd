extends Control
## HUD overlay shown only for the Retro Terminal skin while in-world.
## This is a read-only view plus four UI navigation buttons, never a second
## gameplay/session authority. All actions still run in the existing panels.

signal section_requested(index: int)

var status_panel := PanelContainer.new()
var section_panel := PanelContainer.new()
var actor_label := Label.new()
var world_label := Label.new()
var health_text := Label.new()
var energy_text := Label.new()
var health := ProgressBar.new()
var energy := ProgressBar.new()
var action_buttons: Array[Button] = []
var _displayed_epoch := 0

func _init() -> void:
	name = "TerminalWorldHud"
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	status_panel.name = "TerminalStatus"
	status_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	section_panel.name = "TerminalShortcuts"
	section_panel.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(status_panel)
	add_child(section_panel)
	var status_padding := MarginContainer.new()
	status_padding.add_theme_constant_override("margin_top",10)
	status_padding.add_theme_constant_override("margin_left",8)
	status_padding.add_theme_constant_override("margin_right",8)
	status_padding.add_theme_constant_override("margin_bottom",7)
	status_panel.add_child(status_padding)
	var status_columns := VBoxContainer.new()
	status_columns.add_theme_constant_override("separation",6)
	status_padding.add_child(status_columns)
	var banner := HBoxContainer.new()
	var indicator := Label.new()
	indicator.text = "[ SYS / LIVE ]"
	indicator.add_theme_color_override("font_color",Color("#f4b052"))
	banner.add_child(indicator)
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	banner.add_child(spacer)
	var tag := Label.new()
	tag.text = "SGTEST TERMINAL"
	tag.add_theme_font_size_override("font_size",11)
	banner.add_child(tag)
	status_columns.add_child(banner)
	actor_label.text = "尚未進入世界"
	status_columns.add_child(actor_label)
	health_text.text = "HP  -- / --"
	energy_text.text = "MP  -- / --"
	status_columns.add_child(health_text)
	health.custom_minimum_size.y = 12
	health.show_percentage = false
	health.min_value = 0
	health.max_value = 1
	status_columns.add_child(health)
	status_columns.add_child(energy_text)
	energy.custom_minimum_size.y = 12
	energy.show_percentage = false
	energy.min_value = 0
	energy.max_value = 1
	status_columns.add_child(energy)
	world_label.text = "WORLD -- / EPOCH --"
	world_label.add_theme_font_size_override("font_size",12)
	status_columns.add_child(world_label)
	var actions := HBoxContainer.new()
	actions.add_theme_constant_override("separation",4)
	section_panel.add_child(actions)
	var captions := ["01 背包","02 戰鬥","03 NPC","04 社交"]
	for i in captions.size():
		var button := Button.new()
		button.text = captions[i]
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.custom_minimum_size.x = 75
		button.pressed.connect(_ask_section.bind(i))
		button.focus_mode = Control.FOCUS_NONE
		actions.add_child(button)
		action_buttons.append(button)
	resize_view(Vector2i(1280,720))
	clear()

func _ask_section(index: int) -> void:
	section_requested.emit(index)

func resize_view(size: Vector2i) -> void:
	var width := minf(390.0,maxf(250.0,float(size.x) * 0.43))
	status_panel.position = Vector2(16,64)
	status_panel.size = Vector2(width,202)
	section_panel.position = Vector2(16,maxf(265.0,float(size.y)-68.0))
	section_panel.size = Vector2(width,56)

func set_ui_theme(value: Theme) -> void:
	theme = value

func clear() -> void:
	_displayed_epoch = 0
	actor_label.text = "未接入角色"
	world_label.text = "WORLD -- / EPOCH --"
	health_text.text = "HP -- / --"
	energy_text.text = "MP -- / --"
	health.value = 0
	energy.value = 0

func apply_snapshot(snapshot: Dictionary) -> void:
	if not bool(snapshot.get("world_ready",false)):
		clear()
		return
	var epoch := int(snapshot.get("map_epoch",0))
	if epoch <= 0:
		clear()
		return
	_displayed_epoch = epoch
	actor_label.text = "角色 #%d   /   SERVER AUTHORITATIVE" % int(snapshot.get("actor_id",0))
	world_label.text = "MAP %d   /   EPOCH %d" % [int(snapshot.get("map_id",0)),epoch]
	var max_hp := maxi(1,int(snapshot.get("player_max_hp",0)))
	var max_mp := maxi(1,int(snapshot.get("player_max_mp",0)))
	var hp := clampi(int(snapshot.get("player_current_hp",0)),0,max_hp)
	var mp := clampi(int(snapshot.get("player_current_mp",0)),0,max_mp)
	health.max_value = max_hp
	health.value = hp
	energy.max_value = max_mp
	energy.value = mp
	health_text.text = "HP  %d / %d" % [hp,max_hp]
	energy_text.text = "MP  %d / %d" % [mp,max_mp]
