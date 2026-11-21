extends RefCounted
## Plug-in UI skin catalog. New palettes are data-only JSON files under skins/.
## UI ownership, networking, command callbacks and input bindings never change
## when a style or layout changes.

const SkinArtwork = preload("res://ui/skin_artwork.gd")
const CONFIG_PATH := "user://ui_skin.cfg"
const SKIN_DIRECTORY := "res://ui/skins"
const DEFAULT_SKIN := "retro_terminal"
const COLOR_FIELDS := ["bg","border","text","field","button","hover","accent"]

static func _profiles() -> Array[Dictionary]:
	var profiles: Array[Dictionary] = []
	var files := DirAccess.get_files_at(SKIN_DIRECTORY)
	files.sort()
	for file in files:
		if not file.ends_with(".json"):
			continue
		var id := file.trim_suffix(".json")
		if not id.is_valid_filename():
			continue
		var value = JSON.parse_string(FileAccess.get_file_as_string(
			SKIN_DIRECTORY.path_join(file)))
		if not value is Dictionary:
			continue
		if bool(value.get("requires_local_assets",false)) and not FileAccess.file_exists(str(value.get("source_theme",""))):
			continue
		if str(value.get("id","")) != id or str(value.get("name","")).is_empty() or str(value.get("layout","")) not in ["tabs","stacked"]:
			continue
		var valid_colors := true
		for field in COLOR_FIELDS:
			if not value.get(field) is String or not Color.html_is_valid(str(value[field])):
				valid_colors = false
				break
		if valid_colors:
			profiles.append(value)
	return profiles

static func available() -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	for skin in _profiles():
		result.append({"id":skin.id,"name":skin.name,"layout":skin.layout})
	return result

static func valid(id: String) -> bool:
	for skin in _profiles():
		if str(skin.id) == id:
			return true
	return false

static func load_selected() -> String:
	var config := ConfigFile.new()
	if config.load(CONFIG_PATH) != OK:
		return DEFAULT_SKIN
	# Migrate older palette-only selections to the new pictured UI baseline.
	# After the user selects a skin in the new version, persist it normally.
	if int(config.get_value("ui","skin_schema",0)) < 2:
		return DEFAULT_SKIN
	var chosen := str(config.get_value("ui","skin",DEFAULT_SKIN))
	return chosen if valid(chosen) else DEFAULT_SKIN

static func save_selected(id: String) -> Error:
	if not valid(id):
		return ERR_INVALID_PARAMETER
	var config := ConfigFile.new()
	config.set_value("ui","skin",id)
	config.set_value("ui","skin_schema",2)
	return config.save(CONFIG_PATH)

static func _palette(id: String) -> Dictionary:
	var profiles := _profiles()
	for skin in profiles:
		if str(skin.id) == id:
			return skin
	for skin in profiles:
		if str(skin.id) == DEFAULT_SKIN:
			return skin
	return profiles[0] if not profiles.is_empty() else {}

static func dock(id: String) -> String:
	var requested := str(_palette(id).get("dock","left"))
	return requested if requested in ["left","right"] else "left"

static func backdrop_color(id: String) -> Color:
	return Color(str(_palette(id).get("bg","#171d36"))).darkened(0.55)

static func backdrop_path(id: String) -> String:
	return SkinArtwork.artwork_path(_palette(id),"backdrop")

static func backdrop_motif(id: String) -> int:
	match str(_palette(id).get("effect","stars")):
		"nebula": return 1
		"grid": return 2
		_: return 0

static func _box(background: Color, border: Color, width: int, radius: int, margin: float) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = background
	style.border_color = border
	style.set_border_width_all(width)
	style.set_corner_radius_all(radius)
	style.set_content_margin_all(margin)
	return style

static func build_theme(id: String) -> Theme:
	var colors := _palette(id)
	var background := Color(str(colors.bg))
	var border := Color(str(colors.border))
	var text := Color(str(colors.text))
	var field := Color(str(colors.field))
	var button := Color(str(colors.button))
	var hover := Color(str(colors.hover))
	var accent := Color(str(colors.accent))
	var theme := Theme.new()
	theme.set_color("font_color","Label",text)
	theme.set_color("font_color","Button",text)
	theme.set_color("font_hover_color","Button",accent)
	theme.set_color("font_pressed_color","Button",accent)
	theme.set_color("font_disabled_color","Button",text.darkened(0.40))
	theme.set_color("font_color","OptionButton",text)
	theme.set_color("font_hover_color","OptionButton",accent)
	theme.set_color("font_color","LineEdit",text)
	theme.set_color("font_placeholder_color","LineEdit",text.darkened(0.30))
	theme.set_color("font_color","SpinBox",text)
	theme.set_color("font_color","CheckButton",text)
	theme.set_stylebox("panel","PanelContainer",_box(background,border,1,5,12.0))
	for control in ["Button","OptionButton"]:
		theme.set_stylebox("normal",control,_box(button,border.darkened(0.4),1,3,5.0))
		theme.set_stylebox("hover",control,_box(hover,accent,1,3,5.0))
		theme.set_stylebox("pressed",control,_box(field,accent,2,3,5.0))
		theme.set_stylebox("disabled",control,_box(field,background,1,3,5.0))
		theme.set_stylebox("focus",control,_box(Color.TRANSPARENT,accent,1,3,0.0))
	theme.set_stylebox("normal","LineEdit",_box(field,border.darkened(0.5),1,2,4.0))
	theme.set_stylebox("focus","LineEdit",_box(Color.TRANSPARENT,accent,1,2,0.0))
	theme.set_stylebox("read_only","LineEdit",_box(background,background,1,2,4.0))
	theme.set_stylebox("panel","PopupMenu",_box(background,border,1,3,6.0))
	SkinArtwork.apply_theme(theme,colors)
	return theme

static func apply(id: String, toolbar: PanelContainer, session_panel: PanelContainer) -> bool:
	if not valid(id):
		return false
	var colors := _palette(id)
	if not session_panel.set_ui_layout(str(colors.layout)):
		return false
	# External Recovered2 Theme is scoped to the three local auth scenes;
	# never replace SGTest's unrelated in-world gameplay controls with it.
	var theme := build_theme(id)
	toolbar.theme = theme
	session_panel.theme = theme
	var background := Color(str(colors.bg))
	var border := Color(str(colors.border))
	# Existing outer shells had local overrides; replace both. Inner controls
	# inherit Theme so every screen and gameplay tab uses this single profile.
	var outer := SkinArtwork._image_style(str(colors.get("art",{}).get("root","")),"window_panel.png",18.0,18.0) if colors.has("art") else null
	toolbar.add_theme_stylebox_override("panel",outer if outer != null else _box(background,border,1,2,8.0))
	session_panel.add_theme_stylebox_override("panel",outer if outer != null else _box(background,border,2,4,16.0))
	session_panel.apply_skin_art(colors)
	return true
