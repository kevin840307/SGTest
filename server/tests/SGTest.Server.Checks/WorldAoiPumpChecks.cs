using System.Text.Json;
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
        Checks.Run("trusted two-map transfer preserves Gateway identity and rebuilds AOI both directions",
            () => TrustedTwoMapTransfer(root).GetAwaiter().GetResult());
        Checks.Run("bounded same-map resync request is rebuilt by the sole AOI pump",
            () => LiveResync(root).GetAwaiter().GetResult());
        Checks.Run("live Host Router Pump publishes coalesced monster state through shared AOI FIFO",
            () => LiveMonsterPipeline(root).GetAwaiter().GetResult());
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

        await using var host = new ServerWorldHost([grid]);
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
            Require(gateway.TryDequeueReliable(out var spawn) &&
                    WireProtocol.TryDecodeMonsterState(spawn,out var kind,out var monster) &&
                    kind==WireOpcode.MonsterSpawn &&
                    monster.EnemyId==1 && monster.MapId==106 &&
                    monster.TargetActorId==actor.ActorId &&
                    monster.State==MonsterAiState.Attack,
                    "monster baseline was not coalesced into authoritative Spawn");

            await WaitForFrames(gateway,1);
            Require(gateway.TryDequeueReliable(out var delta) &&
                    WireProtocol.TryDecodeMonsterState(delta,out kind,out var updated) &&
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
