extends RefCounted
## Presentation-only, main-thread API. Call pump independently of network/state work.
## A scope owns an asset once; renderers must drop returned data before end_scope.

signal asset_ready(owner: StringName, epoch: int, asset_id: String)
signal asset_failed(owner: StringName, epoch: int, asset_id: String, reason: String)

static var _mounted_digest := ""
var max_assets := 256
var max_workers := 2
var max_texture_jobs := 1
var max_rgba_bytes := 256 * 1024 * 1024
var _catalog: Dictionary = {}
var _scopes: Dictionary = {}
var _entries: Dictionary = {}
var _decoders: Array = []
var _textures: Dictionary = {}
var _serial := 0
var _epoch := 0
var _reserved_bytes := 0
var _opened := false


class DecodeJob extends RefCounted:
	var asset_id: String
	var path: String
	var serial: int
	var task: int
	var result: Dictionary = {}

	func run() -> void:
		var manifest = JSON.parse_string(FileAccess.get_file_as_string(path))
		if not manifest is Dictionary:
			result = {"error": "Invalid asset JSON"}
			return
		var pages: Array = []
		var sizes: Array = []
		var extra: Dictionary = {}
		if asset_id.begins_with("maps/") and asset_id.get_slice_count("/") == 2:
			var map_id := int(asset_id.get_slice("/", 1))
			if manifest.get("map_id") != map_id or manifest.get("version") != 3:
				result = {"error": "Wrong world map/schema"}
				return
			if manifest.get("scenery_schema") != "sgtest.scenery/v1":
				result = {"error": "Missing scenery presentation contract"}
				return
			if manifest.get("tile_source_rule") != "int16_plus_one_wrap":
				result = {"error": "Missing original terrain tile mapping contract"}
				return
			var grid = manifest.get("atlas_grid", [])
			if not grid is Array or grid.size() != 2 or int(manifest.get("tile_side", 0)) <= 0:
				result = {"error": "Invalid map terrain dimensions"}
				return
			pages = ["terrain.png"]
			sizes = [[int(grid[0]) * int(manifest.tile_side), int(grid[1]) * int(manifest.tile_side)]]
			for name in ["npcs", "population"]:
				var document = JSON.parse_string(FileAccess.get_file_as_string(path.get_base_dir().path_join(name + ".json")))
				if not document is Dictionary or document.get("map_id") != map_id:
					result = {"error": "Wrong map companion: " + name}
					return
				extra[name] = document
			for name in ["tiles", "blocking"]:
				var suffix := ".bin" if name == "tiles" else ".dat"
				var file := path.get_base_dir().path_join(name + suffix)
				if FileAccess.get_sha256(file) != manifest.get(name + "_sha256" if name == "tiles" else "collision_sha256", ""):
					result = {"error": "Map bytes differ: " + name}
					return
				extra[name] = FileAccess.get_file_as_bytes(file)
			if extra.npcs.get("schema") != "sgtest.npcs/v1" or extra.population.get("version") != 3:
				result = {"error": "Unsupported map companion schema"}
				return
		else:
			if manifest.get("version") != 9 or manifest.get("asset_id") != asset_id:
				result = {"error": "Wrong Atlas identity/schema"}
				return
			if not manifest.get("pages") is Array or not manifest.get("page_sizes") is Array:
				result = {"error": "Invalid page tables"}
				return
			pages = manifest.pages
			sizes = manifest.page_sizes
		if not pages is Array or not sizes is Array or pages.size() != sizes.size():
			result = {"error": "Missing page dimensions"}
			return
		var bytes := 0
		var paths: Array = []
		for index in pages.size():
			var name := str(pages[index])
			var size = sizes[index]
			if not name.is_valid_filename() or name in [".", ".."] or not size is Array or size.size() != 2 or int(size[0]) <= 0 or int(size[1]) <= 0 or int(size[0]) > 4096 or int(size[1]) > 4096:
				result = {"error": "Unsafe page geometry/path"}
				return
			var file := path.get_base_dir().path_join(name)
			var expected := str(manifest.get("page_sha256", {}).get(name, manifest.get("terrain_sha256", "")))
			if expected.is_empty() or FileAccess.get_sha256(file) != expected:
				result = {"error": "Page source hash mismatch"}
				return
			paths.append(file)
			bytes += int(size[0]) * int(size[1]) * 4
		if manifest.get("version") == 9:
			if not manifest.get("frames") is Array or not manifest.get("clips") is Dictionary:
				result = {"error": "Missing frame/clip table"}
				return
			for frame in manifest.frames:
				if not frame is Dictionary or not frame.get("rect") is Array or not frame.get("origin") is Array:
					result = {"error": "Invalid frame record"}
					return
				var page := int(frame.get("page", -2))
				var rect = frame.get("rect", [])
				if page < -1 or page >= sizes.size() or rect.size() != 4 or frame.get("origin", []).size() != 2:
					result = {"error": "Invalid frame geometry"}
					return
				if page >= 0 and (rect[0] < 0 or rect[1] < 0 or rect[2] <= 0 or rect[3] <= 0 or rect[0] + rect[2] > sizes[page][0] or rect[1] + rect[3] > sizes[page][1]):
					result = {"error": "Frame outside page"}
					return
			for clip in manifest.clips.values():
				if not clip is Array:
					result = {"error": "Invalid clip record"}
					return
				for frame_index in clip:
					if int(frame_index) < 0 or int(frame_index) >= manifest.frames.size():
						result = {"error": "Clip frame index outside table"}
						return
		freeze(manifest)
		freeze(extra)
		result = {"manifest": manifest, "extra": extra, "paths": paths, "sizes": sizes, "bytes": bytes}

	static func freeze(value: Variant) -> void:
		if value is Dictionary:
			for child in value.values():
				freeze(child)
			value.make_read_only()
		elif value is Array:
			for child in value:
				freeze(child)
			value.make_read_only()


