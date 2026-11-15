extends SceneTree
# Recovered client native animation path, including MonsterEntity-style frame offset,
# original ACT timing via MonsterLoader, world Y ordering and continuous movement.
const WARMUP:=35
const SUFFIXES:=["NS1","NW1","FA1","NH1","ND1","NS3","NW3","FA3","NW2","NW4","NW8"]
const ACTIONS:={"NS":"IDLE","NW":"WALK","FA":"ATTACK","NH":"HIT","ND":"DEATH"}

class SpriteEntity:
	extends AnimatedSprite2D
	func _ready()->void:
		centered=false
		frame_changed.connect(_refresh_offset)
		animation_changed.connect(_refresh_offset)
		_refresh_offset()
	func _refresh_offset()->void:
		if sprite_frames==null or not sprite_frames.has_animation(animation):
			return
		if frame<0 or frame>=sprite_frames.get_frame_count(animation):
			return
		var tex:Texture2D=sprite_frames.get_frame_texture(animation,frame)
		if tex==null:return
		var old_origin:Vector2=tex.get_meta("old_offset",Vector2.ZERO)
		offset=Vector2(-(tex.get_width()-old_origin.x) if flip_h else -old_origin.x,-old_origin.y)

func _pct(values:Array[float],pct:float)->float:
	if values.is_empty():return 0.0
	values.sort()
	return values[int(floor(float(values.size()-1)*pct))]

func _initialize()->void:
	call_deferred("_run")

func _run()->void:
	var args:=OS.get_cmdline_user_args()
	if args.size()<6:
		printerr("id_csv count frames width height output")
		quit(2);return
	var ids:=str(args[0]).split(",",false)
	var count:=int(args[1]);var frames:=int(args[2])
	var width:=int(args[3]);var height:=int(args[4]);var output:=str(args[5])
	DisplayServer.window_set_size(Vector2i(width,height));root.size=Vector2i(width,height)
	RenderingServer.render_loop_enabled=true;Engine.max_fps=0
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	await process_frame
	RenderingServer.render_loop_enabled=true;Engine.max_fps=0
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var tex_before:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var vid_before:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var nodes_before:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var started:=Time.get_ticks_usec()
	var resources:Array[SpriteFrames]=[]
	var clips:Array[Array]=[]
	var offsets_verified:=0
	var loader:=root.get_node_or_null("MonsterLoader")
	if loader==null:printerr("Recovered MonsterLoader autoload unavailable");quit(3);return
	for ident in ids:
		var sf:SpriteFrames=loader.get_monster_frames(str(ident),true)
		if sf==null:printerr("Missing source "+str(ident));quit(3);return
		resources.append(sf)
		var available:Array[StringName]=[]
		for suffix in SUFFIXES:
			var name: String = str(ACTIONS[suffix.left(2)]) + "_" + suffix.substr(2)
			if sf.has_animation(name):
				available.append(StringName(name))
		if available.is_empty():printerr("No action data "+str(ident));quit(3);return
		clips.append(available)
		for key in available:
			for j in sf.get_frame_count(key):
				var tex=sf.get_frame_texture(key,j)
				if tex==null or not tex.has_meta("old_offset"):
					printerr("Missing recovered frame origin ",key," frame ",j)
					quit(4);return
				offsets_verified+=1
	var load_ms:=float(Time.get_ticks_usec()-started)/1000.0
	var group:=Node2D.new()
	group.y_sort_enabled=true;root.add_child(group)
	var sprites:Array[SpriteEntity]=[]
	for i in count:
		var kind:=i%resources.size();var sf:SpriteFrames=resources[kind]
		var sprite:=SpriteEntity.new()
		sprite.sprite_frames=sf
		sprite.position=Vector2(24+(i*83)%maxi(100,width-48),32+(i*47)%maxi(80,height-64))
		group.add_child(sprite)
		sprite.play(clips[kind][(i*7)%clips[kind].size()])
		sprite._refresh_offset()
		sprites.append(sprite)
	await process_frame
	var tex_delta:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))-tex_before
	var vid_delta:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))-vid_before
	var node_delta:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))-nodes_before
	var times:Array[float]=[];var draws:Array[float]=[];var last:=Time.get_ticks_usec()
	for f in frames:
		RenderingServer.render_loop_enabled=true;Engine.max_fps=0
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
		for i in sprites.size():
			var sprite:=sprites[i]
			sprite.position+=Vector2(sin(float(f+i)*0.04)*0.35,cos(float(f+i)*0.03)*0.25)
		if f>0 and f%25==0:
			for i in range(0,sprites.size(),7):
				var kind:=i%resources.size()
				var names:Array=clips[kind]
				sprites[i].play(names[(int(f/25)+i)%names.size()])
				sprites[i]._refresh_offset()
		await process_frame
		var now:=Time.get_ticks_usec()
		if f>=WARMUP:
			times.append(float(now-last)/1000.0)
			draws.append(float(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last=now
	var result={
		"renderer_path":"recovered_corrected_native_ysort",
		"assets":ids,"instances":count,"load_ms":load_ms,
		"frame_p50_ms":_pct(times,.50),"frame_p95_ms":_pct(times,.95),
		"frame_p99_ms":_pct(times,.99),"draw_calls_p95":_pct(draws,.95),
		"texture_mem_delta":maxi(0,tex_delta),"video_mem_delta":maxi(0,vid_delta),
		"node_delta":maxi(0,node_delta),"frame_origins_verified":offsets_verified,
		"world_ysort_enabled":true,"source_act_timing_enabled":true
	}
	var file:=FileAccess.open(output,FileAccess.WRITE)
	if file!=null:file.store_string(JSON.stringify(result)+"\n");file.close()
	print(JSON.stringify(result))
	quit(0)
