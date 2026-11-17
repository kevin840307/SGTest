using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class TlsGatewayListenerChecks
{
    public static void Verify(string root)
    {
        Checks.Run("TLS listener rejects invalid bounds and public-only certificate before binding",
            () => InvalidConfiguration(root).GetAwaiter().GetResult());
        Checks.Run("TLS listener global capacity and cancelled slow handshake release socket and slot",
            () => Admission(root, false).GetAwaiter().GetResult());
        Checks.Run("TLS listener per-address capacity refuses without allocating another session",
            () => Admission(root, true).GetAwaiter().GetResult());
        Checks.Run("TLS listener authenticates reconnect, WorldReady, MoveReceipt and cleans actors on shutdown",
            () => AuthenticatedReconnect(root).GetAwaiter().GetResult());
        Checks.Run("Plaintext and invalid credentials close only that client and release admission",
            () => UntrustedClients(root).GetAwaiter().GetResult());
        Checks.Run("TLS session infrastructure failure stops admission and is observable by supervisor",
            () => SessionFailure(root).GetAwaiter().GetResult());
        Checks.Run("Host serve boots TLS on synthetic maps, stops cleanly and rejects invalid CLI",
            () => HostEntry(root).GetAwaiter().GetResult());
    }

    private static async Task InvalidConfiguration(string root)
    {
        await using var fixture = await Fixture.Create(root);
        bool bounds = false, certificate = false;
        try { await using var invalid = fixture.Listener(new(MaxConnections: 0)); }
        catch (ArgumentOutOfRangeException) { bounds = true; }
        using var publicOnly = new X509Certificate2(fixture.Certificate.Export(X509ContentType.Cert));
        try
        {
            await using var invalid = new TlsGatewayListener(IPAddress.Loopback, 0,
                fixture.Authorization, publicOnly, fixture.Router);
        }
        catch (ArgumentException) { certificate = true; }
        Checks.Require(bounds && certificate, "listener accepted unsafe deployment configuration");
    }

    private static async Task Admission(string root, bool perAddress)
    {
        await using var fixture = await Fixture.Create(root);
        await using var listener = fixture.Listener(new(MaxConnections: perAddress ? 2 : 1,
            MaxConnectionsPerAddress: 1));
        using var stop = new CancellationTokenSource();
        var run = listener.RunAsync(stop.Token);
        var endpoint = listener.LocalEndpoint;
        using var first = new TcpClient();
        await first.ConnectAsync(endpoint.Address, endpoint.Port);
        await Until(() => listener.Statistics.ActiveConnections == 1);
        using var refused = new TcpClient();
        await refused.ConnectAsync(endpoint.Address, endpoint.Port);
        await Until(() => perAddress ? listener.Statistics.RejectedAddress == 1
                                    : listener.Statistics.RejectedCapacity == 1);
        Checks.Require(listener.Statistics.AcceptedConnections == 1 &&
            listener.Statistics.ActiveAddresses == 1, "rejection allocated TLS/identity session");
        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Checks.Require(listener.Statistics.ActiveConnections == 0 &&
            listener.Statistics.ActiveAddresses == 0 && listener.Statistics.CompletedConnections == 1 &&
            fixture.Router.Statistics.ActiveSessions == 0, "slow handshake leaked admission capacity");
        using var replacement = new TcpListener(endpoint);
        replacement.Start();
        replacement.Stop();
    }

    private static async Task AuthenticatedReconnect(string root)
    {
        await using var fixture = await Fixture.Create(root);
        await using var listener = fixture.Listener(new(MaxConnections: 2, MaxConnectionsPerAddress: 2));
        using var stop = new CancellationTokenSource();
        var run = listener.RunAsync(stop.Token);
        long previousEpoch = 0;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(listener.LocalEndpoint);
            using var tls = new SslStream(client.GetStream(), false, (_, cert, _, _) =>
                cert?.GetCertHashString() == fixture.Certificate.GetCertHashString());
            await TlsSocketChecks.AuthenticateClient(tls);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new("listener_user", "Listener-Password-123!")));
            Checks.Require(WireProtocol.TryDecodeAuthResult(await Read(tls), out var auth) &&
                auth.Status == AuthWireStatus.Success, "listener Auth failed");
            Checks.Require(WireProtocol.TryDecodeCharacterList(await Read(tls), out var list) &&
                list.Characters.Count == 1, "listener character ownership/list failed");
            await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(fixture.CharacterId)));
            Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(await Read(tls), out var choose) &&
                choose.Status == CharacterChooseWireStatus.Success && choose.MapEpoch > previousEpoch,
                "listener reconnect did not advance generation");
            previousEpoch = choose.MapEpoch;
            Checks.Require(WireProtocol.TryDecodeWorldReady(await Read(tls), out var ready) &&
                ready.ActorId == fixture.CharacterId && ready.ReceiverMapEpoch == choose.MapEpoch,
                "listener did not deliver authoritative WorldReady");
            await tls.WriteAsync(WireProtocol.EncodeWorldReadyAck(
                new(ready.ReceiverMapEpoch, ready.ServerTick)));
            await tls.WriteAsync(WireProtocol.EncodeMoveTarget(new(choose.MapEpoch, 1, 24, 8)));
            bool receipt = false;
            for (int frame = 0; frame < 16 && !receipt; frame++)
            {
                var bytes = await Read(tls);
                receipt = WireProtocol.TryDecodeMoveReceipt(bytes, out var value) &&
                    value.MovementSequence == 1 && value.Status == MoveReceiptStatus.Accepted;
            }
            Checks.Require(receipt, "listener failed to route reliable MoveReceipt");
            if (attempt == 1)
            {
                stop.Cancel(); // still-authenticated socket; server must release actor first
                await run.WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                await tls.DisposeAsync();
                await Until(() => listener.Statistics.ActiveConnections == 0 &&
                    fixture.Host.Statistics.ActiveActors == 0);
            }
        }
        var stats = listener.Statistics;
        Checks.Require(stats.AcceptedConnections == 2 && stats.CompletedConnections == 2 &&
            stats.ActiveConnections == 0 && stats.ActiveAddresses == 0 && stats.CommandsAccepted == 2 &&
            fixture.Host.Statistics.ActiveActors == 0 && fixture.Router.Statistics.ActiveSessions == 0,
            "shutdown left authenticated actor/socket/route or incorrect command accounting");
        await Task.WhenAll(listener.DisposeAsync().AsTask(), listener.DisposeAsync().AsTask());
    }

    private static async Task SessionFailure(string root)
    {
        await using var fixture = await Fixture.Create(root);
        var listener = fixture.Listener(new(MaxConnections: 2, MaxConnectionsPerAddress: 2));
        var run = listener.RunAsync();
        var endpoint = listener.LocalEndpoint;
        // Deliberately invalidate the borrowed certificate after startup. This
        // models an infrastructure fault, not an untrusted malformed packet.
        fixture.Certificate.Dispose();
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint);
        bool failed = false;
        try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception error) when (error is CryptographicException or InvalidOperationException or ObjectDisposedException)
        { failed = true; }
        finally
        {
            try { await listener.DisposeAsync(); }
            catch (Exception error) when (error is CryptographicException or InvalidOperationException or ObjectDisposedException) { }
        }
        Checks.Require(failed && listener.Statistics.ActiveConnections == 0 &&
            listener.Statistics.ActiveAddresses == 0 && fixture.Router.Statistics.ActiveSessions == 0,
            "session failure was hidden or left open admission/socket/route");
        using var replacement = new TcpListener(endpoint);
        replacement.Start();
        replacement.Stop();
    }

    private static async Task UntrustedClients(string root)
    {
        await using var fixture = await Fixture.Create(root);
        await using var listener = fixture.Listener(new(MaxConnections: 2, MaxConnectionsPerAddress: 2,
            Connection: new(HandshakeTimeoutMilliseconds: 500, AuthorizationTimeoutMilliseconds: 1000)));
        using var stop = new CancellationTokenSource();
        var run = listener.RunAsync(stop.Token);
        using (var plaintext = new TcpClient())
        {
            await plaintext.ConnectAsync(listener.LocalEndpoint);
            await plaintext.GetStream().WriteAsync("not-a-TLS-client"u8.ToArray());
        }
        await Until(() => listener.Statistics.CompletedConnections == 1);
        Checks.Require(!run.IsCompleted, "untrusted plaintext stopped all admission");
        using (var denied = new TcpClient())
        {
            await denied.ConnectAsync(listener.LocalEndpoint);
            using var tls = new SslStream(denied.GetStream(), false, (_, cert, _, _) =>
                cert?.GetCertHashString() == fixture.Certificate.GetCertHashString());
            await TlsSocketChecks.AuthenticateClient(tls);
            await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new("listener_user", "Wrong-Password-123!")));
            Checks.Require(WireProtocol.TryDecodeAuthResult(await Read(tls), out var result) &&
                result.Status == AuthWireStatus.InvalidCredentials, "invalid login was not rejected");
        }
        await Until(() => listener.Statistics.CompletedConnections == 2);
        Checks.Require(!run.IsCompleted && listener.Statistics.ActiveConnections == 0 &&
            fixture.Router.Statistics.ActiveSessions == 0 && fixture.Host.Statistics.ActiveActors == 0,
            "invalid login killed listener or left untrusted actor/socket");
        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task HostEntry(string root)
    {
        await using var fixture = await Fixture.Create(root);
        string content = Path.Combine(root, "listener-host-content");
        foreach (int id in new[] { 13, 15 })
        {
            string path = Checks.Fixture(Path.Combine(content, "maps"), id, 4, 4, (_, _) => 0);
            File.WriteAllText(Path.Combine(path, "npcs.json"), JsonSerializer.Serialize(new
            {
                schema = "sgtest.npcs/v1", coordinate_space = "world_pixels", map_id = id,
                npcs = Array.Empty<object>()
            }));
        }
        string pfx = Path.Combine(content, "test-only.pfx");
        File.WriteAllBytes(pfx, fixture.Certificate.Export(X509ContentType.Pfx, "Listener-PFX-test-only"));
        string repository = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(repository, "server", "host")))
            repository = Directory.GetParent(repository)?.FullName ?? throw new Exception("Host build not found");
        string hostDll = Path.Combine(repository, "server/host/SGTest.Server.Host/bin/Release/net8.0/SGTest.Server.Host.dll");
        async Task<(int Exit, string Out, string Error)> Run(params string[] args)
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add(hostDll);
            foreach (string arg in args) start.ArgumentList.Add(arg);
            start.Environment["SGTEST_TLS_PASSWORD"] = "Listener-PFX-test-only";
            using var process = Process.Start(start) ?? throw new Exception("Host process not started");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            catch { process.Kill(entireProcessTree: true); throw; }
            return (process.ExitCode, await output, await errors);
        }
        var valid = await Run("serve", "--content", content, "--identity", Path.Combine(content, "identity.db"),
            "--certificate", pfx, "--port", "0", "--seconds", "1");
        Checks.Require(valid.Exit == 0 && valid.Out.Contains("TLS_HOST_READY") &&
            valid.Out.Contains("TLS_HOST_STOPPED") && valid.Out.Contains("\"ActiveConnections\":0"),
            "serve startup/shutdown failed: " + valid.Error);
        var invalid = await Run("serve", "--content", content, "--identity", Path.Combine(content, "identity.db"),
            "--certificate", pfx, "--bind", "not-an-ip");
        Checks.Require(invalid.Exit == 2 && !invalid.Out.Contains("TLS_HOST_READY"),
            "invalid host configuration opened a listener");
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(5)) throw new Exception("Listener condition timed out");
            await Task.Delay(5);
        }
    }

    private static async Task<byte[]> Read(SslStream tls)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] header = new byte[8];
        await tls.ReadExactlyAsync(header, timeout.Token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        Checks.Require(length is >= 8 and <= 4096, "TLS frame bound");
        byte[] frame = new byte[length];
        header.CopyTo(frame, 0);
        await tls.ReadExactlyAsync(frame.AsMemory(8), timeout.Token);
        return frame;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required ServerWorldHost Host;
        public required SqliteIdentityStore Store;
        public required X509Certificate2 Certificate;
        public required WorldResultRouter Router;
        public required IdentityTlsAuthorization Authorization;
        public long CharacterId;
        public required Task Routing;
        public required Task Visibility;

        public static async Task<Fixture> Create(string root)
        {
            string path = Checks.Fixture(root, 115, 4, 4, (_, _) => 0);
            var host = new ServerWorldHost([BlockingGrid.Load(path, 115)]);
            await host.StartAsync();
            var store = new SqliteIdentityStore(Path.Combine(root, "listener-"+Guid.NewGuid().ToString("N")+".db"));
            await store.InitializeAsync();
            long account = (await store.CreateAccountAsync("listener_user", "Listener-Password-123!")).AccountId;
            var character = await store.CreateCharacterAsync(account, 0, "ListenerHero", new(115, 8, 8));
            var router = new WorldResultRouter();
            var aoi = new WorldAoiPump(router);
            return new() { Host = host, Store = store, Certificate = TlsSocketChecks.CreateCertificate(),
                Router = router, Authorization = new(store, host), CharacterId = character.Character!.CharacterId,
                Routing = router.RunAsync(host.Results), Visibility = aoi.RunAsync() };
        }

        public TlsGatewayListener Listener(TlsGatewayListenerOptions options) =>
            new(IPAddress.Loopback, 0, Authorization, Certificate, Router, options);

        public async ValueTask DisposeAsync()
        {
            await Host.StopAsync();
            await Task.WhenAll(Routing, Visibility).WaitAsync(TimeSpan.FromSeconds(5));
            await Host.DisposeAsync();
            Certificate.Dispose();
            Store.Dispose();
        }
    }
}
