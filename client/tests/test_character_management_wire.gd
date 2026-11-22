extends SceneTree

const Codec = preload("res://network/wire_codec.gd")
const State = preload("res://world/world_state.gd")

func _initialize() -> void:
    var set_password := Codec.encode_character_manage(1,97,"1234","5678")
    var delete := Codec.encode_character_manage(2,97,"5678")
    var result := "1200000001006a0002066100000000000000".hex_decode()
    if set_password.hex_encode() != "1b0000000100690001610000000000000004043132333435363738":
        _fail("password rotation golden frame does not match C#")
        return
    if delete.hex_encode() != "1700000001006900026100000000000000040035363738":
        _fail("deletion golden frame does not match C#")
        return
    if not Codec.encode_character_manage(2,97,"").is_empty() or not Codec.encode_character_manage(1,97,"1234","123").is_empty() or not Codec.encode_character_manage(1,97,"123","5678").is_empty():
        _fail("invalid credential length permitted")
        return
    var parsed := Codec.decode_character_manage_result(result)
    if parsed.is_empty() or parsed.action != 2 or parsed.status != 6 or parsed.character_id != 97:
        _fail("server result decoder rejected or misread response")
        return
    var state := State.new()
    state.state = State.STATE_SELECTING_CHARACTER
    if not state.mark_character_manage_sent(97):
        _fail("valid management request was rejected")
        return
    if not state.apply_frame(result) or state.state != State.STATE_SELECTING_CHARACTER or state.character_manage_status != 6 or state.character_manage_result_revision != 1:
        _fail("failed management result did not restore selection")
        return
    var success := "1200000001006a0001006100000000000000".hex_decode()
    if not state.mark_character_manage_sent(97) or not state.apply_frame(success) or state.state != State.STATE_WAIT_CHARACTER_LIST:
        _fail("successful management result did not await authoritative character list")
        return
    print("CHARACTER_MANAGEMENT_WIRE_PASS csharp_golden=true secondary_credential_validated=true result_reducer=true")
    quit(0)

func _fail(message: String) -> void:
    push_error("CHARACTER_MANAGEMENT_WIRE_FAIL "+message)
    quit(1)
