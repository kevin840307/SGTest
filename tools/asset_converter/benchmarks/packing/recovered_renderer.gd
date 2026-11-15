extends SceneTree

func percentile(values:Array[float], q:float)->float:
	if values.is_empty(): return 0.0
	values.sort()
	return values[int(floor(float(values.size()-1)*q))]

func choose_animation(frames:SpriteFrames, index:int)->StringName:
	var preferred=["IDLE_1","WALK_1","ATTACK_1","HIT_1","IDLE_2","WALK_2","ATTACK_2","HIT_2"]
	for offset in preferred.size():
		var name:=StringName(preferred[(index+offset)%preferred.size()])
		if frames.has_animation(name): return name
	var names:=frames.get_animation_names()
	return names[index%names.size()] if not names.is_empty() else &"default"

func _initialize()->void:
	call_deferred("_run")

func _run()->void:
	var args:=OS.get_cmdline_user_args()
	if args.size()<6:
		printerr("kind assets_csv instances frames output warmup")
		quit(2);return
	var kind:=str(args[0])
	var asset_ids:=str(args[1]).split(",",false)
	var instances:=int(args[2])
	var frame_count:=int(args[3])
	var output:=str(args[4])
	var warmup:=int(args[5])
	DisplayServer.window_set_size(Vector2i(1440,900))
	root.size=Vector2i(1440,900)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)

	var baseline_tex:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var baseline_video:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var loader:Node
	var frames_by_id:Dictionary={}
	var load_started:=Time.get_ticks_usec()
	if kind=="monster":
		loader=load("res://scripts/monster_loader.gd").new()
		root.add_child(loader)
		for id in asset_ids:
			var sf:SpriteFrames=loader.get_monster_frames(str(id),true)
			if sf==null:
				printerr("missing monster ",id);quit(2);return
			frames_by_id[str(id)]=sf
	elif kind=="part":
		loader=load("res://scripts/paper_doll_loader.gd").new()
		root.add_child(loader)
		for id in asset_ids:
			var sf:SpriteFrames=loader.get_sprite_frames("B/Equip",str(id))
			if sf==null:
				printerr("missing part ",id);quit(2);return
			frames_by_id[str(id)]=sf
	else:
		printerr("unknown kind");quit(2);return
	var load_ms:=float(Time.get_ticks_usec()-load_started)/1000.0
	for _i in 4: await process_frame
	RenderingServer.force_sync()
	var loaded_tex:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var loaded_video:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var container:=Node2D.new()
	root.add_child(container)
	var sprites:Array[AnimatedSprite2D]=[]
	for i in instances:
		var id:=str(asset_ids[i%asset_ids.size()])
		var sf:SpriteFrames=frames_by_id[id]
		var sprite:=AnimatedSprite2D.new()
		sprite.sprite_frames=sf
		sprite.centered=false
		sprite.position=Vector2(float((i*83)%1360)+20.0,float((i*47)%820)+20.0)
		var animation:=choose_animation(sf,i)
		sprite.play(animation)
		sprite.frame=i%maxi(1,sf.get_frame_count(animation))
		container.add_child(sprite)
		sprites.append(sprite)
	for _i in 8: await process_frame
	RenderingServer.force_sync()
	var ready_tex:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var ready_video:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var node_count:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var resource_count:=int(Performance.get_monitor(Performance.OBJECT_RESOURCE_COUNT))
	var intervals:Array[float]=[]
	var draws:Array[float]=[]
	var last:=Time.get_ticks_usec()
	var update:Array[float]=[]
	for frame_id in frame_count:
		var started:=Time.get_ticks_usec()
		if frame_id%18==0:
			for i in sprites.size():
				var sprite:=sprites[i]
				var sf:=sprite.sprite_frames
				var anim:=choose_animation(sf,(frame_id/18+i)%8)
				if sprite.animation!=anim:
					sprite.play(anim)
		update.append(float(Time.get_ticks_usec()-started)/1000.0)
		await process_frame
		var now:=Time.get_ticks_usec()
		if frame_id>=warmup:
			intervals.append(float(now-last)/1000.0)
			draws.append(float(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last=now
	var result={
		"renderer":"recovered_animated_sprite2d",
		"kind":kind,"assets":asset_ids,"instances":instances,
		"load_ms":load_ms,
		"texture_after_load_delta":maxi(0,loaded_tex-baseline_tex),
		"video_after_load_delta":maxi(0,loaded_video-baseline_video),
		"texture_with_instances_delta":maxi(0,ready_tex-baseline_tex),
		"video_with_instances_delta":maxi(0,ready_video-baseline_video),
		"node_count":node_count,"resource_count":resource_count,
		"update_p95_ms":percentile(update,0.95),
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
