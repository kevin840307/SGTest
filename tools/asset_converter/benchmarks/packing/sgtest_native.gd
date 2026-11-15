extends SceneTree

const ATLAS = preload("res://scripts/assets/entity_atlas.gd")
const WARMUP := 30

class BenchCanvas extends Node2D:
	var atlases:Array=[]
	var clips:Array[Array]=[]
	var instances:Array[Dictionary]=[]
	var phase:=0
	func _draw()->void:
		for entity in instances:
			var atlas:RefCounted=atlases[int(entity.visual)]
			var names:Array=clips[int(entity.visual)]
			if names.is_empty(): continue
			var clip:=str(names[int(entity.clip)%names.size()])
			var seconds:=float(phase+int(entity.offset))/8.0
			atlas.draw_frame(self,Vector2(entity.x,entity.y),clip,seconds,8.0)

func _pct(values:Array[float],q:float)->float:
	if values.is_empty(): return 0.0
	values.sort()
	return values[int(floor(float(values.size()-1)*q))]

func _initialize()->void:
	call_deferred("_run")

func _run()->void:
	var args:=OS.get_cmdline_user_args()
	if args.size()<7:
		printerr("variant_root sample_csv instances frames width height kind [output]")
		quit(2); return
	var variant_root:=str(args[0])
	var samples:Array[String]=[]
	for raw in str(args[1]).split(",",false): samples.append(str(raw))
	var count:=int(args[2]);var frame_count:=int(args[3])
	var width:=int(args[4]);var height:=int(args[5]);var kind:=str(args[6])
	DisplayServer.window_set_size(Vector2i(width,height));root.size=Vector2i(width,height)
	RenderingServer.render_loop_enabled=true
	Engine.max_fps=0
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var tex_before:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var video_before:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var nodes_before:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var canvas:=BenchCanvas.new()
	var started:=Time.get_ticks_usec()
	var rgba:=0
	for sample in samples:
		var atlas:=ATLAS.new()
		var manifest:=variant_root.path_join(sample).path_join("atlas.json")
		var err:Error=atlas.open(manifest)
		if err!=OK:
			printerr("atlas open failed "+manifest+" err="+str(err));quit(3);return
		canvas.atlases.append(atlas)
		var keys:Array=atlas.data.get("clips",{}).keys();keys.sort()
		canvas.clips.append(keys)
		for page in atlas.pages: rgba+=page.get_width()*page.get_height()*4
	var load_ms:=float(Time.get_ticks_usec()-started)/1000.0
	for i in count:
		canvas.instances.append({"visual":i%samples.size(),"clip":(i*7)%maxi(1,canvas.clips[i%samples.size()].size()),
			"offset":(i*11)%13,"x":24+((i*83)%maxi(100,width-48)),"y":32+((i*47)%maxi(80,height-64))})
	root.add_child(canvas)
	await process_frame
	var tex_after:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var video_after:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var nodes_after:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var intervals:Array[float]=[];var draws:Array[float]=[];var last:=Time.get_ticks_usec()
	for frame_id in frame_count:
		canvas.phase=frame_id;canvas.queue_redraw();await process_frame
		var now:=Time.get_ticks_usec()
		if frame_id>=WARMUP:
			intervals.append(float(now-last)/1000.0)
			draws.append(float(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last=now
	var result={"renderer_path":"sgtest_entityatlas_direct_canvas","kind":kind,"assets":samples,
		"instances":count,"frames":frame_count,"load_ms":load_ms,"atlas_rgba_bytes":rgba,
		"frame_p50_ms":_pct(intervals,.5),"frame_p95_ms":_pct(intervals,.95),"frame_p99_ms":_pct(intervals,.99),
		"draw_calls_p50":_pct(draws,.5),"draw_calls_p95":_pct(draws,.95),
		"texture_mem_delta":maxi(0,tex_after-tex_before),"video_mem_delta":maxi(0,video_after-video_before),
		"node_delta":maxi(0,nodes_after-nodes_before),"adapter":RenderingServer.get_video_adapter_name()}
	var output:=JSON.stringify(result);print(output)
	if args.size()>=8:
		var f:=FileAccess.open(str(args[7]),FileAccess.WRITE)
		if f!=null:f.store_string(output+"\n");f.close()
	quit(0)
