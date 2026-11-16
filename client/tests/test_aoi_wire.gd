extends SceneTree

const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://network/world_state.gd")

const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
# Matches C# WireProtocol V1 fixed little-endian fixtures.
const READY := "2C0000000100140001000000000000002A000000000000000B000000000000000F0000000000C04100000041"
const SPAWN := "490000000100150001000000000000002B000000000000001600000000000000020000000000000001000000000000000F0000000700000000000000000040420000C0410104000000"
const DELTA := "490000000100170001000000000000002C000000000000001600000000000000020000000000000001000000000000000F0000000800000000000000000060420000C0410005000000"
const DESPAWN := "280000000100160001000000000000002D0000000000000016000000000000000200000000000000"
const MAP13_READY := "2C00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A042"

func _fail(message: String) -> void:
	push_error("AOI_WIRE_FAIL " + message)
	quit(1)

func _initialize() -> void:
	var ready := WireCodec.decode_world_ready(READY.hex_decode())
	if ready.is_empty() or int(ready.actor_id) != 11 or int(ready.map_epoch) != 1 or float(ready.x) != 24.0:
		_fail("WorldReady C# golden decode")
		return
	var spawn := WireCodec.decode_entity_state(SPAWN.hex_decode())
	if spawn.is_empty() or int(spawn.actor_id) != 22 or int(spawn.revision) != 7 or not bool(spawn.moving) or int(spawn.movement_action_sequence) != 4:
		_fail("Spawn C# golden decode")
		return
	var delta := WireCodec.decode_entity_state(DELTA.hex_decode())
	if delta.is_empty() or int(delta.revision) != 8 or float(delta.x) != 56.0 or bool(delta.moving):
		_fail("Delta C# golden decode")
		return
	var gone := WireCodec.decode_entity_despawn(DESPAWN.hex_decode())
	if gone.is_empty() or int(gone.actor_id) != 22 or int(gone.connection_epoch) != 2:
		_fail("Despawn C# golden decode")
		return
	if not WireCodec.decode_entity_state(_mutate_u8(SPAWN.hex_decode(), 68, 2)).is_empty():
		_fail("malformed moving bit accepted")
		return
	if not WireCodec.decode_world_ready(_mutate_u8(READY.hex_decode(), 0, 43)).is_empty():
		_fail("bad WorldReady length accepted")
		return
	var nan_frame := SPAWN.hex_decode()
	nan_frame[60] = 0
	nan_frame[61] = 0
	nan_frame[62] = 0xC0
	nan_frame[63] = 0x7F
	if not WireCodec.decode_entity_state(nan_frame).is_empty():
		_fail("NaN position accepted")
		return

	var world = WorldState.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()):
		_fail("auth/choose prereq")
		return
	if not world.apply_frame(READY.hex_decode()):
		_fail("WorldReady reducer")
		return
	var snapshot := world.snapshot()
	if not bool(snapshot.world_ready) or int(snapshot.actor_id) != 11 or int(snapshot.map_id) != 15 or float(snapshot.x) != 24.0 or not snapshot.entities.is_empty():
		_fail("Ready state mismatch %s" % snapshot)
		return
	if not world.apply_frame(SPAWN.hex_decode()) or not world.snapshot().entities.has(22):
		_fail("Spawn not visible")
		return
	if not world.apply_frame(DELTA.hex_decode()) or int(world.snapshot().entities[22].revision) != 8:
		_fail("Delta did not update previous Spawn")
		return
	if not world.apply_frame(DESPAWN.hex_decode()) or not world.snapshot().entities.is_empty():
		_fail("Despawn left a ghost")
		return
	if not world.apply_frame(DESPAWN.hex_decode()) or int(world.snapshot().stale_aoi_events) != 1:
		_fail("duplicate despawn not ignored")
		return
	if not world.apply_frame(MAP13_READY.hex_decode()):
		_fail("MapEpoch transfer WorldReady")
		return
	snapshot = world.snapshot()
	if int(snapshot.map_epoch) != 2 or int(snapshot.map_id) != 13 or float(snapshot.y) != 80.0 or not snapshot.entities.is_empty():
		_fail("old AOI not cleared on map transfer %s" % snapshot)
		return
	if not world.apply_frame(SPAWN.hex_decode()) or int(world.snapshot().stale_aoi_events) != 2:
		_fail("old map Spawn not ignored after transfer")
		return
	var future := _mutate_u8(SPAWN.hex_decode(), 8, 3)
	if world.apply_frame(future) or int(world.snapshot().state) != WorldState.STATE_FAILED:
		_fail("future map AOI did not fail closed")
		return
	print("AOI_WIRE_GODOT_PASS ready=44 spawn=73 delta=73 despawn=40 worldstate=PASS map_epoch=PASS stale=IGNORED future=FAIL_CLOSED")
	quit(0)

func _mutate_u8(bytes: PackedByteArray, offset: int, value: int) -> PackedByteArray:
	var changed := bytes.duplicate()
	changed[offset] = value
	return changed
