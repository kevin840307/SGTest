extends RefCounted

# Static map objects keep the original asset identity: one source visual ->
# one atlas manifest. A single AssetStore still owns decoding/cache/LRU.
# Objects are acquired lazily from the current view; nothing is merged with
# monsters or with other scenery just to reduce file count.
const ATLAS = preload("res://scripts/entity_atlas.gd")
const RELEASE_AFTER_MSEC := 8000

var root := ""
var objects: Array = []
var visible: Array = []

var store: RefCounted
var store_owner := ""
var store_epoch := 0
var requested: Dictionary = {} # absolute atlas path -> last visible msec
var atlases: Dictionary = {}   # absolute atlas path -> EntityAtlas
var failed: Dictionary = {}

func reset(force_purge: bool = true) -> void:
	if store != null and not store_owner.is_empty():
		store.release_owner(store_owner)
		if force_purge:
			store.purge_unowned(true)
	objects.clear()
	visible.clear()
	requested.clear()
	atlases.clear()
	failed.clear()
	store = null
	store_owner = ""
	store_epoch = 0
	root = ""

func configure(asset_root: String, metadata: Dictionary, value: RefCounted = null,
		owner: String = "map-objects") -> void:
	reset(true)
	root = asset_root
	objects = metadata.get("objects",[])
	for object in objects:
		object.box = Rect2(object.bounds[0],object.bounds[1],object.bounds[2],object.bounds[3])
		var relative := str(object.get("atlas",""))
		if relative.is_empty():
			push_error("Map object has no independent atlas: %s" % str(object.get("source","")))
	if value != null:
		store = value
		store_owner = owner
		store_epoch = store.begin_owner(owner)
		if store_epoch <= 0:
			push_error("Map object owner generation failed")

func _absolute(object: Dictionary) -> String:
	var relative := str(object.get("atlas",""))
	return root.path_join(relative) if not relative.is_empty() else ""

func _request(path: String, priority: int, now: int) -> void:
	if path.is_empty():
		return
	requested[path] = now
	if atlases.has(path) or failed.has(path):
		return
	if store != null:
		if not store.is_ready(path) and not store.is_failed(path):
			var error: Error = store.acquire(path,store_owner,store_epoch,priority)
			if error != OK:
				failed[path] = error
				push_error("Map object asset queue failed: %s (%d)" % [path,error])
	else:
		var atlas := ATLAS.new()
		var error: Error = atlas.open(path)
		if error == OK:
			atlases[path] = atlas
		else:
			failed[path] = error
			push_error("Map object asset load failed: %s (%d)" % [path,error])

func refresh() -> void:
	if store == null:
		return
	for path in requested.keys():
		if atlases.has(path) or failed.has(path):
			continue
		if store.is_failed(path):
			var error: Error = store.error(path)
			failed[path] = error
			push_error("Map object asset load failed: %s (%d)" % [path,error])
		elif store.is_ready(path):
			var atlas: RefCounted = store.get_atlas(path)
			if atlas != null:
				atlases[path] = atlas

func is_ready() -> bool:
	refresh()
	for object in visible:
		var path := _absolute(object)
		if not path.is_empty() and not atlases.has(path):
			return false
	return true

func prepare(view: Rect2) -> void:
	refresh()
	visible.clear()
	var needed: Dictionary = {}
	for object in objects:
		if object.box.intersects(view):
			visible.append(object)
			var path := _absolute(object)
			if not path.is_empty():
				needed[path] = int(needed.get(path,0)) + 1
	var now := Time.get_ticks_msec()
	for path in needed:
		# Repeated visible instances raise queue priority but still share one atlas.
		_request(str(path),mini(1000,100+int(needed[path])),now)
	if store != null:
		for path in requested.keys().duplicate():
			if needed.has(path):
				continue
			if now-int(requested[path]) < RELEASE_AFTER_MSEC:
				continue
			store.release(str(path),store_owner,store_epoch)
			requested.erase(path)
			atlases.erase(path)
			failed.erase(path)
	refresh()

func draw_object(canvas: CanvasItem, object: Dictionary, camera: Vector2, seconds: float = 0.0) -> void:
	var atlas: RefCounted = atlases.get(_absolute(object))
	if atlas == null:
		return
	# Scenery has no verified ACT timing. Keep deterministic 8 FPS playback;
	# no network packet is generated for decorative animation.
	var count := int(object.get("frames",1))
	var phase := fposmod(seconds,count/8.0) if count > 1 else 0.0
	atlas.draw_frame(canvas,Vector2(object.p[0],object.p[1])-camera,object.clip,phase)
