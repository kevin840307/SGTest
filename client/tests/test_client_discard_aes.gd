extends SceneTree
const Session = preload("res://session/client_session.gd")
const WorldState = preload("res://world/world_state.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_DISCARD_AES_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()!=2 or not str(args[0]).is_valid_int():
		_fail("expected port/certificate")
		return
	var cert = null
	var session = Session.new()
	if session.connect_secure("127.0.0.1",int(args[0]),USERNAME,PASSWORD,"localhost",cert) != OK:
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
			and int(v.pending_world_ready_ack_epoch)==0
			and int(v.last_world_ready_ack_epoch)==int(v.map_epoch)
			and v.inventory_items.size()==1
		),7000)
	if ready.is_empty():
		_fail("inventory baseline "+str(session.snapshot()),session)
		return
	var uids: Array = ready.inventory_items.keys()
	var uid := int(uids[0])
	if uid <= 0 or int(ready.inventory_items[uid].item_definition_id)!=2701 or int(ready.inventory_items[uid].quantity)!=2:
		_fail("unexpected initial inventory "+str(ready.inventory_items),session)
		return

	if not session.discard_inventory_item(uid,1):
		_fail("first discard intent refused",session)
		return
	var first := _wait(session,func(v):
		return (
			int(v.last_discard_result_sequence)==1
			and int(v.last_discard_result_status)==0
			and int(v.pending_discard_sequence)==0
			and v.inventory_items.has(uid)
			and int(v.inventory_items[uid].quantity)==1
		),7000)
	if first.is_empty():
		_fail("first authoritative discard "+str(session.snapshot()),session)
		return
	if int(first.last_discard_result.item_uid)!=uid or int(first.last_discard_result.discarded_quantity)!=1 or int(first.last_discard_result.remaining_quantity)!=1:
		_fail("first discard receipt payload "+str(first.last_discard_result),session)
		return

	if not session.discard_inventory_item(uid,1):
		_fail("second discard intent refused",session)
		return
	var second := _wait(session,func(v):
		return (
			int(v.last_discard_result_sequence)==2
			and int(v.last_discard_result_status)==0
			and int(v.pending_discard_sequence)==0
			and v.inventory_items.is_empty()
		),7000)
	if second.is_empty():
		_fail("final authoritative discard "+str(session.snapshot()),session)
		return
	if int(second.last_discard_result.item_uid)!=uid or int(second.last_discard_result.discarded_quantity)!=1 or int(second.last_discard_result.remaining_quantity)!=0:
		_fail("final discard receipt payload "+str(second.last_discard_result),session)
		return
	if session.discard_inventory_item(uid,1):
		_fail("absent UID remained discardable",session)
		return

	session.stop()
	print("CLIENT_DISCARD_AES_PASS item_uid=%d first_remaining=1 final_remaining=0 local_update_after_receipt=true request_has_no_actor_or_item_definition=true" % uid)
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
