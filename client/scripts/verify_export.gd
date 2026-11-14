extends RefCounted

func run(report_path: String) -> int:
	var errors: Array = []
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string("res://assets/export_manifest.json"))
	var count := 0
	var asset_hashes: Dictionary = {}
	if not parsed is Dictionary or int(parsed.get("version",0)) != 1:
		errors.append("export asset manifest missing or invalid")
	else:
		for file in parsed.get("files",[]):
			var path := str(file.path)
			if not path.begins_with("res://assets/") or FileAccess.get_sha256(path) != str(file.sha256):
				errors.append("raw asset mismatch: "+path)
			asset_hashes[path] = str(file.sha256)
			count += 1
	if count == 0:errors.append("no raw assets verified")
	var result := {"passed":errors.is_empty(),"files":count,"errors":errors,
		"asset_sha256":asset_hashes,
		"source_sha256":parsed.get("source_sha256",{}) if parsed is Dictionary else {},
		"editor_build":OS.has_feature("editor"),"debug_build":OS.has_feature("debug"),
		"template_release":OS.has_feature("template_release")}
	if not report_path.is_empty():
		var output := FileAccess.open(report_path,FileAccess.WRITE)
		if output == null:return 1
		output.store_string(JSON.stringify(result,"  "))
	print("export_integrity "+JSON.stringify(result))
	return 0 if errors.is_empty() else 1
