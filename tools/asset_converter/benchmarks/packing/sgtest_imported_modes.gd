extends SceneTree

const ENTITY_ATLAS = preload("res://scripts/assets/entity_atlas.gd")
const WARMUP := 30

class FastPlan:
	extends RefCounted
	var pages := PackedInt32Array()
	var rects: Array[Rect2] = []
	var offsets: Array[Vector2] = []

class FastAtlas:
	extends RefCounted
	var textures: Array[Texture2D] = []
	var plans: Dictionary = {}
	func open(manifest_path:String)->Error:
		var parsed=JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
		if not parsed is Dictionary:
			return ERR_FILE_CORRUPT
		var base:=manifest_path.get_base_dir()
		for page_name in parsed.get("pages",[]):
			var tex=ResourceLoader.load(base.path_join(str(page_name)))
			if tex==null:
				return ERR_FILE_CANT_READ
			textures.append(tex)
		var frames:Array=parsed.get("frames",[])
		for clip_name in parsed.get("clips",{}):
			var plan:=FastPlan.new()
			for frame_id in parsed.clips[clip_name]:
				var frame:Dictionary=frames[int(frame_id)]
				var rect:Array=frame.get("rect",[0,0,0,0])
				var trim:Array=frame.get("trim",[0,0])
				var origin:Array=frame.get("origin",[0,0])
				plan.pages.append(int(frame.get("page",-1)))
				plan.rects.append(Rect2(rect[0],rect[1],rect[2],rect[3]))
				plan.offsets.append(Vector2(trim[0]-origin[0],trim[1]-origin[1]))
			plans[str(clip_name)]=plan
		return OK
	func clip_names()->Array:
		var names:=plans.keys()
		names.sort()
		return names
	func draw(canvas:CanvasItem,at:Vector2,clip:String,seconds:float,fps:float=8.0)->void:
		var plan:FastPlan=plans.get(clip)
		if plan==null or plan.rects.is_empty():
			return
		var index:=int(maxf(seconds,0.0)*fps)%plan.rects.size()
		var page:=plan.pages[index]
		if page<0:
			return
		var rect:=plan.rects[index]
		canvas.draw_texture_rect_region(textures[page],Rect2(at+plan.offsets[index],rect.size),rect)

class DirectCanvas:
	extends Node2D
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

class FastCanvas:
	extends Node2D
	var atlases:Array=[]
	var clips:Array[Array]=[]
	var instances:Array[Dictionary]=[]
	var phase:=0
	func _draw()->void:
		for entity in instances:
			var atlas:FastAtlas=atlases[int(entity.visual)]
			var names:Array=clips[int(entity.visual)]
			if names.is_empty(): continue
			var clip:=str(names[int(entity.clip)%names.size()])
			atlas.draw(self,Vector2(entity.x,entity.y),clip,float(phase+int(entity.offset))/8.0,8.0)

func _pct(values:Array[float],q:float)->float:
	if values.is_empty(): return 0.0
	values.sort()
	return values[int(floor(float(values.size()-1)*q))]

func _load_direct(manifest_path:String)->RefCounted:
	var parsed=JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
	if not parsed is Dictionary:
		return null
	var atlas:=ENTITY_ATLAS.new()
	atlas.data=parsed
	atlas.pages.clear()
	atlas.sample_cache.clear()
	atlas.compiled.clear()
	var base:=manifest_path.get_base_dir()
	for page_name in parsed.get("pages",[]):
		var tex=ResourceLoader.load(base.path_join(str(page_name)))
		if tex==null:
			return null
		atlas.pages.append(tex)
	return atlas

func _build_sprite_frames(manifest_path:String)->SpriteFrames:
	var parsed=JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
	if not parsed is Dictionary:
		return null
	var base:=manifest_path.get_base_dir()
	var textures:Array[Texture2D]=[]
	for page_name in parsed.get("pages",[]):
		var tex=ResourceLoader.load(base.path_join(str(page_name)))
		if tex==null:
			return null
		textures.append(tex)
	var result:=SpriteFrames.new()
	result.remove_animation("default")
	var frames:Array=parsed.get("frames",[])
	for clip_name in parsed.get("clips",{}):
		var animation:=str(clip_name)
		result.add_animation(animation)
		result.set_animation_speed(animation,8.0)
		result.set_animation_loop(animation,true)
		for frame_id in parsed.clips[clip_name]:
			var frame:Dictionary=frames[int(frame_id)]
			var page:=int(frame.get("page",-1))
			if page<0: continue
			var rect:Array=frame.get("rect",[0,0,0,0])
			var atlas_tex:=AtlasTexture.new()
			atlas_tex.atlas=textures[page]
			atlas_tex.region=Rect2(rect[0],rect[1],rect[2],rect[3])
			atlas_tex.filter_clip=true
			result.add_frame(animation,atlas_tex)
	return result

