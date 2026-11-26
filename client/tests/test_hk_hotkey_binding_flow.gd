extends SceneTree

const Shell = preload("res://ui/classic_world_shell.gd")
var failed := false

class FakePanel:
	extends RefCounted
	var removed:Array[int]=[]
	var bound:Array[Array]=[]
	var bind_allowed:=true

	func submit_hotkey_remove(slot_index:int)->bool:
		removed.append(slot_index)
		bind_allowed=false
		return true

	func submit_hotkey_bind(slot_index:int,data_type:int,data_id:int)->bool:
		if not bind_allowed:
			return false
		bound.append([slot_index,data_type,data_id])
		return true


func _init()->void:
	call_deferred("_run")


func _run()->void:
	var shell:=Shell.new()
	root.add_child(shell)
	var panel:=FakePanel.new()
	shell._panel=panel
	shell._stats={
		"hotkey_sequence":0,
		"pending_hotkey_sequence":0,
		"last_hotkey_status":1,
		"hotkeys":{0:{"data_type":1,"data_id":99001}},
		"inventory_items":{}
	}
	shell._refresh_hotkeys()
	var slot0=shell._hotkey_slots[0]
	_expect(slot0.hotkey_drag_payload=={"hotkey_kind":"skill","data_id":99001},
		"authoritative binding becomes movable drag payload")
	_expect(slot0.drag_enabled,"idle authoritative slot is draggable")

	# Original CHotKeyItf::PickButton only clears the local UI slot while the
	# button is being carried. Durable Server state must survive a cancelled drag.
	var drag_payload=slot0._get_drag_data(Vector2.ZERO)
	_expect(drag_payload is Dictionary and int(drag_payload.get("source_slot",-1))==0,
		"slot drag carries its original source position")
	_expect(panel.removed.is_empty(),"picking/cancelling a bound slot does not remove durable binding")

	# ReceiveDropButton sends source Remove only after a valid hotkey destination
	# accepts the drop. Destination Save waits for that authoritative ACK.
	var slot6=shell._hotkey_slots[6]
	slot6._drop_data(Vector2.ZERO,drag_payload)
	_expect(panel.removed==[0],"valid slot move submits authoritative source remove")
	_expect(panel.bound.is_empty(),"move does not race destination bind ahead of remove ACK")
	_expect(shell._pending_hotkey_rebind=={
		"slot_index":6,"data_type":1,"data_id":99001,"after_sequence":0
	},"accepted move preserves destination intent until remove ACK")
	# A stale snapshot still carries the previous failure status; it must not
	# resolve this move before the source Remove sequence advances.
	shell._try_submit_pending_hotkey_rebind()
	_expect(not shell._pending_hotkey_rebind.is_empty(),"stale status cannot discard a new move")
	# Simulate the authoritative Applied snapshot after source removal.
	panel.bind_allowed=true
	shell._stats["hotkey_sequence"]=1
	shell._stats["pending_hotkey_sequence"]=0
	shell._stats["last_hotkey_status"]=0
	shell._try_submit_pending_hotkey_rebind()
	_expect(panel.bound==[[6,1,99001]],"move binds destination after authoritative remove ACK")
	_expect(shell._pending_hotkey_rebind.is_empty(),"move queue drains exactly once")

	# A rejected source remove must never leave the same binding in two slots.
	shell._pending_hotkey_rebind={
		"slot_index":7,"data_type":1,"data_id":99001,"after_sequence":1
	}
	shell._stats["hotkey_sequence"]=2
	shell._stats["last_hotkey_status"]=1
	shell._try_submit_pending_hotkey_rebind()
	_expect(panel.bound==[[6,1,99001]],"rejected remove cannot create duplicate destination binding")
	_expect(shell._pending_hotkey_rebind.is_empty(),"rejected remove discards queued destination")
	shell._stats["last_hotkey_status"]=0

	# Inventory/skill-window drops have no source slot and remain direct saves,
	# including replacement of an existing destination slot.
	var slot2=shell._hotkey_slots[2]
	slot2._drop_data(Vector2.ZERO,{"hotkey_kind":"item","data_id":1234})
	_expect(panel.bound==[[6,1,99001],[2,0,1234]],"external drag directly saves/overwrites destination")

	shell._stats["pending_hotkey_sequence"]=9
	shell._refresh_hotkeys()
	_expect(not slot0.drag_enabled,"pending authority disables a second source pick")

	shell.free()
	if not failed:
		print("HK_HOTKEY_BINDING_FLOW_PASS pickup_cancel_safe=true remove_then_move=true overwrite=true authority_gate=true")
	quit(1 if failed else 0)


func _expect(condition:bool,label:String)->void:
	if not condition:
		failed=true
		push_error("HK_HOTKEY_BINDING_FLOW_FAIL "+label)
