extends RefCounted

# TCP record: little-endian u32 ciphertext length + AES-256-CBC/PKCS7 ciphertext.
# Both the 32-byte key and the 16-byte IV are deployment-fixed.
# WARNING: repeated IV and unauthenticated CBC are intentionally legacy-compatible,
# not equivalent to TLS/AEAD and never proof of identity.
const MAX_PLAIN_BYTES := 65536
const MAX_CIPHER_BYTES := 65552

static func read_key_from_environment() -> PackedByteArray:
    return _read_hex_env("SGTEST_AES_KEY_HEX", 64)

static func read_iv_from_environment() -> PackedByteArray:
    return _read_hex_env("SGTEST_AES_IV_HEX", 32)

static func _read_hex_env(name: String, expected_chars: int) -> PackedByteArray:
    var value := OS.get_environment(name)
    if value.length() != expected_chars or not value.is_valid_hex_number():
        return PackedByteArray()
    return value.hex_decode()

static func encrypt_frame(frame: PackedByteArray, key: PackedByteArray, iv: PackedByteArray) -> PackedByteArray:
    if not _valid_material(key, iv) or frame.size() < 1 or frame.size() > MAX_PLAIN_BYTES:
        return PackedByteArray()
    var padded := frame.duplicate()
    var padding := 16 - (padded.size() % 16)
    for _i in range(padding):
        padded.append(padding)
    var aes := AESContext.new()
    if aes.start(AESContext.MODE_CBC_ENCRYPT, key, iv) != OK:
        return PackedByteArray()
    var encrypted := aes.update(padded)
    aes.finish()
    if encrypted.size() != padded.size():
        return PackedByteArray()
    var result := PackedByteArray()
    result.resize(4)
    result.encode_u32(0, encrypted.size())
    result.append_array(encrypted)
    return result

static func decrypt_record(cipher: PackedByteArray, key: PackedByteArray, iv: PackedByteArray) -> PackedByteArray:
    if not _valid_material(key, iv) or cipher.size() < 16 or cipher.size() > MAX_CIPHER_BYTES or cipher.size() % 16 != 0:
        return PackedByteArray()
    var aes := AESContext.new()
    if aes.start(AESContext.MODE_CBC_DECRYPT, key, iv) != OK:
        return PackedByteArray()
    var padded := aes.update(cipher)
    aes.finish()
    if padded.size() != cipher.size():
        return PackedByteArray()
    var padding := int(padded[padded.size() - 1])
    if padding < 1 or padding > 16:
        return PackedByteArray()
    var valid := true
    for i in range(padding):
        valid = valid and int(padded[padded.size() - i - 1]) == padding
    if not valid:
        return PackedByteArray()
    var plain := padded.slice(0, padded.size() - padding)
    if plain.is_empty() or plain.size() > MAX_PLAIN_BYTES:
        return PackedByteArray()
    return plain

static func _valid_material(key: PackedByteArray, iv: PackedByteArray) -> bool:
    return key.size() == 32 and iv.size() == 16
