extends SceneTree

const Store = preload("res://assets_runtime/asset_store.gd")
var store = Store.new()
var events: Array = []
var failures: Array = []
var peer_epoch := 0


func _initialize() -> void:
	_run.call_deferred()


func check(value: bool, reason: String) -> void:
	if not value:
		failures.append(reason)
		push_error(reason)


func tick() -> void:
	store.pump()
	await create_timer(0.001).timeout


func loaded(owner: StringName, epoch: int, id: String) -> void:
	var deadline := Time.get_ticks_msec() + 10000
	while store.get_asset(owner, epoch, id).is_empty() and Time.get_ticks_msec() < deadline:
		await tick()
	check(not store.get_asset(owner, epoch, id).is_empty(), "Replacement resource timed out")


func drained() -> void:
	var deadline := Time.get_ticks_msec() + 10000
	while store.statistics().decode_jobs + store.statistics().texture_jobs > 0 and Time.get_ticks_msec() < deadline:
		await tick()
	check(store.statistics() == {"assets":0,"scopes":0,"ready":0,"decode_jobs":0,"texture_jobs":0,"rgba_bytes":0}, "Epoch test did not return to baseline")


func _ready(owner: StringName, epoch: int, id: String) -> void:
	events.append([owner, epoch, id])
	if owner == &"callback_a":
		store.end_scope(&"callback_b", peer_epoch)
		store.end_scope(owner, epoch)


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 2 or store.open_pack(args[0]) != OK:
		quit(2)
		return
	store.asset_ready.connect(_ready)
	store.asset_failed.connect(func(_owner, _epoch, _id, reason): check(false, reason))
	# Restart the same map while a metadata job is registered.
	var old := store.begin_scope(&"map")
	store.acquire(&"map", old, "maps/15")
	store.pump()
	check(store.statistics().decode_jobs == 1, "CPU cancellation setup failed")
	var current := store.begin_scope(&"map")
	store.acquire(&"map", current, "maps/15")
	await loaded(&"map", current, "maps/15")
	check(not events.has([&"map", old, "maps/15"]), "Old CPU job delivered ready")
	store.end_scope(&"map", current)
	await drained()
	# Restart the same map while its exact Texture path is in flight.
	old = store.begin_scope(&"map")
	store.acquire(&"map", old, "maps/15")
	var deadline := Time.get_ticks_msec() + 10000
	while store.statistics().texture_jobs == 0 and Time.get_ticks_msec() < deadline:
		await tick()
	check(store.statistics().texture_jobs == 1, "Texture cancellation setup failed")
	current = store.begin_scope(&"map")
	store.acquire(&"map", current, "maps/15")
	await loaded(&"map", current, "maps/15")
	check(not events.has([&"map", old, "maps/15"]), "Old Texture job delivered ready")
	store.end_scope(&"map", current)
	await drained()
	# A ready handler can release both owners before the next owner is notified.
	var first := store.begin_scope(&"callback_a")
	peer_epoch = store.begin_scope(&"callback_b")
	store.acquire(&"callback_a", first, "maps/13")
	store.acquire(&"callback_b", peer_epoch, "maps/13")
	deadline = Time.get_ticks_msec() + 10000
	while not events.has([&"callback_a", first, "maps/13"]) and Time.get_ticks_msec() < deadline:
		await tick()
	check(events.has([&"callback_a", first, "maps/13"]), "Ready callback timed out")
	check(not events.has([&"callback_b", peer_epoch, "maps/13"]), "Released peer received ready")
	await drained()
	var report := {"status":"PASS" if failures.is_empty() else "FAIL", "scenarios":3,
		"failures":failures, "final":store.statistics()}
	var file := FileAccess.open(args[1], FileAccess.WRITE)
	file.store_string(JSON.stringify(report, "\t"))
	print("ASSETSTORE_RESULT ", JSON.stringify(report))
	quit(0 if failures.is_empty() else 1)
