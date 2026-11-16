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
	var gradient := Gradient.new()
	gradient.colors = PackedColorArray([first,second])
	var texture := GradientTexture2D.new()
	texture.width = width
	texture.height = height
	texture.gradient = gradient
	return ResourceSaver.save(texture,path) == OK

func build() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2:
		fail("usage: build_online_gate_pack.gd -- <PACK> <SERVER_CONTENT>",2)
		return
	var output := args[0]
	var server_root := args[1]
	var stage_virtual := "user://online_gate_%d" % Time.get_ticks_usec()
	var assets_virtual := stage_virtual.path_join("assets")
	if DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(assets_virtual)) != OK:
		fail("stage directory")
		return
	if DirAccess.make_dir_recursive_absolute(server_root) != OK:
		fail("server content directory")
		return

	var catalog_assets := {}
	var part_index := 0
	for part in Players.PARTS.values():
		var asset_id := str(part[0])
		var label := str(part[1])
		var directory := assets_virtual.path_join(asset_id)
		if DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(directory)) != OK:
			fail("player directory")
			return
		var page := directory.path_join("page.tres")
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
			"version":9,"asset_id":asset_id,"pages":["page.tres"],"page_sizes":[[8,8]],
			"page_sha256":{"page.tres":FileAccess.get_sha256(page)},
			"frames":[{"page":0,"rect":[0,0,8,8],"trim":[0,0],"origin":[4,4]}],
			"clips":{"SOURCE":[0]},"logical_views":views
		}
		if not write_json(directory.path_join("atlas.json"),manifest):
			fail("player manifest")
			return
		catalog_assets[asset_id] = {"kind":"sprite_atlas","file":asset_id+"/atlas.json"}
		part_index += 1

	var catalog_virtual := assets_virtual.path_join("catalog")
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(catalog_virtual))
	if not write_json(catalog_virtual.path_join("players.json"),{"assets":catalog_assets}):
		fail("player catalog")
		return
	if not write_json(assets_virtual.path_join("catalog/assets.json"),{
		"schema":"sgtest.asset_catalog_root/v1",
		"categories":{"players":{"index":"catalog/players.json"}}
	}):
		fail("root catalog")
		return

	for map_id in [13,15]:
		var map_virtual := assets_virtual.path_join("maps").path_join(str(map_id))
		var map_server := server_root.path_join("maps").path_join(str(map_id))
		DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(map_virtual))
		DirAccess.make_dir_recursive_absolute(map_server)
		var terrain := map_virtual.path_join("terrain.tres")
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
		if not write_bytes(map_virtual.path_join("tiles.bin"),tiles) or not write_bytes(map_virtual.path_join("blocking.dat"),blocking):
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
			"collision_sha256":FileAccess.get_sha256(map_virtual.path_join("blocking.dat")),
			"tiles_sha256":FileAccess.get_sha256(map_virtual.path_join("tiles.bin")),
			"terrain_sha256":FileAccess.get_sha256(terrain),
			"scenery_schema":"sgtest.scenery/v1","tile_source_rule":"int16_plus_one_wrap",
			"atlas_grid":[1,1],"tile_side":16,"tile_width":4,"tile_height":4,"tile_ids":[0],
			"scenery_order":{"version":1,"groups":[[],[],[]]},
			"objects":[],"scenery_definitions":{}
		}
		if not write_json(map_virtual.path_join("world.json"),world) or not write_json(map_virtual.path_join("npcs.json"),npcs) or not write_json(map_virtual.path_join("population.json"),population):
			fail("map metadata")
			return
		if not write_bytes(map_server.path_join("blocking.dat"),blocking) or not write_json(map_server.path_join("world.json"),world) or not write_json(map_server.path_join("npcs.json"),npcs):
			fail("server map materialization")
			return

	DirAccess.make_dir_recursive_absolute(output.get_base_dir())
	var pack := PCKPacker.new()
	if pack.pck_start(output) != OK:
		fail("pck_start",3)
		return
	var source_root := ProjectSettings.globalize_path(assets_virtual)
	var files: Array[String] = []
	collect(source_root,"",files)
	for relative in files:
		if pack.add_file("res://assets/"+relative,source_root.path_join(relative),false) != OK:
			fail("pck add "+relative,4)
			return
	if pack.flush(false) != OK:
		fail("pck flush",5)
		return
	print("ONLINE_GATE_PACK_READY sha256=",FileAccess.get_sha256(output)," files=",files.size())
	quit(0)

func collect(root: String, relative: String, out: Array[String]) -> void:
	var at := root if relative.is_empty() else root.path_join(relative)
	var directory := DirAccess.open(at)
	if directory == null:
		return
	for child in directory.get_directories():
		collect(root,child if relative.is_empty() else relative.path_join(child),out)
	for name in directory.get_files():
		out.append(name if relative.is_empty() else relative.path_join(name))
	out.sort()