func open_pack(path: String, expected_digest := "") -> Error:
	if _opened or not _entries.is_empty() or not _decoders.is_empty() or not _textures.is_empty():
		return ERR_ALREADY_IN_USE
	var digest := FileAccess.get_sha256(path)
	if digest.is_empty() or (not expected_digest.is_empty() and digest != expected_digest):
		return ERR_FILE_CORRUPT
	if not _mounted_digest.is_empty() and _mounted_digest != digest:
		return ERR_ALREADY_IN_USE # Godot cannot safely unmount/remap a live pack.
	if _mounted_digest.is_empty():
		if not ProjectSettings.load_resource_pack(path, true):
			return ERR_FILE_CORRUPT
		_mounted_digest = digest
	var root = JSON.parse_string(FileAccess.get_file_as_string("res://assets/catalog/assets.json"))
	if not root is Dictionary or root.get("schema") != "sgtest.asset_catalog_root/v1":
		return ERR_INVALID_DATA
	var catalog: Dictionary = {}
	for category in root.get("categories", {}).values():
		var relative := str(category.get("index", ""))
		if not _safe_path(relative):
			return ERR_INVALID_DATA
		var index = JSON.parse_string(FileAccess.get_file_as_string("res://assets/" + relative))
		if not index is Dictionary or not index.get("assets") is Dictionary:
			return ERR_INVALID_DATA
		for id in index.assets:
			var entry = index.assets[id]
			if entry.get("kind") != "sprite_atlas":
				continue
			if catalog.has(id) or not _safe_path(id) or entry.get("file") != id + "/atlas.json":
				return ERR_INVALID_DATA
			catalog[id] = "res://assets/" + entry.file
	_catalog = catalog
	_opened = true
	return OK


func begin_scope(owner: StringName) -> int:
	if not _opened or str(owner).is_empty() or (_scopes.size() >= max_assets and not _scopes.has(owner)):
		return 0
	if _scopes.has(owner):
		end_scope(owner, _scopes[owner].epoch)
	_epoch += 1
	_scopes[owner] = {"epoch": _epoch, "assets": {}}
	return _epoch


func acquire(owner: StringName, epoch: int, asset_id: String) -> Error:
	if not _scope_valid(owner, epoch):
		return ERR_INVALID_PARAMETER
	if not _entries.has(asset_id):
		var path := str(_catalog.get(asset_id, ""))
		if path.is_empty() and _map_id(asset_id) > 0:
			path = "res://assets/" + asset_id + "/world.json"
		if path.is_empty() or not FileAccess.file_exists(path):
			return ERR_FILE_NOT_FOUND
		if _entries.size() >= max_assets:
			return ERR_OUT_OF_MEMORY
		_serial += 1
		_entries[asset_id] = {"path": path, "serial": _serial, "state": "queued", "owners": {}, "bytes": 0}
	if _entries[asset_id].state == "failed":
		return ERR_INVALID_DATA
	_entries[asset_id].owners[owner] = epoch
	_scopes[owner].assets[asset_id] = true
	return OK


func get_asset(owner: StringName, epoch: int, asset_id: String) -> Dictionary:
	if not _owns(owner, epoch, asset_id) or _entries[asset_id].state != "ready":
		return {}
	return _entries[asset_id].data


func release(owner: StringName, epoch: int, asset_id: String) -> void:
	if not _owns(owner, epoch, asset_id):
		return
	_scopes[owner].assets.erase(asset_id)
	_entries[asset_id].owners.erase(owner)
	if _entries[asset_id].owners.is_empty():
		_reserved_bytes -= int(_entries[asset_id].bytes)
		_entries.erase(asset_id)


func end_scope(owner: StringName, epoch: int) -> void:
	if not _scope_valid(owner, epoch):
		return
	for id in _scopes[owner].assets.keys():
		release(owner, epoch, id)
	_scopes.erase(owner)


