extends SceneTree
## Native QA of the regular main scene against a separately started real TLS Host.
## Sends GUI mouse/key input; never substitutes Session/WorldState with a stub.
const MainScene = preload("res://app/main.tscn")
var app: Node
var output := ""
var failures: Array = []
var screens: Array = []

func _initialize() -> void:
	_run.call_deferred()

func check(ok: bool, reason: String) -> void:
	if not ok:
		failures.append(reason)
		push_error("NATIVE_UI_WALKTHROUGH_FAIL "+reason)

func click(control: Control) -> void:
	check(control.is_visible_in_tree(),"target hidden: "+str(control.name))
	var point := control.get_global_rect().get_center()
	var motion := InputEventMouseMotion.new()
	motion.position = point
	Input.parse_input_event(motion)
	await process_frame
	for pressed in [true,false]:
		var event := InputEventMouseButton.new()
		event.button_index = MOUSE_BUTTON_LEFT
		event.position = point
		event.pressed = pressed
		Input.parse_input_event(event)
		await process_frame

func type_into(control: Control, value: String) -> void:
	await click(control)
	check(control.has_focus(),"text field did not receive click focus")
	for character in value:
		var event := InputEventKey.new()
		event.pressed = true
		event.unicode = character.unicode_at(0)
		Input.parse_input_event(event)
	await process_frame

func capture(label: String) -> void:
	await create_timer(0.4).timeout
	await RenderingServer.frame_post_draw
	var path := output.path_join(label+".png")
	check(root.get_texture().get_image().save_png(path)==OK,"save "+label)
	screens.append(label)
	print("NATIVE_UI_WALKTHROUGH_SCREEN "+label)

func wait_until(predicate: Callable, label: String) -> bool:
	var deadline := Time.get_ticks_msec()+20000
	while not predicate.call() and Time.get_ticks_msec()<deadline:
		await process_frame
	var ok: bool = predicate.call()
	check(ok,"timeout: "+label+" status="+str(app.session_panel.status.text))
	return ok

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	for i in range(args.size()-1):
		if args[i]=="--walk-dir":
			output = args[i+1]
	if output.is_empty() or DisplayServer.get_name()=="headless":
		quit(2)
		return
	DirAccess.make_dir_recursive_absolute(output)
	root.size = Vector2i(1152,620)
	app = MainScene.instantiate()
	root.add_child(app)
	app._select_recovered2_skin("recovered2_local")
	var panel = app.session_panel
	var shell = app.recovered2_auth
	check(app._recovered2_local_ready,"Recovered2 private artwork unavailable")
	await capture("01_login")
	var username := "walk"+str(Time.get_unix_time_from_system()).replace(".","")
	await type_into(shell._account,username)
	await type_into(shell._secret,"Walkthrough-Only-123!")
	check(shell._account.text==username,"account typing mismatch")
	check(shell._secret.text=="Walkthrough-Only-123!","secret typing mismatch")
	await click(shell._register_button)
	await capture("01_registration_pending")
	if await wait_until(func(): return panel.active and (shell._selection.visible or shell._creation.visible),"TLS registration/Auth"):
		await capture("02_initial_character_page")
		if shell._selection.visible:
			await click(shell._slot_actions[0])
		await capture("03_creation")
		await type_into(shell._name_edit,"WalkHero")
		await click(shell._confirm_create)
		if await wait_until(func(): return panel._characters.size()==1 and shell._selection.visible,"durable character creation"):
			await capture("04_character_selection")
			await click(shell._slot_actions[0])
			if await wait_until(func(): return bool(app._last_snapshot.get("world_ready",false)) and app.world_view.phase=="ready","real WorldReady and map"):
				await capture("05_world")
				var tabs: TabContainer = panel._layout.world_tabs
				for index in tabs.get_tab_count():
					var bar := tabs.get_tab_bar()
					if index==0:
						var point := bar.get_global_transform()*bar.get_tab_rect(index).get_center()
						for pressed in [true,false]:
							var event := InputEventMouseButton.new()
							event.position = point
							event.button_index = MOUSE_BUTTON_LEFT
							event.pressed = pressed
							Input.parse_input_event(event)
							await process_frame
					else:
						# TabBar scrolls overflow titles when navigated by keyboard.
						# Never click a tab rect outside the visible input bounds.
						bar.grab_focus()
						var event := InputEventKey.new()
						event.pressed = true
						event.keycode = KEY_RIGHT
						Input.parse_input_event(event)
						await process_frame
					check(tabs.current_tab==index,"gameplay tab click failed")
					await capture("06_world_group_"+str(index))
	var store = app.world_view.store
	panel.disconnect_session()
	app.queue_free()
	await process_frame
	var deadline := Time.get_ticks_msec()+10000
	while store.statistics().decode_jobs+store.statistics().texture_jobs>0 and Time.get_ticks_msec()<deadline:
		store.pump(2)
		await process_frame
	var final: Dictionary = store.statistics()
	check(final.assets+final.scopes+final.rgba_bytes+final.decode_jobs+final.texture_jobs==0,"resources not drained")
	var result := {"status":"PASS" if failures.is_empty() and screens.size()==10 else "FAIL","screens":screens,"failures":failures,"final":final,
		"scope":"real_TLS_native_current_UI_not_Recovered2_full_parity","missing":["guild window","dedicated character status window","Recovered2 world HUD and floating windows"]}
	var file := FileAccess.open(output.path_join("result.json"),FileAccess.WRITE)
	file.store_string(JSON.stringify(result,"  "))
	file.close()
	print("NATIVE_UI_WALKTHROUGH_"+str(result.status))
	quit(0 if result.status=="PASS" else 1)
