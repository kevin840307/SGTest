extends Node2D
## Draw an already ordered presentation snapshot; texture references are shared.
var _commands: Array = []
var _ground_markers: Array = []
var _combat_labels: Array = []
const DROP_OUTLINE := Color(0.12,0.08,0.02,0.9)
const DROP_CORE := Color(1.0,0.82,0.22,0.95)


func present(commands: Array, ground_markers: Array = [], combat_labels: Array = []) -> void:
	_commands = commands
	_ground_markers = ground_markers
	_combat_labels = combat_labels
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	queue_redraw()


func clear() -> void:
	_commands = []
	_ground_markers = []
	_combat_labels = []
	queue_redraw()


func _draw() -> void:
	for command in _commands:
		var destination: Rect2 = command.destination
		if destination.size.x < 0:
			# draw_texture_rect_region normalizes negative destination sizes;
			# use a Canvas transform to mirror UVs as well as SPR geometry.
			draw_set_transform(Vector2(destination.position.x*2,0),0,Vector2(-1,1))
			destination.size.x = -destination.size.x
			draw_texture_rect_region(command.texture,destination,command.uv,command.modulate,false,true)
			draw_set_transform(Vector2.ZERO)
		else:
			draw_texture_rect_region(command.texture,destination,command.uv,command.modulate,false,true)

	# Lightweight evidence-only markers until source-verified item atlases
	# are available. They reuse the existing Canvas draw call; no extra
	# per-drop Sprite2D/Texture/Node resources are allocated.
	for drop in _ground_markers:
		var position: Vector2 = drop.p
		draw_circle(position, 6.0, DROP_OUTLINE)
		draw_circle(position, 4.0, DROP_CORE)

	# Reliable combat presentation is still rendered by this shared Canvas.
	# No Label/Sprite Node is allocated per attack, so dense fights do not
	# multiply scene-tree resources.
	var font := ThemeDB.fallback_font
	for label in _combat_labels:
		var position: Vector2 = label.p
		var alpha := clampf(float(label.alpha),0.0,1.0)
		var text := str(label.text)
		var foreground := Color(1.0,0.92,0.25,alpha) if bool(label.miss) else Color(1.0,1.0,1.0,alpha)
		var shadow := Color(0.0,0.0,0.0,0.9*alpha)
		draw_string(font,position+Vector2(1,1),text,HORIZONTAL_ALIGNMENT_CENTER,-1,18,shadow)
		draw_string(font,position,text,HORIZONTAL_ALIGNMENT_CENTER,-1,18,foreground)