func _initialize()->void:
	call_deferred("_run")

func _run()->void:
	var args:=OS.get_cmdline_user_args()
	if args.size()<8:
		printerr("mode strategy sample_csv instances frames width height output")
		quit(2);return
	var mode:=str(args[0])
	var strategy:=str(args[1])
	var samples:Array[String]=[]
	for raw in str(args[2]).split(",",false): samples.append(str(raw))
	var count:=int(args[3]);var frame_count:=int(args[4])
	var width:=int(args[5]);var height:=int(args[6]);var output_path:=str(args[7])
	DisplayServer.window_set_size(Vector2i(width,height));root.size=Vector2i(width,height)
	RenderingServer.render_loop_enabled=true
	Engine.max_fps=0
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var tex_before:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var video_before:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var nodes_before:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var started:=Time.get_ticks_usec()
	var root_path:="res://benchmark_imported/"+strategy+"/"
	var canvas:Node2D=null
	var sprite_frames:Array[SpriteFrames]=[]
	var clips:Array[Array]=[]
	if mode=="direct":
		var direct:=DirectCanvas.new();canvas=direct
		for sample in samples:
			var manifest:=root_path+sample+"/atlas.json"
			var atlas:=_load_direct(manifest)
			if atlas==null: printerr("direct load failed "+manifest);quit(3);return
			direct.atlases.append(atlas)
			var names:Array=atlas.data.get("clips",{}).keys();names.sort();direct.clips.append(names)
	elif mode=="fast":
		var fast:=FastCanvas.new();canvas=fast
		for sample in samples:
			var manifest:=root_path+sample+"/atlas.json"
			var atlas:=FastAtlas.new()
			if atlas.open(manifest)!=OK: printerr("fast load failed "+manifest);quit(3);return
			fast.atlases.append(atlas);fast.clips.append(atlas.clip_names())
	elif mode=="sprite":
		for sample in samples:
			var manifest:=root_path+sample+"/atlas.json"
			var sf:=_build_sprite_frames(manifest)
			if sf==null: printerr("sprite load failed "+manifest);quit(3);return
			sprite_frames.append(sf)
			var names:=sf.get_animation_names();names.sort();clips.append(names)
	else:
		printerr("unknown mode "+mode);quit(2);return
	var load_ms:=float(Time.get_ticks_usec()-started)/1000.0
	if mode=="sprite":
		var holder:=Node2D.new();root.add_child(holder)
		for i in count:
			var visual:=i%samples.size()
			var sprite:=AnimatedSprite2D.new()
			sprite.sprite_frames=sprite_frames[visual]
			var names:=clips[visual]
			if not names.is_empty():
				sprite.animation=str(names[(i*7)%names.size()])
				sprite.frame=(i*11)%maxi(1,sprite.sprite_frames.get_frame_count(sprite.animation))
				sprite.play()
			sprite.position=Vector2(24+((i*83)%maxi(100,width-48)),32+((i*47)%maxi(80,height-64)))
			holder.add_child(sprite)
	else:
		for i in count:
			canvas.instances.append({"visual":i%samples.size(),"clip":i*7,"offset":(i*11)%13,
				"x":24+((i*83)%maxi(100,width-48)),"y":32+((i*47)%maxi(80,height-64))})
		root.add_child(canvas)
	await process_frame
	var tex_after:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var video_after:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var nodes_after:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var intervals:Array[float]=[];var draws:Array[float]=[];var last:=Time.get_ticks_usec()
	for frame_id in frame_count:
		if canvas!=null:
			canvas.phase=frame_id;canvas.queue_redraw()
		await process_frame
		var now:=Time.get_ticks_usec()
		if frame_id>=WARMUP:
			intervals.append(float(now-last)/1000.0)
			draws.append(float(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last=now
	var result={"renderer_path":"sgtest_bptc_"+mode,"strategy":strategy,"assets":samples,
		"instances":count,"frames":frame_count,"load_ms":load_ms,
		"frame_p50_ms":_pct(intervals,.5),"frame_p95_ms":_pct(intervals,.95),"frame_p99_ms":_pct(intervals,.99),
		"draw_calls_p50":_pct(draws,.5),"draw_calls_p95":_pct(draws,.95),
		"texture_mem_delta":maxi(0,tex_after-tex_before),"video_mem_delta":maxi(0,video_after-video_before),
		"node_delta":maxi(0,nodes_after-nodes_before),"adapter":RenderingServer.get_video_adapter_name()}
	var output:=JSON.stringify(result);print(output)
	var f:=FileAccess.open(output_path,FileAccess.WRITE)
	if f!=null:f.store_string(output+"\n");f.close()
	quit(0)
