using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class StorageGatewayChecks
{
    public static void Verify()
    {
        Checks.Run("storage gateway binds identity and routes server-only reliable state",
            WireAndGateway);
    }

    private static void WireAndGateway()
    {
        var actor=new ActorSnapshot(11,13,19,9,0,0,24,24,1,false);
        var gateway=new GatewayConnection(actor);

        TrustedStorageCommand opened=default;
        var open=WireProtocol.EncodeStorageOpen(new(9,7,99));
        var feed=gateway.FeedClientBytes(open,_=>true,out var accepted,
            postStorage:value=>{opened=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            opened.ActorId==11&&opened.ConnectionEpoch==19&&
            opened.RequestMapEpoch==9&&opened.StorageSequence==7&&
            opened.NpcSpawnId==99&&opened.Kind==TrustedStorageCommandKind.Open,
            "storage open did not bind authenticated identity");

        var items=new StoredItem[]
        {
            new(7001,11,2701,2,501),
            new(7002,11,2702,1,0)
        };
        Checks.Require(gateway.QueueStorageSnapshot(
                opened,StorageOpenStatus.Opened,items)==GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var frame)&&
            WireProtocol.TryDecodeStorageSnapshot(frame,out var snapshot)&&
            snapshot is not null&&snapshot.Complete&&snapshot.Items.Count==2,
            "storage snapshot did not enter reliable FIFO");

        TrustedStorageCommand deposit=default;
        var move=WireProtocol.EncodeStorageDeposit(new(9,8,99,7001));
        feed=gateway.FeedClientBytes(move,_=>true,out accepted,
            postStorage:value=>{deposit=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            deposit.ActorId==11&&deposit.ConnectionEpoch==19&&
            deposit.Kind==TrustedStorageCommandKind.Deposit&&
            deposit.ItemUid==7001,
            "storage deposit trusted context mismatch");

        var result=new StorageTransferResult(
            StorageTransferStatus.Applied,StorageTransferKind.Deposit,
            7001,2701,2,501);
        Checks.Require(gateway.QueueStorageTransferResult(deposit,result)==
            GatewaySendStatus.Enqueued&&gateway.TryDequeueReliable(out frame)&&
            WireProtocol.TryDecodeStorageTransferResult(frame,out var receipt)&&
            receipt.MapEpoch==9&&receipt.StorageSequence==8&&
            receipt.ItemUid==7001&&receipt.SourceDropId==501,
            "storage committed receipt did not enter trusted FIFO");

        var forged=new GatewayConnection(actor);
        Checks.Require(forged.FeedClientBytes(
            WireProtocol.EncodeStorageTransferResult(new(
                9,8,StorageTransferKind.Deposit,StorageTransferStatus.Applied,
                99,7001,2701,2,501)),_=>true,out _)==WireFeedStatus.ProtocolViolation,
            "client forged server-only storage result");

        Checks.Require(gateway.QueueStorageTransferResult(
            deposit with {ConnectionEpoch=20},result)==GatewaySendStatus.GenerationMismatch,
            "stale storage generation received receipt");
    }
}
