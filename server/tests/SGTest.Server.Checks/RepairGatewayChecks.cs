using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class RepairGatewayChecks
{
    public static void Verify()
    {
        Checks.Run("repair gateway binds identity and rejects forged settlement state", Gateway);
    }

    private static void Gateway()
    {
        var actor=new ActorSnapshot(11,15,19,9,0,0,24,24,1,false);
        var gateway=new GatewayConnection(actor);

        TrustedRepairCommand open=default;
        var openFrame=WireProtocol.EncodeRepairOpen(new(9,7,88));
        var feed=gateway.FeedClientBytes(openFrame,_=>true,out var accepted,
            postRepair:value=>{open=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            open.ActorId==11&&open.ConnectionEpoch==19&&
            open.RequestMapEpoch==9&&open.RepairSequence==7&&
            open.NpcSpawnId==88&&open.Kind==TrustedRepairCommandKind.Open&&
            open.ItemUid==0,
            "repair open did not bind authenticated identity");

        var preview=new RepairPreview(100,new[]
        {
            new RepairPreviewItem(
                7001,3701,RepairItemLocation.Equipped,80,100,20)
        });
        Checks.Require(
            gateway.QueueRepairSnapshot(open,RepairOpenStatus.Opened,preview)==
                GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var snapshotFrame)&&
            WireProtocol.TryDecodeRepairSnapshot(snapshotFrame,out var snapshot)&&
            snapshot is not null&&snapshot.WalletMoney==100&&
            snapshot.Items.Single().Cost==20,
            "repair preview did not enter reliable FIFO");

        TrustedRepairCommand repair=default;
        var repairFrame=WireProtocol.EncodeRepairItem(new(9,8,88,7001));
        feed=gateway.FeedClientBytes(repairFrame,_=>true,out accepted,
            postRepair:value=>{repair=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            repair.ActorId==11&&repair.ConnectionEpoch==19&&
            repair.Kind==TrustedRepairCommandKind.Repair&&repair.ItemUid==7001,
            "repair item trusted context mismatch");

        var result=new RepairResult(
            RepairStatus.Applied,88,7001,3701,RepairItemLocation.Equipped,
            100,100,20,80);
        Checks.Require(
            gateway.QueueRepairResult(repair,result)==GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var resultFrame)&&
            WireProtocol.TryDecodeRepairResult(resultFrame,out var decoded)&&
            decoded.ItemUid==7001&&decoded.Cost==20&&decoded.WalletMoney==80,
            "repair result did not enter reliable FIFO");

        var forged=new GatewayConnection(actor);
        Checks.Require(
            forged.FeedClientBytes(
                WireProtocol.EncodeRepairSnapshot(new(
                    9,1,RepairOpenStatus.Opened,88,100,0,true,
                    Array.Empty<RepairPreviewItemWire>())),
                _=>true,out _)==WireFeedStatus.ProtocolViolation&&
            forged.FeedClientBytes(
                WireProtocol.EncodeRepairResult(new(
                    9,1,RepairStatus.Applied,88,7001,3701,
                    RepairItemLocation.Equipped,100,100,20,80)),
                _=>true,out _)==WireFeedStatus.ProtocolViolation,
            "client forged server-only repair snapshot/result");

        Checks.Require(
            gateway.QueueRepairResult(
                repair with{ConnectionEpoch=20},result)==
                GatewaySendStatus.GenerationMismatch,
            "stale repair generation received settlement");
    }
}
