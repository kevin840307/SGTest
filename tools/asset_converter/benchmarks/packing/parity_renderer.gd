extends SceneTree
# Renderer parity benchmark. Same imported atlas, timeline, world updates, Y ordering.
# Sprite implementation recreates Recovered's frame_changed origin correction.
const WARMUP := 35
const TICK_RATE := 50.0
const VIEW_SUFFIXES := ["NS1", "NW1", "FA1", "NH1", "ND1", "NS3", "NW3", "FA3", "NW2", "NW4", "NW8"]

class ClipPlan:
	extends RefCounted
	var texture: Texture2D
	var regions: Array[Rect2] = []
	var offsets: Array[Vector2] = []
	var page_ids: PackedInt32Array = PackedInt32Array()
	var ticks: PackedInt32Array = PackedInt32Array()
	var flip_h := false
	var looped := true
	var total := 0
	func frame_index(t: float) -> int:
		if regions.is_empty(): return -1
		var tick: int = int(t * 50.0)
		if looped: tick = tick % maxi(total, 1)
		else: tick = mini(tick, maxi(total - 1, 0))
		var i := ticks.bsearch(tick, false)
		return mini(i, regions.size() - 1)

class AtlasData:
	extends RefCounted
	var textures: Array[Texture2D] = []
	var plans: Dictionary = {}
	var clips: Array[String] = []
	var sprite_frames: SpriteFrames
	var frame_offsets: Dictionary = {}
	var clip_flips: Dictionary = {}
	var frames_built := 0
	func load_asset(path: String) -> Error:
		var doc = JSON.parse_string(FileAccess.get_file_as_string(path))
		if not doc is Dictionary: return ERR_FILE_CORRUPT
		var frames: Array = doc.get("frames",[])
		var base := path.get_base_dir()
		for page_name in doc.get("pages",[]):
			var tex := ResourceLoader.load(base.path_join(str(page_name))) as Texture2D
			if tex == null: return ERR_FILE_CANT_READ
			textures.append(tex)
		sprite_frames = SpriteFrames.new()
		sprite_frames.remove_animation("default")
		var prefix := str(doc.get("group","")).get_file().to_upper()
		# Benchmark image candidates only store source clips. Reuse the
		# production logical view/ACT metadata for the *same* asset ID.
		var canonical_path := "C:/SGO/SRC/SGTest/client/assets/Enemy/%s/atlas.json" % prefix.to_lower()
		var canonical_doc = JSON.parse_string(FileAccess.get_file_as_string(canonical_path))
		var views: Dictionary = canonical_doc.get("logical_views",{}) if canonical_doc is Dictionary else doc.get("logical_views",{})
		for suffix in VIEW_SUFFIXES:
			var name: String = prefix + "_" + suffix
			var view: Dictionary = views.get(name,{})
			if view.is_empty(): continue
			var clip: String = str(view.get("clip",""))
			var index_list: Array = doc.get("clips",{}).get(clip,[])
			if index_list.is_empty(): continue
			var indices: Array = []
			var durations: Array = []
			for timing in view.get("timeline_ticks",[]):
				indices.append(int(timing[0]))
				durations.append(maxi(1,int(timing[1])))
			if indices.is_empty():
				for j in index_list.size():
					indices.append(j);durations.append(6.25)
			var plan := ClipPlan.new()
			plan.flip_h=bool(view.get("flip_h",false))
			plan.looped=bool(view.get("loop",true))
			var sf_name := StringName(name)
			sprite_frames.add_animation(sf_name)
			sprite_frames.set_animation_speed(sf_name,TICK_RATE)
			sprite_frames.set_animation_loop(sf_name,plan.looped)
			var frame_offsets: Array[Vector2] = []
			var running := 0
			for k in indices.size():
				var index := int(indices[k])
				if index < 0 or index >= index_list.size(): continue
				var frame: Dictionary = frames[int(index_list[index])]
				var page := int(frame.get("page",-1))
				if page < 0 or page >= textures.size(): continue
				var rr: Array = frame.get("rect",[0,0,0,0])
				var trim: Array = frame.get("trim",[0,0])
				var origin: Array = frame.get("origin",[0,0])
				var region := Rect2(float(rr[0]),float(rr[1]),float(rr[2]),float(rr[3]))
				var offset := Vector2(float(trim[0])-float(origin[0]),float(trim[1])-float(origin[1]))
				var ticks_for_frame := int(durations[k])
				running += ticks_for_frame
				plan.regions.append(region);plan.offsets.append(offset);plan.ticks.append(running)
				plan.page_ids.append(page)
				var atlas := AtlasTexture.new()
				atlas.atlas=textures[page];atlas.region=region;atlas.filter_clip=true
				sprite_frames.add_frame(sf_name,atlas,float(ticks_for_frame))
				frame_offsets.append(Vector2(-offset.x - region.size.x,offset.y) if plan.flip_h else offset)
			plan.total=running
			if plan.regions.is_empty():continue
			plans[name]=plan
			clips.append(name)
			self.frame_offsets[sf_name]=frame_offsets
			self.clip_flips[sf_name]=plan.flip_h
			frames_built += plan.regions.size()
		return OK if not clips.is_empty() else ERR_FILE_CORRUPT

