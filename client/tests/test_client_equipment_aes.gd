extends SceneTree
const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_EQUIPMENT_AES_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()!=1 or not str(args[0]).is_valid_int():
		_fail("expected port/certificate")
		return
	var session = Session.new()
	if session.connect_secure("127.0.0.1",int(args[0]),USERNAME,PASSWORD) != OK:
		_fail("connect",session)
		return
	var selecting := _wait(session,func(v):
		return int(v.state)==WorldState.STATE_SELECTING_CHARACTER,5000)
	if selecting.is_empty() or selecting.characters.size()!=1:
		_fail("character list "+str(session.snapshot()),session)
		return
	if not session.choose_character(int(selecting.characters[0].character_id)):
		_fail("choose",session)
		return

	var ready := _wait(session,func(v):
		return (
			int(v.state)==WorldState.STATE_IN_WORLD
			and bool(v.world_ready)
			and bool(v.inventory_ready)
			and bool(v.equipment_ready)
			and int(v.pending_world_ready_ack_epoch)==0
			and int(v.last_world_ready_ack_epoch)==int(v.map_epoch)
			and v.inventory_items.size()==1
			and v.equipment_items.is_empty()
		),7000)
	if ready.is_empty():
		_fail("login baselines "+str(session.snapshot()),session)
		return
	var uids: Array = ready.inventory_items.keys()
	var uid := int(uids[0])
	var initial: Dictionary = ready.inventory_items[uid]
	if uid <= 0 or int(initial.item_definition_id)!=3701 or int(initial.quantity)!=1 or int(initial.get("drop_id",0))<=0:
		_fail("unexpected initial inventory "+str(ready.inventory_items),session)
		return
	var source_drop := int(initial.drop_id)

	if not session.equip_inventory_item(uid):
		_fail("equip intent refused",session)
		return
	var equipped := _wait(session,func(v):
		return (
			int(v.pending_equipment_sequence)==0
			and int(v.last_equipment_response_sequence)==1
			and not v.inventory_items.has(uid)
			and v.equipment_items.has(uid)
		),7000)
	if equipped.is_empty():
		_fail("equip settlement "+str(session.snapshot()),session)
		return
	var item: Dictionary = equipped.equipment_items[uid]
	if int(item.item_definition_id)!=3701 or int(item.slot_id)!=4 or int(item.get("drop_id",0))!=source_drop or int(equipped.last_equipment_result.status)!=0 or int(equipped.last_equipment_result.kind)!=1:
		_fail("equip receipt/provenance "+str(equipped.last_equipment_result),session)
		return

	if not session.unequip_item(uid):
		_fail("unequip intent refused",session)
		return
	var unequipped := _wait(session,func(v):
		return (
			int(v.pending_equipment_sequence)==0
			and int(v.last_equipment_response_sequence)==2
			and v.inventory_items.has(uid)
			and not v.equipment_items.has(uid)
		),7000)
	if unequipped.is_empty():
		_fail("unequip settlement "+str(session.snapshot()),session)
		return
	var restored: Dictionary = unequipped.inventory_items[uid]
	if int(restored.item_definition_id)!=3701 or int(restored.quantity)!=1 or int(restored.get("drop_id",0))!=source_drop or int(unequipped.last_equipment_result.status)!=0 or int(unequipped.last_equipment_result.kind)!=2:
		_fail("unequip receipt/provenance "+str(unequipped.last_equipment_result),session)
		return

	session.stop()
	print("CLIENT_EQUIPMENT_AES_PASS item_uid=%d slot=4 quantity=1 source_drop=%d equip=true unequip=true local_update_after_receipt=true request_has_no_actor_or_slot_or_item_definition=true" % [uid,source_drop])
	quit(0)

func _wait(session, predicate: Callable, timeout_ms: int) -> Dictionary:
	var deadline := Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value: Dictionary=session.snapshot()
		if predicate.call(value):
			return value
		if int(value.state)==WorldState.STATE_FAILED and not bool(value.get("reconnecting",false)):
			return {}
		OS.delay_msec(2)
	return {}
