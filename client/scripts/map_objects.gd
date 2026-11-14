extends RefCounted

# Static scenery shares the map lifecycle sprite bundle with map monsters.
# Viewport culling decides what is drawn, not what texture owner exists.
const ATLAS = preload("res://scripts/entity_atlas.gd")

var root := ""
var objects: Array = []
var visible: Array = []
var atlas: RefCounted
var bundle_path := ""

var store: RefCounted
var store_owner := ""
var store_epoch := 0

func reset(force_purge: bool = true) -> void:
	if store != null and not store_owner.is_empty():
		store.release_owner(store_owner)
		if force_purge:
			store.purge_unowned(true)
	objects.clear()
	visible.clear()
	atlas = null
	bundle_path = ""
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
	var relative := str(metadata.get("sprite_bundle",""))
	if relative.is_empty() and not objects.is_empty():
		relative = str(objects[0].get("atlas",""))
	for object in objects:
		if str(object.get("atlas",relative)) != relative:
			push_error("Map objects contain multiple runtime bundles; rebuild assets")
			return
	if relative.is_empty():
		return
	bundle_path = root.path_join(relative)
	if value != null:
		store = value
		store_owner = owner
		store_epoch = store.begin_owner(owner)
		if store_epoch <= 0:
			push_error("Map object owner generation failed")
			return
		var error: Error = store.acquire(bundle_path,store_owner,store_epoch,1)
		if error != OK:
			push_error("Map bundle queue failed: %s (%d)" % [bundle_path,error])
	else:
		var loaded := ATLAS.new()
		var error: Error = loaded.open(bundle_path)
		if error != OK:
			push_error("Map bundle load failed: %s (%d)" % [bundle_path,error])
			return
		atlas = loaded

func refresh() -> void:
	if store == null or atlas != null or bundle_path.is_empty():
		return
	if store.is_failed(bundle_path):
		push_error("Map bundle load failed: %s (%d)" % [bundle_path,store.error(bundle_path)])
		return
	atlas = store.get_atlas(bundle_path)

func is_ready() -> bool:
	refresh()
	return atlas != null or objects.is_empty()

func prepare(view: Rect2) -> void:
	refresh()
	visible.clear()
	for object in objects:
		if object.box.intersects(view):
			visible.append(object)

func draw_object(canvas: CanvasItem, object: Dictionary, camera: Vector2, seconds: float = 0.0) -> void:
	if atlas == null:
		return
	# Scenery has no ACT timing. Explicit 8 FPS policy, deterministic server
	# clock phase; no per-frame network updates for decorative animation.
	var count := int(object.get("frames",1))
	var phase := fposmod(seconds,count/8.0) if count > 1 else 0.0
	atlas.draw_frame(canvas,Vector2(object.p[0],object.p[1])-camera,object.clip,phase)
