extends SceneTree

const Transport = preload("res://network/client_transport.gd")
const WireCodec = preload("res://network/wire_codec.gd")
const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"

func _fail(message: String, transport = null) -> void:
	if transport != null:
		transport.stop()
	push_error("CLIENT_TLS_LOGIN_FAIL " + message)
	quit(1)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2 or not str(args[0]).is_valid_int():
		_fail("expected port and trusted certificate path")
		return
	var port := int(args[0])
	var trusted := X509Certificate.new()
	var cert_error := trusted.load(str(args[1]))
	if cert_error != OK:
		_fail("trusted certificate load %d" % cert_error)
		return
	var transport = Transport.new()
	var error := transport.start_secure("127.0.0.1", port, "localhost", trusted)
	if error != OK:
		_fail("transport start %d" % error, transport)
		return
	if not _wait_connected(transport, 5000):
		_fail("TLS connect timeout %s" % transport.get_status(), transport)
		return
	if not transport.send_frame(WireCodec.encode_auth_request(USERNAME, PASSWORD)):
		_fail("auth enqueue", transport)
		return
	var login_frames := _wait_frames(transport, 2, 5000)
	if login_frames.size() != 2:
		_fail("login response timeout %s" % transport.get_status(), transport)
		return
	var auth := WireCodec.decode_auth_result(login_frames[0])
	var characters := WireCodec.decode_character_list(login_frames[1])
	if auth != {"status": 0} or characters.is_empty() or characters.characters.size() != 1:
		_fail("login response invalid auth=%s characters=%s" % [auth, characters], transport)
		return
	var character_id := int(characters.characters[0].character_id)
	if not transport.send_frame(WireCodec.encode_character_choose(character_id)):
		_fail("choose enqueue", transport)
		return
	var choose_frames := _wait_frames(transport, 1, 5000)
	if choose_frames.size() != 1:
		_fail("choose response timeout", transport)
		return
	var chosen := WireCodec.decode_character_choose_result(choose_frames[0])
	if chosen.is_empty() or int(chosen.status) != 0 or int(chosen.character_id) != character_id or int(chosen.map_id) != 15 or int(chosen.map_epoch) <= 0:
		_fail("choose response invalid %s" % chosen, transport)
		return
	if not transport.send_frame(WireCodec.encode_move_target(int(chosen.map_epoch), 1, 24, 8)):
		_fail("move enqueue", transport)
		return
	if not _wait_sent(transport, 3, 2000):
		_fail("move did not leave bounded queue %s" % transport.get_status(), transport)
		return
	OS.delay_msec(250)
	var status_before_stop := transport.get_status()
	transport.stop()
	if int(status_before_stop.frames_received) != 3 or int(status_before_stop.frames_sent) != 3:
		_fail("unexpected frame counters %s" % status_before_stop)
		return
	print("CLIENT_TLS_LOGIN_PASS auth=PASS character=%d map_epoch=%d sent=3 received=3 worker_thread=true certificate_validation=PASS" % [character_id, int(chosen.map_epoch)])
	quit(0)

func _wait_connected(transport, timeout_ms: int) -> bool:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var state := int(transport.get_status().state)
		if state == Transport.STATE_CONNECTED:
			return true
		if state == Transport.STATE_FAILED:
			return false
		OS.delay_msec(2)
	return false

func _wait_frames(transport, count: int, timeout_ms: int) -> Array[PackedByteArray]:
	var frames: Array[PackedByteArray] = []
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline and frames.size() < count:
		for event in transport.drain_events(64):
			if str(event.get("type", "")) == "frame":
				var frame: PackedByteArray = event.get("frame", PackedByteArray())
				frames.append(frame)
		if int(transport.get_status().state) == Transport.STATE_FAILED:
			break
		if frames.size() < count:
			OS.delay_msec(2)
	return frames

func _wait_sent(transport, count: int, timeout_ms: int) -> bool:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var status: Dictionary = transport.get_status()
		if int(status.frames_sent) >= count:
			return true
		if int(status.state) == Transport.STATE_FAILED:
			return false
		OS.delay_msec(2)
	return false
