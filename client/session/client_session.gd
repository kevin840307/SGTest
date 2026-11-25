extends RefCounted

const Transport = preload("res://network/client_transport.gd")
const WireCodec = preload("res://network/wire_codec.gd")
const IntentAdmissionPolicy = preload("res://session/intent_admission_policy.gd")
const WorldState = preload("res://world/world_state.gd")

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
var _published_revision := 0 # monotonic for this ClientSession, including reconnects
var _host := ""
var _port := 0
var _username := ""
var _credential_bytes := PackedByteArray()
# Session-scoped secondary character proof. Like the account credential, keep
# only zeroable bytes so automatic reconnect can authenticate the same selected
# character without weakening the Server's password gate.
var _character_credential_bytes := PackedByteArray()
var _register_first := false
var _selected_character_id := 0
var _has_entered_world := false
var _reconnecting := false
var _reconnect_attempt := 0
var _reconnect_count := 0
var _last_transport_error := OK
var _intent_token := 0
var _attribute_sequence := 0

func _init(transport_override: RefCounted = null) -> void:
	_transport = transport_override if transport_override != null else Transport.new()

func connect_secure(host:String,port:int,username:String,password:String)->Error:
	return _connect_secure(host,port,username,password,false)


func register_secure(host:String,port:int,username:String,password:String)->Error:
	return _connect_secure(host,port,username,password,true)


func _connect_secure(
		host:String,port:int,username:String,password:String,
		register_first:bool)->Error:
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
	_username = username
	_clear_credentials_locked()
	_credential_bytes = password.to_utf8_buffer()
	_register_first = register_first
	_selected_character_id = 0
	_has_entered_world = false
	_reconnecting = false
	_reconnect_attempt = 0
	_reconnect_count = 0
	_last_transport_error = OK
	_intent_token = 0
	_attribute_sequence = 0
	_published = _decorate_snapshot(_world.snapshot())
	_published_revision += 1
	_running = true
	_mutex.unlock()
	var error: int = _transport.start_encrypted(host, port)
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

func create_character(
		slot: int, character_name: String, appearance: Dictionary = {},
		creation: Dictionary = {}) -> bool:
	var current := snapshot()
	var trimmed := character_name.strip_edges()
	var bytes := trimmed.to_utf8_buffer()
	if int(current.get("state",-1)) != WorldState.STATE_SELECTING_CHARACTER or 			slot < 0 or slot > 31 or bytes.size() < 1 or bytes.size() > 96:
		return false
	if not WorldState.allows_character_appearance(
			appearance,current.get("character_creation_appearances",[])):
		return false
	if not creation.is_empty() and not WireCodec.valid_character_creation_settings(creation):
		return false
	return _queue_intent({
		"type": "create_character",
		"slot": slot,
		"name": trimmed,
		"appearance": appearance.duplicate(true),
		"creation": creation.duplicate(true),
	})

func choose_character(character_id: int, character_password: String = "") -> bool:
	if int(snapshot().get("state", -1)) != WorldState.STATE_SELECTING_CHARACTER:
		return false
	var encoded := WireCodec.encode_character_choose(character_id, character_password)
	if encoded.is_empty():
		return false
	encoded.fill(0)
	return _queue_intent({"type": "choose", "character_id": character_id, "character_password": character_password})

func change_character_password(character_id: int, old_password: String, new_password: String) -> bool:
	if int(snapshot().get("state",-1)) != WorldState.STATE_SELECTING_CHARACTER or WireCodec.encode_character_manage(1,character_id,old_password,new_password).is_empty():
		return false
	return _queue_intent({"type":"character_manage","action":1,"character_id":character_id,
		"old_password":old_password,"new_password":new_password})

func delete_character(character_id: int, password: String) -> bool:
	if int(snapshot().get("state",-1)) != WorldState.STATE_SELECTING_CHARACTER or WireCodec.encode_character_manage(2,character_id,password).is_empty():
		return false
	return _queue_intent({"type":"character_manage","action":2,"character_id":character_id,
		"old_password":password,"new_password":""})

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
	if item_uid <= 0 or not bool(current.get("inventory_ready",false)) or not bool(current.get("combat_available",false)) or bool(current.get("player_dead",false)) or int(current.get("pending_recovery_sequence",0)) != 0 or int(current.get("pending_discard_sequence",0)) != 0 or not (current.get("inventory_items",{}) as Dictionary).has(item_uid) or int(((current.get("inventory_items",{}) as Dictionary)[item_uid] as Dictionary).get("quantity",0)) <= 0:
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
	if npc_spawn_id <= 0 or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_enhancement_sequence",0)) != 0 or int(current.get("trade_id",0)) != 0:
		return false
	return _queue_intent({"type":"bank_open","npc_spawn_id":npc_spawn_id,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func deposit_bank_money(amount: int) -> bool:
	var current := snapshot()
	var npc := int(current.get("active_bank_npc",0))
	if npc <= 0 or amount <= 0 or amount > WireCodec.MAX_MONEY or int(current.get("bank_scope",0)) not in [1,2] or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_enhancement_sequence",0)) != 0 or int(current.get("trade_id",0)) != 0:
		return false
	return _queue_intent({"type":"bank_deposit","npc_spawn_id":npc,"amount":amount,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func withdraw_bank_money(amount: int) -> bool:
	var current := snapshot()
	var npc := int(current.get("active_bank_npc",0))
	if npc <= 0 or amount <= 0 or amount > WireCodec.MAX_MONEY or int(current.get("bank_scope",0)) not in [1,2] or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_enhancement_sequence",0)) != 0 or int(current.get("trade_id",0)) != 0:
		return false
	return _queue_intent({"type":"bank_withdraw","npc_spawn_id":npc,"amount":amount,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func open_player_shop(item_uid:int,price:int)->bool:
	var current:=snapshot()
	var inventory:Dictionary=current.get("inventory_items",{})
	if item_uid<=0 or price<=0 or price>WireCodec.MAX_MONEY or not inventory.has(item_uid):
		return false
	return _queue_player_shop_intent(1,0,0,item_uid,price,current)


func close_player_shop(listing_id:int)->bool:
	if listing_id<=0:
		return false
	var current:=snapshot()
	return _queue_player_shop_intent(2,0,listing_id,0,0,current)


func inspect_player_shop(seller_actor_id:int)->bool:
	var current:=snapshot()
	var entities:Dictionary=current.get("entities",{})
	if (
		seller_actor_id<=0 or seller_actor_id==int(current.get("actor_id",0)) or
		not entities.has(seller_actor_id)
	):
		return false
	return _queue_player_shop_intent(3,seller_actor_id,0,0,0,current)


func buy_player_shop(seller_actor_id:int,listing_id:int)->bool:
	var current:=snapshot()
	var entities:Dictionary=current.get("entities",{})
	if (
		seller_actor_id<=0 or seller_actor_id==int(current.get("actor_id",0)) or
		listing_id<=0 or not entities.has(seller_actor_id)
	):
		return false
	return _queue_player_shop_intent(
		4,seller_actor_id,listing_id,0,0,current)


func _queue_player_shop_intent(
		action:int,seller_actor_id:int,listing_id:int,item_uid:int,
		price:int,current:Dictionary)->bool:
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		bool(current.get("player_dead",false)) or
		not bool(current.get("world_ready",false)) or
		not bool(current.get("inventory_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_player_shop_sequence",0))!=0 or
		int(current.get("trade_id",0))!=0
	):
		return false
	return _queue_intent({
		"type":"player_shop","action":action,
		"seller_actor_id":seller_actor_id,"listing_id":listing_id,
		"item_uid":item_uid,"price":price,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))
	})


func send_nearby_chat(message:String,bubble_type:int=0)->bool:
	var clean:=message.strip_edges()
	var bytes:=clean.to_utf8_buffer()
	var current:=snapshot()
	if (
		clean.is_empty() or bytes.size()>WireCodec.CHAT_MAX_MESSAGE_BYTES or
		bubble_type<0 or bubble_type>5 or
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_whisper_sequence",0))!=0
	):
		return false
	return _queue_intent({
		"type":"nearby_chat","message":clean,"bubble_type":bubble_type,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))
	})

func send_whisper(
		target_actor_id:int,message:String,bubble_type:int=0)->bool:
	return _queue_whisper_intent(target_actor_id,"",message,bubble_type)


func send_whisper_name(
		target_name:String,message:String,bubble_type:int=0)->bool:
	return _queue_whisper_intent(0,target_name,message,bubble_type)


func _queue_whisper_intent(
		target_actor_id:int,target_name:String,message:String,bubble_type:int)->bool:
	var clean:=message.strip_edges()
	var bytes:=clean.to_utf8_buffer()
	var clean_name:=target_name.strip_edges()
	var name_bytes:=clean_name.to_utf8_buffer()
	var current:=snapshot()
	var by_name:=not clean_name.is_empty()
	if (
		target_actor_id<0 or (target_actor_id>0)==by_name or
		(target_actor_id>0 and target_actor_id==int(current.get("actor_id",0))) or
		clean_name!=target_name or name_bytes.size()>WireCodec.CHAT_MAX_SENDER_NAME_BYTES or
		clean.is_empty() or bytes.size()>WireCodec.CHAT_MAX_MESSAGE_BYTES or
		bubble_type<0 or bubble_type>5 or
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_whisper_sequence",0))!=0
	):
		return false
	return _queue_intent({
		"type":"whisper","target_actor_id":target_actor_id,
		"target_name":clean_name,"message":clean,"bubble_type":bubble_type,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))
	})


