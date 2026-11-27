extends SceneTree
const Shell = preload("res://ui/classic_world_shell.gd")
const ItemCatalog = preload("res://inventory/item_catalog.gd")

class FakePanel extends RefCounted:
	var item_catalog:=ItemCatalog.new()
	var mounted_uid:=0
	var dismounts:=0
	var charged_uid:=0
	var equipped_uid:=0
	var unequipped_uid:=0
	func submit_mecha_mount(uid:int)->void: mounted_uid=uid
	func submit_mecha_dismount()->void: dismounts+=1
	func submit_mecha_charge(_mecha_uid:int,item_uid:int)->void: charged_uid=item_uid
	func submit_mecha_equip(_mecha_uid:int,item_uid:int)->void: equipped_uid=item_uid
	func submit_mecha_unequip(_mecha_uid:int,item_uid:int)->void: unequipped_uid=item_uid
	func submit_mecha_refresh()->void: pass

func _fail(message:String)->void:
	push_error("CLASSIC_MECHA_WINDOW_FAIL "+message)
	quit(1)

func _initialize()->void:
	var shell=Shell.new()
	root.add_child(shell)
	var panel=FakePanel.new()
	if not panel.item_catalog.loaded:
		_fail("presentation item catalog missing"); return
	shell._panel=panel
	shell._stats={
		"mecha_instances":{
			11:{
				"definition_id":50001,"level":4,"experience":88,
				"battery_charge":20,"battery_capacity":120
			}
		},
		"mounted_mecha_uid":0,
		"mecha_equipment_items":{
			301:{"mecha_uid":11,"item_uid":301,"slot_id":1,"item_definition_id":50293},
			302:{"mecha_uid":11,"item_uid":302,"slot_id":2,"item_definition_id":50297},
			303:{"mecha_uid":11,"item_uid":303,"slot_id":3,"item_definition_id":7301},
			306:{"mecha_uid":11,"item_uid":306,"slot_id":6,"item_definition_id":7601},
		},
		"inventory_items":{
			401:{"item_definition_id":50101,"quantity":2},
			402:{"item_definition_id":50293,"quantity":1},
		}
	}
	shell._classic_mecha_uid=11
	shell._refresh_mecha_window()
	var window:Control=shell._mecha_window
	if window==null or window.size!=Vector2(523,393):
		_fail("056 Driver_INFO source geometry missing"); return
	if shell._mecha_drive_button.position!=Vector2(1,330) or shell._mecha_drive_button.size!=Vector2(113,23):
		_fail("B_DRIVER01 source rect mismatch"); return
	if shell._mecha_exit_button.position!=Vector2(1,360) or shell._mecha_exit_button.size!=Vector2(113,23):
		_fail("B_DRIVER02 source rect mismatch"); return
	if not shell._mecha_drive_button.visible or shell._mecha_exit_button.visible:
		_fail("unmounted button state incorrect"); return
	shell._submit_selected_mecha_mount()
	if panel.mounted_uid!=11:
		_fail("drive button did not route authoritative mount intent"); return
	shell._submit_selected_mecha_charge(401)
	if panel.charged_uid!=401:
		_fail("battery row did not route authoritative charge intent"); return
	shell._stats["mounted_mecha_uid"]=11
	shell._refresh_mecha_window()
	if shell._mecha_drive_button.visible or not shell._mecha_exit_button.visible:
		_fail("mounted button state incorrect"); return
	shell._submit_selected_mecha_dismount()
	if panel.dismounts!=1:
		_fail("exit button did not route authoritative dismount intent"); return
	var weapon:Button=shell._mecha_slot_buttons[1]
	if int(weapon.get_meta("item_uid",0))!=301:
		_fail("server weapon snapshot did not populate source slot"); return
	shell._on_mecha_slot_pressed(1)
	if panel.unequipped_uid!=301:
		_fail("source slot did not route authoritative unequip intent"); return
	print("CLASSIC_MECHA_WINDOW_PASS source_523x393=true drive_rect=true exit_rect=true server_snapshot=true mount=true dismount=true charge=true unequip=true")
	quit(0)
