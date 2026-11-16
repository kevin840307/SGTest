using System.Diagnostics;
using System.Text.Json;
using SGTest.Server.World;

internal static class Program
{
    // The original bounded offline smoke remains available. Network startup is
    // explicit and requires a deployment certificate and persistent identity DB.
    public static async Task<int> Main(string[] args)
    {
        if (args is ["serve", .. var serveArgs]) return await TlsHost.RunAsync(serveArgs);
        if (args is ["account-create", .. var accountArgs]) return await IdentityAdmin.RunAsync(true, accountArgs);
        if (args is ["character-create", .. var characterArgs]) return await IdentityAdmin.RunAsync(false, characterArgs);
        if (args.Length != 4 || args[0] != "--content" || args[2] != "--seconds" ||
            !int.TryParse(args[3], out int seconds) || seconds is < 1 or > 30)
        {
            Console.Error.WriteLine("Usage: SGTest.Server.Host serve|account-create|character-create <options>; " +
                "offline smoke: --content <PRIVATE_MAPS_ROOT> --seconds <1..30>");
            return 2;
        }
        try
        {
            string root = Path.GetFullPath(args[1]);
            var maps = new[] { 13, 15 }
                .Select(id => BlockingGrid.Load(Path.Combine(root, "maps", id.ToString()), id))
                .ToArray();
            await using var host = new ServerWorldHost(maps);
            await host.StartAsync();
            var stopwatch = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            await host.StopAsync();
            stopwatch.Stop();
            var stats = host.Statistics;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result = "WORLD_HOST_STOPPED",
                mode = "map13_map15_fixed_tick_simulation_NO_NETWORK_NO_AUTH",
                maps = maps.Select(map => new { id = map.MapId, map.Width, map.Height,
                    collision_sha256 = map.SourceSha256 }).ToArray(),
                target_tick_hz = host.TickHz,
                observed_tick_hz = stats.Ticks / stopwatch.Elapsed.TotalSeconds,
                wall_ms = stopwatch.Elapsed.TotalMilliseconds,
                stats
            }));
            if (stats.Ticks < seconds * 15 || stats.Ticks > seconds * 45 ||
                stats.ResultOverflowFailures != 0)
            {
                Console.Error.WriteLine("Host cadence or output bounds outside smoke window");
                return 3;
            }
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                     InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"WORLD_HOST_FAIL: {error.GetType().Name}: {error.Message}");
            return 1;
        }
    }
}
