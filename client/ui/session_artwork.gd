extends RefCounted
const UiTextureLoader = preload("res://ui/ui_texture_loader.gd")
## Image-backed ornaments shared across the four session screens.
## The live forms remain owned by SessionPanel: these are never interactive
## substitutes for login, character creation or gameplay commands.

var _rows: Array[HBoxContainer] = []
var _icons: Array[TextureRect] = []
var _titles: Array[Label] = []
var _subtitles: Array[Label] = []
var _previews: Array[TextureRect] = []
var _dividers: Array[TextureRect] = []

const HEADINGS = [
	["SGTEST  /  AUTHENTICATION","存取終端  |  帳號驗證"],
	["CHARACTER  /  DIRECTORY","角色檔案  |  選擇進入世界"],
	["IDENTITY  /  INITIALIZE","角色建立  |  新檔案配置"],
	["WORLD  /  OPS CONSOLE","世界監控  |  線上控制介面"],
]

func setup(layout: VBoxContainer) -> void:
	var pages := [layout.login_page,layout.select_page,layout.create_page,layout.world_page]
	for i in pages.size():
		var page: VBoxContainer = pages[i]
		var header := HBoxContainer.new()
		header.name = "ArtworkHeader"
		header.add_theme_constant_override("separation",14)
		header.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var mark := TextureRect.new()
		mark.name = "TerminalMark"
		mark.custom_minimum_size = Vector2(55,55)
		mark.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		mark.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		mark.mouse_filter = Control.MOUSE_FILTER_IGNORE
		header.add_child(mark)
		var texts := VBoxContainer.new()
		texts.add_theme_constant_override("separation",2)
		texts.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		var title := Label.new()
		title.text = HEADINGS[i][0]
		title.add_theme_font_size_override("font_size",18)
		texts.add_child(title)
		var subtitle := Label.new()
		subtitle.text = HEADINGS[i][1]
		subtitle.add_theme_font_size_override("font_size",12)
		texts.add_child(subtitle)
		header.add_child(texts)
		page.add_child(header)
		page.move_child(header,0)
		_rows.append(header)
		_icons.append(mark)
		_titles.append(title)
		_subtitles.append(subtitle)
		var divider := TextureRect.new()
		divider.name = "ArtworkDivider"
		divider.custom_minimum_size.y = 8
		divider.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		divider.stretch_mode = TextureRect.STRETCH_SCALE
		divider.mouse_filter = Control.MOUSE_FILTER_IGNORE
		page.add_child(divider)
		page.move_child(divider,1)
		_dividers.append(divider)
		if i == 1 or i == 2:
			var preview := TextureRect.new()
			preview.name = "UnboundCharacterPreview"
			preview.custom_minimum_size.y = 118 if i == 1 else 78
			preview.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
			preview.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
			preview.mouse_filter = Control.MOUSE_FILTER_IGNORE
			preview.tooltip_text = "示意識別圖；尚未接入角色外觀／裝備模型"
			page.add_child(preview)
			page.move_child(preview,2)
			_previews.append(preview)
		else:
			_previews.append(null)
	_apply_visibility(false)

func _apply_visibility(enabled: bool) -> void:
	for row in _rows:
		row.visible = enabled
	for divider in _dividers:
		divider.visible = enabled
	for preview in _previews:
		if preview != null:
			preview.visible = enabled

func apply(profile: Dictionary) -> void:
	var art: Dictionary = profile.get("art",{})
	var base := str(art.get("root",""))
	var has_art := not base.is_empty()
	if has_art:
		var logo = UiTextureLoader.get_texture(base.path_join(str(art.get("logo","terminal_mark.png"))))
		var divider = UiTextureLoader.get_texture(base.path_join("divider.png"))
		var identity = UiTextureLoader.get_texture(base.path_join(str(art.get("portrait","identity_scan.png"))))
		if not logo is Texture2D or not divider is Texture2D or not identity is Texture2D:
			has_art = false
		else:
			for icon in _icons:
				icon.texture = logo
			for node in _dividers:
				node.texture = divider
			for preview in _previews:
				if preview != null:
					preview.texture = identity
			for title in _titles:
				title.add_theme_color_override("font_color",Color(str(profile.get("accent","#f4b052"))))
			for subtitle in _subtitles:
				subtitle.add_theme_color_override("font_color",Color(str(profile.get("text","#eee6d7"))).darkened(0.2))
	_apply_visibility(has_art)