class SpriteEntity:
	extends AnimatedSprite2D
	var offsets: Dictionary
	var flips: Dictionary
	func _ready() -> void:
		centered=false
		frame_changed.connect(_refresh_offset)
		animation_changed.connect(_refresh_offset)
		_refresh_offset()
	func _refresh_offset() -> void:
		var arr: Array = offsets.get(animation,[])
		if arr.is_empty(): return
		offset = arr[mini(frame,arr.size()-1)]
		flip_h=bool(flips.get(animation,false))

class FastCanvas:
	extends Node2D
	var atlas_list: Array[AtlasData] = []
	var kinds: PackedInt32Array = PackedInt32Array()
	var clips: PackedInt32Array = PackedInt32Array()
	var positions: PackedVector2Array = PackedVector2Array()
	var starts: PackedFloat32Array = PackedFloat32Array()
	var elapsed := 0.0
	var sort_order: Array[int] = []
	func _draw() -> void:
		for i in sort_order:
			var atlas: AtlasData=atlas_list[kinds[i]]
			var clip: ClipPlan=atlas.plans[atlas.clips[clips[i]]]
			var index := clip.frame_index(maxf(0.0,elapsed-starts[i]))
			if index < 0: continue
			var off: Vector2=clip.offsets[index]
			var rect: Rect2=clip.regions[index]
			var texture := atlas.textures[clip.page_ids[index]]
			if clip.flip_h:
				draw_set_transform(positions[i],0.0,Vector2(-1,1))
				draw_texture_rect_region(texture,Rect2(off,rect.size),rect,Color.WHITE,false,true)
				draw_set_transform(Vector2.ZERO)
			else:
				draw_texture_rect_region(texture,Rect2(positions[i]+off,rect.size),rect,Color.WHITE,false,true)

func _verify_frame_semantics(assets: Array[AtlasData]) -> int:
	var checks := 0
	for atlas in assets:
		for name in atlas.clips:
			var key := StringName(name)
			var plan: ClipPlan = atlas.plans[name]
			if not atlas.sprite_frames.has_animation(key):
				return -1
			if atlas.sprite_frames.get_frame_count(key) != plan.regions.size():
				return -1
			if atlas.sprite_frames.get_animation_loop(key) != plan.looped:
				return -1
			var offsets: Array = atlas.frame_offsets[key]
			for i in plan.regions.size():
				var tex := atlas.sprite_frames.get_frame_texture(key,i) as AtlasTexture
				if tex == null or tex.region != plan.regions[i]:
					return -1
				if tex.atlas != atlas.textures[plan.page_ids[i]]:
					return -1
				var off := plan.offsets[i]
				var expected := Vector2(-off.x-plan.regions[i].size.x,off.y) if plan.flip_h else off
				if offsets[i] != expected:
					return -1
				var prev_tick: int = int(plan.ticks[i - 1]) if i > 0 else 0
				var expected_ticks: float = float(plan.ticks[i] - prev_tick)
				var actual_ticks: float = atlas.sprite_frames.get_frame_duration(key, i)
				if not is_equal_approx(expected_ticks, actual_ticks):
					return -1
				checks+=1
	return checks