func party_invite(target_actor_id:int)->bool:
	var current:=snapshot()
	return _queue_party_intent(
		1,int(current.get("party_id",0)),target_actor_id,current)


func party_invite_name(target_name:String)->bool:
	var clean:=target_name.strip_edges()
	var bytes:=clean.to_utf8_buffer()
	if clean.is_empty() or bytes.size()>WireCodec.CHAT_MAX_SENDER_NAME_BYTES:
		return false
	var current:=snapshot()
	return _queue_party_intent(
		1,int(current.get("party_id",0)),0,current,clean)


func party_accept()->bool:
	var current:=snapshot()
	var invite:Dictionary=current.get("pending_party_invite",{})
	if invite.is_empty():
		return false
	return _queue_party_intent(
		2,int(invite.get("party_id",0)),int(invite.get("inviter_actor_id",0)),current)


func party_reject()->bool:
	var current:=snapshot()
	var invite:Dictionary=current.get("pending_party_invite",{})
	if invite.is_empty():
		return false
	return _queue_party_intent(
		3,int(invite.get("party_id",0)),int(invite.get("inviter_actor_id",0)),current)


func party_leave()->bool:
	var current:=snapshot()
	return _queue_party_intent(4,int(current.get("party_id",0)),0,current)


func party_kick(target_actor_id:int)->bool:
	var current:=snapshot()
	return _queue_party_intent(5,int(current.get("party_id",0)),target_actor_id,current)


func party_promote(target_actor_id:int)->bool:
	var current:=snapshot()
	return _queue_party_intent(6,int(current.get("party_id",0)),target_actor_id,current)


func clan_create(name:String)->bool:
	var current:=snapshot()
	return _queue_clan_intent(1,0,name,current)


func clan_join(clan_id:int)->bool:
	var current:=snapshot()
	return _queue_clan_intent(2,clan_id,"",current)


func clan_leave()->bool:
	var current:=snapshot()
	return _queue_clan_intent(3,0,"",current)


func clan_refresh()->bool:
	var current:=snapshot()
	return _queue_clan_intent(4,0,"",current)


func clan_set_announcement(text:String)->bool:
	var current:=snapshot()
	return _queue_clan_intent(5,0,text,current)


func clan_kick(target_actor_id:int)->bool:
	var current:=snapshot()
	return _queue_clan_intent(6,0,"",current,target_actor_id)


func clan_transfer_leadership(target_actor_id:int)->bool:
	var current:=snapshot()
	return _queue_clan_intent(7,0,"",current,target_actor_id)


func friend_add_name(target_name:String)->bool:
	var current:=snapshot()
	return _queue_friend_intent(1,0,target_name,current)


func friend_add_actor(target_actor_id:int)->bool:
	var current:=snapshot()
	return _queue_friend_intent(1,target_actor_id,"",current)


func friend_remove(target_actor_id:int)->bool:
	var current:=snapshot()
	return _queue_friend_intent(2,target_actor_id,"",current)


func friend_refresh()->bool:
	var current:=snapshot()
	return _queue_friend_intent(3,0,"",current)


func pet_summon(pet_uid:int)->bool:
	var current:=snapshot()
	return _queue_pet_intent(1,pet_uid,current)


func pet_recall()->bool:
	var current:=snapshot()
	return _queue_pet_intent(2,int(current.get("active_pet_uid",0)),current)


func pet_refresh()->bool:
	var current:=snapshot()
	return _queue_pet_intent(3,0,current)


func pet_equip(pet_uid:int,item_uid:int)->bool:
	var current:=snapshot()
	return _queue_pet_intent(4,pet_uid,current,item_uid)


func pet_unequip(pet_uid:int,item_uid:int)->bool:
	var current:=snapshot()
	return _queue_pet_intent(5,pet_uid,current,item_uid)


func _queue_pet_intent(action:int,pet_uid:int,current:Dictionary,item_uid:int=0)->bool:
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_pet_sequence",0))!=0
	):
		return false
	var pets:Dictionary=current.get("pet_instances",{})
	match action:
		1:
			if item_uid!=0 or pet_uid<=0 or not pets.has(pet_uid) or int(current.get("active_pet_uid",0))==pet_uid:
				return false
		2:
			if item_uid!=0 or pet_uid<=0 or int(current.get("active_pet_uid",0))!=pet_uid:
				return false
		3:
			if pet_uid!=0 or item_uid!=0:
				return false
		4:
			var inventory:Dictionary=current.get("inventory_items",{})
			if pet_uid<=0 or not pets.has(pet_uid) or item_uid<=0 or not inventory.has(item_uid):
				return false
		5:
			var equipped:Dictionary=current.get("pet_equipment_items",{}).get(item_uid,{})
			if (
				pet_uid<=0 or not pets.has(pet_uid) or item_uid<=0 or
				equipped.is_empty() or int(equipped.get("pet_uid",0))!=pet_uid
			):
				return false
		_:
			return false
	return _queue_intent({
		"type":"pet","action":action,"pet_uid":pet_uid,"item_uid":item_uid,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))
	})

func allocate_attribute(attribute:int)->bool:
	var current:=snapshot()
	if (
		attribute<1 or attribute>4 or
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("player_available_attribute_points",0))<=0
	):
		return false
	return _queue_intent({
		"type":"attribute_allocate","attribute":attribute,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))
	})

func mecha_mount(mecha_uid:int)->bool:
	var current:=snapshot()
	return _queue_mecha_intent(1,mecha_uid,current)


func mecha_dismount()->bool:
	var current:=snapshot()
	return _queue_mecha_intent(2,int(current.get("mounted_mecha_uid",0)),current)


func mecha_refresh()->bool:
	var current:=snapshot()
	return _queue_mecha_intent(3,0,current)


func mecha_equip(mecha_uid:int,item_uid:int)->bool:
	var current:=snapshot()
	return _queue_mecha_intent(4,mecha_uid,current,item_uid)


func mecha_unequip(mecha_uid:int,item_uid:int)->bool:
	var current:=snapshot()
	return _queue_mecha_intent(5,mecha_uid,current,item_uid)


func mecha_charge(mecha_uid:int,item_uid:int)->bool:
	var current:=snapshot()
	return _queue_mecha_intent(6,mecha_uid,current,item_uid)


func _queue_mecha_intent(action:int,mecha_uid:int,current:Dictionary,item_uid:int=0)->bool:
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_mecha_sequence",0))!=0
	):
		return false
	var mechas:Dictionary=current.get("mecha_instances",{})
	var mounted:=int(current.get("mounted_mecha_uid",0))
	match action:
		1:
			if item_uid!=0 or mecha_uid<=0 or not mechas.has(mecha_uid) or mounted==mecha_uid:
				return false
		2:
			if item_uid!=0 or mecha_uid<=0 or mounted!=mecha_uid:
				return false
		3:
			if mecha_uid!=0 or item_uid!=0:
				return false
		4:
			var inventory:Dictionary=current.get("inventory_items",{})
			if (
				mecha_uid<=0 or not mechas.has(mecha_uid) or mounted==mecha_uid or
				item_uid<=0 or not inventory.has(item_uid)
			):
				return false
		5:
			var equipped:Dictionary=current.get("mecha_equipment_items",{}).get(item_uid,{})
			if (
				mecha_uid<=0 or not mechas.has(mecha_uid) or mounted==mecha_uid or
				item_uid<=0 or equipped.is_empty() or
				int(equipped.get("mecha_uid",0))!=mecha_uid
			):
				return false
		6:
			var inventory:Dictionary=current.get("inventory_items",{})
			if mecha_uid<=0 or not mechas.has(mecha_uid) or item_uid<=0 or not inventory.has(item_uid):
				return false
		_:
			return false
	return _queue_intent({
		"type":"mecha","action":action,"mecha_uid":mecha_uid,"item_uid":item_uid,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))
	})


