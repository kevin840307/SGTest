class_name RuntimeAssetStore
extends RefCounted

# Generic runtime texture store. File/PNG decoding happens on one worker thread.
# Scene/texture objects are created only on the main thread with a strict
# per-frame commit budget. Owners are logical lifetimes (map, player, UI, etc.).
const ATLAS = preload("res://scripts/entity_atlas.gd")
const DEFAULT_CACHE_BUDGET := 64 * 1024 * 1024
const DEFAULT_READY_BUDGET := 16 * 1024 * 1024
const MAX_PAGE_BYTES := 64 * 1024 * 1024
const MAX_JOBS := 128

var cache_budget_bytes := DEFAULT_CACHE_BUDGET
var ready_budget_bytes := DEFAULT_READY_BUDGET

var _mutex := Mutex.new()
var _worker := Thread.new()
var _running := false
var _requests: Array = []
var _decoded: Array = []
var _ready_bytes := 0

# Main-thread only.
var _entries: Dictionary = {}
var _owner_epochs: Dictionary = {}
var _next_token := 1
var _resident_bytes := 0
var _clock := 0
var _metrics := {
	"requests": 0,
	"cache_hits": 0,
	"cache_misses": 0,
	"decoded_pages": 0,
	"uploaded_pages": 0,
	"evictions": 0,
	"cancelled_jobs": 0,
	"peak_ready_bytes": 0,
	"peak_resident_bytes": 0,
	"decode_ms": [],
	"upload_submit_ms": [],
	"gpu_commit_frame_ms": [],
}

func _metric_increment(key: String, amount: int = 1) -> void:
	_mutex.lock()
	_metrics[key] = int(_metrics.get(key, 0)) + amount
	_mutex.unlock()

func _metric_append(key: String, value: float) -> void:
	_mutex.lock()
	_metrics[key].append(value)
	_mutex.unlock()

func _metric_max(key: String, value: int) -> void:
	_mutex.lock()
	_metrics[key] = maxi(int(_metrics.get(key, 0)), value)
	_mutex.unlock()

func start() -> void:
	if _worker.is_started():
		return
	_running = true
	_worker.start(_decode_loop)

func configure(cache_bytes: int = DEFAULT_CACHE_BUDGET, ready_bytes: int = DEFAULT_READY_BUDGET) -> void:
	cache_budget_bytes = maxi(1, cache_bytes)
	ready_budget_bytes = maxi(1, ready_bytes)

func begin_owner(owner: String) -> int:
	if owner.is_empty():
		return -1
	release_owner(owner)
	var epoch := int(_owner_epochs.get(owner, 0)) + 1
	_owner_epochs[owner] = epoch
	return epoch

func owner_epoch(owner: String) -> int:
	return int(_owner_epochs.get(owner, 0))

func acquire(path: String, owner: String, epoch: int = -1, priority: int = 0) -> Error:
	if path.is_empty() or owner.is_empty():
		return ERR_INVALID_PARAMETER
	if epoch < 0:
		epoch = owner_epoch(owner)
	if epoch <= 0 or epoch != owner_epoch(owner):
		return ERR_INVALID_DATA
	start()
	_clock += 1
	_metric_increment("requests")
	if _entries.has(path):
		var existing: Dictionary = _entries[path]
		existing.owners[owner] = epoch
		existing.last_used = _clock
		_entries[path] = existing
		if existing.state == "ready":
			_metric_increment("cache_hits")
		return OK
	if _entries.size() >= MAX_JOBS:
		purge_unowned(false)
		if _entries.size() >= MAX_JOBS:
			return ERR_BUSY
	var token := _next_token
	_next_token += 1
	_entries[path] = {
		"state": "loading",
		"owners": {owner: epoch},
		"token": token,
		"atlas": null,
		"remaining": -1,
		"bytes": 0,
		"last_used": _clock,
		"error": OK,
	}
	_metric_increment("cache_misses")
	_mutex.lock()
	_requests.append({"path": path, "token": token, "priority": priority})
	_mutex.unlock()
	return OK

func is_ready(path: String) -> bool:
	return _entries.has(path) and _entries[path].state == "ready"

func is_failed(path: String) -> bool:
	return _entries.has(path) and _entries[path].state == "failed"

