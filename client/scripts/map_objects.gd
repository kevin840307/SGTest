extends RefCounted

const ATLAS = preload("res://scripts/entity_atlas.gd")
const CACHE_BUDGET := 32*1024*1024
var root := ""
var objects: Array = []
var visible: Array = []
var cache: Dictionary = {}
var serial := 0

func reset() -> void:
	objects = []
	visible = []
	cache.clear()

func configure(asset_root: String, metadata: Dictionary) -> void:
	reset()
	root = asset_root
	objects = metadata.objects
	for object in objects:
		object.box = Rect2(object.bounds[0],object.bounds[1],object.bounds[2],object.bounds[3])

func prepare(view: Rect2) -> void:
	serial += 1
	visible.clear()
	var pinned: Dictionary = {}
	for object in objects:
		if not object.box.intersects(view):
			continue
		visible.append(object)
		pinned[object.atlas] = true
		if not cache.has(object.atlas):
			var atlas := ATLAS.new()
			var error: Error = atlas.open(root.path_join(object.atlas))
			if error != OK:
				push_error("Map object load failed: %s (%d)" % [object.atlas,error])
				continue
			var bytes := 0
			for size in atlas.data.page_sizes:
				bytes += int(size[0])*int(size[1])*4
			cache[object.atlas] = {"atlas":atlas,"bytes":bytes,"used":serial}
		cache[object.atlas].used = serial
	var bytes := 0
	for entry in cache.values():
		bytes += int(entry.bytes)
	while bytes > CACHE_BUDGET:
		var oldest := ""
		var age := serial+1
		for key in cache:
			if not pinned.has(key) and cache[key].used < age:
				oldest = key
				age = cache[key].used
		if oldest.is_empty():
			break # Visible images are pinned; never reload them every frame.
		bytes -= int(cache[oldest].bytes)
		cache.erase(oldest)

func draw_object(canvas: CanvasItem, object: Dictionary, camera: Vector2, seconds: float = 0.0) -> void:
	if cache.has(object.atlas):
		# Scenery has no ACT timing. Explicit 8 FPS policy, deterministic server
		# clock phase; no per-frame network updates for decorative animation.
		var count := int(object.get("frames",1))
		var phase := fposmod(seconds,count/8.0) if count > 1 else 0.0
		cache[object.atlas].atlas.draw_frame(canvas,Vector2(object.p[0],object.p[1])-camera,object.clip,phase)
