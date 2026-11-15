extends SceneTree
# Verify visual ordering across scenery, player and monster siblings.
# A single monster-only Canvas is not equivalent to interleaved world Y-sort.
const SIZE:=Vector2i(256,256)
const PROBE:=Vector2i(126,137)

func _initialize()->void:
	call_deferred("_run")

func _solid(c:Color)->Texture2D:
	var img:=Image.create(64,64,false,Image.FORMAT_RGBA8)
	img.fill(c)
	return ImageTexture.create_from_image(img)

func _sprite(texture:Texture2D,at:Vector2)->Sprite2D:
	var s:=Sprite2D.new()
	s.texture=texture
	s.centered=false
	s.position=at
	return s

func _sample(viewport:SubViewport)->Color:
	for i in 4:
		await process_frame
	return viewport.get_texture().get_image().get_pixelv(PROBE)

func _assert_color(actual:Color,wanted:Color,description:String)->bool:
	var ok:=absf(actual.r-wanted.r)<0.03 and absf(actual.g-wanted.g)<0.03 and absf(actual.b-wanted.b)<0.03 and actual.a>0.9
	print("YSORT ",description," expected=",wanted," got=",actual," pass=",ok)
	return ok

func _run()->void:
	var out:="C:/SGO/SRC/SGTest/.cache/ysort_layer_parity.json"
	var args:=OS.get_cmdline_user_args()
	if not args.is_empty():out=str(args[0])
	var viewport:=SubViewport.new()
	viewport.size=SIZE;viewport.transparent_bg=true
	viewport.disable_3d=true;viewport.render_target_update_mode=SubViewport.UPDATE_ALWAYS
	root.add_child(viewport)
	var world:=Node2D.new()
	world.y_sort_enabled=true
	viewport.add_child(world)
	var grass_color:=Color(0,1,0,1)
	var player_color:=Color(0,0,1,1)
	var monster_color:=Color(1,0,0,1)
	var scenery:=_sprite(_solid(grass_color),Vector2(100,100))
	var player:=_sprite(_solid(player_color),Vector2(100,110))
	var monster:=_sprite(_solid(monster_color),Vector2(100,120))
	world.add_child(scenery);world.add_child(player);world.add_child(monster)
	var results:Dictionary={}
	results["monster_front"]=_assert_color(await _sample(viewport),monster_color,"monster_front")
	monster.position.y=95
	results["player_front"]=_assert_color(await _sample(viewport),player_color,"player_front")
	player.position.y=90
	results["scenery_front"]=_assert_color(await _sample(viewport),grass_color,"scenery_front")
	scenery.queue_free()
	for i in 3:await process_frame
	results["monster_revealed"]=_assert_color(await _sample(viewport),monster_color,"monster_revealed")
	monster.position.y=70
	results["player_revealed"]=_assert_color(await _sample(viewport),player_color,"player_revealed")
	var passed:=0
	for k in results:
		if bool(results[k]):passed+=1
	var result={"checks":results,"passed":passed,"total":results.size(),"world_ysort_enabled":world.y_sort_enabled}
	var f:=FileAccess.open(out,FileAccess.WRITE)
	if f!=null:f.store_string(JSON.stringify(result)+"\n");f.close()
	print("YSORT_SUMMARY ",JSON.stringify(result))
	quit(0 if passed==results.size() else 5)
