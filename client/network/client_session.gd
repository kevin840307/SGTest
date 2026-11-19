extends RefCounted

const Transport = preload("res://network/client_transport.gd")
const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://network/world_state.gd")

const MAX_INTENTS := 128
const LOGIC_SLEEP_MS := 2
const MAX_RECONNECT_ATTEMPTS := 3
const RECONNECT_BACKOFF_MS := [100,250,500]
const NORMAL_ATTACK_OUT_OF_RANGE := 4
const MOVE_RECEIPT_ACCEPTED := 0
const MAX_AUTO_APPROACH_CYCLES := 4
const AUTO_TARGET_MISSING_GRACE_MS := 500

var _transport: RefCounted
var _world: RefCounted = WorldState.new()
var _thread := Thread.new()
var _mutex := Mutex.new()
var _stop_requested := false
var _running := false
var _intents: Array[Dictionary] = []
var _published: Dictionary = _world.snapshot()
var _host := ""
var _port := 0
var _common_name := ""
var _trusted_chain = null
var _username := ""
var _credential_bytes := PackedByteArray()
var _selected_character_id := 0
var _has_entered_world := false
var _reconnecting := false
var _reconnect_attempt := 0
var _reconnect_count := 0
var _last_transport_error := OK
var _intent_token := 0

func _init(transport_override: RefCounted = null) -> void:
	_transport = transport_override if transport_override != null else Transport.new()

func connect_secure(host: String, port: int, username: String, password: String, common_name: String = "", trusted_chain = null) -> Error:
	if username.is_empty() or password.is_empty():
		return ERR_INVALID_PARAMETER
	_mutex.lock()
	if _running:
		_mutex.unlock()
		return ERR_ALREADY_IN_USE
	_stop_requested = false
	_intents.clear()
	_world = WorldState.new()
	_host = host
	_port = port
	_common_name = common_name
	_trusted_chain = trusted_chain
	_username = username
	_clear_credentials_locked()
	_credential_bytes = password.to_utf8_buffer()
	_selected_character_id = 0
	_has_entered_world = false
	_reconnecting = false
	_reconnect_attempt = 0
	_reconnect_count = 0
	_last_transport_error = OK
	_intent_token = 0
	_published = _decorate_snapshot(_world.snapshot())
	_running = true
	_mutex.unlock()
	var error: int = _transport.start_secure(host, port, common_name, trusted_chain)
	if error != OK:
		_mutex.lock()
		_running = false
		_clear_credentials_locked()
		_username = ""
		_mutex.unlock()
		return error
	_thread = Thread.new()
	error = _thread.start(_logic_loop)
	if error != OK:
		_transport.stop()
		_mutex.lock()
		_running = false
		_clear_credentials_locked()
		_mutex.unlock()
	return error

func create_character(slot: int, character_name: String) -> bool:
	var current := snapshot()
	var trimmed := character_name.strip_edges()
	var bytes := trimmed.to_utf8_buffer()
	if int(current.get("state",-1)) != WorldState.STATE_SELECTING_CHARACTER or 		slot < 0 or slot > 31 or bytes.size() < 1 or bytes.size() > 96:
		return false
	return _queue_intent({
		"type": "create_character",
		"slot": slot,
		"name": trimmed,
	})

func choose_character(character_id: int) -> bool:
	if character_id <= 0:
		return false
	return _queue_intent({"type": "choose", "character_id": character_id})

func move_target(target_x: int, target_y: int) -> bool:
	if bool(snapshot().get("player_dead",false)):
		return false
	return _queue_intent({"type": "move", "x": target_x, "y": target_y})

func stop_movement() -> bool:
	var current := snapshot()
	if bool(current.get("player_dead",false)) or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD:
		return false
	return _queue_intent({"type":"stop_move"})

func use_recovery_item(item_uid: int) -> bool:
	var current := snapshot()
	if item_uid <= 0 or not bool(current.get("inventory_ready",false)) or not bool(current.get("combat_available",false)) or bool(current.get("player_dead",false)) or int(current.get("pending_recovery_sequence",0)) != 0 or int(current.get("pending_discard_sequence",0)) != 0 or not (current.get("inventory_items",{}) as Dictionary).has(item_uid):
		return false
	return _queue_intent({"type":"use_recovery_item","item_uid":item_uid})


func discard_inventory_item(item_uid: int, quantity: int = 1) -> bool:
	var current := snapshot()
	var items: Dictionary = current.get("inventory_items",{})
	if item_uid <= 0 or quantity < 1 or quantity > 1000000 or not bool(current.get("inventory_ready",false)) or int(current.get("pending_discard_sequence",0)) != 0 or int(current.get("pending_recovery_sequence",0)) != 0 or not items.has(item_uid) or int((items[item_uid] as Dictionary).quantity) < quantity:
		return false
	return _queue_intent({"type":"discard_inventory_item","item_uid":item_uid,"quantity":quantity})


func pickup_ground_drop(drop_id: int) -> bool:
	var current := snapshot()
	if drop_id <= 0 or bool(current.get("player_dead",false)) or int(current.get("pending_pickup_sequence",0)) != 0 or not (current.get("ground_drops",{}) as Dictionary).has(drop_id):
		return false
	return _queue_intent({"type":"pickup_ground_drop","drop_id":drop_id})




