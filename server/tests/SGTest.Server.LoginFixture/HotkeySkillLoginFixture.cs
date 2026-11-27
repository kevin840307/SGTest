using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Map;
using SGTest.Server.Monster;
using SGTest.Server.Skill;
using SGTest.Server.Transport;
using SGTest.Server.Visibility;
using SGTest.Server.World;

internal static class HotkeySkillLoginFixture
{
    internal const int SkillId = 28101;

    internal static async Task<int> RunAsync(bool expectCast = true)
    {
        string root = Path.Combine(Path.GetTempPath(),
            "sgtest_hotkey_skill_login_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var grid = BlockingGrid.Load(
                Program.CreateMap(root, 15, width: 40, height: 3), 15);
            string populationPath = Path.Combine(root, "hotkey-skill-population.json");
            File.WriteAllText(populationPath, JsonSerializer.Serialize(new
            {
                version = 4,
                kind = "verified_monster_runtime",
                map_id = 15,
                stats_verified = true,
                gameplay_ready = true,
                definitions = new[]
                {
                    new
                    {
                        id = 1, name = "HotkeySkillFixture", sprite = "EN001A",
                        atlas = "Enemy/EN001A/atlas.json",
                        stats = new
                        {
                            level = 3, max_hp = 100, attack_damage = 1,
                            move_step = 1.0, aggro_radius = 1.0,
                            lose_target_radius = 2.0, leash_radius = 2.0,
                            attack_range = 1.0, attack_cooldown_ticks = 1000,
                            respawn_ticks = 300, wander_radius = 0.0,
                            wander_interval_ticks = 100
                        }
                    }
                },
                spawns = new[]
                {
                    new { source_index = 0, source_flags = 7, definition = 1,
                          p = new[] { 40, 24 } }
                }
            }));
            var runtime = new MonsterRuntime(
                grid, MonsterPopulationCatalog.LoadVerified(populationPath, 15, grid),
                new(AiIntervalTicks: 1, MaxAiPerStep: 8,
                    MaxPathQueriesPerStep: 1, MaxPathExpandedCells: 128));
            var coordinator = new MonsterWorldCoordinator([runtime],
                new Dictionary<int, VerifiedSkillRule>
                {
                    [SkillId] = new(SkillId, 40, 48, 1, MpCost: 1)
                });

            await using var world = new ServerWorldHost(
                [grid], options: new WorldHostOptions(PublishIdleTicks: true));
            await world.StartAsync().ConfigureAwait(false);
            using var store = new SqliteIdentityStore(Path.Combine(root, "identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long accountId = (await store.CreateAccountAsync(
                Program.Username, Program.Password).ConfigureAwait(false)).AccountId;
            long characterId = (await store.CreateCharacterAsync(
                accountId, 0, "HotkeySkillHero", new CharacterSpawn(15, 24, 24))
                .ConfigureAwait(false)).Character?.CharacterId
                ?? throw new InvalidOperationException(
                    "hotkey-skill character create failed");
            await store.SetCharacterSkillLevelAsync(
                characterId, SkillId, 3, "ci:tls-hotkey-skill-fixture")
                .ConfigureAwait(false);
            await store.SetCharacterCombatProfileAsync(characterId,
                new CharacterCombatProfileData(
                    CombatProfileTrust.SyntheticTest,
                    "ci:tls-hotkey-skill-fixture",
                    Race: 0, Career: 0, Strength: 5, Dexterity: 5,
                    Constitution: 5, Wisdom: 5, Level: 1,
                    CurrentHp: 100, MaxHp: 100, Attack: 10, Defense: 10,
                    Hit: 10, Dodge: 3, AttackRange: 48,
                    AttackCooldownTicks: 15, CurrentMp: 2, MaxMp: 2))
                .ConfigureAwait(false);

            var router = new WorldResultRouter(
                new WorldResultRouterOptions(AttackRequestCapacity: 8));
            var combat = new PlayerCombatSettlement(
                store, new(Capacity: 8), router.QueuePlayerDamage);
            using var combatStop = new CancellationTokenSource();
            Task combatWorker = combat.RunAsync(combatStop.Token);
            var pump = new WorldAoiPump(
                router, monsters: coordinator, playerCombat: combat);
            var authorization = new IdentitySessionAuthorization(
                store, world, combat: combat);
            var settings = AesCbcSettings.FromEnvironment();

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(2);
            using var stop = new CancellationTokenSource();
            Task routing = router.RunAsync(world.Results);
            Task pumping = pump.RunAsync(stop.Token);
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"HOTKEY_SKILL_FIXTURE_READY port={port} " +
                    $"username={Program.Username} character={characterId} " +
                    $"skill_id={SkillId}");
                Console.Out.Flush();

                for (int connection = 1; connection <= 2; connection++)
                {
                    using var client = await listener.AcceptTcpClientAsync()
                        .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    var ended = await authorization.RunAuthenticatedAesSocketAsync(
                        client, settings, router,
                        new AesWireConnectionOptions(
                            AuthorizationTimeoutMilliseconds: 15000))
                        .WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false);
                    if (router.Statistics.ActiveSessions != 0)
                        throw new InvalidOperationException(
                            $"hotkey-skill connection {connection} leaked session");
                    Console.WriteLine(
                        $"HOTKEY_SKILL_FIXTURE_CONNECTION_PASS index={connection} " +
                        $"commands={ended.CommandsAccepted}");
                    Console.Out.Flush();
                }

                var monster = runtime.SnapshotAll().Single();
                var profile = await store.GetCharacterCombatProfileAsync(characterId)
                    .ConfigureAwait(false);
                var hotkeys = await store.ListCharacterHotkeysAsync(characterId)
                    .ConfigureAwait(false);
                int expectedHp = expectCast ? 60 : 100;
                int expectedMp = expectCast ? 1 : 2;
                int expectedSlot = expectCast ? 0 : 1;
                if (monster.CurrentHp != expectedHp || monster.MaxHp != 100 ||
                    profile?.Data.CurrentMp != expectedMp || profile.Data.MaxMp != 2 ||
                    hotkeys.Count != 1 || hotkeys[0].Slot != expectedSlot ||
                    hotkeys[0].DataType != 1 || hotkeys[0].DataId != SkillId)
                    throw new InvalidOperationException(
                        $"hotkey-skill persistence mismatch monster=" +
                        $"{monster.CurrentHp}/{monster.MaxHp} " +
                        $"mp={profile?.Data.CurrentMp}/{profile?.Data.MaxMp} " +
                        $"hotkeys={hotkeys.Count} expected_slot={expectedSlot}");

                Console.WriteLine(
                    $"HOTKEY_SKILL_FIXTURE_PASS skill_id={SkillId} " +
                    $"monster_hp={monster.CurrentHp}/{monster.MaxHp} " +
                    $"player_mp={profile!.Data.CurrentMp}/{profile.Data.MaxMp} " +
                    $"hotkey_slot={expectedSlot} relog=true native_drag={!expectCast}");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                await pumping.ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);
                combatStop.Cancel();
                try
                {
                    await combatWorker.WaitAsync(TimeSpan.FromSeconds(3))
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("HOTKEY_SKILL_FIXTURE_FAIL " + error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
