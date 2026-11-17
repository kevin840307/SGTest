extends SceneTree

const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://network/world_state.gd")

const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
# Matches C# WireProtocol V1 fixed little-endian fixtures.
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const SPAWN := "4A0000000100150001000000000000002B000000000000001600000000000000020000000000000001000000000000000F0000000700000000000000000040420000C041010400000001"
const DELTA := "4A0000000100170001000000000000002C000000000000001600000000000000020000000000000001000000000000000F0000000800000000000000000060420000C041000500000001"
const SELF := "4A0000000100180001000000000000002C000000000000000B00000000000000020000000000000001000000000000000F0000000800000000000000000060420000C041000500000006"
const DESPAWN := "280000000100160001000000000000002D0000000000000016000000000000000200000000000000"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const READY_ACK := "180000000100190001000000000000002A00000000000000"
const RESYNC := "1000000001001A000100000000000000"
const MONSTER_SPAWN := "4E00000001001B000100000000000000320000000000000021A0AE3B0000000001000000000000000D0000000000F0420000A04202000000000000005A0000006400000002060B00000000000000"
const MONSTER_DELTA := "4E00000001001C000100000000000000330000000000000021A0AE3B0000000001000000000000000D0000000000F8420000A0420300000000000000500000006400000003060B00000000000000"
const MONSTER_DESPAWN := "2800000001001D000100000000000000340000000000000021A0AE3B000000000300000000000000"
const GROUND_SPAWN := "340000000100200001000000000000003700000000000000F5010000000000000F000000000040420000C0418D0A000002000000"
const GROUND_DESPAWN := "200000000100210001000000000000003800000000000000F501000000000000"

func _fail(message: String) -> void:
	push_error("AOI_WIRE_FAIL " + message)
	quit(1)

