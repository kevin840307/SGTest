extends SceneTree
const Preview = preload("res://player/character_preview.gd")
const Players = preload("res://player/player_presentation.gd")
var _failed := false

class TestStore extends RefCounted:
	signal asset_failed(owner: StringName, epoch: int, id: String, reason: String)
	var assets := {}
	var scopes := {}
	var serial := 0
	var reject := false
	func begin_scope(owner: StringName) -> int:
		serial += 1
		scopes[owner] = serial
		return serial
	func acquire(_owner: StringName, _epoch: int, _id: String) -> Error:
		return ERR_FILE_NOT_FOUND if reject else OK
	func get_asset(_owner: StringName, _epoch: int, id: String) -> Dictionary:
		return assets.get(id,{})
	func end_scope(owner: StringName, _epoch: int) -> void:
		scopes.erase(owner)

func _initialize() -> void:
	_run.call_deferred()

func check(ok: bool, message: String) -> void:
	if not ok:
		_failed = true
		push_error("CHARACTER_PREVIEW_FAIL "+message)
		quit(1)

func _run() -> void:
	var store := TestStore.new()
	var image := Image.create(4,4,false,Image.FORMAT_RGBA8)
	image.fill(Color.WHITE)
	var texture := ImageTexture.create_from_image(image)
	var slots := [12,7,9,5,0,1]
	for part in Players.PARTS.values():
		var views := {}
		for action in ["NS","NR"]:
			for facing in range(1,9):
				views[str(part[1])+"_"+action+str(facing)] = {
					"clip":action,"flip_h":false,"timeline_ticks":[[0,8],[1,8]],
					"loop":true,"draw_slots":slots}
		store.assets[part[0]] = {"pages":[texture],"manifest":{
			"logical_views":views,"clips":{"NS":[0,1],"NR":[0,1]},"frames":[
				{"page":0,"rect":[0,0,3,2],"trim":[2,0],"origin":[1,1]},
				{"page":0,"rect":[0,2,3,2],"trim":[2,0],"origin":[1,1]}]}}
	var previews: Array = []
	for i in 2:
		var preview := Preview.new()
		preview.size = Vector2(176,200)
		preview.bind_store(store)
		root.add_child(preview)
		preview.set_occupied(true)
		previews.append(preview)
	await process_frame
	await process_frame
	check(store.scopes.size()==2,"cards must own distinct scopes in one store")
	for preview in previews:
		check(preview.status=="ready" and preview._canvas._commands.size()==6,"six-part standing paper doll missing")
		for index in 6:
			var command: Dictionary = preview._canvas._commands[index]
			check(command.texture==texture and int(command.slot)==slots[index],"texture sharing or original part order changed")
	var first_frame := int(previews[0]._canvas._commands[0].frame)
	var deadline := Time.get_ticks_msec()+2000
	while int(previews[0]._canvas._commands[0].frame)==first_frame and Time.get_ticks_msec()<deadline:
		await process_frame
	check(int(previews[0]._canvas._commands[0].frame)!=first_frame,"standing animation did not advance at fixed ACT timing")
	previews[0].hide()
	check(store.scopes.size()==1 and previews[0]._canvas._commands.is_empty() and not previews[0].is_processing(),"hidden card retained resources or render work")
	previews[1].set_occupied(false)
	check(store.scopes.is_empty(),"empty slot retained scope")
	store.reject = true
	previews[0].show()
	check(previews[0].status=="unavailable" and store.scopes.is_empty(),"missing asset did not fail closed")
	store.reject = false
	previews[0].hide()
	previews[0].show()
	store.asset_failed.emit(previews[0]._owner,previews[0]._epoch,"players/b/base/body","fixture failure")
	check(previews[0].status=="unavailable" and store.scopes.is_empty(),"async asset failure retained a scope")
	for preview in previews:
		preview.queue_free()
	await process_frame
	if _failed:
		return
	print("CHARACTER_PREVIEW_PASS six_parts=true shared_textures=true fixed_ACT_animation=true hidden_no_work=true missing_and_failed_assets_release=true")
	quit(0)
