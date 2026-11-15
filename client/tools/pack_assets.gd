extends SceneTree

# Platform-independent raw asset PCK builder. It does not import textures and
# does not depend on EditorPlugin. All files below client/assets are packed at
# the same res://assets/... path, so RuntimeAssetStore sees no platform change.

func _init() -> void:
	var source := ProjectSettings.globalize_path("res://assets")
	var output := ""
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--source="):
			source = argument.trim_prefix("--source=")
		elif argument.begins_with("--output="):
			output = argument.trim_prefix("--output=")
	if output.is_empty():
		push_error("--output=<assets.pck> is required")
		quit(2)
		return
	source = source.simplify_path()
	output = output.simplify_path()
	if not DirAccess.dir_exists_absolute(source):
		push_error("asset source directory does not exist: "+source)
		quit(2)
		return
	var files := _collect_files(source)
	if files.is_empty():
		push_error("asset source contains no files: "+source)
		quit(2)
		return
	var mkdir_error := DirAccess.make_dir_recursive_absolute(output.get_base_dir())
	if mkdir_error != OK:
		push_error("cannot create asset pack directory: %s" % mkdir_error)
		quit(2)
		return
	var packer := PCKPacker.new()
	var error := packer.pck_start(output)
	if error != OK:
		push_error("pck_start failed: %s" % error)
		quit(2)
		return
	var total_bytes := 0
	for relative in files:
		var source_path := source.path_join(relative)
		var target_path := "res://assets/"+relative.replace("\\","/")
		error = packer.add_file(target_path,source_path,false)
		if error != OK:
			push_error("add_file failed: %s -> %s (%s)" % [source_path,target_path,error])
			quit(2)
			return
		var source_file := FileAccess.open(source_path,FileAccess.READ)
		if source_file == null:
			push_error("cannot stat packed file: "+source_path)
			quit(2)
			return
		total_bytes += source_file.get_length()
	error = packer.flush(false)
	if error != OK:
		push_error("PCK flush failed: %s" % error)
		quit(2)
		return
	print("asset_pack_build "+JSON.stringify({
		"files": files.size(),
		"source_bytes": total_bytes,
		"output": output,
		"output_bytes": FileAccess.open(output,FileAccess.READ).get_length(),
		"sha256": FileAccess.get_sha256(output),
	}))
	quit(0)

func _collect_files(root: String) -> Array[String]:
	var result: Array[String] = []
	var pending: Array[String] = [""]
	while not pending.is_empty():
		var relative_dir: String = str(pending.pop_back())
		var absolute_dir: String = root if relative_dir.is_empty() else root.path_join(relative_dir)
		var directory: DirAccess = DirAccess.open(absolute_dir)
		if directory == null:
			continue
		for child in directory.get_directories():
			if child.begins_with("."):
				continue
			pending.append(child if relative_dir.is_empty() else relative_dir.path_join(child))
		for name in directory.get_files():
			if name.begins_with(".") or name.ends_with(".tmp"):
				continue
			result.append(name if relative_dir.is_empty() else relative_dir.path_join(name))
	result.sort()
	return result
