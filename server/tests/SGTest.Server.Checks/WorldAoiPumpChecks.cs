using SGTest.Server.Visibility;
using SGTest.Server.Monster;
using SGTest.Server.Map;
using System.Text.Json;
using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Diagnostics;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
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
        Checks.Run("trusted two-map transfer preserves Gateway identity and rebuilds AOI both directions",
            () => TrustedTwoMapTransfer(root).GetAwaiter().GetResult());
        Checks.Run("bounded same-map resync request is rebuilt by the sole AOI pump",
            () => LiveResync(root).GetAwaiter().GetResult());
        Checks.Run("live Host Router Pump publishes coalesced monster state through shared AOI FIFO",
            () => LiveMonsterPipeline(root).GetAwaiter().GetResult());
    }

    public static void VerifyLoad(string root)
    {
        Checks.Run("20-player 300-monster 500-drop shared AOI load stays spatially bounded without reliable overflow",
            () => TwentyPlayersThreeHundredMonsters(root).GetAwaiter().GetResult());
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

    private static async Task TrustedTwoMapTransfer(string root)
    {
        string map15 = Checks.Fixture(root, 15, 8, 8, (_, _) => 0);
        string map13 = Checks.Fixture(root, 13, 8, 8, (_, _) => 0);
        await using var host = new ServerWorldHost([
            BlockingGrid.Load(map15, 15), BlockingGrid.Load(map13, 13)]);
        await host.StartAsync();
        var router = new WorldResultRouter();
        var pump = new WorldAoiPump(router);
        var resultTask = router.RunAsync(host.Results);
        using var stop = new CancellationTokenSource();
        var aoiTask = pump.RunAsync(stop.Token);
        try
        {
            var one = await host.TrustedRegisterAsync(501, 15, 8, 8)
                ?? throw new Exception("Map15 first actor");
            var two = await host.TrustedRegisterAsync(502, 15, 24, 8)
                ?? throw new Exception("Map15 second actor");
            var a = new GatewayConnection(one);
            var b = new GatewayConnection(two);
            Require(router.TryAttach(a) && router.TryAttach(b), "trusted AOI attach");
            await WaitForFrames(a, 2);
            await WaitForFrames(b, 2);
            Require(a.TryDequeueReliable(out var aReady) &&
                    WireProtocol.TryDecodeWorldReady(aReady, out var firstReady) &&
                    firstReady.MapId == 15 && firstReady.ReceiverMapEpoch == one.MapEpoch &&
                    a.TryDequeueReliable(out var aSpawn) &&
                    WireProtocol.TryDecodeEntityState(aSpawn, out var aType, out var bState) &&
                    aType == WireOpcode.EntitySpawn && bState.ActorId == 502,
                    "Map15 initial AOI A");
            Require(b.TryDequeueReliable(out var bReady) &&
                    WireProtocol.TryDecodeWorldReady(bReady, out var secondReady) &&
                    secondReady.MapId == 15 &&
                    b.TryDequeueReliable(out var bSpawn) &&
                    WireProtocol.TryDecodeEntityState(bSpawn, out var bType, out var aState) &&
                    bType == WireOpcode.EntitySpawn && aState.ActorId == 501,
                    "Map15 initial AOI B");

            // Out-of-bounds destination is rejected atomically; no new MapEpoch.
            Require(await router.TrustedTransferAsync(host, 502, 13, 99999, 8) is null &&
                    b.Binding.MapEpoch == two.MapEpoch,
                "unwalkable/unloaded transfer changed trusted map");

            var transferred = await router.TrustedTransferAsync(host, 502, 13, 8, 8)
                ?? throw new Exception("Map13 authorized transfer failed");
            Require(transferred.MapId == 13 &&
                    transferred.ConnectionEpoch == two.ConnectionEpoch &&
                    transferred.MapEpoch > two.MapEpoch &&
                    b.Binding.MapEpoch == transferred.MapEpoch,
                "Gateway did not advance trusted MapEpoch");
            Require(!await host.TrustedDisconnectAsync(
                    two.ActorId, two.ConnectionEpoch, two.MapEpoch),
                "stale old-map disconnect removed a transferred actor");
            await WaitForFrames(a, 1);
            await WaitForFrames(b, 1);
            Require(a.TryDequeueReliable(out var gone) &&
                    WireProtocol.TryDecodeEntityDespawn(gone, out var left) &&
                    left.ActorId == 502 && left.ReceiverMapEpoch == one.MapEpoch,
                "other Map15 player did not get Despawn");
            Require(b.TryDequeueReliable(out var newReady) &&
                    WireProtocol.TryDecodeWorldReady(newReady, out var inMap13) &&
                    inMap13.ActorId == 502 && inMap13.MapId == 13 &&
                    inMap13.ReceiverMapEpoch == transferred.MapEpoch,
                "Map13 WorldReady missing");
            Require(a.ReliableFrames == 0 && b.ReliableFrames == 0,
                "cross-map entities leaked through AOI");

            var returned = await router.TrustedTransferAsync(host, 502, 15, 24, 8)
                ?? throw new Exception("Map15 return transfer failed");
            Require(returned.MapEpoch > transferred.MapEpoch &&
                    returned.ConnectionEpoch == two.ConnectionEpoch &&
                    b.Binding.MapEpoch == returned.MapEpoch,
                "return MapEpoch is not monotonic");
            await WaitForFrames(a, 1);
            await WaitForFrames(b, 2);
            Require(a.TryDequeueReliable(out var respawn) &&
                    WireProtocol.TryDecodeEntityState(respawn, out var kind, out var returnedActor) &&
                    kind == WireOpcode.EntitySpawn && returnedActor.ActorId == 502 &&
                    returnedActor.EntityMapEpoch == returned.MapEpoch,
                "Map15 return did not Spawn the fresh generation");
            Require(b.TryDequeueReliable(out var backReady) &&
                    WireProtocol.TryDecodeWorldReady(backReady, out var back) &&
                    back.MapId == 15 && back.ReceiverMapEpoch == returned.MapEpoch &&
                    b.TryDequeueReliable(out var backSpawn) &&
                    WireProtocol.TryDecodeEntityState(backSpawn, out kind, out var sibling) &&
                    kind == WireOpcode.EntitySpawn && sibling.ActorId == 501,
                "returning player did not rebuild Map15 WorldReady and peer snapshot");
            Require(router.TryDetach(b) &&
                    await host.TrustedDisconnectAsync(502, two.ConnectionEpoch, returned.MapEpoch),
                "latest map session failed to disconnect cleanly");
            await WaitForFrames(a, 1);
            Require(a.TryDequeueReliable(out var logout) &&
                    WireProtocol.TryDecodeEntityDespawn(logout, out var finalGone) &&
                    finalGone.ActorId == 502,
                "logout after Map15 return left ghost AOI");
            Require(host.Statistics.ActiveActors == 1, "wrong active World actor count");
        }
        finally
        {
            stop.Cancel();
            await aoiTask.WaitAsync(TimeSpan.FromSeconds(3));
            await host.StopAsync();
            await resultTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private static async Task LiveResync(string root)
    {
        string folder = Checks.Fixture(root, 105, 12, 12, (_, _) => 0);
        await using var host = new ServerWorldHost([BlockingGrid.Load(folder,105)]);
        await host.StartAsync();
        var actor = await host.TrustedRegisterAsync(901,105,24,24)
            ?? throw new Exception("resync actor");
        var peer = await host.TrustedRegisterAsync(902,105,40,24)
            ?? throw new Exception("resync peer");
        var gateway = new GatewayConnection(actor);
        var router = new WorldResultRouter(new WorldResultRouterOptions(
            ChangedBatchCapacity:128, MaxSessions:10000, ResyncRequestCapacity:1));
        Require(router.TryAttach(gateway),"resync gateway attach");
        var pump = new WorldAoiPump(router);
        using var stop = new CancellationTokenSource();
        var routerTask = router.RunAsync(host.Results);
        var pumpTask = pump.RunAsync(stop.Token);
        try
        {
            await WaitForFrames(gateway,2);
            Require(gateway.TryDequeueReliable(out var ready), "resync initial Ready frame missing");
            Require(WireProtocol.TryDecodeWorldReady(ready,out var readyValue),
                    "resync initial Ready decode");
            Require(gateway.TryDequeueReliable(out var spawn) &&
                    WireProtocol.TryDecodeEntityState(spawn,out var kind,out var peerValue) &&
                    kind == WireOpcode.EntitySpawn && peerValue.ActorId == peer.ActorId,
                    "resync initial peer Spawn");
            Require(gateway.FeedClientBytes(
                    WireProtocol.EncodeWorldReadyAck(new(actor.MapEpoch,readyValue.ServerTick)),
                    _ => true,out var controls) == WireFeedStatus.Accepted &&
                    controls == 0 && gateway.WorldReadyAcknowledged,
                    "resync initial ACK");
            Require(gateway.FeedClientBytes(
                    WireProtocol.EncodeWorldResyncRequest(new(actor.MapEpoch)),
                    _ => true,out controls,null,router.TryRequestResync) ==
                    WireFeedStatus.Accepted && controls == 0 &&
                    !gateway.WorldReadyAcknowledged,
                    "resync request ingress");
            await WaitForFrames(gateway,3);
            Require(gateway.TryDequeueReliable(out var rebuiltReady),
                    "resync replacement Ready frame missing");
            Require(WireProtocol.TryDecodeWorldReady(rebuiltReady,out var rebuilt) &&
                    rebuilt.ReceiverMapEpoch == actor.MapEpoch,
                    "resync replacement Ready decode/generation");
            Require(gateway.TryDequeueReliable(out var self) &&
                    WireProtocol.TryDecodeEntityState(self,out var selfKind,out var own) &&
                    selfKind == WireOpcode.SelfState && own.ActorId == actor.ActorId &&
                    gateway.TryDequeueReliable(out var rebuiltSpawn) &&
                    WireProtocol.TryDecodeEntityState(rebuiltSpawn,out kind,out peerValue) &&
                    kind == WireOpcode.EntitySpawn && peerValue.ActorId == peer.ActorId,
                    "resync pump did not rebuild ordered SelfState/Spawn baseline");
            Require(gateway.FeedClientBytes(
                    WireProtocol.EncodeWorldReadyAck(new(actor.MapEpoch,rebuilt.ServerTick)),
                    _ => true,out controls) == WireFeedStatus.Accepted &&
                    gateway.WorldReadyAcknowledged,
                    "resync replacement WorldReady ACK");
        }
        finally
        {
            stop.Cancel();
            await pumpTask.WaitAsync(TimeSpan.FromSeconds(3));
            await host.StopAsync();
            await routerTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private static async Task LiveMonsterPipeline(string root)
    {
        string folder = Checks.Fixture(root,106,20,20,(_,_)=>0);
        var grid = BlockingGrid.Load(folder,106);
        string populationPath = Path.Combine(folder,"population.json");
        File.WriteAllText(populationPath,JsonSerializer.Serialize(new
        {
            version=4,
            kind="verified_monster_runtime",
            map_id=106,
            stats_verified=true,
            gameplay_ready=true,
            definitions=new[]
            {
                new
                {
                    id=1,name="Synthetic",sprite="EN001A",
                    atlas="Enemy/EN001A/atlas.json",
                    stats=new
                    {
                        level=3,max_hp=100,attack_damage=6,move_step=8.0,
                        aggro_radius=200.0,lose_target_radius=320.0,
                        leash_radius=320.0,attack_range=24.0,
                        attack_cooldown_ticks=1,respawn_ticks=30,
                        wander_radius=0.0,wander_interval_ticks=100
                    }
                }
            },
            spawns=new[]
            {
                new { source_index=0,source_flags=7,definition=1,p=new[]{40,24} }
            }
        }));
        var population = MonsterPopulationCatalog.LoadVerified(
            populationPath,106,grid);
        var runtime = new MonsterRuntime(grid,population,
            new(AiIntervalTicks:1,MaxAiPerStep:16,MaxPathQueriesPerStep:4,
                MaxPathExpandedCells:256));
        var coordinator = new MonsterWorldCoordinator([runtime]);

        await using var host = new ServerWorldHost(
            [grid], options:new WorldHostOptions(PublishIdleTicks:true));
        await host.StartAsync();
        var actor = await host.TrustedRegisterAsync(701,106,24,24)
            ?? throw new Exception("monster pipeline actor");
        var gateway = new GatewayConnection(actor);
        var router = new WorldResultRouter();
        Require(router.TryAttach(gateway),"monster pipeline Gateway attach");
        var pump = new WorldAoiPump(router,monsters:coordinator);
        using var stop = new CancellationTokenSource();
        var routing = router.RunAsync(host.Results);
        var pumping = pump.RunAsync(stop.Token);
        try
        {
            await WaitForFrames(gateway,2);
            Require(gateway.TryDequeueReliable(out var ready) &&
                    WireProtocol.TryDecodeWorldReady(ready,out var readyValue) &&
                    readyValue.ActorId==actor.ActorId,
                    "monster pipeline WorldReady missing");
            Require(gateway.TryDequeueReliable(out var spawn),
                    "monster pipeline Spawn frame missing");
            Require(WireProtocol.TryDecodeMonsterState(
                        spawn,out var kind,out var monster) &&
                    kind==WireOpcode.MonsterSpawn &&
                    monster.EnemyId==1 && monster.MapId==106 &&
                    monster.TargetActorId==actor.ActorId &&
                    monster.State==MonsterAiState.Attack,
                    "monster baseline was not coalesced into authoritative Spawn");

            await WaitForFrames(gateway,1);
            Require(gateway.TryDequeueReliable(out var delta),
                    "monster pipeline Delta frame missing");
            Require(WireProtocol.TryDecodeMonsterState(
                        delta,out kind,out var updated) &&
                    kind==WireOpcode.MonsterDelta &&
                    updated.EntityId==monster.EntityId &&
                    updated.Revision>monster.Revision,
                    "next bounded AI step did not publish MonsterDelta");
            Require(pump.Aoi.MonsterCount==1 &&
                    coordinator.Statistics.Maps==1 &&
                    coordinator.Statistics.Monsters==1 &&
                    coordinator.Statistics.RuntimeTicks>0,
                    "monster coordinator/AOI accounting");
        }
        finally
        {
            stop.Cancel();
            await pumping.WaitAsync(TimeSpan.FromSeconds(3));
            await host.StopAsync();
            await routing.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private static async Task TwentyPlayersThreeHundredMonsters(string root)
    {
        const int mapId = 107;
        const int playerCount = 20;
        const int monstersPerCluster = 15;
        const int monsterCount = playerCount * monstersPerCluster;
        const int dropsPerCluster = 25;
        const int dropCount = playerCount * dropsPerCluster;
        string folder = Checks.Fixture(root, mapId, 420, 240, (_, _) => 0);
        var grid = BlockingGrid.Load(folder, mapId);

        var spawnRows = new List<object>(monsterCount);
        var dropRows = new List<GroundDropRecord>(dropCount);
        var centers = new (int X, int Y)[playerCount];
        for (int player = 0; player < playerCount; player++)
        {
            int column = player % 5;
            int row = player / 5;
            int centerX = 400 + column * 1400;
            int centerY = 400 + row * 900;
            centers[player] = (centerX, centerY);
            for (int local = 0; local < monstersPerCluster; local++)
            {
                int source = player * monstersPerCluster + local;
                int x = centerX + ((local % 5) - 2) * 24;
                int y = centerY + ((local / 5) - 1) * 24;
                spawnRows.Add(new
                {
                    source_index = source,
                    source_flags = 7,
                    definition = 1,
                    p = new[] { x, y }
                });
            }
            for (int local = 0; local < dropsPerCluster; local++)
            {
                // Synthetic committed ground records, isolated from SQLite.
                // Each nearby observer sees exactly 25; this is NOT a loot
                // probability, durability or real-item sprite benchmark.
                int x = centerX + ((local % 5) - 2) * 16;
                int y = centerY + ((local / 5) - 2) * 16;
                dropRows.Add(new GroundDropRecord(
                    200_000L + player * dropsPerCluster + local,
                    mapId, x, y, 100_107_0001L,
                    4_294_967_301L + player, local, 2701, 1,
                    LootSourceTrust.Verified, "ci:synthetic-mixed-aoi-load", null));
            }
        }
        Require(spawnRows.Count == monsterCount && dropRows.Count == dropCount,
            "mixed fixture has exact 20/300/500 accounting");

        string populationPath = Path.Combine(folder, "population-load.json");
        File.WriteAllText(populationPath, JsonSerializer.Serialize(new
        {
            version = 4,
            kind = "verified_monster_runtime",
            map_id = mapId,
            stats_verified = true,
            gameplay_ready = true,
            definitions = new[]
            {
                new
                {
                    id = 1,
                    name = "SyntheticLoadMonster",
                    sprite = "EN001A",
                    atlas = "Enemy/EN001A/atlas.json",
                    stats = new
                    {
                        level = 3,
                        max_hp = 100,
                        attack_damage = 6,
                        move_step = 8.0,
                        aggro_radius = 160.0,
                        lose_target_radius = 320.0,
                        leash_radius = 320.0,
                        attack_range = 24.0,
                        attack_cooldown_ticks = 8,
                        respawn_ticks = 90,
                        wander_radius = 0.0,
                        wander_interval_ticks = 100
                    }
                }
            },
            spawns = spawnRows
        }));

        var population = MonsterPopulationCatalog.LoadVerified(
            populationPath, mapId, grid, maxSpawns: monsterCount);
        var runtime = new MonsterRuntime(grid, population,
            new MonsterRuntimeOptions(
                AiIntervalTicks: 4,
                MaxAiPerStep: 64,
                MaxPathQueriesPerStep: 16,
                MaxPathExpandedCells: 256));
        var coordinator = new MonsterWorldCoordinator([runtime]);

        await using var host = new ServerWorldHost(
            [grid],
            options: new WorldHostOptions(
                ResultCapacity: 256,
                PublishIdleTicks: true));
        await host.StartAsync();

        var router = new WorldResultRouter(
            new WorldResultRouterOptions(
                ChangedBatchCapacity: 128,
                MaxSessions: 64,
                ResyncRequestCapacity: 32));
        var gateways = new List<GatewayConnection>(playerCount);
        var actors = new List<ActorSnapshot>(playerCount);
        for (int i = 0; i < playerCount; i++)
        {
            var center = centers[i];
            var actor = await host.TrustedRegisterAsync(
                10_000 + i, mapId, center.X, center.Y)
                ?? throw new Exception($"load actor {i}");
            actors.Add(actor);
            var gateway = new GatewayConnection(actor);
            Require(router.TryAttach(gateway), $"load Gateway attach {i}");
            gateways.Add(gateway);
        }

        var aoi = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth: 320,
            EnterHalfHeight: 240,
            ExitMargin: 64,
            BucketSize: 128,
            MaxEntities: 1024,
            MaxObservers: 64,
            MaxVisiblePerObserver: 64,
            MaxPendingEventsPerObserver: 256));
        // Seed only before the AOI owner starts, as the production pump does
        // from committed SQLite ground records at Host boot.
        var pump = new WorldAoiPump(
            router, aoi,
            new WorldAoiPumpOptions(
                FlushIntervalMilliseconds: 2,
                MaxFramesPerObserverPerPoll: 64),
            coordinator,
            initialGroundDrops: dropRows);
        var loadTimer = Stopwatch.StartNew();
        using var stop = new CancellationTokenSource();
        var routing = router.RunAsync(host.Results);
        var pumping = pump.RunAsync(stop.Token);

        try
        {
            int totalBaselineSpawns = 0;
            int totalBaselineDrops = 0;
            long baselineBytes = 0;
            var initialReadyTicks = new long[playerCount];
            for (int player = 0; player < playerCount; player++)
            {
                var gateway = gateways[player];
                await WaitForFrames(gateway, 1 + monstersPerCluster + dropsPerCluster);
                var expectedMin = player * monstersPerCluster;
                var expectedMax = expectedMin + monstersPerCluster - 1;
                bool ready = false;
                var spawned = new HashSet<int>();
                var drops = new HashSet<long>();
                while (gateway.TryDequeueReliable(out var frame))
                {
                    baselineBytes += frame.Length;
                    if (WireProtocol.TryDecodeWorldReady(frame, out var readyFrame))
                    {
                        Require(readyFrame.ActorId == actors[player].ActorId,
                            $"load Ready actor {player}");
                        initialReadyTicks[player] = readyFrame.ServerTick;
                        ready = true;
                        continue;
                    }
                    if (WireProtocol.TryDecodeMonsterState(
                            frame, out var opcode, out var monster))
                    {
                        Require(monster.SourceIndex >= expectedMin &&
                                monster.SourceIndex <= expectedMax,
                            $"observer {player} received foreign monster source {monster.SourceIndex}");
                        if (opcode == WireOpcode.MonsterSpawn)
                            spawned.Add(monster.SourceIndex);
                        continue;
                    }
                    if (WireProtocol.TryDecodeGroundDropSpawn(frame, out var drop))
                    {
                        long minDrop = 200_000L + player * dropsPerCluster;
                        Require(drop.DropId >= minDrop &&
                                drop.DropId < minDrop + dropsPerCluster,
                            $"observer {player} received foreign ground UID {drop.DropId}");
                        Require(drops.Add(drop.DropId),
                            $"observer {player} received repeated ground UID {drop.DropId}");
                        continue;
                    }
                    Require(false,
                        $"observer {player} received unexpected baseline frame");
                }
                Require(ready && spawned.Count == monstersPerCluster &&
                        drops.Count == dropsPerCluster,
                    $"observer {player} isolated monster/drop baseline count");
                totalBaselineSpawns += spawned.Count;
                totalBaselineDrops += drops.Count;
            }

            Require(totalBaselineSpawns == monsterCount &&
                    totalBaselineDrops == dropCount &&
                    aoi.GroundDropCount == dropCount &&
                    aoi.MonsterCount == monsterCount &&
                    aoi.ObserverCount == playerCount &&
                    coordinator.Statistics.TrackedActors == playerCount &&
                    coordinator.Statistics.Monsters == monsterCount,
                "20-player/300-monster shared authority accounting");

            // Allow more than one full 300-monster round-robin pass while
            // immutable ground drops remain present. An unchanged drop must
            // never consume another reliable queue slot in an idle/delta tick.
            // Each observer sees only 15 monsters and 25 drops.
            await Task.Delay(900);
            int totalDeltas = 0;
            int repeatedDropSpawns = 0;
            int peakReliableFrames = 0;
            int peakReliableBytes = 0;
            long deltaBytes = 0;
            for (int player = 0; player < playerCount; player++)
            {
                var gateway = gateways[player];
                peakReliableFrames = Math.Max(
                    peakReliableFrames, gateway.ReliableFrames);
                peakReliableBytes = Math.Max(
                    peakReliableBytes, gateway.ReliableBytes);
                Require(gateway.ReliableFrames < 128,
                    $"observer {player} reliable queue grew beyond spatial bound");
                int expectedMin = player * monstersPerCluster;
                int expectedMax = expectedMin + monstersPerCluster - 1;
                while (gateway.TryDequeueReliable(out var frame))
                {
                    deltaBytes += frame.Length;
                    if (WireProtocol.TryDecodeGroundDropSpawn(frame, out _))
                    {
                        repeatedDropSpawns++;
                        continue;
                    }
                    if (!WireProtocol.TryDecodeMonsterState(
                            frame, out var opcode, out var monster))
                        continue;
                    Require(monster.SourceIndex >= expectedMin &&
                            monster.SourceIndex <= expectedMax,
                        $"observer {player} received foreign AI delta {monster.SourceIndex}");
                    if (opcode == WireOpcode.MonsterDelta)
                        totalDeltas++;
                }
            }

            var stats = runtime.Statistics;
            var hostStats = host.Statistics;
            Require(totalDeltas > 0 &&
                    repeatedDropSpawns == 0 &&
                    stats.MonsterCount == monsterCount &&
                    stats.MaxProcessedInStep <= 64 &&
                    stats.MonstersProcessed >= monsterCount &&
                    router.Statistics.BackpressureFailures == 0 &&
                    peakReliableFrames < 128 &&
                    hostStats.ResultOverflowFailures == 0 &&
                    pump.ProcessedBatches > 0 &&
                    aoi.TrackedEntityCount == playerCount + monsterCount + dropCount,
                "20-player/300-monster/500-drop mixed load violated spatial/reliable bounds");
            loadTimer.Stop();
            Console.WriteLine(
                $"SERVER_MIXED_AOI_LOAD_PASS players={playerCount} monsters={monsterCount} " +
                $"drops={dropCount} baseline_monster_spawns={totalBaselineSpawns} " +
                $"baseline_drop_spawns={totalBaselineDrops} repeated_drop_spawns={repeatedDropSpawns} " +
                $"monster_deltas={totalDeltas} baseline_wire_bytes={baselineBytes} " +
                $"delta_wire_bytes={deltaBytes} peak_reliable_frames={peakReliableFrames} " +
                $"peak_reliable_bytes={peakReliableBytes} ai_max_processed={stats.MaxProcessedInStep} " +
                $"host_ticks={hostStats.Ticks} host_max_tick_us={hostStats.MaxTickWorkMicroseconds} " +
                $"host_skipped_deadlines={hostStats.SkippedDeadlines} wall_ms={loadTimer.ElapsedMilliseconds} " +
                $"source=synthetic no_tls_or_gpu=true");
            await VerifyMixedTlsResyncAsync(
                router, gateways, actors, initialReadyTicks,
                monstersPerCluster, dropsPerCluster);
        }
        finally
        {
            stop.Cancel();
            await pumping.WaitAsync(TimeSpan.FromSeconds(3));
            await host.StopAsync();
            await routing.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }


    /// <summary>
    /// 20 real encrypted TCP/TLS transports on the already authorized test
    /// bindings. This intentionally does NOT claim 20 account logins, 20 real
    /// Godot renderers, or 20 production listener admission slots.
    /// Unlike the direct Gateway dequeue check, each observer must parse its
    /// 15 monster + 25 ground spawn frames from a real SslStream after a valid
    /// client WorldReady ACK followed by a same-map resync request.
    /// </summary>
    private static async Task VerifyMixedTlsResyncAsync(
        WorldResultRouter router, IReadOnlyList<GatewayConnection> gateways,
        IReadOnlyList<ActorSnapshot> actors, IReadOnlyList<long> initialReadyTicks,
        int monstersPerCluster, int dropsPerCluster)
    {
        using var certificate = TlsSocketChecks.CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(gateways.Count);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var clients = new List<TcpClient>(gateways.Count);
        var secureStreams = new List<SslStream>(gateways.Count);
        var servers = new List<Task<TlsWireConnectionResult>>(gateways.Count);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            for (int i = 0; i < gateways.Count; i++)
            {
                var accepting = listener.AcceptTcpClientAsync();
                var client = new TcpClient(AddressFamily.InterNetwork);
                clients.Add(client);
                await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
                TcpClient accepted = await accepting.WaitAsync(deadline.Token);
                int observerIndex = i;
                servers.Add(TlsWireConnection.RunServerAsync(
                    accepted, certificate,
                    (_, _) => ValueTask.FromResult<GatewayConnection?>(
                        gateways[observerIndex]),
                    _ => false,
                    new TlsWireConnectionOptions(
                        HandshakeTimeoutMilliseconds: 10000,
                        AuthorizationTimeoutMilliseconds: 10000),
                    deadline.Token, postResync: router.TryRequestResync));
                var tls = TlsSocketChecks.CreateClientTls(client);
                secureStreams.Add(tls);
                await TlsSocketChecks.AuthenticateClient(tls)
                    .WaitAsync(deadline.Token);
            }

            // ACKs pass through the real authenticated wire parser, not an
            // artificial call to the Gateway's internal acknowledgement setter.
            for (int i = 0; i < secureStreams.Count; i++)
            {
                await secureStreams[i].WriteAsync(
                    WireProtocol.EncodeWorldReadyAck(new(
                        actors[i].MapEpoch, initialReadyTicks[i])), deadline.Token);
                await secureStreams[i].FlushAsync(deadline.Token);
            }
            while (!gateways.All(x => x.WorldReadyAcknowledged))
            {
                deadline.Token.ThrowIfCancellationRequested();
                await Task.Delay(2, deadline.Token);
            }

            var starts = new long[gateways.Count];
            var readers = new Task<(long Bytes, int Frames, double LatencyMs)>[gateways.Count];
            for (int i = 0; i < gateways.Count; i++)
            {
                int observer = i;
                readers[i] = ReadTlsSnapshotAsync(
                    secureStreams[i], observer, actors[i], monstersPerCluster,
                    dropsPerCluster, () => starts[observer], deadline.Token);
            }
            for (int i = 0; i < gateways.Count; i++)
            {
                starts[i] = Stopwatch.GetTimestamp();
                await secureStreams[i].WriteAsync(
                    WireProtocol.EncodeWorldResyncRequest(
                        new(actors[i].MapEpoch)), deadline.Token);
                await secureStreams[i].FlushAsync(deadline.Token);
            }

            var completed = await Task.WhenAll(readers).WaitAsync(deadline.Token);
            Require(completed.Length == gateways.Count &&
                    completed.All(x => x.Frames >= 1 + monstersPerCluster + dropsPerCluster),
                "20 real TLS connections did not receive complete AOI resync");
            Require(servers.All(x => !x.IsCompleted),
                "one of the TLS sessions exited before clients finished resync");
            var latencies = completed.Select(x => x.LatencyMs)
                .OrderBy(x => x).ToArray();
            static double P(double[] values, double q) =>
                values[Math.Clamp((int)Math.Ceiling(values.Length * q) - 1,
                    0, values.Length - 1)];
            Console.WriteLine(
                $"SERVER_MIXED_TLS_RESYNC_PASS clients={gateways.Count} " +
                $"monsters={gateways.Count * monstersPerCluster} " +
                $"drops={gateways.Count * dropsPerCluster} " +
                $"network_frames={completed.Sum(x => x.Frames)} " +
                $"network_payload_bytes={completed.Sum(x => x.Bytes)} " +
                $"snapshot_latency_p50_ms={P(latencies, .50):F2} " +
                $"snapshot_latency_p95_ms={P(latencies, .95):F2} " +
                $"snapshot_latency_p99_ms={P(latencies, .99):F2} " +
                "encrypted=true auth_bindings=test_only renderer=none");
        }
        finally
        {
            foreach (var stream in secureStreams)
                await stream.DisposeAsync();
            foreach (var client in clients)
                client.Dispose();
            // A test socket's Close() also closes its pre-attached Gateway.
            // Remove the bindings before the AOI pump is stopped.
            foreach (var gateway in gateways)
                router.TryDetach(gateway);
            if (servers.Count > 0)
            {
                try
                {
                    var finished = await Task.WhenAll(servers)
                        .WaitAsync(TimeSpan.FromSeconds(5));
                    Require(finished.All(x =>
                        x.Reason is TlsWireEndReason.CleanEof or TlsWireEndReason.Cancelled &&
                        x.Protocol is SslProtocols.Tls12 or SslProtocols.Tls13),
                        "TLS resync tests ended with a socket/protocol failure");
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    // The test's own deadline already reports an explicit failure.
                }
            }
        }
    }

    private static async Task<(long Bytes, int Frames, double LatencyMs)> ReadTlsSnapshotAsync(
        SslStream tls, int observer, ActorSnapshot actor,
        int monstersPerCluster, int dropsPerCluster, Func<long> startedAt,
        CancellationToken cancellationToken)
    {
        bool ready = false, self = false;
        var monsters = new HashSet<int>();
        var drops = new HashSet<long>();
        long encodedBytes = 0;
        int frames = 0;
        while (!ready || !self || monsters.Count != monstersPerCluster ||
               drops.Count != dropsPerCluster)
        {
            var lengthBytes = new byte[4];
            await tls.ReadExactlyAsync(lengthBytes, cancellationToken);
            int size = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            Require(size >= WireProtocol.HeaderBytes &&
                    size <= WireProtocol.MaximumFrameBytes,
                $"TLS observer {observer} invalid frame length {size}");
            var frame = new byte[size];
            lengthBytes.CopyTo(frame, 0);
            await tls.ReadExactlyAsync(frame.AsMemory(4), cancellationToken);
            Require(WireProtocol.TryReadHeader(frame, out var header),
                $"TLS observer {observer} malformed frame");
            encodedBytes += size;
            frames++;
            Require(frames < 200,
                $"TLS observer {observer} resync did not converge within 200 frames");

            if (header.Opcode == WireOpcode.WorldReady)
            {
                Require(WireProtocol.TryDecodeWorldReady(frame, out var result) &&
                        result.ActorId == actor.ActorId &&
                        result.ReceiverMapEpoch == actor.MapEpoch &&
                        !ready, $"TLS observer {observer} invalid/replayed WorldReady");
                ready = true;
                continue;
            }
            if (header.Opcode == WireOpcode.SelfState)
            {
                Require(ready && !self &&
                    WireProtocol.TryDecodeEntityState(
                        frame, out var kind, out var result) &&
                    kind == WireOpcode.SelfState &&
                    result.ActorId == actor.ActorId,
                    $"TLS observer {observer} wrong SelfState");
                self = true;
                continue;
            }
            if (header.Opcode == WireOpcode.MonsterDelta)
                continue; // Legitimate concurrent AI delta, NOT part of baseline.
            if (header.Opcode == WireOpcode.MonsterSpawn)
            {
                Require(ready && WireProtocol.TryDecodeMonsterState(
                    frame, out _, out var monster) &&
                    monster.SourceIndex >= observer * monstersPerCluster &&
                    monster.SourceIndex < (observer + 1) * monstersPerCluster &&
                    monsters.Add(monster.SourceIndex),
                    $"TLS observer {observer} missing, foreign or repeated monster");
                continue;
            }
            if (header.Opcode == WireOpcode.GroundDropSpawn)
            {
                long first = 200_000L + observer * dropsPerCluster;
                Require(ready && WireProtocol.TryDecodeGroundDropSpawn(frame, out var drop) &&
                    drop.DropId >= first &&
                    drop.DropId < first + dropsPerCluster &&
                    drops.Add(drop.DropId),
                    $"TLS observer {observer} missing, foreign or repeated ground drop");
                continue;
            }
            Require(false, $"TLS observer {observer} unexpected opcode {header.Opcode}");
        }
        long since = startedAt();
        Require(since > 0, $"TLS observer {observer} no resync send time");
        return (encodedBytes, frames,
            Stopwatch.GetElapsedTime(since).TotalMilliseconds);
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
