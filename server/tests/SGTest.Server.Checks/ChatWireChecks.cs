using SGTest.Server.Chat;
using SGTest.Server.Clan;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Pvp;
using SGTest.Server.Party;
using SGTest.Server.Wire;
using SGTest.Server.World;
using SGTest.Server.Visibility;

internal static class ChatWireChecks
{
    public static void Verify()
    {
        Checks.Run("nearby chat wire binds sender identity only at Gateway", Wire);
        Checks.Run("whisper wire binds sender and target in shared chat sequence", Whisper);
        Checks.Run("chat gateway admits trusted World/Warband and rejects wrong opcode/replay", Gate);
        Checks.Run("trusted chat routing isolates nearby and cross-map whisper", Routing);
        Checks.Run("emotion command is authoritative and fans out only across mutual AOI", EmotionRouting);
        Checks.Run("chat recipient ignore filters world and whisper delivery", IgnoreRouting);
        Checks.Run("trusted world chat enforces original level gate and crosses maps", WorldRouting);
        Checks.Run("trusted warband chat reaches current clan members only",()=>WarbandRouting().GetAwaiter().GetResult());
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

    private static void EmotionRouting()
    {
        var senderActor=new ActorSnapshot(11,15,5,3,0,0,8,8,1,false);
        var targetActor=new ActorSnapshot(22,15,7,3,1,0,8,8,1,false);
        var outsiderActor=new ActorSnapshot(33,15,9,3,2,0,8,8,1,false);
        var sender=new GatewayConnection(senderActor,displayName:"EmotionSender",
            pvpState:new CharacterPvpState(11,PvpMode.ProtectedBlue,false,0,0,0));
        var target=new GatewayConnection(targetActor,displayName:"EmotionTarget",
            pvpState:new CharacterPvpState(22,PvpMode.ProtectedBlue,false,0,0,0));
        var outsider=new GatewayConnection(outsiderActor,displayName:"EmotionOutsider",
            pvpState:new CharacterPvpState(33,PvpMode.ProtectedBlue,false,0,0,0));
        var router=new WorldResultRouter();
        Checks.Require(router.TryAttach(sender)&&router.TryAttach(target)&&router.TryAttach(outsider),
            "emotion routing fixture attach failed");

        Checks.Require(
            sender.QueueAoi(new(AoiEventKind.Spawn,1,3,targetActor))==GatewaySendStatus.Enqueued&&
            target.QueueAoi(new(AoiEventKind.Spawn,1,3,senderActor))==GatewaySendStatus.Enqueued&&
            sender.TryDequeueReliable(out _)&&target.TryDequeueReliable(out _),
            "emotion routing mutual AOI fixture failed");

        var command=new EmotionCommandWire(3,1,5);
        byte[] frame=WireProtocol.EncodeEmotionCommand(command);
        Checks.Require(frame.Length==WireProtocol.EmotionCommandFrameBytes&&
            WireProtocol.TryDecodeEmotionCommand(frame,out var commandBack)&&commandBack==command,
            "emotion command wire mismatch");
        TrustedChatCommand trusted=default;
        Checks.Require(sender.FeedClientBytes(
                frame,_=>true,out int accepted,
                postChat:request=>{trusted=request;return true;})==WireFeedStatus.Accepted&&accepted==1&&
            trusted.ActorId==11&&trusted.ConnectionEpoch==5&&trusted.RequestMapEpoch==3&&
            trusted.ChatSequence==1&&trusted.Channel==ChatChannel.Emotion&&
            trusted.BubbleType==5&&trusted.Message==string.Empty,
            "emotion gateway did not bind authenticated sender generation");

        ChatRouting.RouteAsync(trusted,router,null).GetAwaiter().GetResult();
        Checks.Require(
            sender.TryDequeueReliable(out var selfFrame)&&
            WireProtocol.TryDecodeEmotionVisual(selfFrame,out var selfVisual)&&
            selfVisual==new EmotionVisualWire(3,11,5,1,5)&&
            target.TryDequeueReliable(out var targetFrame)&&
            WireProtocol.TryDecodeEmotionVisual(targetFrame,out var targetVisual)&&
            targetVisual==new EmotionVisualWire(3,11,5,1,5)&&
            !outsider.TryDequeueReliable(out _)&&
            !sender.TryDequeueReliable(out _)&&!target.TryDequeueReliable(out _),
            "emotion visual missed self/mutual AOI peer, leaked, or duplicated");

        var malformed=frame.ToArray();
        malformed[^1]=13;
        Checks.Require(!WireProtocol.TryDecodeEmotionCommand(malformed,out _),
            "emotion wire admitted id outside original 1..12 range");
    }

    private static void IgnoreRouting()
    {
        var router=new WorldResultRouter();
        var sender=new GatewayConnection(
            new ActorSnapshot(11,15,5,3,0,0,8,8,1,false),
            displayName:"IgnoredSender",
            pvpState:new CharacterPvpState(11,PvpMode.ProtectedBlue,false,0,0,0));
        var target=new GatewayConnection(
            new ActorSnapshot(22,13,7,4,0,0,8,8,1,false),
            displayName:"BlockingTarget",
            pvpState:new CharacterPvpState(22,PvpMode.ProtectedBlue,false,0,0,0));
        Checks.Require(router.TryAttach(sender)&&router.TryAttach(target),
            "ignore chat routing fixture attach failed");
        Task<bool> Ignore(long owner,long source)=>
            Task.FromResult(owner==22&&source==11);

        ChatRouting.RouteAsync(
            new(11,5,3,1,ChatChannel.World,0,"hidden world",PlayerLevel:10),
            router,null,Ignore).GetAwaiter().GetResult();
        Checks.Require(sender.TryDequeueReliable(out var selfFrame)&&
            WireProtocol.TryDecodeChatDelivery(selfFrame,out var selfMessage)&&
            selfMessage.Message=="hidden world"&&!target.TryDequeueReliable(out _),
            "ignored world message reached blocking recipient or missed sender echo");

        ChatRouting.RouteAsync(
            new(11,5,3,2,ChatChannel.Whisper,0,"hidden whisper",22),
            router,null,Ignore).GetAwaiter().GetResult();
        Checks.Require(!target.TryDequeueReliable(out _)&&
            sender.TryDequeueReliable(out var receipt)&&
            WireProtocol.TryDecodeWhisperResult(receipt,out var result)&&
            result.Status==WhisperResultStatus.TargetUnavailable,
            "ignored whisper reached blocking recipient or leaked a delivered receipt");
    }

    private static void WorldRouting()
    {
        var router=new WorldResultRouter();
        var sender=new GatewayConnection(
            new ActorSnapshot(11,15,5,3,0,0,8,8,1,false),
            displayName:"WorldSender",
            pvpState:new CharacterPvpState(11,PvpMode.ProtectedBlue,false,0,0,0));
        var target=new GatewayConnection(
            new ActorSnapshot(22,13,7,4,0,0,8,8,1,false),
            displayName:"WorldTarget",
            pvpState:new CharacterPvpState(22,PvpMode.ProtectedBlue,false,0,0,0));
        Checks.Require(router.TryAttach(sender)&&router.TryAttach(target),
            "world chat routing fixture attach failed");

        ChatRouting.RouteAsync(
            new(11,5,3,1,ChatChannel.World,2,"world cross map",PlayerLevel:10),
            router,null).GetAwaiter().GetResult();
        Checks.Require(
            sender.TryDequeueReliable(out var selfFrame)&&
            WireProtocol.TryDecodeChatDelivery(selfFrame,out var selfMessage)&&
            selfMessage.Channel==ChatChannel.World&&
            selfMessage.ReceiverMapEpoch==3&&
            target.TryDequeueReliable(out var targetFrame)&&
            WireProtocol.TryDecodeChatDelivery(targetFrame,out var targetMessage)&&
            targetMessage.Channel==ChatChannel.World&&
            targetMessage.ReceiverMapEpoch==4&&
            targetMessage.Message=="world cross map"&&
            !sender.TryDequeueReliable(out _)&&!target.TryDequeueReliable(out _),
            "level-10 world chat did not broadcast exactly once across active maps");

        ChatRouting.RouteAsync(
            new(11,5,3,2,ChatChannel.World,2,"too low",PlayerLevel:9),
            router,null).GetAwaiter().GetResult();
        Checks.Require(!sender.TryDequeueReliable(out _)&&!target.TryDequeueReliable(out _),
            "original WorldChannel lvllimit=10 was not enforced by authoritative routing");
    }

    private static async Task WarbandRouting()
    {
        string root=Path.Combine(Path.GetTempPath(),"sgtest_warband_chat_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store=new SqliteIdentityStore(Path.Combine(root,"identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long clanAccount=(await store.CreateAccountAsync(
                "warband_a","Warband-Account-Secret-2026!")).AccountId;
            long outsiderAccount=(await store.CreateAccountAsync(
                "warband_b","Warband-Outsider-Secret-2026!")).AccountId;
            long leaderId=(await store.CreateCharacterAsync(
                clanAccount,0,"WarbandLeader",new(15,8,8))).Character!.CharacterId;
            long memberId=(await store.CreateCharacterAsync(
                clanAccount,1,"WarbandMember",new(13,8,8))).Character!.CharacterId;
            long outsiderId=(await store.CreateCharacterAsync(
                outsiderAccount,0,"WarbandOutsider",new(15,8,8))).Character!.CharacterId;
            var created=await store.CreateClanAsync(leaderId,"星門先遣隊")
                .ConfigureAwait(false);
            Checks.Require(created.Status==ClanStoreStatus.Success&&created.Clan is not null,
                "warband routing fixture clan create failed");
            var joined=await store.JoinClanAsync(memberId,created.Clan!.ClanId)
                .ConfigureAwait(false);
            Checks.Require(joined.Status==ClanStoreStatus.Success,
                "warband routing fixture member join failed");

            var router=new WorldResultRouter();
            var leader=new GatewayConnection(
                new ActorSnapshot(leaderId,15,11,1,0,0,8,8,1,false),
                displayName:"WarbandLeader",
                pvpState:new CharacterPvpState(
                    leaderId,PvpMode.ProtectedBlue,false,0,0,0));
            var member=new GatewayConnection(
                new ActorSnapshot(memberId,13,12,4,0,0,8,8,1,false),
                displayName:"WarbandMember",
                pvpState:new CharacterPvpState(
                    memberId,PvpMode.ProtectedBlue,false,0,0,0));
            var outsider=new GatewayConnection(
                new ActorSnapshot(outsiderId,15,13,1,0,0,8,8,1,false),
                displayName:"WarbandOutsider",
                pvpState:new CharacterPvpState(
                    outsiderId,PvpMode.ProtectedBlue,false,0,0,0));
            Checks.Require(
                router.TryAttach(leader)&&router.TryAttach(member)&&router.TryAttach(outsider),
                "warband chat routing fixture attach failed");
            var clans=new ClanCoordinator(store);

            await ChatRouting.RouteAsync(
                new(leaderId,11,1,1,ChatChannel.Warband,2,"戰團跨圖"),
                router,null,null,clans).ConfigureAwait(false);
            Checks.Require(
                leader.TryDequeueReliable(out var leaderFrame)&&
                WireProtocol.TryDecodeChatDelivery(leaderFrame,out var leaderBack)&&
                leaderBack.Channel==ChatChannel.Warband&&
                leaderBack.ReceiverMapEpoch==1&&
                member.TryDequeueReliable(out var memberFrame)&&
                WireProtocol.TryDecodeChatDelivery(memberFrame,out var memberBack)&&
                memberBack.Channel==ChatChannel.Warband&&
                memberBack.ReceiverMapEpoch==4&&
                memberBack.Message=="戰團跨圖"&&
                !outsider.TryDequeueReliable(out _)&&
                !leader.TryDequeueReliable(out _)&&!member.TryDequeueReliable(out _),
                "warband chat leaked outside clan or missed a current member");

            await ChatRouting.RouteAsync(
                new(outsiderId,13,1,1,ChatChannel.Warband,0,"不得廣播"),
                router,null,null,clans).ConfigureAwait(false);
            Checks.Require(
                !leader.TryDequeueReliable(out _)&&
                !member.TryDequeueReliable(out _)&&
                !outsider.TryDequeueReliable(out _),
                "non-member Warband chat produced authoritative delivery");
        }
        finally
        {
            Directory.Delete(root,true);
        }
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
        var worldGateway=new GatewayConnection(
            actor,combatProfile:new TrustedCombatProfile(1,1,1,1,48,15,10));
        TrustedChatCommand trustedWorld=default;
        var world=WireProtocol.EncodeChatCommand(new(
            3,1,ChatChannel.World,0,"world"));
        Checks.Require(worldGateway.FeedClientBytes(
                world,_=>true,out int worldAccepted,
                postChat:request=>{trustedWorld=request;return true;})==
                WireFeedStatus.Accepted&&worldAccepted==1&&
            trustedWorld.Channel==ChatChannel.World&&trustedWorld.PlayerLevel==10,
            "World chat did not bind the authoritative player level at Gateway");

        var warbandGateway=new GatewayConnection(actor);
        TrustedChatCommand trustedWarband=default;
        var warband=WireProtocol.EncodeChatCommand(new(
            3,1,ChatChannel.Warband,0,"club"));
        Checks.Require(warbandGateway.FeedClientBytes(
                warband,_=>true,out int warbandAccepted,
                postChat:request=>{trustedWarband=request;return true;})==
                WireFeedStatus.Accepted&&warbandAccepted==1&&
            trustedWarband.Channel==ChatChannel.Warband,
            "source-backed Warband chat was not admitted");

        var invalidGateway=new GatewayConnection(actor);
        var wrongOpcode=WireProtocol.EncodeChatCommand(new(
            3,1,ChatChannel.Whisper,0,"wrong opcode"));
        Checks.Require(invalidGateway.FeedClientBytes(
                wrongOpcode,_=>true,out int invalidAccepted,
                postChat:_=>true)==WireFeedStatus.ProtocolViolation&&
            invalidAccepted==0,
            "Whisper channel bypassed its dedicated command opcode");

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