func pump(max_commits := 2) -> void:
	# Only completed tasks are joined; no worker wait on an active task.
	for job in _decoders.duplicate():
		if not WorkerThreadPool.is_task_completed(job.task):
			continue
		WorkerThreadPool.wait_for_task_completion(job.task)
		_decoders.erase(job)
		if not _current(job.asset_id, job.serial):
			continue
		var entry: Dictionary = _entries[job.asset_id]
		if job.result.has("error") or not job.result.has_all(["manifest", "extra", "paths", "sizes", "bytes"]):
			_fail(job.asset_id, str(job.result.get("error", "Incomplete decode result")))
			continue
		if _reserved_bytes + int(job.result.bytes) > max_rgba_bytes:
			_fail(job.asset_id, "Asset RGBA budget exceeded")
			continue
		entry.bytes = job.result.bytes
		_reserved_bytes += int(entry.bytes)
		entry.description = job.result
		entry.pages = []
		entry.state = "textures"
	for path in _textures.keys():
		var task: Dictionary = _textures[path]
		var status := ResourceLoader.load_threaded_get_status(path)
		if status == ResourceLoader.THREAD_LOAD_IN_PROGRESS or max_commits <= 0:
			continue
		var resource = ResourceLoader.load_threaded_get(path)
		_textures.erase(path)
		max_commits -= 1
		if not _current(task.id, task.serial) or _entries[task.id].state != "textures":
			continue # Cancelled resources are drained and immediately dropped.
		var entry: Dictionary = _entries[task.id]
		var size = entry.description.sizes[entry.pages.size()]
		if not resource is Texture2D or resource.get_width() != int(size[0]) or resource.get_height() != int(size[1]):
			_fail(task.id, "Texture load/dimensions failed")
			continue
		entry.pages.append(resource)
	for id in _entries.keys():
		if not _entries.has(id):
			continue
		var entry: Dictionary = _entries[id]
		if entry.state == "queued" and _decoders.size() < max_workers:
			var job := DecodeJob.new()
			job.asset_id = id
			job.path = entry.path
			job.serial = entry.serial
			entry.state = "decoding"
			job.task = WorkerThreadPool.add_task(job.run)
			_decoders.append(job)
		elif entry.state == "textures":
			if entry.pages.size() == entry.description.paths.size():
				entry.pages.make_read_only()
				entry.data = {"manifest": entry.description.manifest, "extra": entry.description.extra, "pages": entry.pages}
				entry.data.make_read_only()
				entry.erase("description")
				entry.state = "ready"
				for owner in entry.owners.keys():
					var epoch := int(entry.owners.get(owner, 0))
					if _owns(owner, epoch, id):
						asset_ready.emit(owner, epoch, id)
			elif _textures.size() < max_texture_jobs:
				var path := str(entry.description.paths[entry.pages.size()])
				if _textures.has(path):
					continue
				var error := ResourceLoader.load_threaded_request(path, "Texture2D", false, ResourceLoader.CACHE_MODE_IGNORE)
				if error != OK:
					_fail(id, "Texture request failed: " + error_string(error))
				else:
					_textures[path] = {"id": id, "serial": entry.serial}


func statistics() -> Dictionary:
	var ready := 0
	for entry in _entries.values():
		if entry.state == "ready":
			ready += 1
	return {"assets": _entries.size(), "scopes": _scopes.size(), "ready": ready,
		"decode_jobs": _decoders.size(), "texture_jobs": _textures.size(), "rgba_bytes": _reserved_bytes}


func _fail(id: String, reason: String) -> void:
	var entry: Dictionary = _entries[id]
	_reserved_bytes -= int(entry.bytes)
	entry.bytes = 0
	entry.erase("description")
	entry.erase("pages")
	entry.state = "failed"
	for owner in entry.owners.keys():
		var epoch := int(entry.owners.get(owner, 0))
		if _owns(owner, epoch, id):
			asset_failed.emit(owner, epoch, id, reason)


func _scope_valid(owner: StringName, epoch: int) -> bool:
	return _scopes.has(owner) and _scopes[owner].epoch == epoch


func _owns(owner: StringName, epoch: int, id: String) -> bool:
	return _scope_valid(owner, epoch) and _entries.has(id) and _entries[id].owners.get(owner) == epoch


func _current(id: String, serial: int) -> bool:
	return _entries.has(id) and _entries[id].serial == serial


static func _safe_path(value: String) -> bool:
	if value.is_empty() or value.contains("\\") or value.contains(":"):
		return false
	for part in value.split("/"):
		if part.is_empty() or part == "." or part == "..":
			return false
	return true


static func _map_id(id: String) -> int:
	if not _safe_path(id) or id.get_slice_count("/") != 2 or id.get_slice("/", 0) != "maps":
		return 0
	var number := id.get_slice("/", 1)
	if not number.is_valid_int() or str(int(number)) != number or int(number) < 1 or int(number) > 65535:
		return 0
	return int(number)
