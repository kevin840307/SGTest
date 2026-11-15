extends Node

# Mounts the platform-independent external asset pack before gameplay starts.
# Windows and macOS use the same pack contents and res://assets paths; only
# executable/app-bundle path discovery differs here.
const PACK_RELATIVE_PATH := "assets/assets.pck"
const REQUIRED_MARKERS := [
	"res://assets/catalog/assets.json",
	"res://assets/bundles/index.json",
]

var loaded_pack_path := ""
var error_message := ""
var assets_ready := false

func _init() -> void:
	if _has_runtime_assets():
		assets_ready = true
		return
	var explicit := _argument_value("--asset-pack=")
	var candidates := [explicit] if not explicit.is_empty() else default_candidates(OS.get_executable_path(),OS.get_name())
	for candidate in candidates:
		if candidate.is_empty() or not FileAccess.file_exists(candidate):
			continue
		if ProjectSettings.load_resource_pack(candidate,true) and _has_runtime_assets():
			loaded_pack_path = candidate
			assets_ready = true
			print("asset_pack mounted "+candidate)
			return
	error_message = "runtime assets unavailable; expected external assets/assets.pck"
	push_error(error_message)

func is_ready() -> bool:
	return assets_ready

func status() -> Dictionary:
	return {
		"ready": assets_ready,
		"pack": loaded_pack_path,
		"error": error_message,
		"external": not loaded_pack_path.is_empty(),
	}

static func default_candidates(executable: String, os_name: String) -> Array[String]:
	var normalized := executable.replace("\\","/")
	var result: Array[String] = []
	var executable_dir := normalized.get_base_dir()
	_append_unique(result,executable_dir.path_join(PACK_RELATIVE_PATH))
	if os_name == "macOS":
		var marker := ".app/Contents/MacOS/"
		var position := normalized.find(marker)
		if position >= 0:
			var app_path := normalized.substr(0,position+4)
			# Standard app-bundle resource location. The pack is still a separate
			# file and uses the exact same format/content as Windows.
			_append_unique(result,app_path.path_join("Contents/Resources").path_join(PACK_RELATIVE_PATH))
			# Launcher/distribution layout shared with Windows:
			# <distribution>/<Game.app> + <distribution>/assets/assets.pck
			_append_unique(result,app_path.get_base_dir().path_join(PACK_RELATIVE_PATH))
	return result

static func _append_unique(values: Array[String], value: String) -> void:
	if not value.is_empty() and not values.has(value):
		values.append(value)

func _argument_value(prefix: String) -> String:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with(prefix):
			return argument.trim_prefix(prefix)
	return ""

func _has_runtime_assets() -> bool:
	for marker in REQUIRED_MARKERS:
		if FileAccess.file_exists(marker):
			return true
	return false
