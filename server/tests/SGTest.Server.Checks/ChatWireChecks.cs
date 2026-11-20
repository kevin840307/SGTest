using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class ChatWireChecks
{
    public static void Verify()
    {
        Checks.Run("nearby chat wire binds sender identity only at Gateway", Wire);
        Checks.Run("whisper wire binds sender and target in shared chat sequence", Whisper);
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

    private static void Whisper()
    {
        var command=new WhisperCommandWire(3,10,22,3,"跨圖私訊");
        var frame=WireProtocol.EncodeWhisperCommand(command);
        Checks.Require(
            frame.Length>=WireProtocol.WhisperCommandHeaderBytes+1&&
            frame.Length<=WireProtocol.WhisperCommandMaximumFrameBytes&&
            WireProtocol.TryDecodeWhisperCommand(frame,out var decoded)&&
            decoded==command,
            "whisper command wire mismatch");

        var byName=new WhisperCommandWire(
            3,11,0,2,"按名私訊","TargetHero");
        var byNameFrame=WireProtocol.EncodeWhisperCommand(byName);
        Checks.Require(
            Convert.ToHexString(byNameFrame)==
                "370000000100620003000000000000000B0000000000000000000000020C00E68C89E5908DE7A781E8A88A0A005461726765744865726F"&&
            WireProtocol.TryDecodeWhisperCommand(byNameFrame,out var byNameDecoded)&&
            byNameDecoded==byName,
            "whisper character-name wire mismatch");

        bool dualSelectorRejected=false;
        try { _=WireProtocol.EncodeWhisperCommand(new(
            3,12,22,0,"invalid","TargetHero")); }
        catch(ArgumentOutOfRangeException){dualSelectorRejected=true;}
        Checks.Require(dualSelectorRejected,
            "whisper admitted both ActorId and character-name selectors");

        var actor=new ActorSnapshot(11,15,5,3,0,0,8,8,1,false);
        var gateway=new GatewayConnection(actor);
        TrustedChatCommand trusted=default;
        Checks.Require(gateway.FeedClientBytes(
                frame,_=>true,out int accepted,
                postChat:request=>{trusted=request;return true;})==
                WireFeedStatus.Accepted&&accepted==1&&
            trusted.ActorId==11&&trusted.ConnectionEpoch==5&&
            trusted.RequestMapEpoch==3&&trusted.ChatSequence==10&&
            trusted.Channel==ChatChannel.Whisper&&trusted.BubbleType==3&&
            trusted.TargetActorId==22&&trusted.Message=="跨圖私訊",
            "whisper Gateway did not bind authenticated sender/target");

        var result=new WhisperResultWire(
            3,10,WhisperResultStatus.Delivered,22,7,"TargetHero");
        Checks.Require(gateway.QueueWhisperResult(trusted,result)==
                GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var queued)&&
            WireProtocol.TryDecodeWhisperResult(queued,out var decodedResult)&&
            decodedResult==result,
            "whisper sender result reliable roundtrip mismatch");

        var unavailable=new WhisperResultWire(
            3,10,WhisperResultStatus.TargetUnavailable,22,0,string.Empty);
        Checks.Require(
            WireProtocol.TryDecodeWhisperResult(
                WireProtocol.EncodeWhisperResult(unavailable),out var unavailableBack)&&
            unavailableBack==unavailable,
            "whisper unavailable result mismatch");

        bool malformed=false;
        try
        {
            _=WireProtocol.EncodeWhisperResult(new(
                3,10,WhisperResultStatus.Delivered,22,0,string.Empty));
        }
        catch(ArgumentOutOfRangeException){malformed=true;}
        Checks.Require(malformed,
            "whisper delivered result admitted without target identity");

        var unresolvedName=new WhisperResultWire(
            3,11,WhisperResultStatus.TargetUnavailable,0,0,string.Empty);
        Checks.Require(
            WireProtocol.TryDecodeWhisperResult(
                WireProtocol.EncodeWhisperResult(unresolvedName),out var unresolvedBack)&&
            unresolvedBack==unresolvedName,
            "whisper unresolved character-name result mismatch");

        TrustedChatCommand trustedByName=default;
        var byNameGateway=new GatewayConnection(actor);
        Checks.Require(byNameGateway.FeedClientBytes(
                byNameFrame,_=>true,out int byNameAccepted,
                postChat:request=>{trustedByName=request;return true;})==
                WireFeedStatus.Accepted&&byNameAccepted==1&&
            trustedByName.TargetActorId==0&&trustedByName.TargetName=="TargetHero",
            "whisper Gateway did not preserve untrusted name selector");

        var shared=new GatewayConnection(actor);
        var nearby=WireProtocol.EncodeChatCommand(new(
            3,4,ChatChannel.Nearby,0,"nearby"));
        Checks.Require(shared.FeedClientBytes(
                nearby,_=>true,out var nearAccepted,postChat:_=>true)==
                WireFeedStatus.Accepted&&nearAccepted==1,
            "shared chat sequence nearby fixture rejected");
        var staleWhisper=WireProtocol.EncodeWhisperCommand(new(
            3,4,22,0,"same sequence"));
        Checks.Require(shared.FeedClientBytes(
                staleWhisper,_=>true,out var staleAccepted,postChat:_=>true)==
                WireFeedStatus.ProtocolViolation&&staleAccepted==0,
            "whisper bypassed shared chat replay sequence");
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
