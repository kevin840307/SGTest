extends CanvasLayer
## App-owned cursor presentation shared by every SGTest UI skin.
##
## When the local Recovered reference pack is present, use the exact animated
## cursor frames from C:\\SGO\\Recovered\\client\\ui\\cursor. Recovered hides
## the native pointer and draws an AnimatedSprite2D, so SGTest does the same.
## The generated res://ui/cursors images remain only as a portable fallback.

const UiTextureLoader = preload("res://ui/ui_texture_loader.gd")
const RECOVERED_ROOT := "res://ui/private_recovered2/cursor"
const FALLBACK_ROOT := "res://ui/cursors"
const ANIMATION_SPECS := {
	"normal": {"prefix":"C_NORMAL.spr.", "count":10, "fps":8.0},
	"attack": {"prefix":"C_ATTACK.spr.", "count":6, "fps":8.0},
	"check": {"prefix":"C_CHECK.spr.", "count":6, "fps":8.0},
	"pick": {"prefix":"C_TAKE.spr.", "count":5, "fps":8.0},
	"player": {"prefix":"C_PLAYER.spr.", "count":6, "fps":8.0},
	"skill": {"prefix":"C_SKILL.spr.", "count":6, "fps":8.0},
}
const AUTORUN_PATH := RECOVERED_ROOT + "/C_AUTOWALK.spr.png"
const AUTORUN_FRAME_COUNT := 10
const AUTORUN_FRAME_SIZE := Vector2i(32,32)
const NATIVE_SHAPES := [
	Input.CURSOR_ARROW,
	Input.CURSOR_POINTING_HAND,
	Input.CURSOR_IBEAM,
	Input.CURSOR_DRAG,
	Input.CURSOR_CROSS,
	Input.CURSOR_HELP,
	Input.CURSOR_WAIT,
	Input.CURSOR_FORBIDDEN,
	Input.CURSOR_MOVE,
]

var _mode := "normal"
var _cursor_visual := AnimatedSprite2D.new()
var _loaded: Dictionary = {}
var _focus_inside := true
var _using_recovered_cursor := false
var _popup_depth := 0

func _init() -> void:
	name = "SharedGameCursor"
	layer = 4096
	process_mode = Node.PROCESS_MODE_ALWAYS
	_cursor_visual.name = "RecoveredCursorSprite"
	_cursor_visual.centered = false
	_cursor_visual.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	_cursor_visual.visible = false
	add_child(_cursor_visual)

func _ready() -> void:
	install()
	if not get_tree().node_added.is_connected(_on_node_added):
		get_tree().node_added.connect(_on_node_added)
	_register_controls(get_tree().root)

func _exit_tree() -> void:
	if get_tree().node_added.is_connected(_on_node_added):
		get_tree().node_added.disconnect(_on_node_added)
	_clear_native_overrides()
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	Input.set_default_cursor_shape(Input.CURSOR_ARROW)
	_cursor_visual.hide()
	_cursor_visual.sprite_frames = null
	_loaded.clear()

func install() -> void:
	_loaded.clear()
	var frames := _build_recovered_frames()
	if frames != null and frames.has_animation("normal") and frames.get_frame_count("normal") == 10:
		_using_recovered_cursor = true
		_cursor_visual.sprite_frames = frames
		_install_native_popup_fallback(frames.get_frame_texture("normal",0))
		_apply_mode()
		return
	_using_recovered_cursor = false
	_install_generated_fallback()

func using_recovered_cursor() -> bool:
	return _using_recovered_cursor

func _build_recovered_frames() -> SpriteFrames:
	var frames := SpriteFrames.new()
	if frames.has_animation("default"):
		frames.remove_animation("default")
	for animation_name in ANIMATION_SPECS:
		var spec: Dictionary = ANIMATION_SPECS[animation_name]
		var textures: Array[Texture2D] = []
		for frame_index in range(int(spec.count)):
			var path := "%s/%s%04d.png" % [RECOVERED_ROOT,str(spec.prefix),frame_index]
			var texture := UiTextureLoader.get_texture(path)
			if texture == null:
				return null
			textures.append(texture)
		frames.add_animation(animation_name)
		frames.set_animation_loop(animation_name,true)
		frames.set_animation_speed(animation_name,float(spec.fps))
		for texture in textures:
			frames.add_frame(animation_name,texture)
			_loaded["%s/%d" % [animation_name,frames.get_frame_count(animation_name)-1]] = texture

	var autorun_atlas := UiTextureLoader.get_texture(AUTORUN_PATH)
	if autorun_atlas == null:
		return null
	if autorun_atlas.get_width() != AUTORUN_FRAME_COUNT * AUTORUN_FRAME_SIZE.x or autorun_atlas.get_height() != AUTORUN_FRAME_SIZE.y:
		push_warning("Recovered auto-walk cursor must be 320x32: "+AUTORUN_PATH)
		return null
	frames.add_animation("autorun")
	frames.set_animation_loop("autorun",true)
	frames.set_animation_speed("autorun",10.0)
	for frame_index in range(AUTORUN_FRAME_COUNT):
		var frame_texture := AtlasTexture.new()
		frame_texture.atlas = autorun_atlas
		frame_texture.region = Rect2(
			Vector2(frame_index * AUTORUN_FRAME_SIZE.x,0),
			Vector2(AUTORUN_FRAME_SIZE))
		frames.add_frame("autorun",frame_texture)
		_loaded["autorun/%d" % frame_index] = frame_texture
	return frames

