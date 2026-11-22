extends SceneTree

const App = preload("res://app/main.gd")

func _initialize() -> void:
	run.call_deferred()

func run() -> void:
	var baseline := Node.get_orphan_node_ids().size()
	for cycle in range(4):
		var app := App.new()
		app.auto_start = false
		root.add_child(app)
		await process_frame
		app.queue_free()
		await process_frame
		await process_frame
		var remaining := Node.get_orphan_node_ids().size() - baseline
		if remaining != 0:
			push_error("UI cycle %d left %d orphan nodes" % [cycle + 1, remaining])
			quit(1)
			return
	print("UI_LIFETIME_PASS cycles=4 orphan_delta=0")
	quit(0)
