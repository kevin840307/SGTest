extends SceneTree
## A newly registered account has no characters. The login UI must show
## creation controls even when the character list never changes from [].
const SessionPanel = preload("res://session/session_panel.gd")
const State = preload("res://world/world_state.gd")

class SessionStub:
	extends RefCounted
	var revision := 1
	var published: Dictionary
	var creates := 0
	var last_created_name := ""
	var last_creation: Dictionary = {}
	var chosen_id := 0

	func snapshot_since(last_revision: int) -> Dictionary:
		if last_revision == revision:
			return {}
		return {"revision": revision, "snapshot": published.duplicate(true)}

	func create_character(
			slot: int, name: String, _appearance: Dictionary = {},
			creation: Dictionary = {}) -> bool:
		if slot != 0 or name.is_empty() or creation.is_empty():
			return false
		creates += 1
		last_created_name = name
		last_creation = creation.duplicate(true)
		return true

	func choose_character(character_id: int, _character_password: String = "") -> bool:
		chosen_id = character_id
		return character_id > 0

	func stop() -> void:
		pass


func _initialize() -> void:
	_run.call_deferred()


func _fail(panel: Control, reason: String) -> void:
	panel.queue_free()
	push_error("CLIENT_EMPTY_CHARACTER_UI_FAIL " + reason)
	quit(1)


func _run() -> void:
	var panel = SessionPanel.new()
	root.add_child(panel)
	var source = State.new()
	source.state = State.STATE_SELECTING_CHARACTER
	var fake = SessionStub.new()
	fake.published = source.snapshot()
	if not (fake.published.get("characters", []) as Array).is_empty():
		_fail(panel, "baseline must have no characters")
		return
	panel.session = fake
	panel.active = true
	panel._process(0.06)
	if not panel.character_name_input.visible or not panel.character_slot_input.visible or not panel.create_character_button.visible or panel.create_character_button.disabled:
		_fail(panel, "empty character list must show enabled creation controls")
		return
	if panel.characters.visible or panel.choose_button.visible:
		_fail(panel, "empty character list must not expose a broken selector")
		return

	panel.character_name_input.text = "NewHero"
	panel.character_slot_input.value = 0
	panel.character_strength_input.value = 3
	panel.character_dexterity_input.value = 2
	panel.character_constitution_input.value = 2
	panel.character_wisdom_input.value = 1
	panel.character_password_input.text = "2468"
	panel.character_password_confirm_input.text = "2468"
	panel.create_character_button.pressed.emit()
	if fake.creates != 1 or fake.last_created_name != "NewHero" or 			int(fake.last_creation.get("strength",0)) != 3 or 			str(fake.last_creation.get("password","")) != "2468":
		_fail(panel, "creation button did not submit protected stat intent")
		return
	if not panel.create_character_button.disabled or panel.character_name_input.editable or panel.creation_options.editable:
		_fail(panel,"queued creation must lock controls before the network reply")
		return

	source.state = State.STATE_CREATING_CHARACTER
	fake.published = source.snapshot()
	fake.revision += 1
	panel._process(0.06)
	if not panel.character_name_input.visible or not panel.create_character_button.visible or not panel.create_character_button.disabled:
		_fail(panel, "in-flight create must retain visible, disabled controls")
		return

	source.state = State.STATE_SELECTING_CHARACTER
	fake.published = source.snapshot()
	fake.published["characters"] = [{"character_id": 101, "name": "NewHero", "slot": 0}]
	fake.published["last_created_character_id"] = 101
	fake.published["character_create_result_revision"] = 1
	fake.revision += 1
	panel._process(0.06)
	if not panel.characters.visible or not panel.choose_button.visible or panel.choose_button.disabled or panel.characters.item_count != 1:
		_fail(panel, "created character must expose selector and enter-world button")
		return
	panel.choose_button.pressed.emit()
	if fake.chosen_id != 101:
		_fail(panel, "enter world did not submit the selected character")
		return

	panel.disconnect_session()
	print("CLIENT_EMPTY_CHARACTER_UI_PASS empty_list_creation_visible=true create_intent=true pending_controls=true character_selector=true enter_world=true")
	panel.queue_free()
	quit(0)
