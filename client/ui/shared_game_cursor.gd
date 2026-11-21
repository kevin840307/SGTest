extends CanvasLayer
## One app-owned cursor set shared by ALL UI skins. No per-skin cursor copies.
## Original icon sprites generated in tools/ui_art/build_shared_cursor.py.
##
## Godot handles normal GUI shape selection (hand for buttons, text for
## inputs). Only the world interaction mode uses explicit cursor priority.

const UiTextureLoader = preload("res://ui/ui_texture_loader.gd")
const ROOT := "res://ui/cursors"
const CURSORS := [
	{"file":"arrow.png", "shape":Input.CURSOR_ARROW, "hot":Vector2(3,3)},
	{"file":"point.png", "shape":Input.CURSOR_POINTING_HAND, "hot":Vector2(17,3)},
	{"file":"text.png", "shape":Input.CURSOR_IBEAM, "hot":Vector2(21,18)},
	{"file":"drag.png", "shape":Input.CURSOR_DRAG, "hot":Vector2(21,19)},
	{"file":"attack.png", "shape":Input.CURSOR_CROSS, "hot":Vector2(21,21)},
	{"file":"inspect.png", "shape":Input.CURSOR_HELP, "hot":Vector2(14,14)},
	{"file":"wait.png", "shape":Input.CURSOR_WAIT, "hot":Vector2(21,21)},
	{"file":"forbidden.png", "shape":Input.CURSOR_FORBIDDEN, "hot":Vector2(21,21)},
	{"file":"move.png", "shape":Input.CURSOR_MOVE, "hot":Vector2(21,21)},
]
var _mode := "normal"
var _locked_visual := Sprite2D.new()
var _pointer_hidden := false
var _loaded: Dictionary = {}
var _focus_inside := true

func _init() -> void:
	name = "SharedGameCursor"
	layer = 150
	process_mode = Node.PROCESS_MODE_ALWAYS
	_locked_visual.name = "LockedAutorunCursor"
	_locked_visual.centered = true
	_locked_visual.visible = false
	add_child(_locked_visual)

func _ready() -> void:
	install()
	if not get_tree().node_added.is_connected(_on_node_added):
		get_tree().node_added.connect(_on_node_added)
	_register_controls(get_tree().root)

func _exit_tree() -> void:
	restore_pointer()
	if get_tree().node_added.is_connected(_on_node_added):
		get_tree().node_added.disconnect(_on_node_added)
	# Clearing overridden cursor textures protects editor/test transitions.
	for entry in CURSORS:
		Input.set_custom_mouse_cursor(null,int(entry.shape))
	Input.set_default_cursor_shape(Input.CURSOR_ARROW)
	_locked_visual.texture = null
	_loaded.clear()

func install() -> void:
	for entry in CURSORS:
		var path := ROOT.path_join(str(entry.file))
		var bitmap := UiTextureLoader.get_texture(path)
		if bitmap == null:
			push_warning("Shared cursor icon unavailable: "+path)
			continue
		_loaded[str(entry.file)] = bitmap
		Input.set_custom_mouse_cursor(bitmap,int(entry.shape),entry.hot)
	_locked_visual.texture = _loaded.get("move.png")
	set_mode("normal")

func _register_controls(node: Node) -> void:
	_on_node_added(node)
	for child in node.get_children():
		_register_controls(child)

func _on_node_added(node: Node) -> void:
	# Controls can opt into a different shape later, but all skins share
	# this baseline. Do not touch mouse_filter or click/input handling.
	if node is BaseButton or node is TabBar or node is ItemList:
		node.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	elif node is LineEdit or node is TextEdit:
		node.mouse_default_cursor_shape = Control.CURSOR_IBEAM
	elif node is HSlider or node is VSlider or node is HScrollBar or node is VScrollBar:
		node.mouse_default_cursor_shape = Control.CURSOR_DRAG

func set_mode(value: String) -> void:
	var allowed := ["normal","attack","inspect","pickup","move","wait","forbidden"]
	if value not in allowed:
		value = "normal"
	if _mode == value and (value != "move" or _pointer_hidden):
		return
	_mode = value
	if value == "move" and _focus_inside and _locked_visual.texture != null:
		if Input.mouse_mode != Input.MOUSE_MODE_HIDDEN:
			Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
		_pointer_hidden = true
		_locked_visual.visible = true
	else:
		restore_pointer()
		match value:
			"attack": Input.set_default_cursor_shape(Input.CURSOR_CROSS)
			"inspect": Input.set_default_cursor_shape(Input.CURSOR_HELP)
			"pickup": Input.set_default_cursor_shape(Input.CURSOR_POINTING_HAND)
			"wait": Input.set_default_cursor_shape(Input.CURSOR_WAIT)
			"forbidden": Input.set_default_cursor_shape(Input.CURSOR_FORBIDDEN)
			_: Input.set_default_cursor_shape(Input.CURSOR_ARROW)

func restore_pointer() -> void:
	_locked_visual.hide()
	if _pointer_hidden and Input.mouse_mode == Input.MOUSE_MODE_HIDDEN:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	_pointer_hidden = false

func _process(delta: float) -> void:
	if not _locked_visual.visible:
		return
	_locked_visual.global_position = get_viewport().get_mouse_position()
	_locked_visual.rotation = fposmod(_locked_visual.rotation+delta*4.4,TAU)

func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_WINDOW_FOCUS_OUT:
		_focus_inside = false
		restore_pointer()
	elif what == NOTIFICATION_WM_WINDOW_FOCUS_IN:
		_focus_inside = true
		if _mode == "move":
			_mode = "normal"
			set_mode("move")
