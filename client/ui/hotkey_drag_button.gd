extends Button

var hotkey_drag_payload:Dictionary={}

func _get_drag_data(_at_position:Vector2):
	if hotkey_drag_payload.is_empty():
		return null
	var preview:=Label.new()
	preview.text=text.strip_edges()
	preview.add_theme_font_size_override("font_size",10)
	set_drag_preview(preview)
	return hotkey_drag_payload.duplicate(true)
