extends Node2D

const WORLD_STATE = preload("res://core/state/world_state.gd")
const NETWORK_PEER = preload("res://core/net/network_peer.gd")
const WORLD_RENDERER = preload("res://core/presentation/world_renderer.gd")
const SERVER_TICK_RATE: float = 30.0

var _state: RefCounted = WORLD_STATE.new()
var _network: RefCounted = NETWORK_PEER.new()
var _renderer: Node2D
var _visual_tick: float = 0.0

func _ready() -> void:
	_renderer = WORLD_RENDERER.new()
	add_child(_renderer)
	_renderer.state = _state
	_network.connect_local()

func _physics_process(delta: float) -> void:
	# Network and state logic always run at fixed Physics rate even when
	# render settings, low quality, focus and AFK modes are added later.
	for packet in _network.poll_events():
		var kind: String = str(packet.get("type", ""))
		if kind == "_connected":
			_network.send_join("Player-%d" % (Time.get_ticks_msec() % 100000))
		elif kind == "_disconnected":
			_state.reset_map(_state.map_epoch + 1)
		else:
			_state.apply(packet)
			_visual_tick = maxf(_visual_tick, float(_state.latest_server_tick) - 3.0)
	_visual_tick += delta * SERVER_TICK_RATE

func _process(_delta: float) -> void:
	_renderer.connection_status = _network.status
	_renderer.render_tick = _visual_tick
	_renderer.queue_redraw()

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_LEFT:
			_network.send_move(event.position)
		elif event.button_index == MOUSE_BUTTON_RIGHT:
			_network.send_stop()
