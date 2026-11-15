extends SceneTree

func _init() -> void:
	var output := ""
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--output="):
			output = argument.trim_prefix("--output=")
	if output.is_empty():
		push_error("--output=<assets.pck> is required")
		quit(2)
		return
	var source_root := ProjectSettings.globalize_path("res://assets")
	if not DirAccess.dir_exists_absolute(source_root):
		push_error("staged assets missing: " + source_root)
		quit(2)
		return
	DirAccess.make_dir_recursive_absolute(output.get_base_dir())
	var pck := PCKPacker.new()
	var error := pck.pck_start(output)
	if error != OK:
		push_error("pck_start failed: %s" % error)
		quit(3)
		return
	var files: Array[String] = []
	_collect(source_root, "", files)
	var imported: Dictionary = {}
	var source_bytes := 0
	for relative in files:
		var absolute := source_root.path_join(relative)
		var virtual := "res://assets/" + relative.replace("\\", "/")
		error = pck.add_file(virtual, absolute, false)
		if error != OK:
			push_error("add_file failed: %s" % virtual)
			quit(4)
			return
		var handle := FileAccess.open(absolute, FileAccess.READ)
		if handle != null:
			source_bytes += handle.get_length()
		if relative.ends_with(".import"):
			for imported_path in _dest_files(absolute):
				if imported.has(imported_path):
					continue
				var imported_absolute := ProjectSettings.globalize_path(imported_path)
				if not FileAccess.file_exists(imported_absolute):
					push_error("missing imported resource: " + imported_path)
					quit(5)
					return
				error = pck.add_file(imported_path, imported_absolute, false)
				if error != OK:
					push_error("add imported failed: " + imported_path)
					quit(6)
					return
				imported[imported_path] = true
	error = pck.flush(false)
	if error != OK:
		push_error("flush failed: %s" % error)
		quit(7)
		return
	print("imported_asset_pack " + JSON.stringify({
		"source_files": files.size(),
		"imported_files": imported.size(),
		"source_bytes": source_bytes,
		"output": output,
		"output_bytes": FileAccess.open(output, FileAccess.READ).get_length(),
		"sha256": FileAccess.get_sha256(output),
	}))
	quit(0)

func _dest_files(import_path: String) -> Array[String]:
	var result: Array[String] = []
	var text := FileAccess.get_file_as_string(import_path)
	for line in text.split("\n"):
		if not line.begins_with("dest_files="):
			continue
		var parsed = JSON.parse_string(line.trim_prefix("dest_files="))
		if parsed is Array:
			for path in parsed:
				result.append(str(path))
	return result

func _collect(root: String, relative: String, out: Array[String]) -> void:
	var directory_path := root if relative.is_empty() else root.path_join(relative)
	var directory := DirAccess.open(directory_path)
	if directory == null:
		return
	for child in directory.get_directories():
		if child.begins_with("."):
			continue
		_collect(root, child if relative.is_empty() else relative.path_join(child), out)
	for name in directory.get_files():
		if name.begins_with(".") or name.ends_with(".tmp"):
			continue
		out.append(name if relative.is_empty() else relative.path_join(name))
	out.sort()
