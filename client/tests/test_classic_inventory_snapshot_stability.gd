extends SceneTree
## GP-025-01 / UI-023: repeated authoritative snapshots must not destroy
## unchanged interactive inventory rows every presentation tick.
const Shell = preload("res://ui/classic_world_shell.gd")
var failed := false

func _init() -> void:
	call_deferred("_run")

func _run() -> void:
	var shell := Shell.new()
	root.add_child(shell)
	shell.set_world_active(true)
	var snapshot := {
		"inventory_items": {7001: {"item_definition_id": 100, "quantity": 2}},
		"equipment_items": {},
		"skill_book": {28101: 2},
		"skill_book_ready": true
	}
	shell.apply_snapshot(snapshot)
	_expect(shell._inventory_rows.get_child_count() == 1, "initial item row")
	var original_row := shell._inventory_rows.get_child(0)
	var original_skill: String = str(shell.skill_window._name_labels[0].text)
	for i in 12:
		shell.apply_snapshot(snapshot.duplicate(true))
		_expect(shell._inventory_rows.get_child(0) == original_row,
			"identical snapshot must retain existing interactive row %d" % i)
	_expect(shell.skill_window._name_labels[0].text == original_skill,
		"unchanged authoritative SkillBook remains visible")
	var updated := snapshot.duplicate(true)
	updated["inventory_items"][7001]["quantity"] = 1
	shell.apply_snapshot(updated)
	_expect(shell._inventory_rows.get_child(0) != original_row,
		"authoritative inventory quantity update rebuilds row")
	_expect("×1" in str((shell._inventory_rows.get_child(0) as Button).text),
		"updated row displays real quantity")
	var after_update := shell._inventory_rows.get_child(0)
	shell.set_world_active(false)
	shell.set_world_active(true)
	shell.apply_snapshot(updated)
	_expect(shell._inventory_rows.get_child(0) != after_update,
		"new world session invalidates presentation cache")
	shell.free()
	if not failed:
		print("CLASSIC_INVENTORY_SNAPSHOT_STABILITY_PASS unchanged=12 changed=1 session_reset=1")
	quit(1 if failed else 0)

func _expect(ok: bool, message: String) -> void:
	if not ok:
		failed = true
		push_error("CLASSIC_INVENTORY_SNAPSHOT_STABILITY_FAIL " + message)
