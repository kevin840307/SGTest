extends RefCounted

# Static scenery is view-driven. Synchronous open() remains available when no
# RuntimeAssetStore is supplied; production uses one map owner and lets the
# global bounded cache retain recently-unpinned atlases without local refs.
const ATLAS = preload("res://scripts/entity_atlas.gd")
const CACHE_BUDGET := 32 * 1024 * 1024

var root := ""
var objects: Array = []
var visible: Array = []
var cache: Dictionary = {}
var serial := 0

var store: RefCounted
var store_owner := ""
var store_epoch := 0
var pending: Dictionary = {}

func reset(force_purge: bool = true) -> void:
	if store != null and not store_owner.is_empty():
		store.release_owner(store_owner)
		if force_purge:
			store.purge_unowned(true)
	objects = []
	visible = []
	cache.clear()
	pending.clear()
	store_owner = ""
	store_epoch = 0
	root = ""

func configure(asset_root: String, metadata: Dictionary, value: RefCounted = null,
		owner: String = "map-objects") -> void:
	reset(true)
	root = asset_root
	objects = metadata.objects
	for object in objects:
		object.box = Rect2(object.bounds[0],object.bounds[1],object.bounds[2],object.bounds[3])
	if value != null:
		store = value
		store_owner = owner
		store_epoch = store.begin_owner(owner)

func _attach_ready(relative: String) -> void:
	if store == null:
		return
	var path := str(pending.get(relative, root.path_join(relative)))
	if store.is_failed(path):
		push_error("Map object load failed: %s (%d)" % [relative, store.error(path)])
		store.release(path, store_owner, store_epoch)
		pending.erase(relative)
		return
	var atlas: RefCounted = store.get_atlas(path)
	if atlas != null:
		cache[relative] = {"atlas": atlas, "used": serial, "path": path}
		pending.erase(relative)

func _prepare_async(pinned: Dictionary) -> void:
	for relative in pinned:
		if cache.has(relative):
			cache[relative].used = serial
			continue
		if not pending.has(relative):
			var path := root.path_join(str(relative))
			var error: Error = store.acquire(path, store_owner, store_epoch, 1)
			if error != OK:
				push_error("Map object queue failed: %s (%d)" % [relative, error])
				continue
			pending[relative] = path
			_attach_ready(str(relative))
		else:
			_attach_ready(str(relative))

	# Local references exist only while visible. The store may keep the texture
	# as unowned LRU cache, bounded together with monsters/player assets.
	for relative in cache.keys().duplicate():
		if pinned.has(relative):
			continue
		var entry: Dictionary = cache[relative]
		store.release(str(entry.path), store_owner, store_epoch)
		cache.erase(relative)
	for relative in pending.keys().duplicate():
		if pinned.has(relative):
			continue
		store.release(str(pending[relative]), store_owner, store_epoch)
		pending.erase(relative)

func _prepare_sync(pinned: Dictionary) -> void:
	for relative in pinned:
		if not cache.has(relative):
			var atlas := ATLAS.new()
			var error: Error = atlas.open(root.path_join(str(relative)))
			if error != OK:
				push_error("Map object load failed: %s (%d)" % [relative,error])
				continue
			var bytes := 0
			for size in atlas.data.page_sizes:
				bytes += int(size[0]) * int(size[1]) * 4
			cache[relative] = {"atlas":atlas,"bytes":bytes,"used":serial}
		cache[relative].used = serial
	var bytes := 0
	for entry in cache.values():
		bytes += int(entry.bytes)
	while bytes > CACHE_BUDGET:
		var oldest := ""
		var age := serial + 1
		for key in cache:
			if not pinned.has(key) and cache[key].used < age:
				oldest = key
				age = cache[key].used
		if oldest.is_empty():
			break
		bytes -= int(cache[oldest].bytes)
		cache.erase(oldest)

func prepare(view: Rect2) -> void:
	serial += 1
	visible.clear()
	var pinned: Dictionary = {}
	for object in objects:
		if not object.box.intersects(view):
			continue
		visible.append(object)
		pinned[object.atlas] = true
	if store != null and store_epoch > 0:
		_prepare_async(pinned)
	else:
		_prepare_sync(pinned)

func draw_object(canvas: CanvasItem, object: Dictionary, camera: Vector2, seconds: float = 0.0) -> void:
	if cache.has(object.atlas):
		# Scenery has no ACT timing. Explicit 8 FPS policy, deterministic server
		# clock phase; no per-frame network updates for decorative animation.
		var count := int(object.get("frames",1))
		var phase := fposmod(seconds,count/8.0) if count > 1 else 0.0
		cache[object.atlas].atlas.draw_frame(canvas,Vector2(object.p[0],object.p[1])-camera,object.clip,phase)
