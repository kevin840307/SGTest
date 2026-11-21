extends Control
## Recovered2-compatible loading presentation. Server/Game state is not owned here.
## Progress is a bounded estimate derived from real AssetStore readiness,
## not from a fake timer or from received AOI entity counts.
const TextureLoader = preload("res://ui/ui_texture_loader.gd")
const LOCAL_BACKGROUND := "res://ui/private_recovered2/BG_LOADING.png"

var _panel := PanelContainer.new()
var _text := Label.new()
var _detail := Label.new()
var _progress := ProgressBar.new()
var _bar_art := TextureRect.new()
var _open_since := 0
var _current_map := 0
var _state := ""
var _display_progress := 0.0
var _last_info := ""
var _last_stage := ""

func _init() -> void:
	name = "WorldLoadingOverlay"
	mouse_filter = Control.MOUSE_FILTER_STOP
	visible = false
	_panel.name = "LoadingCard"
	add_child(_panel)
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.016,0.04,0.08,0.95)
	style.border_color = Color(0.28,0.73,0.9,0.94)
	style.set_border_width_all(2)
	style.set_corner_radius_all(9)
	style.shadow_color = Color(0.005,0.02,0.05,0.8)
	style.shadow_size = 14
	style.content_margin_left = 22
	style.content_margin_right = 22
	style.content_margin_top = 14
	style.content_margin_bottom = 18
	_panel.add_theme_stylebox_override("panel",style)
	var content := VBoxContainer.new()
	content.name = "LoadingContent"
	content.add_theme_constant_override("separation",9)
	_panel.add_child(content)
	_text.text = "星夢 Online  |  世界載入中"
	_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_text.add_theme_color_override("font_color",Color("#c9f1ff"))
	_text.add_theme_font_size_override("font_size",18)
	content.add_child(_text)
	var status := HBoxContainer.new()
	content.add_child(status)
	var stage_tag := Label.new()
	stage_tag.text = "ASSET / WORLD"
	stage_tag.add_theme_color_override("font_color",Color("#60bedb"))
	stage_tag.add_theme_font_size_override("font_size",11)
	status.add_child(stage_tag)
	_detail.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_detail.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_detail.add_theme_color_override("font_color",Color("#e2ebf6"))
	_detail.add_theme_font_size_override("font_size",12)
	status.add_child(_detail)
	var bar_slot := Control.new()
	bar_slot.name = "LoadingBar"
	bar_slot.custom_minimum_size.y = 48
	content.add_child(bar_slot)
	_bar_art.name = "OriginalLoadingBarArtwork"
	_bar_art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_bar_art.stretch_mode = TextureRect.STRETCH_SCALE
	_bar_art.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# Delay private-art allocation until a real map is entering. Offline
	# source asset checks should not allocate the login/loading image.
	_bar_art.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	bar_slot.add_child(_bar_art)
	_progress.name = "LoadingProgress"
	_progress.min_value = 0
	_progress.max_value = 100
	_progress.show_percentage = false
	_progress.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_progress.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_progress.offset_left = 23
	_progress.offset_right = -23
	_progress.offset_top = 18
	_progress.offset_bottom = -18
	var track := StyleBoxFlat.new()
	track.bg_color = Color(0.025,0.09,0.15,0.96)
	track.set_corner_radius_all(3)
	var fill := StyleBoxFlat.new()
	fill.bg_color = Color(0.18,0.67,0.85,1)
	fill.border_color = Color("#c3f4ff")
	fill.set_border_width_all(1)
	fill.set_corner_radius_all(3)
	_progress.add_theme_stylebox_override("background",track)
	_progress.add_theme_stylebox_override("fill",fill)
	bar_slot.add_child(_progress)
	var foot := Label.new()
	foot.name = "LoadingAdvice"
	foot.text = "紋理／地圖在背景準備，角色及 AOI 狀態以伺服器為準"
	foot.add_theme_color_override("font_color",Color("#7fadd0"))
	foot.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	foot.add_theme_font_size_override("font_size",11)
	content.add_child(foot)
	resize_view(Vector2(1152,620))

func resize_view(view: Vector2) -> void:
	size = view
	var width := minf(660.0,maxf(280.0,view.x-32.0))
	var height := minf(165.0,maxf(110.0,view.y-32.0))
	_panel.size = Vector2(width,height)
	_panel.position = (view-_panel.size)*0.5

func update_state(phase: String, map_id: int, ready: int, total: int, decoding: int, textures: int, show: bool) -> void:
	var loading := show and phase in ["map","palette"]
	if not loading:
		if visible:
			visible = false
		_state = ""
		_current_map = 0
		_display_progress = 0.0
		return
	if _bar_art.texture == null:
		_bar_art.texture = TextureLoader.get_texture(LOCAL_BACKGROUND)
		_bar_art.visible = _bar_art.texture != null
	if phase != _state or map_id != _current_map:
		_state = phase
		if map_id != _current_map:
			_display_progress = 0.0
			_open_since = Time.get_ticks_msec()
		_current_map = map_id
	visible = true
	var target := 8.0 if phase == "map" else 15.0 + float(clampi(ready,0,maxi(total,1))) / float(maxi(total,1)) * 80.0
	_display_progress = maxf(_display_progress,minf(95.0,target))
	_progress.value = _display_progress
	var stage := "讀取地圖" if phase == "map" else "解碼／紋理"
	_text.text = "星夢 Online  |  地圖 %d  %s" % [map_id,stage]
	var elapsed := maxi(0,Time.get_ticks_msec()-_open_since)
	_detail.text = "%d / %d 素材  •  解碼 %d  貼圖 %d  •  %.1f 秒" % [ready,total,decoding,textures,float(elapsed)/1000.0]

func progress_value() -> float:
	return _progress.value