func open_shop(npc_spawn_id: int) -> bool:
	var current := snapshot()
	if npc_spawn_id <= 0 or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or int(current.get("pending_shop_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"shop_open","npc_spawn_id":npc_spawn_id,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func buy_shop_item(item_definition_id: int, quantity: int = 1) -> bool:
	var current := snapshot()
	var npc := int(current.get("active_shop_npc",0))
	var shops: Dictionary = current.get("shop_items",{})
	if npc <= 0 or item_definition_id <= 0 or quantity < 1 or quantity > 1000000 or not shops.has(item_definition_id) or int(current.get("pending_shop_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"shop_buy","npc_spawn_id":npc,
		"item_definition_id":item_definition_id,"quantity":quantity,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func sell_shop_item(item_uid: int, quantity: int = 1) -> bool:
	var current := snapshot()
	var npc := int(current.get("active_shop_npc",0))
	var items: Dictionary = current.get("inventory_items",{})
	var shop_items: Dictionary = current.get("shop_items",{})
	if npc <= 0 or item_uid <= 0 or quantity < 1 or quantity > 1000000 or not items.has(item_uid):
		return false
	var item: Dictionary = items[item_uid]
	var item_id := int(item.get("item_definition_id",0))
	if not shop_items.has(item_id) or int((shop_items[item_id] as Dictionary).get("sell_price",0)) <= 0 or int(item.get("quantity",0)) < quantity or int(current.get("pending_shop_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"shop_sell","npc_spawn_id":npc,
		"item_uid":item_uid,"item_definition_id":item_id,"quantity":quantity,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func open_storage(npc_spawn_id: int) -> bool:
	var current := snapshot()
	if npc_spawn_id <= 0 or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_shop_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"storage_open","npc_spawn_id":npc_spawn_id,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func deposit_storage_item(item_uid: int) -> bool:
	var current := snapshot()
	var npc := int(current.get("active_storage_npc",0))
	var items: Dictionary = current.get("inventory_items",{})
	if npc <= 0 or item_uid <= 0 or not bool(current.get("storage_ready",false)) or not items.has(item_uid) or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_shop_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"storage_deposit","npc_spawn_id":npc,
		"item_uid":item_uid,"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))})


func withdraw_storage_item(item_uid: int) -> bool:
	var current := snapshot()
	var npc := int(current.get("active_storage_npc",0))
	var items: Dictionary = current.get("storage_items",{})
	if npc <= 0 or item_uid <= 0 or not bool(current.get("storage_ready",false)) or not items.has(item_uid) or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_shop_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"storage_withdraw","npc_spawn_id":npc,
		"item_uid":item_uid,"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))})


func open_bank(npc_spawn_id: int) -> bool:
	var current := snapshot()
	if npc_spawn_id <= 0 or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"bank_open","npc_spawn_id":npc_spawn_id,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func deposit_bank_money(amount: int) -> bool:
	var current := snapshot()
	var npc := int(current.get("active_bank_npc",0))
	if npc <= 0 or amount <= 0 or amount > WireCodec.MAX_MONEY or int(current.get("bank_scope",0)) not in [1,2] or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"bank_deposit","npc_spawn_id":npc,"amount":amount,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func withdraw_bank_money(amount: int) -> bool:
	var current := snapshot()
	var npc := int(current.get("active_bank_npc",0))
	if npc <= 0 or amount <= 0 or amount > WireCodec.MAX_MONEY or int(current.get("bank_scope",0)) not in [1,2] or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"bank_withdraw","npc_spawn_id":npc,"amount":amount,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func request_trade(target_actor_id: int) -> bool:
	var current := snapshot()
	if target_actor_id <= 0 or target_actor_id == int(current.get("actor_id",0)) or int(current.get("trade_id",0)) != 0:
		return false
	return _queue_trade_intent(1,0,target_actor_id,0,0,current)


func accept_trade() -> bool:
	return _queue_current_trade_action(2)


func reject_trade() -> bool:
	return _queue_current_trade_action(3)


func trade_add_item(item_uid: int) -> bool:
	var current := snapshot()
	var inventory: Dictionary = current.get("inventory_items",{})
	if item_uid <= 0 or not inventory.has(item_uid):
		return false
	return _queue_trade_intent(4,int(current.get("trade_id",0)),0,item_uid,0,current)


func trade_remove_item(item_uid: int) -> bool:
	var current := snapshot()
	var offered: Dictionary = current.get("trade_my_items",{})
	if item_uid <= 0 or not offered.has(item_uid):
		return false
	return _queue_trade_intent(5,int(current.get("trade_id",0)),0,item_uid,0,current)


func trade_set_money(money: int) -> bool:
	var current := snapshot()
	if money < 0 or money > WireCodec.MAX_MONEY:
		return false
	return _queue_trade_intent(6,int(current.get("trade_id",0)),0,0,money,current)


func trade_lock() -> bool:
	return _queue_current_trade_action(7)


func trade_final_confirm() -> bool:
	return _queue_current_trade_action(8)


func cancel_trade() -> bool:
	return _queue_current_trade_action(9)


func _queue_current_trade_action(action: int) -> bool:
	var current := snapshot()
	return _queue_trade_intent(action,int(current.get("trade_id",0)),0,0,0,current)


func _queue_trade_intent(action: int, trade_id: int,target_actor_id: int,
		item_uid: int,money: int,current: Dictionary) -> bool:
	if int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)):
		return false
	return _queue_intent({"type":"trade","action":action,
		"trade_id":trade_id,"target_actor_id":target_actor_id,
		"item_uid":item_uid,"money":money,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))})


func open_repair(npc_spawn_id: int) -> bool:
	var current := snapshot()
	if npc_spawn_id <= 0 or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_repair_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"repair_open","npc_spawn_id":npc_spawn_id,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func repair_item(item_uid: int) -> bool:
	var current := snapshot()
	var npc := int(current.get("active_repair_npc",0))
	var items: Dictionary = current.get("repair_items",{})
	if npc <= 0 or item_uid <= 0 or not bool(current.get("repair_ready",false)) or not items.has(item_uid) or int(current.get("pending_repair_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"repair_item","npc_spawn_id":npc,"item_uid":item_uid,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func equip_inventory_item(item_uid: int) -> bool:
	var current := snapshot()
	var inventory: Dictionary = current.get("inventory_items",{})
	if item_uid <= 0 or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or not bool(current.get("equipment_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_equipment_sequence",0)) != 0 or not inventory.has(item_uid):
		return false
	return _queue_intent({"type":"equipment_equip","item_uid":item_uid,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func unequip_item(item_uid: int) -> bool:
	var current := snapshot()
	var equipment: Dictionary = current.get("equipment_items",{})
	if item_uid <= 0 or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or not bool(current.get("equipment_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_equipment_sequence",0)) != 0 or not equipment.has(item_uid):
		return false
	return _queue_intent({"type":"equipment_unequip","item_uid":item_uid,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})

func respawn() -> bool:
	var current := snapshot()
	if not bool(current.get("combat_available",false)) or not bool(current.get("player_dead",false)):
		return false
	return _queue_intent({"type":"respawn"})

func cast_skill(skill_id: int, entity_id: int) -> bool:
	if entity_id <= 0:
		return false
	return _queue_skill(skill_id,entity_id)


func cast_area_skill(skill_id: int) -> bool:
	return _queue_skill(skill_id,0)


func _queue_skill(skill_id: int, target: int) -> bool:
	var current := snapshot()
	if skill_id <= 0 or skill_id > 100000000 or target < 0 or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or not bool(current.get("combat_available",false)) or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)) or int(current.get("pending_skill_sequence",0)) != 0:
		return false
	# Bind queued skill to the authenticated actor and exact map generation.
	# Never execute a pre-transfer skill intent on the next map.
	return _queue_intent({"type":"cast_skill","skill_id":skill_id,"entity_id":target,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})

func attack_monster(entity_id: int) -> bool:
	if entity_id <= 0 or bool(snapshot().get("player_dead",false)):
		return false
	return _queue_intent({"type":"attack_monster","entity_id":entity_id})

func npc_interact(spawn_id: int) -> bool:
	# The client names only a map-local NPC; Server provides the destination.
	if spawn_id <= 0 or spawn_id > 0x7fffffff or bool(snapshot().get("player_dead",false)):
		return false
	return _queue_intent({"type": "npc", "spawn_id": spawn_id})

func snapshot() -> Dictionary:
	_mutex.lock()
	var result := _published.duplicate(true)
	_mutex.unlock()
	return result

func stop() -> void:
	_mutex.lock()
	_stop_requested = true
	_mutex.unlock()
	if _thread.is_started():
		_thread.wait_to_finish()
	_transport.stop()
	_mutex.lock()
	_running = false
	_reconnecting = false
	_intents.clear()
	_clear_credentials_locked()
	_username = ""
	_selected_character_id = 0
	_mutex.unlock()

func _queue_intent(intent: Dictionary) -> bool:
	_mutex.lock()
	var accepted := _running and not _stop_requested and not _reconnecting and _intents.size() < MAX_INTENTS
	var kind := str(intent.get("type",""))
	# Direct player navigation always supersedes an outstanding click-to-attack
	# chase. Clicking another monster also replaces an earlier navigation target.
	# Any already-sent Server command may still return a reliable receipt, but
	# its Client-side auto-follow state is no longer retained.
	if accepted and kind == "stop_move":
		var kept: Array[Dictionary] = []
		for queued in _intents:
			if str(queued.get("type","")) not in ["move","attack_monster","stop_move"]:
				kept.append(queued)
		_intents = kept
	if accepted and kind in ["move","attack_monster"]:
		var kept: Array[Dictionary] = []
		for queued in _intents:
			# A stop intent is a barrier: later interaction/navigation waits
			# until the Server has acknowledged the stop SelfState.
			if str(queued.get("type","")) not in ["move","attack_monster"]:
				kept.append(queued)
		_intents = kept
	if accepted and kind == "stop_move":
		for queued in _intents:
			if str(queued.get("type","")) == "stop_move":
				accepted = false
				break
	if accepted and kind == "use_recovery_item":
		if int(_published.get("pending_shop_sequence",0)) != 0 or int(_published.get("pending_storage_sequence",0)) != 0 or int(_published.get("pending_equipment_sequence",0)) != 0 or int(_published.get("pending_bank_sequence",0)) != 0 or int(_published.get("pending_repair_sequence",0)) != 0:
			accepted = false
		for queued in _intents:
			if str(queued.get("type","")) in ["use_recovery_item","discard_inventory_item","shop_buy","shop_sell","storage_deposit","storage_withdraw","equipment_equip","equipment_unequip","bank_deposit","bank_withdraw","repair_open","repair_item"]:
				accepted = false
				break
	if accepted and kind == "discard_inventory_item":
		if int(_published.get("pending_shop_sequence",0)) != 0 or int(_published.get("pending_storage_sequence",0)) != 0 or int(_published.get("pending_equipment_sequence",0)) != 0 or int(_published.get("pending_bank_sequence",0)) != 0 or int(_published.get("pending_repair_sequence",0)) != 0:
			accepted = false
		for queued in _intents:
			if str(queued.get("type","")) in ["discard_inventory_item","use_recovery_item","shop_buy","shop_sell","storage_deposit","storage_withdraw","equipment_equip","equipment_unequip","bank_deposit","bank_withdraw","repair_open","repair_item"]:
				accepted = false
				break
	if accepted and kind in ["shop_open","shop_buy","shop_sell"]:
		if int(_published.get("pending_shop_sequence",0)) != 0 or int(_published.get("pending_storage_sequence",0)) != 0 or int(_published.get("pending_equipment_sequence",0)) != 0 or int(_published.get("pending_bank_sequence",0)) != 0 or int(_published.get("pending_repair_sequence",0)) != 0:
			accepted = false
		for queued in _intents:
			if str(queued.get("type","")) in ["shop_open","shop_buy","shop_sell","use_recovery_item","discard_inventory_item","storage_open","storage_deposit","storage_withdraw","equipment_equip","equipment_unequip","bank_open","bank_deposit","bank_withdraw","repair_open","repair_item"]:
				accepted = false
				break
	if accepted and kind in ["storage_open","storage_deposit","storage_withdraw"]:
		if int(_published.get("pending_storage_sequence",0)) != 0 or int(_published.get("pending_shop_sequence",0)) != 0 or int(_published.get("pending_bank_sequence",0)) != 0 or int(_published.get("pending_repair_sequence",0)) != 0:
			accepted = false
		for queued in _intents:
			if str(queued.get("type","")) in ["storage_open","storage_deposit","storage_withdraw","shop_open","shop_buy","shop_sell","use_recovery_item","discard_inventory_item","equipment_equip","equipment_unequip","bank_open","bank_deposit","bank_withdraw","repair_open","repair_item"]:
				accepted = false
				break
	if accepted and kind in ["equipment_equip","equipment_unequip"]:
		if int(_published.get("pending_equipment_sequence",0)) != 0 or int(_published.get("pending_shop_sequence",0)) != 0 or int(_published.get("pending_storage_sequence",0)) != 0 or int(_published.get("pending_bank_sequence",0)) != 0 or int(_published.get("pending_repair_sequence",0)) != 0 or int(_published.get("pending_recovery_sequence",0)) != 0 or int(_published.get("pending_discard_sequence",0)) != 0:
			accepted = false
		for queued in _intents:
			if str(queued.get("type","")) in ["equipment_equip","equipment_unequip","shop_open","shop_buy","shop_sell","storage_open","storage_deposit","storage_withdraw","use_recovery_item","discard_inventory_item","bank_open","bank_deposit","bank_withdraw","repair_open","repair_item"]:
				accepted = false
				break
	if accepted and kind in ["bank_open","bank_deposit","bank_withdraw"]:
		if int(_published.get("pending_bank_sequence",0)) != 0 or int(_published.get("pending_repair_sequence",0)) != 0 or int(_published.get("pending_shop_sequence",0)) != 0 or int(_published.get("pending_storage_sequence",0)) != 0 or int(_published.get("pending_equipment_sequence",0)) != 0 or int(_published.get("pending_recovery_sequence",0)) != 0 or int(_published.get("pending_discard_sequence",0)) != 0:
			accepted = false
		for queued in _intents:
			if str(queued.get("type","")) in ["bank_open","bank_deposit","bank_withdraw","shop_open","shop_buy","shop_sell","storage_open","storage_deposit","storage_withdraw","equipment_equip","equipment_unequip","use_recovery_item","discard_inventory_item","repair_open","repair_item"]:
				accepted = false
				break
	if accepted and kind in ["repair_open","repair_item"]:
		if int(_published.get("pending_repair_sequence",0)) != 0 or int(_published.get("pending_bank_sequence",0)) != 0 or int(_published.get("pending_shop_sequence",0)) != 0 or int(_published.get("pending_storage_sequence",0)) != 0 or int(_published.get("pending_equipment_sequence",0)) != 0 or int(_published.get("pending_recovery_sequence",0)) != 0 or int(_published.get("pending_discard_sequence",0)) != 0:
			accepted = false
		for queued in _intents:
			if str(queued.get("type","")) in ["repair_open","repair_item","bank_open","bank_deposit","bank_withdraw","shop_open","shop_buy","shop_sell","storage_open","storage_deposit","storage_withdraw","equipment_equip","equipment_unequip","use_recovery_item","discard_inventory_item"]:
				accepted = false
				break
	if accepted and kind == "cast_skill":
		# A direct caller must not enqueue a second MP debit while queued or
		# while the reliable server receipt for the first skill is unresolved.
		if int(_published.get("pending_skill_sequence",0)) != 0:
			accepted = false
		for queued in _intents:
			if str(queued.get("type","")) == "cast_skill":
				accepted = false
				break
	if accepted and kind == "pickup_ground_drop":
		# Reject repeated clicks while the first one is still in the intent
		# queue, before it acquires a pending authoritative sequence.
		for queued in _intents:
			if str(queued.get("type","")) == "pickup_ground_drop":
				accepted = false
				break
	if accepted:
		_intent_token += 1
		var queued_intent := intent.duplicate(true)
		queued_intent["_token"] = _intent_token
		_intents.append(queued_intent)
	_mutex.unlock()
	return accepted

func _logic_loop() -> void:
	var auth_sent := false
	while not _should_stop():
		var transport_status: Dictionary = _transport.get_status()
		var transport_state := int(transport_status.state)
		if transport_state == Transport.STATE_FAILED:
			_last_transport_error = int(transport_status.terminal_error)
			if _can_auto_reconnect() and _start_reconnect_attempt():
				auth_sent = false
				continue
			_world.fail("transport_error_%d" % _last_transport_error)
			_publish()
			break
		if transport_state == Transport.STATE_CONNECTED and not auth_sent:
			var local_password := _credential_string()
			var auth_frame := WireCodec.encode_auth_request(_username, local_password)
			local_password = ""
			if auth_frame.is_empty():
				_world.fail("invalid_auth_input")
				_publish()
				break
			if _transport.send_frame(auth_frame):
				auth_sent = _world.mark_auth_sent()
				_publish()

		for event in _transport.drain_events(64):
			if str(event.get("type", "")) != "frame":
				continue
			var frame: PackedByteArray = event.get("frame", PackedByteArray())
			if not _world.apply_frame(frame):
				_publish()
				_transport.stop()
				_clear_credentials()
				return
			_publish()
			_flush_control_frames()

		_flush_control_frames()
		_process_reconnect_choose()
		_process_first_intent()
		_maybe_finish_world_entry()
		if int(_world.state) == WorldState.STATE_FAILED:
			if _can_retry_world_rejected() and _start_reconnect_attempt():
				auth_sent = false
				continue
			_transport.stop()
			break
		OS.delay_msec(LOGIC_SLEEP_MS)
	_publish()
	if int(_world.state) == WorldState.STATE_FAILED:
		_clear_credentials()

func _can_auto_reconnect() -> bool:
	_mutex.lock()
	var allowed := _has_entered_world and _selected_character_id > 0 and _reconnect_attempt < MAX_RECONNECT_ATTEMPTS and not _stop_requested
	_mutex.unlock()
	return allowed

func _can_retry_world_rejected() -> bool:
	if int(_world.choose_status) != 2:
		return false
	_mutex.lock()
	var allowed := _reconnecting and _reconnect_attempt < MAX_RECONNECT_ATTEMPTS and not _stop_requested
	_mutex.unlock()
	return allowed

func _start_reconnect_attempt() -> bool:
	_transport.stop()
	_mutex.lock()
	if _stop_requested or not _has_entered_world or _selected_character_id <= 0 or _reconnect_attempt >= MAX_RECONNECT_ATTEMPTS:
		_mutex.unlock()
		return false
	_reconnecting = true
	_reconnect_attempt += 1
	var attempt := _reconnect_attempt
	_intents.clear() # never replay old gameplay intents across a new connection generation
	_mutex.unlock()
	_publish()
	var delay_ms := int(RECONNECT_BACKOFF_MS[mini(attempt-1,RECONNECT_BACKOFF_MS.size()-1)])
	var until := Time.get_ticks_msec()+delay_ms
	while Time.get_ticks_msec()<until:
		if _should_stop():
			return false
		OS.delay_msec(mini(LOGIC_SLEEP_MS,until-Time.get_ticks_msec()))
	_world = WorldState.new()
	_publish()
	var error: int = _transport.start_secure(_host,_port,_common_name,_trusted_chain)
	if error != OK:
		_last_transport_error = error
		return _start_reconnect_attempt() if attempt < MAX_RECONNECT_ATTEMPTS else false
	return true

func _process_reconnect_choose() -> void:
	_mutex.lock()
	var reconnecting := _reconnecting
	var character_id := _selected_character_id
	_mutex.unlock()
	if not reconnecting or character_id <= 0 or int(_world.state) != WorldState.STATE_SELECTING_CHARACTER:
		return
	if not _world.characters.any(func(entry: Dictionary) -> bool: return int(entry.character_id)==character_id):
		_world.fail("reconnect_character_missing")
		_publish()
		return
	var frame := WireCodec.encode_character_choose(character_id)
	if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_choose_sent(character_id):
		_publish()

func _maybe_finish_world_entry() -> void:
	var current: Dictionary = _world.snapshot()
	if int(current.state) != WorldState.STATE_IN_WORLD or not bool(current.world_ready) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch):
		return
	_mutex.lock()
	_has_entered_world = true
	if _reconnecting:
		_reconnecting = false
		_reconnect_attempt = 0
		_reconnect_count += 1
	_mutex.unlock()
	_publish()

func _credential_string() -> String:
	_mutex.lock()
	var copy := _credential_bytes.duplicate()
	_mutex.unlock()
	var value := copy.get_string_from_utf8()
	copy.fill(0)
	return value

func _clear_credentials() -> void:
	_mutex.lock()
	_clear_credentials_locked()
	_mutex.unlock()

func _clear_credentials_locked() -> void:
	if not _credential_bytes.is_empty():
		_credential_bytes.fill(0)
		_credential_bytes.clear()

func _decorate_snapshot(value: Dictionary) -> Dictionary:
	value["reconnecting"] = _reconnecting
	value["reconnect_attempt"] = _reconnect_attempt
	value["reconnect_count"] = _reconnect_count
	value["max_reconnect_attempts"] = MAX_RECONNECT_ATTEMPTS
	value["last_transport_error"] = _last_transport_error
	return value

func _flush_control_frames() -> void:
	var current: Dictionary = _world.snapshot()
	var ack_epoch := int(current.pending_world_ready_ack_epoch)
	var ack_tick := int(current.pending_world_ready_ack_tick)
	if ack_epoch > 0 and ack_tick >= 0:
		var ack := WireCodec.encode_world_ready_ack(ack_epoch,ack_tick)
		if not ack.is_empty() and _transport.send_frame(ack):
			_world.mark_world_ready_ack_sent(ack_epoch,ack_tick)
			_publish()
			current = _world.snapshot()
	if bool(current.resync_pending) and not bool(current.resync_request_sent):
		var epoch := int(current.map_epoch)
		var request := WireCodec.encode_world_resync_request(epoch)
		if not request.is_empty() and _transport.send_frame(request):
			_world.mark_resync_request_sent(epoch)
			_publish()

func _process_first_intent() -> void:
	var intent := _peek_intent()
	if intent.is_empty():
		return
	var kind := str(intent.get("type", ""))
	if kind == "create_character":
		if int(_world.state) != WorldState.STATE_SELECTING_CHARACTER:
			return
		var slot := int(intent.get("slot",-1))
		var character_name := str(intent.get("name",""))
		var frame := WireCodec.encode_character_create(slot,character_name)
		if not frame.is_empty() and _transport.send_frame(frame) and 			_world.mark_character_create_sent(slot,character_name):
			_pop_intent()
			_publish()
	elif kind == "choose":
		if int(_world.state) != WorldState.STATE_SELECTING_CHARACTER:
			return
		var character_id := int(intent.character_id)
		var frame := WireCodec.encode_character_choose(character_id)
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_choose_sent(character_id):
			_mutex.lock()
			_selected_character_id = character_id
			_mutex.unlock()
			_pop_intent()
			_publish()
	elif kind == "npc":
		var current: Dictionary = _world.snapshot()
		if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.player_dead) or not bool(current.world_ready) or bool(current.resync_pending) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or bool(current.waiting_for_new_map) or int(current.pending_npc_sequence) != 0:
			return
		var sequence := int(current.npc_interaction_sequence) + 1
		var frame := WireCodec.encode_npc_interact(int(current.map_epoch), sequence, int(intent.spawn_id))
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_npc_interact_sent(sequence):
			_pop_intent()
			_publish()

	elif kind == "trade":
		var current: Dictionary = _world.snapshot()
		if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
			_pop_intent()
			return
		if int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)):
			return
		var sequence := int(current.get("trade_sequence",0))+1
		var action := int(intent.get("action",0))
		var trade_id := int(intent.get("trade_id",0))
		var target := int(intent.get("target_actor_id",0))
		var uid := int(intent.get("item_uid",0))
		var money := int(intent.get("money",0))
		var frame := WireCodec.encode_trade_command(
			int(current.get("map_epoch",0)),sequence,action,
			trade_id,target,uid,money)
		if frame.is_empty():
			_pop_intent()
			return
		if _transport.send_frame(frame) and _world.mark_trade_sent(
				sequence,action,trade_id,target,uid,money):
			_pop_intent()
			_publish()

	elif kind in ["repair_open","repair_item"]:
		var current: Dictionary = _world.snapshot()
		if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
			_pop_intent()
			return
		if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)) or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_shop_sequence",0)) != 0 or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_equipment_sequence",0)) != 0 or int(current.get("pending_recovery_sequence",0)) != 0 or int(current.get("pending_discard_sequence",0)) != 0:
			return
		var sequence := int(current.get("repair_sequence",0))+1
		var npc := int(intent.get("npc_spawn_id",0))
		var uid := int(intent.get("item_uid",0))
		var frame := PackedByteArray()
		var repair_kind := 0
		if kind == "repair_open":
			repair_kind = 1
			frame = WireCodec.encode_repair_open(int(current.map_epoch),sequence,npc)
		else:
			repair_kind = 2
			if not bool(current.get("repair_ready",false)) or int(current.get("active_repair_npc",0)) != npc or not (current.get("repair_items",{}) as Dictionary).has(uid):
				_pop_intent()
				return
			frame = WireCodec.encode_repair_item(int(current.map_epoch),sequence,npc,uid)
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_repair_sent(sequence,repair_kind,npc,uid):
			_pop_intent()
			_publish()
	elif kind in ["bank_open","bank_deposit","bank_withdraw"]:
		var current: Dictionary = _world.snapshot()
		if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
			_pop_intent()
			return
		if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)) or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_shop_sequence",0)) != 0 or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_equipment_sequence",0)) != 0 or int(current.get("pending_recovery_sequence",0)) != 0 or int(current.get("pending_discard_sequence",0)) != 0:
			return
		var sequence := int(current.get("bank_sequence",0))+1
		var npc := int(intent.get("npc_spawn_id",0))
		var amount := int(intent.get("amount",0))
		var frame := PackedByteArray()
		var bank_kind := 0
		if kind == "bank_open":
			bank_kind = 1
			frame = WireCodec.encode_bank_open(int(current.map_epoch),sequence,npc)
		elif kind == "bank_deposit":
			bank_kind = 2
			frame = WireCodec.encode_bank_deposit(int(current.map_epoch),sequence,npc,amount)
		else:
			bank_kind = 3
			frame = WireCodec.encode_bank_withdraw(int(current.map_epoch),sequence,npc,amount)
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_bank_sent(sequence,bank_kind,npc,amount):
			_pop_intent()
			_publish()
	elif kind in ["equipment_equip","equipment_unequip"]:
		var current: Dictionary = _world.snapshot()
		if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
			_pop_intent()
			return
		if int(current.state) != WorldState.STATE_IN_WORLD or not bool(current.world_ready) or not bool(current.inventory_ready) or not bool(current.get("equipment_ready",false)) or bool(current.resync_pending) or bool(current.waiting_for_new_map) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or int(current.get("pending_equipment_sequence",0)) != 0 or int(current.get("pending_shop_sequence",0)) != 0 or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_recovery_sequence",0)) != 0 or int(current.get("pending_discard_sequence",0)) != 0:
			return
		var uid := int(intent.get("item_uid",0))
		var sequence := int(current.get("equipment_sequence",0))+1
		var equipment_kind := 1 if kind == "equipment_equip" else 2
		var frame := WireCodec.encode_equipment_equip(int(current.map_epoch),sequence,uid) if equipment_kind == 1 else WireCodec.encode_equipment_unequip(int(current.map_epoch),sequence,uid)
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_equipment_sent(sequence,equipment_kind,uid):
			_pop_intent()
			_publish()
	elif kind in ["storage_open","storage_deposit","storage_withdraw"]:
		var current: Dictionary = _world.snapshot()
		if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
			_pop_intent()
			return
		if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.player_dead) or not bool(current.world_ready) or not bool(current.inventory_ready) or bool(current.resync_pending) or bool(current.waiting_for_new_map) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_shop_sequence",0)) != 0 or int(current.get("pending_equipment_sequence",0)) != 0 or int(current.get("pending_recovery_sequence",0)) != 0 or int(current.get("pending_discard_sequence",0)) != 0:
			return
		var sequence := int(current.get("storage_sequence",0))+1
		var npc := int(intent.get("npc_spawn_id",0))
		var uid := int(intent.get("item_uid",0))
		var frame := PackedByteArray()
		var storage_kind := 0
		if kind == "storage_open":
			storage_kind = 1
			frame = WireCodec.encode_storage_open(int(current.map_epoch),sequence,npc)
		elif kind == "storage_deposit":
			storage_kind = 2
			frame = WireCodec.encode_storage_deposit(int(current.map_epoch),sequence,npc,uid)
		else:
			storage_kind = 3
			frame = WireCodec.encode_storage_withdraw(int(current.map_epoch),sequence,npc,uid)
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_storage_sent(sequence,storage_kind,npc,uid):
			_pop_intent()
			_publish()
	elif kind in ["shop_open","shop_buy","shop_sell"]:
		var current: Dictionary = _world.snapshot()
		if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
			_pop_intent()
			return
		if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.player_dead) or not bool(current.world_ready) or not bool(current.inventory_ready) or bool(current.resync_pending) or bool(current.waiting_for_new_map) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or int(current.get("pending_shop_sequence",0)) != 0 or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_equipment_sequence",0)) != 0 or int(current.get("pending_recovery_sequence",0)) != 0 or int(current.get("pending_discard_sequence",0)) != 0:
			return
		var sequence := int(current.get("shop_sequence",0))+1
		var npc := int(intent.get("npc_spawn_id",0))
		var frame := PackedByteArray()
		var shop_kind := 0
		var item_id := int(intent.get("item_definition_id",0))
		var uid := int(intent.get("item_uid",0))
		var quantity := int(intent.get("quantity",0))
		if kind == "shop_open":
			shop_kind = 1
			frame = WireCodec.encode_shop_open(int(current.map_epoch),sequence,npc)
		elif kind == "shop_buy":
			shop_kind = 2
			frame = WireCodec.encode_shop_buy(int(current.map_epoch),sequence,npc,item_id,quantity)
		else:
			shop_kind = 3
			frame = WireCodec.encode_shop_sell(int(current.map_epoch),sequence,npc,uid,item_id,quantity)
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_shop_sent(sequence,shop_kind,npc,item_id,uid,quantity):
			_pop_intent()
			_publish()
	elif kind == "use_recovery_item":
		var current: Dictionary = _world.snapshot()
		if int(current.state) != WorldState.STATE_IN_WORLD or not bool(current.combat_available) or bool(current.player_dead) or not bool(current.inventory_ready) or not bool(current.world_ready) or bool(current.resync_pending) or bool(current.waiting_for_new_map) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or int(current.pending_recovery_sequence) != 0 or int(current.pending_discard_sequence) != 0 or int(current.get("pending_shop_sequence",0)) != 0 or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_equipment_sequence",0)) != 0 or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0:
			return
		var uid := int(intent.item_uid)
		if not (current.inventory_items as Dictionary).has(uid):
			_pop_intent()
			return
		var sequence := int(current.recovery_sequence)+1
		var frame := WireCodec.encode_recovery_use(int(current.map_epoch),sequence,uid)
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_recovery_use_sent(sequence,uid):
			_pop_intent()
			_publish()
	elif kind == "discard_inventory_item":
		var current: Dictionary = _world.snapshot()
		if int(current.state) != WorldState.STATE_IN_WORLD or not bool(current.inventory_ready) or not bool(current.world_ready) or bool(current.resync_pending) or bool(current.waiting_for_new_map) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or int(current.pending_discard_sequence) != 0 or int(current.pending_recovery_sequence) != 0 or int(current.get("pending_shop_sequence",0)) != 0 or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_equipment_sequence",0)) != 0 or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0:
			return
		var uid := int(intent.item_uid)
		var quantity := int(intent.quantity)
		var items: Dictionary = current.inventory_items
		if not items.has(uid) or quantity < 1 or int((items[uid] as Dictionary).quantity) < quantity:
			_pop_intent()
			return
		var sequence := int(current.discard_sequence)+1
		var frame := WireCodec.encode_inventory_discard(int(current.map_epoch),sequence,uid,quantity)
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_inventory_discard_sent(sequence,uid,quantity):
			_pop_intent()
			_publish()
	elif kind == "pickup_ground_drop":
		var current: Dictionary = _world.snapshot()
		if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.player_dead) or not bool(current.world_ready) or bool(current.resync_pending) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or bool(current.waiting_for_new_map) or int(current.pending_pickup_sequence) != 0:
			return
		if not (current.ground_drops as Dictionary).has(int(intent.drop_id)):
			_pop_intent() # ground item already disappeared before this intent sent
			return
		var sequence := int(current.pickup_sequence) + 1
		var frame := WireCodec.encode_ground_pickup(int(current.map_epoch),sequence,int(intent.drop_id))
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_pickup_sent(sequence,int(intent.drop_id)):
			_pop_intent()
			_publish()
	elif kind == "respawn":
		var current: Dictionary = _world.snapshot()
		if int(current.state) != WorldState.STATE_IN_WORLD or not bool(current.player_dead) or not bool(current.combat_available) or not bool(current.world_ready) or bool(current.resync_pending) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or bool(current.waiting_for_new_map):
			return
		var sequence := int(current.respawn_sequence) + 1
		var frame := WireCodec.encode_player_respawn(int(current.map_epoch),sequence)
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_player_respawn_sent(sequence):
			_pop_intent()
			_publish()
	elif kind == "cast_skill":
		var current: Dictionary = _world.snapshot()
		if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
			_pop_intent() # stale before send: never retarget a different map/actor
			return
		if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.player_dead) or not bool(current.world_ready) or bool(current.resync_pending) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or bool(current.waiting_for_new_map):
			return
		if int(current.get("pending_skill_sequence",0)) != 0:
			return
		var target := int(intent.entity_id)
		var skill_id := int(intent.skill_id)
		if target < 0 or skill_id <= 0:
			_pop_intent()
			return
		var sequence := int(current.skill_sequence)+1
		var frame := WireCodec.encode_skill_use(int(current.map_epoch),sequence,skill_id,target)
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_skill_sent(sequence):
			# Publish pending before removing this intent: no duplicate window.
			_publish()
			_pop_intent()
	elif kind == "attack_monster":
		_process_attack_monster_intent(intent)
	elif kind == "stop_move":
		_process_stop_movement_intent(intent)
	elif kind == "move":
		var current: Dictionary = _world.snapshot()
		if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.player_dead) or not bool(current.world_ready) or bool(current.resync_pending) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or bool(current.waiting_for_new_map):
			return
		var token := int(intent.get("_token",0))
		if not _first_intent_matches(token):
			return
		var sequence := int(current.movement_sequence) + 1
		var frame := WireCodec.encode_move_target(int(current.map_epoch), sequence, int(intent.x), int(intent.y))
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_move_sent(sequence):
			_pop_intent_token(token)
			_publish()
	else:
		_world.fail("unknown_intent")
		_publish()

