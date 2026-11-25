extends Control

signal binding_dropped(slot_index:int,payload:Dictionary)

var slot_index:int=-1

func _can_drop_data(_at_position:Vector2,data)->bool:
	return (
		slot_index>=0 and slot_index<12 and data is Dictionary and
		str((data as Dictionary).get("hotkey_kind","")) in ["item","skill"] and
		int((data as Dictionary).get("data_id",0))>0
	)

func _drop_data(_at_position:Vector2,data)->void:
	if _can_drop_data(Vector2.ZERO,data):
		binding_dropped.emit(slot_index,(data as Dictionary).duplicate(true))
