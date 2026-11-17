using SGTest.Server.Gateway;
using SGTest.Server.World;

internal static class WorldAoiChecks
{
    private static void Require(bool condition, string message) => Checks.Require(condition, message);
    private static ActorSnapshot Actor(long id, int map, double x, double y,
        long revision = 1, long connection = 1, long mapEpoch = 1) =>
        new(id, map, connection, mapEpoch, 0, 0, x, y, revision, false);
    private static ActorLifecycleEvent Join(ActorSnapshot actor) =>
        new(ActorLifecycleKind.Joined, actor);
    private static ActorLifecycleEvent Leave(ActorSnapshot actor) =>
        new(ActorLifecycleKind.Left, actor);

    private static void Life(WorldAoi aoi, long tick, params ActorLifecycleEvent[] changes) =>
        aoi.Apply(new WorldChangedBatch(tick, Array.Empty<ActorSnapshot>(), changes));

    private static void Move(WorldAoi aoi, long tick, ActorSnapshot actor) =>
        aoi.Apply(new WorldChangedBatch(tick, new[] { actor }));

    private static AoiEvent[] Drain(WorldAoi aoi, ActorSnapshot observer)
    {
        var output = new List<AoiEvent>();
        while (aoi.TryDequeue(observer.ActorId, observer.ConnectionEpoch,
               observer.MapEpoch, out var item))
            output.Add(item);
        return output.ToArray();
    }

    public static void Verify(string root)
    {
        Checks.Run("trusted World registration/disconnect/transfer emits ordered lifecycle even without Move",
            () => TrustedLifecycle(root));
        Checks.Run("AOI WorldReady, Spawn/Delta/Despawn, hysteresis and map isolation",
            Interest);
        Checks.Run("same AOI tracks monster Spawn/Delta/Despawn hysteresis and resync baseline",
            MonsterInterest);
        Checks.Run("AOI self state is changed-only, generation-safe and retained on bounded overflow", OwnState);
        Checks.Run("AOI transfer clears old map; reconnect resets generation and ignores stale dequeue",
            TransfersAndReconnects);
        Checks.Run("AOI same-generation resync rebuilds WorldReady SelfState and visible Spawn baseline",
            ResyncBaseline);
        Checks.Run("AOI fails closed on stale revision, malformed actor and bounded reliable events",
            InvalidAndBounded);
        Checks.Run("30Hz Host publishes idle join and leave to AOI through WorldResultRouter",
            () => RealHostLifecycle(root).GetAwaiter().GetResult());
    }

