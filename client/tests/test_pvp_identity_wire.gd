extends SceneTree

const WireCodec=preload("res://wire/wire_codec.gd")
const WorldState=preload("res://world/world_state.gd")

func _fail(message:String)->void:
	push_error("PVP_IDENTITY_WIRE_FAIL "+message)
	quit(1)

func _initialize()->void:
	var pink:=_identity(1,22,9,4,1,false,2200000000000,2200000030000,2199999999990,"測試玩家")
	var decoded:=WireCodec.decode_player_identity(pink)
	if pink.size()!=80 or decoded.is_empty() or str(decoded.display_name)!="測試玩家" or int(decoded.actor_id)!=22 or int(decoded.entity_map_epoch)!=4 or int(decoded.mode)!=1 or bool(decoded.red_man) or int(decoded.pink_until_unix_ms)!=2200000030000:
		_fail("identity codec")
		return
	var malformed:=pink.duplicate()
	malformed[41]=2
	if not WireCodec.decode_player_identity(malformed).is_empty():
		_fail("invalid red flag accepted")
		return

	var self_world:=_world()
	if not self_world.apply_frame(_identity(1,11,7,1,0,false,2200000000000,0,0,"OwnHero")):
		_fail("self identity before SelfState")
		return
	if str(self_world.snapshot().player_identities[11].display_name)!="OwnHero":
		_fail("self identity snapshot")
		return
	if not self_world.apply_frame(_entity(WireCodec.OPCODE_SELF_STATE,1,11,7,1,13,1,8,8,false,0,1)):
		_fail("SelfState generation after identity")
		return

	var remote:=_world()
	if not remote.apply_frame(_entity(WireCodec.OPCODE_ENTITY_SPAWN,1,22,9,4,13,1,24,8,false,0,1)):
		_fail("remote spawn")
		return
	if not remote.apply_frame(pink):
		_fail("remote identity")
		return
	var snap:Dictionary=remote.snapshot()
	if not snap.player_identities.has(22) or str(snap.player_identities[22].display_name)!="測試玩家":
		_fail("remote identity snapshot")
		return

	# Same durable PvP revision may refresh server wall-clock correlation only.
	if not remote.apply_frame(_identity(1,22,9,4,1,false,2200000000100,2200000030000,2199999999990,"測試玩家")):
		_fail("same revision clock refresh")
		return
	# An older PvP revision cannot overwrite a newer presentation.
	if not remote.apply_frame(_identity(1,22,9,4,1,false,2200000000200,2200000020000,2199999999989,"測試玩家")) or int(remote.snapshot().stale_player_identities)!=1:
		_fail("older identity revision not stale")
		return
	var red:=_identity(1,22,9,4,1,true,2200000000300,0,2200000000300,"測試玩家")
	if not remote.apply_frame(red) or not bool(remote.snapshot().player_identities[22].red_man):
		_fail("red identity update")
		return

	if not remote.apply_frame(_despawn(1,20,22,9)) or remote.snapshot().player_identities.has(22):
		_fail("despawn retained identity")
		return

	var unordered:=_world()
	if not unordered.apply_frame(pink) or not bool(unordered.snapshot().resync_pending) or str(unordered.snapshot().resync_reason)!="player_identity_without_spawn":
		_fail("identity before Spawn did not request resync")
		return

	var conflict:=_world()
	if not conflict.apply_frame(_entity(WireCodec.OPCODE_ENTITY_SPAWN,1,22,9,4,13,1,24,8,false,0,1)) or not conflict.apply_frame(pink):
		_fail("conflict baseline")
		return
	if not conflict.apply_frame(_identity(1,22,9,4,0,false,2200000000400,2200000030000,2199999999990,"測試玩家")) or not bool(conflict.snapshot().resync_pending):
		_fail("same revision conflict did not resync")
		return

	print("PVP_IDENTITY_WIRE_PASS variable_utf8=true self_generation=true spawn_order=true pvp_revision=true despawn_clear=true unordered_resync=true conflict_resync=true")
	quit(0)

func _world()->RefCounted:
	var world:=WorldState.new()
	world.state=WorldState.STATE_IN_WORLD
	world.actor_id=11
	world.map_id=13
	world.map_epoch=1
	world.world_ready=true
	world.last_aoi_tick=10
	world.last_world_ready_ack_epoch=1
	return world

func _identity(receiver:int,actor:int,connection:int,entity_epoch:int,mode:int,red:bool,
		server_ms:int,pink_until:int,updated:int,name:String)->PackedByteArray:
	var name_bytes:=name.to_utf8_buffer()
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(68+name_bytes.size())
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_PLAYER_IDENTITY)
	writer.put_64(receiver)
	writer.put_64(actor)
	writer.put_64(connection)
	writer.put_64(entity_epoch)
	writer.put_u8(mode)
	writer.put_u8(1 if red else 0)
	writer.put_64(server_ms)
	writer.put_64(pink_until)
	writer.put_64(updated)
	writer.put_u16(name_bytes.size())
	writer.put_data(name_bytes)
	return writer.data_array

func _entity(opcode:int,receiver:int,actor:int,connection:int,entity_epoch:int,map:int,
		revision:int,x:float,y:float,moving:bool,action:int,facing:int)->PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(74)
	writer.put_u16(1)
	writer.put_u16(opcode)
	writer.put_64(receiver)
	writer.put_64(11)
	writer.put_64(actor)
	writer.put_64(connection)
	writer.put_64(entity_epoch)
	writer.put_32(map)
	writer.put_64(revision)
	writer.put_float(x)
	writer.put_float(y)
	writer.put_u8(1 if moving else 0)
	writer.put_u32(action)
	writer.put_u8(facing)
	return writer.data_array

func _despawn(receiver:int,tick:int,actor:int,connection:int)->PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(40)
	writer.put_u16(1)
	writer.put_u16(WireCodec.OPCODE_ENTITY_DESPAWN)
	writer.put_64(receiver)
	writer.put_64(tick)
	writer.put_64(actor)
	writer.put_64(connection)
	return writer.data_array
