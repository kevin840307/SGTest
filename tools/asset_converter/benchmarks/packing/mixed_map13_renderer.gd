extends SceneTree
# Map13 visual workload: real scenery placements + independent per-asset art,
# 20 player placeholders, 500 drop markers, and Enemy AOI churn.
# Runs with --path <Recovered/client> OR --path <compressed SGTest test project>.
# No server/network simulation; do not call this an end-to-end gameplay test.
const ACTION_SUFFIXES := ["NS1","NW1","FA1","NH1","ND1","NS3","NW3","FA3","NW2","NW4","NW8","NS4","NW6","FA6"]
const ACTION_NAMES := {"NS":"IDLE","NW":"WALK","FA":"ATTACK","NH":"HIT","ND":"DEATH"}
const IDS := ["EN114A","EN089A","EN121A","EN116A","EN023A","EN084A","EN111A","EN125A","EN050C","EN080A","EN093A","EN079A"]
const CAMERA := Vector2(6616,4728)
const SCREEN := Vector2(1280,720)
const WARMUP := 35
var asset_location := ""

class MonsterSprite:
	extends AnimatedSprite2D
	var origin_table: Dictionary = {}
	var original_recovered := false
	var monster_id := ""
	func _ready() -> void:
		centered = false
		frame_changed.connect(_update_origin)
		animation_changed.connect(_update_origin)
		_update_origin()
	func _update_origin() -> void:
		if sprite_frames==null or not sprite_frames.has_animation(animation):return
		if frame<0 or frame>=sprite_frames.get_frame_count(animation):return
		if original_recovered:
			var tex := sprite_frames.get_frame_texture(animation,frame)
			if tex==null:return
			var o:Vector2=tex.get_meta("old_offset",Vector2.ZERO)
			offset=Vector2(-(tex.get_width()-o.x) if flip_h else -o.x,-o.y)
		else:
			var offsets: Array=origin_table.get(animation,[])
			if not offsets.is_empty():offset=offsets[mini(frame,offsets.size()-1)]

func _pct(a:Array[float],p:float)->float:
	if a.is_empty():return 0.0
	a.sort();return a[int(floor(float(a.size()-1)*p))]

func _initialize()->void:
	call_deferred("_run")

func _load_raw_texture(path:String)->Texture2D:
	var image:=Image.load_from_file(path)
	return ImageTexture.create_from_image(image) if image!=null else null

func _build_scenery(root_path:String,world:Node2D)->Dictionary:
	var doc=JSON.parse_string(FileAccess.get_file_as_string(root_path.path_join("maps/13/world.json")))
	if not doc is Dictionary:return {}
	var cache:Dictionary={}
	var drawn:=0
	var distinct:Dictionary={}
	var scanned:=0
	for item in doc.get("objects",[]):
		var p:=Vector2(float(item.p[0]),float(item.p[1]))
		if absf(p.x-CAMERA.x)>900.0 or absf(p.y-CAMERA.y)>700.0:continue
		scanned+=1
		var relative:=str(item.get("atlas",""))
		var manifest:=root_path.path_join(relative)
		if not cache.has(manifest):
			var atlas_doc=JSON.parse_string(FileAccess.get_file_as_string(manifest))
			if not atlas_doc is Dictionary:return {"error":"missing scene manifest "+manifest}
			var pages:Array[Texture2D]=[]
			for page_name in atlas_doc.get("pages",[]):
				var tex=_load_raw_texture(manifest.get_base_dir().path_join(str(page_name)))
				if tex==null:return {"error":"missing scenery PNG "+str(page_name)}
				pages.append(tex)
			cache[manifest]={"doc":atlas_doc,"pages":pages}
		var source:Dictionary=cache[manifest]
		var metadata:Dictionary=source.doc
		var keys:Array=metadata.get("clips",{}).get(str(item.get("clip","")),[])
		if keys.is_empty():continue
		var frame:Dictionary=metadata.frames[int(keys[0])]
		var page:=int(frame.get("page",-1))
		if page<0 or page>=source.pages.size():continue
		var rr:Array=frame.get("rect",[0,0,0,0])
		var origin:Array=frame.get("origin",[0,0])
		var trim:Array=frame.get("trim",[0,0])
		var tex:=AtlasTexture.new()
		tex.atlas=source.pages[page]
		tex.region=Rect2(float(rr[0]),float(rr[1]),float(rr[2]),float(rr[3]))
		tex.filter_clip=true
		var sprite:=Sprite2D.new()
		sprite.centered=false
		sprite.texture=tex
		sprite.position=p-CAMERA+SCREEN*0.5+Vector2(0,float(item.get("z",0)))
		sprite.offset=Vector2(float(trim[0])-float(origin[0]),float(trim[1])-float(origin[1]))
		world.add_child(sprite)
		distinct[manifest]=true
		drawn+=1
	return {"placed":drawn,"scanned":scanned,"independent_types":distinct.size(),"texture_resources":cache.size(),"cache":cache}

