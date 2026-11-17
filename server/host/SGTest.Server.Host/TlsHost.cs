using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class TlsHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var options = Parse(args);
            string root = Path.GetFullPath(Required(options, "--content"));
            string database = Path.GetFullPath(Required(options, "--identity"));
            string certificatePath = Path.GetFullPath(Required(options, "--certificate"));
            if (!IPAddress.TryParse(options.GetValueOrDefault("--bind", "127.0.0.1"), out var address))
                throw new ArgumentException("--bind must be an IP address");
            int port = Number(options, "--port", 7777, 0, 65535);
            int maximum = Number(options, "--max-connections", 128, 1, 4096);
            int perAddress = Number(options, "--max-per-address", Math.Min(8, maximum), 1, maximum);
            int checkpointMs = Number(options, "--position-checkpoint-ms", 5000, 500, 60000);
            if (options.ContainsKey("--seconds"))
                stop.CancelAfter(TimeSpan.FromSeconds(Number(options, "--seconds", 1, 1, 86400)));
            using var certificate = new X509Certificate2(certificatePath,
                Environment.GetEnvironmentVariable("SGTEST_TLS_PASSWORD"),
                // Schannel needs an accessible user-key container. Do not mark
                // it Exportable/PersistKeySet; disposal releases the temporary
                // imported key. Linux can use an ephemeral in-memory key.
                OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
            var maps = new[] { 13, 15 }.Select(id =>
                BlockingGrid.Load(Path.Combine(root, "maps", id.ToString()), id)).ToArray();
            var portals = PortalCatalog.LoadNpcDocuments(new[] { 13, 15 }.Select(id =>
                Path.Combine(root, "maps", id.ToString(), "npcs.json")));
            using var identity = new SqliteIdentityStore(database);
            await identity.InitializeAsync(stop.Token);
            await using var world = new ServerWorldHost(maps);
            var positions = new CharacterPositionCheckpoint(identity,
                new(IntervalMilliseconds: checkpointMs));
            var router = new WorldResultRouter(changedObserver: positions.Observe);
            var aoi = new WorldAoiPump(router);
            var authorization = new IdentityTlsAuthorization(identity, world, portals, positions);
            await using var listener = new TlsGatewayListener(address, port, authorization,
                certificate, router, new(MaxConnections: maximum, MaxConnectionsPerAddress: perAddress));
            await world.StartAsync();
            using var pumps = new CancellationTokenSource();
            var routing = router.RunAsync(world.Results, pumps.Token);
            var visibility = aoi.RunAsync(pumps.Token);
            var checkpointing = positions.RunAsync(pumps.Token);
            try
            {
                var accepting = listener.RunAsync(stop.Token);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    result = "TLS_HOST_READY", endpoint = listener.LocalEndpoint.ToString(),
                    max_connections = maximum, max_per_address = perAddress,
                    maps = maps.Select(m => m.MapId), audited_portals = portals.Count,
                    position_checkpoint_ms = checkpointMs
                }));
                var stopping = Task.Delay(Timeout.Infinite, stop.Token);
                var completed = await Task.WhenAny(accepting, routing, visibility, checkpointing, stopping);
                if (completed != stopping && !stop.IsCancellationRequested)
                {
                    await completed; // propagate faults; never serve after a result/AOI pump failed
                    throw new InvalidOperationException("A live server pipeline stopped unexpectedly");
                }
            }
            finally
            {
                stop.Cancel();
                try
                {
                    // World and pumps remain live until authenticated sockets have
                    // completed their trusted Disconnect/Router detach cleanup.
                    await listener.DisposeAsync();
                }
                finally
                {
                    await world.StopAsync();
                    pumps.Cancel();
                    try { await Task.WhenAll(routing, visibility, checkpointing); }
                    catch (OperationCanceledException) when (pumps.IsCancellationRequested) { }
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result = "TLS_HOST_STOPPED", listener = listener.Statistics,
                world = world.Statistics, router = router.Statistics,
                positions = positions.Statistics
            }));
            return 0;
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine("TLS_HOST_CONFIGURATION: " + error.Message);
            Console.Error.WriteLine("Usage: serve --content <PRIVATE_ROOT> --identity <DB> --certificate <PFX> " +
                "[--bind 127.0.0.1] [--port 7777] [--max-connections 128] [--max-per-address 8] " +
                "[--position-checkpoint-ms 5000] [--seconds N]");
            return 2;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception error) when (error is IOException or InvalidOperationException or
                                     CryptographicException or System.Net.Sockets.SocketException or
                                     WorldResultRoutingException or AoiBackpressureException or
                                     CharacterPositionCheckpointException)
        {
            Console.Error.WriteLine("TLS_HOST_FAIL: " + error.GetType().Name + ": " + error.Message);
            return 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        string[] known = ["--content", "--identity", "--certificate", "--bind", "--port",
                          "--max-connections", "--max-per-address", "--position-checkpoint-ms",
                          "--seconds"];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length % 2 != 0) throw new ArgumentException("Every option requires a value");
        for (int i = 0; i < args.Length; i += 2)
            if (!known.Contains(args[i]) || string.IsNullOrWhiteSpace(args[i + 1]) ||
                !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unknown, duplicate or empty option: " + args[i]);
        return values;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : throw new ArgumentException("Missing " + name);

    private static int Number(Dictionary<string, string> options, string name, int fallback, int min, int max)
    {
        if (!options.TryGetValue(name, out var value)) return fallback;
        return int.TryParse(value, out int number) && number >= min && number <= max
            ? number : throw new ArgumentException("Invalid " + name);
    }
}
