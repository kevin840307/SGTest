extends SceneTree

const Cipher = preload("res://network/aes_cbc_frame.gd")

func _initialize() -> void:
    var key := "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f".hex_decode()
    var iv := "000102030405060708090a0b0c0d0e0f".hex_decode()
    var original := "1c00000001000100010000000000000001000000e8030000d0070000".hex_decode()
    var golden := "20000000c8742a0e72a5d69b17332b42d767b080934464023a67818d9c4a56b6478a503e"
    var encrypted: PackedByteArray = Cipher.encrypt_frame(original, key, iv)
    if encrypted.hex_encode() != golden:
        push_error("AES-CBC .NET/Python fixture mismatch: " + encrypted.hex_encode())
        quit(1)
        return
    var decoded: PackedByteArray = Cipher.decrypt_record(encrypted.slice(4), key, iv)
    if decoded != original:
        push_error("AES-CBC roundtrip mismatch")
        quit(1)
        return
    if not Cipher.decrypt_record(encrypted.slice(4), key, PackedByteArray()).is_empty():
        push_error("Invalid IV accepted")
        quit(1)
        return
    if not Cipher.decrypt_record(PackedByteArray([0, 1, 2]), key, iv).is_empty():
        push_error("Malformed ciphertext accepted")
        quit(1)
        return
    print("AES_CBC_CROSS_RUNTIME_GOLDEN_PASS frame=28 cipher=32 record=36")
    quit(0)
