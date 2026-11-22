using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class EquipmentWireChecks
{
    public static void Verify()
    {
        Checks.Run("equipment wire v1 exposes only UID requests and server slot result", () =>
        {
            var move=new EquipmentMoveWire(9,7,7001);
            var equip=WireProtocol.EncodeEquipmentEquip(move);
            var unequip=WireProtocol.EncodeEquipmentUnequip(move);
            Checks.Require(
                Convert.ToHexString(equip)=="1C00000001003A00090000000000000007000000591B000000000000" &&
                WireProtocol.TryDecodeEquipmentEquip(equip,out var equipBack)&&equipBack==move&&
                WireProtocol.TryDecodeEquipmentUnequip(unequip,out var unequipBack)&&unequipBack==move,
                "equipment UID-only move golden/roundtrip");

            var snapshot=new EquipmentSnapshotWire(11,0,true,[
                new EquipmentItemWire(7001,4,3701,501,5,80)
            ]);
            var snapshotFrame=WireProtocol.EncodeEquipmentSnapshot(snapshot);
            Checks.Require(
                snapshotFrame.Length==WireProtocol.EquipmentSnapshotHeaderBytes+
                    WireProtocol.EquipmentItemBytes&&
                WireProtocol.TryDecodeEquipmentSnapshot(snapshotFrame,out var snapshotBack)&&
                snapshotBack is not null&&snapshotBack.CharacterId==11&&
                snapshotBack.Complete&&snapshotBack.Items.SequenceEqual(snapshot.Items),
                "equipment snapshot roundtrip");

            var result=new EquipmentTransferResultWire(
                9,8,EquipmentTransferKind.Equip,EquipmentTransferStatus.Applied,
                7001,3701,4,501);
            var resultFrame=WireProtocol.EncodeEquipmentTransferResult(result);
            Checks.Require(resultFrame.Length==WireProtocol.EquipmentTransferResultFrameBytes&&
                WireProtocol.TryDecodeEquipmentTransferResult(resultFrame,out var resultBack)&&
                resultBack==result,"equipment result roundtrip");
        });

        Checks.Run("equipment wire rejects duplicate slots and malformed results", () =>
        {
            bool duplicate=false,badSuccess=false,badFailure=false,badDurability=false;
            try
            {
                WireProtocol.EncodeEquipmentSnapshot(new(11,0,true,[
                    new EquipmentItemWire(7001,4,3701,501,5),
                    new EquipmentItemWire(7002,4,3702,502,6)
                ]));
            }
            catch(ArgumentOutOfRangeException){duplicate=true;}
            try
            {
                WireProtocol.EncodeEquipmentTransferResult(new(
                    9,8,EquipmentTransferKind.Equip,EquipmentTransferStatus.Applied,
                    7001,0,0,0));
            }
            catch(ArgumentOutOfRangeException){badSuccess=true;}
            try
            {
                WireProtocol.EncodeEquipmentTransferResult(new(
                    9,8,EquipmentTransferKind.Equip,EquipmentTransferStatus.SlotOccupied,
                    7001,3701,4,0));
            }
            catch(ArgumentOutOfRangeException){badFailure=true;}
            try
            {
                WireProtocol.EncodeEquipmentSnapshot(new(11,0,true,[
                    new EquipmentItemWire(7001,4,3701,501,5,-2)
                ]));
            }
            catch(ArgumentOutOfRangeException){badDurability=true;}
            Checks.Require(duplicate&&badSuccess&&badFailure&&badDurability,
                "invalid equipment wire payload was admitted");
        });
    }
}
