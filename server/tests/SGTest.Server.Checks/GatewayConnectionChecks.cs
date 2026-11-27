using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Visibility;
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

        Checks.Run("gateway attribute allocation rejects replay without losing backpressured command", () =>
        {
            static void OpenGameplay(GatewayConnection gateway, ActorSnapshot actor)
            {
                Require(gateway.QueueAoi(new(
                        AoiEventKind.WorldReady,42,actor.MapEpoch,actor)) ==
                        GatewaySendStatus.Enqueued,
                    "attribute replay gate WorldReady enqueue failed");
                Require(gateway.FeedClientBytes(
                        WireProtocol.EncodeWorldReadyAck(new(actor.MapEpoch,42)),
                        _ => true,out int controls) == WireFeedStatus.Accepted &&
                        controls == 0 && gateway.WorldReadyAcknowledged,
                    "attribute replay gate WorldReady ACK failed");
            }

            var actor=Actor();
            var acceptedGateway=new GatewayConnection(actor);
            OpenGameplay(acceptedGateway,actor);
            byte[] allocation=WireProtocol.EncodePlayerAttributeAllocate(
                new(7,CharacterAttributeKind.Strength));
            int dispatched=0;
            TrustedPlayerAttributeAllocate observed=default;
            var accepted=acceptedGateway.FeedClientBytes(
                allocation,_ => true,out int acceptedCount,
                postAttribute: command =>
                {
                    dispatched++;
                    observed=command;
                    return true;
                });
            Require(accepted==WireFeedStatus.Accepted && acceptedCount==1 &&
                    dispatched==1 && observed.ActorId==actor.ActorId &&
                    observed.ConnectionEpoch==actor.ConnectionEpoch &&
                    observed.MapEpoch==actor.MapEpoch &&
                    observed.AttributeSequence==7 &&
                    observed.Attribute==CharacterAttributeKind.Strength,
                "attribute allocation did not preserve trusted binding/sequence");

            var replay=acceptedGateway.FeedClientBytes(
                allocation,_ => true,out int replayCount,
                postAttribute: _ =>
                {
                    dispatched++;
                    return true;
                });
            Require(replay==WireFeedStatus.ProtocolViolation && replayCount==0 &&
                    dispatched==1,
                "duplicate attribute sequence was dispatched twice");

            var blockedGateway=new GatewayConnection(actor);
            OpenGameplay(blockedGateway,actor);
            byte[] retained=WireProtocol.EncodePlayerAttributeAllocate(
                new(8,CharacterAttributeKind.Constitution));
            int attempts=0;
            var blocked=blockedGateway.FeedClientBytes(
                retained,_ => true,out int blockedCount,
                postAttribute: _ =>
                {
                    attempts++;
                    return false;
                });
            Require(blocked==WireFeedStatus.Backpressured && blockedCount==0 &&
                    attempts==1 && blockedGateway.BufferedInputBytes==retained.Length,
                "backpressured attribute command was consumed");

            TrustedPlayerAttributeAllocate resumed=default;
            var resumedStatus=blockedGateway.ResumeClientIngress(
                _ => true,out int resumedCount,
                postAttribute: command =>
                {
                    attempts++;
                    resumed=command;
                    return true;
                });
            Require(resumedStatus==WireFeedStatus.Accepted && resumedCount==1 &&
                    attempts==2 && resumed.AttributeSequence==8 &&
                    resumed.Attribute==CharacterAttributeKind.Constitution &&
                    blockedGateway.BufferedInputBytes==0,
                "backpressured attribute command did not resume exactly once");
        });

        Checks.Run("gateway reliable queue is bounded FIFO and generation routed", () =>
        {
            var gateway = new GatewayConnection(Actor(), new GatewayConnectionOptions(
                MaxReliableFrames: 2,
                MaxReliableBytes: WireProtocol.MaximumFrameBytes));

            Require(gateway.QueuePlayerCombatState(
                    new ConnectionBinding(77,4,9),82,100,3,5,7,9) ==
                    GatewaySendStatus.Enqueued, "combat baseline");
            Require(gateway.QueueReceipt(new MoveReceipt(
                    77, 4, 9, 1, MoveReceiptStatus.Accepted, 42)) ==
                    GatewaySendStatus.Enqueued, "first receipt");
            Require(gateway.QueueReceipt(new MoveReceipt(
                    77, 4, 8, 2, MoveReceiptStatus.OldMapEpoch, 43)) ==
                    GatewaySendStatus.Backpressured,
                "baseline participates in bounded reliable FIFO");
            Require(gateway.QueueReceipt(new MoveReceipt(
                    77, 4, 9, 3, MoveReceiptStatus.Accepted, 44)) ==
                    GatewaySendStatus.Backpressured,
                "reliable queue silently exceeded frame cap");
            Require(gateway.ReliableFrames == 2 &&
                    gateway.ReliableBytes ==
                        WireProtocol.PlayerCombatStateFrameBytes +
                        WireProtocol.MoveReceiptFrameBytes,
                "reliable queue accounting");

            Require(gateway.TryDequeueReliable(out var baseline) &&
                    WireProtocol.TryDecodePlayerCombatState(
                        baseline,out var decodedBaseline) &&
                    decodedBaseline==new PlayerCombatStateWire(9,82,100,3,5,7,9),
                "combat baseline FIFO/wire corrupt");
            Require(gateway.TryDequeueReliable(out var first) &&
                    WireProtocol.TryDecodeMoveReceipt(first, out var decoded) &&
                    decoded.MapEpoch == 9 && decoded.MovementSequence == 1 &&
                    decoded.ServerTick == 42,
                "reliable queue order or wire receipt corrupt");

            Require(gateway.QueuePlayerCombatState(
                    new ConnectionBinding(77,4,8),82,100,2,5,6,9) ==
                    GatewaySendStatus.GenerationMismatch,
                "old-map combat baseline routed to current generation");
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
