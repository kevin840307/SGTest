extends SceneTree
# Pixel-output parity between a region draw and AnimatedSprite2D for the SAME
# compressed atlas, including original frame origin and mirrored directions.
# This is an independent correctness test, NOT a performance benchmark.
const SAMPLES:=["enemy_small","enemy_medium","enemy_huge","mixed_en114a"]
const VIEWS:=["NS1","NS4","NW1","NW6","FA1","FA6","NH1","ND1"]

class RegionCanvas:
	extends Node2D
	var texture:Texture2D
	var region:Rect2
	var offset:Vector2
	var mirrored:=false
	func _draw()->void:
		var at:=Vector2(512,512)
		if mirrored:
			draw_set_transform(at,0,Vector2(-1,1))
			draw_texture_rect_region(texture,Rect2(offset,region.size),region,Color.WHITE,false,true)
			draw_set_transform(Vector2.ZERO)
		else:
			draw_texture_rect_region(texture,Rect2(at+offset,region.size),region,Color.WHITE,false,true)

func _initialize()->void:
	call_deferred("_run")

func _render(viewport:SubViewport)->PackedByteArray:
	for _i in 3:await process_frame
	var img:=viewport.get_texture().get_image()
	img.convert(Image.FORMAT_RGBA8)
	return img.get_data()

func _run()->void:
	var args:=OS.get_cmdline_user_args()
	var output:=str(args[0]) if args.size()>0 else "C:/SGO/SRC/SGTest/.cache/pixel_parity.json"
	var viewport:=SubViewport.new()
	viewport.size=Vector2i(1024,1024)
	viewport.transparent_bg=true
	viewport.render_target_update_mode=SubViewport.UPDATE_ALWAYS
	viewport.disable_3d=true
	root.add_child(viewport)
	var region_canvas:=RegionCanvas.new()
	region_canvas.texture_filter=CanvasItem.TEXTURE_FILTER_NEAREST
	viewport.add_child(region_canvas)
	var sprite:=Sprite2D.new()
	sprite.centered=false
	sprite.texture_filter=CanvasItem.TEXTURE_FILTER_NEAREST
	sprite.position=Vector2(512,512)
	sprite.visible=false
	viewport.add_child(sprite)
	var results:Array=[]
	var total_pixels:=0
	var total_mismatch:=0
	var max_mismatch:=0
	var verified_flips:=0
	for sample in SAMPLES:
		var path:="res://assets/height_p0/%s/atlas.json" % sample
		var doc=JSON.parse_string(FileAccess.get_file_as_string(path))
		if not doc is Dictionary:
			printerr("missing "+path);quit(2);return
		var group:=str(doc.get("group",""))
		var ident:=group.get_file().to_upper()
		var canonical:Dictionary=JSON.parse_string(FileAccess.get_file_as_string("C:/SGO/SRC/SGTest/client/assets/Enemy/%s/atlas.json" % ident.to_lower()))
		var pages:Array[Texture2D]=[]
		for name in doc.get("pages",[]):
			pages.append(ResourceLoader.load(path.get_base_dir().path_join(str(name))))
		var manifest_frames:Array=doc.get("frames",[])
		for suffix in VIEWS:
			var view:Dictionary=canonical.get("logical_views",{}).get(ident+"_"+suffix,{})
			if view.is_empty():continue
			var clip_name:=str(view.get("clip",""))
			var clip:Array=doc.get("clips",{}).get(clip_name,[])
			if clip.is_empty():continue
			var flip:=bool(view.get("flip_h",false))
			var indices:Array[int]=[0,mini(2,clip.size()-1),clip.size()-1]
			for local in indices:
				var frame:Dictionary=manifest_frames[int(clip[local])]
				var page:=int(frame.get("page",-1))
				if page<0 or page>=pages.size():continue
				var rect:Array=frame.get("rect",[0,0,0,0])
				var trim:Array=frame.get("trim",[0,0])
				var origin:Array=frame.get("origin",[0,0])
				var region:=Rect2(float(rect[0]),float(rect[1]),float(rect[2]),float(rect[3]))
				var off:=Vector2(float(trim[0])-float(origin[0]),float(trim[1])-float(origin[1]))
				region_canvas.texture=pages[page]
				region_canvas.region=region
				region_canvas.offset=off
				region_canvas.mirrored=flip
				sprite.visible=false
				region_canvas.visible=true
				region_canvas.queue_redraw()
				var direct:PackedByteArray=await _render(viewport)
				# Prevent a false pass if both draw paths accidentally render blank.
				var visible_pixels:=0
				for alpha_idx in range(3,direct.size(),4):
					if direct[alpha_idx]>0:
						visible_pixels+=1
				if visible_pixels<=0:
					printerr("BLANK DRAW: "+sample+" "+suffix)
					quit(7);return
				var atlas_tex:=AtlasTexture.new()
				atlas_tex.atlas=pages[page]
				atlas_tex.region=region
				atlas_tex.filter_clip=true
				sprite.texture=atlas_tex
				sprite.flip_h=flip
				sprite.offset=Vector2(-off.x-region.size.x,off.y) if flip else off
				region_canvas.visible=false
				sprite.visible=true
				var sprite_pixels:PackedByteArray=await _render(viewport)
				var mismatches:=0
				for j in range(0,direct.size(),4):
					if direct[j]!=sprite_pixels[j] or direct[j+1]!=sprite_pixels[j+1] or direct[j+2]!=sprite_pixels[j+2] or direct[j+3]!=sprite_pixels[j+3]:
						mismatches+=1
				var pixels:=viewport.size.x*viewport.size.y
				total_pixels+=pixels;total_mismatch+=mismatches
				max_mismatch=maxi(max_mismatch,mismatches)
				if flip:verified_flips+=1
				results.append({"sample":sample,"view":suffix,"frame":local,"flipped":flip,"mismatch_pixels":mismatches,"pixels":pixels})
				print("CHECK",sample,suffix,local,"flip",flip,"pixel_diff",mismatches)
	var result={"cases":results.size(),"flipped_cases":verified_flips,"total_pixels":total_pixels,
		"mismatch_pixels":total_mismatch,"max_case_mismatch":max_mismatch,"results":results}
	var f:=FileAccess.open(output,FileAccess.WRITE)
	if f!=null:f.store_string(JSON.stringify(result)+"\n");f.close()
	print("SUMMARY",JSON.stringify({"cases":results.size(),"flips":verified_flips,
		"pixel_mismatch":total_mismatch,"max_case":max_mismatch}))
	quit(0 if total_mismatch==0 and verified_flips>0 else 5)
