extends Control
## UI-only standing paper doll. Shares the app AssetStore and formal sampler.
## B/G/A/R/Y use source-normalized profiles; only this small preview renderer
## applies the verified Recovered2 hair hue palette, leaving world batching intact.
signal status_changed(status: String)
const Players = preload("res://player/player_presentation.gd")
const Profile = preload("res://player/character_appearance_profile.gd")
const DrawList = preload("res://world/world_draw_list.gd")
const Canvas = preload("res://player/character_preview_canvas.gd")
const BASE_APPEARANCE := {"race":0,"hair_index":0,"hair_color":0}

var status := "empty"
var _store: RefCounted
var _owner: StringName
var _epoch := 0
var _occupied := false
var _appearance: Dictionary = {}
var _definition: Dictionary = {}
var _assets: Dictionary = {}
var _players := Players.new()
var _draw_list := DrawList.new()
var _canvas := Canvas.new()
var _frame_key: Array = []

func _init() -> void:
	name = "CharacterPreview"
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_owner = StringName("character_preview_"+str(get_instance_id()))
	add_child(_canvas)
	visibility_changed.connect(_sync_activity)
	resized.connect(func(): _frame_key.clear())
	set_process(false)

func bind_store(store: RefCounted) -> void:
	if _store == store:
		return
	_release()
	if _store != null and _store.asset_failed.is_connected(_asset_failed):
		_store.asset_failed.disconnect(_asset_failed)
	_store = store
	if _store != null:
		_store.asset_failed.connect(_asset_failed)
	_sync_activity()

func _asset_failed(owner: StringName, epoch: int, _id: String, _reason: String) -> void:
	if owner == _owner and epoch == _epoch:
		_release()
		_set_status("unavailable")

func set_occupied(value: bool) -> void:
	if _occupied == value:
		return
	_occupied = value
	_sync_activity()

func set_appearance(value: Dictionary) -> void:
	if value == _appearance:
		return
	var old_definition := Profile.definition(_appearance)
	var new_definition := Profile.definition(value)
	var same_assets: bool = (
		not old_definition.is_empty() and
		not new_definition.is_empty() and
		old_definition.parts == new_definition.parts and
		old_definition.get("overlays",{}) == new_definition.get("overlays",{}))
	_appearance = value.duplicate()
	# Hair colour changes share the exact same source atlases. Do not tear down
	# the AssetStore scope just to change one shader parameter.
	if same_assets and _epoch > 0:
		_canvas.set_hair_color(int(_appearance.get("hair_color",0)))
		_definition = new_definition
		_frame_key.clear()
		if status == "ready":
			if _players.configure(_assets,_appearance) != OK:
				_release()
				_set_status("unavailable")
				return
			_apply_preview_snapshot()
		return
	# Hair/race changes require different atlases. Keep the last good frame
	# visible while the replacement scope loads, then swap in one frame.
	_release(true)
	_sync_activity()

func _set_status(value: String) -> void:
	if status != value:
		status = value
		status_changed.emit(status)

func _definition_parts() -> Array:
	if _definition.is_empty():
		return []
	var result: Array = _definition.parts.values()
	result.append_array(_definition.get("overlays",{}).values())
	return result


func _sync_activity() -> void:
	var active := is_inside_tree() and is_visible_in_tree() and _occupied
	if not active:
		_release()
		_set_status("empty")
		return
	_definition = Profile.definition(_appearance)
	if _definition.is_empty():
		_release()
		_set_status("unsupported")
		return
	if _store == null:
		_set_status("unavailable")
		return
	if _epoch <= 0:
		_epoch = _store.begin_scope(_owner)
		if _epoch <= 0:
			_set_status("unavailable")
			return
		for part in _definition_parts():
			if _store.acquire(_owner,_epoch,str(part[0])) != OK:
				_release()
				_set_status("unavailable")
				return
		_set_status("loading")
	set_process(true)

func _process(_delta: float) -> void:
	# WorldView pumps the single shared store; a preview never starts a
	# second loader, pump loop, viewport, network session or world reducer.
	if status == "loading":
		for part in _definition_parts():
			var id := str(part[0])
			var asset: Dictionary = _store.get_asset(_owner,_epoch,id)
			if asset.is_empty():
				return
			_assets[id] = asset
		if _players.configure(_assets,_appearance) != OK:
			_release()
			_set_status("unavailable")
			return
		_apply_preview_snapshot()
		_canvas.set_hair_color(int(_appearance.get("hair_color",0)))
		_set_status("ready")
	var actors: Array = _players.actors_at(Time.get_ticks_usec())
	if actors.is_empty():
		return
	var key: Array = [size]
	for part in actors[0].parts:
		key.append(part.frame)
	if key == _frame_key:
		return
	_frame_key = key
	actors[0].p = Vector2(size.x*0.5,size.y*0.5+30.0)
	_canvas.present(_draw_list.build_actor_preview(_assets,actors[0],Rect2(Vector2.ZERO,size)))

func _apply_preview_snapshot() -> void:
	var own := {"actor_id":1,"connection_epoch":1,"entity_map_epoch":1,
		"server_tick":0,"x":0.0,"y":0.0,"facing":1,"moving":false,
		"movement_action_sequence":0,"appearance":_appearance}
	_players.apply_snapshot({"world_ready":true,"map_id":1,"map_epoch":1,
		"actor_id":1,"self_state":own,"entities":{}},Time.get_ticks_usec())

func _release(preserve_canvas: bool = false) -> void:
	set_process(false)
	if not preserve_canvas:
		_canvas.clear()
	_draw_list.clear()
	_players.clear()
	_assets.clear()
	_frame_key.clear()
	if _store != null and _epoch > 0:
		_store.end_scope(_owner,_epoch)
	_epoch = 0

func _exit_tree() -> void:
	_release()
