using SGTest.Server.Npc;
using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class NpcPortalWireChecks
{
    private const string Request = "18000000010003000100000000000000010000002E000000";
    private const string Accepted = "1D00000001000400010000000000000001000000000200000000000000";
    private const string TooFar = "1D00000001000400020000000000000002000000040200000000000000";

    private static void Require(bool ok, string name) => Checks.Require(ok, name);

    public static void Verify()
    {
        Checks.Run("NPC action V1 CSharp Godot fixed golden and rejects malformed frames",
            Golden);
        Checks.Run("authenticated NPC Gateway binds actor identity and has bounded reliable result",
            Gateway);
    }

    private static void Golden()
    {
        byte[] ask = WireProtocol.EncodeNpcInteract(new(1, 1, 46));
        Require(Convert.ToHexString(ask) == Request &&
                WireProtocol.TryDecodeNpcInteract(ask, out var parsed) &&
                parsed.MapEpoch == 1 && parsed.InteractionSequence == 1 &&
                parsed.NpcSpawnId == 46, "NPC request golden");
        byte[] accepted = WireProtocol.EncodeNpcInteractResult(new(
            1, 1, NpcPortalStatus.Accepted, 2));
        Require(Convert.ToHexString(accepted) == Accepted &&
                WireProtocol.TryDecodeNpcInteractResult(accepted, out var yes) &&
                yes.ResultMapEpoch == 2 && yes.Status == NpcPortalStatus.Accepted,
                "NPC accepted result golden");
        byte[] far = WireProtocol.EncodeNpcInteractResult(new(
            2, 2, NpcPortalStatus.TooFar, 2));
        Require(Convert.ToHexString(far) == TooFar &&
                WireProtocol.TryDecodeNpcInteractResult(far, out var no) &&
                no.Status == NpcPortalStatus.TooFar, "NPC denial result golden");
        ask[20] = 0; ask[21] = 0; ask[22] = 0; ask[23] = 0;
        Require(!WireProtocol.TryDecodeNpcInteract(ask, out _),
                "zero NPC spawn ID accepted");
        far[20] = 200;
        Require(!WireProtocol.TryDecodeNpcInteractResult(far, out _),
                "invalid NPC denial status accepted");
        var truncated = Convert.FromHexString(Request)[..^1];
        Require(!WireProtocol.TryDecodeNpcInteract(truncated, out _),
                "short NPC gameplay command accepted");
    }

    private static void Gateway()
    {
        var actor = new ActorSnapshot(71, 15, 9, 1, 0, 0, 24, 24, 1, false);
        var gateway = new GatewayConnection(actor, new GatewayConnectionOptions(
            MaxReliableFrames: 1));
        TrustedNpcInteraction captured = default;
        var request = Convert.FromHexString(Request);
        var disposition = gateway.FeedClientBytes(request, _ => true,
            out var count, command => { captured = command; return true; });
        Require(disposition == WireFeedStatus.Accepted && count == 1 &&
                captured.ActorId == 71 && captured.ConnectionEpoch == 9 &&
                captured.RequestMapEpoch == 1 && captured.NpcSpawnId == 46,
                "NPC client injected actor identity or callback refused command");
        Require(gateway.QueueNpcResult(captured,
                new(NpcPortalStatus.TooFar, null)) == GatewaySendStatus.Enqueued,
                "trusted NPC denial not queued");
        Require(gateway.QueueNpcResult(captured,
                new(NpcPortalStatus.UnknownNpc, null)) == GatewaySendStatus.Backpressured,
                "reliable queue full silently lost NPC result");
        Require(gateway.TryDequeueReliable(out var denial) &&
                WireProtocol.TryDecodeNpcInteractResult(denial, out var outcome) &&
                outcome.Status == NpcPortalStatus.TooFar,
                "NPC result FIFO status");
        var next = actor with { MapId = 13, MapEpoch = 2, EntityRevision = 2 };
        Require(gateway.TryUpdateTrustedBinding(next) &&
                gateway.QueueNpcResult(captured,
                    new(NpcPortalStatus.Accepted, next)) == GatewaySendStatus.Enqueued,
                "accepted portal did not advance authenticated Gateway generation");
        Require(gateway.TryDequeueReliable(out var success) &&
                Convert.ToHexString(success) == Accepted,
                "NPC accepted result on actual Gateway mismatched golden");

        var noHandler = new GatewayConnection(actor);
        Require(noHandler.FeedClientBytes(request, _ => true, out _)
                == WireFeedStatus.ProtocolViolation,
            "NPC command with no authorized portal handler did not fail closed");
    }
}
