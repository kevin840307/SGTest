extends SceneTree
## UI-006 / HK common texture loader: assets synced after first lookup
## must become visible without restarting the client. Probe is gitignored.
const UiTextures = preload("res://ui/ui_texture_loader.gd")
const PROBE := "res://.cache/v14_ui_texture_loader_probe.png"
var failures: Array[String] = []

func check(condition: bool, message: String) -> void:
	if not condition:
		failures.append(message)
		push_error(message)

func _initialize() -> void:
	run.call_deferred()

func run() -> void:
	var absolute := ProjectSettings.globalize_path(PROBE)
	var parent := absolute.get_base_dir()
	check(DirAccess.make_dir_recursive_absolute(parent) == OK,
		"UI loader probe directory unavailable")
	if FileAccess.file_exists(PROBE):
		DirAccess.remove_absolute(absolute)

	UiTextures.clear_cache()
	check(UiTextures.get_texture(PROBE) == null,
		"absent HK PNG was not reported as missing")

	var bitmap := Image.create(3, 2, false, Image.FORMAT_RGBA8)
	bitmap.fill(Color(0.25, 0.5, 0.75, 1.0))
	check(bitmap.save_png(PROBE) == OK,
		"could not stage an original-image-format test PNG")
	var loaded := UiTextures.get_texture(PROBE)
	check(loaded is Texture2D and loaded.get_size() == Vector2(3, 2),
		"UI PNG remained missing after its source appeared")
	check(UiTextures.get_texture(PROBE) == loaded,
		"valid UI PNG was not cached for subsequent control draws")

	check(DirAccess.remove_absolute(absolute) == OK,
		"UI loader probe cleanup failed")
	UiTextures.clear_cache()
	check(UiTextures.get_texture(PROBE) == null,
		"UI loader cache reset retained a removed image")
	if failures.is_empty():
		print("UI_TEXTURE_LATE_SOURCE_PASS missing_retry=true valid_cache=true clear_cache=true")
	quit(0 if failures.is_empty() else 1)
