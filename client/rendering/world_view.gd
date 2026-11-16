extends Node2D
## One map presentation owner. No gameplay state, networking, or implicit transfer.
signal status_changed(message: String)

const Store = preload("res://assets_runtime/asset_store.gd")
const Camera = preload("res://rendering/world_camera.gd")
const TerrainData = preload("res://rendering/terrain_data.gd")
const Terrain = preload("res://rendering/terrain.gd")
const DrawList = preload("res://rendering/world_draw_list.gd")
const Canvas = preload("res://rendering/world_canvas.gd")
var store := Store.new()
var camera := Camera.new()
var draw_list := DrawList.new()
var phase := "closed"
var map_id := 0
var epoch := 0
var _size := Vector2i(1280,720)
var _map: Dictionary = {}
var _palette: Array = []
var _terrain := Terrain.new()
var _canvas := Canvas.new()
var _started_at := 0
var _last_update := -1
var _last_rect := Rect2()


func _ready() -> void:
	add_child(_terrain)
	add_child(_canvas)
	store.asset_failed.connect(_asset_failed)


func open_pack(path: String) -> Error:
	var error := store.open_pack(path)
	if error == OK:
		phase = "empty"
	return error


func request_map(id: int) -> Error:
	if phase == "closed" or id <= 0:
		return ERR_INVALID_PARAMETER
	clear_map()
	epoch = store.begin_scope(&"world_view")
	map_id = id
	var error := store.acquire(&"world_view",epoch,"maps/"+str(id))
	if error != OK:
		_fail("無法載入地圖 " + str(id))
		return error
	phase = "map"
	status_changed.emit("載入地圖 " + str(id) + "…")
	return OK


func clear_map() -> void:
	_canvas.clear()
	_terrain.configure(null)
	draw_list.clear()
	camera.clear()
	_map = {}
	_palette = []
	_last_update = -1
	_last_rect = Rect2()
	get_viewport().canvas_transform = Transform2D.IDENTITY
	if epoch > 0:
		store.end_scope(&"world_view",epoch)
	map_id = 0
	if phase != "closed":
		phase = "empty"


func resize_view(size: Vector2i) -> Error:
	if not Camera._valid_size(size):
		return ERR_INVALID_PARAMETER
	_size = size
	return camera.resize(epoch,size) if phase == "ready" else OK


func _process(_delta: float) -> void:
	# Only presentation resource work runs here. Future network/reducer must
	# have a separate owner/scheduler; this is never a world simulation tick.
	store.pump(2)
	if phase == "map":
		_map = store.get_asset(&"world_view",epoch,"maps/"+str(map_id))
		if _map.is_empty():
			return
		var ids := {}
		for definition in _map.manifest.scenery_definitions.values():
			for appearance in definition.appearances:
				if appearance != null:
					ids[str(appearance.atlas).trim_suffix("/atlas.json")] = true
		_palette = ids.keys()
		for id in _palette:
			if store.acquire(&"world_view",epoch,id) != OK:
				_fail("地圖素材載入失敗")
				return
		phase = "palette"
	if phase == "palette":
		var assets := {}
		for id in _palette:
			var asset := store.get_asset(&"world_view",epoch,id)
			if asset.is_empty():
				return
			assets[id] = asset
		var data := TerrainData.new()
		if data.configure(_map) != OK or draw_list.configure(_map.manifest,assets,epoch) != OK:
			_fail("地圖資料無法顯示")
			return
		var world_size := Vector2i(_map.manifest.size[0],_map.manifest.size[1])
		if camera.configure(epoch,world_size,_size,Vector2(world_size)/2.0) != OK:
			_fail("相機設定失敗")
			return
		_terrain.configure(data)
		_started_at = Time.get_ticks_msec()
		phase = "ready"
		status_changed.emit("地圖 " + str(map_id) + "｜地圖檢視・尚未連線")
	if phase == "ready":
		present()


func present() -> void:
	if phase != "ready":
		return
	var rect := camera.view_rect()
	# Stateless ACT sampling; no callbacks, combat, or visibility state advances.
	var update := (Time.get_ticks_msec()-_started_at)*50/1000
	if rect == _last_rect and update == _last_update:
		return
	get_viewport().canvas_transform = Transform2D(0,-rect.position)
	_terrain.set_view_rect(rect)
	_canvas.present(draw_list.build(rect,[],{},draw_list.frames_at_update(update)))
	_last_rect = rect
	_last_update = update


func _asset_failed(owner: StringName, failed_epoch: int, _id: String, _reason: String) -> void:
	if owner == &"world_view" and failed_epoch == epoch:
		_fail("地圖素材載入失敗")


func _fail(message: String) -> void:
	clear_map()
	phase = "failed"
	status_changed.emit(message)


func _exit_tree() -> void:
	clear_map()
