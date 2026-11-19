using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class RepairWireChecks
{
    public static void Verify()
    {
        Checks.Run("repair wire keeps price durability and wallet server-owned", () =>
        {
            var open=new RepairOpenWire(9,7,88);
            var openFrame=WireProtocol.EncodeRepairOpen(open);
            Checks.Require(openFrame.Length==WireProtocol.RepairOpenFrameBytes&&
                WireProtocol.TryDecodeRepairOpen(openFrame,out var openBack)&&
                openBack==open,"repair open roundtrip");

            var rows=new[]
            {
                new RepairPreviewItemWire(
                    7001,3701,RepairItemLocation.Equipped,80,100,20),
                new RepairPreviewItemWire(
                    7002,3702,RepairItemLocation.Inventory,99,100,1)
            };
            var snapshot=new RepairSnapshotWire(
                9,7,RepairOpenStatus.Opened,88,100,0,true,rows);
            var snapshotFrame=WireProtocol.EncodeRepairSnapshot(snapshot);
            Checks.Require(
                snapshotFrame.Length==
                    WireProtocol.RepairSnapshotHeaderBytes+
                    rows.Length*WireProtocol.RepairPreviewItemBytes&&
                WireProtocol.TryDecodeRepairSnapshot(snapshotFrame,out var decoded)&&
                decoded is not null&&decoded.WalletMoney==100&&
                decoded.Items.Count==2&&decoded.Items[0]==rows[0]&&
                decoded.Items[1]==rows[1],
                "repair snapshot roundtrip");

            var item=new RepairItemWire(9,8,88,7001);
            var itemFrame=WireProtocol.EncodeRepairItem(item);
            Checks.Require(itemFrame.Length==WireProtocol.RepairItemFrameBytes&&
                WireProtocol.TryDecodeRepairItem(itemFrame,out var itemBack)&&
                itemBack==item,
                "repair UID-only request roundtrip");

            var result=new RepairResultWire(
                9,8,RepairStatus.Applied,88,7001,3701,
                RepairItemLocation.Equipped,100,100,20,80);
            var resultFrame=WireProtocol.EncodeRepairResult(result);
            Checks.Require(resultFrame.Length==WireProtocol.RepairResultFrameBytes&&
                WireProtocol.TryDecodeRepairResult(resultFrame,out var resultBack)&&
                resultBack==result,
                "repair result roundtrip");
        });

        Checks.Run("repair wire rejects forged or malformed authority state", () =>
        {
            bool badOpen=false,badSnapshot=false,badResult=false;
            try{WireProtocol.EncodeRepairOpen(new(0,1,88));}
            catch(ArgumentOutOfRangeException){badOpen=true;}
            try
            {
                WireProtocol.EncodeRepairSnapshot(new(
                    9,1,RepairOpenStatus.TooFar,88,1,0,true,
                    Array.Empty<RepairPreviewItemWire>()));
            }
            catch(ArgumentOutOfRangeException){badSnapshot=true;}
            try
            {
                WireProtocol.EncodeRepairResult(new(
                    9,2,RepairStatus.Applied,88,7001,0,
                    RepairItemLocation.None,100,100,20,80));
            }
            catch(ArgumentOutOfRangeException){badResult=true;}
            Checks.Require(badOpen&&badSnapshot&&badResult,
                "malformed repair authority payload was admitted");
        });
    }
}
