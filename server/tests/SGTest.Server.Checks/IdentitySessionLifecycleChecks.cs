using SGTest.Server.Map;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Transport;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class IdentitySessionLifecycleChecks
{
    private static void Require(bool ok, string name) => Checks.Require(ok, name);

    private sealed record Session(TcpClient Client, AesCbcFrameStream Stream,
        Task<AesWireConnectionResult> Server, int MapId, double X, double Y,
        long MapEpoch) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync();
            Client.Dispose();
        }
    }

    public static void Verify(string root)
    {
        Checks.Run("AES socket cleanup allows same character login again with a new generation",
            () => Reconnect(root).GetAwaiter().GetResult());
        Checks.Run("AES protocol violation releases authenticated actor for subsequent login",
            () => ProtocolFailureCleanup(root).GetAwaiter().GetResult());
        Checks.Run("AES clean disconnect persists final authoritative position before reconnect",
            () => PositionPersistsAcrossReconnect(root).GetAwaiter().GetResult());
    }

    private static async Task Reconnect(string root)
    {
        using var fixture = await Fixture.CreateAsync(root, 97);
        await using var first = await OpenAsync(fixture);
        long oldEpoch = first.MapEpoch;
        await first.DisposeAsync();
        var ended = await first.Server.WaitAsync(TimeSpan.FromSeconds(3));
        Require(ended.Reason is AesWireEndReason.CleanEof or AesWireEndReason.TransportError &&
                fixture.Router.Statistics.ActiveSessions == 0,
            "first AES socket failed to release its Gateway");
        await WaitForZeroActors(fixture.Host);

        await using var second = await OpenAsync(fixture);
        Require(second.MapEpoch > oldEpoch, "reconnected actor did not advance World MapEpoch");
        Require(fixture.Router.Statistics.ActiveSessions == 1,
            "second valid AES socket did not rebind receipt route");
        await second.DisposeAsync();
        var endedAgain = await second.Server.WaitAsync(TimeSpan.FromSeconds(3));
        Require(endedAgain.Reason is AesWireEndReason.CleanEof or AesWireEndReason.TransportError &&
                fixture.Router.Statistics.ActiveSessions == 0,
            "second socket cleanup left stale Gateway");
        await WaitForZeroActors(fixture.Host);
    }

    private static async Task PositionPersistsAcrossReconnect(string root)
    {
        using var fixture = await Fixture.CreateAsync(root, 99);
        await using var first = await OpenAsync(fixture);
        Require(first.X == 8 && first.Y == 8, "initial persisted position mismatch");
        await first.Stream.WriteAsync(WireProtocol.EncodeMoveTarget(
            new MoveTargetWire(first.MapEpoch, 1, 40, 8)));
        await first.Stream.FlushAsync();

        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            bool reached = false;
            while (!reached)
            {
                var tick = await fixture.Host.Results.ReadAsync(timeout.Token);
                reached = tick.Changed.Any(x => x.ActorId == fixture.CharacterId &&
                    Math.Abs(x.X - 40) < 0.001 && Math.Abs(x.Y - 8) < 0.001 &&
                    !x.IsMoving);
            }
        }

        await first.DisposeAsync();
        var ended = await first.Server.WaitAsync(TimeSpan.FromSeconds(3));
        Require(ended.Reason is AesWireEndReason.CleanEof or AesWireEndReason.TransportError,
            "moved session did not close cleanly");
        await WaitForZeroActors(fixture.Host);

        var stored = await fixture.Store.ListCharactersAsync(fixture.AccountId);
        Require(stored.Count == 1 && stored[0].MapId == first.MapId &&
            Math.Abs(stored[0].X - 40) < 0.001 && Math.Abs(stored[0].Y - 8) < 0.001,
            "final authoritative position not persisted");

        await using var second = await OpenAsync(fixture);
        Require(second.MapId == first.MapId &&
            Math.Abs(second.X - 40) < 0.001 && Math.Abs(second.Y - 8) < 0.001,
            "reconnect did not spawn from durable final position");
        await second.DisposeAsync();
        await second.Server.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitForZeroActors(fixture.Host);
    }

    private static async Task ProtocolFailureCleanup(string root)
    {
        using var fixture = await Fixture.CreateAsync(root, 98);
        await using var session = await OpenAsync(fixture);
        // Server-bound gameplay only accepts MoveTarget, never an AuthRequest.
        await session.Stream.WriteAsync(WireProtocol.EncodeAuthRequest(
            new AuthRequestWire("retry_user", "Retry-Password-123!")));
        await session.Stream.FlushAsync();
        var ended = await session.Server.WaitAsync(TimeSpan.FromSeconds(3));
        Require(ended.Reason == AesWireEndReason.ProtocolViolation &&
                fixture.Router.Statistics.ActiveSessions == 0,
            "protocol violation did not close and detach authenticated socket");
        await WaitForZeroActors(fixture.Host);
        await session.DisposeAsync();

        await using var retry = await OpenAsync(fixture);
        Require(retry.MapEpoch > session.MapEpoch, "protocol failure caused ghost actor on retry");
        await retry.DisposeAsync();
        await retry.Server.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitForZeroActors(fixture.Host);
    }

    private static async Task<Session> OpenAsync(Fixture fixture)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();

        var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var socket = await accepted;
        var server = fixture.Auth.RunAuthenticatedAesSocketAsync(socket,
            fixture.Settings, fixture.Router,
            new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds: 10000));
        var tls = AesSocketChecks.CreateClientStream(client); // test-only certificate
        try
        {

            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(
                new AuthRequestWire("retry_user", "Retry-Password-123!")));
            await tls.FlushAsync();
            Require(WireProtocol.TryDecodeAuthResult(await ReadFrameAsync(tls), out var auth) &&
                    auth.Status == AuthWireStatus.Success, "reconnect Auth failed");
            Require(WireProtocol.TryDecodeCharacterList(await ReadFrameAsync(tls), out var list) &&
                    list.Characters.Count == 1 && list.Characters[0].CharacterId == fixture.CharacterId,
                "reconnect list ownership failed");
            await CharacterChooseTestHarness.ChooseAsync(tls,fixture.CharacterId);
            await tls.FlushAsync();
            Require(WireProtocol.TryDecodeCharacterChooseResult(await ReadFrameAsync(tls), out var choose) &&
                    choose.Status == CharacterChooseWireStatus.Success &&
                    choose.CharacterId == fixture.CharacterId && choose.MapEpoch > 0,
                "reconnect World registration was rejected");
            // CharacterChooseResult is authored inside AuthorizeAndBindAsync;
            // the outer AES authorization callback attaches the Gateway route
            // immediately afterwards. Do not race that legal callback boundary
            // with an instantaneous ActiveSessions assertion.
            await WaitForActiveSessions(fixture.Router,1);
            return new Session(client, tls, server,
                choose.MapId, choose.X, choose.Y, choose.MapEpoch);
        }
        catch
        {
            await tls.DisposeAsync();
            client.Dispose();
            try { await server.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
            throw;
        }
    }

    private static async Task<byte[]> ReadFrameAsync(AesCbcFrameStream tls)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var header = new byte[8];
        await tls.ReadExactlyAsync(header, timeout.Token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        Require(length is >= 8 and <= 4096, "AES test frame bound");
        var frame = new byte[length];
        header.CopyTo(frame, 0);
        await tls.ReadExactlyAsync(frame.AsMemory(8), timeout.Token);
        return frame;
    }

    private static async Task WaitForActiveSessions(
        WorldResultRouter router,int expected)
    {
        var deadline=DateTime.UtcNow.AddSeconds(3);
        while(router.Statistics.ActiveSessions!=expected)
        {
            if(DateTime.UtcNow>=deadline)
                throw new Exception(
                    $"Gateway route count did not reach {expected}; actual={router.Statistics.ActiveSessions}");
            await Task.Delay(5);
        }
    }

    private static async Task WaitForZeroActors(ServerWorldHost host)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (host.Statistics.ActiveActors != 0)
        {
            if (DateTime.UtcNow >= deadline)
                throw new Exception("World actor was not released after socket ended");
            await Task.Delay(5);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public required ServerWorldHost Host;
        public required SqliteIdentityStore Store;
        public required WorldResultRouter Router;
        public required IdentitySessionAuthorization Auth;
        public required AesCbcSettings Settings;
        public long CharacterId;
        public long AccountId;

        public static async Task<Fixture> CreateAsync(string root, int mapId)
        {
            string mapPath = Checks.Fixture(root, mapId, 4, 4, (_, _) => 0);
            var host = new ServerWorldHost([BlockingGrid.Load(mapPath, mapId)]);
            await host.StartAsync();
            string db = Path.Combine(root, "identity_lifecycle_" + mapId + ".db");
            var store = new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long accountId = (await store.CreateAccountAsync("retry_user", "Retry-Password-123!")).AccountId;
            var created = await store.CreateCharacterAsync(accountId, 0, "RetryHero",
                new CharacterSpawn(mapId, 8, 8));
            long characterId = created.Character?.CharacterId ?? throw new Exception("fixture character missing");
            var router = new WorldResultRouter();
            return new Fixture { Host = host, Store = store,
                Router = router, Auth = new IdentitySessionAuthorization(store, host),
                Settings = AesSocketChecks.CreateSettings(),
                CharacterId = characterId, AccountId = accountId };
        }

        public void Dispose()
        {
            Host.StopAsync().GetAwaiter().GetResult();
            Host.DisposeAsync().AsTask().GetAwaiter().GetResult();

            Store.Dispose();
        }
    }
}
