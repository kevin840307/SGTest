using System.Net;
using System.Net.Sockets;
using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Map;
using SGTest.Server.Transport;
using SGTest.Server.Visibility;
using SGTest.Server.World;

internal static class RecoveryHotkeyLoginFixture
{
    internal static async Task<int> RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "sgtest_recovery_hotkey_login_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var grid = BlockingGrid.Load(Program.CreateMap(root, 15), 15);
            await using var world = new ServerWorldHost(
                [grid], options: new WorldHostOptions(PublishIdleTicks: true));
            await world.StartAsync().ConfigureAwait(false);

            string database = Path.Combine(root, "identity.db");
            using var store = new SqliteIdentityStore(database);
            await store.InitializeAsync().ConfigureAwait(false);
            long accountId = (await store.CreateAccountAsync(
                Program.Username, Program.Password).ConfigureAwait(false)).AccountId;
            long characterId = (await store.CreateCharacterAsync(
                accountId, 0, "RecoveryHotkeyHero", new CharacterSpawn(15, 24, 24))
                .ConfigureAwait(false)).Character?.CharacterId
                ?? throw new InvalidOperationException(
                    "recovery-hotkey character create failed");

            await store.SetCharacterCombatProfileAsync(characterId,
                new CharacterCombatProfileData(
                    CombatProfileTrust.SyntheticTest,
                    "ci:tls-recovery-hotkey-fixture",
                    Race: 0, Career: 0, Strength: 5, Dexterity: 5,
                    Constitution: 5, Wisdom: 5, Level: 1,
                    CurrentHp: 60, MaxHp: 100, Attack: 10, Defense: 10,
                    Hit: 10, Dodge: 3, AttackRange: 16, AttackCooldownTicks: 3))
                .ConfigureAwait(false);

            long generation = await store.AllocateLootWorldGenerationAsync()
                .ConfigureAwait(false);
            var drops = await store.CreateTrustedGroundDropsAsync([
                new TrustedDropSpawn(
                    15, 24, 24, 1002802001,
                    SqliteIdentityStore.ComposeLootDeathRevision(generation, 1),
                    0, 1001, 2, LootSourceTrust.SyntheticTest,
                    "ci:tls-recovery-hotkey")
            ]).ConfigureAwait(false);
            var grant = await store.ClaimGroundDropAsync(
                new TrustedPickupContext(characterId, 19, 1, 1, 15, 24, 24),
                drops.Single().DropId).ConfigureAwait(false);
            if (grant.Status != PickupStatus.Granted || grant.ItemUid <= 0 ||
                grant.Quantity != 2)
                throw new InvalidOperationException(
                    "recovery-hotkey fixture could not seed durable inventory");

            var router = new WorldResultRouter();
            var pump = new WorldAoiPump(router);
            var combat = new PlayerCombatSettlement(
                store, delivery: router.QueuePlayerDamage);
            using var combatStop = new CancellationTokenSource();
            Task combatWorker = combat.RunAsync(combatStop.Token);
            var recoveryRules = new Dictionary<int, RecoveryItemRule>
            {
                [1001] = new RecoveryItemRule(1001, 30, 1)
            };
            var authorization = new IdentitySessionAuthorization(
                store, world, combat: combat,
                pickupWorldGeneration: generation, recoveryRules: recoveryRules);
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
                    $"RECOVERY_HOTKEY_FIXTURE_READY port={port} " +
                    $"username={Program.Username} character={characterId} " +
                    $"item_uid={grant.ItemUid}");
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
                            $"recovery-hotkey connection {connection} leaked session");
                    Console.WriteLine(
                        $"RECOVERY_HOTKEY_FIXTURE_CONNECTION_PASS index={connection} " +
                        $"commands={ended.CommandsAccepted}");
                    Console.Out.Flush();
                }

                var inventory = await store.ListInventoryItemsAsync(characterId)
                    .ConfigureAwait(false);
                var profile = await store.GetCharacterCombatProfileAsync(characterId)
                    .ConfigureAwait(false);
                var hotkeys = await store.ListCharacterHotkeysAsync(characterId)
                    .ConfigureAwait(false);
                if (inventory.Count != 1 ||
                    inventory[0].ItemUid != grant.ItemUid ||
                    inventory[0].ItemDefinitionId != 1001 ||
                    inventory[0].Quantity != 1 ||
                    profile?.Data.CurrentHp != 90 ||
                    profile.Data.MaxHp != 100 ||
                    hotkeys.Count != 1 ||
                    hotkeys[0].Slot != 0 ||
                    hotkeys[0].DataType != 0 ||
                    hotkeys[0].DataId != grant.ItemUid)
                    throw new InvalidOperationException(
                        $"recovery-hotkey persistence mismatch inventory={inventory.Count} " +
                        $"qty={(inventory.Count == 1 ? inventory[0].Quantity : -1)} " +
                        $"hp={profile?.Data.CurrentHp}/{profile?.Data.MaxHp} " +
                        $"hotkeys={hotkeys.Count}");

                Console.WriteLine(
                    $"RECOVERY_HOTKEY_FIXTURE_PASS item_uid={grant.ItemUid} " +
                    "quantity=1 hp=90/100 hotkey_slot=0 relog=true");
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
            Console.Error.WriteLine("RECOVERY_HOTKEY_FIXTURE_FAIL " + error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