func _pct(values: Array[float],pct:float)->float:
	if values.is_empty(): return 0.0
	values.sort()
	return values[int(floor(float(values.size()-1)*pct))]

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 8:
		printerr("mode strategy sample_csv count frames width height output")
		quit(2);return
	var mode := str(args[0]);var strategy:=str(args[1])
	var samples := str(args[2]).split(",",false)
	var count:=int(args[3]);var total_frames:=int(args[4])
	var width:=int(args[5]);var height:=int(args[6]);var output:=str(args[7])
	DisplayServer.window_set_size(Vector2i(width,height));root.size=Vector2i(width,height)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	Engine.max_fps=0;RenderingServer.render_loop_enabled=true
	var base_tex:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var base_video:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))
	var base_nodes:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var atlas_list:Array[AtlasData]=[]
	var started:=Time.get_ticks_usec()
	for sample in samples:
		var a:=AtlasData.new()
		var path:="res://assets/%s/%s/atlas.json" % [strategy,sample]
		var err:=a.load_asset(path)
		if err!=OK:printerr("LOAD ",path," error ",err);quit(3);return
		atlas_list.append(a)
	var semantics_checks := _verify_frame_semantics(atlas_list)
	if semantics_checks <= 0:
		printerr("sprite/direct frame metadata parity FAIL")
		quit(4);return
	var load_ms:=float(Time.get_ticks_usec()-started)/1000.0
	var fast:FastCanvas=null
	var sprites:Array[SpriteEntity]=[]
	var container:=Node2D.new()
	container.y_sort_enabled=true
	root.add_child(container)
	if mode=="fast":
		fast=FastCanvas.new()
		fast.atlas_list=atlas_list
		for i in count:
			var kind:=i%atlas_list.size()
			var a:AtlasData=atlas_list[kind]
			var clip_index: int=(i*7)%a.clips.size()
			fast.kinds.append(kind);fast.clips.append(clip_index)
			fast.positions.append(Vector2(24+(i*83)%maxi(100,width-48),32+(i*47)%maxi(80,height-64)))
			fast.starts.append(0.0)
			fast.sort_order.append(i)
		container.add_child(fast)
	else:
		for i in count:
			var kind:=i%atlas_list.size()
			var a:AtlasData=atlas_list[kind]
			var s:=SpriteEntity.new()
			s.sprite_frames=a.sprite_frames
			s.offsets=a.frame_offsets
			s.flips=a.clip_flips
			s.position=Vector2(24+(i*83)%maxi(100,width-48),32+(i*47)%maxi(80,height-64))
			container.add_child(s)
			var chosen:=StringName(a.clips[(i*7)%a.clips.size()])
			s.play(chosen)
			s._refresh_offset()
			sprites.append(s)
	await process_frame
	var tex:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))-base_tex
	var video:=int(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED))-base_video
	var nodes:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))-base_nodes
	var p95:Array[float]=[];var draws:Array[float]=[]
	var last:=Time.get_ticks_usec();var sort_usec:=0
	var elapsed_started:=Time.get_ticks_usec()
	for frame_id in total_frames:
		var phase:=float(Time.get_ticks_usec()-elapsed_started)/1000000.0
		if fast!=null:
			fast.elapsed=phase
			# Natural independent movement, preserve world Y ordering.
			for i in fast.positions.size():
				var p:=fast.positions[i]
				fast.positions[i]=Vector2(p.x+sin(float(frame_id+i)*0.04)*0.35,p.y+cos(float(frame_id+i)*0.03)*0.25)
			var started_sort:=Time.get_ticks_usec()
			fast.sort_order.sort_custom(func(a:int,b:int)->bool:return fast.positions[a].y<fast.positions[b].y)
			sort_usec+=Time.get_ticks_usec()-started_sort
			fast.queue_redraw()
		else:
			for i in sprites.size():
				var s:=sprites[i]
				s.position+=Vector2(sin(float(frame_id+i)*0.04)*0.35,cos(float(frame_id+i)*0.03)*0.25)
		if frame_id>0 and frame_id%25==0:
			if fast!=null:
				for i in range(0,fast.clips.size(),7):
					fast.clips[i]=(fast.clips[i]+1)%atlas_list[fast.kinds[i]].clips.size()
					fast.starts[i]=phase
			else:
				for i in range(0,sprites.size(),7):
					var s:=sprites[i]
					var names:=atlas_list[i%atlas_list.size()].clips
					s.play(StringName(names[(int(frame_id/25)+i)%names.size()]))
					s._refresh_offset()
		await process_frame
		var now:=Time.get_ticks_usec()
		if frame_id>=WARMUP:
			p95.append(float(now-last)/1000.0)
			draws.append(float(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
		last=now
	var result={
		"mode":mode,"strategy":strategy,"assets":samples,"instances":count,
		"renderer_path":"corrected_sprite_origin_timeline_ysort" if mode=="sprite" else "fast_canvas_timeline_ysort",
		"load_ms":load_ms,
		"frame_p50_ms":_pct(p95,.50),"frame_p95_ms":_pct(p95,.95),"frame_p99_ms":_pct(p95,.99),
		"draw_calls_p95":_pct(draws,.95),"texture_mem_delta":maxi(0,tex),
		"video_mem_delta":maxi(0,video),"node_delta":maxi(0,nodes),
		"origin_corrected":true,"timeline_from_logical_views":true,
		"frame_semantics_assertions":semantics_checks,
		"world_ysort_enabled":true,"flip_from_logical_views":true,"sort_cpu_total_usec":sort_usec
	}
	var f:=FileAccess.open(output,FileAccess.WRITE)
	if f!=null:f.store_string(JSON.stringify(result)+"\n");f.close()
	print(JSON.stringify(result))
	quit(0)