func _queue_clan_intent(action:int,request_clan_id:int,name:String,current:Dictionary,target_actor_id:int=0)->bool:
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_clan_sequence",0))!=0
	):
		return false
	var clean:=name.strip_edges()
	if clean!=name:
		return false
	var current_clan:=int(current.get("clan_id",0))
	var actor:=int(current.get("actor_id",0))
	var leader:=int(current.get("clan_leader_actor_id",0))
	var members:Dictionary=current.get("clan_members",{})
	match action:
		1:
			if current_clan!=0 or request_clan_id!=0 or target_actor_id!=0 or clean.length()<2 or clean.length()>24 or clean.to_utf8_buffer().size()>WireCodec.CLAN_MAX_NAME_BYTES:
				return false
		2:
			if current_clan!=0 or request_clan_id<=0 or target_actor_id!=0 or not clean.is_empty():
				return false
		3:
			if current_clan<=0 or request_clan_id!=0 or target_actor_id!=0 or not clean.is_empty():
				return false
		4:
			if request_clan_id!=0 or target_actor_id!=0 or not clean.is_empty():
				return false
		5:
			if current_clan<=0 or leader!=actor or request_clan_id!=0 or target_actor_id!=0 or clean.to_utf8_buffer().size()>WireCodec.CLAN_MAX_ANNOUNCEMENT_BYTES:
				return false
		6,7:
			if current_clan<=0 or leader!=actor or request_clan_id!=0 or target_actor_id<=0 or target_actor_id==actor or not members.has(target_actor_id) or not clean.is_empty():
				return false
		_:
			return false
	return _queue_intent({
		"type":"clan","action":action,"clan_id":request_clan_id,"name":clean,
		"target_actor_id":target_actor_id,
		"actor_id":actor,
		"map_epoch":int(current.get("map_epoch",0))
	})


func _queue_friend_intent(
		action:int,target_actor_id:int,target_name:String,current:Dictionary)->bool:
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_friend_sequence",0))!=0
	):
		return false
	var clean:=target_name.strip_edges()
	if clean!=target_name or clean.to_utf8_buffer().size()>WireCodec.PLAYER_IDENTITY_MAX_NAME_BYTES:
		return false
	var actor:=int(current.get("actor_id",0))
	var friends:Dictionary=current.get("friends",{})
	match action:
		1:
			if (target_actor_id>0)==(not clean.is_empty()) or target_actor_id==actor:
				return false
		2:
			if target_actor_id<=0 or target_actor_id==actor or not clean.is_empty() or not friends.has(target_actor_id):
				return false
		3:
			if target_actor_id!=0 or not clean.is_empty():
				return false
		_:
			return false
	return _queue_intent({
		"type":"friend","action":action,
		"target_actor_id":target_actor_id,"target_name":clean,
		"actor_id":actor,
		"map_epoch":int(current.get("map_epoch",0))
	})


func _queue_party_intent(
		action:int,party_id:int,target_actor_id:int,current:Dictionary,
		target_name:String="")->bool:
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_party_sequence",0))!=0
	):
		return false
	var clean_name:=target_name.strip_edges()
	if clean_name!=target_name or clean_name.to_utf8_buffer().size()>WireCodec.CHAT_MAX_SENDER_NAME_BYTES:
		return false
	if action==1:
		var name_target:=not clean_name.is_empty()
		if (target_actor_id>0)==name_target:
			return false
		if target_actor_id==int(current.get("actor_id",0)):
			return false
		if int(current.get("party_id",0))>0 and (
			party_id!=int(current.get("party_id",0)) or
			int(current.get("party_leader_actor_id",0))!=int(current.get("actor_id",0))
		):
			return false
	elif action in [2,3]:
		if not clean_name.is_empty():
			return false
		var invite:Dictionary=current.get("pending_party_invite",{})
		if (
			not int(current.get("party_id",0))==0 or invite.is_empty() or
			int(invite.get("party_id",0))!=party_id or
			int(invite.get("inviter_actor_id",0))!=target_actor_id
		):
			return false
	elif action==4:
		if not clean_name.is_empty():
			return false
		if party_id<=0 or party_id!=int(current.get("party_id",0)) or target_actor_id!=0:
			return false
	elif action in [5,6]:
		if not clean_name.is_empty():
			return false
		var members:Dictionary=current.get("party_members",{})
		if (
			party_id<=0 or party_id!=int(current.get("party_id",0)) or
			int(current.get("party_leader_actor_id",0))!=int(current.get("actor_id",0)) or
			target_actor_id<=0 or target_actor_id==int(current.get("actor_id",0)) or
			not members.has(target_actor_id)
		):
			return false
	else:
		return false
	return _queue_intent({
		"type":"party","action":action,"party_id":party_id,
		"target_actor_id":target_actor_id,"target_name":clean_name,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))
	})


func send_party_chat(message:String,bubble_type:int=0)->bool:
	var clean:=message.strip_edges()
	var bytes:=clean.to_utf8_buffer()
	var current:=snapshot()
	if (
		int(current.get("party_id",0))<=0 or clean.is_empty() or
		bytes.size()>WireCodec.CHAT_MAX_MESSAGE_BYTES or
		bubble_type<0 or bubble_type>5 or
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_whisper_sequence",0))!=0
	):
		return false
	return _queue_intent({
		"type":"party_chat","message":clean,"bubble_type":bubble_type,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))
	})


func open_quest(npc_spawn_id:int)->bool:
	var current:=snapshot()
	if (
		npc_spawn_id<=0 or
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		bool(current.get("player_dead",false)) or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_quest_sequence",0))!=0
	):
		return false
	return _queue_intent({
		"type":"quest_open","npc_spawn_id":npc_spawn_id,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))
	})


func choose_quest_choice(choice_id:int)->bool:
	var current:=snapshot()
	var view:Dictionary=current.get("quest_view",{})
	if (
		choice_id<=0 or view.is_empty() or
		int(current.get("pending_quest_sequence",0))!=0
	):
		return false
	var npc:=int(view.get("npc_spawn_id",0))
	var quest:=int(view.get("quest_id",0))
	var found:=false
	for raw in view.get("choices",[]):
		if int((raw as Dictionary).get("choice_id",0))==choice_id:
			found=true
			break
	if not found or npc<=0 or quest<=0:
		return false
	return _queue_intent({
		"type":"quest_choose","npc_spawn_id":npc,"quest_id":quest,
		"choice_id":choice_id,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))
	})


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
	if int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)) or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_enhancement_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"trade","action":action,
		"trade_id":trade_id,"target_actor_id":target_actor_id,
		"item_uid":item_uid,"money":money,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))})


