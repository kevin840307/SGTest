extends SceneTree

const WARMUP := 30

func _pct(values:Array[float], q:float)->float:
	if values.is_empty(): return 0.0
	values.sort()
	return values[int(floor(float(values.size()-1)*q))]

func _loader(name:String)->Node:
	return root.get_node_or_null(name)

func _load_frames(kind:String, names:Array[String])->Dictionary:
	var started:=Time.get_ticks_usec()
	var loaded:Dictionary={}
	if kind=="enemy":
		var loader:=_loader("MonsterLoader")
		if loader==null: return {}
		for name in names:
			var sf:SpriteFrames=loader.get_monster_frames(name,false)
			if sf==null: return {}
			loaded[name]=sf
	else:
		var loader:=_loader("DollLoader")
		if loader==null: return {}
		for name in names:
			var category:=name.substr(0,1)+"/Equip"
			var sf:SpriteFrames=loader.get_sprite_frames(category,name)
			if sf==null: return {}
			loaded[name]=sf
	loaded["_load_ms"]=float(Time.get_ticks_usec()-started)/1000.0
	return loaded

func _initialize()->void:
	call_deferred("_run")

func _run()->void:
	var args:=OS.get_cmdline_user_args()
	if args.size()<6:
		printerr("kind names_csv instances frames width height [output]")
		quit(2); return
	var kind:=str(args[0])
	var names:Array[String]=[]
	for raw in str(args[1]).split(",",false):
		names.append(str(raw))
	var count:=int(args[2]); var frame_count:=int(args[3])
	var width:=int(args[4]); var height:=int(args[5])
	DisplayServer.window_set_size(Vector2i(width,height))
	root.size=Vector2i(width,height)
	# Benchmark override only: Recovered production intentionally enters
	# background power-save when the benchmark window is not focused.
	# Keep the visual renderer active here so we measure Model1, not AFK/unfocus.
	RenderingServer.render_loop_enabled=true
	Engine.max_fps=0
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	await process_frame
	RenderingServer.render_loop_enabled=true
	Engine.max_fps=0
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var tex_before:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var video_before:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var nodes_before:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var frames_by_name:=_load_frames(kind,names)
	if frames_by_name.is_empty():
		printerr("load failed"); quit(3); return
	var load_ms:=float(frames_by_name["_load_ms"]); frames_by_name.erase("_load_ms")
	var holder:=Node2D.new(); root.add_child(holder)
	var sprites:Array[AnimatedSprite2D]=[]
	for i in count:
		var name:=names[i%names.size()]
		var sf:SpriteFrames=frames_by_name[name]
		var sprite:=AnimatedSprite2D.new()
		sprite.sprite_frames=sf
		var anims:=sf.get_animation_names()
		if anims.size()>0:
			sprite.animation=anims[(i*7)%anims.size()]
			sprite.frame=(i*11)%maxi(1,sf.get_frame_count(sprite.animation))
			sprite.play()
		sprite.centered = kind=="enemy"
		sprite.position=Vector2(24+((i*83)%maxi(100,width-48)),32+((i*47)%maxi(80,height-64)))
		holder.add_child(sprite); sprites.append(sprite)
	await process_frame
	var tex_after:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var video_after:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var nodes_after:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var intervals:Array[float]=[]; var draws:Array[float]=[]
	var last:=Time.get_ticks_usec()
	var changes:=0
	for frame_id in frame_count:
		# Focus callbacks can fire while automated windows are switched.
		RenderingServer.render_loop_enabled=true
		Engine.max_fps=0
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
		if frame_id>0 and frame_id%19==0:
			for i in range(0,sprites.size(),7):
				var sprite:=sprites[i]
				var anims:=sprite.sprite_frames.get_animation_names()
				if anims.size()>1:
					sprite.animation=anims[(frame_id/19+i)%anims.size()]
					sprite.play(); changes+=1
		await process_frame
		var now:=Time.get_ticks_usec()
		if frame_id>=WARMUP:
			intervals.append(float(now-last)/1000.0)
			draws.append(float(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last=now
	var result={
		"renderer_path":"recovered_native_animatedsprite2d",
		"kind":kind,"assets":names,"instances":count,"frames":frame_count,
		"load_ms":load_ms,"frame_p50_ms":_pct(intervals,.5),
		"frame_p95_ms":_pct(intervals,.95),"frame_p99_ms":_pct(intervals,.99),
		"draw_calls_p50":_pct(draws,.5),"draw_calls_p95":_pct(draws,.95),
		"texture_mem_delta":maxi(0,tex_after-tex_before),
		"video_mem_delta":maxi(0,video_after-video_before),
		"node_delta":maxi(0,nodes_after-nodes_before),"animation_changes":changes,
		"adapter":RenderingServer.get_video_adapter_name()
	}
	var output:=JSON.stringify(result); print(output)
	if args.size()>=7:
		var f:=FileAccess.open(str(args[6]),FileAccess.WRITE)
		if f!=null: f.store_string(output+"\n");f.close()
	quit(0)
