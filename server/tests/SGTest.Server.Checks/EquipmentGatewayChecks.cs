using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class EquipmentGatewayChecks
{
    public static void Verify()
    {
        Checks.Run("equipment gateway binds identity and rejects forged server state", WireAndGateway);
    }

    private static void WireAndGateway()
    {
        var actor=new ActorSnapshot(11,13,19,9,0,0,24,24,1,false);
        var gateway=new GatewayConnection(actor);

        TrustedEquipmentCommand equip=default;
        var frame=WireProtocol.EncodeEquipmentEquip(new(9,7,7001));
        var feed=gateway.FeedClientBytes(frame,_=>true,out var accepted,
            postEquipment:value=>{equip=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            equip.ActorId==11&&equip.ConnectionEpoch==19&&
            equip.RequestMapEpoch==9&&equip.EquipmentSequence==7&&
            equip.Kind==TrustedEquipmentCommandKind.Equip&&equip.ItemUid==7001,
            "equipment request did not bind authenticated identity");

        var result=new EquipmentTransferResult(
            EquipmentTransferStatus.Applied,EquipmentTransferKind.Equip,
            7001,3701,4,501);
        Checks.Require(gateway.QueueEquipmentTransferResult(equip,result)==
            GatewaySendStatus.Enqueued&&gateway.TryDequeueReliable(out var receipt)&&
            WireProtocol.TryDecodeEquipmentTransferResult(receipt,out var decoded)&&
            decoded.MapEpoch==9&&decoded.EquipmentSequence==7&&
            decoded.ItemUid==7001&&decoded.ItemDefinitionId==3701&&
            decoded.SlotId==4&&decoded.SourceDropId==501,
            "equipment durable receipt did not enter reliable FIFO");

        TrustedEquipmentCommand unequip=default;
        frame=WireProtocol.EncodeEquipmentUnequip(new(9,8,7001));
        feed=gateway.FeedClientBytes(frame,_=>true,out accepted,
            postEquipment:value=>{unequip=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            unequip.Kind==TrustedEquipmentCommandKind.Unequip&&unequip.ItemUid==7001,
            "unequip trusted context mismatch");

        var forged=new GatewayConnection(actor);
        Checks.Require(forged.FeedClientBytes(
            WireProtocol.EncodeEquipmentTransferResult(new(
                9,7,EquipmentTransferKind.Equip,EquipmentTransferStatus.Applied,
                7001,3701,4,501)),_=>true,out _)==WireFeedStatus.ProtocolViolation,
            "client forged server-only equipment result");

        Checks.Require(gateway.QueueEquipmentTransferResult(
            equip with {ConnectionEpoch=20},result)==GatewaySendStatus.GenerationMismatch,
            "stale equipment generation received receipt");
    }
}
