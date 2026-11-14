extends RefCounted

# One decoder thread owns each Image until handing it to the bounded queue.
# Only the main thread creates ImageTexture; poll_gpu limits commits per frame.
const ATLAS = preload("res://scripts/entity_atlas.gd")
const READY_BUDGET := 8*1024*1024
const MAX_PAGE_BYTES := 64*1024*1024
const MAX_JOBS := 64
var mutex := Mutex.new()
var worker := Thread.new()
var running := false
var generation := 0
var next_id := 1
var requests: Array = []
var decoded: Array = []
var ready_bytes := 0
var jobs: Dictionary = {}
var complete: Dictionary = {}
var errors: Dictionary = {}
var metrics := {"decoded_pages":0,"uploaded_pages":0,"peak_ready_bytes":0,
	"peak_decode_bytes":0,"decode_ms":[],"upload_submit_ms":[]}

func start() -> void:
	if worker.is_started():
		return
	running = true
	worker.start(_decode_loop)

func request(path: String) -> int:
	start()
	if jobs.size() >= MAX_JOBS:
		return -1
	var id := next_id
	next_id += 1
	jobs[id] = {"atlas":null,"remaining":-1}
	mutex.lock()
	requests.append({"id":id,"path":path,"generation":generation})
	mutex.unlock()
	return id

func _active(epoch: int) -> bool:
	mutex.lock()
	var result := running and generation == epoch
	mutex.unlock()
	return result

func _publish(item: Dictionary, bytes: int = 0) -> bool:
	while _active(int(item.generation)):
		mutex.lock()
		# Permit one larger page, never an unbounded collection of decoded RGBA.
		if bytes == 0 or ready_bytes == 0 or ready_bytes+bytes <= READY_BUDGET:
			decoded.append(item)
			ready_bytes += bytes
			metrics.peak_ready_bytes = maxi(metrics.peak_ready_bytes,ready_bytes)
			mutex.unlock()
			return true
		mutex.unlock()
		OS.delay_msec(2)
	return false

func _decode_loop() -> void:
	while true:
		mutex.lock()
		var active := running
		var job: Dictionary = requests.pop_front() if not requests.is_empty() else {}
		mutex.unlock()
		if not active:
			return
		if job.is_empty():
			OS.delay_msec(2)
			continue
		var data: Variant = JSON.parse_string(FileAccess.get_file_as_string(job.path))
		if not data is Dictionary or int(data.get("version",0)) not in [3,4,5,6,7,8,9] or not data.get("pages") is Array:
			_publish({"id":job.id,"generation":job.generation,"kind":"error","error":ERR_FILE_CORRUPT})
			continue
		if not _publish({"id":job.id,"generation":job.generation,"kind":"metadata","data":data}):
			continue
		for index in data.pages.size():
			if not _active(int(job.generation)):
				break
			var name := str(data.pages[index])
			if name != name.get_file() or name.is_absolute_path():
				_publish({"id":job.id,"generation":job.generation,"kind":"error","error":ERR_INVALID_DATA})
				break
			var started := Time.get_ticks_usec()
			var image := Image.load_from_file(str(job.path).get_base_dir().path_join(name))
			if image == null:
				_publish({"id":job.id,"generation":job.generation,"kind":"error","error":ERR_FILE_CANT_READ})
				break
			var bytes := image.get_width()*image.get_height()*4
			mutex.lock()
			metrics.peak_decode_bytes = maxi(metrics.peak_decode_bytes,ready_bytes+bytes)
			metrics.decode_ms.append((Time.get_ticks_usec()-started)/1000.0)
			metrics.decoded_pages += 1
			mutex.unlock()
			if bytes > MAX_PAGE_BYTES:
				_publish({"id":job.id,"generation":job.generation,"kind":"error","error":ERR_OUT_OF_MEMORY})
				break
			if not _publish({"id":job.id,"generation":job.generation,"kind":"image","index":index,"image":image,"bytes":bytes},bytes):
				break

func poll_gpu(max_pages: int = 1, budget_usec: int = 2000) -> int:
	var uploaded := 0
	var started := Time.get_ticks_usec()
	while uploaded < max_pages and Time.get_ticks_usec()-started < budget_usec:
		mutex.lock()
		var item: Dictionary = decoded.pop_front() if not decoded.is_empty() else {}
		if not item.is_empty():
			ready_bytes -= int(item.get("bytes",0))
		mutex.unlock()
		if item.is_empty():
			break
		if int(item.generation) != generation or not jobs.has(item.id):
			continue
		var record: Dictionary = jobs[item.id]
		if item.kind == "error":
			errors[item.id] = item.error
			jobs.erase(item.id)
			continue
		if item.kind == "metadata":
			var atlas := ATLAS.new()
			atlas.data = item.data
			atlas.pages.resize(item.data.pages.size())
			record.atlas = atlas
			record.remaining = atlas.pages.size()
		else:
			var stamp := Time.get_ticks_usec()
			record.atlas.pages[int(item.index)] = ImageTexture.create_from_image(item.image)
			mutex.lock()
			metrics.upload_submit_ms.append((Time.get_ticks_usec()-stamp)/1000.0)
			metrics.uploaded_pages += 1
			mutex.unlock()
			record.remaining -= 1
			uploaded += 1
		if record.remaining == 0:
			complete[item.id] = record.atlas
			jobs.erase(item.id)
	return uploaded

func finished(id: int) -> bool:
	return complete.has(id) or errors.has(id)

func take(id: int) -> RefCounted:
	var atlas: RefCounted = complete.get(id)
	complete.erase(id)
	return atlas

func statistics() -> Dictionary:
	mutex.lock()
	var result := metrics.duplicate(true)
	mutex.unlock()
	return result

func reset() -> void:
	mutex.lock()
	generation += 1
	requests.clear()
	decoded.clear()
	ready_bytes = 0
	mutex.unlock()
	jobs.clear()
	complete.clear()
	errors.clear()

func close() -> void:
	reset()
	mutex.lock()
	running = false
	mutex.unlock()
	if worker.is_started():
		worker.wait_to_finish()
