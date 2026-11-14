extends SceneTree

var stores: Array = []

func _initialize() -> void:
	var count := 0
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--stores="):count = clampi(int(argument.trim_prefix("--stores=")),0,8)
	for index in count:
		var store := preload("res://scripts/runtime_asset_store.gd").new()
		store.start()
		stores.append(store)
	_run.call_deferred()

func _run() -> void:
	# The host measures process CPU after warmup. No world/network/images here.
	await create_timer(1.0).timeout
	print("asset_worker_idle_ready")
	await create_timer(20.0).timeout
	for store in stores:store.close()
	quit()
