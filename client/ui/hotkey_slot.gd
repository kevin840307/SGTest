extends Control

signal binding_dropped(slot_index:int,payload:Dictionary)
signal activated(slot_index:int)

var slot_index:int=-1
var hotkey_drag_payload:Dictionary={}
var drag_enabled:=true
var _click_pending:=false

func _gui_input(event:InputEvent)->void:
	if not event is InputEventMouseButton or event.button_index!=MOUSE_BUTTON_LEFT:
		return
	if event.pressed:
		_click_pending=not hotkey_drag_payload.is_empty()
		return
	var activate:=_click_pending and Rect2(Vector2.ZERO,size).has_point(event.position)
	_click_pending=false
	if activate and not get_viewport().gui_is_dragging():
		activated.emit(slot_index)
		accept_event()

func _get_drag_data(_at_position:Vector2):
	if (
		not drag_enabled or slot_index<0 or slot_index>=12 or
		hotkey_drag_payload.is_empty()
	):
		return null
	_click_pending=false
	var payload:=hotkey_drag_payload.duplicate(true)
	payload["source_slot"]=slot_index
	# Original CHotKeyItf::PickButton clears only its local slot state here;
	# SendRemoveHotKey(source) happens later in ReceiveDropButton after a valid
	# hotkey destination accepts the drop. Keep Server state unchanged on pickup
	# so a cancelled Godot drag cannot delete a durable binding.
	var preview:=TextureRect.new()
	preview.custom_minimum_size=Vector2(22,22)
	preview.size=Vector2(22,22)
	preview.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	preview.stretch_mode=TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	var icon:=get_node_or_null("Icon") as TextureRect
	if icon!=null:
		preview.texture=icon.texture
	# Direct headless handler checks are not a live GUI drag. The engine sets
	# this state during a real _get_drag_data callback, where the preview is legal.
	if get_viewport()!=null and get_viewport().gui_is_dragging():
		set_drag_preview(preview)
	else:
		preview.queue_free()
	return payload

func _can_drop_data(_at_position:Vector2,data)->bool:
	return (
		slot_index>=0 and slot_index<12 and data is Dictionary and
		str((data as Dictionary).get("hotkey_kind","")) in ["item","skill"] and
		int((data as Dictionary).get("data_id",0))>0
	)

func _drop_data(_at_position:Vector2,data)->void:
	if _can_drop_data(Vector2.ZERO,data):
		binding_dropped.emit(slot_index,(data as Dictionary).duplicate(true))
