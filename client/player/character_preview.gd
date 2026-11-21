extends Control
## UI-only standing paper doll. Shares the app AssetStore and formal renderer.
## The current renderer supports B/hair00/default color. Explicit unsupported
## choices hide that doll rather than misrepresenting another saved appearance.
signal status_changed(status: String)
const Players = preload("res://player/player_presentation.gd")
const DrawList = preload("res://world/world_draw_list.gd")
const Canvas = preload("res://world/world_canvas.gd")
const BASE_APPEARANCE := {"race":0,"hair_index":0,"hair_color":0}

var status := "empty"
var _store: RefCounted
var _owner: StringName
var _epoch := 0
var _occupied := false
var _appearance: Dictionary = {}
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
	var same_base := (_appearance.is_empty() or _appearance == BASE_APPEARANCE) and (value.is_empty() or value == BASE_APPEARANCE)
	_appearance = value.duplicate()
	if same_base and _epoch > 0:
		return
	_release()
	_sync_activity()

func _set_status(value: String) -> void:
	if status != value:
		status = value
		status_changed.emit(status)

func _sync_activity() -> void:
	var active := is_inside_tree() and is_visible_in_tree() and _occupied
	if not active:
		_release()
		_set_status("empty")
		return
	if not _appearance.is_empty() and _appearance != BASE_APPEARANCE:
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
		for part in Players.PARTS.values():
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
		for part in Players.PARTS.values():
			var id := str(part[0])
			var asset: Dictionary = _store.get_asset(_owner,_epoch,id)
			if asset.is_empty():
				return
			_assets[id] = asset
		if _players.configure(_assets) != OK:
			_release()
			_set_status("unavailable")
			return
		var own := {"actor_id":1,"connection_epoch":1,"entity_map_epoch":1,
			"server_tick":0,"x":0.0,"y":0.0,"facing":1,"moving":false,"movement_action_sequence":0}
		_players.apply_snapshot({"world_ready":true,"map_id":1,"map_epoch":1,
			"actor_id":1,"self_state":own,"entities":{}},Time.get_ticks_usec())
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

func _release() -> void:
	set_process(false)
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