func open_repair(npc_spawn_id: int) -> bool:
	var current := snapshot()
	if npc_spawn_id <= 0 or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_enhancement_sequence",0)) != 0 or int(current.get("trade_id",0)) != 0:
		return false
	return _queue_intent({"type":"repair_open","npc_spawn_id":npc_spawn_id,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func repair_item(item_uid: int) -> bool:
	var current := snapshot()
	var npc := int(current.get("active_repair_npc",0))
	var items: Dictionary = current.get("repair_items",{})
	if npc <= 0 or item_uid <= 0 or not bool(current.get("repair_ready",false)) or not items.has(item_uid) or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_enhancement_sequence",0)) != 0 or int(current.get("trade_id",0)) != 0:
		return false
	return _queue_intent({"type":"repair_item","npc_spawn_id":npc,"item_uid":item_uid,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func open_enhancement(npc_spawn_id: int) -> bool:
	var current := snapshot()
	if npc_spawn_id <= 0 or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_enhancement_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("trade_id",0)) != 0:
		return false
	return _queue_intent({"type":"enhancement_open","npc_spawn_id":npc_spawn_id,
		"actor_id":int(current.get("actor_id",0)),"map_epoch":int(current.get("map_epoch",0))})


func enhance_item(item_uid: int) -> bool:
	var current := snapshot()
	var npc := int(current.get("active_enhancement_npc",0))
	var items: Dictionary = current.get("enhancement_items",{})
	if npc <= 0 or item_uid <= 0 or not bool(current.get("enhancement_ready",false)) or not items.has(item_uid) or int(current.get("pending_enhancement_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("trade_id",0)) != 0:
		return false
	return _queue_intent({"type":"enhancement_item","npc_spawn_id":npc,
		"item_uid":item_uid,"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))})


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

func request_pvp_mode(action: int) -> bool:
	var current := snapshot()
	if action not in [0,1,2] or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or not bool(current.get("world_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)) or int(current.get("pending_pvp_sequence",0)) != 0:
		return false
	if action != 0 and bool(current.get("player_dead",false)):
		return false
	if action == 2 and bool(current.get("pvp_known",false)) and not bool(current.get("pvp_map_allows",false)):
		return false
	return _queue_intent({"type":"pvp_mode","action":action,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))})


func attack_player(target_actor_id: int) -> bool:
	var current := snapshot()
	var entities: Dictionary = current.get("entities",{})
	if target_actor_id <= 0 or target_actor_id == int(current.get("actor_id",0)) or not entities.has(target_actor_id) or int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or not bool(current.get("combat_available",false)) or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)) or not bool(current.get("pvp_known",false)) or not bool(current.get("pvp_map_allows",false)) or int(current.get("pending_pvp_attack_sequence",0)) != 0:
		return false
	return _queue_intent({"type":"pvp_attack","target_actor_id":target_actor_id,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))})


func cast_player_skill(skill_id: int,target_actor_id: int) -> bool:
	var current:=snapshot()
	var entities:Dictionary=current.get("entities",{})
	if skill_id<=0 or skill_id>100000000 or target_actor_id<=0 or target_actor_id==int(current.get("actor_id",0)) or not entities.has(target_actor_id) or int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or not bool(current.get("combat_available",false)) or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0))!=0 or int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or not bool(current.get("pvp_known",false)) or not bool(current.get("pvp_map_allows",false)) or int(current.get("pending_skill_sequence",0))!=0:
		return false
	return _queue_intent({"type":"cast_player_skill","skill_id":skill_id,
		"target_actor_id":target_actor_id,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))})


func gm_set_pvp_name(target_actor_id:int,name_state:int)->bool:
	var current:=snapshot()
	var entities:Dictionary=current.get("entities",{})
	if (
		not bool(current.get("gm_authorized",false)) or
		target_actor_id<=0 or target_actor_id==int(current.get("actor_id",0)) or
		not entities.has(target_actor_id) or name_state<0 or name_state>4 or
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_gm_sequence",0))!=0
	):
		return false
	return _queue_intent({"type":"gm_pvp_name",
		"target_actor_id":target_actor_id,"name_state":name_state,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))})


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


func pet_attack_monster(entity_id:int)->bool:
	var current:=snapshot()
	if (
		entity_id<=0 or bool(current.get("player_dead",false)) or
		int(current.get("active_pet_uid",0))<=0 or
		int(current.get("pending_pet_attack_sequence",0))!=0
	):
		return false
	return _queue_intent({
		"type":"pet_attack_monster","entity_id":entity_id,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))
	})


func pet_skill_monster(skill_id:int,entity_id:int)->bool:
	var current:=snapshot()
	if (
		skill_id<=0 or skill_id>100000000 or entity_id<=0 or
		bool(current.get("player_dead",false)) or
		int(current.get("active_pet_uid",0))<=0 or
		int(current.get("pending_pet_skill_sequence",0))!=0
	):
		return false
	return _queue_intent({
		"type":"pet_skill_monster","skill_id":skill_id,"entity_id":entity_id,
		"actor_id":int(current.get("actor_id",0)),
		"map_epoch":int(current.get("map_epoch",0))
	})


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

func snapshot_since(last_revision: int) -> Dictionary:
	# Version and immutable copy are captured under one lock. Checking a
	# separate revision getter before snapshot() would race with publication.
	_mutex.lock()
	if _published_revision == last_revision:
		_mutex.unlock()
		return {}
	var update := {
		"revision": _published_revision,
		"snapshot": _published.duplicate(true),
	}
	_mutex.unlock()
	return update

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
	if accepted:
		accepted = IntentAdmissionPolicy.allows(kind, _published, _intents)
	if accepted:
		_intent_token += 1
		var queued_intent := intent.duplicate(true)
		queued_intent["_token"] = _intent_token
		_intents.append(queued_intent)
	_mutex.unlock()
	return accepted

func _logic_loop() -> void:
	var auth_sent:=false
	var registration_sent:=false
	_mutex.lock()
	var registration_complete:=not _register_first
	_mutex.unlock()
	while not _should_stop():
		var transport_status: Dictionary = _transport.get_status()
		var transport_state := int(transport_status.state)
		if transport_state == Transport.STATE_FAILED:
			_last_transport_error = int(transport_status.terminal_error)
			if _can_auto_reconnect() and _start_reconnect_attempt():
				auth_sent=false
				registration_sent=false
				registration_complete=true
				continue
			_world.fail("transport_error_%d" % _last_transport_error)
			_publish()
			break
		if (
			transport_state==Transport.STATE_CONNECTED and
			not registration_complete and not registration_sent
		):
			var local_password:=_credential_string()
			var registration_frame:=WireCodec.encode_account_register_request(
				_username,local_password)
			local_password=""
			if registration_frame.is_empty():
				_world.fail("invalid_account_registration_input")
				_publish()
				break
			if _transport.send_frame(registration_frame):
				registration_sent=_world.mark_registration_sent()
				_publish()
		elif (
			transport_state==Transport.STATE_CONNECTED and
			registration_complete and not auth_sent
		):
			var local_password:=_credential_string()
			var auth_frame:=WireCodec.encode_auth_request(_username,local_password)
			local_password=""
			if auth_frame.is_empty():
				_world.fail("invalid_auth_input")
				_publish()
				break
			if _transport.send_frame(auth_frame):
				auth_sent=_world.mark_auth_sent()
				_publish()

		for event in _transport.drain_events(64):
			if str(event.get("type", "")) != "frame":
				continue
			var frame:PackedByteArray=event.get("frame",PackedByteArray())
			var opcode:=WireCodec.frame_opcode(frame)
			if not _world.apply_frame(frame):
				_publish()
				_transport.stop()
				_clear_credentials()
				return
			if (
				opcode==WireCodec.OPCODE_ACCOUNT_REGISTER_RESULT and
				int(_world.registration_status)==0
			):
				registration_complete=true
				_mutex.lock()
				_register_first=false
				_mutex.unlock()
			_publish()
			_flush_control_frames()

		_flush_control_frames()
		_process_reconnect_choose()
		_process_first_intent()
		_maybe_finish_world_entry()
		_maybe_query_pvp_mode()
		if int(_world.state) == WorldState.STATE_FAILED:
			if _can_retry_world_rejected() and _start_reconnect_attempt():
				auth_sent=false
				registration_sent=false
				registration_complete=true
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
	var error: int = _transport.start_encrypted(_host,_port)
	if error != OK:
		_last_transport_error = error
		return _start_reconnect_attempt() if attempt < MAX_RECONNECT_ATTEMPTS else false
	return true

func _process_reconnect_choose() -> void:
	# A reconnect is a new authenticated connection generation, but the active
	# ClientSession must still prove the same selected character. Reuse only the
	# zeroable in-memory proof captured by the successful selection intent; it is
	# never published and is cleared with the account credential on stop/failure.
	if int(_world.choose_status) in [3, 4, 5]:
		_mutex.lock()
		_reconnecting = false
		_mutex.unlock()
		return
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
	var character_password := _character_credential_string()
	var frame := WireCodec.encode_character_choose(character_id, character_password)
	character_password = ""
	if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_choose_sent(character_id):
		_publish()

func _maybe_query_pvp_mode() -> void:
	# Logic-thread-only reads. Public snapshots are for cross-thread/UI use;
	# constructing a deep copy on every idle poll scales with AOI population.
	# A trusted WorldReady ACK is required before a query is sent, and each
	# new MapEpoch gets exactly one query unless the connection restarts.
	if (
		_world.state != WorldState.STATE_IN_WORLD or
		not _world.world_ready or _world.resync_pending or
		_world.waiting_for_new_map or _world.pending_world_ready_ack_epoch != 0 or
		_world.last_world_ready_ack_epoch != _world.map_epoch
	):
		return
	if _world.pvp_known or _world.pvp_sequence != 0 or _world.pending_pvp_sequence != 0:
		return
	var frame := WireCodec.encode_pvp_mode_command(_world.map_epoch,1,0)
	if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_pvp_sent(1,0):
		_publish()


