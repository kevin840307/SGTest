using SGTest.Server.Chat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Pvp;
using SGTest.Server.Party;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class ChatWireChecks
{
    public static void Verify()
    {
        Checks.Run("nearby chat wire binds sender identity only at Gateway", Wire);
        Checks.Run("whisper wire binds sender and target in shared chat sequence", Whisper);
        Checks.Run("nearby chat gateway rejects unsupported channel and replay", Gate);
        Checks.Run("trusted chat routing isolates nearby and cross-map whisper", Routing);
        Checks.Run("trusted party chat reaches cross-map members only", PartyRouting);
    }

    private static void Routing()
    {
        var router=new WorldResultRouter();
        var sender=new GatewayConnection(
            new ActorSnapshot(11,15,5,3,0,0,8,8,1,false),
            displayName:"SenderHero",
            pvpState:new CharacterPvpState(11,PvpMode.ProtectedBlue,false,0,0,0));
        var target=new GatewayConnection(
            new ActorSnapshot(22,13,7,4,0,0,8,8,1,false),
            displayName:"TargetHero",
            pvpState:new CharacterPvpState(22,PvpMode.ProtectedBlue,false,0,0,0));
        Checks.Require(router.TryAttach(sender)&&router.TryAttach(target),
            "chat routing fixture attach failed");

        ChatRouting.RouteAsync(
            new(11,5,3,1,ChatChannel.Nearby,2,"nearby"),router,null)
            .GetAwaiter().GetResult();
        Checks.Require(sender.TryDequeueReliable(out var nearby)&&
            WireProtocol.TryDecodeChatDelivery(nearby,out var nearbyBack)&&
            nearbyBack.SenderActorId==11&&nearbyBack.SenderName=="SenderHero"&&
            nearbyBack.Channel==ChatChannel.Nearby&&
            !target.TryDequeueReliable(out _),
            "nearby chat leaked to unobserved cross-map player or missed self");

        ChatRouting.RouteAsync(
            new(11,5,3,2,ChatChannel.Whisper,1,"private",0,"TargetHero"),
            router,null).GetAwaiter().GetResult();
        Checks.Require(target.TryDequeueReliable(out var delivery)&&
            WireProtocol.TryDecodeChatDelivery(delivery,out var privateBack)&&
            privateBack.ReceiverMapEpoch==4&&privateBack.Message=="private"&&
            sender.TryDequeueReliable(out var receipt)&&
            WireProtocol.TryDecodeWhisperResult(receipt,out var whisperBack)&&
            whisperBack.Status==WhisperResultStatus.Delivered&&
            whisperBack.TargetActorId==22&&whisperBack.TargetConnectionEpoch==7,
            "cross-map by-name whisper lost target binding or reliable receipt");

        ChatRouting.RouteAsync(
            new(11,5,3,3,ChatChannel.Whisper,0,"missing",0,"Nobody"),
            router,null).GetAwaiter().GetResult();
        Checks.Require(sender.TryDequeueReliable(out var unavailable)&&
            WireProtocol.TryDecodeWhisperResult(unavailable,out var missingBack)&&
            missingBack.Status==WhisperResultStatus.TargetUnavailable&&
            !target.TryDequeueReliable(out _),
            "unresolved whisper leaked to recipient or missed denial receipt");

        ChatRouting.RouteAsync(
            new(11,4,3,4,ChatChannel.Nearby,0,"stale"),router,null)
            .GetAwaiter().GetResult();
        Checks.Require(!sender.TryDequeueReliable(out _)&&
            !target.TryDequeueReliable(out _),
            "stale authenticated chat generation delivered a frame");
    }

    private static void PartyRouting()
    {
        var router=new WorldResultRouter();
        var leader=new GatewayConnection(
            new ActorSnapshot(31,15,11,1,0,0,8,8,1,false),
            displayName:"Leader",
            pvpState:new CharacterPvpState(31,PvpMode.ProtectedBlue,false,0,0,0));
        var member=new GatewayConnection(
            new ActorSnapshot(32,13,12,4,0,0,8,8,1,false),
            displayName:"Member",
            pvpState:new CharacterPvpState(32,PvpMode.ProtectedBlue,false,0,0,0));
        Checks.Require(router.TryAttach(leader)&&router.TryAttach(member),
            "cross-map party chat fixture attach failed");
        var parties=new PartyCoordinator();
        parties.HandleAsync(new(
            31,11,1,1,PartyCommandAction.Invite,0,32),router)
            .GetAwaiter().GetResult();
        PartyResultWire inviteResult=default;
        Checks.Require(leader.TryDequeueReliable(out var inviteFrame)&&
            WireProtocol.TryDecodePartyResult(inviteFrame,out inviteResult)&&
            inviteResult.Status==PartyResultStatus.Success&&inviteResult.PartyId>0&&
            member.TryDequeueReliable(out var receivedInvite)&&
            WireProtocol.TryDecodePartyInvite(receivedInvite,out _)&&
            leader.TryDequeueReliable(out var soloSnapshot)&&
            WireProtocol.TryDecodePartySnapshot(soloSnapshot,out _),
            "trusted party invite did not yield authoritative membership baseline");
        parties.HandleAsync(new(
            32,12,4,1,PartyCommandAction.Accept,inviteResult.PartyId,31),router)
            .GetAwaiter().GetResult();
        Checks.Require(member.TryDequeueReliable(out var acceptResult)&&
            WireProtocol.TryDecodePartyResult(acceptResult,out var accepted)&&
            accepted.Status==PartyResultStatus.Success&&
            leader.TryDequeueReliable(out var leaderSnapshot)&&
            WireProtocol.TryDecodePartySnapshot(leaderSnapshot,out _)&&
            member.TryDequeueReliable(out var memberSnapshot)&&
            WireProtocol.TryDecodePartySnapshot(memberSnapshot,out _),
            "cross-map party invite acceptance did not publish current members");

        ChatRouting.RouteAsync(
            new(31,11,1,2,ChatChannel.Party,0,"party cross map"),router,parties)
            .GetAwaiter().GetResult();
        Checks.Require(leader.TryDequeueReliable(out var selfFrame)&&
            WireProtocol.TryDecodeChatDelivery(selfFrame,out var selfMessage)&&
            selfMessage.ReceiverMapEpoch==1&&
            member.TryDequeueReliable(out var memberFrame)&&
            WireProtocol.TryDecodeChatDelivery(memberFrame,out var memberMessage)&&
            memberMessage.ReceiverMapEpoch==4&&
            selfMessage.Channel==ChatChannel.Party&&
            memberMessage.Message=="party cross map"&&
            !leader.TryDequeueReliable(out _)&&!member.TryDequeueReliable(out _),
            "cross-map party routing delivered wrong epoch or duplicate messages");
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

        var resolvedByName=new WhisperResultWire(
            3,11,WhisperResultStatus.Delivered,22,7,"TargetHero");
        Checks.Require(
            byNameGateway.QueueWhisperResult(trustedByName,resolvedByName)==
                GatewaySendStatus.Enqueued&&
            byNameGateway.TryDequeueReliable(out var byNameQueued)&&
            WireProtocol.TryDecodeWhisperResult(byNameQueued,out var byNameResultBack)&&
            byNameResultBack==resolvedByName,
            "whisper by-name resolved ActorId result was rejected by Gateway correlation");
        Checks.Require(
            gateway.QueueWhisperResult(
                trusted,new WhisperResultWire(
                    3,10,WhisperResultStatus.Delivered,23,7,"WrongTarget"))==
                GatewaySendStatus.GenerationMismatch,
            "legacy ActorId whisper result admitted a mismatched target");

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