func _initialize() -> void:
	if not _verify_self():
		return
	var ack := WireCodec.encode_world_ready_ack(1,42)
	var ack_value := WireCodec.decode_world_ready_ack(READY_ACK.hex_decode())
	var resync := WireCodec.encode_world_resync_request(1)
	var resync_value := WireCodec.decode_world_resync_request(RESYNC.hex_decode())
	if ack.hex_encode().to_upper() != READY_ACK or int(ack_value.map_epoch) != 1 or int(ack_value.server_tick) != 42 or resync.hex_encode().to_upper() != RESYNC or int(resync_value.map_epoch) != 1:
		_fail("WorldReady ACK/resync C# golden")
		return
	var monster_spawn := WireCodec.decode_monster_state(MONSTER_SPAWN.hex_decode())
	var monster_delta := WireCodec.decode_monster_state(MONSTER_DELTA.hex_decode())
	var monster_despawn := WireCodec.decode_monster_despawn(MONSTER_DESPAWN.hex_decode())
	if monster_spawn.is_empty() or int(monster_spawn.entity_id) != 1001300001 or int(monster_spawn.enemy_id) != 1 or int(monster_spawn.current_hp) != 90 or int(monster_spawn.state) != 2 or int(monster_spawn.facing) != 6 or monster_delta.is_empty() or int(monster_delta.revision) != 3 or int(monster_delta.current_hp) != 80 or int(monster_delta.state) != 3 or monster_despawn.is_empty() or int(monster_despawn.entity_id) != 1001300001:
		_fail("Monster C# golden decode")
		return
	var malformed_monster := MONSTER_DELTA.hex_decode()
	malformed_monster[68] = 5
	if not WireCodec.decode_monster_state(malformed_monster).is_empty():
		_fail("invalid monster AI state accepted")
		return
	malformed_monster = MONSTER_DELTA.hex_decode()
	malformed_monster.encode_s32(60,101)
	if not WireCodec.decode_monster_state(malformed_monster).is_empty():
		_fail("monster HP above MaxHP accepted")
		return
	var ground := WireCodec.decode_ground_drop_spawn(GROUND_SPAWN.hex_decode())
	var drop_gone := WireCodec.decode_ground_drop_despawn(GROUND_DESPAWN.hex_decode())
	if ground.is_empty() or int(ground.receiver_map_epoch) != 1 or int(ground.server_tick) != 55 or int(ground.drop_id) != 501 or int(ground.item_definition_id) != 2701 or int(ground.quantity) != 2 or float(ground.x) != 48.0 or drop_gone.is_empty() or int(drop_gone.drop_id) != 501:
		_fail("GroundDropSpawn/Despawn C# V1 goldens")
		return
	var malformed_ground := GROUND_SPAWN.hex_decode()
	malformed_ground.encode_s32(48,0)
	if not WireCodec.decode_ground_drop_spawn(malformed_ground).is_empty():
		_fail("zero ground quantity accepted")
		return
	malformed_ground = GROUND_SPAWN.hex_decode()
	malformed_ground.encode_u32(36,0x7FC00000)
	if not WireCodec.decode_ground_drop_spawn(malformed_ground).is_empty():
		_fail("NaN ground position accepted")
		return
	var ground_world = _ready_world()
	if not ground_world.mark_world_ready_ack_sent(1,42):
		_fail("ground fixture initial WorldReady ACK")
		return
	if not ground_world.apply_frame(GROUND_SPAWN.hex_decode()) or not ground_world.snapshot().ground_drops.has(501):
		_fail("ground baseline spawn not accepted")
		return
	if not ground_world.apply_frame(GROUND_SPAWN.hex_decode()) or int(ground_world.snapshot().stale_aoi_events) != 1:
		_fail("duplicate immutable ground UID did not become stale")
		return
	var wrong_ground := GROUND_SPAWN.hex_decode()
	wrong_ground.encode_s64(16,56)
	wrong_ground.encode_s32(44,2702)
	if not ground_world.apply_frame(wrong_ground) or not bool(ground_world.snapshot().resync_pending):
		_fail("changed item for identical ground UID did not require resync")
		return
	var ground_ready := READY.hex_decode()
	ground_ready.encode_s64(16,57)
	if not ground_world.apply_frame(ground_ready) or not ground_world.snapshot().ground_drops.is_empty():
		_fail("ground AOI baseline survived same-map resync")
		return
	if not ground_world.mark_world_ready_ack_sent(1,57) or not ground_world.apply_frame(GROUND_SPAWN.hex_decode()):
		_fail("ground AOI after resync sequence")
		return
	# A post-Ready stale tick=55 is ignored; a fresh authoritative event
	# arrives with later server tick and must again populate the baseline.
	var fresh_ground := GROUND_SPAWN.hex_decode()
	fresh_ground.encode_s64(16,58)
	if not ground_world.apply_frame(fresh_ground) or ground_world.snapshot().ground_drops.size() != 1:
		_fail("fresh ground AOI after resync missing")
		return
	var fresh_despawn := GROUND_DESPAWN.hex_decode()
	fresh_despawn.encode_s64(16,59)
	if not ground_world.apply_frame(fresh_despawn) or not ground_world.snapshot().ground_drops.is_empty():
		_fail("ground UID despawn left a ghost")
		return
	if not ground_world.apply_frame(fresh_despawn):
		_fail("duplicate ground despawn became fatal")
		return
	var again := GROUND_SPAWN.hex_decode()
	again.encode_s64(16,60)
	if not ground_world.apply_frame(again) or ground_world.snapshot().ground_drops.size() != 1:
		_fail("ground re-enter Spawn failed")
		return
	var to_map13 := MAP13_READY.hex_decode()
	to_map13.encode_s64(16,61)
	if not ground_world.apply_frame(to_map13) or not ground_world.snapshot().ground_drops.is_empty() or int(ground_world.snapshot().map_id) != 13:
		_fail("map transfer did not clear old ground UID")
		return
	if not ground_world.apply_frame(again) or not ground_world.snapshot().ground_drops.is_empty():
		_fail("stale map ground UID crossed transfer")
		return
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

	for invalid in [0,9,255]:
		if not WireCodec.decode_entity_state(_mutate_u8(SELF.hex_decode(),73,invalid)).is_empty() or not WireCodec.decode_world_ready(_mutate_u8(READY.hex_decode(),44,invalid)).is_empty():
			_fail("invalid Facing accepted")
			return

	var monster_world = _ready_world()
	if not monster_world.mark_world_ready_ack_sent(1,42):
		_fail("monster fixture initial Ready ACK")
		return
	# Golden bytes intentionally describe Map13; reducer fixture runs in the
	# existing Map15 world and changes only the receiver's current map identity.
	var local_monster_spawn := MONSTER_SPAWN.hex_decode()
	local_monster_spawn.encode_s32(40,15)
	var local_monster_delta := MONSTER_DELTA.hex_decode()
	local_monster_delta.encode_s32(40,15)
	if not monster_world.apply_frame(local_monster_spawn) or not monster_world.snapshot().monsters.has(1001300001):
		_fail("MonsterSpawn reducer")
		return
	if not monster_world.apply_frame(local_monster_delta) or int(monster_world.snapshot().monsters[1001300001].revision) != 3 or int(monster_world.snapshot().monsters[1001300001].current_hp) != 80:
		_fail("MonsterDelta reducer")
		return
	if not monster_world.apply_frame(MONSTER_DESPAWN.hex_decode()) or not monster_world.snapshot().monsters.is_empty():
		_fail("MonsterDespawn reducer")
		return
	if not monster_world.apply_frame(MONSTER_DESPAWN.hex_decode()):
		_fail("duplicate MonsterDespawn should be stale")
		return
	var monster_gap := local_monster_delta.duplicate()
	monster_gap.encode_s64(16,53)
	if not monster_world.apply_frame(monster_gap) or not bool(monster_world.snapshot().resync_pending) or str(monster_world.snapshot().resync_reason) != "monster_delta_without_spawn":
		_fail("Monster revision/baseline gap did not enter bounded resync")
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
	if not world.mark_world_ready_ack_sent(1,42):
		_fail("initial WorldReady ACK tracking")
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
	var gap_delta := DELTA.hex_decode()
	gap_delta.encode_s64(16,46) # newer than the prior Despawn tick=45
	if not world.apply_frame(gap_delta):
		_fail("recoverable delta gap became terminal")
		return
	snapshot = world.snapshot()
	if not bool(snapshot.resync_pending) or bool(snapshot.world_ready) or str(snapshot.resync_reason) != "delta_without_spawn":
		_fail("delta gap did not enter resync %s" % snapshot)
		return
	if not world.mark_resync_request_sent(1) or int(world.snapshot().resync_requests) != 1:
		_fail("resync request tracking")
		return
	if not world.apply_frame(SPAWN.hex_decode()) or not world.snapshot().entities.is_empty():
		_fail("FIFO-old AOI was applied during resync")
		return
	var replacement_ready := READY.hex_decode()
	replacement_ready.encode_s64(16,46)
	if not world.apply_frame(replacement_ready):
		_fail("same-map resync WorldReady rejected")
		return
	snapshot=world.snapshot()
	if bool(snapshot.resync_pending) or not bool(snapshot.world_ready) or int(snapshot.resync_completions) != 1 or int(snapshot.pending_world_ready_ack_tick) != 46:
		_fail("resync baseline did not complete %s" % snapshot)
		return
	if not world.mark_world_ready_ack_sent(1,46):
		_fail("replacement Ready ACK tracking")
		return
	if not world.apply_frame(MAP13_READY.hex_decode()):
		_fail("MapEpoch transfer WorldReady")
		return
	snapshot = world.snapshot()
	if int(snapshot.map_epoch) != 2 or int(snapshot.map_id) != 13 or float(snapshot.y) != 80.0 or not snapshot.entities.is_empty():
		_fail("old AOI not cleared on map transfer %s" % snapshot)
		return
	var stale_before_old_map := int(world.snapshot().stale_aoi_events)
	if not world.apply_frame(SPAWN.hex_decode()) or int(world.snapshot().stale_aoi_events) != stale_before_old_map + 1 or not world.snapshot().entities.is_empty():
		_fail("old map Spawn not ignored after transfer")
		return
	var future := _mutate_u8(SPAWN.hex_decode(), 8, 3)
	if world.apply_frame(future) or int(world.snapshot().state) != WorldState.STATE_FAILED:
		_fail("future map AOI did not fail closed")
		return
	print("AOI_WIRE_GODOT_PASS ready=45 ack=24 resync=16 spawn=74 delta=74 despawn=40 monster_spawn=78 monster_delta=78 monster_despawn=40 ground_spawn=52 ground_despawn=32 ground_resync=PASS worldstate=PASS same_epoch_resync=PASS monster_gap=RESYNC map_epoch=PASS stale=IGNORED future=FAIL_CLOSED")
	quit(0)