    private static void OwnState()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(MaxPendingEventsPerObserver: 1));
        var a = Actor(71, 15, 24, 8);
        Life(aoi, 1, Join(a));
        Drain(aoi, a);
        Move(aoi, 2, a); // replayed revision creates no extra owner traffic
        Require(aoi.PendingEvents(a.ActorId) == 0, "idle self generated traffic");
        var moving = a with { X = 28, EntityRevision = 2, IsMoving = true, MovementActionSequence = 1 };
        Move(aoi, 3, moving);
        Require(aoi.TryPeek(a.ActorId, a.ConnectionEpoch, a.MapEpoch, out var retained) &&
                retained.Kind == AoiEventKind.SelfState && retained.Entity == moving,
                "own changed revision not retained");
        bool overflow = false;
        try { Move(aoi, 4, moving with { X = 32, EntityRevision = 3 }); }
        catch (AoiBackpressureException) { overflow = true; }
        Require(overflow && aoi.TryPeek(a.ActorId, a.ConnectionEpoch, a.MapEpoch, out var after) &&
                after == retained, "self overflow silently discarded reliable event");
        Require(!aoi.TryConfirmQueued(a.ActorId, a.ConnectionEpoch + 1, a.MapEpoch),
                "wrong connection consumed own event");
    }

    private static void TrustedLifecycle(string root)
    {
        string dirA = Checks.Fixture(root, 101, 8, 8, (_, _) => 0);
        string dirB = Checks.Fixture(root, 102, 8, 8, (_, _) => 0);
        var world = new ServerWorld(new[] { BlockingGrid.Load(dirA, 101),
                                            BlockingGrid.Load(dirB, 102) });
        Require(world.TryTrustedRegister(11, 101, 24, 24, out var a), "first register");
        Require(world.TryTrustedRegister(22, 101, 40, 24, out var b), "second register");
        var first = world.Tick();
        Require(first.Changed.Count == 0 && first.Receipts.Count == 0 &&
                first.Lifecycle is { Count: 2 } &&
                first.Lifecycle[0].Kind == ActorLifecycleKind.Joined &&
                first.Lifecycle[0].Actor.ActorId == 11 &&
                first.Lifecycle[1].Actor.ActorId == 22,
                "idle joins not retained or wrong order");

        Require(world.TryTrustedDisconnect(11, a.ConnectionEpoch, a.MapEpoch),
                "trusted player leave");
        Require(world.TryTrustedTransfer(22, b.ConnectionEpoch, b.MapEpoch, 102, 24, 40,
                out var moved), "trusted transfer");
        var second = world.Tick();
        Require(second.Lifecycle is { Count: 3 } &&
                second.Lifecycle[0].Kind == ActorLifecycleKind.Left &&
                second.Lifecycle[0].Actor.ActorId == 11 &&
                second.Lifecycle[1].Kind == ActorLifecycleKind.Left &&
                second.Lifecycle[1].Actor.MapId == 101 &&
                second.Lifecycle[2].Kind == ActorLifecycleKind.Joined &&
                second.Lifecycle[2].Actor.MapId == 102 &&
                moved.MapEpoch > b.MapEpoch, "disconnect and transfer ordering");
        Require(!world.TryTrustedDisconnect(22, b.ConnectionEpoch, b.MapEpoch),
                "stale map disconnect unexpectedly succeeded");
        Require(world.Tick().Lifecycle is { Count: 0 },
                "lifecycle emitted repeatedly across idle ticks");
    }

    private static void Interest()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth: 200, EnterHalfHeight: 150, ExitMargin: 60, BucketSize: 64));
        var a = Actor(1, 15, 100, 100);
        var b = Actor(2, 15, 140, 100);
        var c = Actor(3, 15, 1200, 100);
        var otherMap = Actor(4, 13, 140, 100);
        Life(aoi, 1, Join(a));
        Require(Drain(aoi, a) is [var first] &&
                first.Kind == AoiEventKind.WorldReady && first.ReceiverMapEpoch == 1,
                "WorldReady must precede visible spawns");

        Life(aoi, 2, Join(b));
        var aEvents = Drain(aoi, a);
        var bEvents = Drain(aoi, b);
        Require(aEvents.Length == 1 && aEvents[0].Kind == AoiEventKind.Spawn &&
                aEvents[0].Entity?.ActorId == 2, "A did not see B spawn");
        Require(bEvents.Length == 2 && bEvents[0].Kind == AoiEventKind.WorldReady &&
                bEvents[1].Kind == AoiEventKind.Spawn &&
                bEvents[1].Entity?.ActorId == 1, "new player WorldReady ordering");

        Life(aoi, 3, Join(c));
        Life(aoi, 4, Join(otherMap));
        Require(Drain(aoi, a).Length == 0 && Drain(aoi, b).Length == 0 &&
                Drain(aoi, c).Length == 1 && Drain(aoi, otherMap).Length == 1,
                "far or different map player leaked into AOI");

        var bEdge = b with { X = 350, EntityRevision = 2 };
        Move(aoi, 5, bEdge); // diff=250, stays visible through exit hysteresis=260
        aEvents = Drain(aoi, a);
        Require(aEvents.Length == 1 && aEvents[0].Kind == AoiEventKind.Delta &&
                aEvents[0].Entity?.EntityRevision == 2,
                "hysteresis should retain and delta visible B");
        Require(Drain(aoi, bEdge) is [var own] && own.Kind == AoiEventKind.SelfState &&
                own.Entity == bEdge, "moving observer did not receive its own authoritative state");

        var bOutside = b with { X = 370, EntityRevision = 3 };
        Move(aoi, 6, bOutside); // diff=270 exits
        aEvents = Drain(aoi, a);
        bEvents = Drain(aoi, bOutside);
        Require(aEvents.Length == 1 && aEvents[0].Kind == AoiEventKind.Despawn &&
                aEvents[0].EntityActorId == 2 &&
                bEvents.Length == 2 && bEvents[0].Kind == AoiEventKind.SelfState &&
                bEvents[1].Kind == AoiEventKind.Despawn && bEvents[1].EntityActorId == 1,
                "crossing hysteresis exit should despawn in both directions");

        var bNear = b with { X = 250, EntityRevision = 4 };
        Move(aoi, 7, bNear);
        Require(Drain(aoi, a) is [var aSpawn] &&
                aSpawn.Kind == AoiEventKind.Spawn && aSpawn.Entity?.ActorId == 2 &&
                Drain(aoi, bNear) is [{ Kind: AoiEventKind.SelfState }, var bSpawn] &&
                bSpawn.Kind == AoiEventKind.Spawn && bSpawn.Entity?.ActorId == 1,
                "re-entering view should respawn, not delta without an entity");

        Require(aoi.EntityCount == 4 && aoi.ObserverCount == 4 &&
                aoi.LastTick == 7, "AOI world counters");
    }

    private static MonsterSnapshot Monster(long id, int map, double x, double y,
        long revision = 1, int hp = 100, MonsterAiState state = MonsterAiState.Idle) =>
        new(id, 1, 0, map, x, y, 140, 100, hp, 100, state, 0, revision, 1);

    private static void MonsterInterest()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth:200, EnterHalfHeight:150, ExitMargin:60, BucketSize:64));
        var viewer = Actor(51,15,100,100);
        Life(aoi,1,Join(viewer));
        Require(Drain(aoi,viewer) is [{ Kind:AoiEventKind.WorldReady }],
            "monster fixture Ready");

        var monster = Monster(1001500001,15,140,100);
        aoi.ApplyMonsters(1,[monster]);
        Require(Drain(aoi,viewer) is [var spawn] &&
                spawn.Kind == AoiEventKind.MonsterSpawn &&
                spawn.Monster?.EntityId == monster.EntityId,
                "near monster did not Spawn through shared AOI");

        aoi.Apply(new WorldChangedBatch(2,Array.Empty<ActorSnapshot>()));
        var edge = monster with { X=350, EntityRevision=2, State=MonsterAiState.Chase,
                                  TargetActorId=viewer.ActorId };
        aoi.ApplyMonsters(2,[edge]);
        Require(Drain(aoi,viewer) is [var delta] &&
                delta.Kind == AoiEventKind.MonsterDelta &&
                delta.Monster?.EntityRevision == 2,
                "monster exit hysteresis did not retain Delta");

        aoi.Apply(new WorldChangedBatch(3,Array.Empty<ActorSnapshot>()));
        var outside = edge with { X=370, EntityRevision=3 };
        aoi.ApplyMonsters(3,[outside]);
        Require(Drain(aoi,viewer) is [var gone] &&
                gone.Kind == AoiEventKind.MonsterDespawn &&
                gone.MonsterEntityId == monster.EntityId &&
                gone.MonsterRevision == 3,
                "monster crossing shared AOI exit did not Despawn");

        aoi.Apply(new WorldChangedBatch(4,Array.Empty<ActorSnapshot>()));
        var near = outside with { X=250, EntityRevision=4, CurrentHp=80 };
        aoi.ApplyMonsters(4,[near]);
        Require(Drain(aoi,viewer) is [var reenter] &&
                reenter.Kind == AoiEventKind.MonsterSpawn &&
                reenter.Monster?.EntityRevision == 4,
                "monster re-entry did not rebuild Spawn baseline");

        Require(aoi.TryResync(new TrustedWorldResyncRequest(
                    viewer.ActorId,viewer.ConnectionEpoch,viewer.MapEpoch)),
                "monster AOI resync rejected");
        var rebuilt=Drain(aoi,viewer);
        Require(rebuilt.Length==3 &&
                rebuilt[0].Kind==AoiEventKind.WorldReady &&
                rebuilt[1].Kind==AoiEventKind.SelfState &&
                rebuilt[2].Kind==AoiEventKind.MonsterSpawn &&
                rebuilt[2].Monster?.EntityRevision==4,
                "monster missing from same AOI resync baseline");
        Require(aoi.MonsterCount==1 && aoi.TrackedEntityCount==2,
                "shared AOI monster counters");
    }

    private static void TransfersAndReconnects()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth: 200, EnterHalfHeight: 150, ExitMargin: 60, BucketSize: 64));
        var a = Actor(11, 15, 100, 100);
        var b = Actor(22, 15, 120, 100);
        var c = Actor(33, 13, 100, 100);
        Life(aoi, 1, Join(a), Join(b), Join(c));
        Drain(aoi, a); Drain(aoi, b); Drain(aoi, c);

        var transferred = b with { MapId = 13, MapEpoch = 2, EntityRevision = 2,
                                   X = 120, Y = 100 };
        Life(aoi, 2, Leave(b), Join(transferred));
        Require(Drain(aoi, a) is [var gone] &&
                gone.Kind == AoiEventKind.Despawn &&
                gone.EntityActorId == 22 &&
                gone.EntityConnectionEpoch == 1, "old map did not despawn transfer");
        Require(Drain(aoi, c) is [var entered] &&
                entered.Kind == AoiEventKind.Spawn &&
                entered.Entity?.ActorId == 22, "new map observer missed transfer");
        var bEvents = Drain(aoi, transferred);
        Require(bEvents.Length == 2 &&
                bEvents[0].Kind == AoiEventKind.WorldReady &&
                bEvents[0].ReceiverMapEpoch == 2 &&
                bEvents[1].Kind == AoiEventKind.Spawn &&
                bEvents[1].Entity?.ActorId == 33, "transfer requires new WorldReady then snapshot");
        Require(!aoi.TryDequeue(22, 1, 1, out _), "old MapEpoch dequeued new map events");

        Life(aoi, 3, Leave(transferred));
        Require(Drain(aoi, c) is [var removed] &&
                removed.Kind == AoiEventKind.Despawn &&
                removed.EntityActorId == 22, "disconnect ghost actor");

        var reconnected = b with { ConnectionEpoch = 2, MapEpoch = 3,
                                   EntityRevision = 1 };
        Life(aoi, 4, Join(reconnected));
        Require(!aoi.TryDequeue(22, 1, 1, out _) &&
                Drain(aoi, reconnected).Any(e => e.Kind == AoiEventKind.WorldReady),
                "reconnect must not reuse old queue");
        Require(Drain(aoi, a) is [var reSpawn] &&
                reSpawn.Kind == AoiEventKind.Spawn &&
                reSpawn.Entity?.ConnectionEpoch == 2,
                "reconnect did not create new entity generation");
    }

    private static void ResyncBaseline()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth: 200, EnterHalfHeight: 150, ExitMargin: 60, BucketSize: 64));
        var a = Actor(81, 15, 100, 100);
        var b = Actor(82, 15, 120, 100);
        Life(aoi, 1, Join(a), Join(b));
        Drain(aoi,a); Drain(aoi,b);
        var moved = b with { X = 136, EntityRevision = 2, IsMoving = true,
                             MovementActionSequence = 1 };
        Move(aoi,2,moved);
        Require(aoi.PendingEvents(a.ActorId) == 1, "resync fixture missing stale pending delta");
        Require(aoi.TryResync(new TrustedWorldResyncRequest(
                    a.ActorId,a.ConnectionEpoch,a.MapEpoch)),
                "current observer resync rejected");
        var rebuilt = Drain(aoi,a);
        Require(rebuilt.Length == 3 &&
                rebuilt[0].Kind == AoiEventKind.WorldReady &&
                rebuilt[1].Kind == AoiEventKind.SelfState &&
                rebuilt[1].Entity?.ActorId == a.ActorId &&
                rebuilt[2].Kind == AoiEventKind.Spawn &&
                rebuilt[2].Entity?.ActorId == b.ActorId &&
                rebuilt[2].Entity?.EntityRevision == 2,
                "resync baseline order/content wrong");
        Require(!aoi.TryResync(new TrustedWorldResyncRequest(
                    a.ActorId,a.ConnectionEpoch+1,a.MapEpoch)),
                "stale connection resync accepted");
    }

    private static void InvalidAndBounded()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth: 200, EnterHalfHeight: 200, BucketSize: 64));
        var a = Actor(1, 15, 100, 100);
        Life(aoi, 1, Join(a));
        Drain(aoi, a);
        var newer = a with { EntityRevision = 3, X = 110 };
        Move(aoi, 2, newer);
        Move(aoi, 3, newer); // identical revision is idempotent
        bool stale = false, wrongEpoch = false, wrongTick = false;
        try { Move(aoi, 4, a with { EntityRevision = 2 }); }
        catch (InvalidOperationException) { stale = true; }
        try { Move(aoi, 5, newer with { MapEpoch = 99, EntityRevision = 4 }); }
        catch (InvalidOperationException) { wrongEpoch = true; }
        try { Move(aoi, 5, newer); }
        catch (InvalidOperationException) { wrongTick = true; }
        Require(stale && wrongEpoch && wrongTick, "AOI accepted stale/future state");

        var bounded = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth: 200, EnterHalfHeight: 200, BucketSize: 64,
            MaxPendingEventsPerObserver: 1));
        Life(bounded, 1, Join(a));
        bool overflow = false;
        try { Life(bounded, 2, Join(Actor(2, 15, 110, 100))); }
        catch (AoiBackpressureException) { overflow = true; }
        Require(overflow && bounded.PendingEvents(1) == 1 &&
                Drain(bounded, a) is [var ready] &&
                ready.Kind == AoiEventKind.WorldReady,
                "full AOI event queue dropped WorldReady or silently accepted overflow");
    }

    private static async Task RealHostLifecycle(string root)
    {
        string mapDir = Checks.Fixture(root, 103, 8, 8, (_, _) => 0);
        await using var host = new ServerWorldHost([BlockingGrid.Load(mapDir, 103)]);
        await host.StartAsync();
        var a = await host.TrustedRegisterAsync(71, 103, 24, 24)
            ?? throw new Exception("host trusted register");
        var b = await host.TrustedRegisterAsync(72, 103, 40, 24)
            ?? throw new Exception("host second register");
        var aoi = new WorldAoi();
        var router = new WorldResultRouter();
        var pump = router.RunAsync(host.Results);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (aoi.EntityCount < 2)
            aoi.Apply(await router.ChangedBatches.ReadAsync(timeout.Token));
        Require(Drain(aoi, a).Any(e => e.Kind == AoiEventKind.WorldReady) &&
                Drain(aoi, b).Any(e => e.Kind == AoiEventKind.Spawn),
                "idle WorldHost join did not reach AOI without a Move");
        Require(await host.TrustedDisconnectAsync(72, b.ConnectionEpoch, b.MapEpoch),
                "trusted live disconnect");
        while (aoi.EntityCount != 1)
            aoi.Apply(await router.ChangedBatches.ReadAsync(timeout.Token));
        Require(Drain(aoi, a) is [var gone] &&
                gone.Kind == AoiEventKind.Despawn && gone.EntityActorId == 72,
                "idle WorldHost disconnect did not despawn other player");
        await host.StopAsync();
        await pump.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
