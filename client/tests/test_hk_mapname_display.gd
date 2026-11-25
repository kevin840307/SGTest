extends SceneTree
## UI-045: source art/rect, one announcement per authoritative map epoch,
## expiry, and early teardown. This is a diagnostic, not native-player Gate.
const WorldView = preload("res://world/world_view.gd")
var failures: Array[String] = []

func _initialize() -> void:
	run.call_deferred()

func check(condition: bool, detail: String) -> void:
	if not condition:
		failures.append(detail)
		push_error("HK_MAPNAME_DISPLAY_FAIL " + detail)

func source_name(map_id: int) -> String:
	var path := "res://assets/maps/%d/world.json" % map_id
	var source: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	return str(source.get("name", "")) if source is Dictionary else ""

func run() -> void:
	var view := WorldView.new()
	root.add_child(view)
	var notice = view.map_name_display
	check(notice._banner.visible == false, "new scene is not hidden")
	check(notice._banner.mouse_filter == Control.MOUSE_FILTER_IGNORE, "banner steals map clicks")
	check(notice._caption.mouse_filter == Control.MOUSE_FILTER_IGNORE, "label steals map clicks")
	check(notice._text_area.position == Vector2(18, 4) and notice._text_area.size == Vector2(308, 11) and notice._text_area.clip_contents, "HK ITF control 2076 rect")
	check(notice._banner.offset_left == -173.0 and notice._banner.offset_right == 173.0, "HK sprite top-centre width")
	check(notice.show_source_name("unverified", 0, 1) == false and not notice._banner.visible, "invalid MapId must not show")
	view.map_id = 13
	view.show_players = true
	view.phase = "ready"
	view._map = {"manifest": {"map_id":13,"name":source_name(13)}}
	view._player_snapshot = {"world_ready":true,"map_id":13,"map_epoch":10}
	check(source_name(13) == "尼西亞台地" and source_name(15) == "首都．尼尼亞皇城", "map names must come from converter world source")
	view._show_hk_place_name()
	check(notice._banner.visible and notice._caption.text == source_name(13), "real WorldReady map13 name")
	check(notice._art.texture != null and notice._art.texture.get_size() == Vector2(346,19), "real HK 346x19 image")
	check(notice._remaining_seconds == 7.0, "Old CPlaceNameItf seven-second timer")
	notice._process(3.0)
	check(notice._remaining_seconds == 4.0, "timer elapses only through UI clock")
	view._show_hk_place_name()
	check(notice._remaining_seconds == 4.0, "repeated AOI snapshot must not restart timer")
	notice._process(4.01)
	check(not notice._banner.visible, "notice expires")
	view._show_hk_place_name()
	check(not notice._banner.visible, "same epoch does not restart expired notice")
	view._player_snapshot.map_epoch = 11
	view._show_hk_place_name()
	check(notice._banner.visible and notice._remaining_seconds == 7.0, "new authoritative map epoch can announce")
	view.clear_map()
	check(not notice._banner.visible and notice._caption.text.is_empty(), "transfer/logout clears old title")
	view.map_id = 15
	view.phase = "ready"
	view.show_players = true
	view._map = {"manifest":{"map_id":15,"name":source_name(15)}}
	view._player_snapshot = {"world_ready":true,"map_id":15,"map_epoch":12}
	view._show_hk_place_name()
	check(notice._caption.text == source_name(15) and notice._banner.visible, "new map 15 uses its own source name")
	view.free()
	if failures.is_empty():
		print("HK_MAPNAME_DISPLAY_PASS HK_sprite=346x19 control_2076=true source_maps=13_15 timer_seconds=7 map_epoch_replay=true clear=true")
	quit(0 if failures.is_empty() else 1)
