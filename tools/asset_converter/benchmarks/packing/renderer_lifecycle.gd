extends SceneTree
# Repeated imported-texture SpriteFrames unload for AFK/map-change lifecycle.
const MONSTERS:=["EN114A","EN089A","EN121A","EN116A","EN023A","EN084A","EN111A","EN125A","EN050C","EN080A","EN093A","EN079A"]

func _initialize()->void:
	call_deferred("_run")

func _run()->void:
	var args:=OS.get_cmdline_user_args()
	var output:=str(args[0]) if args.size()>0 else "C:/SGO/SRC/SGTest/.cache/sprite_lifecycle.json"
	DisplayServer.window_set_size(Vector2i(1280,720));root.size=Vector2i(1280,720)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	Engine.max_fps=0;RenderingServer.render_loop_enabled=true
	for _k in 5:await process_frame
	var tex_base:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
	var nodes_base:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var results:Array=[]
	var min_after:int=9223372036854775807
	var max_after:=0
	for cycle in 5:
		var start:=Time.get_ticks_usec()
		var shared:Array[SpriteFrames]=[]
		for monster in MONSTERS:
			var name:="res://assets/height_p0/mixed_%s/atlas.json" % monster.to_lower()
			var data=JSON.parse_string(FileAccess.get_file_as_string(name))
			if not data is Dictionary:printerr("missing ",name);quit(3);return
			var pages:Array[Texture2D]=[]
			for rel in data.get("pages",[]):
				var image=ResourceLoader.load(name.get_base_dir().path_join(str(rel))) as Texture2D
				if image==null:printerr("missing image ",rel);quit(4);return
				pages.append(image)
			var sf:=SpriteFrames.new();sf.remove_animation("default")
			sf.add_animation("idle")
			sf.set_animation_speed("idle",8.0)
			var clip:Array=data.get("clips",{}).get(monster+"_NS1",[])
			var frames:Array=data.get("frames",[])
			for index in clip:
				var record:Dictionary=frames[int(index)]
				var page:=int(record.get("page",-1))
				if page<0:continue
				var rr: Array=record.get("rect",[0,0,0,0])
				var a:=AtlasTexture.new()
				a.atlas=pages[page]
				a.region=Rect2(rr[0],rr[1],rr[2],rr[3])
				a.filter_clip=true
				sf.add_frame("idle",a)
			shared.append(sf)
		var host:=Node2D.new();root.add_child(host)
		for i in 1000:
			var s:=AnimatedSprite2D.new()
			s.sprite_frames=shared[i%shared.size()]
			s.centered=false
			s.position=Vector2(24+(i*83)%1220,32+(i*47)%660)
			host.add_child(s)
			s.play("idle")
		for _j in 8:await process_frame
		RenderingServer.force_sync()
		var loaded_tex:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
		var loaded_nodes:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
		var load_ms:=float(Time.get_ticks_usec()-start)/1000.0
		host.queue_free()
		host=null
		shared.clear()
		for _j in 12:await process_frame
		RenderingServer.force_sync()
		var after_tex:=int(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
		var after_nodes:=int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
		min_after=mini(min_after,after_tex);max_after=maxi(max_after,after_tex)
		var row={"cycle":cycle+1,"load_ms":load_ms,
			"loaded_texture_delta":maxi(0,loaded_tex-tex_base),
			"unloaded_texture_delta":maxi(0,after_tex-tex_base),
			"loaded_node_delta":loaded_nodes-nodes_base,
			"unloaded_node_delta":after_nodes-nodes_base,
			"static_bytes":OS.get_static_memory_usage()}
		results.append(row)
		print("LIFECYCLE ",JSON.stringify(row))
	var report={"cycles":results,"baseline_tex":tex_base,"baseline_nodes":nodes_base,
		"max_unloaded_texture_delta":maxi(0,max_after-tex_base),
		"texture_returned_to_base":max_after-tex_base<2*1024*1024,
		"nodes_returned_to_base":all_nodes_released(results)}
	var file:=FileAccess.open(output,FileAccess.WRITE)
	if file!=null:file.store_string(JSON.stringify(report)+"\n");file.close()
	print("SUMMARY ",JSON.stringify({"texture_returned":report.texture_returned_to_base,
		"nodes_returned":report.nodes_returned_to_base,
		"max_unloaded_texture":report.max_unloaded_texture_delta}))
	quit(0 if report.texture_returned_to_base and report.nodes_returned_to_base else 5)

func all_nodes_released(rows:Array)->bool:
	for row in rows:
		if abs(int(row.unloaded_node_delta))>2:return false
	return true