func _maybe_finish_world_entry() -> void:
	# Only the logic thread owns WorldState; never copy the entire world to
	# check four scalar fields at the idle polling cadence.
	if (
		_world.state != WorldState.STATE_IN_WORLD or not _world.world_ready or
		_world.pending_world_ready_ack_epoch != 0 or
		_world.last_world_ready_ack_epoch != _world.map_epoch
	):
		return
	_mutex.lock()
	var changed := not _has_entered_world
	_has_entered_world = true
	if _reconnecting:
		_reconnecting = false
		_reconnect_attempt = 0
		_reconnect_count += 1
		changed = true
	_mutex.unlock()
	if changed:
		_publish()

func _credential_string() -> String:
	_mutex.lock()
	var copy := _credential_bytes.duplicate()
	_mutex.unlock()
	var value := copy.get_string_from_utf8()
	copy.fill(0)
	return value

func _character_credential_string() -> String:
	_mutex.lock()
	var copy := _character_credential_bytes.duplicate()
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
	_clear_character_credential_locked()

func _clear_character_credential_locked() -> void:
	if not _character_credential_bytes.is_empty():
		_character_credential_bytes.fill(0)
		_character_credential_bytes.clear()

func _decorate_snapshot(value: Dictionary) -> Dictionary:
	value["reconnecting"] = _reconnecting
	value["reconnect_attempt"] = _reconnect_attempt
	value["reconnect_count"] = _reconnect_count
	value["max_reconnect_attempts"] = MAX_RECONNECT_ATTEMPTS
	value["last_transport_error"] = _last_transport_error
	return value

func _flush_control_frames() -> void:
	# ACK/resync checks run on the same logic thread as WorldState writes.
	# Read the current scalars directly, including after an ACK transition.
	var ack_epoch: int = int(_world.pending_world_ready_ack_epoch)
	var ack_tick: int = int(_world.pending_world_ready_ack_tick)
	if ack_epoch > 0 and ack_tick >= 0:
		var ack := WireCodec.encode_world_ready_ack(ack_epoch,ack_tick)
		if not ack.is_empty() and _transport.send_frame(ack):
			_world.mark_world_ready_ack_sent(ack_epoch,ack_tick)
			_publish()
	if _world.resync_pending and not _world.resync_request_sent:
		var epoch: int = int(_world.map_epoch)
		var request := WireCodec.encode_world_resync_request(epoch)
		if not request.is_empty() and _transport.send_frame(request):
			_world.mark_resync_request_sent(epoch)
			_publish()

func _process_character_create_intent(intent: Dictionary) -> void:
	if int(_world.state) != WorldState.STATE_SELECTING_CHARACTER:
		return
	var slot := int(intent.get("slot",-1))
	var character_name := str(intent.get("name",""))
	var appearance: Dictionary = intent.get("appearance",{})
	var creation: Dictionary = intent.get("creation",{})
	if not WorldState.allows_character_appearance(
			appearance,_world.character_creation_appearances):
		_world.fail("queued_character_appearance_no_longer_allowed")
		_publish()
		return
	if not creation.is_empty() and not WireCodec.valid_character_creation_settings(creation):
		_world.fail("queued_character_creation_settings_invalid")
		_publish()
		return
	var frame := WireCodec.encode_character_create(
		slot,character_name,appearance,creation)
	if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_character_create_sent(slot,character_name,appearance):
		if creation.has("password"):
			creation.erase("password")
		intent.erase("creation")
		_pop_intent()
		_publish()


func _process_character_manage_intent(intent: Dictionary) -> void:
	if int(_world.state) != WorldState.STATE_SELECTING_CHARACTER:
		return
	var character_id := int(intent.get("character_id",0))
	var frame := WireCodec.encode_character_manage(int(intent.action),character_id,
		str(intent.get("old_password","")),str(intent.get("new_password","")))
	if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_character_manage_sent(character_id):
		intent.erase("old_password")
		intent.erase("new_password")
		_pop_intent()
		_publish()

func _process_first_intent() -> void:
	var intent := _peek_intent()
	if intent.is_empty():
		return
	var kind := str(intent.get("type", ""))
	if kind == "create_character":
		_process_character_create_intent(intent)
	elif kind == "character_manage":
		_process_character_manage_intent(intent)
	elif kind == "choose":
		_process_character_choose_intent(intent)
	elif kind == "npc":
		_process_npc_intent(intent)

	elif kind=="player_shop":
		_process_player_shop_intent(intent)


	elif kind in ["quest_open","quest_choose"]:
		_process_quest_intent(intent, kind)


	elif kind in ["nearby_chat","party_chat","whisper"]:
		_process_chat_intent(intent, kind)


	elif kind=="party":
		_process_party_intent(intent)


	elif kind=="clan":
		_process_clan_intent(intent)
	elif kind=="friend":
		_process_friend_intent(intent)
	elif kind=="pet":
		_process_pet_intent(intent)
	elif kind=="mecha":
		_process_mecha_intent(intent)
	elif kind=="attribute_allocate":
		_process_attribute_allocate_intent(intent)


	elif kind == "gm_pvp_name":
		_process_gm_pvp_name_intent(intent)


	elif kind == "pvp_mode":
		_process_pvp_mode_intent(intent)


	elif kind == "pvp_attack":
		_process_pvp_attack_intent(intent)


	elif kind == "cast_player_skill":
		_process_player_skill_intent(intent)


	elif kind == "trade":
		_process_trade_intent(intent)


	elif kind in ["repair_open","repair_item"]:
		_process_repair_intent(intent, kind)


	elif kind in ["enhancement_open","enhancement_item"]:
		_process_enhancement_intent(intent, kind)


	elif kind in ["bank_open","bank_deposit","bank_withdraw"]:
		_process_bank_intent(intent, kind)


	elif kind in ["equipment_equip","equipment_unequip"]:
		_process_equipment_intent(intent, kind)


	elif kind in ["storage_open","storage_deposit","storage_withdraw"]:
		_process_storage_intent(intent, kind)


	elif kind in ["shop_open","shop_buy","shop_sell"]:
		_process_shop_intent(intent, kind)


	elif kind == "use_recovery_item":
		_process_recovery_item_intent(intent)


	elif kind == "discard_inventory_item":
		_process_inventory_discard_intent(intent)


	elif kind == "pickup_ground_drop":
		_process_pickup_intent(intent)


	elif kind == "respawn":
		_process_respawn_intent(intent)


	elif kind == "cast_skill":
		_process_monster_skill_intent(intent)


	elif kind == "attack_monster":
		_process_attack_monster_intent(intent)
	elif kind == "pet_attack_monster":
		_process_pet_attack_monster_intent(intent)
	elif kind == "pet_skill_monster":
		_process_pet_skill_monster_intent(intent)
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

func _process_player_shop_intent(intent: Dictionary) -> void:
	var current:Dictionary=_world.snapshot()
	if (
		int(intent.get("actor_id",0))!=int(current.get("actor_id",0)) or
		int(intent.get("map_epoch",0))!=int(current.get("map_epoch",0))
	):
		_pop_intent()
		return
	var action:=int(intent.get("action",0))
	var seller:=int(intent.get("seller_actor_id",0))
	var listing:=int(intent.get("listing_id",0))
	var uid:=int(intent.get("item_uid",0))
	var price:=int(intent.get("price",0))
	var entities:Dictionary=current.get("entities",{})
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		bool(current.get("player_dead",false)) or
		not bool(current.get("world_ready",false)) or
		not bool(current.get("inventory_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_player_shop_sequence",0))!=0 or
		int(current.get("trade_id",0))!=0
	):
		return
	var valid:=false
	match action:
		1:
			valid=(
				seller==0 and listing==0 and uid>0 and price>0 and
				price<=WireCodec.MAX_MONEY and
				(current.get("inventory_items",{}) as Dictionary).has(uid)
			)
		2:
			valid=seller==0 and listing>0 and uid==0 and price==0
		3:
			valid=(
				seller>0 and seller!=int(current.get("actor_id",0)) and
				entities.has(seller) and listing==0 and uid==0 and price==0
			)
		4:
			valid=(
				seller>0 and seller!=int(current.get("actor_id",0)) and
				entities.has(seller) and listing>0 and uid==0 and price==0
			)
	if not valid:
		_pop_intent()
		return
	var sequence:=int(current.get("player_shop_sequence",0))+1
	var frame:=WireCodec.encode_player_shop_command(
		int(current.get("map_epoch",0)),sequence,action,
		seller,listing,uid,price)
	if frame.is_empty():
		_pop_intent()
		return
	if (
		_transport.send_frame(frame) and
		_world.mark_player_shop_sent(
			sequence,action,seller,listing,uid,price)
	):
		_pop_intent()
		_publish()


