extends Node2D
## Draw an already ordered presentation snapshot; texture references are shared.
## Indexed palette variants are baked off the main thread and cached per map.
const PaletteHue = preload("res://player/palette_hue.gd")
const UiTextureLoader = preload("res://ui/ui_texture_loader.gd")
const HK_MISS_TEXTURE := "res://ui/private_ie_hk2/MISS.png"
const HK_MISS_ORIGIN := Vector2(-7.0,0.0)
const HK_RIDING_BG := "res://ui/private_ie_hk2/BG_DRIVE_COMPLEX_BAR.png"
const HK_RIDING_WP_PREFIX := "res://ui/private_ie_hk2/WP_"
const HK_RIDING_RECT := Rect2(10,5,77,3)

var _commands: Array = []
var _ground_markers: Array = []
var _combat_labels: Array = []
var _player_labels: Array = []
var _chat_bubbles: Array = []
var _mecha_energy_bars: Array = []
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
		combat_labels: Array = [], player_labels: Array = [],
		chat_bubbles: Array = [], mecha_energy_bars: Array = []) -> void:
	_commands = commands
	_ground_markers = ground_markers
	_combat_labels = combat_labels
	_player_labels = player_labels
	_chat_bubbles = chat_bubbles
	_mecha_energy_bars = mecha_energy_bars
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
	_chat_bubbles = []
	_mecha_energy_bars = []
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

func _draw_texture_for_color(source: Texture2D, color_index: int) -> Texture2D:
	# A palette is purely cosmetic. Even if get_image() returns null, a worker
	# stalls, or a new animation page has not been baked yet, render the SOURCE.
	# Never hide the entire player because one hair/cloth tint is unavailable.
	var baked := _palette_texture(source,color_index)
	return baked if baked != null else source


