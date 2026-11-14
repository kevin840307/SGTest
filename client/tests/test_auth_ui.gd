extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var ui = preload("res://scripts/auth_ui.gd").new()
	get_root().add_child(ui)
	await process_frame
	assert(ui.login_center.visible)
	assert(not ui.character_center.visible)
	ui.show_characters([[11,"HeroA"],[12,"英雄B"]],"ready")
	assert(not ui.login_center.visible)
	assert(ui.character_center.visible)
	assert(ui.character_list.item_count == 2)
	assert(int(ui.character_list.get_item_metadata(0)) == 11)
	assert(ui.character_status.text == "ready")
	ui.set_character_status("created")
	assert(ui.character_status.text == "created")
	ui.show_login("again")
	assert(ui.login_center.visible)
	assert(not ui.character_center.visible)
	assert(ui.login_status.text == "again")
	ui.hide_all()
	assert(not ui.login_center.visible and not ui.character_center.visible)
	ui.queue_free()
	print("auth UI login/character selection state checks passed")
	quit()
