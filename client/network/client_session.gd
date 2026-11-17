extends RefCounted

const Transport = preload("res://network/client_transport.gd")
const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://network/world_state.gd")

const MAX_INTENTS := 128
const LOGIC_SLEEP_MS := 2
const MAX_RECONNECT_ATTEMPTS := 3
const RECONNECT_BACKOFF_MS := [100,250,500]

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

func choose_character(character_id: int) -> bool:
	if character_id <= 0:
		return false
	return _queue_intent({"type": "choose", "character_id": character_id})

func move_target(target_x: int, target_y: int) -> bool:
	if bool(snapshot().get("player_dead",false)):
		return false
	return _queue_intent({"type": "move", "x": target_x, "y": target_y})

func use_recovery_item(item_uid: int) -> bool:
	var current := snapshot()
	if item_uid <= 0 or not bool(current.get("inventory_ready",false)) or not bool(current.get("combat_available",false)) or bool(current.get("player_dead",false)) or int(current.get("pending_recovery_sequence",0)) != 0 or not (current.get("inventory_items",{}) as Dictionary).has(item_uid):
		return false
	return _queue_intent({"type":"use_recovery_item","item_uid":item_uid})


func pickup_ground_drop(drop_id: int) -> bool:
	var current := snapshot()
	if drop_id <= 0 or bool(current.get("player_dead",false)) or int(current.get("pending_pickup_sequence",0)) != 0 or not (current.get("ground_drops",{}) as Dictionary).has(drop_id):
		return false
	return _queue_intent({"type":"pickup_ground_drop","drop_id":drop_id})


func respawn() -> bool:
	var current := snapshot()
	if not bool(current.get("combat_available",false)) or not bool(current.get("player_dead",false)):
		return false
	return _queue_intent({"type":"respawn"})

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
	if accepted and str(intent.get("type","")) == "use_recovery_item":
		for queued in _intents:
			if str(queued.get("type","")) == "use_recovery_item":
				accepted = false
				break
	if accepted and str(intent.get("type","")) == "pickup_ground_drop":
		# Reject repeated clicks while the first one is still in the intent
		# queue, before it acquires a pending authoritative sequence.
		for queued in _intents:
			if str(queued.get("type","")) == "pickup_ground_drop":
				accepted = false
				break
	if accepted:
		_intents.append(intent)
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
	if kind == "choose":
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
	elif kind == "use_recovery_item":
		var current: Dictionary = _world.snapshot()
		if int(current.state) != WorldState.STATE_IN_WORLD or not bool(current.combat_available) or bool(current.player_dead) or not bool(current.inventory_ready) or not bool(current.world_ready) or bool(current.resync_pending) or bool(current.waiting_for_new_map) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or int(current.pending_recovery_sequence) != 0:
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
	elif kind == "attack_monster":
		var current: Dictionary = _world.snapshot()
		if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.player_dead) or not bool(current.world_ready) or bool(current.resync_pending) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or bool(current.waiting_for_new_map):
			return
		var sequence := int(current.attack_sequence) + 1
		var frame := WireCodec.encode_normal_attack(int(current.map_epoch),sequence,int(intent.entity_id))
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_normal_attack_sent(sequence):
			_pop_intent()
			_publish()
	elif kind == "move":
		var current: Dictionary = _world.snapshot()
		if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.player_dead) or not bool(current.world_ready) or bool(current.resync_pending) or int(current.pending_world_ready_ack_epoch) != 0 or int(current.last_world_ready_ack_epoch) != int(current.map_epoch) or bool(current.waiting_for_new_map):
			return
		var sequence := int(current.movement_sequence) + 1
		var frame := WireCodec.encode_move_target(int(current.map_epoch), sequence, int(intent.x), int(intent.y))
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_move_sent(sequence):
			_pop_intent()
			_publish()
	else:
		_world.fail("unknown_intent")
		_publish()

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
