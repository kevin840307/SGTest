extends SceneTree
const ClassicWorldShell = preload("res://ui/classic_world_shell.gd")
const HK_BITMAP := "res://ui/private_ie_hk2/HK_SHARED_UI_FONT.fnt"

func _init() -> void:
	var shell = ClassicWorldShell.new()
	var label = shell._character_labels.get("智慧")
	if label == null:
		_fail("missing Character Profile text cell")
		return
	if label.text_font != shell._character_font:
		_fail("Character Profile fallback font owner drifted")
		return
	if label.raster_font != shell._character_raster_font:
		_fail("Character Profile visible raster is not the shared renderer")
		return
	if label.font_size != 12 or label.baseline != 10.0:
		_fail("Character Profile font geometry drifted")
		return
	if FileAccess.file_exists(HK_BITMAP):
		if not (shell._character_raster_font is FontFile):
			_fail("private HK GDI BMFont exists but Profile still renders through SystemFont")
			return
		var font_file := shell._character_raster_font as FontFile
		if font_file.get_font_name() != "新細明體":
			_fail("HK bitmap font face drifted: " + font_file.get_font_name())
			return
	else:
		var fallback := shell._character_raster_font as SystemFont
		if fallback == null or fallback.font_names != PackedStringArray(["新細明體"]) or fallback.font_weight != 500:
			_fail("Character Profile fallback is not the verified HK GDI face")
			return
	print("CHARACTER_PROFILE_HK_RASTER_PASS private_bitmap=", FileAccess.file_exists(HK_BITMAP), " face=新細明體 height=12 weight=500 baseline=10")
	shell.free()
	quit(0)

func _fail(message:String) -> void:
	push_error(message)
	quit(1)
