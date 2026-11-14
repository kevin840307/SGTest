extends SceneTree

class Preview extends Node2D:
	var doll := preload("res://scripts/paper_doll.gd").new()
	var monster := preload("res://scripts/entity_atlas.gd").new()
	var headings := [6,7,8,1,2,3,4,5]
	var names := ["E","SE","S","SW","W","NW","N","NE"]
	func _draw() -> void:
		draw_rect(Rect2(0,0,1440,900),Color(0.09,0.12,0.14))
		for row in 4:
			for i in 8:
				var at := Vector2(90+i*180,165+row*210)
				if row < 3:
					doll.draw(self,at,row,headings[i],0.175)
				else:
					monster.draw_frame(self,at,"EN010A_NW%d" % headings[i],0.175)
				var from := at+Vector2(0,16)
				var to := from+Vector2.from_angle(i*PI/4.0)*22
				draw_line(from,to,Color.CYAN,2)
				draw_circle(to,3,Color.CYAN)
				draw_string(ThemeDB.fallback_font,at+Vector2(-70,42),"%s %s / %d" % [["idle","walk","attack","monster"][row],names[i],headings[i]],HORIZONTAL_ALIGNMENT_LEFT,-1,16)

func _initialize() -> void:
	root.content_scale_size = Vector2i(1440,900)
	var preview := Preview.new()
	var error: Error = preview.doll.open("res://assets/generated")
	if error == OK:
		error = preview.monster.open("res://assets/generated/bundles/map/13/sprites.json")
	if error != OK:
		push_error("direction preview asset load failed: %d" % error)
		quit(1)
		return
	root.add_child(preview)
	_capture.call_deferred()

func _capture() -> void:
	await create_timer(0.2).timeout
	await RenderingServer.frame_post_draw
	var error := root.get_texture().get_image().save_png(ProjectSettings.globalize_path("res://").path_join("../reports/directions.png"))
	print("direction preview saved; error=%d" % error)
	quit(0 if error == OK else 1)
