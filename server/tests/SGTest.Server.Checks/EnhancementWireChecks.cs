using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class EnhancementWireChecks
{
    public static void Verify()
    {
        Checks.Run("enhancement wire keeps probability cost roll and wallet server-owned", () =>
        {
            var open=new EnhancementOpenWire(9,7,99);
            var openFrame=WireProtocol.EncodeEnhancementOpen(open);
            Checks.Require(
                openFrame.Length==WireProtocol.EnhancementOpenFrameBytes&&
                WireProtocol.TryDecodeEnhancementOpen(openFrame,out var openBack)&&
                openBack==open,
                "enhancement open roundtrip");

            var rows=new[]
            {
                new EnhancementPreviewItemWire(
                    7001,5701,EnhancementItemLocation.Equipped,
                    0,1,750000,50,true),
                new EnhancementPreviewItemWire(
                    7002,5702,EnhancementItemLocation.Inventory,
                    3,4,1000000,80,false)
            };
            var snapshot=new EnhancementSnapshotWire(
                9,7,EnhancementOpenStatus.Opened,99,100,0,true,rows);
            var snapshotFrame=WireProtocol.EncodeEnhancementSnapshot(snapshot);
            Checks.Require(
                snapshotFrame.Length==
                    WireProtocol.EnhancementSnapshotHeaderBytes+
                    rows.Length*WireProtocol.EnhancementPreviewItemBytes&&
                WireProtocol.TryDecodeEnhancementSnapshot(
                    snapshotFrame,out var decoded)&&
                decoded is not null&&decoded.WalletMoney==100&&
                decoded.Items.Count==2&&decoded.Items[0]==rows[0]&&
                decoded.Items[1]==rows[1],
                "enhancement snapshot roundtrip");

            var item=new EnhancementItemWire(9,8,99,7001);
            var itemFrame=WireProtocol.EncodeEnhancementItem(item);
            Checks.Require(
                itemFrame.Length==WireProtocol.EnhancementItemFrameBytes&&
                WireProtocol.TryDecodeEnhancementItem(itemFrame,out var itemBack)&&
                itemBack==item,
                "enhancement UID-only request roundtrip");

            var result=new EnhancementResultWire(
                9,8,EnhancementStatus.Applied,99,7001,5701,
                EnhancementItemLocation.Equipped,
                0,1,1,true,50,50,50,123);
            var resultFrame=WireProtocol.EncodeEnhancementResult(result);
            Checks.Require(
                resultFrame.Length==WireProtocol.EnhancementResultFrameBytes&&
                WireProtocol.TryDecodeEnhancementResult(
                    resultFrame,out var resultBack)&&resultBack==result,
                "enhancement result roundtrip");
        });

        Checks.Run("enhancement wire rejects forged or malformed authority state", () =>
        {
            bool badOpen=false,badSnapshot=false,badResult=false;
            try{WireProtocol.EncodeEnhancementOpen(new(0,1,99));}
            catch(ArgumentOutOfRangeException){badOpen=true;}
            try
            {
                WireProtocol.EncodeEnhancementSnapshot(new(
                    9,1,EnhancementOpenStatus.TooFar,99,1,0,true,
                    Array.Empty<EnhancementPreviewItemWire>()));
            }
            catch(ArgumentOutOfRangeException){badSnapshot=true;}
            try
            {
                WireProtocol.EncodeEnhancementResult(new(
                    9,2,EnhancementStatus.Applied,99,7001,0,
                    EnhancementItemLocation.None,
                    0,1,1,true,50,50,50,1));
            }
            catch(ArgumentOutOfRangeException){badResult=true;}
            Checks.Require(badOpen&&badSnapshot&&badResult,
                "malformed enhancement authority payload was admitted");
        });
    }
}
