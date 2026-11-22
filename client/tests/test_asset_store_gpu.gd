extends SceneTree
const Store = preload("res://asset_store/asset_store.gd")
func _initialize() -> void:
	run.call_deferred()
func run() -> void:
	var store = Store.new()
	var args := OS.get_cmdline_user_args()
	if args.size() != 1:
		push_error("Expected a synthetic atlas PCK path")
		quit(2)
		return
	var path := args[0]
	if store.open_pack(path) != OK:
		quit(2)
		return
	root.size = Vector2i(320, 240)
	for frame in range(8):
		await RenderingServer.frame_post_draw
	var baseline = Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)
	var peak = baseline
	var max_residual = 0
	for cycle in range(30):
		var a = store.begin_scope(&"map")
		var b = store.begin_scope(&"actor")
		var id = "test/shared"
		if store.acquire(&"map", a, id) != OK or store.acquire(&"actor", b, id) != OK:
			quit(3)
			return
		var deadline = Time.get_ticks_msec() + 2000
		while store.get_asset(&"map", a, id).is_empty() and Time.get_ticks_msec() < deadline:
			store.pump()
			await create_timer(0.001).timeout
		if store.statistics().ready != 1:
			quit(4)
			return
		var sprite = Sprite2D.new()
		sprite.position = Vector2(100, 100)
		var texture_ref := bind_shared_texture(store, sprite, a, b, id)
		if texture_ref == null:
			push_error("Map and actor received different Texture resources")
			quit(8)
			return
		root.add_child(sprite)
		for frame in range(3):
			await RenderingServer.frame_post_draw
		peak = max(peak, Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED))
		store.end_scope(&"map", a)
		if store.get_asset(&"actor", b, id).is_empty():
			quit(5)
			return
		await RenderingServer.frame_post_draw
		var image := root.get_texture().get_image()
		if not image.get_pixel(100, 100).is_equal_approx(Color8(20, 100, 210)):
			push_error("Shared sprite was not drawn after map scope release")
			quit(10)
			return
		sprite.texture = null
		sprite.queue_free()
		store.end_scope(&"actor", b)
		if store.statistics() != {"assets":0,"scopes":0,"ready":0,"decode_jobs":0,"texture_jobs":0,"rgba_bytes":0}:
			quit(6)
			return
		for frame in range(3):
			await RenderingServer.frame_post_draw
		max_residual = max(max_residual, Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED) - baseline)
		if texture_ref.get_ref() != null:
			push_error("Texture survived its last scope in cycle %d" % (cycle + 1))
			quit(9)
			return
	print("GPU_TEXTURE_BYTES baseline=", baseline, " peak=", peak, " max_residual=", max_residual)
	if peak <= baseline or max_residual != 0:
		quit(7)
		return
	print("SYNTHETIC_ASSET_SCOPE_PASS cycles=30 ", store.statistics())
	quit()


func bind_shared_texture(store: RefCounted, sprite: Sprite2D, map_epoch: int, actor_epoch: int, id: String) -> WeakRef:
	var map_texture: Texture2D = store.get_asset(&"map", map_epoch, id).pages[0]
	var actor_texture: Texture2D = store.get_asset(&"actor", actor_epoch, id).pages[0]
	if map_texture != actor_texture:
		return null
	sprite.texture = map_texture
	return weakref(map_texture)
