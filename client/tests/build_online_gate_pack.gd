extends SceneTree
const Players = preload("res://rendering/player_presentation.gd")

func _initialize() -> void:
	build.call_deferred()

func fail(message: String, code := 1) -> void:
	push_error(message)
	quit(code)

func write_json(path: String, value: Variant) -> bool:
	var file := FileAccess.open(path,FileAccess.WRITE)
	if file == null:
		return false
	file.store_string(JSON.stringify(value))
	file.close()
	return true

func write_bytes(path: String, value: PackedByteArray) -> bool:
	var file := FileAccess.open(path,FileAccess.WRITE)
	if file == null:
		return false
	file.store_buffer(value)
	file.close()
	return true

func make_texture(path: String, width: int, height: int, first: Color, second: Color) -> bool:
	var image := Image.create(width,height,false,Image.FORMAT_RGBA8)
	for y in height:
		for x in width:
			var mix := float(x+y)/float(maxi(1,width+height-2))
			image.set_pixel(x,y,first.lerp(second,mix))
	return image.save_png(path) == OK

func build() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2:
		fail("usage: build_online_gate_pack.gd -- <SOURCE_ASSETS> <SERVER_CONTENT>",2)
		return
	var assets_root := args[0]
	var server_root := args[1]
	if DirAccess.make_dir_recursive_absolute(assets_root) != OK:
		fail("source assets directory")
		return
	if DirAccess.make_dir_recursive_absolute(server_root) != OK:
		fail("server content directory")
		return

	var catalog_assets := {}
	var part_index := 0
	for part in Players.PARTS.values():
		var asset_id := str(part[0])
		var label := str(part[1])
		var directory := assets_root.path_join(asset_id)
		if DirAccess.make_dir_recursive_absolute(directory) != OK:
			fail("player directory")
			return
		var page := directory.path_join("page.png")
		var shade := 0.20 + 0.10 * part_index
		if not make_texture(page,8,8,Color(shade,0.65,0.9),Color(0.9,shade,0.5)):
			fail("player texture")
			return
		var views := {}
		for action in ["NS","NR"]:
			for facing in range(1,9):
				views[label+"_"+action+str(facing)] = {
					"clip":"SOURCE","flip_h":facing>=5,
					"timeline_ticks":[[0,8]],"loop":true,
					"draw_slots":[12,7,9,5,0,1]
				}
		var manifest := {
			"version":9,"asset_id":asset_id,"pages":["page.png"],"page_sizes":[[8,8]],
			"page_sha256":{"page.png":FileAccess.get_sha256(page)},
			"frames":[{"page":0,"rect":[0,0,8,8],"trim":[0,0],"origin":[4,4]}],
			"clips":{"SOURCE":[0]},"logical_views":views
		}
		if not write_json(directory.path_join("atlas.json"),manifest):
			fail("player manifest")
			return
		catalog_assets[asset_id] = {"kind":"sprite_atlas","file":asset_id+"/atlas.json"}
		part_index += 1

	var catalog := assets_root.path_join("catalog")
	DirAccess.make_dir_recursive_absolute(catalog)
	if not write_json(catalog.path_join("players.json"),{"assets":catalog_assets}):
		fail("player catalog")
		return
	if not write_json(catalog.path_join("assets.json"),{
		"schema":"sgtest.asset_catalog_root/v1",
		"categories":{"players":{"index":"catalog/players.json"}}
	}):
		fail("root catalog")
		return

	for map_id in [13,15]:
		var map_assets := assets_root.path_join("maps").path_join(str(map_id))
		var map_server := server_root.path_join("maps").path_join(str(map_id))
		DirAccess.make_dir_recursive_absolute(map_assets)
		DirAccess.make_dir_recursive_absolute(map_server)
		var terrain := map_assets.path_join("terrain.png")
		if not make_texture(terrain,16,16,Color(0.12,0.18,0.12),Color(0.22,0.28,0.18)):
			fail("terrain texture")
			return
		var tiles := PackedByteArray()
		tiles.resize(8 + 4*4*2)
		tiles.encode_u32(0,4)
		tiles.encode_u32(4,4)
		for i in 16:
			tiles.encode_u16(8+i*2,0)
		var blocking := PackedByteArray()
		blocking.resize(8 + 4*4)
		blocking.encode_u32(0,4)
		blocking.encode_u32(4,4)
		if not write_bytes(map_assets.path_join("tiles.bin"),tiles) or not write_bytes(map_assets.path_join("blocking.dat"),blocking):
			fail("map bytes")
			return
		var spawn_id := 46 if map_id == 15 else 1
		var target := 13 if map_id == 15 else 15
		var npcs := {
			"schema":"sgtest.npcs/v1","coordinate_space":"world_pixels","map_id":map_id,
			"npcs":[{"spawn_id":spawn_id,"p":[24,24],"interaction":{
				"mode":"confirm_transfer","adapter":"audited_TS_Common_v1",
				"dependencies_complete":true,"destination_status":"walkable","choice":0,
				"destination":{"map_id":target,"p":[24,24]}
			}}]
		}
		var population := {"version":3,"map_id":map_id,"definitions":[]}
		var world := {
			"version":3,"map_id":map_id,"collision_cell":16,"size":[64,64],
			"collision_sha256":FileAccess.get_sha256(map_assets.path_join("blocking.dat")),
			"tiles_sha256":FileAccess.get_sha256(map_assets.path_join("tiles.bin")),
			"terrain_sha256":FileAccess.get_sha256(terrain),
			"scenery_schema":"sgtest.scenery/v1","tile_source_rule":"int16_plus_one_wrap",
			"atlas_grid":[1,1],"tile_side":16,"tile_width":4,"tile_height":4,"tile_ids":[0],
			"scenery_order":{"version":1,"groups":[[],[],[]]},
			"objects":[],"scenery_definitions":{}
		}
		if not write_json(map_assets.path_join("world.json"),world) or not write_json(map_assets.path_join("npcs.json"),npcs) or not write_json(map_assets.path_join("population.json"),population):
			fail("map metadata")
			return
		if not write_bytes(map_server.path_join("blocking.dat"),blocking) or not write_json(map_server.path_join("world.json"),world) or not write_json(map_server.path_join("npcs.json"),npcs):
			fail("server map materialization")
			return

	print("ONLINE_GATE_SOURCE_READY assets=",assets_root," server=",server_root)
	quit(0)
