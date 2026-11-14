extends RefCounted

# Monster definitions are logical prefixes. Runtime ownership is by manifest
# path/lifetime, so many monster types on the same map can share one atlas.
const ATLAS = preload("res://scripts/entity_atlas.gd")

var definitions: Dictionary = {} # definition id -> {prefix,path}
var shared: Dictionary = {} # manifest path -> {atlas,path,counted}
var rgba_bytes := 0
var store: RefCounted
var store_owner := ""
var store_epoch := 0

func reset(force_purge: bool = true) -> void:
	if store != null and not store_owner.is_empty():
		store.release_owner(store_owner)
		if force_purge:
			store.purge_unowned(true)
	definitions.clear()
	shared.clear()
	rgba_bytes = 0
	store_owner = ""
	store_epoch = 0

func begin_async(value: RefCounted, owner: String) -> Error:
	if value == null or owner.is_empty():
		return ERR_INVALID_PARAMETER
	reset(false)
	store = value
	store_owner = owner
	store_epoch = store.begin_owner(owner)
	return OK if store_epoch > 0 else ERR_INVALID_DATA

func _validate_definition(definition: Dictionary) -> Dictionary:
	var id := int(definition.get("id",-1))
	var sprite := str(definition.get("sprite",""))
	var atlas := str(definition.get("atlas",""))
	if id < 0 or definitions.has(id) or not sprite.is_valid_identifier():
		return {}
	if atlas.is_empty() or atlas.is_absolute_path() or atlas.get_file().is_empty():
		return {}
	return {"id":id,"prefix":sprite,"atlas":atlas}

func add(root: String, definition: Dictionary) -> Error:
	var record := _validate_definition(definition)
	if record.is_empty():
		return ERR_INVALID_DATA
	var path := root.path_join(str(record.atlas))
	if not shared.has(path):
		var atlas := ATLAS.new()
		var error: Error = atlas.open(path)
		if error != OK:
			return error
		shared[path] = {"atlas":atlas,"path":path,"counted":true}
		for page in atlas.pages:
			rgba_bytes += page.get_width()*page.get_height()*4
	definitions[int(record.id)] = {"prefix":str(record.prefix),"path":path}
	return OK

func add_async(root: String, definition: Dictionary, priority: int = 0) -> Error:
	if store == null or store_owner.is_empty() or store_epoch <= 0:
		return ERR_UNCONFIGURED
	var record := _validate_definition(definition)
	if record.is_empty():
		return ERR_INVALID_DATA
	var path := root.path_join(str(record.atlas))
	if not shared.has(path):
		var error: Error = store.acquire(path,store_owner,store_epoch,priority)
		if error != OK:
			return error
		shared[path] = {"atlas":null,"path":path,"counted":false}
	definitions[int(record.id)] = {"prefix":str(record.prefix),"path":path}
	return OK

func refresh() -> void:
	if store == null:
		return
	for path in shared:
		var record: Dictionary = shared[path]
		if record.atlas != null or store.is_failed(str(path)):
			continue
		var atlas: RefCounted = store.get_atlas(str(path))
		if atlas != null:
			record.atlas = atlas
			if not bool(record.counted):
				rgba_bytes += store.bytes_for(str(path))
				record.counted = true
			shared[path] = record

func poll_gpu(max_pages: int = 1, budget_usec: int = 1500) -> int:
	if store == null:
		return 0
	var uploaded: int = store.poll_gpu(max_pages,budget_usec)
	refresh()
	return uploaded

func is_ready() -> bool:
	for record in shared.values():
		if record.atlas == null:
			return false
	return true

func first_error() -> Error:
	if store == null:
		return OK
	for path in shared:
		if shared[path].atlas == null and store.is_failed(str(path)):
			return store.error(str(path))
	return OK

func loaded_count() -> int:
	var count := 0
	for record in shared.values():
		if record.atlas != null:
			count += 1
	return count

func resolve(id: int) -> Dictionary:
	if not definitions.has(id):
		return {}
	var definition: Dictionary = definitions[id]
	var path := str(definition.path)
	if not shared.has(path):
		return {}
	var record: Dictionary = shared[path]
	return {"atlas":record.atlas,"prefix":str(definition.prefix),"path":path}