func _build_sg_monsters() -> Dictionary:
	var result:Dictionary={}
	for ident in IDS:
		var path:="res://assets/height_p0/mixed_%s/atlas.json" % ident.to_lower()
		var doc=JSON.parse_string(FileAccess.get_file_as_string(path))
		if not doc is Dictionary:return {}
		var canonical:Variant=JSON.parse_string(FileAccess.get_file_as_string(asset_location.path_join("Enemy/%s/atlas.json" % ident.to_lower())))
		if not canonical is Dictionary:return {}
		var images:Array[Texture2D]=[]
		for page in doc.get("pages",[]):
			var tex:=ResourceLoader.load(path.get_base_dir().path_join(str(page))) as Texture2D
			if tex==null:return {}
			images.append(tex)
		var sf:=SpriteFrames.new();sf.remove_animation("default")
		var origin_table:Dictionary={}
		var names:Array[Dictionary]=[]
		for suffix in ACTION_SUFFIXES:
			var key: String = str(ident)+"_"+suffix
			var view:Dictionary=canonical.get("logical_views",{}).get(key,{})
			if view.is_empty():continue
			var ids:Array=doc.get("clips",{}).get(str(view.get("clip","")) ,[])
			if ids.is_empty():continue
			var ticks:Array=view.get("timeline_ticks",[])
			if ticks.is_empty():continue
			var animation:=StringName(key)
			sf.add_animation(animation)
			sf.set_animation_speed(animation,50.0)
			sf.set_animation_loop(animation,bool(view.get("loop",true)))
			var mirror:=bool(view.get("flip_h",false))
			var offsets:Array[Vector2]=[]
			for entry in ticks:
				var index:=int(entry[0])
				if index<0 or index>=ids.size():continue
				var f:Dictionary=doc.frames[int(ids[index])]
				var page_id:=int(f.get("page",-1))
				if page_id<0 or page_id>=images.size():continue
				var rr:Array=f.rect
				var trim:Array=f.get("trim",[0,0])
				var origin:Array=f.get("origin",[0,0])
				var region:=Rect2(float(rr[0]),float(rr[1]),float(rr[2]),float(rr[3]))
				var off:=Vector2(float(trim[0])-float(origin[0]),float(trim[1])-float(origin[1]))
				var atlas:=AtlasTexture.new()
				atlas.atlas=images[page_id];atlas.region=region;atlas.filter_clip=true
				sf.add_frame(animation,atlas,float(maxi(1,int(entry[1]))))
				offsets.append(Vector2(-off.x-region.size.x,off.y) if mirror else off)
			if not offsets.is_empty():
				origin_table[animation]=offsets
				names.append({"animation":animation,"flip":bool(view.get("flip_h",false)),"view":key})
		if names.is_empty():return {}
		result[ident]={"frames":sf,"origins":origin_table,"names":names}
	return result