func _process_character_choose_intent(intent: Dictionary) -> void:
	if int(_world.state) != WorldState.STATE_SELECTING_CHARACTER:
		return
	var character_id := int(intent.character_id)
	var character_password := str(intent.get("character_password", ""))
	var frame := WireCodec.encode_character_choose(character_id, character_password)
	if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_choose_sent(character_id):
		var character_credential := character_password.to_utf8_buffer()
		character_password = ""
		intent.erase("character_password")
		_mutex.lock()
		_selected_character_id = character_id
		_clear_character_credential_locked()
		_character_credential_bytes = character_credential
		_mutex.unlock()
		_pop_intent()
		_publish()


func _process_npc_intent(intent: Dictionary) -> void:
	var current: Dictionary = _world.snapshot()
	if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.player_dead) or not bool(current.world_ready) or bool(current.resync_pending) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or bool(current.waiting_for_new_map) or int(current.pending_npc_sequence) != 0:
		return
	var sequence := int(current.npc_interaction_sequence) + 1
	var frame := WireCodec.encode_npc_interact(int(current.map_epoch), sequence, int(intent.spawn_id))
	if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_npc_interact_sent(sequence):
		_pop_intent()
		_publish()


func _process_quest_intent(intent: Dictionary, kind: String) -> void:
	var current:Dictionary=_world.snapshot()
	if (
		int(intent.get("actor_id",0))!=int(current.get("actor_id",0)) or
		int(intent.get("map_epoch",0))!=int(current.get("map_epoch",0))
	):
		_pop_intent()
		return
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		bool(current.get("player_dead",false)) or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_quest_sequence",0))!=0
	):
		return
	var sequence:=int(current.get("quest_sequence",0))+1
	var npc:=int(intent.get("npc_spawn_id",0))
	var quest_id:=int(intent.get("quest_id",0))
	var choice_id:=int(intent.get("choice_id",0))
	var quest_kind:=1 if kind=="quest_open" else 2
	var frame:=PackedByteArray()
	if quest_kind==1:
		frame=WireCodec.encode_quest_open(
			int(current.get("map_epoch",0)),sequence,npc)
	else:
		frame=WireCodec.encode_quest_choose(
			int(current.get("map_epoch",0)),sequence,npc,quest_id,choice_id)
	if frame.is_empty():
		_pop_intent()
		return
	if (
		_transport.send_frame(frame) and
		_world.mark_quest_sent(
			sequence,quest_kind,npc,quest_id,choice_id)
	):
		_pop_intent()
		_publish()


func _process_chat_intent(intent: Dictionary, kind: String) -> void:
	var current:Dictionary=_world.snapshot()
	if (
		int(intent.get("actor_id",0))!=int(current.get("actor_id",0)) or
		int(intent.get("map_epoch",0))!=int(current.get("map_epoch",0))
	):
		_pop_intent()
		return
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_whisper_sequence",0))!=0
	):
		return
	var message:=str(intent.get("message",""))
	var bubble:=int(intent.get("bubble_type",0))
	var sequence:=int(current.get("chat_sequence",0))+1
	var frame:=PackedByteArray()
	if kind in ["nearby_chat","party_chat"]:
		var channel:=0 if kind=="nearby_chat" else 3
		if channel==3 and int(current.get("party_id",0))<=0:
			_pop_intent()
			return
		frame=WireCodec.encode_chat_command(
			int(current.get("map_epoch",0)),sequence,channel,bubble,message)
		if frame.is_empty():
			_pop_intent()
			return
		if _transport.send_frame(frame) and _world.mark_chat_sent(
			sequence,channel,bubble,message):
			_pop_intent()
			_publish()
		return
	var target:=int(intent.get("target_actor_id",0))
	var target_name:=str(intent.get("target_name",""))
	var by_name:=not target_name.is_empty()
	if (
		target<0 or (target>0)==by_name or
		(target>0 and target==int(current.get("actor_id",0)))
	):
		_pop_intent()
		return
	frame=WireCodec.encode_whisper_command(
		int(current.get("map_epoch",0)),sequence,target,bubble,message,target_name)
	if frame.is_empty():
		_pop_intent()
		return
	if (
		_transport.send_frame(frame) and
		_world.mark_whisper_sent(sequence,target,bubble,message,target_name)
	):
		_pop_intent()
		_publish()


func _process_party_intent(intent: Dictionary) -> void:
	var current:Dictionary=_world.snapshot()
	if (
		int(intent.get("actor_id",0))!=int(current.get("actor_id",0)) or
		int(intent.get("map_epoch",0))!=int(current.get("map_epoch",0))
	):
		_pop_intent()
		return
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_party_sequence",0))!=0
	):
		return
	var action:=int(intent.get("action",0))
	var request_party_id:=int(intent.get("party_id",0))
	var target:=int(intent.get("target_actor_id",0))
	var target_name:=str(intent.get("target_name",""))
	var sequence:=int(current.get("party_sequence",0))+1
	var frame:=WireCodec.encode_party_command(
		int(current.get("map_epoch",0)),sequence,action,request_party_id,
		target,target_name)
	if frame.is_empty():
		_pop_intent()
		return
	if (
		_transport.send_frame(frame) and
		_world.mark_party_sent(
			sequence,action,request_party_id,target,target_name)
	):
		_pop_intent()
		_publish()


func _process_clan_intent(intent: Dictionary) -> void:
	var current:Dictionary=_world.snapshot()
	if (
		int(intent.get("actor_id",0))!=int(current.get("actor_id",0)) or
		int(intent.get("map_epoch",0))!=int(current.get("map_epoch",0))
	):
		_pop_intent()
		return
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_clan_sequence",0))!=0
	):
		return
	var action:=int(intent.get("action",0))
	var request_clan_id:=int(intent.get("clan_id",0))
	var clan_name:=str(intent.get("name",""))
	var target_actor_id:=int(intent.get("target_actor_id",0))
	var sequence:=int(current.get("clan_sequence",0))+1
	var frame:=WireCodec.encode_clan_command(
		int(current.get("map_epoch",0)),sequence,action,request_clan_id,clan_name,target_actor_id)
	if frame.is_empty():
		_pop_intent()
		return
	if (
		_transport.send_frame(frame) and
		_world.mark_clan_sent(sequence,action,request_clan_id,clan_name,target_actor_id)
	):
		_pop_intent()
		_publish()


func _process_friend_intent(intent:Dictionary)->void:
	var current:Dictionary=_world.snapshot()
	if (
		int(intent.get("actor_id",0))!=int(current.get("actor_id",0)) or
		int(intent.get("map_epoch",0))!=int(current.get("map_epoch",0))
	):
		_pop_intent()
		return
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_friend_sequence",0))!=0
	):
		return
	var action:=int(intent.get("action",0))
	var target_actor_id:=int(intent.get("target_actor_id",0))
	var target_name:=str(intent.get("target_name",""))
	var sequence:=int(current.get("friend_sequence",0))+1
	var frame:=WireCodec.encode_friend_command(
		int(current.get("map_epoch",0)),sequence,action,target_actor_id,target_name)
	if frame.is_empty():
		_pop_intent()
		return
	if (
		_transport.send_frame(frame) and
		_world.mark_friend_sent(sequence,action,target_actor_id,target_name)
	):
		_pop_intent()
		_publish()


func _process_pet_intent(intent:Dictionary)->void:
	var current:Dictionary=_world.snapshot()
	if (
		int(intent.get("actor_id",0))!=int(current.get("actor_id",0)) or
		int(intent.get("map_epoch",0))!=int(current.get("map_epoch",0))
	):
		_pop_intent()
		return
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_pet_sequence",0))!=0
	):
		return
	var action:=int(intent.get("action",0))
	var pet_uid:=int(intent.get("pet_uid",0))
	var item_uid:=int(intent.get("item_uid",0))
	var sequence:=int(current.get("pet_sequence",0))+1
	var frame:=WireCodec.encode_pet_command(
		int(current.get("map_epoch",0)),sequence,action,pet_uid,item_uid)
	if frame.is_empty():
		_pop_intent()
		return
	if _transport.send_frame(frame) and _world.mark_pet_sent(
		sequence,action,pet_uid,item_uid
	):
		_pop_intent()
		_publish()

