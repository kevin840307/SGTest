using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class AoiWireChecks
{
    private static void Require(bool ok, string name) => Checks.Require(ok, name);
    private const string Ready = "2C0000000100140001000000000000002A000000000000000B000000000000000F0000000000C04100000041";
    private const string Spawn = "490000000100150001000000000000002B000000000000001600000000000000020000000000000001000000000000000F0000000700000000000000000040420000C0410104000000";
    private const string Delta = "490000000100170001000000000000002C000000000000001600000000000000020000000000000001000000000000000F0000000800000000000000000060420000C0410005000000";
    private const string Self = "490000000100180001000000000000002C000000000000000B00000000000000020000000000000001000000000000000F0000000800000000000000000060420000C0410005000000";
    private const string Despawn = "280000000100160001000000000000002D0000000000000016000000000000000200000000000000";

    public static void Verify()
    {
        Checks.Run("AOI compact wire WorldReady/Spawn/Delta/Despawn fixed cross-Godot golden",
            CodecRoundtrip);
        Checks.Run("AOI wire rejects malformed float, generations, sizes and boolean",
            CodecReject);
        Checks.Run("Gateway AOI reliable FIFO enforces viewer MapEpoch, identity and backpressure",
            GatewayAoi);
    }

    private static void CodecRoundtrip()
    {
        byte[] ready = WireProtocol.EncodeWorldReady(new(1, 42, 11, 15, 24, 8));
        Require(Convert.ToHexString(ready) == Ready &&
                WireProtocol.TryDecodeWorldReady(ready, out var own) &&
                own.ActorId == 11 && own.X == 24 && own.ServerTick == 42,
                "WorldReady binary fixture");
        var e = new EntityStateWire(1, 43, 22, 2, 1, 15, 7,
            48, 24, true, 4);
        byte[] spawn = WireProtocol.EncodeEntityState(WireOpcode.EntitySpawn, e);
        Require(Convert.ToHexString(spawn) == Spawn &&
                WireProtocol.TryDecodeEntityState(spawn, out var kind, out var parsed) &&
                kind == WireOpcode.EntitySpawn && parsed == e,
                "Spawn binary fixture");
        e = e with { ServerTick = 44, Revision = 8, X = 56,
                     IsMoving = false, MovementActionSequence = 5 };
        byte[] delta = WireProtocol.EncodeEntityState(WireOpcode.EntityDelta, e);
        Require(Convert.ToHexString(delta) == Delta &&
                WireProtocol.TryDecodeEntityState(delta, out kind, out parsed) &&
                kind == WireOpcode.EntityDelta && parsed == e,
                "Delta binary fixture");
        var self = e with { ActorId = 11 };
        byte[] ownBytes = WireProtocol.EncodeEntityState(WireOpcode.SelfState, self);
        Require(Convert.ToHexString(ownBytes) == Self &&
                WireProtocol.TryDecodeEntityState(ownBytes, out kind, out parsed) &&
                kind == WireOpcode.SelfState && parsed == self, "SelfState binary fixture");
        byte[] despawn = WireProtocol.EncodeEntityDespawn(new(1, 45, 22, 2));
        Require(Convert.ToHexString(despawn) == Despawn &&
                WireProtocol.TryDecodeEntityDespawn(despawn, out var left) &&
                left.ActorId == 22 && left.ConnectionEpoch == 2 &&
                left.ServerTick == 45, "Despawn binary fixture");
    }

    private static void CodecReject()
    {
        var bad = Convert.FromHexString(Spawn);
        bad[68] = 2;
        Require(!WireProtocol.TryDecodeEntityState(bad, out _, out _),
                "invalid moving bit accepted");
        bad = Convert.FromHexString(Ready);
        bad[0] = 43;
        Require(!WireProtocol.TryDecodeWorldReady(bad, out _),
                "invalid Ready frame length accepted");
        bad = Convert.FromHexString(Delta);
        bad[60] = 0; bad[61] = 0; bad[62] = 0xC0; bad[63] = 0x7F; // quiet NaN float
        Require(!WireProtocol.TryDecodeEntityState(bad, out _, out _),
                "NaN coordinate accepted");
        bad = Convert.FromHexString(Despawn);
        Array.Clear(bad, 8, 8);
        Require(!WireProtocol.TryDecodeEntityDespawn(bad, out _),
                "zero receiver epoch accepted");
    }

    private static void GatewayAoi()
    {
        var owner = new ActorSnapshot(11, 15, 1, 1, 0, 0, 24, 8, 1, false);
        var other = new ActorSnapshot(22, 15, 2, 1, 0, 4, 48, 24, 7, true);
        var gateway = new GatewayConnection(owner, new GatewayConnectionOptions(
            MaxReliableFrames: 3, MaxReliableBytes: WireProtocol.MaximumFrameBytes));
        Require(gateway.QueueAoi(new(AoiEventKind.WorldReady, 42, 1, owner)) ==
                GatewaySendStatus.Enqueued, "own Ready enqueue");
        Require(gateway.QueueAoi(new(AoiEventKind.Spawn, 43, 1, other)) ==
                GatewaySendStatus.Enqueued, "other Spawn enqueue");
        Require(gateway.QueueAoi(new(AoiEventKind.Despawn, 45, 1, null, 22, 2)) ==
                GatewaySendStatus.Enqueued, "other Despawn enqueue");
        Require(gateway.QueueAoi(new(AoiEventKind.Delta, 46, 1, other)) ==
                GatewaySendStatus.Backpressured, "bounded AOI queue silently overflowed");
        Require(gateway.TryDequeueReliable(out var first) &&
                WireProtocol.TryDecodeWorldReady(first, out _), "reliable FIFO ready");
        Require(gateway.TryDequeueReliable(out var second) &&
                WireProtocol.TryDecodeEntityState(second, out var op, out _) &&
                op == WireOpcode.EntitySpawn, "reliable FIFO spawn");
        Require(gateway.TryDequeueReliable(out var third) &&
                WireProtocol.TryDecodeEntityDespawn(third, out _),
                "reliable FIFO despawn");

        Require(gateway.QueueAoi(new(AoiEventKind.SelfState, 46, 1, other)) ==
                GatewaySendStatus.GenerationMismatch, "other actor impersonated self state");
        Require(gateway.QueueAoi(new(AoiEventKind.SelfState, 46, 1,
                owner with { ConnectionEpoch = 2 })) == GatewaySendStatus.GenerationMismatch,
                "wrong connection impersonated self state");
        Require(gateway.QueueAoi(new(AoiEventKind.SelfState, 46, 1, owner)) ==
                GatewaySendStatus.Enqueued, "own state enqueue");
        Require(gateway.TryDequeueReliable(out var ownFrame) &&
                WireProtocol.TryDecodeEntityState(ownFrame, out var ownKind, out var own) &&
                ownKind == WireOpcode.SelfState && own.ActorId == owner.ActorId,
                "own state reliable wire");
        Require(gateway.QueueAoi(new(AoiEventKind.WorldReady, 50, 2, owner)) ==
                GatewaySendStatus.GenerationMismatch, "future map sent without transfer");
        Require(gateway.QueueAoi(new(AoiEventKind.WorldReady, 50, 1, other)) ==
                GatewaySendStatus.GenerationMismatch, "other actor impersonated Ready");
        Require(gateway.TryUpdateTrustedBinding(owner with { MapId = 13, MapEpoch = 2 }),
                "trusted MapEpoch transfer");
        Require(gateway.QueueAoi(new(AoiEventKind.WorldReady, 51, 1, owner)) ==
                GatewaySendStatus.GenerationMismatch, "old map AOI leaked after transfer");
        Require(gateway.QueueAoi(new(AoiEventKind.WorldReady, 52, 2,
                owner with { MapId = 13, MapEpoch = 2 })) ==
                GatewaySendStatus.Enqueued, "new map Ready rejected");
    }
}
