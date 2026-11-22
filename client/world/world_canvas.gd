extends Node2D
## Draw an already ordered presentation snapshot; texture references are shared.
## Indexed palette variants are baked off the main thread and cached per map.
const PaletteHue = preload("res://player/palette_hue.gd")

var _commands: Array = []
var _ground_markers: Array = []
var _combat_labels: Array = []
var _player_labels: Array = []
var _palette_textures: Dictionary = {}
var _palette_jobs: Dictionary = {}
var _palette_generation := 1
const DROP_OUTLINE := Color(0.12,0.08,0.02,0.9)
const DROP_CORE := Color(1.0,0.82,0.22,0.95)

class PaletteJob extends RefCounted:
	var key := ""
	var generation := 0
	var color_index := 0
	var image: Image
	var task := -1
	var result: Image

	func run() -> void:
		result = PaletteHue.shift_image(image,color_index)

func _ready() -> void:
	set_process(false)

func present(
		commands: Array, ground_markers: Array = [],
		combat_labels: Array = [], player_labels: Array = []) -> void:
	_commands = commands
	_ground_markers = ground_markers
	_combat_labels = combat_labels
	_player_labels = player_labels
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	for command in _commands:
		_request_palette(
			command.get("texture"),
			int(command.get("palette_index",0)))
	queue_redraw()

func clear() -> void:
	_commands = []
	_ground_markers = []
	_combat_labels = []
	_player_labels = []
	_palette_generation += 1
	_palette_textures.clear()
	# Active WorkerThreadPool tasks cannot be cancelled. Keep stale jobs until
	# completion, then discard by generation without creating GPU textures.
	set_process(not _palette_jobs.is_empty())
	queue_redraw()

func _palette_key(source: Texture2D, color_index: int) -> String:
	return "%s:%d" % [str(source.get_rid().get_id()),color_index]

func _job_key(source: Texture2D, color_index: int) -> String:
	return "%d:%s" % [_palette_generation,_palette_key(source,color_index)]

func _request_palette(source: Variant, color_index: int) -> void:
	if not source is Texture2D or color_index <= 0:
		return
	var index := clampi(color_index,0,PaletteHue.HUE_DEGREES.size()-1)
	var key := _palette_key(source,index)
	if _palette_textures.has(key) or _palette_jobs.has(_job_key(source,index)):
		return
	var image: Image = source.get_image()
	if image == null or image.is_empty():
		return
	var job := PaletteJob.new()
	job.key = key
	job.generation = _palette_generation
	job.color_index = index
	job.image = image
	job.task = WorkerThreadPool.add_task(job.run)
	_palette_jobs[_job_key(source,index)] = job
	set_process(true)

func _process(_delta: float) -> void:
	var changed := false
	for job_key in _palette_jobs.keys().duplicate():
		var job: PaletteJob = _palette_jobs[job_key]
		if not WorkerThreadPool.is_task_completed(job.task):
			continue
		WorkerThreadPool.wait_for_task_completion(job.task)
		_palette_jobs.erase(job_key)
		if job.generation != _palette_generation or job.result == null or job.result.is_empty():
			continue
		_palette_textures[job.key] = ImageTexture.create_from_image(job.result)
		changed = true
	if changed:
		queue_redraw()
	if _palette_jobs.is_empty():
		set_process(false)

func _palette_texture(source: Texture2D, color_index: int) -> Texture2D:
	if source == null or color_index <= 0:
		return source
	var index := clampi(color_index,0,PaletteHue.HUE_DEGREES.size()-1)
	return _palette_textures.get(_palette_key(source,index),null)

func palette_texture_count() -> int:
	return _palette_textures.size()

func palette_pending_count() -> int:
	var count := 0
	for job in _palette_jobs.values():
		if job.generation == _palette_generation:
			count += 1
	return count

func _draw() -> void:
	# Never flash a player in the wrong palette while an async color variant
	# is being prepared. Hide the whole actor until every requested layer is ready.
	var pending_actors := {}
	for command in _commands:
		var palette_index := int(command.get("palette_index",0))
		if palette_index <= 0:
			continue
		var source: Texture2D = command.texture
		if _palette_texture(source,palette_index) == null:
			var actor_id := str(command.get("actor_id",""))
			if not actor_id.is_empty():
				pending_actors[actor_id] = true

	for command in _commands:
		var actor_id := str(command.get("actor_id",""))
		if not actor_id.is_empty() and pending_actors.has(actor_id):
			continue
		var destination: Rect2 = command.destination
		var palette_index := int(command.get("palette_index",0))
		var texture: Texture2D = command.texture
		if palette_index > 0:
			var shifted := _palette_texture(texture,palette_index)
			if shifted == null:
				continue
			texture = shifted
		if destination.size.x < 0:
			# draw_texture_rect_region normalizes negative destination sizes;
			# use a Canvas transform to mirror UVs as well as SPR geometry.
			draw_set_transform(Vector2(destination.position.x*2,0),0,Vector2(-1,1))
			destination.size.x = -destination.size.x
			draw_texture_rect_region(texture,destination,command.uv,command.modulate,false,true)
			draw_set_transform(Vector2.ZERO)
		else:
			draw_texture_rect_region(texture,destination,command.uv,command.modulate,false,true)

	# Lightweight evidence-only markers until source-verified item atlases
	# are available. They reuse the existing Canvas draw call; no extra
	# per-drop Sprite2D/Texture/Node resources are allocated.
	for drop in _ground_markers:
		var position: Vector2 = drop.p
		draw_circle(position, 6.0, DROP_OUTLINE)
		draw_circle(position, 4.0, DROP_CORE)

	# Names are one batched Canvas pass, not one Label Node per player.
	var font := ThemeDB.fallback_font
	for label in _player_labels:
		var name_position:Vector2=label.p
		var name_text:=str(label.text)
		var name_color:Color=label.color
		draw_string(font,name_position+Vector2(1,1),name_text,
			HORIZONTAL_ALIGNMENT_CENTER,-1,16,Color(0.0,0.0,0.0,0.92))
		draw_string(font,name_position,name_text,
			HORIZONTAL_ALIGNMENT_CENTER,-1,16,name_color)

	# Reliable combat presentation is still rendered by this shared Canvas.
	# No Label/Sprite Node is allocated per attack, so dense fights do not
	# multiply scene-tree resources.
	for label in _combat_labels:
		var position: Vector2 = label.p
		var alpha := clampf(float(label.alpha),0.0,1.0)
		var text := str(label.text)
		var foreground := Color(1.0,0.92,0.25,alpha) if bool(label.miss) else Color(1.0,1.0,1.0,alpha)
		var shadow := Color(0.0,0.0,0.0,0.9*alpha)
		draw_string(font,position+Vector2(1,1),text,HORIZONTAL_ALIGNMENT_CENTER,-1,18,shadow)
		draw_string(font,position,text,HORIZONTAL_ALIGNMENT_CENTER,-1,18,foreground)
