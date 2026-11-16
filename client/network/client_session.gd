extends RefCounted

const Transport = preload("res://network/client_transport.gd")
const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://network/world_state.gd")

const MAX_INTENTS := 128
const LOGIC_SLEEP_MS := 2

var _transport: RefCounted
var _world: RefCounted = WorldState.new()
var _thread := Thread.new()
var _mutex := Mutex.new()
var _stop_requested := false
var _running := false
var _intents: Array[Dictionary] = []
var _published: Dictionary = _world.snapshot()

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
	_published = _world.snapshot()
	_running = true
	_mutex.unlock()
	var error: int = _transport.start_secure(host, port, common_name, trusted_chain)
	if error != OK:
		_mutex.lock()
		_running = false
		_mutex.unlock()
		return error
	_thread = Thread.new()
	error = _thread.start(_logic_loop.bind(username, password))
	if error != OK:
		_transport.stop()
		_mutex.lock()
		_running = false
		_mutex.unlock()
	return error

func choose_character(character_id: int) -> bool:
	if character_id <= 0:
		return false
	return _queue_intent({"type": "choose", "character_id": character_id})

func move_target(target_x: int, target_y: int) -> bool:
	return _queue_intent({"type": "move", "x": target_x, "y": target_y})

func npc_interact(spawn_id: int) -> bool:
	# The client names only a map-local NPC; Server provides the destination.
	if spawn_id <= 0 or spawn_id > 0x7fffffff:
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
	_mutex.unlock()

func _queue_intent(intent: Dictionary) -> bool:
	_mutex.lock()
	var accepted := _running and not _stop_requested and _intents.size() < MAX_INTENTS
	if accepted:
		_intents.append(intent)
	_mutex.unlock()
	return accepted

func _logic_loop(username: String, password: String) -> void:
	var auth_sent := false
	var local_password := password
	while not _should_stop():
		var transport_status: Dictionary = _transport.get_status()
		var transport_state := int(transport_status.state)
		if transport_state == Transport.STATE_FAILED:
			_world.fail("transport_error_%d" % int(transport_status.terminal_error))
			_publish()
			break
		if transport_state == Transport.STATE_CONNECTED and not auth_sent:
			var auth_frame := WireCodec.encode_auth_request(username, local_password)
			if auth_frame.is_empty():
				_world.fail("invalid_auth_input")
				_publish()
				break
			if _transport.send_frame(auth_frame):
				auth_sent = _world.mark_auth_sent()
				local_password = ""
				_publish()

		for event in _transport.drain_events(64):
			if str(event.get("type", "")) != "frame":
				continue
			var frame: PackedByteArray = event.get("frame", PackedByteArray())
			if not _world.apply_frame(frame):
				_publish()
				_transport.stop()
				return
			_publish()

		_process_first_intent()
		if int(_world.snapshot().state) == WorldState.STATE_FAILED:
			_transport.stop()
			break
		OS.delay_msec(LOGIC_SLEEP_MS)
	_publish()

func _process_first_intent() -> void:
	var intent := _peek_intent()
	if intent.is_empty():
		return
	var kind := str(intent.get("type", ""))
	if kind == "choose":
		if int(_world.snapshot().state) != WorldState.STATE_SELECTING_CHARACTER:
			return
		var character_id := int(intent.character_id)
		var frame := WireCodec.encode_character_choose(character_id)
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_choose_sent(character_id):
			_pop_intent()
			_publish()
	elif kind == "npc":
		var current: Dictionary = _world.snapshot()
		if int(current.state) != WorldState.STATE_IN_WORLD or not bool(current.world_ready) or bool(current.waiting_for_new_map) or int(current.pending_npc_sequence) != 0:
			return
		var sequence := int(current.npc_interaction_sequence) + 1
		var frame := WireCodec.encode_npc_interact(int(current.map_epoch), sequence, int(intent.spawn_id))
		if not frame.is_empty() and _transport.send_frame(frame) and _world.mark_npc_interact_sent(sequence):
			_pop_intent()
			_publish()
	elif kind == "move":
		var current: Dictionary = _world.snapshot()
		if int(current.state) != WorldState.STATE_IN_WORLD or bool(current.waiting_for_new_map):
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
	_published = value
	_mutex.unlock()