func _install_native_popup_fallback(normal_texture: Texture2D) -> void:
	# PopupMenu owns a separate Window/Viewport. Recovered renders another game
	# cursor there; SGTest uses the same C_NORMAL frame as the native fallback
	# while the popup is open so the pointer never disappears behind the popup.
	for shape in NATIVE_SHAPES:
		Input.set_custom_mouse_cursor(normal_texture,int(shape),Vector2.ZERO)
	Input.set_default_cursor_shape(Input.CURSOR_ARROW)

func _install_generated_fallback() -> void:
	var fallback := {
		Input.CURSOR_ARROW:"arrow.png",
		Input.CURSOR_POINTING_HAND:"point.png",
		Input.CURSOR_IBEAM:"text.png",
		Input.CURSOR_DRAG:"drag.png",
		Input.CURSOR_CROSS:"attack.png",
		Input.CURSOR_HELP:"inspect.png",
		Input.CURSOR_WAIT:"wait.png",
		Input.CURSOR_FORBIDDEN:"forbidden.png",
		Input.CURSOR_MOVE:"move.png",
	}
	for shape in fallback:
		var path := FALLBACK_ROOT.path_join(str(fallback[shape]))
		var texture := UiTextureLoader.get_texture(path)
		if texture == null:
			push_warning("Fallback cursor icon unavailable: "+path)
			continue
		_loaded["fallback/"+str(fallback[shape])] = texture
		Input.set_custom_mouse_cursor(texture,int(shape))
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	Input.set_default_cursor_shape(Input.CURSOR_ARROW)

func _clear_native_overrides() -> void:
	for shape in NATIVE_SHAPES:
		Input.set_custom_mouse_cursor(null,int(shape))

func _register_controls(node: Node) -> void:
	_on_node_added(node)
	for child in node.get_children():
		_register_controls(child)

func _on_node_added(node: Node) -> void:
	# Keep semantic shapes for the generated fallback. With the Recovered
	# cursor active these shapes all resolve to the same C_NORMAL popup frame.
	if node is BaseButton or node is TabBar or node is ItemList:
		node.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	elif node is LineEdit or node is TextEdit:
		node.mouse_default_cursor_shape = Control.CURSOR_IBEAM
	elif node is HSlider or node is VSlider or node is HScrollBar or node is VScrollBar:
		node.mouse_default_cursor_shape = Control.CURSOR_DRAG
	if node is PopupMenu and not node.has_meta("_sgtest_cursor_bound"):
		node.set_meta("_sgtest_cursor_bound",true)
		node.about_to_popup.connect(_on_popup_opened)
		node.popup_hide.connect(_on_popup_closed)

func set_mode(value: String) -> void:
	if value not in ["normal","attack","inspect","pickup","move","wait","forbidden","player","skill"]:
		value = "normal"
	_mode = value
	_apply_mode()

func _animation_for_mode() -> String:
	match _mode:
		"attack":
			return "attack"
		"inspect":
			return "check"
		"pickup":
			return "pick"
		"move":
			return "autorun"
		"player":
			return "player"
		"skill":
			return "skill"
		_:
			return "normal"

func _native_shape_for_mode() -> int:
	# One tested mapping serves the portable nine-icon fallback on systems
	# without the private Recovered animated sprite assets.
	match _mode:
		"attack": return Input.CURSOR_CROSS
		"inspect": return Input.CURSOR_HELP
		"pickup": return Input.CURSOR_POINTING_HAND
		"move": return Input.CURSOR_MOVE
		"wait": return Input.CURSOR_WAIT
		"forbidden": return Input.CURSOR_FORBIDDEN
		_: return Input.CURSOR_ARROW

func _apply_mode() -> void:
	if not _using_recovered_cursor:
		Input.set_default_cursor_shape(_native_shape_for_mode())
		return
	if not _focus_inside or _popup_depth > 0:
		_cursor_visual.hide()
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		Input.set_default_cursor_shape(Input.CURSOR_ARROW)
		return
	Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
	_cursor_visual.show()
	var animation_name := _animation_for_mode()
	if not _cursor_visual.sprite_frames.has_animation(animation_name):
		animation_name = "normal"
	if _cursor_visual.animation != animation_name or not _cursor_visual.is_playing():
		_cursor_visual.play(animation_name)

func restore_pointer() -> void:
	_cursor_visual.hide()
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

func _process(_delta: float) -> void:
	if _using_recovered_cursor and _cursor_visual.visible:
		_cursor_visual.position = get_viewport().get_mouse_position()

func _on_popup_opened() -> void:
	_popup_depth += 1
	_apply_mode()

func _on_popup_closed() -> void:
	_popup_depth = maxi(0,_popup_depth-1)
	_apply_mode()

func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_WINDOW_FOCUS_OUT:
		_focus_inside = false
		restore_pointer()
	elif what == NOTIFICATION_WM_WINDOW_FOCUS_IN:
		_focus_inside = true
		_apply_mode()