func error(path: String) -> Error:
	return _entries[path].error if _entries.has(path) else ERR_DOES_NOT_EXIST

func get_atlas(path: String) -> RefCounted:
	if not is_ready(path):
		return null
	_clock += 1
	var entry: Dictionary = _entries[path]
	entry.last_used = _clock
	_entries[path] = entry
	return entry.atlas

func bytes_for(path: String) -> int:
	return int(_entries[path].bytes) if _entries.has(path) else 0

func release(path: String, owner: String, epoch: int = -1) -> void:
	if not _entries.has(path):
		return
	var entry: Dictionary = _entries[path]
	if not entry.owners.has(owner):
		return
	if epoch >= 0 and int(entry.owners[owner]) != epoch:
		return
	entry.owners.erase(owner)
	if entry.owners.is_empty() and entry.state == "loading":
		# Invalidate all worker results for this request. A future acquire will
		# receive a new token and cannot consume stale decoded images.
		_metric_increment("cancelled_jobs")
		_entries.erase(path)
		return
	_entries[path] = entry
	purge_unowned(false)

func release_owner(owner: String) -> void:
	for path in _entries.keys().duplicate():
		release(str(path), owner)

func purge_unowned(force: bool = false) -> int:
	var evicted := 0
	# Failed requests carry no useful resident texture and must not permanently
	# consume MAX_JOBS. Once their last owner leaves, drop them even when the
	# byte cache is under budget so a later map generation may retry the path.
	for path in _entries.keys().duplicate():
		var stale: Dictionary = _entries[path]
		if stale.state == "failed" and stale.owners.is_empty():
			_entries.erase(path)
			_metric_increment("evictions")
			evicted += 1
	while force or _resident_bytes > cache_budget_bytes:
		var candidate := ""
		var oldest := 0x7fffffffffffffff
		for path in _entries:
			var entry: Dictionary = _entries[path]
			if entry.state == "ready" and entry.owners.is_empty() and int(entry.last_used) < oldest:
				candidate = str(path)
				oldest = int(entry.last_used)
		if candidate.is_empty():
			break
		var entry: Dictionary = _entries[candidate]
		_resident_bytes -= int(entry.bytes)
		_entries.erase(candidate)
		_metric_increment("evictions")
		evicted += 1
		if force:
			continue
	return evicted

func poll_gpu(max_pages: int = 1, budget_usec: int = 1500) -> int:
	max_pages = maxi(1, max_pages)
	budget_usec = maxi(100, budget_usec)
	var uploaded := 0
	var started := Time.get_ticks_usec()
	while uploaded < max_pages and Time.get_ticks_usec() - started < budget_usec:
		_mutex.lock()
		var item: Dictionary = _decoded.pop_front() if not _decoded.is_empty() else {}
		if not item.is_empty():
			_ready_bytes -= int(item.get("bytes", 0))
		_mutex.unlock()
		if item.is_empty():
			break
		var path := str(item.path)
		if not _entries.has(path):
			continue
		var entry: Dictionary = _entries[path]
		if int(entry.token) != int(item.token):
			continue
		match str(item.kind):
			"error":
				entry.state = "failed"
				entry.error = int(item.error)
				entry.atlas = null
				_entries[path] = entry
			"metadata":
				var atlas := ATLAS.new()
				atlas.data = item.data
				atlas.pages.resize(item.data.pages.size())
				entry.atlas = atlas
				entry.remaining = atlas.pages.size()
				if entry.remaining == 0:
					entry.state = "ready"
				_entries[path] = entry
			"image":
				if entry.atlas == null or int(entry.remaining) <= 0:
					entry.state = "failed"
					entry.error = ERR_FILE_CORRUPT
					_entries[path] = entry
					continue
				var stamp := Time.get_ticks_usec()
				entry.atlas.pages[int(item.index)] = ImageTexture.create_from_image(item.image)
				var elapsed_ms := (Time.get_ticks_usec() - stamp) / 1000.0
				_metric_append("upload_submit_ms", elapsed_ms)
				_metric_increment("uploaded_pages")
				entry.remaining -= 1
				entry.bytes += int(item.bytes)
				_resident_bytes += int(item.bytes)
				_metric_max("peak_resident_bytes", _resident_bytes)
				uploaded += 1
				if entry.remaining == 0:
					entry.state = "ready"
				_entries[path] = entry
	_metric_append("gpu_commit_frame_ms", (Time.get_ticks_usec() - started) / 1000.0)
	purge_unowned(false)
	return uploaded

