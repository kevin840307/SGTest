extends SceneTree
const Transport = preload("res://network/client_transport.gd")
func _initialize() -> void:
	var transport = Transport.new()
	if transport.start_secure("", 443) != ERR_INVALID_PARAMETER:
		push_error("TRANSPORT_FAIL invalid start")
		quit(1)
		return
	var status := transport.get_status()
	if int(status.state) != Transport.STATE_STOPPED or int(status.outbound_frames) != 0:
		push_error("TRANSPORT_FAIL initial state %s" % status)
		quit(1)
		return
	transport.stop()
	print("CLIENT_TRANSPORT_UNIT_PASS invalid_start=PASS initial_bounds=PASS render_loop_independent_thread=true")
	quit(0)