func _process_attribute_allocate_intent(intent:Dictionary)->void:
	var current:Dictionary=_world.snapshot()
	if (
		int(intent.get("actor_id",0))!=int(current.get("actor_id",0)) or
		int(intent.get("map_epoch",0))!=int(current.get("map_epoch",0))
	):
		_pop_intent()
		return
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("player_available_attribute_points",0))<=0
	):
		return
	var next_sequence:=_attribute_sequence+1
	if next_sequence<=0:
		next_sequence=1
	var frame:=WireCodec.encode_player_attribute_allocate(
		next_sequence,int(intent.get("attribute",0)))
	if frame.is_empty():
		_pop_intent()
		return
	if _transport.send_frame(frame):
		_attribute_sequence=next_sequence
		_pop_intent()
		_publish()

func _process_mecha_intent(intent:Dictionary)->void:
	var current:Dictionary=_world.snapshot()
	if (
		int(intent.get("actor_id",0))!=int(current.get("actor_id",0)) or
		int(intent.get("map_epoch",0))!=int(current.get("map_epoch",0))
	):
		_pop_intent()
		return
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_mecha_sequence",0))!=0
	):
		return
	var action:=int(intent.get("action",0))
	var mecha_uid:=int(intent.get("mecha_uid",0))
	var item_uid:=int(intent.get("item_uid",0))
	var sequence:=int(current.get("mecha_sequence",0))+1
	var frame:=WireCodec.encode_mecha_command(
		int(current.get("map_epoch",0)),sequence,action,mecha_uid,item_uid)
	if frame.is_empty():
		_pop_intent()
		return
	if _transport.send_frame(frame) and _world.mark_mecha_sent(
		sequence,action,mecha_uid,item_uid
	):
		_pop_intent()
		_publish()


# PvP, economic and inventory requests share the same ClientSession FIFO consumer.
func _process_gm_pvp_name_intent(intent: Dictionary) -> void:
	var current:Dictionary=_world.snapshot()
	if (
		int(intent.get("actor_id",0))!=int(current.get("actor_id",0)) or
		int(intent.get("map_epoch",0))!=int(current.get("map_epoch",0))
	):
		_pop_intent()
		return
	var target:=int(intent.get("target_actor_id",0))
	var name_state:=int(intent.get("name_state",-1))
	var entities:Dictionary=current.get("entities",{})
	if (
		not bool(current.get("gm_authorized",false)) or
		target<=0 or target==int(current.get("actor_id",0)) or
		not entities.has(target) or name_state<0 or name_state>4
	):
		_pop_intent()
		return
	if (
		int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or
		not bool(current.get("world_ready",false)) or
		bool(current.get("resync_pending",false)) or
		bool(current.get("waiting_for_new_map",false)) or
		int(current.get("pending_world_ready_ack_epoch",0))!=0 or
		int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or
		int(current.get("pending_gm_sequence",0))!=0
	):
		return
	var sequence:=int(current.get("gm_sequence",0))+1
	var frame:=WireCodec.encode_gm_pvp_name_command(
		int(current.get("map_epoch",0)),sequence,target,name_state)
	if frame.is_empty():
		_pop_intent()
		return
	if (
		_transport.send_frame(frame) and
		_world.mark_gm_pvp_name_sent(sequence,target,name_state)
	):
		_pop_intent()
		_publish()


func _process_pvp_mode_intent(intent: Dictionary) -> void:
	var current: Dictionary = _world.snapshot()
	if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
		_pop_intent()
		return
	var action := int(intent.get("action",-1))
	if action not in [0,1,2] or (action != 0 and bool(current.get("player_dead",false))) or (action == 2 and bool(current.get("pvp_known",false)) and not bool(current.get("pvp_map_allows",false))):
		_pop_intent()
		return
	if int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or not bool(current.get("world_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)) or int(current.get("pending_pvp_sequence",0)) != 0:
		return
	var sequence := int(current.get("pvp_sequence",0))+1
	var frame := WireCodec.encode_pvp_mode_command(int(current.get("map_epoch",0)),sequence,action)
	if frame.is_empty():
		_pop_intent()
		return
	if _transport.send_frame(frame) and _world.mark_pvp_sent(sequence,action):
		_pop_intent()
		_publish()


func _process_pvp_attack_intent(intent: Dictionary) -> void:
	var current: Dictionary = _world.snapshot()
	if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
		_pop_intent()
		return
	var target := int(intent.get("target_actor_id",0))
	var entities: Dictionary = current.get("entities",{})
	if target <= 0 or target == int(current.get("actor_id",0)) or not entities.has(target):
		_pop_intent()
		return
	if int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or not bool(current.get("combat_available",false)) or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)) or not bool(current.get("pvp_known",false)) or not bool(current.get("pvp_map_allows",false)) or int(current.get("pending_pvp_attack_sequence",0)) != 0:
		return
	var sequence := int(current.get("pvp_attack_sequence",0))+1
	var frame := WireCodec.encode_pvp_attack(int(current.get("map_epoch",0)),sequence,target)
	if frame.is_empty():
		_pop_intent()
		return
	if _transport.send_frame(frame) and _world.mark_pvp_attack_sent(sequence,target):
		_pop_intent()
		_publish()


func _process_player_skill_intent(intent: Dictionary) -> void:
	var current:Dictionary=_world.snapshot()
	if int(intent.get("actor_id",0))!=int(current.get("actor_id",0)) or int(intent.get("map_epoch",0))!=int(current.get("map_epoch",0)):
		_pop_intent()
		return
	var target:=int(intent.get("target_actor_id",0))
	var skill_id:=int(intent.get("skill_id",0))
	var entities:Dictionary=current.get("entities",{})
	if target<=0 or target==int(current.get("actor_id",0)) or not entities.has(target) or skill_id<=0 or skill_id>100000000:
		_pop_intent()
		return
	if int(current.get("state",-1))!=WorldState.STATE_IN_WORLD or not bool(current.get("combat_available",false)) or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0))!=0 or int(current.get("last_world_ready_ack_epoch",0))!=int(current.get("map_epoch",0)) or not bool(current.get("pvp_known",false)) or not bool(current.get("pvp_map_allows",false)) or int(current.get("pending_skill_sequence",0))!=0:
		return
	var sequence:=int(current.get("skill_sequence",0))+1
	var frame:=WireCodec.encode_pvp_skill(
		int(current.get("map_epoch",0)),sequence,skill_id,target)
	if frame.is_empty():
		_pop_intent()
		return
	if _transport.send_frame(frame) and _world.mark_skill_sent(sequence,1,target):
		_pop_intent()
		_publish()


func _process_trade_intent(intent: Dictionary) -> void:
	var current: Dictionary = _world.snapshot()
	if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
		_pop_intent()
		return
	if int(current.get("state",-1)) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)) or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_enhancement_sequence",0)) != 0:
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


func _process_repair_intent(intent: Dictionary, kind: String) -> void:
	var current: Dictionary = _world.snapshot()
	if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
		_pop_intent()
		return
	if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)) or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_enhancement_sequence",0)) != 0 or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_shop_sequence",0)) != 0 or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_equipment_sequence",0)) != 0 or int(current.get("pending_recovery_sequence",0)) != 0 or int(current.get("pending_discard_sequence",0)) != 0:
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


func _process_enhancement_intent(intent: Dictionary, kind: String) -> void:
	var current: Dictionary = _world.snapshot()
	if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
		_pop_intent()
		return
	if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or not bool(current.get("inventory_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)) or int(current.get("pending_enhancement_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_shop_sequence",0)) != 0 or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_equipment_sequence",0)) != 0 or int(current.get("pending_recovery_sequence",0)) != 0 or int(current.get("pending_discard_sequence",0)) != 0 or int(current.get("trade_id",0)) != 0:
		return
	var sequence := int(current.get("enhancement_sequence",0))+1
	var npc := int(intent.get("npc_spawn_id",0))
	var uid := int(intent.get("item_uid",0))
	var frame := PackedByteArray()
	var enhancement_kind := 0
	if kind == "enhancement_open":
		enhancement_kind = 1
		frame = WireCodec.encode_enhancement_open(
			int(current.map_epoch),sequence,npc)
	else:
		enhancement_kind = 2
		if not bool(current.get("enhancement_ready",false)) or int(current.get("active_enhancement_npc",0)) != npc or not (current.get("enhancement_items",{}) as Dictionary).has(uid):
			_pop_intent()
			return
		frame = WireCodec.encode_enhancement_item(
			int(current.map_epoch),sequence,npc,uid)
	if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_enhancement_sent(sequence,enhancement_kind,npc,uid):
		_pop_intent()
		_publish()