func pending_count() -> int:
	var count := 0
	for entry in _entries.values():
		if entry.state == "loading":
			count += 1
	return count

func resident_bytes() -> int:
	return _resident_bytes

func statistics() -> Dictionary:
	_mutex.lock()
	var result: Dictionary = _metrics.duplicate(true)
	result["ready_queue_bytes"] = _ready_bytes
	result["queued_decode_jobs"] = _requests.size()
	_mutex.unlock()
	result["resident_bytes"] = _resident_bytes
	result["entries"] = _entries.size()
	result["pending"] = pending_count()
	return result

func close() -> void:
	for owner in _owner_epochs.keys().duplicate():
		release_owner(str(owner))
	purge_unowned(true)
	_mutex.lock()
	_running = false
	_requests.clear()
	_decoded.clear()
	_ready_bytes = 0
	_mutex.unlock()
	if _worker.is_started():
		_worker.wait_to_finish()
	_entries.clear()
	_resident_bytes = 0

func _publish(item: Dictionary, bytes: int = 0) -> bool:
	while true:
		_mutex.lock()
		var active := _running
		var allowed := bytes == 0 or _ready_bytes == 0 or _ready_bytes + bytes <= ready_budget_bytes
		if active and allowed:
			_decoded.append(item)
			_ready_bytes += bytes
			_metrics.peak_ready_bytes = maxi(int(_metrics.peak_ready_bytes), _ready_bytes)
			_mutex.unlock()
			return true
		_mutex.unlock()
		if not active:
			return false
		OS.delay_msec(1)
	return false

func _decode_loop() -> void:
	while true:
		_mutex.lock()
		var active := _running
		var job: Dictionary = {}
		if not _requests.is_empty():
			var best := 0
			for index in range(1, _requests.size()):
				if int(_requests[index].priority) > int(_requests[best].priority):
					best = index
			job = _requests.pop_at(best)
		_mutex.unlock()
		if not active:
			return
		if job.is_empty():
			OS.delay_msec(1)
			continue
		var manifest_path := str(job.path)
		if not FileAccess.file_exists(manifest_path):
			_publish({"path": job.path, "token": job.token, "kind": "error", "error": ERR_FILE_CANT_READ})
			continue
		var parser := JSON.new()
		if parser.parse(FileAccess.get_file_as_string(manifest_path)) != OK:
			_publish({"path": job.path, "token": job.token, "kind": "error", "error": ERR_FILE_CORRUPT})
			continue
		var data: Variant = parser.data
		if not data is Dictionary or int(data.get("version", 0)) not in [3,4,5,6,7,8,9] or not data.get("pages") is Array:
			_publish({"path": job.path, "token": job.token, "kind": "error", "error": ERR_FILE_CORRUPT})
			continue
		if not _publish({"path": job.path, "token": job.token, "kind": "metadata", "data": data}):
			continue
		for index in data.pages.size():
			var name := str(data.pages[index])
			if name != name.get_file() or name.is_absolute_path():
				_publish({"path": job.path, "token": job.token, "kind": "error", "error": ERR_INVALID_DATA})
				break
			var stamp := Time.get_ticks_usec()
			var image := Image.load_from_file(str(job.path).get_base_dir().path_join(name))
			var decode_ms := (Time.get_ticks_usec() - stamp) / 1000.0
			_mutex.lock()
			_metrics.decode_ms.append(decode_ms)
			_mutex.unlock()
			if image == null:
				_publish({"path": job.path, "token": job.token, "kind": "error", "error": ERR_FILE_CANT_READ})
				break
			var bytes := image.get_width() * image.get_height() * 4
			if bytes > MAX_PAGE_BYTES:
				_publish({"path": job.path, "token": job.token, "kind": "error", "error": ERR_OUT_OF_MEMORY})
				break
			_mutex.lock()
			_metrics.decoded_pages += 1
			_mutex.unlock()
			if not _publish({"path": job.path, "token": job.token, "kind": "image",
					"index": index, "image": image, "bytes": bytes}, bytes):
				break
