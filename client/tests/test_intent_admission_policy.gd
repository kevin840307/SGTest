extends SceneTree

const Policy = preload("res://session/intent_admission_policy.gd")
const ClientSession = preload("res://session/client_session.gd")

func _fail(reason: String) -> void:
	push_error("INTENT_ADMISSION_FAIL " + reason)
	quit(1)


func _allowed(kind: String, published: Dictionary = {}, queued_types: Array = []) -> bool:
	var queued: Array[Dictionary] = []
	for queued_type in queued_types:
		queued.append({"type": queued_type})
	return Policy.allows(kind, published, queued)


func _initialize() -> void:
	# Every previous command/pending blocker is enforced, without creating
	# another mutable queue or making Server authority decisions locally.
	var groups := [
		{"kinds": ["use_recovery_item", "discard_inventory_item",
			"shop_open", "shop_buy", "shop_sell"],
			"pending": Policy.SHOP_AND_ITEM_PENDING},
		{"kinds": ["storage_open", "storage_deposit", "storage_withdraw"],
			"pending": Policy.STORAGE_PENDING},
		{"kinds": ["equipment_equip", "equipment_unequip"],
			"pending": Policy.EQUIPMENT_PENDING},
		{"kinds": ["bank_open", "bank_deposit", "bank_withdraw",
			"repair_open", "repair_item"],
			"pending": Policy.BANK_REPAIR_PENDING},
		{"kinds": ["player_shop"], "pending": ["pending_player_shop_sequence", "trade_id"]},
		{"kinds": ["quest_open", "quest_choose"], "pending": ["pending_quest_sequence"]},
		{"kinds": ["pvp_mode"], "pending": ["pending_pvp_sequence"]},
		{"kinds": ["pvp_attack"], "pending": ["pending_pvp_attack_sequence"]},
		{"kinds": ["cast_skill"], "pending": ["pending_skill_sequence"]},
	]
	var pending_checks := 0
	for group in groups:
		for kind in group.kinds:
			if not _allowed(kind):
				_fail("unblocked %s was rejected" % kind)
				return
			for field in group.pending:
				if _allowed(kind, {field: 1}):
					_fail("%s accepted while %s is pending" % [kind, field])
					return
				pending_checks += 1

	var economy_kinds := [
		"shop_open", "shop_buy", "shop_sell",
		"storage_open", "storage_deposit", "storage_withdraw",
		"equipment_equip", "equipment_unequip",
		"bank_open", "bank_deposit", "bank_withdraw",
		"repair_open", "repair_item",
	]
	var conflict_checks := 0
	for kind in economy_kinds:
		for blocked in Policy.ECONOMY_QUEUED:
			if _allowed(kind, {}, [blocked]):
				_fail("economic %s allowed queued %s" % [kind, blocked])
				return
			conflict_checks += 1
	for kind in ["use_recovery_item", "discard_inventory_item"]:
		for blocked in Policy.ITEM_QUEUED:
			if _allowed(kind, {}, [blocked]):
				_fail("item %s allowed queued %s" % [kind, blocked])
				return
			conflict_checks += 1
	# Preserve the ORIGINAL asymmetry; making the matrix symmetric is a
	# behavior change and requires a separate reviewed design decision.
	for opening in ["shop_open", "storage_open", "bank_open"]:
		for item in ["use_recovery_item", "discard_inventory_item"]:
			if not _allowed(item, {}, [opening]) or _allowed(opening, {}, [item]):
				_fail("original NPC-open/item FIFO asymmetry changed: %s/%s" % [opening, item])
				return
	if not _allowed("shop_open", {"pending_recovery_sequence": 1}):
		_fail("original recovery pending asymmetry changed")
		return
	if not _allowed("bank_open", {"trade_id": 1}):
		_fail("bank-admission policy added an unowned trade restriction")
		return
	if _allowed("gm_pvp_name") or not _allowed("gm_pvp_name", {"gm_authorized": true}):
		_fail("GM authorization gate changed")
		return
	if _allowed("gm_pvp_name", {"gm_authorized": true, "pending_gm_sequence": 1}):
		_fail("pending GM operation accepted")
		return
	for kind in ["player_shop", "quest_choose", "pvp_mode", "gm_pvp_name",
			"pvp_attack", "cast_skill", "pickup_ground_drop", "stop_move"]:
		var published := {"gm_authorized": true}
		if _allowed(kind, published, [kind]):
			_fail("singleflight %s accepted duplicate" % kind)
			return
	if not _allowed("trade_action", {}, ["quest_open"]):
		_fail("unrelated commands lost permissive FIFO behavior")
		return

	# Exercise the actual single queue with its lock, navigation supersession
	# and token monotonicity. Only ClientSession may change _intents.
	var session = ClientSession.new()
	session._running = true
	session._published = {}
	if not session._queue_intent({"type": "move", "x": 1}):
		_fail("first navigation intent rejected")
		return
	if not session._queue_intent({"type": "quest_open", "npc_spawn_id": 9}):
		_fail("independent quest intent rejected")
		return
	if not session._queue_intent({"type": "attack_monster", "entity_id": 11}):
		_fail("second navigation intent rejected")
		return
	if session._intents.size() != 2 or str(session._intents[0].type) != "quest_open" or str(session._intents[1].type) != "attack_monster":
		_fail("navigation supersession reordered non-navigation FIFO entries")
		return
	if not session._queue_intent({"type": "stop_move"}):
		_fail("stop barrier rejected")
		return
	if session._intents.size() != 2 or str(session._intents[1].type) != "stop_move":
		_fail("stop barrier failed to replace obsolete navigation")
		return
	if not session._queue_intent({"type": "move", "x": 3}):
		_fail("follow-up move rejected after stop barrier")
		return
	if session._intents.size() != 3 or str(session._intents[1].type) != "stop_move" or str(session._intents[2].type) != "move":
		_fail("stop barrier did not preserve later move ordering")
		return
	if int(session._intents[2]._token) != 5:
		_fail("single FIFO token sequence reset during navigation supersession")
		return
	if not session._queue_intent({"type": "shop_open"}):
		_fail("unblocked NPC shop was rejected")
		return
	if not session._queue_intent({"type": "use_recovery_item"}):
		_fail("legacy shop-open/item asymmetry was lost in actual queue")
		return
	var previous_token: int = session._intent_token
	if session._queue_intent({"type": "shop_buy"}):
		_fail("conflicting economic operation was accepted")
		return
	if session._intent_token != previous_token or session._intents.size() != 5:
		_fail("rejected intent changed FIFO token or queue length")
		return
	var capacity = ClientSession.new()
	capacity._running = true
	capacity._published = {}
	for index in ClientSession.MAX_INTENTS:
		if not capacity._queue_intent({"type": "unrelated", "index": index}):
			_fail("single FIFO filled early at %d" % index)
			return
	if capacity._queue_intent({"type": "unrelated"}):
		_fail("single FIFO exceeded MAX_INTENTS")
		return
	print("INTENT_ADMISSION_PASS pending_checks=%d conflict_checks=%d asymmetric_item_npc=PASS gm=PASS singleflight=PASS navigation_supersession=PASS fifo_stop_barrier=PASS tokens=PASS bounded_queue=PASS" % [pending_checks, conflict_checks])
	quit(0)
