"""Stable source layout and cross-language wire naming checks.

Keep this free of Godot, .NET and private asset dependencies so every CI job
can validate architectural naming without running a game server.
"""
from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLIENT = ROOT / "client"
SERVER = ROOT / "server" / "src" / "SGTest.Server"
WIRE_CONTRACTS = SERVER / "Wire" / "WireContracts.cs"
WIRE_CODEC = CLIENT / "network" / "wire_codec.gd"


class ArchitectureConventionsChecks(unittest.TestCase):
    def test_feature_panels_follow_owning_directory(self) -> None:
        for folder, filename in (
            ("inventory", "inventory_panel.gd"),
            ("npc", "bank_panel.gd"),
            ("npc", "npc_shop_panel.gd"),
            ("npc", "storage_panel.gd"),
            ("npc", "repair_panel.gd"),
            ("npc", "enhancement_panel.gd"),
            ("trade", "trade_panel.gd"),
            ("chat", "chat_panel.gd"),
            ("party", "party_panel.gd"),
            ("player_shop", "player_shop_panel.gd"),
            ("quest", "quest_panel.gd"),
            ("pvp", "pvp_panel.gd"),
        ):
            with self.subTest(folder=folder):
                self.assertTrue((CLIENT / folder / filename).is_file())

    def test_npc_ui_owns_controls_without_extra_feature_directories(self) -> None:
        panel = (CLIENT / "session" / "session_panel.gd").read_text(encoding="utf-8-sig")
        for feature in ("bank", "npc_shop", "storage", "repair", "enhancement"):
            with self.subTest(feature=feature):
                script = CLIENT / "npc" / (feature + "_panel.gd")
                source = script.read_text(encoding="utf-8-sig")
                self.assertIn("func apply_snapshot(", source)
                self.assertIn("func reset()", source)
                self.assertIn("func set_actions(", source)
                self.assertIn(f'preload("res://npc/{feature}_panel.gd")', panel)
                self.assertFalse((CLIENT / feature).exists(),
                                 "Single-panel NPC directories must be consolidated")
                self.assertNotIn("var " + feature + "_npc_input", panel)

    def test_pvp_ui_keeps_its_own_controls_and_one_authoritative_session(self) -> None:
        session = (CLIENT / "session" / "session_panel.gd").read_text(encoding="utf-8-sig")
        pvp = (CLIENT / "pvp" / "pvp_panel.gd").read_text(encoding="utf-8-sig")
        self.assertIn('preload("res://pvp/pvp_panel.gd")', session)
        self.assertIn("func submit_pvp_mode(", session)
        self.assertIn("func submit_gm_pvp_name(", session)
        self.assertNotIn("var pvp_query_button", session)
        self.assertNotIn("var gm_pvp_state_picker", session)
        self.assertNotIn("func _update_pvp_ui(", session)
        self.assertIn("func _update_pvp_ui(", pvp)
        self.assertIn("func apply_snapshot(", pvp)
        self.assertIn("func set_actions(", pvp)
        self.assertIn("func reset()", pvp)
        self.assertNotIn("session.request_pvp_mode(", pvp)

    def test_chat_routing_is_separate_from_tls_authorization(self) -> None:
        gateway = (SERVER / "Gateway" / "IdentityTlsAuthorization.cs").read_text(
            encoding="utf-8-sig")
        routing = (SERVER / "Chat" / "ChatRouting.cs").read_text(
            encoding="utf-8-sig")
        self.assertIn("handleChat:request=>ChatRouting.RouteAsync(request,router,_parties)", gateway)
        self.assertNotIn("Task ChatHandler(", gateway)
        self.assertIn("public static Task RouteAsync(", routing)
        self.assertIn("router.IsCurrentBinding(sender)", routing)
        self.assertIn("router.IsVisibleActor(sender,recipient)", routing)
        self.assertIn("router.QueueWhisperResult(request,result)", routing)
        self.assertNotIn("TcpClient", routing)
        self.assertNotIn("SslStream", routing)

    def test_npc_item_modification_uses_one_authenticated_gameplay_gate(self) -> None:
        gateway = (SERVER / "Gateway" / "IdentityTlsAuthorization.cs").read_text(
            encoding="utf-8-sig")
        npc = (SERVER / "Npc" / "NpcItemModificationHandler.cs").read_text(
            encoding="utf-8-sig")
        self.assertEqual(gateway.count("new NpcItemModificationHandler("), 1)
        self.assertIn("router, gameplayGate)", gateway)
        self.assertIn("handleRepair:npcItems.HandleRepairAsync", gateway)
        self.assertIn("handleEnhancement:npcItems.HandleEnhancementAsync", gateway)
        self.assertNotIn("async Task RepairHandler(", gateway)
        self.assertNotIn("async Task EnhancementHandler(", gateway)
        self.assertIn("internal sealed class NpcItemModificationHandler", npc)
        self.assertIn("internal async Task HandleRepairAsync(", npc)
        self.assertIn("internal async Task HandleEnhancementAsync(", npc)
        self.assertEqual(npc.count("await _gameplayGate.WaitAsync()"), 2)
        self.assertEqual(npc.count("finally { _gameplayGate.Release(); }"), 2)
        self.assertEqual(npc.count("if(!_router.IsCurrentBinding(binding))return;"), 2)
        self.assertNotIn("new SemaphoreSlim", npc)
        self.assertNotIn("new WorldResultRouter", npc)

    def test_npc_economy_uses_one_authenticated_transaction_owner(self) -> None:
        gateway = (SERVER / "Gateway" / "IdentityTlsAuthorization.cs").read_text(
            encoding="utf-8-sig")
        economy = (SERVER / "Npc" / "NpcEconomyHandler.cs").read_text(
            encoding="utf-8-sig")
        self.assertEqual(gateway.count("new NpcEconomyHandler("), 1)
        self.assertIn("_shops, _storages, _banks, router, gameplayGate", gateway)
        for feature in ("Shop", "Storage", "Bank"):
            with self.subTest(feature=feature):
                self.assertIn(
                    f"handle{feature}:npcEconomy.Handle{feature}Async", gateway)
                self.assertNotIn(f"async Task {feature}Handler(", gateway)
                self.assertEqual(
                    economy.count(f"internal async Task Handle{feature}Async("), 1)
        self.assertEqual(economy.count("await _gameplayGate.WaitAsync()"), 3)
        self.assertEqual(economy.count("finally { _gameplayGate.Release(); }"), 3)
        self.assertEqual(
            economy.count("if(!_router.IsCurrentBinding(binding))return;"), 3)
        self.assertNotIn("new SemaphoreSlim", economy)
        self.assertNotIn("new WorldResultRouter", economy)
        self.assertNotIn("new SqliteIdentityStore", economy)
        self.assertNotIn("new TlsWireConnection", economy)

    def test_npc_quest_handler_keeps_trusted_gate_and_quest_authority(self) -> None:
        gateway = (SERVER / "Gateway" / "IdentityTlsAuthorization.cs").read_text(
            encoding="utf-8-sig")
        quest = (SERVER / "Npc" / "NpcQuestHandler.cs").read_text(
            encoding="utf-8-sig")
        self.assertEqual(gateway.count("new NpcQuestHandler("), 1)
        self.assertIn("handleQuest:npcQuests.HandleQuestAsync", gateway)
        self.assertIn("_combat, _quests, _pickupWorldGeneration", gateway)
        self.assertIn("router, gameplayGate)", gateway)
        self.assertNotIn("async Task QuestHandler(", gateway)
        self.assertIn("internal async Task HandleQuestAsync(", quest)
        self.assertEqual(quest.count("await _gameplayGate.WaitAsync()"), 1)
        self.assertEqual(quest.count("finally { _gameplayGate.Release(); }"), 1)
        self.assertIn("if(!_router.IsCurrentBinding(binding))return;", quest)
        self.assertIn("_identity.ApplyQuestChoiceAsync(", quest)
        self.assertIn("_router.QueueQuestView(", quest)
        self.assertNotIn("new SemaphoreSlim", quest)
        self.assertNotIn("new SqliteIdentityStore", quest)

    def test_pvp_commands_split_state_from_combat_with_one_gameplay_gate(self) -> None:
        gateway = (SERVER / "Gateway" / "IdentityTlsAuthorization.cs").read_text(
            encoding="utf-8-sig")
        state = (SERVER / "Pvp" / "PvpStateHandler.cs").read_text(
            encoding="utf-8-sig")
        combat = (SERVER / "Pvp" / "PvpCombatHandler.cs").read_text(
            encoding="utf-8-sig")
        self.assertEqual(gateway.count("new PvpStateHandler("), 1)
        self.assertEqual(gateway.count("new PvpCombatHandler("), 1)
        for route, owner in (
            ("handlePvp:pvpStates.HandleModeAsync", state),
            ("handleGmPvpName:pvpStates.HandleGmNameAsync", state),
            ("handlePvpAttack:pvpCombat.HandleAttackAsync", combat),
            ("handlePvpSkill:pvpCombat.HandleSkillAsync", combat),
        ):
            with self.subTest(route=route):
                self.assertIn(route, gateway)
        self.assertNotIn("PvpCommandHandler", gateway)
        self.assertFalse((SERVER / "Pvp" / "PvpCommandHandler.cs").exists())
        self.assertIn("private static string OnlineGmEventId(", state)
        self.assertNotIn("private static string OnlineGmEventId(", gateway)
        self.assertIn("private readonly GmPvpNameSequenceGate _gmNameSequenceGate", state)
        self.assertIn("_gmNameSequenceGate.Inspect(request)", state)
        self.assertIn("_attackSequenceGate.Inspect(request)", combat)
        for owner in (state, combat):
            self.assertEqual(owner.count("await _gameplayGate.WaitAsync()"), 2)
            self.assertEqual(owner.count("if(!_router.IsCurrentBinding(binding))return;"), 2)
            self.assertEqual(owner.count("_gameplayGate.Release()"), 2)
            self.assertNotIn("new SemaphoreSlim", owner)
            self.assertNotIn("new WorldResultRouter", owner)

    def test_inventory_commands_share_one_authenticated_uid_owner(self) -> None:
        gateway = (SERVER / "Gateway" / "IdentityTlsAuthorization.cs").read_text(
            encoding="utf-8-sig")
        inventory = (SERVER / "Inventory" / "InventoryCommandHandler.cs").read_text(
            encoding="utf-8-sig")
        self.assertEqual(gateway.count("new InventoryCommandHandler("), 1)
        self.assertIn("_pickupWorldGeneration, router, gameplayGate)", gateway)
        for connection, method in (
            ("inventory.HandlePickupAsync, recovery.HandleRecoveryItemAsync,",
             "HandlePickupAsync"),
            ("handleEquipment:inventory.HandleEquipmentAsync", "HandleEquipmentAsync"),
        ):
            with self.subTest(connection=connection):
                self.assertIn(connection, gateway)
                self.assertIn(f"internal async Task {method}(", inventory)
        self.assertEqual(inventory.count("internal async Task HandleDiscardAsync("), 1)
        self.assertEqual(inventory.count("await _gameplayGate.WaitAsync()"), 3)
        self.assertEqual(inventory.count("finally { _gameplayGate.Release(); }"), 3)
        self.assertEqual(inventory.count("_router.IsCurrentBinding(binding)"), 3)
        self.assertIn("_identity.ClaimGroundDropAsync(", inventory)
        self.assertIn("_identity.EquipInventoryItemAsync(", inventory)
        self.assertIn("_identity.DiscardInventoryItemAsync(", inventory)
        self.assertIn("_router.TryPostGroundDropClaimed(result.DropId)", inventory)
        for forbidden in ("new SemaphoreSlim", "new WorldResultRouter",
                          "new SqliteIdentityStore", "new TlsWireConnection"):
            self.assertNotIn(forbidden, inventory)
        for removed in ("async Task PickupHandler(", "async Task EquipmentHandler(",
                        "async Task DiscardHandler("):
            self.assertNotIn(removed, gateway)

    def test_combat_recovery_and_skill_handlers_preserve_single_gate(self) -> None:
        gateway = (SERVER / "Gateway" / "IdentityTlsAuthorization.cs").read_text(
            encoding="utf-8-sig")
        recovery = (SERVER / "Combat" / "PlayerRecoveryHandler.cs").read_text(
            encoding="utf-8-sig")
        skills = (SERVER / "Skill" / "SkillCommandHandler.cs").read_text(
            encoding="utf-8-sig")
        self.assertEqual(gateway.count("new PlayerRecoveryHandler("), 1)
        self.assertEqual(gateway.count("new SkillCommandHandler("), 1)
        for name in ("HandleRespawnAsync", "HandleRecoveryItemAsync"):
            self.assertEqual(recovery.count("internal async Task " + name + "("), 1)
            self.assertIn("recovery." + name, gateway)
        self.assertIn("handleSkill:skills.HandleSkillAsync", gateway)
        self.assertEqual(skills.count("internal async Task HandleSkillAsync("), 1)
        for owner, count in ((recovery, 2), (skills, 1)):
            self.assertEqual(owner.count("await _gameplayGate.WaitAsync()"), count)
            self.assertEqual(owner.count("_gameplayGate.Release()"), count)
            self.assertNotIn("new SemaphoreSlim", owner)
            self.assertNotIn("new SqliteIdentityStore", owner)
            self.assertNotIn("new WorldResultRouter", owner)
        self.assertEqual(recovery.count("_router.IsCurrentBinding(binding)"), 2)
        self.assertIn("_combat.SuspendAndDrainAsync(", recovery)
        self.assertIn("_identity.UseVerifiedRecoveryItemAsync(", recovery)
        self.assertLess(recovery.index("QueueRecoveryUseResult(request,result)"),
                        recovery.index("_combat!.TryResume(resumed)"))
        self.assertLess(recovery.index("QueuePlayerRespawnResult(request, outcome)"),
                        recovery.index("_combat.TryResume(actor)"))
        self.assertIn("_router.PrepareSkillAsync(request)", skills)
        self.assertIn("_identity.ConsumeSkillMpAsync(", skills)
        self.assertLess(skills.index("_router.QueuePlayerCombatState("),
                        skills.index("_router.FinishPreparedSkillAsync(prepared,cancel)"))
        for old in ("async Task RespawnHandler(", "async Task RecoveryHandler(",
                    "async Task SkillHandler("):
            self.assertNotIn(old, gateway)

    def test_character_reset_keeps_one_reducer_and_one_uid_location_policy(self) -> None:
        world = (CLIENT / "world" / "world_state.gd").read_text(encoding="utf-8-sig")
        self.assertEqual(world.count("func apply_frame("), 1)
        self.assertEqual(world.count("func _item_matches_inventory_or_equipment("), 1)
        self.assertNotIn("_enhancement_item_matches_location(", world)
        self.assertNotIn("_repair_item_matches_location(", world)
        choose = world.split("func _apply_choose(", 1)[1].split(
            "func _reset_character_actions_and_combat(", 1)[0]
        for name in (
            "_reset_character_actions_and_combat",
            "_reset_character_economy_state",
            "_reset_character_world_state",
        ):
            with self.subTest(method=name):
                self.assertIn(name + "(", choose)
                self.assertEqual(world.count("func " + name + "("), 1)
        self.assertNotIn("inventory_items.clear()", choose)
        self.assertNotIn("pvp_skill_visual_events.clear()", choose)

    def test_map_epoch_reset_is_partitioned_inside_one_world_reducer(self) -> None:
        world = (CLIENT / "world" / "world_state.gd").read_text(encoding="utf-8-sig")
        self.assertEqual(world.count("func apply_frame("), 1)
        self.assertEqual(world.count("func snapshot("), 1)
        ready = world.split("func _apply_world_ready(", 1)[1].split(
            "func _reset_map_combat_and_item_commands(", 1)[0]
        for name in (
            "_reset_map_combat_and_item_commands",
            "_reset_map_npc_services",
            "_reset_map_social_and_movement",
        ):
            with self.subTest(helper=name):
                self.assertEqual(world.count("func " + name + "("), 1)
                self.assertIn(name + "()", ready)
        self.assertLess(
            ready.index("_reset_map_combat_and_item_commands()"),
            ready.index("_reset_map_npc_services()"))
        self.assertLess(
            ready.index("_reset_map_npc_services()"),
            ready.index("_reset_map_social_and_movement()"))
        self.assertIn("if next_epoch > map_epoch:", ready)
        self.assertIn("var completing_resync :=", ready)
        self.assertNotIn("inventory_items.clear()", ready)
        self.assertNotIn("equipment_items.clear()", ready)
        self.assertIn("equipment_sequence = 0", world.split(
            "func _reset_map_npc_services(", 1)[1].split(
            "func _reset_map_social_and_movement(", 1)[0])
        self.assertIn("player_skill_events.clear()", world.split(
            "func _reset_map_combat_and_item_commands(", 1)[1].split(
            "func _reset_map_npc_services(", 1)[0])
        test = (CLIENT / "tests" / "test_world_state.gd").read_text(
            encoding="utf-8-sig")
        self.assertIn("func _verify_cross_map_reset()", test)
        self.assertIn("crossmap_preserves_owned_items=PASS", test)
        self.assertIn("same_epoch_resync=PASS", test)

    def test_all_player_damage_sources_share_one_authoritative_hp_timeline(self) -> None:
        world = (CLIENT / "world" / "world_state.gd").read_text(encoding="utf-8-sig")
        self.assertEqual(world.count("func _apply_authoritative_damage("), 1)
        self.assertEqual(world.count("return _apply_authoritative_damage(value, false)"), 1)
        self.assertEqual(world.count("return _apply_authoritative_damage(value, true)"), 1)
        authoritative = world.split("func _apply_authoritative_damage(", 1)[1].split(
            "func _apply_player_respawn_result(", 1)[0]
        for line in (
            "player_damage_sequence = sequence",
            "last_player_damage_tick = tick",
            "player_current_hp = int(value.current_hp)",
            'last_player_damage["source_kind"] = "pvp"',
        ):
            with self.subTest(statement=line):
                self.assertEqual(authoritative.count(line), 1)
        self.assertIn('return fail("%s_damage_sequence_gap" % source)', authoritative)
        self.assertIn('return fail("%s_damage_healed" % source)', authoritative)
        self.assertIn('return fail("%s_damage_tick_regressed" % source)', authoritative)
        test = (CLIENT / "tests" / "test_player_damage_wire.gd").read_text(
            encoding="utf-8-sig")
        self.assertIn("mixed_sources_single_hp_sequence=PASS", test)
        self.assertIn("monster gap after PvP hit did not fail closed", test)
        self.assertIn("mixed PvP/monster/PvP sequence diverged", test)

    def test_live_trade_fixture_waits_for_worldready_ack(self) -> None:
        fixture = (CLIENT / "tests" / "test_client_trade_tls.gd").read_text(
            encoding="utf-8-sig")
        session = (CLIENT / "session" / "client_session.gd").read_text(
            encoding="utf-8-sig")
        self.assertIn("int(v.pending_world_ready_ack_epoch)==0", fixture)
        self.assertIn("int(v.last_world_ready_ack_epoch)==int(v.map_epoch)", fixture)
        self.assertLess(
            fixture.index("int(v.last_world_ready_ack_epoch)==int(v.map_epoch)"),
            fixture.index("CLIENT_TRADE_READY role=observer"))
        self.assertIn('actor request "+str(session.snapshot())', fixture)
        trade = session.split("func _queue_trade_intent(", 1)[1].split(
            "func open_repair(", 1)[0]
        self.assertIn('pending_world_ready_ack_epoch', trade)
        self.assertIn('last_world_ready_ack_epoch', trade)
        self.assertIn('return false', trade)
        self.assertIn('return _queue_intent(', trade)

    def test_godot_scripts_use_snake_case_names(self) -> None:
        for script in CLIENT.rglob("*.gd"):
            if "assets" in script.parts:
                continue  # Generated/private source is owned by the converter.
            with self.subTest(path=script.relative_to(ROOT)):
                self.assertRegex(script.name, r"^[a-z][a-z0-9_]*\.gd$")

    def test_local_gdscript_preloads_refer_to_real_files(self) -> None:
        for script in CLIENT.rglob("*.gd"):
            if "assets" in script.parts:
                continue
            source = script.read_text(encoding="utf-8-sig")
            for relative in re.findall(r'preload\("res://([^"]+\.gd)"\)', source):
                with self.subTest(src=script.relative_to(ROOT), target=relative):
                    self.assertTrue((CLIENT / relative).is_file())

    def test_client_intent_dispatch_keeps_one_fifo_owner(self) -> None:
        source = (CLIENT / "session" / "client_session.gd").read_text(encoding="utf-8-sig")
        self.assertEqual(source.count("func _process_first_intent() -> void:"), 1)
        router = source.split("func _process_first_intent() -> void:", 1)[1]
        router = router.split("func _process_player_shop_intent(", 1)[0]
        self.assertLess(len(router.splitlines()), 150,
                        "ClientSession should route intentions, not embed every gameplay handler")
        self.assertIn("var intent := _peek_intent()", router)
        for name in (
            "_process_player_shop_intent", "_process_quest_intent",
            "_process_chat_intent", "_process_party_intent",
            "_process_trade_intent", "_process_bank_intent",
            "_process_storage_intent", "_process_pvp_attack_intent",
            "_process_inventory_discard_intent", "_process_monster_skill_intent",
        ):
            with self.subTest(name=name):
                self.assertIn(name + "(intent", router)
                self.assertEqual(source.count("func " + name + "("), 1)


    def test_client_framing_is_pure_and_wire_v1_contracts_have_one_owner(self) -> None:
        wire = WIRE_CODEC.read_text(encoding="utf-8-sig")
        framing = (CLIENT / "network" / "frame_codec.gd").read_text(
            encoding="utf-8-sig")
        self.assertIn('const FrameCodec = preload("res://network/frame_codec.gd")', wire)
        self.assertIn("const VERSION := 1", wire)
        self.assertIn("const HEADER_BYTES := 8", wire)
        self.assertIn("static func _writer(frame_bytes: int, opcode: int)", wire)
        self.assertIn("return FrameCodec.writer(frame_bytes, VERSION, opcode)", wire)
        self.assertIn("return FrameCodec.reader(frame, offset)", wire)
        self.assertIn("return FrameCodec.length_prefix(buffer, HEADER_BYTES, MAX_FRAME_BYTES)", wire)
        self.assertIn("return FrameCodec.valid_variable_header(", wire)
        self.assertIn("return FrameCodec.valid_header(", wire)
        self.assertGreaterEqual(wire.count("var writer := _writer("), 37)
        self.assertNotRegex(wire, r"(?m)^\s*var writer\s*:?=\s*StreamPeerBuffer\.new\(")
        self.assertNotRegex(framing, r"(?m)^const (?:VERSION|OPCODE_|HEADER_BYTES|MAX_FRAME_BYTES)")
        for forbidden in ("WorldState", "ClientSession", "PacketTransport",
                          "StreamPeerTCP", "TCPClient", "OS.get_", "Thread.new"):
            self.assertNotIn(forbidden, framing)
        for helper in ("reader", "writer", "length_prefix", "valid_header",
                       "valid_variable_header"):
            self.assertEqual(framing.count("static func " + helper + "("), 1)
        tests = (CLIENT / "tests" / "test_wire_codec.gd").read_text(
            encoding="utf-8-sig")
        self.assertIn("shared header writer changed golden V1 bytes", tests)
        self.assertIn("shared header gate did not reject invalid V1 framing", tests)

    def test_wire_contracts_have_one_owner(self) -> None:
        contracts = WIRE_CONTRACTS.read_text(encoding="utf-8-sig")
        self.assertIn("public enum WireOpcode : ushort", contracts)
        self.assertNotIn("public static partial class WireProtocol", contracts)
        for part in ("WireProtocol.cs", "WireProtocol.Economy.cs",
                     "WireProtocol.Social.cs", "WireProtocol.Pvp.cs",
                     "WireProtocol.Quest.cs", "WireProtocol.Combat.cs",
                     "WireProtocol.Identity.cs", "WireProtocol.Inventory.cs",
                     "WireProtocol.Shop.cs", "WireProtocol.World.cs"):
            with self.subTest(part=part):
                source = (SERVER / "Wire" / part).read_text(encoding="utf-8-sig")
                self.assertIn("public static partial class WireProtocol", source)
                self.assertNotIn("public enum WireOpcode : ushort", source)

    def test_identity_storage_feature_files_keep_one_database_owner(self) -> None:
        identity = SERVER / "Identity"
        core = (identity / "SqliteIdentityStore.cs").read_text(encoding="utf-8-sig")
        self.assertIn("public sealed partial class SqliteIdentityStore", core)
        self.assertNotIn("public async Task InitializeAsync(", core)
        self.assertNotIn("public async Task<CharacterCombatProfile?> GetCharacterCombatProfileAsync(", core)
        for filename, method in (
            ("SqliteIdentityStore.Schema.cs", "InitializeAsync("),
            ("SqliteIdentityStore.Combat.cs", "GetCharacterCombatProfileAsync("),
        ):
            with self.subTest(part=filename):
                source = (identity / filename).read_text(encoding="utf-8-sig")
                self.assertIn("public sealed partial class SqliteIdentityStore", source)
                self.assertIn(method, source)
        self.assertIn("public sealed record IdentityStoreOptions(",
                      (identity / "IdentityContracts.cs").read_text(encoding="utf-8-sig"))

    def test_gateway_authorization_binding_has_one_owner(self) -> None:
        gateway = SERVER / "Gateway"
        core = (gateway / "IdentityTlsAuthorization.cs").read_text(encoding="utf-8-sig")
        binding = (gateway / "IdentityTlsAuthorization.Binding.cs").read_text(encoding="utf-8-sig")
        self.assertIn("public sealed partial class IdentityTlsAuthorization", core)
        self.assertIn("public sealed partial class IdentityTlsAuthorization", binding)
        self.assertNotIn("public async ValueTask<GatewayConnection?> AuthorizeAndBindAsync(", core)
        self.assertEqual(binding.count("AuthorizeAndBindAsync("), 1)

    def test_shared_opcode_numbers_match_across_languages(self) -> None:
        # WireOpcode declarations are the durable public API; no display name,
        # localized state, or presentation color is allowed to renumber them.
        contract = WIRE_CONTRACTS.read_text(encoding="utf-8-sig")
        match = re.search(r"public enum WireOpcode\s*:\s*ushort\s*\{([^}]*)\}", contract, re.S)
        self.assertIsNotNone(match)
        csharp = {
            re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", name).upper(): int(code)
            for name, code in re.findall(r"\b([A-Za-z][A-Za-z0-9]*)\s*=\s*(\d+)", match.group(1))
        }
        godot = {
            name: int(code)
            for name, code in re.findall(
                r"^const OPCODE_([A-Z][A-Z0-9_]*)\s*:=\s*(\d+)\s*$",
                WIRE_CODEC.read_text(encoding="utf-8-sig"),
                re.M,
            )
        }
        common = csharp.keys() & godot.keys()
        self.assertGreaterEqual(len(common), 70, "Unrecognized wire naming drift")
        for name in sorted(common):
            with self.subTest(opcode=name):
                self.assertEqual(csharp[name], godot[name])


if __name__ == "__main__":
    unittest.main()
