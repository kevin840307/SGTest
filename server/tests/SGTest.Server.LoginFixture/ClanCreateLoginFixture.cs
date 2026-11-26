using System.Net;
using System.Net.Sockets;
using SGTest.Server.Clan;
using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Map;
using SGTest.Server.Transport;
using SGTest.Server.Visibility;
using SGTest.Server.World;

internal static class ClanCreateLoginFixture
{
    internal const string ClanName = "RoyalGuild";

    internal static async Task<int> RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "sgtest_clan_create_login_" + Guid.NewGuid().ToString("N"));
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
                accountId, 0, "ClanCreateHero", new CharacterSpawn(15, 24, 24))
                .ConfigureAwait(false)).Character?.CharacterId
                ?? throw new InvalidOperationException("clan-create character create failed");

            await store.SetCharacterCombatProfileAsync(characterId,
                new CharacterCombatProfileData(
                    CombatProfileTrust.SyntheticTest,
                    "ci:tls-clan-create-fixture",
                    Race: 0, Career: 0, Strength: 5, Dexterity: 5,
                    Constitution: 5, Wisdom: 5, Level: 1,
                    CurrentHp: 100, MaxHp: 100, Attack: 10, Defense: 10,
                    Hit: 10, Dodge: 3, AttackRange: 16, AttackCooldownTicks: 3))
                .ConfigureAwait(false);

            var router = new WorldResultRouter();
            var pump = new WorldAoiPump(router);
            var clans = new ClanCoordinator(store);
            var authorization = new IdentitySessionAuthorization(
                store, world, clans: clans);
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
                    $"CLAN_CREATE_FIXTURE_READY port={port} " +
                    $"username={Program.Username} character={characterId}");
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
                            $"clan-create connection {connection} leaked session");
                    Console.WriteLine(
                        $"CLAN_CREATE_FIXTURE_CONNECTION_PASS index={connection} " +
                        $"commands={ended.CommandsAccepted}");
                    Console.Out.Flush();
                }

                var persisted = await store.GetClanForCharacterAsync(characterId)
                    .ConfigureAwait(false);
                if (persisted is null ||
                    persisted.Name != ClanName ||
                    persisted.LeaderCharacterId != characterId ||
                    persisted.Members.Count != 1 ||
                    persisted.Members[0].CharacterId != characterId)
                    throw new InvalidOperationException(
                        "clan-create authoritative persistence mismatch");

                Console.WriteLine(
                    $"CLAN_CREATE_FIXTURE_PASS clan_id={persisted.ClanId} " +
                    $"name={persisted.Name} leader={persisted.LeaderCharacterId} " +
                    "members=1 relog=true");
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
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("CLAN_CREATE_FIXTURE_FAIL " + error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
