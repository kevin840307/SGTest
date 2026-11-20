using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class EnhancementGatewayChecks
{
    public static void Verify()
    {
        Checks.Run(
            "enhancement gateway binds identity and rejects forged settlement state",
            Gateway);
    }

    private static void Gateway()
    {
        var actor=new ActorSnapshot(11,15,19,9,0,0,24,24,1,false);
        var gateway=new GatewayConnection(actor);

        TrustedEnhancementCommand open=default;
        var openFrame=WireProtocol.EncodeEnhancementOpen(new(9,7,99));
        var feed=gateway.FeedClientBytes(
            openFrame,_=>true,out var accepted,
            postEnhancement:value=>{open=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            open.ActorId==11&&open.ConnectionEpoch==19&&
            open.RequestMapEpoch==9&&open.EnhancementSequence==7&&
            open.NpcSpawnId==99&&
            open.Kind==TrustedEnhancementCommandKind.Open&&open.ItemUid==0,
            "enhancement open did not bind authenticated identity");

        var preview=new EnhancementPreview(100,new[]
        {
            new EnhancementPreviewItem(
                7001,5701,EnhancementItemLocation.Equipped,
                0,1,750000,50,true)
        });
        Checks.Require(
            gateway.QueueEnhancementSnapshot(
                open,EnhancementOpenStatus.Opened,preview)==
                GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var snapshotFrame)&&
            WireProtocol.TryDecodeEnhancementSnapshot(
                snapshotFrame,out var snapshot)&&snapshot is not null&&
            snapshot.WalletMoney==100&&
            snapshot.Items.Single().MoneyCost==50&&
            snapshot.Items.Single().SuccessPpm==750000,
            "enhancement preview did not enter reliable FIFO");

        TrustedEnhancementCommand enhance=default;
        var itemFrame=WireProtocol.EncodeEnhancementItem(new(9,8,99,7001));
        feed=gateway.FeedClientBytes(
            itemFrame,_=>true,out accepted,
            postEnhancement:value=>{enhance=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            enhance.ActorId==11&&enhance.ConnectionEpoch==19&&
            enhance.Kind==TrustedEnhancementCommandKind.Enhance&&
            enhance.ItemUid==7001,
            "enhancement item trusted context mismatch");

        var result=new EnhancementResult(
            EnhancementStatus.Applied,99,7001,5701,
            EnhancementItemLocation.Equipped,
            0,1,1,true,50,50,50,123);
        Checks.Require(
            gateway.QueueEnhancementResult(enhance,result)==
                GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var resultFrame)&&
            WireProtocol.TryDecodeEnhancementResult(
                resultFrame,out var decoded)&&
            decoded.ItemUid==7001&&decoded.MoneyCost==50&&
            decoded.MoneyCharged==50&&decoded.WalletMoney==50&&
            decoded.CurrentLevel==1,
            "enhancement result did not enter reliable FIFO");

        var forged=new GatewayConnection(actor);
        Checks.Require(
            forged.FeedClientBytes(
                WireProtocol.EncodeEnhancementSnapshot(new(
                    9,1,EnhancementOpenStatus.Opened,99,100,0,true,
                    Array.Empty<EnhancementPreviewItemWire>())),
                _=>true,out _)==WireFeedStatus.ProtocolViolation&&
            forged.FeedClientBytes(
                WireProtocol.EncodeEnhancementResult(new(
                    9,1,EnhancementStatus.Applied,99,7001,5701,
                    EnhancementItemLocation.Equipped,
                    0,1,1,true,50,50,50,1)),
                _=>true,out _)==WireFeedStatus.ProtocolViolation,
            "client forged server-only enhancement snapshot/result");

        Checks.Require(
            gateway.QueueEnhancementResult(
                enhance with{ConnectionEpoch=20},result)==
                GatewaySendStatus.GenerationMismatch,
            "stale enhancement generation received settlement");
    }
}
