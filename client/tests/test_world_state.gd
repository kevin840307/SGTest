extends SceneTree

const WireCodec = preload("res://network/wire_codec.gd")
const WorldState = preload("res://world/world_state.gd")
const AUTH_OK := "0900000001000B0000"
const REGISTER_CREATED := "090000000100120000"
const REGISTER_RATE_LIMITED := "090000000100120004"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const RECEIPT := "1E0000000100020001000000000000000100000000002A00000000000000"
const OLD_MAP_RECEIPT := "1E0000000100020001000000000000000100000000002A00000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const EMPTY_LIST := "0900000001000C0000"
const CREATE_RESULT := "1B000000010010000021000000000000000007004E65774865726F"
const CREATED_LIST := "1B00000001000C000121000000000000000007004E65774865726F"
const CREATION_PROFILE := "20000000010086000B0000000000000002000000020000000200000002000000"
const PROFILE := "49000000010085000B0000000000000002000000030000000400000040E2010000000000050000000600000007000000080000005A00000050000000460000003C0000000200000001"
const COMBAT := "210000000100070001000000000000003C000000640000001E0000002800000001"

func _fail(message: String) -> void:
	push_error("WORLD_STATE_FAIL " + message)
	quit(1)

func _initialize() -> void:
	var registration:=WorldState.new()
	if (
		not registration.mark_registration_sent() or
		not registration.apply_frame(REGISTER_CREATED.hex_decode()) or
		int(registration.snapshot().state)!=WorldState.STATE_NEW or
		int(registration.snapshot().registration_status)!=0 or
		not registration.mark_auth_sent()
	):
		_fail("registration created did not require ordinary auth")
		return
	var limited:=WorldState.new()
	if (
		not limited.mark_registration_sent() or
		not limited.apply_frame(REGISTER_RATE_LIMITED.hex_decode()) or
		int(limited.snapshot().state)!=WorldState.STATE_FAILED or
		int(limited.snapshot().registration_status)!=4
	):
		_fail("registration rate limit did not fail closed")
		return

	var creation = WorldState.new()
	if not creation.mark_auth_sent() or not creation.apply_frame(AUTH_OK.hex_decode()) or 		not creation.apply_frame(EMPTY_LIST.hex_decode()):
		_fail("empty account auth/list transition")
		return
	if not creation.mark_character_create_sent(0,"NewHero") or 		not creation.apply_frame(CREATE_RESULT.hex_decode()):
		_fail("character create transition")
		return
	if int(creation.snapshot().state) != WorldState.STATE_WAIT_CHARACTER_LIST or 		not creation.apply_frame(CREATED_LIST.hex_decode()):
		_fail("character create refreshed list")
		return
	var created := creation.snapshot()
	if int(created.state) != WorldState.STATE_SELECTING_CHARACTER or 		int(created.last_created_character_id) != 33 or 		str(created.last_created_name) != "NewHero" or 		(created.characters as Array).size() != 1:
		_fail("created character state %s" % created)
		return

	var world = WorldState.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH_OK.hex_decode()) or not world.apply_frame(LIST.hex_decode()):
		_fail("auth/list transition")
		return
	# Character selection must clear every domain from a previous actor/session.
	# These are intentionally poisoned *before* the trusted choose reply.
	world.npc_interaction_sequence = 10
	world.pending_npc_sequence = 10
	world.skill_sequence = 6
	world.pending_skill_sequence = 6
	world.player_skill_events.append({"skill_id":101})
	world.player_dead = true
	world.inventory_items[777] = {"item_uid":777,"quantity":1}
	world.shop_money = 999
	world.storage_items[777] = {"item_uid":777}
	world.bank_money = 555
	world.repair_items[777] = {"item_uid":777}
	world.enhancement_items[777] = {"item_uid":777}
	world.trade_id = 99
	world.trade_my_items[777] = {"item_uid":777}
	world.pvp_sequence = 7
	world.pending_pvp_sequence = 7
	world.respawn_sequence = 12
	world.entities[22] = {"actor_id":22}
	world.player_identities[22] = {"actor_id":22}
	world.monsters[1001] = {"entity_id":1001}
	world.ground_drops[7] = {"drop_id":7}
	world.pending_world_ready_ack_epoch = 9
	world.last_world_ready_ack_epoch = 9
	world.resync_pending = true
	world.world_ready = true
	if not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()):
		_fail("choose transition")
		return
	var chosen: Dictionary = world.snapshot()
	if (
		int(chosen.state) != WorldState.STATE_IN_WORLD or
		int(chosen.actor_id) != 11 or int(chosen.map_epoch) != 1 or
		int(chosen.pending_npc_sequence) != 0 or
		int(chosen.pending_skill_sequence) != 0 or
		not chosen.player_skill_events.is_empty() or bool(chosen.player_dead) or
		not chosen.inventory_items.is_empty() or
		not chosen.storage_items.is_empty() or int(chosen.shop_money) != 0 or
		int(chosen.bank_money) != 0 or
		not chosen.repair_items.is_empty() or
		not chosen.enhancement_items.is_empty() or
		int(chosen.trade_id) != 0 or not chosen.trade_my_items.is_empty() or
		int(chosen.pvp_sequence) != 0 or
		int(chosen.pending_pvp_sequence) != 0 or
		int(chosen.respawn_sequence) != 0 or
		not chosen.entities.is_empty() or
		not chosen.player_identities.is_empty() or
		not chosen.monsters.is_empty() or
		not chosen.ground_drops.is_empty() or
		int(chosen.pending_world_ready_ack_epoch) != 0 or
		int(chosen.last_world_ready_ack_epoch) != 0 or
		bool(chosen.resync_pending) or bool(chosen.world_ready)
	):
		_fail("chosen character retained a previous actor's combat, UID, PvP or WorldReady state")
		return
	if world.mark_move_sent(1):
		_fail("move escaped WorldReady gate")
		return
	if not world.apply_frame(CREATION_PROFILE.hex_decode()):
		_fail("authoritative creation profile baseline rejected")
		return
	var creation_profile_snapshot:=world.snapshot()
	if not bool(creation_profile_snapshot.player_creation_profile_ready) or bool(creation_profile_snapshot.player_profile_ready) or \
		int(creation_profile_snapshot.player_strength)!=2 or int(creation_profile_snapshot.player_dexterity)!=2 or \
		int(creation_profile_snapshot.player_constitution)!=2 or int(creation_profile_snapshot.player_wisdom)!=2:
		_fail("creation profile did not expose only persisted starting attributes: "+str(creation_profile_snapshot))
		return
	if not world.apply_frame(PROFILE.hex_decode()):
		_fail("authoritative player profile baseline rejected")
		return
	var profile_snapshot:=world.snapshot()
	if not bool(profile_snapshot.player_profile_ready) or int(profile_snapshot.player_level)!=4 or \
		int(profile_snapshot.player_experience)!=123456 or int(profile_snapshot.player_strength)!=5 or \
		int(profile_snapshot.player_dexterity)!=6 or int(profile_snapshot.player_constitution)!=7 or \
		int(profile_snapshot.player_wisdom)!=8 or int(profile_snapshot.player_attack)!=90 or \
		int(profile_snapshot.player_defense)!=80 or int(profile_snapshot.player_hit)!=70 or \
		int(profile_snapshot.player_evasion)!=60 or str(profile_snapshot.character_name)!="Hero":
		_fail("player profile snapshot did not expose authoritative values: "+str(profile_snapshot))
		return
	if not world.apply_frame(READY.hex_decode()):
		_fail("WorldReady apply")
		return
	var ready_state := world.snapshot()
	if (
		not bool(ready_state.equipment_ready) or
		not (ready_state.equipment_items as Dictionary).is_empty() or
		int(ready_state.equipment_revision) != 1
	):
		_fail("WorldReady did not resolve an omitted zero-equipment baseline")
		return
	if int(ready_state.pending_world_ready_ack_epoch) != 1 or int(ready_state.pending_world_ready_ack_tick) != 42 or not world.mark_world_ready_ack_sent(1,42):
		_fail("WorldReady ACK lifecycle")
		return
	if not world.mark_move_sent(1):
		_fail("world/move transition")
		return
	if not world.apply_frame(RECEIPT.hex_decode()):
		_fail("receipt apply")
		return
	var after := world.snapshot()
	if int(after.last_receipt_sequence) != 1 or int(after.last_server_tick) != 42 or int(after.last_receipt_status) != 0:
		_fail("receipt state %s" % after)
		return
	if not world.apply_frame(OLD_MAP_RECEIPT.hex_decode()) or int(world.snapshot().stale_receipts) != 1:
		_fail("duplicate receipt not ignored")
		return
	var future := _mutate_i64(RECEIPT.hex_decode(), 8, 2)
	if world.apply_frame(future) or int(world.snapshot().state) != WorldState.STATE_FAILED:
		_fail("future map receipt did not fail closed")
		return
	if not _verify_cross_map_reset():
		return
	print("WORLD_STATE_PASS registration_then_auth=PASS registration_rate_limit=FAIL_CLOSED auth_create_refresh_choose_ready_ack_move=PASS choose_clears_prior_actor=PASS crossmap_preserves_owned_items=PASS crossmap_clears_pending=PASS same_epoch_resync=PASS duplicate_receipt=IGNORED future_map=FAIL_CLOSED")
	quit(0)