func _process_stop_movement_intent(intent: Dictionary) -> void:
	var current: Dictionary = _world.snapshot()
	var token := int(intent.get("_token",0))
	if token <= 0 or not _first_intent_matches(token):
		return
	if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.player_dead) or bool(current.waiting_for_new_map):
		_pop_intent_token(token)
		return
	if not bool(current.world_ready) or bool(current.resync_pending) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch):
		return
	var phase := str(intent.get("phase","send_stop"))
	if phase == "send_stop":
		var sequence := int(current.movement_sequence)+1
		var frame := WireCodec.encode_move_target(
			int(current.map_epoch),sequence,
			roundi(float(current.x)),roundi(float(current.y)))
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_move_sent(sequence):
			var waiting := intent.duplicate(true)
			waiting["phase"] = "await_stop"
			waiting["movement_sequence"] = sequence
			_replace_first_intent(token,waiting)
			_publish()
		return
	if phase == "await_stop":
		var sequence := int(intent.get("movement_sequence",0))
		if int(current.last_receipt_sequence) < sequence:
			return
		if int(current.last_receipt_sequence) != sequence or int(current.last_receipt_status) != 0:
			_pop_intent_token(token)
			return
		var own: Dictionary = current.self_state
		if own.is_empty() or int(own.get("movement_action_sequence",0)) < sequence:
			return
		if bool(own.get("moving",false)):
			return
		_pop_intent_token(token)
		return
	_pop_intent_token(token)


