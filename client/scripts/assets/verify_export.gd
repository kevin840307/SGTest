extends RefCounted

func run(report_path: String) -> int:
	var errors: Array[String] = []
	var pack = Engine.get_main_loop().root.get_node_or_null("AssetPack")
	var status: Dictionary = pack.status() if pack != null else {}
	if pack == null or not bool(status.get("ready",false)):
		errors.append("external asset pack loader is not ready")
	if str(status.get("pack","")).is_empty():
		errors.append("release verification requires an external asset pack")
	var markers := []
	for path in ["res://assets/catalog/assets.json","res://assets/bundles/index.json"]:
		if FileAccess.file_exists(path):
			markers.append(path)
	if markers.is_empty():
		errors.append("no runtime asset catalog/index found after pack mount")
	var pack_path := str(status.get("pack",""))
	var result := {
		"passed": errors.is_empty(),
		"errors": errors,
		"external_asset_pack": not pack_path.is_empty(),
		"asset_pack_path": pack_path,
		"asset_pack_sha256": FileAccess.get_sha256(pack_path) if not pack_path.is_empty() and FileAccess.file_exists(pack_path) else "",
		"markers": markers,
		"editor_build": OS.has_feature("editor"),
		"debug_build": OS.has_feature("debug"),
		"template_release": OS.has_feature("template_release"),
	}
	if not report_path.is_empty():
		var output := FileAccess.open(report_path,FileAccess.WRITE)
		if output == null:
			return 1
		output.store_string(JSON.stringify(result,"  "))
	print("export_integrity "+JSON.stringify(result))
	return 0 if errors.is_empty() else 1
