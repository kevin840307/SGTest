using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class RecoveryUseWireChecks
{
    public static void Verify()
    {
        Checks.Run("recovery 28/45 V1 CSharp-Godot golden and server-only status guard",
            Golden);
        Checks.Run("recovery client UID becomes a trusted authenticated intent and reliable receipt",
            GatewayTrust);
    }
    private const string Request =
        "1C00000001002500090000000000000007000000591B000000000000";
    private const string Result =
        "2D0000000100260009000000000000000700000000591B000000000000E9030000010000005A00000064000000";

    private static void Golden()
    {
        var request=new RecoveryUseWire(9,7,7001);
        byte[] encoded=WireProtocol.EncodeRecoveryUse(request);
        Checks.Require(encoded.Length==28 && Convert.ToHexString(encoded)==Request &&
            WireProtocol.TryDecodeRecoveryUse(encoded,out var decoded) &&
            decoded==request,"cross-language recovery request golden");
        var result=new RecoveryUseResultWire(
            9,7,RecoveryUseStatus.Granted,7001,1001,1,90,100);
        byte[] receipt=WireProtocol.EncodeRecoveryUseResult(result);
        Checks.Require(receipt.Length==45 && Convert.ToHexString(receipt)==Result &&
            WireProtocol.TryDecodeRecoveryUseResult(receipt,out var parsed) &&
            parsed==result,"cross-language recovery reply golden");
        byte[] hostile=(byte[])receipt.Clone();
        hostile[20]=255;
        Checks.Require(!WireProtocol.TryDecodeRecoveryUseResult(hostile,out _),
            "unknown recovery effect status accepted");
        hostile=(byte[])receipt.Clone();
        hostile[37]=0;hostile[38]=0;hostile[39]=0;hostile[40]=0;
        Checks.Require(!WireProtocol.TryDecodeRecoveryUseResult(hostile,out _),
            "success with impossible zero HP accepted");
        hostile=(byte[])encoded.Clone();
        hostile[20]=0;hostile[21]=0;hostile[22]=0;hostile[23]=0;
        Checks.Require(!WireProtocol.TryDecodeRecoveryUse(hostile,out _),
            "zero inventory UID accepted");
        var rejected=WireProtocol.EncodeRecoveryUseResult(
            new(9,8,RecoveryUseStatus.NoRule,7001,0,0,0,0));
        Checks.Require(WireProtocol.TryDecodeRecoveryUseResult(rejected,out var noRule) &&
            noRule.Status==RecoveryUseStatus.NoRule,
            "rejected recovery receipt cannot roundtrip");
    }
    private static void GatewayTrust()
    {
        var actor=new ActorSnapshot(11,13,19,9,0,0,24,24,1,false);
        var gateway=new GatewayConnection(actor);
        TrustedRecoveryUse trusted=default;
        int count=0;
        var payload=WireProtocol.EncodeRecoveryUse(new(9,7,7001));
        var outcome=gateway.FeedClientBytes(payload,_=>true,out var accepted,
            postRecovery:request=>{trusted=request;count++;return true;});
        Checks.Require(outcome==WireFeedStatus.Accepted && accepted==1 &&
            count==1 && trusted.ActorId==11 && trusted.ConnectionEpoch==19 &&
            trusted.RequestMapEpoch==9 && trusted.UseSequence==7 &&
            trusted.ItemUid==7001,
            "client could forge identity or poison recovery request");
        var committed=new RecoveryUseResult(RecoveryUseStatus.Granted,
            7001,1001,1,60,90,100);
        Checks.Require(gateway.QueueRecoveryUseResult(trusted,committed)==
            GatewaySendStatus.Enqueued &&
            gateway.TryDequeueReliable(out var frame) &&
            WireProtocol.TryDecodeRecoveryUseResult(frame,out var reply) &&
            reply.ItemUid==7001 && reply.CurrentHp==90,
            "only committed recovery could enter trusted FIFO");
        Checks.Require(gateway.QueueRecoveryUseResult(
            trusted with {ConnectionEpoch=100},committed)==
            GatewaySendStatus.GenerationMismatch,
            "stale client generation could receive inventory grant");
        var bad=new GatewayConnection(actor);
        Checks.Require(bad.FeedClientBytes(
            WireProtocol.EncodeRecoveryUseResult(new(9,7,
                RecoveryUseStatus.Granted,7001,1001,1,90,100)),
            _=>true,out _) == WireFeedStatus.ProtocolViolation,
            "client forged a server-only recovery result");
    }
}