func _original_animation_suffix(suffix:String)->String:
	match suffix:
		"NS4": return "NS3"
		"NW6": return "NW2"
		"FA6": return "FA1"
	return suffix

func _original_flip(suffix:String)->bool:
	return suffix in ["NS4","NW6","FA6"]

func _build_recovered_monsters()->Dictionary:
	var result:Dictionary={}
	var loader=root.get_node_or_null("MonsterLoader")
	if loader==null:return {}
	for ident in IDS:
		var sf:SpriteFrames=loader.get_monster_frames(ident,true)
		if sf==null:return {}
		var names:Array[Dictionary]=[]
		for suffix in ACTION_SUFFIXES:
			var original_suffix:=_original_animation_suffix(suffix)
			var animation:=StringName(str(ACTION_NAMES[original_suffix.left(2)])+"_"+original_suffix.substr(2))
			if sf.has_animation(animation):
				names.append({"animation":animation,"flip":_original_flip(suffix),"view":ident+"_"+suffix})
		if names.is_empty():return {}
		result[ident]={"frames":sf,"origins":{},"names":names}
	return result

func _spawn(world:Node2D,palette:Dictionary,id:int,recovered:bool)->MonsterSprite:
	var sprite:=MonsterSprite.new()
	var monster: String = str(IDS[id%IDS.size()])
	var visual:Dictionary=palette[monster]
	sprite.sprite_frames=visual.frames
	sprite.origin_table=visual.origins
	sprite.original_recovered=recovered
	sprite.monster_id=monster
	sprite.position=Vector2(40+((id*83)%1200),35+((id*47)%650))
	world.add_child(sprite)
	var choices:Array=visual.names
	var selected:Dictionary=choices[(id*7)%choices.size()]
	sprite.flip_h=bool(selected.flip)
	sprite.play(StringName(selected.animation))
	sprite._update_origin()
	return sprite

