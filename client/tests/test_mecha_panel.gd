extends SceneTree
const MechaPanel = preload("res://mecha/mecha_panel.gd")
const State = preload("res://world/world_state.gd")

var calls: Array = []

func _fail(reason: String) -> void:
	push_error("MECHA_PANEL_FAIL " + reason)
	quit(1)

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	var panel = MechaPanel.new()
	root.add_child(panel)
	panel.set_actions(
		func(uid: int) -> bool: calls.append(["mount",uid]); return true,
		func() -> bool: calls.append(["dismount"]); return true,
		func() -> bool: calls.append(["refresh"]); return true,
		func(mecha_uid: int,item_uid: int) -> bool: calls.append(["equip",mecha_uid,item_uid]); return true,
		func(mecha_uid: int,item_uid: int) -> bool: calls.append(["unequip",mecha_uid,item_uid]); return true,
		func(mecha_uid: int,item_uid: int) -> bool: calls.append(["charge",mecha_uid,item_uid]); return true,
		null
	)
	var snapshot := {
		"state":State.STATE_IN_WORLD,
		"actor_id":42,
		"map_epoch":7,
		"world_ready":true,
		"player_dead":false,
		"reconnecting":false,
		"resync_pending":false,
		"waiting_for_new_map":false,
		"pending_world_ready_ack_epoch":0,
		"last_world_ready_ack_epoch":7,
		"mecha_snapshot_received":true,
		"mecha_equipment_snapshot_received":true,
		"mecha_sequence":3,
		"pending_mecha_sequence":0,
		"mounted_mecha_uid":0,
		"mecha_instances":{
			1001:{"mecha_uid":1001,"definition_id":7001,"level":4,"experience":88,"battery_charge":40,"battery_capacity":100},
			1002:{"mecha_uid":1002,"definition_id":7002,"level":7,"experience":1234,"battery_charge":0,"battery_capacity":0},
		},
		"mecha_equipment_items":{
			9001:{"mecha_uid":1001,"item_uid":9001,"slot_id":3,"item_definition_id":7301},
		},
		"inventory_revision":9,
		"inventory_items":{
			9100:{"item_uid":9100,"item_definition_id":7101,"quantity":1},
		},
		"last_mecha_result":{}
	}
	panel.apply_snapshot(snapshot,true)
	if panel.mecha_picker.item_count != 2 or panel.selected_mecha_uid() != 1001:
		_fail("roster did not bind authoritative mecha instances")
		return
	if panel.equipment_picker.item_count != 1 or panel.inventory_picker.item_count != 1:
		_fail("equipment/inventory UID views did not bind snapshots")
		return
	if panel.mount_button.disabled or not panel.dismount_button.disabled or panel.equip_button.disabled or panel.charge_button.disabled:
		_fail("initial unmounted controls are not actionable")
		return
	panel.mount_button.pressed.emit()
	if calls != [["mount",1001]]:
		_fail("mount action did not preserve selected MechaUid")
		return

	snapshot = snapshot.duplicate(true)
	snapshot["mecha_sequence"] = 4
	snapshot["mounted_mecha_uid"] = 1001
	panel.apply_snapshot(snapshot,true)
	if not panel.mount_button.disabled or panel.dismount_button.disabled or not panel.equip_button.disabled or not panel.unequip_button.disabled or panel.charge_button.disabled:
		_fail("mounted mecha equipment lock or charge availability mismatch")
		return
	panel.charge_button.pressed.emit()
	if calls.back() != ["charge",1001,9100]:
		_fail("charge action did not preserve mounted mecha/item UIDs")
		return

	panel.mecha_picker.select(1)
	panel.mecha_picker.item_selected.emit(1)
	if panel.selected_mecha_uid() != 1002 or panel.equipment_picker.item_count != 0 or panel.equip_button.disabled:
		_fail("selection did not switch to unmounted mecha")
		return
	panel.equip_button.pressed.emit()
	if calls.back() != ["equip",1002,9100]:
		_fail("equip action did not preserve selected mecha/item UIDs")
		return

	snapshot = snapshot.duplicate(true)
	snapshot["mecha_sequence"] = 5
	snapshot["mecha_equipment_items"] = {
		9001:{"mecha_uid":1001,"item_uid":9001,"slot_id":3,"item_definition_id":7301},
		9100:{"mecha_uid":1002,"item_uid":9100,"slot_id":6,"item_definition_id":7601},
	}
	snapshot["inventory_revision"] = 10
	snapshot["inventory_items"] = {}
	snapshot["last_mecha_result"] = {
		"status":0,"action":4,"mecha_uid":1002,"item_uid":9100
	}
	panel.apply_snapshot(snapshot,true)
	panel.mecha_picker.select(1)
	panel.mecha_picker.item_selected.emit(1)
	if panel.equipment_picker.item_count != 1 or panel.unequip_button.disabled:
		_fail("committed equipment snapshot did not enable exact UID unequip")
		return
	if panel.status_label.text.find("已套用") < 0:
		_fail("server result status was not presented")
		return
	panel.unequip_button.pressed.emit()
	if calls.back() != ["unequip",1002,9100]:
		_fail("unequip action did not preserve exact committed UID")
		return

	panel.refresh_button.pressed.emit()
	if calls.back() != ["refresh"]:
		_fail("refresh action was not routed")
		return
	panel.dismount_button.pressed.emit()
	if calls.back() != ["dismount"]:
		_fail("dismount action was not routed")
		return

	snapshot = snapshot.duplicate(true)
	snapshot["pending_mecha_sequence"] = 6
	panel.apply_snapshot(snapshot,true)
	if not panel.mount_button.disabled or not panel.dismount_button.disabled or not panel.refresh_button.disabled:
		_fail("pending reliable mecha command did not lock duplicate submissions")
		return

	panel.queue_free()
	await process_frame
	print("MECHA_PANEL_PASS roster=true mount_dismount=true equipment_uid=true battery=true server_status=true pending_guard=true")
	quit(0)
