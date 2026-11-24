extends SceneTree
## Visible 800x600 isolation harness for UI-023_ITEM native smoke/A-B capture.
const ClassicWorld = preload("res://ui/classic_world_shell.gd")
var _shell
var _last_scroll := -1

func _initialize() -> void:
	root.size=Vector2i(800,600)
	DisplayServer.window_set_size(Vector2i(800,600))
	_run.call_deferred()

func _run() -> void:
	var shell=ClassicWorld.new()
	_shell=shell
	root.add_child(shell)
	shell.visible=true
	shell.stage.visible=true
	shell.hud.visible=true
	for child in shell.hud.get_children():
		if child!=shell.inventory:
			child.hide()
	shell.inventory.show()
	var items:Dictionary={}
	for uid in range(1,19):
		items[uid]={"item_definition_id":uid,"quantity":uid}
	shell._stats={"inventory_items":items,"equipment_items":{}}
	shell._change_inventory_tab(0)
	await process_frame
	await process_frame
	await RenderingServer.frame_post_draw
	var args:=OS.get_cmdline_user_args()
	if args.size()>0:
		var path:=str(args[0])
		var err:=root.get_texture().get_image().save_png(path)
		if err!=OK:
			push_error("CLASSIC_INVENTORY_HK_VISUAL_FAIL save=%d"%err)
			quit(1)
			return
		print("CLASSIC_INVENTORY_HK_VISUAL_CAPTURE "+path)
		quit(0)
		return
	print("CLASSIC_INVENTORY_HK_VISUAL_READY click scroll controls at local x=204..220 y=60..95/459..494")

func _process(_delta: float) -> bool:
	if _shell!=null and is_instance_valid(_shell) and _shell.inventory!=null:
		var scroll:=int(_shell.inventory.get_meta("item_scroll",0))
		if scroll!=_last_scroll:
			_last_scroll=scroll
			var first_text:=""
			var last_text:=""
			if _shell._inventory_rows.get_child_count()>0:
				first_text=str(_shell._inventory_rows.get_child(0).text)
				last_text=str(_shell._inventory_rows.get_child(_shell._inventory_rows.get_child_count()-1).text)
			print("CLASSIC_INVENTORY_HK_VISUAL_SCROLL offset=%d first=%s last=%s"%[scroll,first_text,last_text])
	return false
