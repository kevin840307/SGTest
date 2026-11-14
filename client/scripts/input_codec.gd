extends RefCounted

const STRIDE := 12
const REDUNDANCY := 3

static func encode(frames: Array) -> PackedByteArray:
	var first := maxi(0,frames.size()-REDUNDANCY)
	var bytes := PackedByteArray()
	bytes.resize((frames.size()-first)*STRIDE)
	for i in range(first,frames.size()):
		var at := (i-first)*STRIDE
		bytes.encode_u32(at,int(frames[i].sequence))
		bytes.encode_float(at+4,frames[i].motion.x)
		bytes.encode_float(at+8,frames[i].motion.y)
	return bytes

static func decode(bytes: PackedByteArray) -> Array:
	var result: Array = []
	if bytes.is_empty() or bytes.size()%STRIDE != 0 or bytes.size() > STRIDE*REDUNDANCY:
		return result
	for at in range(0,bytes.size(),STRIDE):
		var motion := Vector2(bytes.decode_float(at+4),bytes.decode_float(at+8))
		if not motion.is_finite() or absf(motion.x) > 1 or absf(motion.y) > 1:
			return []
		result.append({"sequence":bytes.decode_u32(at),"motion":motion.limit_length(1.0)})
	return result