func _mutate_u8(bytes: PackedByteArray, offset: int, value: int) -> PackedByteArray:
	var changed := bytes.duplicate()
	changed[offset] = value
	return changed

func _ready_world():
	var world = WorldState.new()
	world.mark_auth_sent()
	world.apply_frame(AUTH.hex_decode())
	world.apply_frame(LIST.hex_decode())
	world.mark_choose_sent(11)
	world.apply_frame(CHOOSE.hex_decode())
	world.apply_frame(READY.hex_decode())
	return world

func _verify_self() -> bool:
	var value := WireCodec.decode_entity_state(SELF.hex_decode())
	if value.is_empty() or int(value.actor_id) != 11 or int(value.revision) != 8 or float(value.x) != 56.0:
		_fail("SelfState C# golden")
		return false
	var world = _ready_world()
	if not world.apply_frame(SELF.hex_decode()) or float(world.snapshot().x) != 56.0 or int(world.snapshot().facing) != 6 or bool(world.snapshot().self_state.moving) or not world.snapshot().entities.is_empty():
		_fail("SelfState did not update own state independently of remote AOI")
		return false
	# Duplicate Ready cannot clear own generation/revision or rewind coordinates.
	var repeat_ready := READY.hex_decode()
	repeat_ready.encode_s64(16, 45)
	if not world.apply_frame(repeat_ready) or float(world.snapshot().x) != 56.0:
		_fail("duplicate Ready rewound own state")
		return false
	var stale := SELF.hex_decode()
	stale.encode_s64(16, 46)
	stale.encode_s64(52, 7)
	stale.encode_float(60, 1.0)
	if not world.apply_frame(stale) or float(world.snapshot().x) != 56.0:
		_fail("stale own revision rewound XY")
		return false
	var bad_cases := []
	var bad := SELF.hex_decode()
	bad.encode_s64(24, 22) # wrong actor
	bad_cases.append(bad)
	bad = SELF.hex_decode()
	bad.encode_s64(32, 3) # same TLS session cannot change ConnectionEpoch
	bad_cases.append(bad)
	bad = SELF.hex_decode()
	bad.encode_s64(40, 2) # wrong entity MapEpoch
	bad_cases.append(bad)
	bad = SELF.hex_decode()
	bad.encode_s32(48, 13) # wrong map
	bad_cases.append(bad)
	for frame in bad_cases:
		var isolated = _ready_world()
		isolated.apply_frame(SELF.hex_decode())
		if isolated.apply_frame(frame) or int(isolated.state) != WorldState.STATE_FAILED:
			_fail("wrong own identity/generation accepted")
			return false
	# A sequencing gap can be caused by a missing reliable AOI baseline/delta.
	# Do not mutate self authority or disconnect; request the bounded snapshot rebuild.
	var recoverable := SELF.hex_decode()
	recoverable.encode_s64(52, 9)
	recoverable.encode_u32(69, 4)
	var healing = _ready_world()
	healing.apply_frame(SELF.hex_decode())
	if not healing.apply_frame(recoverable) or int(healing.state) != WorldState.STATE_IN_WORLD or not bool(healing.snapshot().resync_pending) or float(healing.snapshot().x) != 56.0:
		_fail("own action/revision gap did not request resync")
		return false
	var before = WorldState.new()
	if before.apply_frame(SELF.hex_decode()):
		_fail("self before Ready accepted")
		return false
	var remote_self := SELF.hex_decode()
	remote_self[6] = WireCodec.OPCODE_ENTITY_SPAWN
	if _ready_world().apply_frame(remote_self):
		_fail("self entered remote AOI")
		return false
	# Transfer clears self baseline; old map update cannot restore the old position.
	if not world.apply_frame(MAP13_READY.hex_decode()) or not world.self_state.is_empty() or not world.apply_frame(SELF.hex_decode()) or world.x != 40.0:
		_fail("old-map SelfState crossed transfer")
		return false
	var new_self := SELF.hex_decode()
	new_self.encode_s64(8, 2)
	new_self.encode_s64(16, 51)
	new_self.encode_s64(40, 2)
	new_self.encode_s32(48, 13)
	new_self.encode_s64(52, 1)
	new_self.encode_float(60, 72.0)
	new_self.encode_u32(69, 0)
	if not world.apply_frame(new_self) or world.x != 72.0:
		_fail("new-map self baseline did not reset")
		return false
	print("SELF_STATE_GODOT_PASS bytes=74 identity=FAIL_CLOSED sequence_gap=RESYNC stale=IGNORED transfer=PASS ready_replay=IGNORED")
	return true