func _draw() -> void:
	for command in _commands:
		var destination: Rect2 = command.destination
		var palette_index := int(command.get("palette_index",0))
		var source: Texture2D = command.texture
		var texture: Texture2D = _draw_texture_for_color(source,palette_index)
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

	# Players and NPCs use one batched world-space Canvas pass. With width=-1
	# Godot doesn't center text using HORIZONTAL_ALIGNMENT_CENTER; offset by
	# the actual font width and follow the center of the sprite's visible bounds.
	var font := ThemeDB.fallback_font
	for label in _player_labels:
		var name_position: Vector2 = label.p
		var name_text := str(label.text)
		var name_color: Color = label.color
		if name_text.is_empty():
			continue
		var font_size := 16
		var width := font.get_string_size(name_text,HORIZONTAL_ALIGNMENT_LEFT,-1,font_size).x
		var baseline_y := name_position.y
		if bool(label.get("recovered_player_anchor",false)):
			# Recovered player.tscn: NameLabel top=-130, bottom=-107,
			# vertical_alignment=CENTER. Reproduce that fixed child-node geometry
			# with our batched Canvas text instead of sampling sprite bounds.
			var rect_top := name_position.y-130.0
			var rect_height := 23.0
			baseline_y = rect_top+(rect_height-font.get_height(font_size))*0.5+font.get_ascent(font_size)
		var at := Vector2(name_position.x-width*0.5,baseline_y)
		draw_string(font,at+Vector2(1,1),name_text,
			HORIZONTAL_ALIGNMENT_LEFT,-1,16,Color(0.0,0.0,0.0,0.92))
		draw_string(font,at,name_text,HORIZONTAL_ALIGNMENT_LEFT,-1,16,name_color)

	# CRidingTPItf::OnUpdate (Paradise.exe 0x0049DCEC) anchors the riding
	# progress surface to the local player's world position:
	# x = worldX - GetNameStrlen()/2 - 30, y = worldY - 149.
	# 087_DRIVE_COMPLEX_BAR.itf then places WP at local 10,5,77x3.
	for bar in _mecha_energy_bars:
		var feet:Vector2=bar.get("p",Vector2.INF)
		var maximum:=int(bar.get("maximum",0))
		if not feet.is_finite() or maximum<=0:
			continue
		var riding_bg:=UiTextureLoader.get_texture(HK_RIDING_BG)
		if riding_bg==null:
			continue
		var riding_name:=str(bar.get("name",""))
		var name_width:=font.get_string_size(
			riding_name,HORIZONTAL_ALIGNMENT_LEFT,-1,16).x
		var top_left:=Vector2(feet.x-name_width*0.5-30.0,feet.y-149.0)
		draw_texture(riding_bg,top_left)
		var ratio:=clampf(float(int(bar.get("current",0)))/float(maximum),0.0,1.0)
		if ratio<=0.0:
			continue
		var wp_index:=int(Time.get_ticks_msec()/70)%18
		var wp:=UiTextureLoader.get_texture(HK_RIDING_WP_PREFIX+str(wp_index)+".png")
		if wp==null:
			continue
		var dest:=Rect2(
			top_left+HK_RIDING_RECT.position,
			Vector2(HK_RIDING_RECT.size.x*ratio,HK_RIDING_RECT.size.y))
		var source:=Rect2(
			Vector2.ZERO,
			Vector2(float(wp.get_width())*ratio,float(wp.get_height())))
		draw_texture_rect_region(wp,dest,source,Color.WHITE,false,true)

	# Original speaking-balloon styles are private HK source sprites. They are
	# drawn in the same world Canvas so they follow interpolation without adding
	# a Label/Sprite node per chat delivery.
	for bubble in _chat_bubbles:
		var style:=clampi(int(bubble.get("style",1)),1,7)
		var bubble_texture:=UiTextureLoader.get_texture(
			"res://ui/private_ie_hk2/BG_BUBBLE_S%02d.png"%style)
		if bubble_texture==null:
			continue
		var anchor:Vector2=bubble.p
		var bubble_size:=Vector2(bubble_texture.get_width(),bubble_texture.get_height())
		var top_left:=Vector2(anchor.x-bubble_size.x*0.5,anchor.y-bubble_size.y)
		draw_texture(bubble_texture,top_left)
		var remaining:=str(bubble.get("text",""))
		var lines:Array[String]=[]
		while not remaining.is_empty() and lines.size()<3:
			var take:=mini(10,remaining.length())
			lines.append(remaining.substr(0,take))
			remaining=remaining.substr(take)
		for line_index in lines.size():
			var line:=lines[line_index]
			var line_width:=font.get_string_size(line,HORIZONTAL_ALIGNMENT_LEFT,-1,12).x
			var line_at:=Vector2(
				top_left.x+(bubble_size.x-line_width)*0.5,
				top_left.y+18.0+14.0*line_index)
			draw_string(font,line_at+Vector2(1,1),line,
				HORIZONTAL_ALIGNMENT_LEFT,-1,12,Color(1.0,1.0,1.0,0.75))
			draw_string(font,line_at,line,
				HORIZONTAL_ALIGNMENT_LEFT,-1,12,Color(0.22,0.12,0.06,1.0))

	# Reliable combat presentation is still rendered by this shared Canvas.
	# No Label/Sprite Node is allocated per attack, so dense fights do not
	# multiply scene-tree resources. HK MISS is a source SPR, not text.
	for label in _combat_labels:
		var position: Vector2 = label.p
		var alpha := clampf(float(label.alpha),0.0,1.0)
		var text := str(label.text)
		if bool(label.miss):
			var miss_texture := UiTextureLoader.get_texture(HK_MISS_TEXTURE)
			if miss_texture != null:
				# Source frame is 45x35 with raw origin (-7,0). Match the same
				# position-origin rule used by world_draw_list for original SPRs.
				draw_texture(miss_texture,position-HK_MISS_ORIGIN,Color(1.0,1.0,1.0,alpha))
				continue
		var foreground := Color(1.0,0.92,0.25,alpha) if bool(label.miss) else Color(1.0,1.0,1.0,alpha)
		var shadow := Color(0.0,0.0,0.0,0.9*alpha)
		draw_string(font,position+Vector2(1,1),text,HORIZONTAL_ALIGNMENT_CENTER,-1,18,shadow)
		draw_string(font,position,text,HORIZONTAL_ALIGNMENT_CENTER,-1,18,foreground)
