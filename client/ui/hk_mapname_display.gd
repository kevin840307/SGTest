extends CanvasLayer
## UI-045: HK place-name banner. WorldView supplies the accepted world-map
## manifest name and authoritative MapEpoch; this node never decides a map.
const TextureLoader = preload("res://ui/ui_texture_loader.gd")
const BACKGROUND := "res://ui/private_ie_hk2/BG_MAPNAME_DISPLAY.png"
const ORIGINAL_SIZE := Vector2(346, 19)
# Old CPlaceNameItf constructor 0x0045066B sets [this+0x140]=7;
# OnUpdate 0x0045070A multiplies that value by 1000 milliseconds.
const ORIGINAL_LIFETIME_SECONDS := 7.0

var _banner := Control.new()
var _art := TextureRect.new()
var _text_area := Control.new()
var _caption := Label.new()
var _remaining_seconds := 0.0
var _map_id := 0
var _map_epoch := 0

func _init() -> void:
	name = "HKMapNameDisplay"
	layer = 2
	_banner.name = "PlaceNameBanner"
	_banner.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_banner.anchor_left = 0.5
	_banner.anchor_right = 0.5
	_banner.offset_left = -ORIGINAL_SIZE.x * 0.5
	_banner.offset_right = ORIGINAL_SIZE.x * 0.5
	_banner.offset_top = 0.0
	_banner.offset_bottom = ORIGINAL_SIZE.y
	_banner.hide()
	add_child(_banner)
	_art.name = "BG_MAPNAME_DISPLAY"
	_art.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_art.stretch_mode = TextureRect.STRETCH_KEEP
	_art.size = ORIGINAL_SIZE
	_banner.add_child(_art)
	# HK 045_Mapname_Display.itf: ID 2076, rect=(18,4,308,11).
	# Label's native font minimum height exceeds the original 11px control.
	# Clip the actual ITF rect instead of allowing a Container to enlarge it.
	_text_area.name = "PlaceName_2076"
	_text_area.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_text_area.clip_contents = true
	_text_area.position = Vector2(18, 4)
	_text_area.size = Vector2(308, 11)
	_banner.add_child(_text_area)
	_caption.name = "PlaceNameText"
	_caption.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_caption.position = Vector2.ZERO
	_caption.size = Vector2(308, 11)
	_caption.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_caption.vertical_alignment = VERTICAL_ALIGNMENT_TOP
	_caption.clip_text = true
	_caption.add_theme_font_size_override("font_size", 10)
	_text_area.add_child(_caption)
	set_process(false)

func show_source_name(display_name: String, map_id: int, map_epoch: int) -> bool:
	var name_text := display_name.strip_edges()
	if name_text.is_empty() or map_id <= 0 or map_epoch <= 0:
		return false
	if _map_id == map_id and _map_epoch == map_epoch:
		# Network/AOI snapshots must not restart the original seven-second timer.
		return false
	var texture := TextureLoader.get_texture(BACKGROUND)
	if texture == null or texture.get_size() != ORIGINAL_SIZE:
		# A missing original SPR cannot be replaced with a synthetic rectangle.
		return false
	_art.texture = texture
	_caption.text = name_text
	_map_id = map_id
	_map_epoch = map_epoch
	_remaining_seconds = ORIGINAL_LIFETIME_SECONDS
	_banner.show()
	set_process(true)
	return true

func clear_notice() -> void:
	_banner.hide()
	_caption.text = ""
	_map_id = 0
	_map_epoch = 0
	_remaining_seconds = 0.0
	set_process(false)

func _process(delta: float) -> void:
	if not _banner.visible:
		set_process(false)
		return
	_remaining_seconds = maxf(0.0, _remaining_seconds - maxf(0.0, delta))
	if _remaining_seconds <= 0.0:
		_banner.hide()
		set_process(false)
