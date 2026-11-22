extends SceneTree

const ClientTransport = preload("res://network/client_transport.gd")
const PLAIN_HEX := "1c00000001000100010000000000000001000000e8030000d0070000"

func _initialize() -> void:
    var args := OS.get_cmdline_user_args()
    if args.is_empty() or not args[0].is_valid_int():
        push_error("Missing local TCP test port")
        quit(1)
        return
    var tcp = ClientTransport.new()
    var result: int = tcp.start_encrypted("127.0.0.1", int(args[0]))
    if result != OK:
        push_error("start_encrypted failed " + str(result))
        quit(1)
        return
    var expected := PLAIN_HEX.hex_decode()
    var sent := false
    var received := false
    var until := Time.get_ticks_msec() + 8000
    while Time.get_ticks_msec() < until and not received:
        for item in tcp.drain_events(32):
            if item.get("type") == "connected" and not sent:
                sent = tcp.send_frame(expected)
            elif item.get("type") == "frame" and item.get("frame") == expected:
                received = true
        if int(tcp.get_status().get("state", -1)) == ClientTransport.STATE_FAILED:
            break
        OS.delay_msec(5)
    tcp.stop()
    if not sent or not received:
        push_error("Encrypted TCP transport failed sent=" + str(sent) + " received=" + str(received))
        quit(1)
        return
    print("AES_CBC_GODOT_TCP_PASS roundtrip=28")
    quit(0)
