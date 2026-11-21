extends SceneTree
## Loading bar visual and truthful resource readiness, independent of TLS.
const Loading = preload("res://ui/world_loading_overlay.gd")
const App = preload("res://app/main.gd")

func _initialize() -> void:
	_run.call_deferred()

func _fail(loading: Node, reason: String) -> void:
	if loading != null:
		loading.queue_free()
	push_error("WORLD_LOADING_UI_FAIL "+reason)
	quit(1)

func _run() -> void:
	var loading = Loading.new()
	root.add_child(loading)
	loading.resize_view(Vector2(1152,620))
	if loading.visible:
		_fail(loading,"loading UI must be hidden before entering a map")
		return
	loading.update_state("map",15,0,1,1,0,true)
	if not loading.visible or loading.progress_value() < 1.0 or loading.progress_value() > 15.0:
		_fail(loading,"map decoding did not expose a visible bounded progress indicator")
		return
	if loading._bar_art.texture == null and FileAccess.file_exists(Loading.LOCAL_BACKGROUND):
		_fail(loading,"local Recovered2 BG_LOADING source not used")
		return
	loading.update_state("palette",15,1,10,2,1,true)
	var first := loading.progress_value()
	loading.update_state("palette",15,5,10,2,1,true)
	if loading.progress_value() <= first or loading.progress_value() >= 100:
		_fail(loading,"palette loading progress did not reflect ready assets")
		return
	var before := loading.progress_value()
	loading.update_state("palette",15,3,10,2,1,true)
	if loading.progress_value() < before:
		_fail(loading,"same-map progress rewound on transient asset state")
		return
	loading.update_state("palette",13,0,12,1,1,true)
	if loading.progress_value() >= before:
		_fail(loading,"new map did not reset the loading progress")
		return
	loading.update_state("ready",13,12,12,0,0,true)
	if loading.visible:
		_fail(loading,"completed map still blocks mouse/gameplay")
		return
	loading.update_state("palette",13,2,12,1,1,false)
	if loading.visible:
		_fail(loading,"background map preload must not obscure login UI")
		return
	loading.queue_free()
	await process_frame
	print("WORLD_LOADING_UI_PASS progress_by_real_asset_count=true local_image_optional=true map_epoch_resets=true no_login_overlay=true hides_on_ready=true")
	quit(0)
