extends RefCounted

const MAX_PACKET_BYTES: int = 65536
var _peer: WebSocketPeer = WebSocketPeer.new()
var _joined: bool = false
var _was_open: bool = false
var status: String = "Disconnected"

func connect_local(url: String = "ws://127.0.0.1:5678/ws") -> Error:
	_peer = WebSocketPeer.new()
	_joined = false
	_was_open = false
	status = "Connecting"
	var err: Error = _peer.connect_to_url(url)
	if err != OK:
		status = "Cannot connect: %s" % str(err)
	return err

func poll_events() -> Array[Dictionary]:
	_peer.poll()
	var events: Array[Dictionary] = []
	var peer_state: int = _peer.get_ready_state()
	if peer_state == WebSocketPeer.STATE_OPEN and not _was_open:
		_was_open = true
		status = "Connected"
		events.append({"type": "_connected"})
	elif peer_state == WebSocketPeer.STATE_CLOSED and _was_open:
		_was_open = false
		status = "Disconnected"
		events.append({"type": "_disconnected"})
	if peer_state != WebSocketPeer.STATE_OPEN:
		return events
	# Always drain all reliable events. Never tie network polling to render FPS,
	# AFK, scene visibility or low-priority visual object creation.
	while _peer.get_available_packet_count() > 0:
		var bytes: PackedByteArray = _peer.get_packet()
		if bytes.size() > MAX_PACKET_BYTES:
			status = "Protocol error: oversized frame"
			_peer.close(1009, "oversized")
			break
		var data: Variant = JSON.parse_string(bytes.get_string_from_utf8())
		if typeof(data) == TYPE_DICTIONARY:
			events.append(data)
	return events

func send_join(name: String) -> Error:
	if _peer.get_ready_state() != WebSocketPeer.STATE_OPEN:
		return ERR_CONNECTION_ERROR
	_joined = true
	return _peer.send_text(JSON.stringify({"type": "join", "name": name}))

func send_move(destination: Vector2) -> Error:
	if not _joined or _peer.get_ready_state() != WebSocketPeer.STATE_OPEN:
		return ERR_CONNECTION_ERROR
	return _peer.send_text(JSON.stringify({"type": "move", "x": destination.x, "y": destination.y}))

func send_stop() -> Error:
	if not _joined or _peer.get_ready_state() != WebSocketPeer.STATE_OPEN:
		return ERR_CONNECTION_ERROR
	return _peer.send_text('{"type":"stop"}')