func _process_attack_monster_intent(intent: Dictionary) -> void:
	var current: Dictionary = _world.snapshot()
	var token := int(intent.get("_token",0))
	if token <= 0 or not _first_intent_matches(token):
		return
	if int(current.state) != WorldState.STATE_IN_WORLD:
		return
	if bool(current.player_dead):
		_pop_intent_token(token)
		return
	if not bool(current.world_ready) or bool(current.resync_pending) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or bool(current.waiting_for_new_map):
		return

	var target_id := int(intent.get("entity_id",0))
	if target_id <= 0:
		_pop_intent_token(token)
		return
	var monsters: Dictionary = current.monsters
	if not monsters.has(target_id):
		# Same-connection AOI resync rebuilds WorldReady before the visible
		# MonsterSpawn baseline is necessarily drained. Do not interpret that
		# short baseline gap as a real despawn; keep it bounded so an actually
		# vanished target still cancels the chase.
		var missing_since := int(intent.get("target_missing_since_ms",0))
		if missing_since == 0:
			var waiting_target := intent.duplicate(true)
			waiting_target["target_missing_since_ms"] = Time.get_ticks_msec()
			_replace_first_intent(token,waiting_target)
			return
		if Time.get_ticks_msec()-missing_since < AUTO_TARGET_MISSING_GRACE_MS:
			return
		_pop_intent_token(token)
		return
	if intent.has("target_missing_since_ms"):
		var restored := intent.duplicate(true)
		restored.erase("target_missing_since_ms")
		if not _replace_first_intent(token,restored):
			return
		intent = restored
	var target: Dictionary = monsters[target_id]
	if int(target.get("current_hp",0)) <= 0 or int(target.get("state",0)) == 4:
		_pop_intent_token(token)
		return

	var phase := str(intent.get("phase","send_attack"))
	if phase == "send_attack":
		var sequence := int(current.attack_sequence)+1
		var frame := WireCodec.encode_normal_attack(
			int(current.map_epoch),sequence,target_id)
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_normal_attack_sent(sequence):
			var waiting := intent.duplicate(true)
			waiting["phase"] = "await_attack"
			waiting["attack_sequence"] = sequence
			_replace_first_intent(token,waiting)
			_publish()
		return

	if phase == "await_attack":
		var attack_sequence := int(intent.get("attack_sequence",0))
		var result_sequence := int(current.last_attack_result_sequence)
		if result_sequence < attack_sequence:
			return
		if result_sequence != attack_sequence:
			_pop_intent_token(token)
			return
		var result: Dictionary = current.last_attack_result
		if int(result.get("target_entity_id",0)) != target_id:
			_pop_intent_token(token)
			return
		if int(current.last_attack_result_status) != NORMAL_ATTACK_OUT_OF_RANGE:
			_pop_intent_token(token)
			return
		var cycles := int(intent.get("approach_cycles",0))
		if cycles >= MAX_AUTO_APPROACH_CYCLES:
			_pop_intent_token(token)
			return
		# Use the latest authoritative monster position from this reducer
		# snapshot. No Client attack range is guessed; the next attack is still
		# accepted/rejected by Server.
		if int(target.get("current_hp",0)) <= 0 or int(target.get("state",0)) == 4:
			_pop_intent_token(token)
			return
		var move_sequence := int(current.movement_sequence)+1
		var target_x := roundi(float(target.get("x",0.0)))
		var target_y := roundi(float(target.get("y",0.0)))
		var move_frame := WireCodec.encode_move_target(
			int(current.map_epoch),move_sequence,target_x,target_y)
		if not move_frame.is_empty() and _transport.send_frame(move_frame) and _world.mark_move_sent(move_sequence):
			var moving := intent.duplicate(true)
			moving["phase"] = "await_move"
			moving["move_sequence"] = move_sequence
			moving["approach_cycles"] = cycles+1
			_replace_first_intent(token,moving)
			_publish()
		return

	if phase == "await_move":
		var move_sequence := int(intent.get("move_sequence",0))
		var receipt_sequence := int(current.last_receipt_sequence)
		if receipt_sequence < move_sequence:
			return
		if receipt_sequence != move_sequence or int(current.last_receipt_status) != MOVE_RECEIPT_ACCEPTED:
			_pop_intent_token(token)
			return
		var self_state: Dictionary = current.self_state
		if self_state.is_empty() or int(self_state.get("movement_action_sequence",0)) < move_sequence:
			return
		if bool(self_state.get("moving",false)):
			return
		# Route completion does not imply attack range. Retry the authoritative
		# attack and only re-approach when Server again says OutOfRange.
		var retry := intent.duplicate(true)
		retry["phase"] = "send_attack"
		retry["attack_sequence"] = 0
		retry["move_sequence"] = 0
		_replace_first_intent(token,retry)
		return

	_pop_intent_token(token)


