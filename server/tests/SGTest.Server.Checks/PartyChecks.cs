using SGTest.Server.Pvp;
using SGTest.Server.Party;
using System.Diagnostics;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class PartyChecks
{
    public static void Verify(string root)
    {
        Checks.Run("party wire binds compact command result invite and snapshot",Wire);
        Checks.Run("party coordinator owns invite leader kick disconnect and recipients",Lifecycle);
        Checks.Run("party invite resolves exact authenticated online character name",InviteByName);
        Checks.Run("party invite cannot cross target reconnect or stale old cleanup",InviteGeneration);
        Checks.Run("party pending invite expires on monotonic clock",InviteTimeout);
        Checks.Run("party failed reliable invite/result enqueue rolls back state",
            InviteEnqueueFailure);
        Checks.Run("party accept backpressure preserves invitation and membership",
            AcceptResultEnqueueFailure);
        Checks.Run("party promote backpressure preserves leader and pending invite",
            PromoteResultEnqueueFailure);
        Checks.Run("party reject backpressure retains retryable invitation",
            RejectResultEnqueueFailure);
    }

    private static void Wire()
    {
        var command=new PartyCommandWire(
            3,7,PartyCommandAction.Invite,0,22);
        var bytes=WireProtocol.EncodePartyCommand(command);
        Checks.Require(bytes.Length==WireProtocol.PartyCommandFrameBytes&&
            WireProtocol.TryDecodePartyCommand(bytes,out var back)&&back==command,
            "party command roundtrip mismatch");
        var byName=new PartyCommandWire(
            3,8,PartyCommandAction.Invite,0,0,"MemberHero");
        var byNameBytes=WireProtocol.EncodePartyCommand(byName);
        Checks.Require(
            byNameBytes.Length==WireProtocol.PartyCommandFrameBytes+
                System.Text.Encoding.UTF8.GetByteCount("MemberHero")&&
            WireProtocol.TryDecodePartyCommand(byNameBytes,out var byNameBack)&&
            byNameBack==byName,
            "party by-name command roundtrip mismatch");
        bool dualSelectorRejected=false;
        try
        {
            _=WireProtocol.EncodePartyCommand(new(
                3,9,PartyCommandAction.Invite,0,22,"MemberHero"));
        }
        catch(ArgumentOutOfRangeException){dualSelectorRejected=true;}
        Checks.Require(dualSelectorRejected,
            "party command admitted ActorId plus name");

        var result=new PartyResultWire(
            3,7,PartyCommandAction.Invite,PartyResultStatus.Success,9001,22);
        Checks.Require(WireProtocol.TryDecodePartyResult(
                WireProtocol.EncodePartyResult(result),out var resultBack)&&
            resultBack==result,
            "party result roundtrip mismatch");

        var invite=new PartyInviteWire(5,9001,2,11,9,"LeaderHero");
        Checks.Require(WireProtocol.TryDecodePartyInvite(
                WireProtocol.EncodePartyInvite(invite),out var inviteBack)&&
            inviteBack==invite,
            "party invite roundtrip mismatch");

        var snapshot=new PartySnapshotWire(
            5,9001,3,11,
            [new(11,9,"LeaderHero"),new(22,4,"MemberHero")]);
        Checks.Require(WireProtocol.TryDecodePartySnapshot(
                WireProtocol.EncodePartySnapshot(snapshot),out var snapshotBack)&&
            snapshotBack is not null&&snapshotBack.PartyId==9001&&
            snapshotBack.Revision==3&&snapshotBack.LeaderActorId==11&&
            snapshotBack.Members.SequenceEqual(snapshot.Members),
            "party snapshot roundtrip mismatch");

        var actor=Actor(11,9,3);
        var gateway=Gateway(actor,"LeaderHero");
        TrustedPartyCommand trusted=default;
        Checks.Require(gateway.FeedClientBytes(
                bytes,_=>true,out int accepted,
                postParty:value=>{trusted=value;return true;})==
                WireFeedStatus.Accepted&&accepted==1&&
            trusted.ActorId==11&&trusted.ConnectionEpoch==9&&
            trusted.RequestMapEpoch==3&&trusted.PartySequence==7&&
            trusted.Action==PartyCommandAction.Invite&&
            trusted.PartyId==0&&trusted.TargetActorId==22,
            "party Gateway did not bind authenticated sender/sequence");
    }

    private static void Lifecycle()
    {
        var router=new WorldResultRouter();
        var a=Gateway(Actor(11,1,1),"PartyA");
        var b=Gateway(Actor(22,2,4),"PartyB");
        var c=Gateway(Actor(33,3,7),"PartyC");
        Checks.Require(router.TryAttach(a)&&router.TryAttach(b)&&router.TryAttach(c),
            "party gateways attach failed");
        var party=new PartyCoordinator();

        var inviteB=new TrustedPartyCommand(
            11,1,1,1,PartyCommandAction.Invite,0,22);
        party.HandleAsync(inviteB,router).GetAwaiter().GetResult();
        var inviteResult=DrainResult(a);
        var bInvite=DrainInvite(b);
        var aSolo=DrainSnapshot(a);
        Checks.Require(inviteResult.Status==PartyResultStatus.Success&&
            inviteResult.PartyId>0&&bInvite.PartyId==inviteResult.PartyId&&
            bInvite.InviterActorId==11&&bInvite.InviterName=="PartyA"&&
            aSolo.LeaderActorId==11&&aSolo.Members.Count==1,
            "party first invite/create mismatch");
        long partyId=inviteResult.PartyId;

        var acceptB=new TrustedPartyCommand(
            22,2,4,1,PartyCommandAction.Accept,partyId,11);
        party.HandleAsync(acceptB,router).GetAwaiter().GetResult();
        Checks.Require(DrainResult(b).Status==PartyResultStatus.Success,
            "party B accept result missing");
        var aTwo=DrainSnapshot(a);
        var bTwo=DrainSnapshot(b);
        Checks.Require(aTwo.Members.Count==2&&bTwo.Members.Count==2&&
            aTwo.LeaderActorId==11&&
            aTwo.Members.Select(x=>x.ActorId).SequenceEqual([11L,22L]),
            "party two-member snapshot mismatch");

        var inviteC=new TrustedPartyCommand(
            11,1,1,2,PartyCommandAction.Invite,partyId,33);
        party.HandleAsync(inviteC,router).GetAwaiter().GetResult();
        Checks.Require(DrainResult(a).Status==PartyResultStatus.Success&&
            DrainInvite(c).InviterActorId==11,
            "party C invite missing");
        var acceptC=new TrustedPartyCommand(
            33,3,7,1,PartyCommandAction.Accept,partyId,11);
        party.HandleAsync(acceptC,router).GetAwaiter().GetResult();
        Checks.Require(DrainResult(c).Status==PartyResultStatus.Success,
            "party C accept result missing");
        var aThree=DrainSnapshot(a);
        var bThree=DrainSnapshot(b);
        var cThree=DrainSnapshot(c);
        Checks.Require(aThree.Members.Count==3&&bThree.Members.Count==3&&
            cThree.Members.Count==3,
            "party three-member broadcast mismatch");

        var promote=new TrustedPartyCommand(
            11,1,1,3,PartyCommandAction.Promote,partyId,22);
        party.HandleAsync(promote,router).GetAwaiter().GetResult();
        Checks.Require(DrainResult(a).Status==PartyResultStatus.Success,
            "party promote result missing");
        Checks.Require(DrainSnapshot(a).LeaderActorId==22&&
            DrainSnapshot(b).LeaderActorId==22&&
            DrainSnapshot(c).LeaderActorId==22,
            "party promoted leader snapshot mismatch");

        var kick=new TrustedPartyCommand(
            22,2,4,2,PartyCommandAction.Kick,partyId,33);
        party.HandleAsync(kick,router).GetAwaiter().GetResult();
        Checks.Require(DrainResult(b).Status==PartyResultStatus.Success,
            "party kick result missing");
        var cClear=DrainSnapshot(c);
        var aAfterKick=DrainSnapshot(a);
        var bAfterKick=DrainSnapshot(b);
        Checks.Require(cClear.Members.Count==0&&cClear.LeaderActorId==0&&
            aAfterKick.Members.Count==2&&bAfterKick.Members.Count==2&&
            aAfterKick.Members.All(x=>x.ActorId!=33),
            "party kick did not clear target/broadcast");

        Checks.Require(party.TryGetRecipients(
                a.Binding,router,out var recipients)&&recipients.Length==2&&
            recipients.Any(x=>x.ActorId==11)&&
            recipients.Any(x=>x.ActorId==22&&x.MapEpoch==4),
            "party chat recipients did not cross map/AOI boundary");

        party.Disconnect(b.Binding,router);
        var aLeader=DrainSnapshot(a);
        Checks.Require(aLeader.LeaderActorId==11&&aLeader.Members.Count==1&&
            party.PartyCount==1,
            "party leader disconnect did not transfer leadership");

        var leave=new TrustedPartyCommand(
            11,1,1,4,PartyCommandAction.Leave,partyId,0);
        party.HandleAsync(leave,router).GetAwaiter().GetResult();
        Checks.Require(DrainResult(a).Status==PartyResultStatus.Success,
            "party leave result missing");
        var aClear=DrainSnapshot(a);
        Checks.Require(aClear.Members.Count==0&&aClear.LeaderActorId==0&&
            party.PartyCount==0,
            "party final leave did not dissolve party");

        router.TryDetach(a);
        router.TryDetach(b);
        router.TryDetach(c);
    }


    private static void InviteByName()
    {
        var router=new WorldResultRouter();
        var a=Gateway(Actor(11,1,1),"PartyA");
        var b=Gateway(Actor(22,2,4),"PartyB");
        Checks.Require(router.TryAttach(a)&&router.TryAttach(b),
            "by-name party gateways attach failed");
        var party=new PartyCoordinator();

        var unknown=new TrustedPartyCommand(
            11,1,1,1,PartyCommandAction.Invite,0,0,"MissingHero");
        party.HandleAsync(unknown,router).GetAwaiter().GetResult();
        var unknownResult=DrainResult(a);
        Checks.Require(
            unknownResult.Status==PartyResultStatus.TargetUnavailable&&
            unknownResult.TargetActorId==0&&party.PartyCount==0,
            "unknown by-name invite mutated party state");

        var wrongCase=new TrustedPartyCommand(
            11,1,1,2,PartyCommandAction.Invite,0,0,"partyb");
        party.HandleAsync(wrongCase,router).GetAwaiter().GetResult();
        var wrongCaseResult=DrainResult(a);
        Checks.Require(
            wrongCaseResult.Status==PartyResultStatus.TargetUnavailable&&
            wrongCaseResult.TargetActorId==0&&party.PartyCount==0,
            "by-name resolution did not preserve character-name semantics");

        var self=new TrustedPartyCommand(
            11,1,1,3,PartyCommandAction.Invite,0,0,"PartyA");
        party.HandleAsync(self,router).GetAwaiter().GetResult();
        var selfResult=DrainResult(a);
        Checks.Require(
            selfResult.Status==PartyResultStatus.CannotTargetSelf&&
            selfResult.TargetActorId==11&&party.PartyCount==0,
            "self by-name invite was not rejected before party creation");

        var invite=new TrustedPartyCommand(
            11,1,1,4,PartyCommandAction.Invite,0,0,"PartyB");
        party.HandleAsync(invite,router).GetAwaiter().GetResult();
        var result=DrainResult(a);
        var received=DrainInvite(b);
        var solo=DrainSnapshot(a);
        Checks.Require(
            result.Status==PartyResultStatus.Success&&result.TargetActorId==22&&
            result.PartyId>0&&received.PartyId==result.PartyId&&
            received.InviterActorId==11&&solo.PartyId==result.PartyId&&
            solo.Members.Count==1&&solo.Members[0].ActorId==11,
            "by-name invite did not resolve to current authenticated target");

        var accept=new TrustedPartyCommand(
            22,2,4,1,PartyCommandAction.Accept,result.PartyId,11);
        party.HandleAsync(accept,router).GetAwaiter().GetResult();
        Checks.Require(DrainResult(b).Status==PartyResultStatus.Success,
            "resolved by-name invite could not be accepted");
        _=DrainSnapshot(a);
        _=DrainSnapshot(b);
        party.Disconnect(a.Binding,router);
        _=DrainSnapshot(b);
        party.Disconnect(b.Binding,router);
        Checks.Require(party.PartyCount==0,
            "by-name party did not clean up");
    }

    private static void InviteGeneration()
    {
        var router=new WorldResultRouter();
        var a=Gateway(Actor(101,10,3),"InviteLeader");
        var oldB=Gateway(Actor(102,20,4),"OldTarget");
        Checks.Require(router.TryAttach(a)&&router.TryAttach(oldB),
            "party generation gateways attach");
        var coordinator=new PartyCoordinator();
        var initial=new TrustedPartyCommand(
            101,10,3,1,PartyCommandAction.Invite,0,102);
        coordinator.HandleAsync(initial,router).GetAwaiter().GetResult();
        long id=DrainResult(a).PartyId;
        Checks.Require(id>0&&DrainInvite(oldB).PartyId==id&&
            DrainSnapshot(a).Members.Count==1,
            "old-generation invite missing");

        // A replacement TLS binding has the same character ActorId but a new
        // ConnectionEpoch. Possessing old PartyId/InviterId must not grant access.
        var newB=Gateway(Actor(102,21,5),"NewTarget");
        Checks.Require(router.TryAttach(newB),
            "party reconnect binding did not replace older socket");
        coordinator.HandleAsync(new TrustedPartyCommand(
            102,21,5,1,PartyCommandAction.Accept,id,101),router)
            .GetAwaiter().GetResult();
        Checks.Require(DrainResult(newB).Status==PartyResultStatus.InviteStale&&
            !coordinator.TryGetRecipients(newB.Binding,router,out _),
            "reconnected target accepted an invite addressed to old socket");

        coordinator.HandleAsync(new TrustedPartyCommand(
            101,10,3,2,PartyCommandAction.Invite,id,102),router)
            .GetAwaiter().GetResult();
        Checks.Require(DrainResult(a).Status==PartyResultStatus.Success&&
            DrainInvite(newB).PartyId==id,
            "new-generation invite did not deliver");
        // The previous socket can finish teardown AFTER a replacement invite
        // was queued. Its stale Disconnect may not erase the new invitation.
        coordinator.Disconnect(oldB.Binding,router);
        coordinator.HandleAsync(new TrustedPartyCommand(
            102,21,5,2,PartyCommandAction.Accept,id,101),router)
            .GetAwaiter().GetResult();
        Checks.Require(DrainResult(newB).Status==PartyResultStatus.Success&&
            DrainSnapshot(a).Members.Count==2&&
            DrainSnapshot(newB).Members.Count==2&&
            coordinator.TryGetRecipients(newB.Binding,router,out var peers)&&
            peers.Length==2,
            "old socket cleanup erased new-generation pending invitation");
        coordinator.Disconnect(newB.Binding,router);
        DrainSnapshot(a);
        coordinator.Disconnect(a.Binding,router);
        Checks.Require(coordinator.PartyCount==0,
            "generation test failed to dissolve its party");
        router.TryDetach(a);
        router.TryDetach(newB);
    }

    private static void InviteTimeout()
    {
        long now=Stopwatch.GetTimestamp();
        var coordinator=new PartyCoordinator(()=>now);
        var router=new WorldResultRouter();
        var leader=Gateway(Actor(201,30,3),"ClockLeader");
        var target=Gateway(Actor(202,31,4),"ClockTarget");
        Checks.Require(router.TryAttach(leader)&&router.TryAttach(target),
            "invite timeout gateways attach");

        coordinator.HandleAsync(new TrustedPartyCommand(
            201,30,3,1,PartyCommandAction.Invite,0,202),router)
            .GetAwaiter().GetResult();
        long id=DrainResult(leader).PartyId;
        Checks.Require(DrainInvite(target).PartyId==id&&
            DrainSnapshot(leader).Members.Count==1,
            "clock invite missing");
        now=checked(now+Stopwatch.Frequency*
            (PartyCoordinator.InvitationLifetimeSeconds+1L));

        coordinator.HandleAsync(new TrustedPartyCommand(
            202,31,4,1,PartyCommandAction.Accept,id,201),router)
            .GetAwaiter().GetResult();
        Checks.Require(DrainResult(target).Status==PartyResultStatus.InviteStale&&
            !coordinator.TryGetRecipients(target.Binding,router,out _),
            "expired invite accepted after lifetime");
        coordinator.HandleAsync(new TrustedPartyCommand(
            201,30,3,2,PartyCommandAction.Invite,id,202),router)
            .GetAwaiter().GetResult();
        Checks.Require(DrainResult(leader).Status==PartyResultStatus.Success&&
            DrainInvite(target).PartyId==id,
            "expired invite blocked a new invitation");

        coordinator.HandleAsync(new TrustedPartyCommand(
            202,31,4,2,PartyCommandAction.Reject,id,201),router)
            .GetAwaiter().GetResult();
        Checks.Require(DrainResult(target).Status==PartyResultStatus.Success,
            "fresh invite reject failed after stale expiry");
        coordinator.Disconnect(leader.Binding,router);
        Checks.Require(coordinator.PartyCount==0,
            "expiry test left abandoned singleton party");
        router.TryDetach(leader);
        router.TryDetach(target);
    }


    private static void InviteEnqueueFailure()
    {
        // Saturate the target's bounded reliable FIFO: the invite must not
        // create a phantom party or send Success to its inviter.
        var router=new WorldResultRouter();
        var a=Gateway(Actor(701,1,1),"QueueLeader");
        var b=Gateway(Actor(702,2,1),"QueueTarget",1);
        Checks.Require(router.TryAttach(a)&&router.TryAttach(b),
            "party target-backpressure fixture attach failed");
        FillReliable(b,"QueueTarget");
        var party=new PartyCoordinator();
        bool targetFailed=false;
        try
        {
            party.HandleAsync(new TrustedPartyCommand(
                701,1,1,1,PartyCommandAction.Invite,0,702),router)
                .GetAwaiter().GetResult();
        }
        catch(IOException){targetFailed=true;}
        Checks.Require(targetFailed&&party.PartyCount==0&&
            !a.TryDequeueReliable(out _),
            "failed target delivery acknowledged or retained ghost party");
        _=DrainSnapshot(b);
        party.HandleAsync(new TrustedPartyCommand(
            701,1,1,2,PartyCommandAction.Invite,0,702),router)
            .GetAwaiter().GetResult();
        var success=DrainResult(a);
        Checks.Require(success.Status==PartyResultStatus.Success&&
            success.PartyId>0&&DrainInvite(b).PartyId==success.PartyId&&
            DrainSnapshot(a).Members.Count==1,
            "re-invite after target backpressure failed");
        router.TryDetach(a);
        router.TryDetach(b);

        // A sender queue can fail *after* the recipient accepted a reliable
        // invite frame. The stale frame may arrive but can never join a party.
        var secondRouter=new WorldResultRouter();
        var sender=Gateway(Actor(711,3,1),"FullSender",1);
        var target=Gateway(Actor(712,4,1),"WaitingTarget");
        Checks.Require(secondRouter.TryAttach(sender)&&
            secondRouter.TryAttach(target),
            "party sender-backpressure fixture attach failed");
        FillReliable(sender,"FullSender");
        var secondParty=new PartyCoordinator();
        bool senderFailed=false;
        try
        {
            secondParty.HandleAsync(new TrustedPartyCommand(
                711,3,1,1,PartyCommandAction.Invite,0,712),secondRouter)
                .GetAwaiter().GetResult();
        }
        catch(IOException){senderFailed=true;}
        Checks.Require(senderFailed&&secondParty.PartyCount==0,
            "failed sender receipt retained ghost party");
        _=DrainSnapshot(sender);
        var leakedInvite=DrainInvite(target);
        secondParty.HandleAsync(new TrustedPartyCommand(
            712,4,1,1,PartyCommandAction.Accept,
            leakedInvite.PartyId,711),secondRouter)
            .GetAwaiter().GetResult();
        Checks.Require(DrainResult(target).Status==PartyResultStatus.InviteMissing&&
            !secondParty.TryGetRecipients(target.Binding,secondRouter,out _),
            "recipient joined after sender receipt failed");
        secondRouter.TryDetach(sender);
        secondRouter.TryDetach(target);
    }

    private static void AcceptResultEnqueueFailure()
    {
        var router=new WorldResultRouter();
        var leader=Gateway(Actor(801,11,1),"AcceptLeader");
        var target=Gateway(Actor(802,12,1),"AcceptTarget",2);
        Checks.Require(router.TryAttach(leader)&&router.TryAttach(target),
            "accept-backpressure fixture attach failed");
        var party=new PartyCoordinator();
        party.HandleAsync(new TrustedPartyCommand(
            801,11,1,1,PartyCommandAction.Invite,0,802),router)
            .GetAwaiter().GetResult();
        long partyId=DrainResult(leader).PartyId;
        _=DrainSnapshot(leader);
        Checks.Require(DrainInvite(target).PartyId==partyId,
            "accept-backpressure initial invitation missing");

        FillReliable(target,"AcceptTarget");
        FillReliable(target,"AcceptTarget");
        bool blocked=false;
        try
        {
            party.HandleAsync(new TrustedPartyCommand(
                802,12,1,1,PartyCommandAction.Accept,partyId,801),router)
                .GetAwaiter().GetResult();
        }
        catch(IOException){blocked=true;}
        Checks.Require(blocked&&party.PartyCount==1&&
            party.TryGetRecipients(leader.Binding,router,out var leaderOnly)&&
            leaderOnly.Length==1&&leaderOnly[0].ActorId==801&&
            !party.TryGetRecipients(target.Binding,router,out _),
            "failed accept receipt changed authoritative membership");

        _=DrainSnapshot(target);
        _=DrainSnapshot(target);
        party.HandleAsync(new TrustedPartyCommand(
            802,12,1,2,PartyCommandAction.Accept,partyId,801),router)
            .GetAwaiter().GetResult();
        Checks.Require(DrainResult(target).Status==PartyResultStatus.Success&&
            DrainSnapshot(target).Members.Count==2&&
            DrainSnapshot(leader).Members.Count==2&&
            party.TryGetRecipients(target.Binding,router,out var joined)&&
            joined.Length==2,
            "accept retry could not consume preserved invitation");
        router.TryDetach(leader);
        router.TryDetach(target);
    }

    private static void PromoteResultEnqueueFailure()
    {
        var router=new WorldResultRouter();
        var leader=Gateway(Actor(811,21,1),"PromoteLeader",2);
        var member=Gateway(Actor(812,22,1),"PromoteMember");
        var pending=Gateway(Actor(813,23,1),"PromotePending");
        Checks.Require(router.TryAttach(leader)&&router.TryAttach(member)&&
            router.TryAttach(pending),
            "promote-backpressure fixture attach failed");
        var party=new PartyCoordinator();
        party.HandleAsync(new TrustedPartyCommand(
            811,21,1,1,PartyCommandAction.Invite,0,812),router)
            .GetAwaiter().GetResult();
        long partyId=DrainResult(leader).PartyId;
        _=DrainSnapshot(leader);
        _=DrainInvite(member);
        party.HandleAsync(new TrustedPartyCommand(
            812,22,1,1,PartyCommandAction.Accept,partyId,811),router)
            .GetAwaiter().GetResult();
        _=DrainResult(member);
        _=DrainSnapshot(member);
        _=DrainSnapshot(leader);

        party.HandleAsync(new TrustedPartyCommand(
            811,21,1,2,PartyCommandAction.Invite,partyId,813),router)
            .GetAwaiter().GetResult();
        Checks.Require(DrainResult(leader).Status==PartyResultStatus.Success&&
            DrainInvite(pending).PartyId==partyId,
            "promote-backpressure pending invitation missing");
        FillReliable(leader,"PromoteLeader");
        FillReliable(leader,"PromoteLeader");
        bool blocked=false;
        try
        {
            party.HandleAsync(new TrustedPartyCommand(
                811,21,1,3,PartyCommandAction.Promote,partyId,812),router)
                .GetAwaiter().GetResult();
        }
        catch(IOException){blocked=true;}
        Checks.Require(blocked,"full leader queue did not reject promote receipt");
        _=DrainSnapshot(leader);
        _=DrainSnapshot(leader);

        // A failed promotion must leave the original leader able to accept
        // the pending member. In particular, it must not invalidate the invite.
        party.HandleAsync(new TrustedPartyCommand(
            813,23,1,1,PartyCommandAction.Accept,partyId,811),router)
            .GetAwaiter().GetResult();
        Checks.Require(DrainResult(pending).Status==PartyResultStatus.Success&&
            DrainSnapshot(pending).Members.Count==3&&
            DrainSnapshot(leader).LeaderActorId==811&&
            DrainSnapshot(member).LeaderActorId==811,
            "failed promotion changed leader or invalidated pending invite");

        party.HandleAsync(new TrustedPartyCommand(
            811,21,1,4,PartyCommandAction.Promote,partyId,812),router)
            .GetAwaiter().GetResult();
        Checks.Require(DrainResult(leader).Status==PartyResultStatus.Success&&
            DrainSnapshot(leader).LeaderActorId==812&&
            DrainSnapshot(member).LeaderActorId==812&&
            DrainSnapshot(pending).LeaderActorId==812,
            "promotion retry failed after queue pressure was relieved");
        router.TryDetach(leader);
        router.TryDetach(member);
        router.TryDetach(pending);
    }

    private static void RejectResultEnqueueFailure()
    {
        var router=new WorldResultRouter();
        var leader=Gateway(Actor(821,31,1),"RejectLeader");
        var target=Gateway(Actor(822,32,1),"RejectTarget",2);
        Checks.Require(router.TryAttach(leader)&&router.TryAttach(target),
            "reject-backpressure fixture attach failed");
        var party=new PartyCoordinator();
        party.HandleAsync(new TrustedPartyCommand(
            821,31,1,1,PartyCommandAction.Invite,0,822),router)
            .GetAwaiter().GetResult();
        long partyId=DrainResult(leader).PartyId;
        _=DrainSnapshot(leader);
        Checks.Require(DrainInvite(target).PartyId==partyId,
            "reject-backpressure invitation missing");

        FillReliable(target,"RejectTarget");
        FillReliable(target,"RejectTarget");
        bool blocked=false;
        try
        {
            party.HandleAsync(new TrustedPartyCommand(
                822,32,1,1,PartyCommandAction.Reject,partyId,821),router)
                .GetAwaiter().GetResult();
        }
        catch(IOException){blocked=true;}
        Checks.Require(blocked&&party.PartyCount==1&&
            !party.TryGetRecipients(target.Binding,router,out _),
            "failed reject receipt mutated membership");
        _=DrainSnapshot(target);
        _=DrainSnapshot(target);

        // Nothing was acknowledged, so the target can still accept the
        // original invitation rather than receiving a phantom InviteMissing.
        party.HandleAsync(new TrustedPartyCommand(
            822,32,1,2,PartyCommandAction.Accept,partyId,821),router)
            .GetAwaiter().GetResult();
        Checks.Require(DrainResult(target).Status==PartyResultStatus.Success&&
            DrainSnapshot(target).Members.Count==2&&
            DrainSnapshot(leader).Members.Count==2,
            "failed reject receipt erased the retryable invitation");
        router.TryDetach(leader);
        router.TryDetach(target);
    }

    private static void FillReliable(GatewayConnection gateway,string name)
    {
        var binding=gateway.Binding;
        var snapshot=new PartySnapshotWire(
            binding.MapEpoch,999,1,binding.ActorId,
            [new PartyMemberWire(
                binding.ActorId,binding.ConnectionEpoch,name)]);
        Checks.Require(gateway.QueuePartySnapshot(
                binding,snapshot)==GatewaySendStatus.Enqueued,
            "party reliable-queue backpressure fixture failed");
    }

    private static GatewayConnection Gateway(ActorSnapshot actor,string name,int maxReliableFrames=256)
    {
        var pvp=new CharacterPvpState(
            actor.ActorId,PvpMode.ProtectedBlue,false,0,0,0);
        return new GatewayConnection(actor,
            new GatewayConnectionOptions(MaxReliableFrames:maxReliableFrames),
            displayName:name,pvpState:pvp);
    }

    private static ActorSnapshot Actor(long id,long connection,long mapEpoch)=>
        new(id,15,connection,mapEpoch,0,0,8,8,1,false,1,false);

    private static PartyResultWire DrainResult(GatewayConnection gateway)
    {
        if(!gateway.TryDequeueReliable(out var frame)||
           !WireProtocol.TryDecodePartyResult(frame,out var value))
            throw new Exception("party result missing");
        return value;
    }

    private static PartyInviteWire DrainInvite(GatewayConnection gateway)
    {
        if(!gateway.TryDequeueReliable(out var frame)||
           !WireProtocol.TryDecodePartyInvite(frame,out var value))
            throw new Exception("party invite missing");
        return value;
    }

    private static PartySnapshotWire DrainSnapshot(GatewayConnection gateway)
    {
        if(!gateway.TryDequeueReliable(out var frame)||
           !WireProtocol.TryDecodePartySnapshot(frame,out var value)||
           value is null)
            throw new Exception("party snapshot missing");
        return value;
    }
}
