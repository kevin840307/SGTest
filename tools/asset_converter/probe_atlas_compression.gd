extends SceneTree

# Test-only readback of the exact imported Texture2D selected by this device.
func _initialize() -> void:
	call_deferred("run")

func run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 3 or not ProjectSettings.load_resource_pack(args[0],true):
		push_error("Expected PCK, contract and output directory")
		quit(2)
		return
	var contract = JSON.parse_string(FileAccess.get_file_as_string(args[1]))
	var rows := []
	for item in contract:
		var texture := ResourceLoader.load(item.path,"Texture2D",ResourceLoader.CACHE_MODE_IGNORE) as Texture2D
		if texture == null:
			push_error("Cannot load probe texture: " + str(item.path))
			quit(3)
			return
		var image := texture.get_image()
		var format := image.get_format()
		var physical := image.get_size()
		var compressed_bytes := image.get_data().size()
		if not image.is_compressed() or image.decompress() != OK:
			push_error("Expected decompressible VRAM texture")
			quit(4)
			return
		var output := args[2].path_join(str(rows.size()) + ".png")
		if image.save_png(output) != OK:
			quit(5)
			return
		rows.append({"path":item.path,"file":output,"format":format,
			"logical":[texture.get_width(),texture.get_height()],
			"physical":[physical.x,physical.y],"compressed_bytes":compressed_bytes})
		texture = null
		image = null
		await process_frame
	var result := FileAccess.open(args[2].path_join("readback.json"),FileAccess.WRITE)
	result.store_string(JSON.stringify({"device":RenderingServer.get_video_adapter_name(),
		"godot":Engine.get_version_info(),"rows":rows}))
	result.close()
	print("ATLAS_COMPRESSION_PROBE_PASS ",rows.size())
	quit(0)
