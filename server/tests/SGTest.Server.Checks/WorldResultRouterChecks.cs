using System.Threading.Channels;
using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class WorldResultRouterChecks
{
    private static void Require(bool good, string name) => Checks.Require(good, name);

    private static ActorSnapshot Actor(long id, long conn = 1, long mapEpoch = 1, int mapId = 15) =>
        new(id, mapId, conn, mapEpoch, 0, 0, 24, 24, 1, false);

    private static MoveReceipt Receipt(long id, uint seq, long conn = 1,
                                       long mapEpoch = 1, long tick = 5) =>
        new(id, conn, mapEpoch, seq, MoveReceiptStatus.Accepted, tick);

    public static void Verify(string root)
    {
        Checks.Run("router sends receipts only to authenticated actor in FIFO order and retains deltas",
            FanoutAndDeltas);
        Checks.Run("router rejects stale attach/detach, drops old connection receipts and gates future map",
            Generations);
        Checks.Run("router fails closed on bounded delta or reliable output overflow", Backpressure);
        Checks.Run("router consumes a bounded world result channel exactly once",
            () => SourcePump().GetAwaiter().GetResult());
        Checks.Run("live 30Hz host routes concurrent player receipts through bounded gateway queues",
            () => LiveTwoActors(root).GetAwaiter().GetResult());
    }

    private static void FanoutAndDeltas()
    {
        var router = new WorldResultRouter();
        var first = new GatewayConnection(Actor(11));
        var second = new GatewayConnection(Actor(22));
        Require(router.TryAttach(first) && router.TryAttach(second), "attach both trusted sessions");

        var result = new WorldTickResult(5,
            [Receipt(11, 1), Receipt(22, 3), Receipt(11, 2)],
            [Actor(11), Actor(22)]);
        router.Route(result);
        Require(first.ReliableFrames == 2 && second.ReliableFrames == 1,
            "cross-actor receipt leak or missing frame");
        Require(first.TryDequeueReliable(out var frame1) &&
                WireProtocol.TryDecodeMoveReceipt(frame1, out var move1) &&
                move1.MovementSequence == 1 && move1.ServerTick == 5,
                "first gateway FIFO head");
        Require(first.TryDequeueReliable(out var frame2) &&
                WireProtocol.TryDecodeMoveReceipt(frame2, out var move2) &&
                move2.MovementSequence == 2, "first gateway FIFO tail");
        Require(second.TryDequeueReliable(out var frame3) &&
                WireProtocol.TryDecodeMoveReceipt(frame3, out var move3) &&
                move3.MovementSequence == 3, "second gateway");
        Require(router.ChangedBatches.TryRead(out var changed) &&
                changed.Tick == 5 && changed.Changed.Count == 2 &&
                changed.Changed[0].ActorId == 11 && changed.Changed[1].ActorId == 22,
                "changed actors were lost before AOI consumer");
        Require(router.Statistics.ReceiptsQueued == 3 &&
                router.Statistics.ChangedBatchesQueued == 1,
                "router accounting");
    }

    private static void Generations()
    {
        var router = new WorldResultRouter();
        var first = new GatewayConnection(Actor(41, 1, 3));
        var newer = new GatewayConnection(Actor(41, 2, 4));
        Require(router.TryAttach(first), "initial attach");
        Require(!router.TryAttach(new GatewayConnection(Actor(41, 1, 3))),
            "equal epoch replaced authenticated socket");
        Require(router.TryAttach(newer) && first.IsClosed,
            "new connection did not supersede stale session");
        Require(!router.TryDetach(first) && router.Statistics.ActiveSessions == 1,
            "late disconnect removed a newer session");

        router.Route(new WorldTickResult(6,
            [Receipt(41, 2, 1, 3, 6), Receipt(41, 1, 2, 4, 6)],
            Array.Empty<ActorSnapshot>()));
        Require(router.Statistics.StaleReceiptsSkipped == 1 &&
                newer.ReliableFrames == 1,
                "old connection event delivered to new session");
        Require(newer.TryDequeueReliable(out var delivered) &&
                WireProtocol.TryDecodeMoveReceipt(delivered, out var decoded) &&
                decoded.MapEpoch == 4 && decoded.MovementSequence == 1,
                "current generation event not delivered");

        bool futureRejected = false;
        try { router.Route(new WorldTickResult(7,
            [Receipt(41, 3, 2, 5, 7)], Array.Empty<ActorSnapshot>())); }
        catch (WorldResultRoutingException) { futureRejected = true; }
        Require(futureRejected && newer.ReliableFrames == 0,
            "future map escaped trusted transfer gate");
        Require(router.TryAdvanceTrustedMap(newer, Actor(41, 2, 5, 13)),
            "trusted transfer advance");
        router.Route(new WorldTickResult(8,
            [Receipt(41, 4, 2, 4, 8)], Array.Empty<ActorSnapshot>()));
        Require(newer.TryDequeueReliable(out var oldMap) &&
                WireProtocol.TryDecodeMoveReceipt(oldMap, out var oldDecoded) &&
                oldDecoded.MapEpoch == 4 && oldDecoded.MovementSequence == 4,
                "same-connection old map receipt should be tagged for client discard");
        Require(router.TryDetach(newer) && router.Statistics.ActiveSessions == 0,
            "legitimate detach failed");
    }

    private static void Backpressure()
    {
        var router = new WorldResultRouter(new WorldResultRouterOptions(ChangedBatchCapacity: 1));
        var gateway = new GatewayConnection(Actor(1),
            new GatewayConnectionOptions(MaxReliableFrames: 1));
        Require(router.TryAttach(gateway), "attach bounded gateway");
        router.Route(new WorldTickResult(1,
            [Receipt(1, 1, tick: 1)], [Actor(1)]));
        bool changedOverflow = false;
        try { router.Route(new WorldTickResult(2,
            [Receipt(1, 2, tick: 2)], [Actor(1)])); }
        catch (WorldResultRoutingException) { changedOverflow = true; }
        Require(changedOverflow && gateway.ReliableFrames == 1 &&
                router.Statistics.BackpressureFailures == 1,
                "delta overflow silently lost an authoritative update/ACK");
        Require(router.ChangedBatches.TryRead(out _), "first changed batch missing");
        bool reliableOverflow = false;
        try { router.Route(new WorldTickResult(3,
            [Receipt(1, 3, tick: 3)], Array.Empty<ActorSnapshot>())); }
        catch (WorldResultRoutingException) { reliableOverflow = true; }
        Require(reliableOverflow && router.Statistics.BackpressureFailures == 2 &&
                gateway.ReliableFrames == 1 &&
                router.Statistics.ReceiptsQueued == 1,
                "reliable queue overflow was hidden or retried with duplication");
        Require(!router.TryAttach(new GatewayConnection(Actor(1))),
            "same generation duplicate login replaced valid session");
    }

    private static async Task SourcePump()
    {
        var router = new WorldResultRouter();
        var gateway = new GatewayConnection(Actor(5));
        Require(router.TryAttach(gateway), "attach pump actor");
        var source = Channel.CreateBounded<WorldTickResult>(new BoundedChannelOptions(4)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var task = router.RunAsync(source.Reader);
        Require(source.Writer.TryWrite(new WorldTickResult(1,
            [Receipt(5, 1, tick: 1)], [Actor(5)])), "world result producer");
        source.Writer.TryComplete();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Require(gateway.ReliableFrames == 1 &&
                router.ChangedBatches.TryRead(out var update) && update.Tick == 1 &&
                update.Changed.Count == 1,
                "async result pump lost receipt or changed batch");
        Require(await router.ChangedBatches.WaitToReadAsync() == false,
                "changed output channel not completed with source");
        bool doubleReaderRejected = false;
        try { await router.RunAsync(source.Reader); }
        catch (InvalidOperationException) { doubleReaderRejected = true; }
        Require(doubleReaderRejected, "second result pump silently started");
    }

    private static async Task LiveTwoActors(string root)
    {
        string directory = Checks.Fixture(root, 96, 12, 12, (_, _) => 0);
        var grid = BlockingGrid.Load(directory, 96);
        await using var host = new ServerWorldHost([grid],
            options: new WorldHostOptions(ResultCapacity: 64));
        await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var one = await host.TrustedRegisterAsync(100, 96, 24, 24)
            ?? throw new Exception("first actor registration");
        var two = await host.TrustedRegisterAsync(200, 96, 40, 24)
            ?? throw new Exception("second actor registration");
        var gatewayOne = new GatewayConnection(one);
        var gatewayTwo = new GatewayConnection(two);
        var router = new WorldResultRouter(new WorldResultRouterOptions(ChangedBatchCapacity: 64));
        Require(router.TryAttach(gatewayOne) && router.TryAttach(gatewayTwo),
            "real world session binding");
        var pump = router.RunAsync(host.Results);
        Require(host.TryPostMove(new MoveTargetCommand(one.ActorId,
                one.ConnectionEpoch, one.MapEpoch, 1, 72, 24)), "first command");
        Require(host.TryPostMove(new MoveTargetCommand(two.ActorId,
                two.ConnectionEpoch, two.MapEpoch, 3, 88, 24)), "second command");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (gatewayOne.ReliableFrames == 0 || gatewayTwo.ReliableFrames == 0)
        {
            if (DateTime.UtcNow >= deadline)
                throw new Exception("both authoritative real-host ACKs not routed");
            await Task.Delay(5);
        }
        Require(gatewayOne.TryDequeueReliable(out var firstFrame) &&
                WireProtocol.TryDecodeMoveReceipt(firstFrame, out var first) &&
                first.MovementSequence == 1 && first.Status == MoveReceiptStatus.Accepted,
                "first player real 30Hz ACK wrong");
        Require(gatewayTwo.TryDequeueReliable(out var secondFrame) &&
                WireProtocol.TryDecodeMoveReceipt(secondFrame, out var second) &&
                second.MovementSequence == 3 && second.Status == MoveReceiptStatus.Accepted,
                "second player real 30Hz ACK wrong");
        // Trusted Join now publishes a lifecycle-only batch before movement.
        // Verify that a real movement Delta arrives without discarding/pretending
        // that the earlier Join/WorldReady source batch was a movement update.
        bool sawDelta = false;
        while (router.ChangedBatches.TryRead(out var changes))
            sawDelta |= changes.Changed.Count > 0;
        Require(sawDelta, "live world delta lost");
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await pump.WaitAsync(TimeSpan.FromSeconds(3));
        Require(router.Statistics.ReceiptsQueued == 2 &&
                host.Statistics.ResultOverflowFailures == 0,
                "router double-send or host result overrun");
    }
}
