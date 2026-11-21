extends RefCounted
const UiTextureLoader = preload("res://ui/ui_texture_loader.gd")
## Optional nine-slice image skin adapter. Layout/gameplay handlers do not change.
## Any skin JSON with art.root can provide these image names; missing assets
## simply fall back to the existing StyleBoxFlat palette.

static func _image_style(root: String, name: String, x_margin: float, y_margin: float) -> StyleBoxTexture:
	var file := root.path_join(name)
	var texture = UiTextureLoader.get_texture(file)
	if not texture is Texture2D:
		return null
	var box := StyleBoxTexture.new()
	box.texture = texture
	box.texture_margin_left = x_margin
	box.texture_margin_right = x_margin
	box.texture_margin_top = y_margin
	box.texture_margin_bottom = y_margin
	box.content_margin_left = 8.0
	box.content_margin_right = 8.0
	box.content_margin_top = 5.0
	box.content_margin_bottom = 5.0
	return box

static func artwork_path(profile: Dictionary, name: String) -> String:
	var art: Dictionary = profile.get("art", {})
	var root := str(art.get("root", ""))
	return root.path_join(str(art.get(name,""))) if not root.is_empty() and not str(art.get(name,"")).is_empty() else ""

static func apply_theme(theme: Theme, profile: Dictionary) -> void:
	var art: Dictionary = profile.get("art", {})
	var root := str(art.get("root",""))
	if root.is_empty():
		return
	var normal := _image_style(root,"button_normal.png",12.0,12.0)
	var hovered := _image_style(root,"button_hover.png",12.0,12.0)
	var pressed := _image_style(root,"button_pressed.png",12.0,12.0)
	var disabled := _image_style(root,"button_disabled.png",12.0,12.0)
	var panel := _image_style(root,"window_panel.png",18.0,18.0)
	var field := _image_style(root,"input_field.png",12.0,12.0)
	var track := _image_style(root,"bar_track.png",10.0,8.0)
	var fill := _image_style(root,"bar_fill.png",10.0,8.0)
	if normal == null or hovered == null or pressed == null or disabled == null or panel == null or field == null or track == null or fill == null:
		return
	# Load a platform installed font by name; never bundle OS font files.
	var terminal_font := SystemFont.new()
	terminal_font.font_names = PackedStringArray(["Cascadia Mono","Consolas","Microsoft JhengHei","Noto Sans CJK TC"])
	theme.default_font = terminal_font
	theme.default_font_size = 15
	theme.set_stylebox("panel","PanelContainer",panel)
	theme.set_stylebox("panel","TabContainer",panel)
	theme.set_stylebox("panel","PopupPanel",panel)
	for type in ["Button","OptionButton"]:
		theme.set_stylebox("normal",type,normal)
		theme.set_stylebox("hover",type,hovered)
		theme.set_stylebox("pressed",type,pressed)
		theme.set_stylebox("disabled",type,disabled)
		theme.set_color("font_color",type,Color("f0e6d5"))
		theme.set_color("font_hover_color",type,Color("ffc87a"))
		theme.set_color("font_pressed_color",type,Color("ffd89b"))
		theme.set_color("font_disabled_color",type,Color("77766f"))
	for type in ["LineEdit","SpinBox"]:
		theme.set_stylebox("normal",type,field)
		theme.set_stylebox("focus",type,hovered)
		theme.set_color("font_color",type,Color("eae2d3"))
	theme.set_color("caret_color","LineEdit",Color("f3ab4a"))
	theme.set_stylebox("tab_selected","TabBar",pressed)
	theme.set_stylebox("tab_unselected","TabBar",normal)
	theme.set_stylebox("tab_hovered","TabBar",hovered)
	theme.set_stylebox("tab_disabled","TabBar",disabled)
	theme.set_stylebox("panel","ItemList",panel)
	theme.set_stylebox("selected","ItemList",pressed)
	theme.set_stylebox("selected_focus","ItemList",pressed)
	theme.set_stylebox("cursor","ItemList",hovered)
	theme.set_color("font_color","ItemList",Color("eae2d3"))
	theme.set_color("font_selected_color","ItemList",Color("ffe6b3"))
	theme.set_stylebox("background","ProgressBar",track)
	# Solid pixel fill avoids diagonal stretch artifacts in long HP/MP bars.
	var progress_fill := StyleBoxFlat.new()
	progress_fill.bg_color = Color("e3a04b")
	progress_fill.border_color = Color("ffc06a")
	progress_fill.set_border_width_all(1)
	progress_fill.set_corner_radius_all(1)
	theme.set_stylebox("fill","ProgressBar",progress_fill)
	theme.set_color("font_color","Label",Color("eee6d7"))
	theme.set_color("font_color","PopupMenu",Color("eae2d3"))
	theme.set_stylebox("panel","PopupMenu",panel)
	theme.set_stylebox("hover","PopupMenu",hovered)
