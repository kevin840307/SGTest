using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class WorldAoiPumpChecks
{
    private static void Require(bool condition, string name) => Checks.Require(condition, name);

    public static void Verify(string root)
    {
        Checks.Run("live 30Hz two-player AOI pipeline sends WorldReady Spawn Delta and Despawn via Gateway FIFO",
            () => LiveTwoPlayers(root).GetAwaiter().GetResult());
        Checks.Run("AOI pump keeps unsent Spawn on reliable Gateway backpressure and fails closed",
            () => ReliableBackpressure().GetAwaiter().GetResult());
    }

    private static async Task LiveTwoPlayers(string root)
    {
        string folder = Checks.Fixture(root, 104, 12, 12, (_, _) => 0);
        await using var host = new ServerWorldHost([BlockingGrid.Load(folder, 104)]);
        await host.StartAsync();
        var a = await host.TrustedRegisterAsync(100, 104, 24, 24)
            ?? throw new Exception("first AOI player registration");
        var b = await host.TrustedRegisterAsync(200, 104, 40, 24)
            ?? throw new Exception("second AOI player registration");
        var ga = new GatewayConnection(a);
        var gb = new GatewayConnection(b);
        var router = new WorldResultRouter();
        Require(router.TryAttach(ga) && router.TryAttach(gb), "trusted Gateway attachments");
        var pump = new WorldAoiPump(router);
        using var stopPump = new CancellationTokenSource();
        var routerTask = router.RunAsync(host.Results);
        var pumpTask = pump.RunAsync(stopPump.Token);
        try
        {
            await WaitForFrames(ga, 2);
            await WaitForFrames(gb, 2);
            Require(ga.TryDequeueReliable(out var ar) &&
                    WireProtocol.TryDecodeWorldReady(ar, out var aReady) &&
                    aReady.ActorId == 100 && aReady.MapId == 104 &&
                    ga.TryDequeueReliable(out var asf) &&
                    WireProtocol.TryDecodeEntityState(asf, out var ao, out var aSpawn) &&
                    ao == WireOpcode.EntitySpawn && aSpawn.ActorId == 200,
                    "A WorldReady/Spawn order");
            Require(gb.TryDequeueReliable(out var br) &&
                    WireProtocol.TryDecodeWorldReady(br, out var bReady) &&
                    bReady.ActorId == 200 &&
                    gb.TryDequeueReliable(out var bsf) &&
                    WireProtocol.TryDecodeEntityState(bsf, out var bo, out var bSpawn) &&
                    bo == WireOpcode.EntitySpawn && bSpawn.ActorId == 100,
                    "B WorldReady/Spawn order");

            Require(host.TryPostMove(new MoveTargetCommand(
                b.ActorId, b.ConnectionEpoch, b.MapEpoch, 1, 88, 24)),
                "B Move enqueue");
            await WaitForFrames(ga, 1);
            Require(ga.TryDequeueReliable(out var movement) &&
                    WireProtocol.TryDecodeEntityState(movement, out var movedOpcode, out var moved) &&
                    movedOpcode == WireOpcode.EntityDelta &&
                    moved.ActorId == b.ActorId && moved.Revision > b.EntityRevision,
                    "A did not see B authoritative Delta");
            // A remains connected while B leaves; stale B socket is detached.
            Require(router.TryDetach(gb), "B trusted detach");
            Require(await host.TrustedDisconnectAsync(b.ActorId, b.ConnectionEpoch, b.MapEpoch),
                "B trusted World disconnect");
            // B may still have in-flight Delta; find Despawn in A's FIFO.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            bool despawnSeen = false;
            while (!despawnSeen)
            {
                timeout.Token.ThrowIfCancellationRequested();
                while (ga.TryDequeueReliable(out var frame))
                    if (WireProtocol.TryDecodeEntityDespawn(frame, out var gone) &&
                        gone.ActorId == b.ActorId && gone.ConnectionEpoch == b.ConnectionEpoch)
                        despawnSeen = true;
                if (!despawnSeen) await Task.Delay(5, timeout.Token);
            }
            Require(pump.ProcessedBatches > 0 && pump.FramesQueued >= 5,
                "AOI pump did not deliver bounded reliable frames");
        }
        finally
        {
            stopPump.Cancel();
            await pumpTask.WaitAsync(TimeSpan.FromSeconds(3));
            await host.StopAsync();
            await routerTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private static async Task ReliableBackpressure()
    {
        var a = new ActorSnapshot(1, 15, 1, 1, 0, 0, 24, 24, 1, false);
        var b = new ActorSnapshot(2, 15, 1, 1, 0, 0, 40, 24, 1, false);
        var ga = new GatewayConnection(a, new GatewayConnectionOptions(MaxReliableFrames: 1));
        var router = new WorldResultRouter();
        Require(router.TryAttach(ga), "authenticated receiver");
        router.Route(new WorldTickResult(1, Array.Empty<MoveReceipt>(),
            Array.Empty<ActorSnapshot>(),
            new[] {
                new ActorLifecycleEvent(ActorLifecycleKind.Joined, a),
                new ActorLifecycleEvent(ActorLifecycleKind.Joined, b)
            }));
        var pump = new WorldAoiPump(router);
        bool refused = false;
        try { await pump.RunAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (AoiBackpressureException) { refused = true; }
        Require(refused && ga.ReliableFrames == 1 &&
                ga.TryDequeueReliable(out var readyFrame) &&
                WireProtocol.TryDecodeWorldReady(readyFrame, out _) &&
                pump.Aoi.TryPeek(a.ActorId, a.ConnectionEpoch, a.MapEpoch, out var stillQueued) &&
                stillQueued.Kind == AoiEventKind.Spawn,
                "backpressure lost the reliable Spawn or bypassed queue cap");
    }

    private static async Task WaitForFrames(GatewayConnection connection, int expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (connection.ReliableFrames < expected)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(5, timeout.Token);
        }
    }
}
