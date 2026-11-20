using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class ChatWireChecks
{
    public static void Verify()
    {
        Checks.Run("nearby chat wire binds sender identity only at Gateway", Wire);
        Checks.Run("nearby chat gateway rejects unsupported channel and replay", Gate);
    }

    private static void Wire()
    {
        var value=new ChatCommandWire(3,9,ChatChannel.Nearby,2,"附近測試");
        byte[] frame=WireProtocol.EncodeChatCommand(value);
        Checks.Require(
            frame.Length>=WireProtocol.ChatCommandHeaderBytes+1&&
            frame.Length<=WireProtocol.ChatCommandMaximumFrameBytes&&
            WireProtocol.TryDecodeChatCommand(frame,out var decoded)&&decoded==value,
            "chat command wire mismatch");

        var actor=new ActorSnapshot(11,15,5,3,0,0,8,8,1,false);
        var gateway=new GatewayConnection(actor);
        TrustedChatCommand trusted=default;
        Checks.Require(gateway.FeedClientBytes(
                frame,_=>true,out int accepted,
                postChat:request=>{trusted=request;return true;})==
                WireFeedStatus.Accepted&&accepted==1&&
            trusted.ActorId==11&&trusted.ConnectionEpoch==5&&
            trusted.RequestMapEpoch==3&&trusted.ChatSequence==9&&
            trusted.Channel==ChatChannel.Nearby&&trusted.BubbleType==2&&
            trusted.Message=="附近測試",
            "chat Gateway did not inject authenticated sender binding");

        var delivery=new ChatDeliveryWire(
            3,11,5,9,ChatChannel.Nearby,2,"Hero","附近測試");
        Checks.Require(gateway.QueueChatDelivery(gateway.Binding,delivery)==
                GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var queued)&&
            WireProtocol.TryDecodeChatDelivery(queued,out var decodedDelivery)&&
            decodedDelivery==delivery,
            "chat reliable delivery mismatch");

        var tooLong=new string('x',WireProtocol.ChatMaxMessageBytes+1);
        bool rejected=false;
        try { _=WireProtocol.EncodeChatCommand(new(
            3,10,ChatChannel.Nearby,0,tooLong)); }
        catch(ArgumentOutOfRangeException){ rejected=true; }
        Checks.Require(rejected,"chat transport admitted over-limit UTF8 payload");
    }

    private static void Gate()
    {
        var actor=new ActorSnapshot(11,15,5,3,0,0,8,8,1,false);
        var factionGateway=new GatewayConnection(actor);
        var faction=WireProtocol.EncodeChatCommand(new(
            3,1,ChatChannel.Faction,0,"not enabled"));
        Checks.Require(factionGateway.FeedClientBytes(
                faction,_=>true,out int factionAccepted,
                postChat:_=>true)==WireFeedStatus.ProtocolViolation&&
            factionAccepted==0,
            "unverified Faction chat was admitted");

        var replayGateway=new GatewayConnection(actor);
        var first=WireProtocol.EncodeChatCommand(new(
            3,5,ChatChannel.Nearby,0,"first"));
        Checks.Require(replayGateway.FeedClientBytes(
                first,_=>true,out int firstAccepted,
                postChat:_=>true)==WireFeedStatus.Accepted&&firstAccepted==1,
            "nearby chat fixture first command rejected");
        var replay=WireProtocol.EncodeChatCommand(new(
            3,5,ChatChannel.Nearby,0,"replay"));
        Checks.Require(replayGateway.FeedClientBytes(
                replay,_=>true,out int replayAccepted,
                postChat:_=>true)==WireFeedStatus.ProtocolViolation&&
            replayAccepted==0,
            "replayed nearby chat sequence admitted");
    }
}
