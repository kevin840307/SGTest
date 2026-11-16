using SGTest.Server.World;

internal static class WorldHostChecks
{
    private static void Require(bool valid, string name) => Checks.Require(valid, name);

    private static async Task WaitForTicks(ServerWorldHost host, long target, int milliseconds = 4000)
    {
        using var timeout = new CancellationTokenSource(milliseconds);
        while (host.Statistics.Ticks < target)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(5, timeout.Token);
        }
    }

    public static void Verify(string root)
    {
        string source1 = Checks.Fixture(root, 90, 12, 12, (_, _) => 0);
        string source2 = Checks.Fixture(root, 91, 12, 12, (_, _) => 0);
        var first = BlockingGrid.Load(source1, 90);
        var second = BlockingGrid.Load(source2, 91);
        Checks.Run("monotonic 30Hz deadline planner skips missed periods without catchup burst", () =>
        {
            var cadence = new FixedTickCadence(30, 3_000_000, 100);
            Require(cadence.PeriodTimestampTicks == 100_000 && cadence.Deadline == 100,
                "30Hz period timestamp");
            Require(cadence.AdvanceAfter(40_100) == 0 && cadence.Deadline == 100_100,
                "first deadline");
            long skipped = cadence.AdvanceAfter(450_100);
            Require(skipped == 3 && cadence.Deadline == 500_100,
                "must skip 3 deadlines rather than compress simulation steps");
            Require(cadence.AdvanceAfter(500_001) == 0 && cadence.Deadline == 600_100,
                "normal schedule recovers");
        });

        Checks.Run("host rejects invalid clock and outbound/control capacities", () =>
        {
            foreach (var options in new[] {
                new WorldHostOptions(TickHz: 0),
                new WorldHostOptions(TickHz: 121),
                new WorldHostOptions(TrustedControlCapacity: 0),
                new WorldHostOptions(MaxTrustedControlsPerTick: 0),
                new WorldHostOptions(ResultCapacity: 0)
            })
            {
                try { _ = new ServerWorldHost([first], options: options); }
                catch (ArgumentOutOfRangeException) { continue; }
                throw new Exception("Bad host settings were accepted");
            }
        });

        Checks.Run("real dedicated 30Hz host: trusted register, Move, output, stop and no restart",
            () => LiveHost([first]).GetAwaiter().GetResult());

        Checks.Run("30Hz host serializes trusted two-map transfer and rejects old movement",
            () => TrustedTransfer([first, second]).GetAwaiter().GetResult());

        Checks.Run("slow consumer fills bounded result channel and FAILS CLOSED, never drop oldest",
            () => FailClosedBackpressure([first]).GetAwaiter().GetResult());

        Checks.Run("bounded trusted ingress returns clear refusal and resolves pending on stop",
            () => BoundedTrustedIngress([first]).GetAwaiter().GetResult());
    }

    private static async Task LiveHost(BlockingGrid[] maps)
    {
        await using var host = new ServerWorldHost(maps, options:
            new WorldHostOptions(TickHz: 30, ResultCapacity: 64));
        await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var rejected = await host.TrustedRegisterAsync(123, 90, -1, 24).WaitAsync(TimeSpan.FromSeconds(3));
        Require(rejected is null, "invalid spawn must be refused");
        var actor = await host.TrustedRegisterAsync(123, 90, 24, 24).WaitAsync(TimeSpan.FromSeconds(3));
        Require(actor.HasValue && actor.Value.ConnectionEpoch == 1, "trusted register");
        var initial = host.Statistics.Ticks;
        var input = new MoveTargetCommand(actor.Value.ActorId, actor.Value.ConnectionEpoch,
            actor.Value.MapEpoch, 1, 168, 24);
        Require(host.TryPostMove(input), "host ingress refused a legitimate move");
        await WaitForTicks(host, initial + 12);

        var results = new List<WorldTickResult>();
        while (host.Results.TryRead(out var update)) results.Add(update);
        Require(results.Any(x => x.Receipts.Any(r => r.Status == MoveReceiptStatus.Accepted)),
            "authoritative Move receipt missing");
        Require(results.Any(x => x.Changed.Any(c => c.ActorId == 123 && c.X > 24 &&
            c.EntityRevision > actor.Value.EntityRevision)), "authoritative movement delta missing");
        Require(results.All(x => x.Receipts.Count <= 64 && x.Changed.Count <= 128),
            "one Tick exceeded bounded payload shape");
        long ticksBeforeStop = host.Statistics.Ticks;
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Require(host.Statistics.Ticks >= ticksBeforeStop && host.Statistics.SkippedDeadlines >= 0,
            "stopped counts invalid");
        Require(host.Statistics.Ticks >= 12 && host.Statistics.PublishedResults > 0 &&
                host.Statistics.ResultOverflowFailures == 0, "live cadence stats");
        Require(!host.TryPostMove(input), "post-stop ingress accepted");
        try { await host.TrustedRegisterAsync(124, 90, 24, 24); }
        catch (InvalidOperationException) { goto refused; }
        throw new Exception("trusted commands accepted after stop");
refused:
        await host.StopAsync();
        try { await host.StartAsync(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("already-stopped host started twice");
    }

    private static async Task TrustedTransfer(BlockingGrid[] maps)
    {
        await using var host = new ServerWorldHost(maps, options:
            new WorldHostOptions(TickHz: 30, ResultCapacity: 64));
        await host.StartAsync();
        var old = await host.TrustedRegisterAsync(33, 90, 24, 24);
        Require(old.HasValue, "register Map90");
        var changed = await host.TrustedTransferAsync(33, old.Value.ConnectionEpoch,
            old.Value.MapEpoch, 91, 40, 40);
        Require(changed.HasValue && changed.Value.MapId == 91 &&
                changed.Value.MapEpoch > old.Value.MapEpoch, "trusted Map90->Map91");
        Require(host.TryPostMove(new MoveTargetCommand(33, old.Value.ConnectionEpoch,
            old.Value.MapEpoch, 1, 72, 24)), "old Map command enqueue");
        var before = host.Statistics.Ticks;
        await WaitForTicks(host, before + 3);
        var items = new List<WorldTickResult>();
        while (host.Results.TryRead(out var tick)) items.Add(tick);
        Require(items.Any(t => t.Receipts.Any(r =>
            r.Status == MoveReceiptStatus.OldMapEpoch)),
            "late old-map movement escaped epoch rejection");
        Require(await host.TrustedDisconnectAsync(33, old.Value.ConnectionEpoch, old.Value.MapEpoch)
            == false, "stale disconnect removed transferred actor");
        Require(await host.TrustedDisconnectAsync(33, changed.Value.ConnectionEpoch,
            changed.Value.MapEpoch), "current disconnect refused");
        await host.StopAsync();
    }

    private static async Task FailClosedBackpressure(BlockingGrid[] maps)
    {
        var host = new ServerWorldHost(maps,
            options: new WorldHostOptions(ResultCapacity: 1));
        try
        {
            await host.StartAsync();
            var actor = await host.TrustedRegisterAsync(888, 90, 24, 24);
            Require(actor.HasValue, "register");
            Require(host.TryPostMove(new MoveTargetCommand(888, actor.Value.ConnectionEpoch,
                actor.Value.MapEpoch, 1, 168, 24)), "move enqueue");
            bool failedClosed = false;
            try { await host.Completion.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (WorldHostBackpressureException) { failedClosed = true; }
            Require(failedClosed && host.Statistics.ResultOverflowFailures == 1,
                "results queue overflow must fail closed");
            Require(host.Results.TryRead(out var firstResult) &&
                firstResult!.Receipts.Any(r => r.Status == MoveReceiptStatus.Accepted),
                "previously published reliable receipt lost");
            Require(!host.TryPostMove(new MoveTargetCommand(888,
                actor.Value.ConnectionEpoch, actor.Value.MapEpoch, 2, 24, 24)),
                "broken host accepted inbound command");
        }
        finally
        {
            // Fault from the deliberately-overflowed queue is expected on disposal too.
            try { await host.DisposeAsync(); }
            catch (WorldHostBackpressureException) { }
        }
    }

    private static async Task BoundedTrustedIngress(BlockingGrid[] maps)
    {
        await using var host = new ServerWorldHost(maps,
            worldLimits: new WorldLimits(MaxActors: 100),
            options: new WorldHostOptions(TrustedControlCapacity: 2,
                MaxTrustedControlsPerTick: 1));
        await host.StartAsync();
        var queued = new Task<ActorSnapshot?>[128];
        for (int i = 0; i < queued.Length; i++)
            queued[i] = host.TrustedRegisterAsync(1000 + i, 90, 24, 24);
        var immediateRefusals = queued.Count(t => t.IsFaulted);
        Require(immediateRefusals >= 100, "trusted ingress was not bounded");
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Require(queued.All(t => t.IsCompleted), "accepted trusted requests silently hung on Stop");
        Require(host.Statistics.PendingTrustedControls == 0,
            "trusted control queue was not drained on Stop");
        Require(!host.TryPostMove(new MoveTargetCommand(1001, 1, 1, 1, 50, 50)),
                "post-stop Move was admitted");
    }

    public static void VerifyOriginal(string assetsRoot)
    {
        Checks.Run("original Map13/Map15 fixed 30Hz Host loads strict hashes and shuts down",
            () => RunOriginal(assetsRoot).GetAwaiter().GetResult());
    }

    private static async Task RunOriginal(string assetsRoot)
    {
        var maps = new[] { 13, 15 }
            .Select(id => BlockingGrid.Load(Path.Combine(assetsRoot, "maps",
                id.ToString()), id)).ToArray();
        await using var host = new ServerWorldHost(maps);
        await host.StartAsync();
        var initial = host.Statistics.Ticks;
        await WaitForTicks(host, initial + 8);
        var from = await host.TrustedRegisterAsync(710, 13, 2560, 2880);
        Require(from.HasValue && from.Value.MapId == 13, "original Map13 authorized seed");
        var changed = await host.TrustedTransferAsync(710, from.Value.ConnectionEpoch,
            from.Value.MapEpoch, 15, 7872, 11296);
        Require(changed.HasValue && changed.Value.MapId == 15, "original Map15 transfer seed");
        var back = await host.TrustedTransferAsync(710, changed.Value.ConnectionEpoch,
            changed.Value.MapEpoch, 13, 2560, 2880);
        Require(back.HasValue && back.Value.MapEpoch > changed.Value.MapEpoch,
            "original Map13 back seed");
        await host.StopAsync();
        Require(host.Statistics.Ticks >= 8 && host.Statistics.ResultOverflowFailures == 0,
            "real-map 30Hz timing");
        Console.WriteLine($"REAL_WORLD_HOST_30HZ_PASS ticks={host.Statistics.Ticks} skipped={host.Statistics.SkippedDeadlines} max_tick_work_us={host.Statistics.MaxTickWorkMicroseconds} scope=simulation_only");
    }
}
