using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class StorageWireChecks
{
    public static void Verify()
    {
        Checks.Run("storage wire v1 fixed requests snapshots and durable result", () =>
        {
            var open=new StorageOpenWire(9,7,99);
            var openFrame=WireProtocol.EncodeStorageOpen(open);
            Checks.Require(Convert.ToHexString(openFrame)==
                "180000000100340009000000000000000700000063000000" &&
                WireProtocol.TryDecodeStorageOpen(openFrame,out var openBack) &&
                openBack==open,"storage open golden/roundtrip");

            var move=new StorageMoveWire(9,8,99,7001);
            var deposit=WireProtocol.EncodeStorageDeposit(move);
            var withdraw=WireProtocol.EncodeStorageWithdraw(move);
            Checks.Require(deposit.Length==WireProtocol.StorageMoveFrameBytes &&
                withdraw.Length==WireProtocol.StorageMoveFrameBytes &&
                WireProtocol.TryDecodeStorageDeposit(deposit,out var depBack) &&
                depBack==move &&
                WireProtocol.TryDecodeStorageWithdraw(withdraw,out var withBack) &&
                withBack==move,"storage move roundtrip");

            var snapshot=new StorageSnapshotWire(9,7,StorageOpenStatus.Opened,
                99,0,true,[
                    new StorageItemWire(7001,2701,2,501),
                    new StorageItemWire(7002,2702,1,0)]);
            var snapshotFrame=WireProtocol.EncodeStorageSnapshot(snapshot);
            Checks.Require(
                WireProtocol.TryDecodeStorageSnapshot(snapshotFrame,out var snapshotBack) &&
                snapshotBack is not null &&
                snapshotBack.MapEpoch==9&&snapshotBack.StorageSequence==7&&
                snapshotBack.NpcSpawnId==99&&snapshotBack.Complete&&
                snapshotBack.Items.SequenceEqual(snapshot.Items),
                "storage snapshot roundtrip");

            var result=new StorageTransferResultWire(
                9,8,StorageTransferKind.Deposit,StorageTransferStatus.Applied,
                99,7001,2701,2,501);
            var resultFrame=WireProtocol.EncodeStorageTransferResult(result);
            Checks.Require(resultFrame.Length==WireProtocol.StorageTransferResultFrameBytes &&
                WireProtocol.TryDecodeStorageTransferResult(resultFrame,out var resultBack) &&
                resultBack==result,"storage transfer result roundtrip");
        });

        Checks.Run("storage wire rejects invalid chunks and malformed result", () =>
        {
            var failed=new StorageSnapshotWire(9,7,StorageOpenStatus.TooFar,
                99,0,true,[]);
            Checks.Require(WireProtocol.TryDecodeStorageSnapshot(
                WireProtocol.EncodeStorageSnapshot(failed),out _),
                "empty access rejection should be representable");

            bool badFailure=false,badDuplicate=false,badResult=false;
            try
            {
                WireProtocol.EncodeStorageSnapshot(new(9,7,StorageOpenStatus.TooFar,
                    99,0,true,[new StorageItemWire(1,1,1,0)]));
            }
            catch(ArgumentOutOfRangeException){badFailure=true;}
            try
            {
                WireProtocol.EncodeStorageSnapshot(new(9,7,StorageOpenStatus.Opened,
                    99,0,true,[
                        new StorageItemWire(1,1,1,0),
                        new StorageItemWire(1,2,1,0)]));
            }
            catch(ArgumentOutOfRangeException){badDuplicate=true;}
            try
            {
                WireProtocol.EncodeStorageTransferResult(new(
                    9,8,StorageTransferKind.Withdraw,StorageTransferStatus.Applied,
                    99,7001,0,0,0));
            }
            catch(ArgumentOutOfRangeException){badResult=true;}
            Checks.Require(badFailure&&badDuplicate&&badResult,
                "invalid storage wire payload was admitted");
        });
    }
}
