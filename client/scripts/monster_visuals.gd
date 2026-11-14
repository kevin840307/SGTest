extends RefCounted

# Map monster definitions share one immutable atlas per visual. Production map
# loading can use RuntimeAssetStore asynchronously; add() stays as a fail-closed
# synchronous fallback for fixtures and recovery tools.
const ATLAS = preload("res://scripts/entity_atlas.gd")
var definitions: Dictionary = {} # definition id -> sprite key
var shared: Dictionary = {} # sprite -> {atlas,prefix,path}
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
	# A map generation owns its definition table and all strong atlas refs.
	# Release/clear the previous generation before issuing the next owner epoch;
	# stale worker results are rejected by RuntimeAssetStore request tokens.
	reset(false)
	store = value
	store_owner = owner
	store_epoch = store.begin_owner(owner)
	return OK if store_epoch > 0 else ERR_INVALID_DATA

func _validate_definition(definition: Dictionary) -> Dictionary:
	var id := int(definition.get("id", -1))
	var sprite := str(definition.get("sprite", ""))
	var atlas := str(definition.get("atlas", ""))
	if id < 0 or definitions.has(id) or not sprite.is_valid_identifier():
		return {}
	if atlas.is_empty() or atlas.is_absolute_path() or atlas.get_file().is_empty():
		return {}
	return {"id": id, "sprite": sprite, "atlas": atlas}

func add(root: String, definition: Dictionary) -> Error:
	var record := _validate_definition(definition)
	if record.is_empty():
		return ERR_INVALID_DATA
	var id := int(record.id)
	var sprite := str(record.sprite)
	if not shared.has(sprite):
		var atlas := ATLAS.new()
		var error: Error = atlas.open(root.path_join(str(record.atlas)))
		if error != OK:
			return error
		shared[sprite] = {"atlas": atlas, "prefix": sprite, "path": root.path_join(str(record.atlas))}
		for page in atlas.pages:
			rgba_bytes += page.get_width() * page.get_height() * 4
	definitions[id] = sprite
	return OK

func add_async(root: String, definition: Dictionary, priority: int = 0) -> Error:
	if store == null or store_owner.is_empty() or store_epoch <= 0:
		return ERR_UNCONFIGURED
	var record := _validate_definition(definition)
	if record.is_empty():
		return ERR_INVALID_DATA
	var id := int(record.id)
	var sprite := str(record.sprite)
	if not shared.has(sprite):
		var path := root.path_join(str(record.atlas))
		var error: Error = store.acquire(path, store_owner, store_epoch, priority)
		if error != OK:
			return error
		shared[sprite] = {"atlas": null, "prefix": sprite, "path": path, "counted": false}
	definitions[id] = sprite
	return OK

func refresh() -> void:
	if store == null:
		return
	for sprite in shared:
		var record: Dictionary = shared[sprite]
		if record.atlas != null:
			continue
		var path := str(record.path)
		if store.is_failed(path):
			continue
		var atlas: RefCounted = store.get_atlas(path)
		if atlas != null:
			record.atlas = atlas
			if not bool(record.get("counted", false)):
				rgba_bytes += store.bytes_for(path)
				record.counted = true
			shared[sprite] = record

func poll_gpu(max_pages: int = 1, budget_usec: int = 1500) -> int:
	if store == null:
		return 0
	var uploaded: int = store.poll_gpu(max_pages, budget_usec)
	refresh()
	return uploaded

func is_ready() -> bool:
	if shared.is_empty():
		return true
	for record in shared.values():
		if record.atlas == null:
			return false
	return true

func first_error() -> Error:
	if store == null:
		return OK
	for record in shared.values():
		if record.atlas == null and store.is_failed(str(record.path)):
			return store.error(str(record.path))
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
	return shared.get(str(definitions[id]), {})
