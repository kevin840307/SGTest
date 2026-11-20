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
    }

    private static void Wire()
    {
        var command=new PartyCommandWire(
            3,7,PartyCommandAction.Invite,0,22);
        var bytes=WireProtocol.EncodePartyCommand(command);
        Checks.Require(bytes.Length==WireProtocol.PartyCommandFrameBytes&&
            WireProtocol.TryDecodePartyCommand(bytes,out var back)&&back==command,
            "party command roundtrip mismatch");

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

    private static GatewayConnection Gateway(ActorSnapshot actor,string name)
    {
        var pvp=new CharacterPvpState(
            actor.ActorId,PvpMode.ProtectedBlue,false,0,0,0);
        return new GatewayConnection(actor,displayName:name,pvpState:pvp);
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