func _run()->void:
	var args:=OS.get_cmdline_user_args()
	if args.size()<6:printerr("mode assets_root enemies frames output viewport_width");quit(2);return
	var mode:=str(args[0]);var assets_root:=str(args[1])
	asset_location=assets_root
	var monster_count:=int(args[2]);var sample_frames:=int(args[3]);var outfile:=str(args[4])
	var width:=int(args[5])
	if mode not in ["recovered","sprite"] or monster_count<10:quit(2);return
	DisplayServer.window_set_size(Vector2i(width,720));root.size=Vector2i(width,720)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	RenderingServer.render_loop_enabled=true;Engine.max_fps=0
	var start_tex:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var start_nodes:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var world:=Node2D.new();world.y_sort_enabled=true;root.add_child(world)
	var scene_start_usec:=Time.get_ticks_usec()
	var scenery:=_build_scenery(assets_root,world)
	var scenery_load_ms:=float(Time.get_ticks_usec()-scene_start_usec)/1000.0
	if scenery.is_empty() or scenery.has("error"):
		printerr("Failed Map13 scenery ",scenery.get("error","empty"));quit(3);return
	# 20 representative player placeholders. Player paper-doll is NOT tested here.
	var art:=Image.create(16,32,false,Image.FORMAT_RGBA8)
	art.fill(Color(0.12,0.8,0.96))
	var player_tex:=ImageTexture.create_from_image(art)
	for i in 20:
		var p:=Sprite2D.new();p.texture=player_tex;p.centered=false
		p.position=Vector2(35+(i*61)%1200,55+(i*91)%620)
		world.add_child(p)
	# Ground drop markers, same batched draw in both renderer modes.
	var drops:=MultiMeshInstance2D.new();world.add_child(drops)
	var mm:=MultiMesh.new();mm.transform_format=MultiMesh.TRANSFORM_2D
	var quad:=QuadMesh.new();quad.size=Vector2(6,6)
	mm.mesh=quad;mm.instance_count=500
	for i in 500:
		mm.set_instance_transform_2d(i,Transform2D(0,Vector2(10+(i*37)%1260,10+(i*73)%690)))
	drops.multimesh=mm;drops.modulate=Color.GOLD
	var start:=Time.get_ticks_usec()
	var palette:=_build_recovered_monsters() if mode=="recovered" else _build_sg_monsters()
	if palette.size()!=IDS.size():printerr("palette missing ",palette.size());quit(4);return
	for monster_name in IDS:
		if palette[monster_name].names.size()!=ACTION_SUFFIXES.size():
			printerr("action set mismatch ",monster_name," available ",palette[monster_name].names.size()," expected ",ACTION_SUFFIXES.size())
			quit(5);return
	var load_ms:=float(Time.get_ticks_usec()-start)/1000.0
	var sprites:Array[MonsterSprite]=[]
	for i in monster_count:
		sprites.append(_spawn(world,palette,i,mode=="recovered"))
	await process_frame
	var tex_delta:=maxi(0,int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))-start_tex)
	var node_delta:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))-start_nodes
	var times:Array[float]=[];var draw_calls:Array[float]=[]
	var spawn_count:=0;var despawn_count:=0;var actions:=0;var gaps:=0
	var last:=Time.get_ticks_usec()
	for frame_id in sample_frames:
		RenderingServer.render_loop_enabled=true;Engine.max_fps=0
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
		DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
		for i in sprites.size():
			var s:MonsterSprite=sprites[i]
			s.position+=Vector2(sin(float(frame_id+i)*0.04)*0.3,cos(float(frame_id+i)*0.03)*0.3)
		# Fixed AOI churn: 5% despawn/spawn every 20 rendered frames.
		if frame_id>0 and frame_id%20==0:
			var changes:=maxi(1,monster_count/20)
			for j in changes:
				var index: int=(frame_id*13+j*19)%sprites.size()
				var old:MonsterSprite=sprites[index]
				world.remove_child(old)
				old.queue_free();despawn_count+=1
				sprites[index]=_spawn(world,palette,frame_id*101+j+index,mode=="recovered")
				spawn_count+=1
		if frame_id>0 and frame_id%25==0:
			for i in range(0,sprites.size(),7):
				var spr:MonsterSprite=sprites[i]
				var monster: String = spr.monster_id
				var names:Array=palette[monster].names
				var chosen:Dictionary=names[(int(frame_id/25)+i)%names.size()]
				var nxt:StringName=StringName(chosen.animation)
				if spr.sprite_frames.has_animation(nxt):
					spr.flip_h=bool(chosen.flip)
					spr.play(nxt);spr._update_origin()
					actions+=1
		await process_frame
		var now:=Time.get_ticks_usec()
		if frame_id>=WARMUP:
			var elapsed:=float(now-last)/1000.0
			times.append(elapsed)
			draw_calls.append(float(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)))
			if elapsed>16.7:gaps+=1
		last=now
	var result={
		"mode":mode,"enemies":monster_count,"scenery":int(scenery.placed),
		"scenery_types":int(scenery.independent_types),"players_placeholder":20,"drops_markers":500,
		"asset_types":IDS.size(),"action_views_per_asset":ACTION_SUFFIXES.size(),"viewport_width":width,"frames":sample_frames,
		"load_ms":load_ms,"scenery_load_ms":scenery_load_ms,"texture_delta":tex_delta,"node_delta":node_delta,
		"p50_ms":_pct(times,.5),"p95_ms":_pct(times,.95),"p99_ms":_pct(times,.99),
		"draw_calls_p95":_pct(draw_calls,.95),
		"spawn":spawn_count,"despawn":despawn_count,"action_switches":actions,
		"gaps_over_16_7_ms":gaps,"source":"Map13 world.json real scenery, dummy players/drops",
		"world_ysort_enabled":true,"not_full_gameplay":true
	}
	var f:=FileAccess.open(outfile,FileAccess.WRITE)
	if f!=null:f.store_string(JSON.stringify(result)+"\n");f.close()
	print("MIXED_MAP13 ",JSON.stringify(result))
	quit(0)
