class_name SnapshotCodec
extends RefCounted

# 24 bytes/entity, including uint32 state revision. RPC chunks stay <=800 bytes.
# Kind: 0 player, 1 monster. Positions retain float precision; IDs use uint32.
const STRIDE := 24
const ROWS_PER_PACKET := 33

static func encode(rows: Array) -> PackedByteArray:
	var bytes := PackedByteArray()
	bytes.resize(rows.size() * STRIDE)
	for i in rows.size():
		var row: Array = rows[i]
		var at := i * STRIDE
		bytes.encode_u32(at, int(row[0]))
		bytes.encode_float(at+4, float(row[1]))
		bytes.encode_float(at+8, float(row[2]))
		bytes.encode_u16(at+12, clampi(int(row[3]), 0, 65535))
		bytes[at+14] = int(row[4])
		bytes[at+15] = int(row[5])
		bytes.encode_float(at+16, float(row[6]))
		bytes.encode_u32(at+20, int(row[7]) if row.size() > 7 else 0)
	return bytes

static func decode(bytes: PackedByteArray) -> Array:
	var rows: Array = []
	if bytes.size() % STRIDE != 0:
		return rows
	for at in range(0, bytes.size(), STRIDE):
		rows.append([bytes.decode_u32(at), bytes.decode_float(at+4), bytes.decode_float(at+8),
			bytes.decode_u16(at+12), bytes[at+14], bytes[at+15], bytes.decode_float(at+16),bytes.decode_u32(at+20)])
	return rows