func _process_bank_intent(intent: Dictionary, kind: String) -> void:
	var current: Dictionary = _world.snapshot()
	if int(intent.get("actor_id",0)) != int(current.get("actor_id",0)) or int(intent.get("map_epoch",0)) != int(current.get("map_epoch",0)):
		_pop_intent()
		return
	if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.get("player_dead",false)) or not bool(current.get("world_ready",false)) or bool(current.get("resync_pending",false)) or bool(current.get("waiting_for_new_map",false)) or int(current.get("pending_world_ready_ack_epoch",0)) != 0 or int(current.get("last_world_ready_ack_epoch",0)) != int(current.get("map_epoch",0)) or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_enhancement_sequence",0)) != 0 or int(current.get("pending_shop_sequence",0)) != 0 or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_equipment_sequence",0)) != 0 or int(current.get("pending_recovery_sequence",0)) != 0 or int(current.get("pending_discard_sequence",0)) != 0:
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


func _process_equipment_intent(intent: Dictionary, kind: String) -> void:
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


func _process_storage_intent(intent: Dictionary, kind: String) -> void:
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


func _process_shop_intent(intent: Dictionary, kind: String) -> void:
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


func _process_recovery_item_intent(intent: Dictionary) -> void:
	var current: Dictionary = _world.snapshot()
	if int(current.state) != WorldState.STATE_IN_WORLD or not bool(current.combat_available) or bool(current.player_dead) or not bool(current.inventory_ready) or not bool(current.world_ready) or bool(current.resync_pending) or bool(current.waiting_for_new_map) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or int(current.pending_recovery_sequence) != 0 or int(current.pending_discard_sequence) != 0 or int(current.get("pending_shop_sequence",0)) != 0 or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_equipment_sequence",0)) != 0 or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_enhancement_sequence",0)) != 0 or int(current.get("trade_id",0)) != 0:
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


func _process_inventory_discard_intent(intent: Dictionary) -> void:
	var current: Dictionary = _world.snapshot()
	if int(current.state) != WorldState.STATE_IN_WORLD or not bool(current.inventory_ready) or not bool(current.world_ready) or bool(current.resync_pending) or bool(current.waiting_for_new_map) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or int(current.pending_discard_sequence) != 0 or int(current.pending_recovery_sequence) != 0 or int(current.get("pending_shop_sequence",0)) != 0 or int(current.get("pending_storage_sequence",0)) != 0 or int(current.get("pending_equipment_sequence",0)) != 0 or int(current.get("pending_bank_sequence",0)) != 0 or int(current.get("pending_repair_sequence",0)) != 0 or int(current.get("pending_enhancement_sequence",0)) != 0 or int(current.get("trade_id",0)) != 0:
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


func _process_pickup_intent(intent: Dictionary) -> void:
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


func _process_respawn_intent(intent: Dictionary) -> void:
	var current: Dictionary = _world.snapshot()
	if int(current.state) != WorldState.STATE_IN_WORLD or not bool(current.player_dead) or not bool(current.combat_available) or not bool(current.world_ready) or bool(current.resync_pending) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or bool(current.waiting_for_new_map):
		return
	var sequence := int(current.respawn_sequence) + 1
	var frame := WireCodec.encode_player_respawn(int(current.map_epoch),sequence)
	if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_player_respawn_sent(sequence):
		_pop_intent()
		_publish()


func _process_monster_skill_intent(intent: Dictionary) -> void:
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


func _process_stop_movement_intent(intent: Dictionary) -> void:
	# Called on the sole WorldState logic thread: no cross-thread snapshot needed.
	var current: RefCounted = _world
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


func _process_pet_attack_monster_intent(intent:Dictionary)->void:
	var current:RefCounted=_world
	var token:=int(intent.get("_token",0))
	if token<=0 or not _first_intent_matches(token):
		return
	if (
		int(intent.get("actor_id",0))!=int(current.actor_id) or
		int(intent.get("map_epoch",0))!=int(current.map_epoch)
	):
		_pop_intent_token(token)
		return
	if int(current.state)!=WorldState.STATE_IN_WORLD or bool(current.player_dead):
		_pop_intent_token(token)
		return
	if (
		not bool(current.world_ready) or bool(current.resync_pending) or
		int(current.pending_world_ready_ack_epoch)!=0 or
		int(current.last_world_ready_ack_epoch)!=int(current.map_epoch) or
		bool(current.waiting_for_new_map)
	):
		return
	var target_id:=int(intent.get("entity_id",0))
	var pet_uid:=int(current.active_pet_uid)
	if target_id<=0 or pet_uid<=0:
		_pop_intent_token(token)
		return
	var monsters:Dictionary=current.monsters
	if not monsters.has(target_id):
		_pop_intent_token(token)
		return
	var target:Dictionary=monsters[target_id]
	if int(target.get("current_hp",0))<=0 or int(target.get("state",0))==4:
		_pop_intent_token(token)
		return
	if int(current.pending_pet_attack_sequence)!=0:
		return
	var sequence:=int(current.pet_attack_sequence)+1
	var frame:=WireCodec.encode_pet_attack(
		int(current.map_epoch),sequence,pet_uid,target_id)
	if frame.is_empty():
		_pop_intent_token(token)
		return
	if _transport.send_frame(frame) and _world.mark_pet_attack_sent(
		sequence,pet_uid,target_id):
		_pop_intent_token(token)
		_publish()


func _process_pet_skill_monster_intent(intent:Dictionary)->void:
	var current:RefCounted=_world
	var token:=int(intent.get("_token",0))
	if token<=0 or not _first_intent_matches(token):
		return
	if (
		int(intent.get("actor_id",0))!=int(current.actor_id) or
		int(intent.get("map_epoch",0))!=int(current.map_epoch)
	):
		_pop_intent_token(token)
		return
	if int(current.state)!=WorldState.STATE_IN_WORLD or bool(current.player_dead):
		_pop_intent_token(token)
		return
	if (
		not bool(current.world_ready) or bool(current.resync_pending) or
		int(current.pending_world_ready_ack_epoch)!=0 or
		int(current.last_world_ready_ack_epoch)!=int(current.map_epoch) or
		bool(current.waiting_for_new_map)
	):
		return
	var target_id:=int(intent.get("entity_id",0))
	var skill_id:=int(intent.get("skill_id",0))
	var pet_uid:=int(current.active_pet_uid)
	if target_id<=0 or skill_id<=0 or skill_id>100000000 or pet_uid<=0:
		_pop_intent_token(token)
		return
	var monsters:Dictionary=current.monsters
	if not monsters.has(target_id):
		_pop_intent_token(token)
		return
	var target:Dictionary=monsters[target_id]
	if int(target.get("current_hp",0))<=0 or int(target.get("state",0))==4:
		_pop_intent_token(token)
		return
	if int(current.pending_pet_skill_sequence)!=0:
		return
	var sequence:=int(current.pet_skill_sequence)+1
	var frame:=WireCodec.encode_pet_skill(
		int(current.map_epoch),sequence,pet_uid,skill_id,target_id)
	if frame.is_empty():
		_pop_intent_token(token)
		return
	if _transport.send_frame(frame) and _world.mark_pet_skill_sent(
		sequence,pet_uid,skill_id,target_id):
		_pop_intent_token(token)
		_publish()


func _cancel_attack_chase(token: int, intent: Dictionary) -> void:
	# If this attack intent already started an authoritative approach route, do
	# not merely forget the chase when its target dies or leaves AOI. Replace the
	# same FIFO slot with the existing Server-authoritative stop barrier so the
	# player does not keep walking toward a stale monster position.
	if str(intent.get("phase","")) == "await_move" and int(intent.get("move_sequence",0)) > 0:
		if _replace_first_intent(token,{"type":"stop_move","phase":"send_stop"}):
			return
	_pop_intent_token(token)


func _process_attack_monster_intent(intent: Dictionary) -> void:
	# Called on the sole WorldState logic thread: no cross-thread snapshot needed.
	var current: RefCounted = _world
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
		_cancel_attack_chase(token,intent)
		return
	if intent.has("target_missing_since_ms"):
		var restored := intent.duplicate(true)
		restored.erase("target_missing_since_ms")
		if not _replace_first_intent(token,restored):
			return
		intent = restored
	var target: Dictionary = monsters[target_id]
	if int(target.get("current_hp",0)) <= 0 or int(target.get("state",0)) == 4:
		_cancel_attack_chase(token,intent)
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
	_published_revision += 1
	_mutex.unlock()
