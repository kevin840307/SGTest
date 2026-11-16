using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class GatewayConnectionChecks
{
    private static void Require(bool condition, string name) => Checks.Require(condition, name);

    private static ActorSnapshot Actor(long actorId = 77, long connectionEpoch = 4,
                                       long mapEpoch = 9) =>
        new(actorId, 15, connectionEpoch, mapEpoch, 0, 0, 24, 24, 1, false);

    public static void Verify()
    {
        Checks.Run("gateway binds client Move to trusted actor and connection generations", () =>
        {
            var gateway = new GatewayConnection(Actor());
            byte[] frame = WireProtocol.EncodeMoveTarget(new MoveTargetWire(9, 1, 6616, 4728));
            MoveTargetCommand observed = default;
            bool received = false;

            var partial = gateway.FeedClientBytes(frame.AsSpan(0, 5), command =>
            {
                received = true;
                observed = command;
                return true;
            }, out int firstAccepted);
            Require(partial == WireFeedStatus.NeedMoreData && firstAccepted == 0 && !received,
                "partial frame dispatched");

            var completed = gateway.FeedClientBytes(frame.AsSpan(5), command =>
            {
                received = true;
                observed = command;
                return true;
            }, out int secondAccepted);
            Require(completed == WireFeedStatus.Accepted && secondAccepted == 1 && received,
                "complete Move not dispatched");
            Require(observed.ActorId == 77 && observed.ConnectionEpoch == 4 &&
                    observed.MapEpoch == 9 && observed.MovementSequence == 1 &&
                    observed.TargetX == 6616 && observed.TargetY == 4728,
                "client bytes escaped trusted identity binding");
        });

        Checks.Run("gateway retains a complete frame when World ingress is backpressured", () =>
        {
            var gateway = new GatewayConnection(Actor());
            byte[] first = WireProtocol.EncodeMoveTarget(new MoveTargetWire(9, 1, 100, 200));
            byte[] second = WireProtocol.EncodeMoveTarget(new MoveTargetWire(9, 2, 300, 400));
            byte[] both = first.Concat(second).ToArray();
            int attempts = 0;

            var blocked = gateway.FeedClientBytes(both, command =>
            {
                attempts++;
                return command.MovementSequence == 1;
            }, out int accepted);
            Require(blocked == WireFeedStatus.Backpressured && accepted == 1 &&
                    attempts == 2 && gateway.BufferedInputBytes == second.Length,
                "backpressured frame was consumed or unbounded");

            MoveTargetCommand resumed = default;
            var resumedStatus = gateway.ResumeClientIngress(command =>
            {
                resumed = command;
                return true;
            }, out int resumedCount);
            Require(resumedStatus == WireFeedStatus.Accepted && resumedCount == 1 &&
                    resumed.MovementSequence == 2 && gateway.BufferedInputBytes == 0,
                "retained frame did not resume exactly once");
        });

        Checks.Run("gateway rejects server-only opcode malformed stream and input overflow", () =>
        {
            var wrongDirection = new GatewayConnection(Actor());
            byte[] receipt = WireProtocol.EncodeMoveReceipt(
                new MoveReceiptWire(9, 1, MoveReceiptStatus.Accepted, 42));
            var serverOpcode = wrongDirection.FeedClientBytes(receipt, _ => true, out int accepted);
            Require(serverOpcode == WireFeedStatus.ProtocolViolation && accepted == 0,
                "client sent server-only receipt opcode");
            Require(wrongDirection.FeedClientBytes(
                    WireProtocol.EncodeMoveTarget(new MoveTargetWire(9, 2, 1, 1)),
                    _ => true, out _) == WireFeedStatus.ProtocolViolation,
                "faulted parser accepted later bytes");

            var malformed = new GatewayConnection(Actor());
            var bad = WireProtocol.EncodeMoveTarget(new MoveTargetWire(9, 1, 1, 1));
            bad[0] = 7;
            Require(malformed.FeedClientBytes(bad, _ => true, out _) ==
                    WireFeedStatus.ProtocolViolation,
                "bad advertised length accepted");

            var overflow = new GatewayConnection(Actor());
            Require(overflow.FeedClientBytes(
                    new byte[WireProtocol.MaximumFrameBytes + 1], _ => true, out _) ==
                    WireFeedStatus.BufferOverflow,
                "fixed receive buffer overflow was not fail-closed");
        });

        Checks.Run("gateway reliable queue is bounded FIFO and generation routed", () =>
        {
            var gateway = new GatewayConnection(Actor(), new GatewayConnectionOptions(
                MaxReliableFrames: 2,
                MaxReliableBytes: WireProtocol.MaximumFrameBytes));

            Require(gateway.QueueReceipt(new MoveReceipt(
                    77, 4, 9, 1, MoveReceiptStatus.Accepted, 42)) ==
                    GatewaySendStatus.Enqueued, "first receipt");
            Require(gateway.QueueReceipt(new MoveReceipt(
                    77, 4, 8, 2, MoveReceiptStatus.OldMapEpoch, 43)) ==
                    GatewaySendStatus.Enqueued, "same-connection old-map receipt");
            Require(gateway.QueueReceipt(new MoveReceipt(
                    77, 4, 9, 3, MoveReceiptStatus.Accepted, 44)) ==
                    GatewaySendStatus.Backpressured,
                "reliable queue silently exceeded frame cap");
            Require(gateway.ReliableFrames == 2 &&
                    gateway.ReliableBytes == WireProtocol.MoveReceiptFrameBytes * 2,
                "reliable queue accounting");

            Require(gateway.TryDequeueReliable(out var first) &&
                    WireProtocol.TryDecodeMoveReceipt(first, out var decoded) &&
                    decoded.MapEpoch == 9 && decoded.MovementSequence == 1 &&
                    decoded.ServerTick == 42,
                "reliable queue order or wire receipt corrupt");
            Require(gateway.TryDequeueReliable(out var second) &&
                    WireProtocol.TryDecodeMoveReceipt(second, out var decodedOld) &&
                    decodedOld.MapEpoch == 8 && decodedOld.MovementSequence == 2,
                "old-map receipt lost generation");

            Require(gateway.QueueReceipt(new MoveReceipt(
                    77, 5, 9, 4, MoveReceiptStatus.Accepted, 45)) ==
                    GatewaySendStatus.GenerationMismatch,
                "new connection receipt routed to old connection");
            Require(gateway.QueueReceipt(new MoveReceipt(
                    78, 4, 9, 4, MoveReceiptStatus.Accepted, 45)) ==
                    GatewaySendStatus.GenerationMismatch,
                "other actor receipt routed to connection");
            Require(gateway.QueueReceipt(new MoveReceipt(
                    77, 4, 10, 4, MoveReceiptStatus.Accepted, 45)) ==
                    GatewaySendStatus.GenerationMismatch,
                "future map receipt routed before trusted binding update");
        });

        Checks.Run("gateway map binding advances only inside one authenticated connection", () =>
        {
            var gateway = new GatewayConnection(Actor());
            Require(gateway.TryUpdateTrustedBinding(Actor(mapEpoch: 10)),
                "trusted map epoch advance rejected");
            Require(gateway.Binding == new ConnectionBinding(77, 4, 10),
                "binding did not advance");
            Require(!gateway.TryUpdateTrustedBinding(Actor(mapEpoch: 9)),
                "map epoch moved backwards");
            Require(!gateway.TryUpdateTrustedBinding(Actor(connectionEpoch: 5, mapEpoch: 11)),
                "reconnect reused old GatewayConnection");
            Require(gateway.QueueReceipt(new MoveReceipt(
                    77, 4, 10, 5, MoveReceiptStatus.Accepted, 50)) ==
                    GatewaySendStatus.Enqueued,
                "current map receipt not routable after trusted update");

            gateway.Close();
            Require(gateway.IsClosed && gateway.ReliableFrames == 0 &&
                    gateway.ReliableBytes == 0,
                "close did not release reliable frames");
            Require(gateway.FeedClientBytes(
                    WireProtocol.EncodeMoveTarget(new MoveTargetWire(10, 6, 10, 10)),
                    _ => true, out _) == WireFeedStatus.Closed,
                "closed connection accepted input");
            Require(gateway.QueueReceipt(new MoveReceipt(
                    77, 4, 10, 6, MoveReceiptStatus.Accepted, 51)) ==
                    GatewaySendStatus.Closed,
                "closed connection accepted output");
        });
    }
}