func _peek_intent() -> Dictionary:
	_mutex.lock()
	var intent: Dictionary = {} if _intents.is_empty() else _intents[0].duplicate(true)
	_mutex.unlock()
	return intent

func _pop_intent() -> void:
	_mutex.lock()
	if not _intents.is_empty():
		_intents.pop_front()
	_mutex.unlock()

func _first_intent_matches(token: int) -> bool:
	_mutex.lock()
	var matches := token > 0 and not _intents.is_empty() and int(_intents[0].get("_token",0)) == token
	_mutex.unlock()
	return matches

func _replace_first_intent(token: int, replacement: Dictionary) -> bool:
	_mutex.lock()
	var replaced := token > 0 and not _intents.is_empty() and int(_intents[0].get("_token",0)) == token
	if replaced:
		var next := replacement.duplicate(true)
		next["_token"] = token
		_intents[0] = next
	_mutex.unlock()
	return replaced

func _pop_intent_token(token: int) -> bool:
	_mutex.lock()
	var removed := token > 0 and not _intents.is_empty() and int(_intents[0].get("_token",0)) == token
	if removed:
		_intents.pop_front()
	_mutex.unlock()
	return removed

func _should_stop() -> bool:
	_mutex.lock()
	var value := _stop_requested
	_mutex.unlock()
	return value

func _publish() -> void:
	var value: Dictionary = _world.snapshot()
	_mutex.lock()
	_published = _decorate_snapshot(value)
	_mutex.unlock()
