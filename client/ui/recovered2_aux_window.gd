extends PanelContainer
## Recovered2 floating-window chrome. The owner supplies only presentation content;
## gameplay commands stay in SessionPanel / ClientSession.
signal close_requested

var title_label := Label.new()
var meta_label := Label.new()
var body := VBoxContainer.new()
var close_button := TextureButton.new()

func _init() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	theme_type_variation = &"ParadiseWindowFrame"
	visible = false
	var root := VBoxContainer.new()
	root.name = "WindowLayout"
	root.add_theme_constant_override("separation",8)
	add_child(root)
	var header := HBoxContainer.new()
	header.custom_minimum_size.y = 34
	root.add_child(header)
	title_label.theme_type_variation = &"ParadiseWindowTitle"
	title_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	header.add_child(title_label)
	meta_label.theme_type_variation = &"ParadiseSecondaryText"
	meta_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	header.add_child(meta_label)
	close_button.name = "BtnClose"
	close_button.custom_minimum_size = Vector2(24,24)
	close_button.ignore_texture_size = true
	close_button.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	close_button.focus_mode = Control.FOCUS_NONE
	close_button.pressed.connect(func(): hide(); close_requested.emit())
	header.add_child(close_button)
	var separator := HSeparator.new()
	separator.theme_type_variation = &"ParadiseHeaderSeparator"
	root.add_child(separator)
	body.name = "Body"
	body.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_theme_constant_override("separation",8)
	root.add_child(body)

func configure(window_name: String, title: String, dimensions: Vector2, close_texture: Texture2D) -> void:
	name = window_name
	title_label.text = title
	size = dimensions
	custom_minimum_size = dimensions
	close_button.texture_normal = close_texture

func clear_body() -> void:
	for child in body.get_children():
		body.remove_child(child)
		child.queue_free()

func section(title: String) -> VBoxContainer:
	var frame := PanelContainer.new()
	frame.theme_type_variation = &"ParadiseContentPanel"
	frame.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	frame.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_child(frame)
	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation",6)
	frame.add_child(content)
	if not title.is_empty():
		var label := Label.new()
		label.text = title
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		label.add_theme_color_override("font_color",Color("#50d4e5"))
		label.add_theme_font_size_override("font_size",14)
		content.add_child(label)
	return content
