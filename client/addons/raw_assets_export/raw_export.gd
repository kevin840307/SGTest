@tool
extends EditorExportPlugin

func _get_name() -> String:
	return "SGTestRawStreamingAssets"

func _export_begin(_features: PackedStringArray, _is_debug: bool, _path: String, _flags: int) -> void:
	# assets/.gdignore keeps generated files out of Godot's importer. Explicitly
	# pack original PNG bytes; the runtime owns decoding and GPU submission.
	var root := OS.get_environment("SGTEST_EXPORT_ASSETS")
	if root.is_empty():root = "res://assets"
	root = root.trim_suffix("/")
	var pending: Array[String] = [root]
	var files: Array = []
	while not pending.is_empty():
		var folder: String = pending.pop_back()
		for directory in DirAccess.get_directories_at(folder):
			if not directory.begins_with("."):pending.append(folder.path_join(directory))
		for name in DirAccess.get_files_at(folder):
			if name.get_extension() not in ["png","json","bin"]:continue
			var path := folder.path_join(name)
			var packed_path := "res://assets/"+path.trim_prefix(root+"/")
			var bytes := FileAccess.get_file_as_bytes(path)
			add_file(packed_path,bytes,false)
			files.append({"path":packed_path,"sha256":FileAccess.get_sha256(path),"bytes":bytes.size()})
	files.sort_custom(func(a: Dictionary,b: Dictionary) -> bool: return str(a.path) < str(b.path))
	var sources := {"res://project.godot":FileAccess.get_sha256("res://project.godot")}
	pending.assign(["res://scripts","res://scenes","res://shaders"])
	while not pending.is_empty():
		var folder: String = pending.pop_back()
		for directory in DirAccess.get_directories_at(folder):
			if not directory.begins_with("."):pending.append(folder.path_join(directory))
		for name in DirAccess.get_files_at(folder):
			if name.get_extension() in ["gd","tscn","gdshader"]:
				var path := folder.path_join(name)
				sources[path] = FileAccess.get_sha256(path)
	add_file("res://assets/export_manifest.json",JSON.stringify({"version":1,"files":files,"source_sha256":sources}).to_utf8_buffer(),false)
