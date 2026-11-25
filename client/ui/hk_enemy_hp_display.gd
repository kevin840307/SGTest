extends CanvasLayer
## UI-084: HK enemy HP art and controls. This is presentation-only:
## WorldView feeds validated monster AOI HP, never client combat estimates.
const TextureLoader = preload("res://ui/ui_texture_loader.gd")
const BACKGROUND := "res://ui/private_ie_hk2/BG_ENEMY_HP.png"
const HP_FILL := "res://ui/private_ie_hk2/HP_00.png"
const FRAME_SIZE := Vector2(75, 21)
const HP_RECT := Rect2(20, 5, 49, 5)
const BADGE_RECT := Rect2(5, 3, 14, 14)

var _panel := Control.new()
var _background := TextureRect.new()
var _bar := TextureProgressBar.new()
var _badge := TextureRect.new()

func _init() -> void:
	name = "HKEnemyHPDisplay"
	layer = 2
	_panel.name = "EnemyHP_084"
	_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_panel.size = FRAME_SIZE
	_panel.hide()
	add_child(_panel)

	_background.name = "BG_ENEMY_HP"
	_background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_background.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_background.stretch_mode = TextureRect.STRETCH_KEEP
	_background.size = FRAME_SIZE
	_panel.add_child(_background)

	_bar.name = "EnemyHP_2147"
	_bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_bar.position = HP_RECT.position
	_bar.size = HP_RECT.size
	_bar.min_value = 0
	_bar.max_value = 1
	_bar.value = 0
	_panel.add_child(_bar)

	_badge.name = "EnemyLevel_2146"
	_badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_badge.position = BADGE_RECT.position
	_badge.size = BADGE_RECT.size
	_badge.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_badge.stretch_mode = TextureRect.STRETCH_KEEP
	_panel.add_child(_badge)

static func badge_index(enemy_level: int, player_level: int) -> int:
	# Old Paradise.exe.lst CSGOEnemHPItf::OnUpdate 0x49E2E5..0x49E392.
	if enemy_level <= 0 or player_level <= 0:
		return -1
	var difference := enemy_level - player_level
	if difference >= 4:
		return 0
	if difference > 0:
		return 1
	if difference >= -3:
		return 2
	if difference >= -6:
		return 3
	return 4

func show_health(screen_position: Vector2, current_hp: int, max_hp: int,
		enemy_level: int, player_level: int) -> bool:
	if not screen_position.is_finite() or max_hp <= 0 or current_hp <= 0 or current_hp > max_hp:
		clear_target()
		return false
	var background := TextureLoader.get_texture(BACKGROUND)
	var bar_image := TextureLoader.get_texture(HP_FILL)
	if background == null or bar_image == null or background.get_size() != FRAME_SIZE or bar_image.get_size() != Vector2(107, 10):
		clear_target()
		return false
	_background.texture = background
	_bar.texture_progress = bar_image
	_bar.max_value = max_hp
	_bar.value = current_hp
	var index := badge_index(enemy_level, player_level)
	_badge.visible = index >= 0
	if index >= 0:
		var badge := TextureLoader.get_texture("res://ui/private_ie_hk2/B_ENEMY_HP_%02d.png" % index)
		if badge == null or badge.get_size() != BADGE_RECT.size:
			clear_target()
			return false
		_badge.texture = badge
	_panel.position = screen_position
	_panel.show()
	return true

func clear_target() -> void:
	_panel.hide()
	_badge.texture = null
