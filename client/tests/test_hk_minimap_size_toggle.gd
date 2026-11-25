extends SceneTree
## UI-008_MINIMAP_L/S: source-backed long/compact native ITF bounds.
## Presentation-only regression; no map scale or Server actor markers invented.
const Shell = preload("res://ui/classic_world_shell.gd")
var failed := false

func _init() -> void:
	call_deferred("_run")

func _run() -> void:
	# Private HK SPR frame exports remain local to authorized installations.
	# A public CI checkout cannot claim an original-art PASS without them.
	var sources := ["MINIMAP_L.png","MINIMAP_S.png","BG_MINIMAP_POINT.png"]
	for art in ["MINISCREEN","ZONESCREEN"]:
		for frame in 4:
			sources.append("%s_%d.png" % [art,frame])
	for source in sources:
		if not FileAccess.file_exists("res://ui/private_ie_hk2/"+source):
			print("HK_MINIMAP_TOGGLE_SKIP original HK private source missing: "+source)
			quit(0)
			return
	var shell := Shell.new()
	root.add_child(shell)
	var panel := shell.minimap_panel
	var art := panel.get_node("MiniMap") as TextureRect
	var points := panel.get_node("MinimapPointsArt") as TextureRect
	var toggle := panel.get_node("MinimapSizeToggle") as TextureButton
	_expect(not shell._minimap_compact and panel.size == Vector2(133,122),
		"full source background and state")
	_expect(art.texture != null and art.texture.get_size() == Vector2(133,122),
		"full source SPR 133x122")
	_expect(points.texture != null and points.texture.get_size() == Vector2(116,82),
		"source-only compass overlay frame present")
	_expect(Rect2(points.position,points.size) == Rect2(8,37,115,81) and points.visible,
		"full ITF 2138 points overlay local rect")
	_expect(Rect2(toggle.position,toggle.size) == Rect2(115,1,17,17),
		"full ITF 2538 hitbox")
	var anchored := Vector2(48,64)
	panel.position = anchored
	for _cycle in 3:
		toggle.pressed.emit()
		_expect(shell._minimap_compact and panel.size == Vector2(133,20),
			"compact source geometry")
		_expect(art.texture != null and art.texture.get_size() == Vector2(133,20) and
			art.size == Vector2(133,20) and not points.visible,
			"compact source draws no full-mode map pixels")
		_expect(toggle.tooltip_text == "放大地圖" and panel.position == anchored,
			"compact action hint and draggable anchor")
		_check_frames(shell,toggle,"ZONESCREEN")
		toggle.pressed.emit()
		_expect(not shell._minimap_compact and panel.size == Vector2(133,122) and
			points.visible and panel.position == anchored,
			"full geometry and overlay restore without moving user panel")
		_expect(art.texture != null and art.texture.get_size() == Vector2(133,122) and
			art.size == Vector2(133,122) and toggle.tooltip_text == "縮小地圖",
			"full original source restore")
		_check_frames(shell,toggle,"MINISCREEN")
		_expect(Rect2(toggle.position,toggle.size) == Rect2(115,1,17,17),
			"switch button retains same original hitbox")
	shell.free()
	if not failed:
		print("HK_MINIMAP_TOGGLE_PASS original_long_short_art=true source_points=true source_4frame_buttons=true cycles=3 drag_anchor=true")
	quit(1 if failed else 0)

func _check_frames(shell: Control, button: TextureButton, art: String) -> void:
	var sources := [button.texture_normal,button.texture_hover,
		button.texture_pressed,button.texture_disabled]
	for i in 4:
		_expect(sources[i] != null and sources[i] == shell._tex("%s_%d" % [art,i]) and
			sources[i].get_size() == Vector2(17,17),
			"%s frame %d has exact source art and size" % [art,i])

func _expect(ok: bool, reason: String) -> void:
	if not ok:
		failed = true
		push_error("HK_MINIMAP_TOGGLE_FAIL " + reason)
