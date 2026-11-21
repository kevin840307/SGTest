extends SceneTree
## Local original-PCK Windows GPU diagnostic for map loads, per-frame stalls,
## and asset worker lifecycle. Never creates players/network authority.
const View = preload("res://world/world_view.gd")

func _initialize() -> void:
	_run.call_deferred()

func _percentile(values: Array, percentile: float) -> float:
	if values.is_empty():
		return 0.0
	var sorted := values.duplicate()
	sorted.sort()
	return float(sorted[mini(sorted.size()-1,int(floor(float(sorted.size()-1)*percentile)))])

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() not in [2,3]:
		push_error("MAP_RUNTIME_PROFILE_USAGE -- <private-assets.pck> <report-path> [texture_jobs]")
		quit(2)
		return
	root.size = Vector2i(1152,620)
	var v = View.new()
	root.add_child(v)
	v.show_players = true
	if args.size() == 3:
		v.store.max_texture_jobs = clampi(int(args[2]),1,4)
	if v.open_pack(str(args[0])) != OK:
		push_error("MAP_RUNTIME_PROFILE_PACK_NOT_READY")
		quit(2)
		return
	var rows: Array = []
	for id in [15,13,15]:
		var requested := Time.get_ticks_msec()
		if v.request_map(id) != OK:
			push_error("MAP_RUNTIME_PROFILE_MAP_REQUEST_FAILED %d" % id)
			quit(1)
			return
		var peak_assets := 0
		var peak_rgba := 0
		while v.phase in ["map","palette"] and Time.get_ticks_msec()-requested < 30000:
			var stats: Dictionary = v.store.statistics()
			peak_assets = maxi(peak_assets,int(stats.assets))
			peak_rgba = maxi(peak_rgba,int(stats.rgba_bytes))
			await process_frame
		if v.phase != "ready":
			push_error("MAP_RUNTIME_PROFILE_MAP_FAILED %d phase=%s" % [id,v.phase])
			quit(1)
			return
		var elapsed := Time.get_ticks_msec()-requested
		var previous := Time.get_ticks_usec()
		var frames: Array[float] = []
		for j in 130:
			v.camera.pan(v.epoch,Vector2(1.9,0.7))
			await process_frame
			var now := Time.get_ticks_usec()
			frames.append(float(now-previous)/1000.0)
			previous = now
		var result := {
			"map":id,"loading_ms":elapsed,"palette_assets":peak_assets,
			"reserved_mb":float(peak_rgba)/(1024.0*1024.0),
			"frame_median_ms":_percentile(frames,0.50),
			"frame_p95_ms":_percentile(frames,0.95),
			"frame_p99_ms":_percentile(frames,0.99),
			"frame_max_ms":_percentile(frames,1.0),
			"sample_frames":frames.size()
		}
		rows.append(result)
		print("MAP_RUNTIME_PROFILE %s" % JSON.stringify(result))
	v.clear_map()
	var deadline := Time.get_ticks_msec()+10000
	while v.store.statistics().decode_jobs+v.store.statistics().texture_jobs>0 and Time.get_ticks_msec()<deadline:
		v.store.pump(2)
		await process_frame
	var drained: Dictionary = v.store.statistics()
	var report := {"schema":"sgtest.map_runtime_profile/v1",
		"client_os":OS.get_name(),"render_backend":RenderingServer.get_rendering_device()!=null,
		"maps":rows,"final_store":drained}
	var file = FileAccess.open(str(args[1]),FileAccess.WRITE)
	if file == null:
		push_error("MAP_RUNTIME_PROFILE_REPORT_WRITE_FAILED")
		quit(2)
		return
	file.store_string(JSON.stringify(report,"  "))
	file.close()
	var success := int(drained.assets)==0 and int(drained.scopes)==0 and int(drained.decode_jobs)==0 and int(drained.texture_jobs)==0 and int(drained.rgba_bytes)==0
	print("MAP_RUNTIME_PROFILE_END resources_drained=%s" % str(success))
	v.queue_free()
	await process_frame
	quit(0 if success else 1)
