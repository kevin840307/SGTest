using SGTest.Server.Map;
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
        Checks.Run("monster clock opt-in publishes bounded idle 30Hz ticks without changing default host",
            () => IdleTickPublication([first]).GetAwaiter().GetResult());

        Checks.Run("30Hz host serializes trusted two-map transfer and rejects old movement",
            () => TrustedTransfer([first, second]).GetAwaiter().GetResult());

        Checks.Run("graceful persistence barrier freezes queued movement and blocks relogin race",
            () => PersistenceBarrier([first]).GetAwaiter().GetResult());

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
        var actorMaybe = await host.TrustedRegisterAsync(123, 90, 24, 24).WaitAsync(TimeSpan.FromSeconds(3));
        var actor = actorMaybe ?? throw new Exception("trusted register returned null");
        Require(actor.ConnectionEpoch == 1, "trusted register");
        var initial = host.Statistics.Ticks;
        var input = new MoveTargetCommand(actor.ActorId, actor.ConnectionEpoch,
            actor.MapEpoch, 1, 168, 24);
        Require(host.TryPostMove(input), "host ingress refused a legitimate move");
        await WaitForTicks(host, initial + 12);

        var results = new List<WorldTickResult>();
        while (host.Results.TryRead(out var update)) results.Add(update);
        Require(results.Any(x => x.Receipts.Any(r => r.Status == MoveReceiptStatus.Accepted)),
            "authoritative Move receipt missing");
        Require(results.Any(x => x.Changed.Any(c => c.ActorId == 123 && c.X > 24 &&
            c.EntityRevision > actor.EntityRevision)), "authoritative movement delta missing");
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

    private static async Task IdleTickPublication(BlockingGrid[] maps)
    {
        await using var defaultHost = new ServerWorldHost(
            maps, options:new WorldHostOptions(ResultCapacity:16));
        await defaultHost.StartAsync();
        var before = defaultHost.Statistics.Ticks;
        await WaitForTicks(defaultHost,before+4);
        Require(!defaultHost.Results.TryRead(out _),
            "player-only host unexpectedly published empty ticks");
        await defaultHost.StopAsync();

        await using var monsterHost = new ServerWorldHost(
            maps, options:new WorldHostOptions(ResultCapacity:16,PublishIdleTicks:true));
        await monsterHost.StartAsync();
        before = monsterHost.Statistics.Ticks;
        await WaitForTicks(monsterHost,before+4);
        int count=0;
        long prior=0;
        while (monsterHost.Results.TryRead(out var tick))
        {
            Require(tick.Tick>prior && tick.Receipts.Count==0 &&
                    tick.Changed.Count==0 &&
                    (tick.Lifecycle is null || tick.Lifecycle.Count==0),
                    "idle monster clock tick shape/order");
            prior=tick.Tick;
            count++;
        }
        Require(count>=4 && monsterHost.Statistics.PublishedResults>=count,
            "monster clock did not publish bounded idle ticks");
        await monsterHost.StopAsync();
    }

    private static async Task TrustedTransfer(BlockingGrid[] maps)
    {
        await using var host = new ServerWorldHost(maps, options:
            new WorldHostOptions(TickHz: 30, ResultCapacity: 64));
        await host.StartAsync();
        var oldMaybe = await host.TrustedRegisterAsync(33, 90, 24, 24);
        var old = oldMaybe ?? throw new Exception("register Map90 returned null");
        var changedMaybe = await host.TrustedTransferAsync(33, old.ConnectionEpoch,
            old.MapEpoch, 91, 40, 40);
        var changed = changedMaybe ?? throw new Exception("trusted Map90->Map91 returned null");
        Require(changed.MapId == 91 && changed.MapEpoch > old.MapEpoch,
            "trusted Map90->Map91");
        Require(host.TryPostMove(new MoveTargetCommand(33, old.ConnectionEpoch,
            old.MapEpoch, 1, 72, 24)), "old Map command enqueue");
        var before = host.Statistics.Ticks;
        await WaitForTicks(host, before + 3);
        var items = new List<WorldTickResult>();
        while (host.Results.TryRead(out var tick)) items.Add(tick);
        Require(items.Any(t => t.Receipts.Any(r =>
            r.Status == MoveReceiptStatus.OldMapEpoch)),
            "late old-map movement escaped epoch rejection");
        Require(await host.TrustedDisconnectAsync(33, old.ConnectionEpoch, old.MapEpoch)
            == false, "stale disconnect removed transferred actor");
        Require(await host.TrustedDisconnectAsync(33, changed.ConnectionEpoch,
            changed.MapEpoch), "current disconnect refused");
        await host.StopAsync();
    }

    private static async Task PersistenceBarrier(BlockingGrid[] maps)
    {
        await using var host = new ServerWorldHost(maps, options:
            new WorldHostOptions(TickHz: 30, ResultCapacity: 64));
        await host.StartAsync();
        var actor = await host.TrustedRegisterAsync(444, 90, 24, 24)
            ?? throw new Exception("persistence actor register failed");
        Require(host.TryPostMove(new MoveTargetCommand(
            actor.ActorId, actor.ConnectionEpoch, actor.MapEpoch, 1, 120, 24)),
            "queued move before release rejected");

        var final = await host.TrustedPrepareDisconnectAsync(
            actor.ActorId, actor.ConnectionEpoch, actor.MapEpoch)
            ?? throw new Exception("prepare disconnect rejected current actor");
        // Move may already have advanced on a prior 30 Hz tick before the
        // trusted prepare command is processed. The snapshot must freeze the
        // actual authoritative position, not necessarily the spawn position.
        Require(final.MapId == 90 && final.X >= 24 && final.X <= 120 &&
                final.Y == 24 && !final.IsMoving,
            "prepare disconnect did not capture a legal frozen snapshot");

        var duplicate = await host.TrustedRegisterAsync(actor.ActorId, 90, 40, 40);
        Require(duplicate is null,
            "same character re-entered before persistence barrier completed");

        long before = host.Statistics.Ticks;
        await WaitForTicks(host, before + 2);
        // Reading all buffered tick results would include legitimate movement
        // emitted before prepare. Query the World owner after the barrier
        // instead: any post-prepare motion must be rejected.
        var frozen = await host.TrustedGetActorAsync(actor.ActorId)
            ?? throw new Exception("prepared actor disappeared before persistence");
        Require(frozen.X == final.X && frozen.Y == final.Y && !frozen.IsMoving,
            "queued movement changed actor after persistence snapshot");

        Require(await host.TrustedDisconnectAsync(
            final.ActorId, final.ConnectionEpoch, final.MapEpoch),
            "prepared actor could not complete disconnect");
        var next = await host.TrustedRegisterAsync(actor.ActorId, 90, final.X, final.Y)
            ?? throw new Exception("same character could not enter after persistence barrier completed");
        Require(next.ConnectionEpoch > actor.ConnectionEpoch,
            "reconnected character did not advance connection generation");
        await host.TrustedDisconnectAsync(next.ActorId,
            next.ConnectionEpoch, next.MapEpoch);
        await host.StopAsync();
    }

    private static async Task FailClosedBackpressure(BlockingGrid[] maps)
    {
        var host = new ServerWorldHost(maps,
            options: new WorldHostOptions(ResultCapacity: 1));
        try
        {
            await host.StartAsync();
            var actorMaybe = await host.TrustedRegisterAsync(888, 90, 24, 24);
            var actor = actorMaybe ?? throw new Exception("register returned null");
            // Lifecycle events now publish even without Move. Consume the initial
            // trusted Join before intentionally saturating the one-slot queue.
            using (var joinTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                while (true)
                {
                    var initial = await host.Results.ReadAsync(joinTimeout.Token);
                    if (initial.Lifecycle?.Any(e => e.Kind == ActorLifecycleKind.Joined &&
                                                   e.Actor.ActorId == 888) == true)
                        break;
                }
            }
            Require(host.TryPostMove(new MoveTargetCommand(888, actor.ConnectionEpoch,
                actor.MapEpoch, 1, 168, 24)), "move enqueue");
            bool failedClosed = false;
            try { await host.Completion.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (WorldHostBackpressureException) { failedClosed = true; }
            Require(failedClosed && host.Statistics.ResultOverflowFailures == 1,
                "results queue overflow must fail closed");
            Require(host.Results.TryRead(out var firstResult) &&
                firstResult!.Receipts.Any(r => r.Status == MoveReceiptStatus.Accepted),
                "previously published reliable receipt lost");
            Require(!host.TryPostMove(new MoveTargetCommand(888,
                actor.ConnectionEpoch, actor.MapEpoch, 2, 24, 24)),
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
        var fromMaybe = await host.TrustedRegisterAsync(710, 13, 2560, 2880);
        var from = fromMaybe ?? throw new Exception("original Map13 authorized seed returned null");
        Require(from.MapId == 13, "original Map13 authorized seed");
        var changedMaybe = await host.TrustedTransferAsync(710, from.ConnectionEpoch,
            from.MapEpoch, 15, 7872, 11296);
        var changed = changedMaybe ?? throw new Exception("original Map15 transfer seed returned null");
        Require(changed.MapId == 15, "original Map15 transfer seed");
        var backMaybe = await host.TrustedTransferAsync(710, changed.ConnectionEpoch,
            changed.MapEpoch, 13, 2560, 2880);
        var back = backMaybe ?? throw new Exception("original Map13 back seed returned null");
        Require(back.MapEpoch > changed.MapEpoch, "original Map13 back seed");
        await host.StopAsync();
        Require(host.Statistics.Ticks >= 8 && host.Statistics.ResultOverflowFailures == 0,
            "real-map 30Hz timing");
        Console.WriteLine($"REAL_WORLD_HOST_30HZ_PASS ticks={host.Statistics.Ticks} skipped={host.Statistics.SkippedDeadlines} max_tick_work_us={host.Statistics.MaxTickWorkMicroseconds} scope=simulation_only");
    }
}
