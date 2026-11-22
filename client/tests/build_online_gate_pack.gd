extends SceneTree
const Players = preload("res://player/player_presentation.gd")
const Appearance = preload("res://player/character_appearance_profile.gd")

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
	# Online fixture must include the SAME default starter clothing overlays
	# as production (body, shorts and both sleeve layers), not only bare limbs.
	var default_profile: Dictionary = Appearance.definition({})
	var source_parts: Array = default_profile.parts.values()
	source_parts.append_array(default_profile.overlays.values())
	for part in source_parts:
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
	var monster_id := "Enemy/en001a"
	var monster_dir := assets_root.path_join(monster_id)
	DirAccess.make_dir_recursive_absolute(monster_dir)
	var monster_page := monster_dir.path_join("page.png")
	if not make_texture(monster_page,8,8,Color(0.80,0.25,0.20),Color(0.95,0.70,0.20)):
		fail("monster texture")
		return
	var monster_views := {}
	var monster_clips := {}
	for action in ["NS","NW","FA","NH","ND"]:
		monster_clips[action] = [0]
		for facing in range(1,9):
			monster_views["EN001A_"+action+str(facing)] = {
				"clip":action,"flip_h":facing>=5,
				"timeline_ticks":[[0,8]],"loop":action!="ND"
			}
	var monster_manifest := {
		"version":9,"asset_id":monster_id,"pages":["page.png"],"page_sizes":[[8,8]],
		"page_sha256":{"page.png":FileAccess.get_sha256(monster_page)},
		"frames":[{"page":0,"rect":[0,0,8,8],"trim":[0,0],"origin":[4,4]}],
		"clips":monster_clips,"logical_views":monster_views
	}
	if not write_json(monster_dir.path_join("atlas.json"),monster_manifest):
		fail("monster manifest")
		return
	if not write_json(catalog.path_join("Enemy.json"),{"assets":{
		monster_id:{"kind":"sprite_atlas","file":monster_id+"/atlas.json"}
	}}):
		fail("monster catalog")
		return
	# Rendered NPCs are essential to detecting the regression where a bad
	# walking player makes ALL players and NPC sprites disappear.
	var npc_id := "npcs/npc003"
	var npc_dir := assets_root.path_join(npc_id)
	DirAccess.make_dir_recursive_absolute(npc_dir)
	var npc_page := npc_dir.path_join("page.png")
	if not make_texture(npc_page,24,40,Color(0.7,0.55,0.18),Color(0.95,0.87,0.37)):
		fail("NPC texture")
		return
	var npc_manifest := {
		"version":9,"asset_id":npc_id,"pages":["page.png"],"page_sizes":[[24,40]],
		"page_sha256":{"page.png":FileAccess.get_sha256(npc_page)},
		"frames":[{"page":0,"rect":[0,0,24,40],"trim":[0,0],"origin":[12,36]}],
		"clips":{"NPC003":[0]}
	}
	if not write_json(npc_dir.path_join("atlas.json"),npc_manifest) or \
			not write_json(catalog.path_join("npcs.json"),{"assets":{
			npc_id:{"kind":"sprite_atlas","file":npc_id+"/atlas.json"}
		}}):
		fail("NPC atlas/catalog")
		return
	if not write_json(catalog.path_join("assets.json"),{
		"schema":"sgtest.asset_catalog_root/v1",
		"categories":{
			"players":{"index":"catalog/players.json"},
			"Enemy":{"index":"catalog/Enemy.json"},
			"npcs":{"index":"catalog/npcs.json"}
		}
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
		const CELLS := 128
		var tiles := PackedByteArray()
		tiles.resize(8 + CELLS*CELLS*2)
		tiles.encode_u32(0,CELLS)
		tiles.encode_u32(4,CELLS)
		for i in CELLS*CELLS:
			tiles.encode_u16(8+i*2,0)
		var blocking := PackedByteArray()
		blocking.resize(8 + CELLS*CELLS)
		blocking.encode_u32(0,CELLS)
		blocking.encode_u32(4,CELLS)
		if map_id == 15:
			# Force the actor gate through a real obstacle. Actor starts at
			# (760,800), target is (952,808); x=54 blocks the straight chord,
			# while observer (880,800) remains on the east side.
			for y in range(47,54):
				blocking[8+y*CELLS+54] = 1
		if not write_bytes(map_assets.path_join("tiles.bin"),tiles) or not write_bytes(map_assets.path_join("blocking.dat"),blocking):
			fail("map bytes")
			return
		var spawn_id := 46 if map_id == 15 else 1
		var target := 13 if map_id == 15 else 15
		var npcs := {
			"schema":"sgtest.npcs/v1","coordinate_space":"world_pixels","map_id":map_id,
			"npcs":[{"spawn_id":spawn_id,"name":"測試傳點管理員","p":[800,800],
				"visual":{"resolved":true,"atlas":"npcs/npc003/atlas.json"},
				"interaction":{
				"mode":"confirm_transfer","adapter":"audited_TS_Common_v1",
				"dependencies_complete":true,"destination_status":"walkable","choice":0,
				"destination":{"map_id":target,"p":[800,800]}
			}}]
		}
		var population := {
			"version":3,"kind":"visual_spawn_content","map_id":map_id,
			"stats_verified":false,"gameplay_ready":false,
			"definitions":[{
				"id":1,"sprite":"EN001A","atlas":"Enemy/en001a/atlas.json",
				"stats":null,"stats_verified":false
			}],
			"spawns":[{
				"definition":1,"source_index":0,"source_flags":7,
				"p":[820,800] if map_id==15 else [800,800]
			}]
		}
		var world := {
			"version":3,"map_id":map_id,"collision_cell":16,"size":[2048,2048],
			"collision_sha256":FileAccess.get_sha256(map_assets.path_join("blocking.dat")),
			"tiles_sha256":FileAccess.get_sha256(map_assets.path_join("tiles.bin")),
			"terrain_sha256":FileAccess.get_sha256(terrain),
			"scenery_schema":"sgtest.scenery/v1","tile_source_rule":"int16_plus_one_wrap",
			"atlas_grid":[1,1],"tile_side":16,"tile_width":128,"tile_height":128,"tile_ids":[0],
			"scenery_order":{"version":1,"groups":[[],[],[]]},
			"objects":[],"scenery_definitions":{}
		}
		if not write_json(map_assets.path_join("world.json"),world) or not write_json(map_assets.path_join("npcs.json"),npcs) or not write_json(map_assets.path_join("population.json"),population):
			fail("map metadata")
			return
		var verified_population := {
			"version":4,
			"kind":"verified_monster_runtime",
			"map_id":map_id,
			"stats_verified":true,
			"gameplay_ready":true,
			"definitions":[{
				"id":1,"name":"Synthetic Gate Monster","sprite":"EN001A",
				"atlas":"Enemy/EN001A/atlas.json",
				"stats":{
					"level":3,"max_hp":100,"attack_damage":6,"move_step":8.0,
					"aggro_radius":1.0,"lose_target_radius":500.0,
					"leash_radius":500.0,"attack_range":48.0,
					"attack_cooldown_ticks":8,"respawn_ticks":90,
					"wander_radius":0.0,"wander_interval_ticks":60
				}
			}],
			"spawns":[{
				"source_index":0,"source_flags":7,"definition":1,
				"p":[820,800] if map_id == 15 else [800,800]
			}],
			"test_only":true
		}
		if not write_bytes(map_server.path_join("blocking.dat"),blocking) or not write_json(map_server.path_join("world.json"),world) or not write_json(map_server.path_join("npcs.json"),npcs) or not write_json(map_server.path_join("population.json"),verified_population):
			fail("server map materialization")
			return

	print("ONLINE_GATE_SOURCE_READY assets=",assets_root," server=",server_root)
	quit(0)
