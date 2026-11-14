extends SceneTree

const STORE = preload("res://scripts/runtime_asset_store.gd")

func wait_ready(store: RefCounted, path: String, frames: int = 240) -> bool:
	for _i in frames:
		store.poll_gpu(1, 1200)
		if store.is_ready(path):
			return true
		if store.is_failed(path):
			return false
		await process_frame
	return false

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var store := STORE.new()
	store.configure(16 * 1024 * 1024, 4 * 1024 * 1024)
	var owner_a := "test-map-a"
	var epoch_a := store.begin_owner(owner_a)
	var path := "res://assets/generated/bundles/map/13/sprites.json"
	assert(store.acquire(path, owner_a, epoch_a, 10) == OK)
	assert(await wait_ready(store, path), "async atlas did not become ready")
	var atlas: RefCounted = store.get_atlas(path)
	assert(atlas != null and not atlas.pages.is_empty())
	assert(not atlas.sample("EN010A_NS2", 0.1).is_empty())
	var bytes := store.bytes_for(path)
	assert(bytes > 0 and store.resident_bytes() >= bytes)

	# A second owner shares the exact atlas instead of reloading it.
	var owner_b := "test-map-b"
	var epoch_b := store.begin_owner(owner_b)
	assert(store.acquire(path, owner_b, epoch_b, 5) == OK)
	assert(store.get_atlas(path) == atlas)
	store.release_owner(owner_a)
	assert(store.is_ready(path), "shared owner must keep atlas resident")
	store.release_owner(owner_b)
	assert(store.is_ready(path), "unowned atlas should remain bounded cache until eviction")

	# Reacquire from cache, then prove explicit owner purge can return to zero.
	var owner_c := "test-map-c"
	var epoch_c := store.begin_owner(owner_c)
	assert(store.acquire(path, owner_c, epoch_c, 1) == OK)
	assert(store.get_atlas(path) == atlas)
	store.release_owner(owner_c)
	assert(store.purge_unowned(true) >= 1)
	assert(store.resident_bytes() == 0)

	# Pending work becomes stale when its owner generation is released.
	var cancel_owner := "cancel-map"
	var cancel_epoch := store.begin_owner(cancel_owner)
	var cancel_path := "res://assets/generated/bundles/session/player-b-base.json"
	assert(store.acquire(cancel_path, cancel_owner, cancel_epoch, 1) == OK)
	store.release_owner(cancel_owner)
	for _i in 8:
		store.poll_gpu(1, 1200)
		await process_frame
	assert(not store.is_ready(cancel_path), "cancelled generation must not resurrect stale atlas")

	# Failed unowned entries must be evicted so a later generation can retry the
	# exact same path instead of permanently consuming MAX_JOBS.
	var retry_path := "user://runtime_asset_store_retry.json"
	DirAccess.remove_absolute(ProjectSettings.globalize_path(retry_path))
	var fail_owner := "failed-map"
	var fail_epoch := store.begin_owner(fail_owner)
	assert(store.acquire(retry_path, fail_owner, fail_epoch, 1) == OK)
	for _i in 60:
		store.poll_gpu(1, 1200)
		if store.is_failed(retry_path):
			break
		await process_frame
	assert(store.is_failed(retry_path), "missing runtime manifest should fail")
	store.release_owner(fail_owner)
	assert(store.error(retry_path) == ERR_DOES_NOT_EXIST, "unowned failed entry must be purged")
	var file := FileAccess.open(retry_path, FileAccess.WRITE)
	assert(file != null)
	file.store_string(JSON.stringify({"version":9,"pages":[],"frames":[],"clips":{},"aliases":{},"act":{}}))
	file.close()
	var retry_owner := "retry-map"
	var retry_epoch := store.begin_owner(retry_owner)
	assert(store.acquire(retry_path, retry_owner, retry_epoch, 1) == OK)
	assert(await wait_ready(store, retry_path), "later generation must be able to retry a previously failed path")
	store.release_owner(retry_owner)
	store.purge_unowned(true)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(retry_path))

	var stats := store.statistics()
	assert(int(stats.cache_misses) >= 4)
	assert(int(stats.cache_hits) >= 2)
	assert(int(stats.cancelled_jobs) >= 1)
	store.close()
	assert(store.resident_bytes() == 0)

	# Automatic cache cap: once the last owner releases a ready atlas larger
	# than the budget, LRU eviction must happen without a force purge.
	var capped := STORE.new()
	capped.configure(1, 4 * 1024 * 1024)
	var cap_owner := "tiny-cache"
	var cap_epoch := capped.begin_owner(cap_owner)
	assert(capped.acquire(path, cap_owner, cap_epoch, 1) == OK)
	assert(await wait_ready(capped, path))
	assert(capped.resident_bytes() > 1)
	capped.release_owner(cap_owner)
	assert(capped.resident_bytes() == 0, "cache budget must evict unowned atlas automatically")
	assert(not capped.is_ready(path))
	assert(int(capped.statistics().evictions) >= 1)
	capped.close()

	print("runtime asset store owner/share/cache-cap/retry/cancel/purge passed")
	quit()
