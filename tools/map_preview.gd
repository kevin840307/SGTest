extends SceneTree

var report: Dictionary = {}

class Scene extends Node2D:
	var objects := preload("res://scripts/map_objects.gd").new()
	var doll := preload("res://scripts/paper_doll.gd").new()
	var camera := Vector2(4500,1200)
	func _draw() -> void:
		var commands: Array = []
		for object in objects.visible:
			commands.append([float(object.p[1])+float(object.z),object])
		commands.append([1700.0,{}])
		commands.sort_custom(func(a: Array,b: Array) -> bool: return a[0] < b[0])
		for command in commands:
			if command[1].is_empty():
				doll.draw(self,Vector2(5280,1700)-camera,0,8,0)
			else:
				objects.draw_object(self,command[1],camera)

func _initialize() -> void:
	root.content_scale_size = Vector2i(1440,900)
	var floor := preload("res://scripts/terrain_renderer.gd").new()
	var error: Error = floor.open("res://assets/generated",13)
	if error != OK:
		push_error("terrain load failed: %d" % error)
		quit(1)
		return
	var view := Rect2(4500,1200,1440,900)
	var scene := Scene.new()
	scene.objects.configure("res://assets/generated",floor.data)
	scene.objects.prepare(view)
	error = scene.doll.open("res://assets/generated")
	if error != OK:
		quit(1)
		return
	floor.position = -view.position
	floor.z_index = -100
	floor.update_view(view)
	root.add_child(floor)
	root.add_child(scene)
	var bytes := 0
	for entry in scene.objects.cache.values():
		bytes += int(entry.bytes)
	report = {"map_id":13,"fingerprint":floor.data.fingerprint,"total_terrain_cells":int(floor.data.tile_width)*int(floor.data.tile_height),
		"visible_terrain_instances":floor.multimesh.visible_instance_count,"visible_objects":scene.objects.visible.size(),
		"resident_object_types":scene.objects.cache.size(),"object_rgba_bytes":bytes,"renderer":RenderingServer.get_video_adapter_name()}
	print("map floor instances=%d visible objects=%d" % [floor.multimesh.visible_instance_count,scene.objects.visible.size()])
	_capture.call_deferred()

func _capture() -> void:
	await create_timer(0.2).timeout
	await RenderingServer.frame_post_draw
	var error := root.get_texture().get_image().save_png("res://reports/map13.png")
	report["draw_calls"] = Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)
	var file := FileAccess.open("res://reports/map_render.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"  "))
	print("map preview saved; error=%d" % error)
	quit(0 if error == OK else 1)
