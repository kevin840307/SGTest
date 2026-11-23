extends SceneTree
const PetPanel = preload("res://pet/pet_panel.gd")
const State = preload("res://world/world_state.gd")

var calls:Array=[]

func _fail(reason:String)->void:
	push_error("PET_PANEL_FAIL "+reason)
	quit(1)

func _initialize()->void:
	_run.call_deferred()

func _run()->void:
	var panel=PetPanel.new()
	root.add_child(panel)
	panel.set_actions(
		func(uid:int)->bool: calls.append(["summon",uid]); return true,
		func()->bool: calls.append(["recall"]); return true,
		func()->bool: calls.append(["refresh"]); return true,
		func(pet_uid:int,item_uid:int)->bool: calls.append(["equip",pet_uid,item_uid]); return true,
		func(pet_uid:int,item_uid:int)->bool: calls.append(["unequip",pet_uid,item_uid]); return true,
		null
	)
	var snapshot={
		"state":State.STATE_IN_WORLD,"actor_id":42,"map_epoch":7,"world_ready":true,
		"player_dead":false,"reconnecting":false,"resync_pending":false,
		"waiting_for_new_map":false,"pending_world_ready_ack_epoch":0,
		"last_world_ready_ack_epoch":7,"pet_snapshot_received":true,
		"pet_equipment_snapshot_received":true,"pet_sequence":3,
		"pending_pet_sequence":0,"active_pet_uid":0,
		"pet_instances":{
			1001:{"pet_uid":1001,"definition_id":5001,"evolution_id":2,"level":4,"experience":88},
			1002:{"pet_uid":1002,"definition_id":5002,"evolution_id":7,"level":7,"experience":1234}
		},
		"pet_equipment_items":{
			9001:{"pet_uid":1001,"item_uid":9001,"slot_id":2,"item_definition_id":8101}
		},
		"inventory_revision":9,
		"inventory_items":{9100:{"item_uid":9100,"item_definition_id":8201,"quantity":1}},
		"last_pet_result":{}
	}
	panel.apply_snapshot(snapshot,true)
	if panel.pet_picker.item_count!=2 or panel.selected_pet_uid()!=1001:
		_fail("roster did not bind authoritative pets"); return
	if panel.equipment_picker.item_count!=1 or panel.inventory_picker.item_count!=1:
		_fail("equipment/inventory views missing"); return
	if panel.summon_button.disabled or not panel.recall_button.disabled or panel.equip_button.disabled:
		_fail("initial controls invalid"); return
	panel.summon_button.pressed.emit()
	if calls!=[["summon",1001]]:
		_fail("summon UID lost"); return

	snapshot=snapshot.duplicate(true)
	snapshot["pet_sequence"]=4
	snapshot["active_pet_uid"]=1001
	panel.apply_snapshot(snapshot,true)
	if not panel.summon_button.disabled or panel.recall_button.disabled or panel.equip_button.disabled:
		_fail("summoned pet controls invalid"); return

	panel.pet_picker.select(1)
	panel.pet_picker.item_selected.emit(1)
	if panel.selected_pet_uid()!=1002 or panel.equipment_picker.item_count!=0:
		_fail("selection did not switch pet"); return
	panel.equip_button.pressed.emit()
	if calls.back()!=["equip",1002,9100]:
		_fail("equip UIDs lost"); return

	snapshot=snapshot.duplicate(true)
	snapshot["pet_sequence"]=5
	snapshot["pet_equipment_items"]={
		9001:{"pet_uid":1001,"item_uid":9001,"slot_id":2,"item_definition_id":8101},
		9100:{"pet_uid":1002,"item_uid":9100,"slot_id":3,"item_definition_id":8201}
	}
	snapshot["inventory_revision"]=10
	snapshot["inventory_items"]={}
	snapshot["last_pet_result"]={"status":0,"action":4,"pet_uid":1002,"item_uid":9100}
	panel.apply_snapshot(snapshot,true)
	panel.pet_picker.select(1)
	panel.pet_picker.item_selected.emit(1)
	if panel.equipment_picker.item_count!=1 or panel.unequip_button.disabled:
		_fail("committed equipment did not enable unequip"); return
	if panel.equipment_picker.get_item_text(0).find("槽位 3")<0:
		_fail("unknown HK slot was not presented generically"); return
	panel.unequip_button.pressed.emit()
	if calls.back()!=["unequip",1002,9100]:
		_fail("unequip UIDs lost"); return

	panel.refresh_button.pressed.emit()
	if calls.back()!=["refresh"]:
		_fail("refresh not routed"); return
	panel.recall_button.pressed.emit()
	if calls.back()!=["recall"]:
		_fail("recall not routed"); return

	snapshot=snapshot.duplicate(true)
	snapshot["pending_pet_sequence"]=6
	panel.apply_snapshot(snapshot,true)
	if not panel.summon_button.disabled or not panel.recall_button.disabled or not panel.refresh_button.disabled:
		_fail("pending guard missing"); return

	panel.queue_free()
	await process_frame
	print("PET_PANEL_PASS roster=true summon_recall=true equipment_uid=true generic_slots=true server_status=true pending_guard=true")
	quit(0)
