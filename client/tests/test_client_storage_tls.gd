extends SceneTree
const Session = preload("res://network/client_session.gd")
const WorldState = preload("res://network/world_state.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, session = null) -> void:
	if session != null:
		session.stop()
	push_error("CLIENT_STORAGE_TLS_FAIL "+message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()!=2 or not str(args[0]).is_valid_int():
		_fail("expected port/certificate")
		return
	var cert := X509Certificate.new()
	if cert.load(str(args[1])) != OK:
		_fail("certificate load")
		return
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
	var initial: Dictionary = ready.inventory_items[uid]
	if uid <= 0 or int(initial.item_definition_id)!=2701 or int(initial.quantity)!=2 or int(initial.get("drop_id",0))<=0:
		_fail("unexpected initial inventory "+str(ready.inventory_items),session)
		return
	var source_drop := int(initial.drop_id)

	if not session.open_storage(88):
		_fail("open storage intent refused",session)
		return
	var opened := _wait(session,func(v):
		return (
			int(v.pending_storage_sequence)==0
			and bool(v.storage_ready)
			and int(v.active_storage_npc)==88
			and v.storage_items.is_empty()
		),7000)
	if opened.is_empty():
		_fail("storage baseline "+str(session.snapshot()),session)
		return

	if not session.deposit_storage_item(uid):
		_fail("deposit intent refused",session)
		return
	var deposited := _wait(session,func(v):
		return (
			int(v.pending_storage_sequence)==0
			and int(v.last_storage_response_sequence)==2
			and not v.inventory_items.has(uid)
			and v.storage_items.has(uid)
		),7000)
	if deposited.is_empty():
		_fail("deposit settlement "+str(session.snapshot()),session)
		return
	var stored: Dictionary = deposited.storage_items[uid]
	if int(stored.item_definition_id)!=2701 or int(stored.quantity)!=2 or int(stored.get("drop_id",0))!=source_drop or int(deposited.last_storage_result.status)!=0 or int(deposited.last_storage_result.kind)!=1:
		_fail("deposit receipt/provenance "+str(deposited.last_storage_result),session)
		return

	if not session.withdraw_storage_item(uid):
		_fail("withdraw intent refused",session)
		return
	var withdrawn := _wait(session,func(v):
		return (
			int(v.pending_storage_sequence)==0
			and int(v.last_storage_response_sequence)==3
			and v.inventory_items.has(uid)
			and not v.storage_items.has(uid)
		),7000)
	if withdrawn.is_empty():
		_fail("withdraw settlement "+str(session.snapshot()),session)
		return
	var restored: Dictionary = withdrawn.inventory_items[uid]
	if int(restored.item_definition_id)!=2701 or int(restored.quantity)!=2 or int(restored.get("drop_id",0))!=source_drop or int(withdrawn.last_storage_result.status)!=0 or int(withdrawn.last_storage_result.kind)!=2:
		_fail("withdraw receipt/provenance "+str(withdrawn.last_storage_result),session)
		return

	session.stop()
	print("CLIENT_STORAGE_TLS_PASS item_uid=%d quantity=2 source_drop=%d open=true deposit=true withdraw=true local_update_after_receipt=true request_has_no_actor_or_item_definition_or_quantity=true" % [uid,source_drop])
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
