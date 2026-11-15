extends SceneTree

const ATLAS_SCRIPT=preload("res://scripts/assets/entity_atlas.gd")

class AtlasCanvas:
	extends Node2D
	var atlases:Array=[]
	var entities:Array[Dictionary]=[]
	var seconds:=0.0
	var draw_cpu_usec:=0
	var draw_count:=0

	func _draw()->void:
		var started:=Time.get_ticks_usec()
		for entity in entities:
			var atlas=atlases[int(entity.atlas)]
			atlas.draw_frame(self,entity.pos,str(entity.clip),seconds+float(entity.phase),8.0)
		draw_cpu_usec+=Time.get_ticks_usec()-started
		draw_count+=1

func percentile(values:Array[float], q:float)->float:
	if values.is_empty(): return 0.0
	values.sort()
	return values[int(floor(float(values.size()-1)*q))]

func preferred_clips(atlas)->Array[String]:
	var names:Array[String]=[]
	var raw:Array=atlas.data.get("clips",{}).keys()
	raw.sort()
	var preferred=["_NS1","_NW1","_FA1","_NH1","_AA1","_AS1","_NR1","_AH1"]
	for suffix in preferred:
		for raw_name in raw:
			var name:=str(raw_name)
			if name.ends_with(suffix) and name not in names:
				names.append(name)
	for raw_name in raw:
		var name:=str(raw_name)
		if name not in names: names.append(name)
	return names

func _initialize()->void:
	call_deferred("_run")

func _run()->void:
	Engine.max_fps=0
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var args:=OS.get_cmdline_user_args()
	if args.size()<7:
		printerr("strategy root names_csv instances frames output warmup filter")
		quit(2);return
	var strategy:=str(args[0])
	var candidate_root:=str(args[1])
	var names:=str(args[2]).split(",",false)
	var instances:=int(args[3])
	var frame_count:=int(args[4])
	var output:=str(args[5])
	var warmup:=int(args[6])
	var nearest:=args.size()>7 and str(args[7])=="nearest"
	DisplayServer.window_set_size(Vector2i(1440,900))
	root.size=Vector2i(1440,900)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var baseline_tex:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var baseline_video:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var atlases:Array=[]
	var clips:Array=[]
	var load_started:=Time.get_ticks_usec()
	for name in names:
		var atlas=ATLAS_SCRIPT.new()
		var path:=candidate_root.path_join(strategy).path_join(str(name)).path_join("atlas.json")
		var err:Error=atlas.open(path)
		if err!=OK:
			printerr("atlas open failed ",path," ",err);quit(2);return
		atlases.append(atlas)
		clips.append(preferred_clips(atlas))
	var load_ms:=float(Time.get_ticks_usec()-load_started)/1000.0
	for _i in 4: await process_frame
	RenderingServer.force_sync()
	var loaded_tex:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var loaded_video:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var canvas:=AtlasCanvas.new()
	canvas.atlases=atlases
	canvas.texture_filter=CanvasItem.TEXTURE_FILTER_NEAREST if nearest else CanvasItem.TEXTURE_FILTER_LINEAR
	root.add_child(canvas)
	for i in instances:
		var atlas_index:=i%atlases.size()
		var choices:Array=clips[atlas_index]
		if choices.is_empty(): continue
		canvas.entities.append({
			"atlas":atlas_index,
			"clip":choices[i%choices.size()],
			"pos":Vector2(float((i*83)%1360)+20.0,float((i*47)%820)+20.0),
			"phase":float((i*7)%31)/8.0
		})
	for _i in 8:
		canvas.queue_redraw()
		await process_frame
	RenderingServer.force_sync()
	var ready_tex:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var ready_video:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var node_count:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var resource_count:=int(Performance.get_monitor(Performance.OBJECT_RESOURCE_COUNT))
	var intervals:Array[float]=[]
	var draws:Array[float]=[]
	var update:Array[float]=[]
	var last:=Time.get_ticks_usec()
	for frame_id in frame_count:
		var started:=Time.get_ticks_usec()
		if frame_id%18==0:
			for i in canvas.entities.size():
				var entity:Dictionary=canvas.entities[i]
				var choices:Array=clips[int(entity.atlas)]
				entity.clip=choices[(frame_id/18+i)%choices.size()]
				canvas.entities[i]=entity
		canvas.seconds=float(frame_id)/60.0
		update.append(float(Time.get_ticks_usec()-started)/1000.0)
		canvas.queue_redraw()
		await process_frame
		var now:=Time.get_ticks_usec()
		if frame_id>=warmup:
			intervals.append(float(now-last)/1000.0)
			draws.append(float(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last=now
	var result={
		"renderer":"sgtest_entity_atlas_canvas",
		"strategy":strategy,"filter":"nearest" if nearest else "linear",
		"assets":names,"instances":instances,"load_ms":load_ms,
		"texture_after_load_delta":maxi(0,loaded_tex-baseline_tex),
		"video_after_load_delta":maxi(0,loaded_video-baseline_video),
		"texture_with_instances_delta":maxi(0,ready_tex-baseline_tex),
		"video_with_instances_delta":maxi(0,ready_video-baseline_video),
		"node_count":node_count,"resource_count":resource_count,
		"update_p95_ms":percentile(update,0.95),
		"draw_submit_cpu_mean_ms":float(canvas.draw_cpu_usec)/maxi(1,canvas.draw_count)/1000.0,
		"frame_p50_ms":percentile(intervals,0.50),
		"frame_p95_ms":percentile(intervals,0.95),
		"frame_p99_ms":percentile(intervals,0.99),
		"draw_p50":percentile(draws,0.50),
		"draw_p95":percentile(draws,0.95),
	}
	var f:=FileAccess.open(output,FileAccess.WRITE)
	f.store_string(JSON.stringify(result)+"\n");f.close()
	print(JSON.stringify(result))
	quit(0)