func _verify_cross_map_reset() -> bool:
	var transfer = WorldState.new()
	if not transfer.mark_auth_sent() or not transfer.apply_frame(AUTH_OK.hex_decode()) or not transfer.apply_frame(LIST.hex_decode()) or not transfer.mark_choose_sent(11) or not transfer.apply_frame(CHOOSE.hex_decode()) or not transfer.apply_frame(READY.hex_decode()):
		_fail("crossmap fixture login failed")
		return false
	if not transfer.apply_frame(PROFILE.hex_decode()) or not transfer.apply_frame(COMBAT.hex_decode()):
		_fail("crossmap fixture could not initialize player stats")
		return false
	# Authenticated owned item locations persist; map-scoped command intent,
	# NPC sessions, world AOI and combat visuals must never follow the actor.
	transfer.inventory_items[301] = {"item_uid": 301, "item_definition_id": 201, "quantity": 3}
	transfer.inventory_ready = true
	transfer.inventory_revision = 4
	transfer.equipment_items[302] = {"item_uid": 302, "item_definition_id": 202, "slot_id": 3}
	transfer.equipment_slots[3] = 302
	transfer.equipment_ready = true
	transfer.equipment_revision = 5
	transfer.skill_sequence = 6
	transfer.pending_skill_sequence = 6
	transfer.player_skill_events.append({"skill_id": 101})
	transfer.pending_recovery_sequence = 7
	transfer.pending_discard_sequence = 8
	transfer.pending_pickup_sequence = 9
	transfer.shop_sequence = 4
	transfer.pending_shop_sequence = 4
	transfer.active_shop_npc = 99
	transfer.shop_money = 650
	transfer.shop_items[10] = {"item_definition_id": 10}
	transfer.storage_sequence = 5
	transfer.pending_storage_sequence = 5
	transfer.bank_sequence = 2
	transfer.pending_bank_sequence = 2
	transfer.repair_sequence = 3
	transfer.pending_repair_sequence = 3
	transfer.enhancement_sequence = 4
	transfer.pending_enhancement_sequence = 4
	transfer.trade_id = 33
	transfer.pvp_sequence = 5
	transfer.pending_pvp_sequence = 5
	transfer.respawn_sequence = 4
	transfer.entities[55] = {"actor_id": 55}
	transfer.monsters[61] = {"entity_id": 61}
	transfer.ground_drops[75] = {"drop_id": 75}
	transfer.waiting_for_new_map = true
	var next_ready := _mutate_i64(READY.hex_decode(), 8, 2)
	# WorldReady layout: header (8), epoch (8), tick (8), actor (8),
	# map ID (4), x (4), y (4), facing (1).
	next_ready[32] = 13
	if not transfer.apply_frame(next_ready):
		_fail("trusted map-epoch 2 WorldReady rejected")
		return false
	var changed: Dictionary = transfer.snapshot()
	if (
		int(changed.map_epoch) != 2 or int(changed.map_id) != 13 or
		not bool(changed.world_ready) or bool(changed.waiting_for_new_map) or
		not changed.player_profile_ready or changed.player_level != 4 or
		changed.player_experience != 123456 or changed.player_wisdom != 8 or
		not changed.combat_available or changed.player_current_hp != 60 or changed.player_max_hp != 100 or
		changed.player_current_mp != 30 or changed.player_max_mp != 40 or
		not changed.inventory_items.has(301) or
		not changed.equipment_items.has(302) or
		int(changed.equipment_slots.get(3, 0)) != 302 or
		int(changed.inventory_revision) != 4 or
		int(changed.equipment_revision) != 5 or
		int(changed.pending_skill_sequence) != 0 or
		not changed.player_skill_events.is_empty() or
		int(changed.pending_recovery_sequence) != 0 or
		int(changed.pending_discard_sequence) != 0 or
		int(changed.pending_pickup_sequence) != 0 or
		int(changed.pending_shop_sequence) != 0 or
		int(changed.active_shop_npc) != 0 or
		not changed.shop_items.is_empty() or
		int(changed.shop_money) != 0 or
		int(changed.pending_storage_sequence) != 0 or
		int(changed.pending_bank_sequence) != 0 or
		int(changed.pending_repair_sequence) != 0 or
		int(changed.pending_enhancement_sequence) != 0 or
		int(changed.trade_id) != 0 or
		int(changed.pending_pvp_sequence) != 0 or
		int(changed.respawn_sequence) != 0 or
		not changed.entities.is_empty() or
		not changed.monsters.is_empty() or
		not changed.ground_drops.is_empty()
	):
		_fail("map change leaked old map intent/AOI or discarded authoritative owned items")
		return false
	# A stale previous-map baseline cannot rewind the new-map actor.
	if not transfer.apply_frame(READY.hex_decode()):
		_fail("old WorldReady was not ignored")
		return false
	if int(transfer.snapshot().map_epoch) != 2:
		_fail("old WorldReady rewound map epoch")
		return false
	# Same-epoch resync replaces AOI, but must not clear owned items or
	# map-scoped command generations as a new map transfer would.
	transfer.resync_pending = true
	transfer.shop_sequence = 17
	var same_epoch := _mutate_i64(next_ready, 16, 43)
	if not transfer.apply_frame(same_epoch):
		_fail("same-epoch resync rejected")
		return false
	var resynced: Dictionary = transfer.snapshot()
	if (
		int(resynced.resync_completions) != 1 or
		int(resynced.shop_sequence) != 17 or
		not resynced.inventory_items.has(301) or
		not resynced.equipment_items.has(302) or
		bool(resynced.resync_pending)
	):
		_fail("same-map resync changed durable items or reset command sequence")
		return false
	return true

func _mutate_i64(source: PackedByteArray, offset: int, value: int) -> PackedByteArray:
	var copy := source.duplicate()
	for i in range(8):
		copy[offset + i] = (value >> (i * 8)) & 0xff
	return copy
