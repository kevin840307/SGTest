using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class GroundPickupWireChecks
{
    public static void Verify()
    {
        Checks.Run("pickup V1 CSharp/Godot golden binary rejects forged or malformed item grants",
            WireGolden);
        Checks.Run("pickup command only inherits authenticated Gateway identity and FIFO response",
            AuthenticatedGateway);
    }
    private const string Request =
        "1C00000001002200090000000000000007000000F501000000000000";
    private const string Result =
        "2D0000000100230009000000000000000700000000F501000000000000591B0000000000008D0A000002000000";

    private static void WireGolden()
    {
        var req=new GroundPickupWire(9,7,501);
        byte[] command=WireProtocol.EncodeGroundPickup(req);
        Checks.Require(Convert.ToHexString(command)==Request &&
            WireProtocol.TryDecodeGroundPickup(command,out var parsed) &&
            parsed==req && command.Length==28,
            "pickup request cross-language golden changed");
        var expected=new GroundPickupResultWire(
            9,7,PickupStatus.Granted,501,7001,2701,2);
        byte[] response=WireProtocol.EncodeGroundPickupResult(expected);
        Checks.Require(Convert.ToHexString(response)==Result &&
            WireProtocol.TryDecodeGroundPickupResult(response,out var decoded) &&
            decoded==expected && response.Length==45,
            "pickup result cross-language golden changed");
        byte[] wrong=(byte[])response.Clone();
        wrong[20]=255;
        Checks.Require(!WireProtocol.TryDecodeGroundPickupResult(wrong,out _),
            "unknown pickup status was accepted");
        wrong=(byte[])response.Clone();
        wrong[29]=0;wrong[30]=0;wrong[31]=0;wrong[32]=0;
        Checks.Require(!WireProtocol.TryDecodeGroundPickupResult(wrong,out _),
            "successful pickup with invalid item UID was accepted");
        wrong=(byte[])command.Clone();
        wrong[20]=0;wrong[21]=0;wrong[22]=0;wrong[23]=0;
        Checks.Require(!WireProtocol.TryDecodeGroundPickup(wrong,out _),
            "zero ground UID request accepted");
        byte[] rejected=WireProtocol.EncodeGroundPickupResult(
            new(9,8,PickupStatus.AlreadyTaken,501,0,0,0));
        Checks.Require(WireProtocol.TryDecodeGroundPickupResult(
            rejected,out var miss) && miss.Status==PickupStatus.AlreadyTaken,
            "rejected pickup wire did not roundtrip");
        byte[] full=WireProtocol.EncodeGroundPickupResult(
            new(9,9,PickupStatus.InventoryFull,501,0,0,0));
        Checks.Require(WireProtocol.TryDecodeGroundPickupResult(
            full,out var rejectedFull) && rejectedFull.Status==PickupStatus.InventoryFull,
            "inventory-capacity pickup rejection did not roundtrip");
    }

    private static void AuthenticatedGateway()
    {
        var actor=new ActorSnapshot(11,13,19,9,0,0,24,24,1,false);
        var gateway=new GatewayConnection(actor);
        TrustedGroundPickup trusted=default;
        int called=0;
        byte[] input=WireProtocol.EncodeGroundPickup(new(9,7,501));
        var status=gateway.FeedClientBytes(input,_=>true,out var accepted,
            postPickup:request=>{trusted=request;called++;return true;});
        Checks.Require(status==WireFeedStatus.Accepted && accepted==1 &&
            called==1 && trusted.ActorId==actor.ActorId &&
            trusted.ConnectionEpoch==actor.ConnectionEpoch &&
            trusted.RequestMapEpoch==9 && trusted.PickupSequence==7 &&
            trusted.DropId==501,
            "raw pickup escaped authenticated actor/connection epoch");
        var receipt=new PickupResult(PickupStatus.Granted,501,7001,2701,2);
        Checks.Require(gateway.QueueGroundPickupResult(trusted,receipt)==
            GatewaySendStatus.Enqueued &&
            gateway.TryDequeueReliable(out var frame) &&
            WireProtocol.TryDecodeGroundPickupResult(frame,out var reply) &&
            reply.Status==PickupStatus.Granted && reply.ItemUid==7001,
            "trusted pickup response missing from reliable FIFO");
        Checks.Require(gateway.QueueGroundPickupResult(
            trusted with{ConnectionEpoch=20},receipt)==
            GatewaySendStatus.GenerationMismatch,
            "stale socket pickup result escaped its connection generation");
        var hostile=new GatewayConnection(actor);
        Checks.Require(hostile.FeedClientBytes(
            WireProtocol.EncodeGroundPickupResult(new(9,7,PickupStatus.Granted,501,7001,2701,2)),_=>true,out _) ==
            WireFeedStatus.ProtocolViolation,
            "client forged server-only inventory grant opcode");
    }
}
