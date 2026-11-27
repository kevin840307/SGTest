using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class ClanChecks
{
    public static void Verify()
    {
        Checks.Run("clan wire roundtrips management commands results and announcement snapshots",Wire);
        Checks.Run("durable clan announcement permissions kick transfer and dissolve",()=>Persistence().GetAwaiter().GetResult());
    }

    private static void Wire()
    {
        var create=new ClanCommandWire(7,1,ClanCommandAction.Create,0,"星門先遣隊");
        var encoded=WireProtocol.EncodeClanCommand(create);
        Checks.Require(WireProtocol.TryDecodeClanCommand(encoded,out var parsed)&&parsed==create,
            "clan create wire roundtrip failed");

        var announcement=new ClanCommandWire(
            7,2,ClanCommandAction.SetAnnouncement,0,"今晚九點集合",0);
        Checks.Require(WireProtocol.TryDecodeClanCommand(
            WireProtocol.EncodeClanCommand(announcement),out parsed)&&parsed==announcement,
            "clan announcement wire roundtrip failed");

        var kick=new ClanCommandWire(7,3,ClanCommandAction.Kick,0,string.Empty,1002);
        Checks.Require(WireProtocol.TryDecodeClanCommand(
            WireProtocol.EncodeClanCommand(kick),out parsed)&&parsed==kick,
            "clan kick wire roundtrip failed");

        var transfer=new ClanCommandWire(
            7,4,ClanCommandAction.TransferLeadership,0,string.Empty,1002);
        Checks.Require(WireProtocol.TryDecodeClanCommand(
            WireProtocol.EncodeClanCommand(transfer),out parsed)&&parsed==transfer,
            "clan leadership transfer wire roundtrip failed");

        var result=new ClanResultWire(
            7,4,ClanCommandAction.TransferLeadership,ClanResultStatus.Success,44);
        Checks.Require(WireProtocol.TryDecodeClanResult(
            WireProtocol.EncodeClanResult(result),out var decodedResult)&&decodedResult==result,
            "clan result roundtrip failed");

        var snapshot=new ClanSnapshotWire(7,44,3,1001,"星門先遣隊",[
            new ClanMemberWire(1001,1,true,"團長"),
            new ClanMemberWire(1002,0,false,"成員")
        ],"今晚九點集合");
        Checks.Require(WireProtocol.TryDecodeClanSnapshot(
            WireProtocol.EncodeClanSnapshot(snapshot),out var decoded)&&decoded is not null&&
            decoded.ClanId==44&&decoded.Name=="星門先遣隊"&&
            decoded.Announcement=="今晚九點集合"&&decoded.Members.Count==2&&
            decoded.Members.Single(x=>x.CharacterId==1001) is { Role:1,IsOnline:true }&&
            decoded.Members.Single(x=>x.CharacterId==1002).IsOnline==false,
            "clan snapshot roundtrip failed");

        var invite=new ClanInviteWire(7,44,3,1001,9001,"團長","星門先遣隊");
        Checks.Require(WireProtocol.TryDecodeClanInvite(
            WireProtocol.EncodeClanInvite(invite),out var decodedInvite)&&decodedInvite==invite,
            "clan invite wire roundtrip failed");

        var accept=new ClanCommandWire(7,5,ClanCommandAction.Join,44,string.Empty,1001);
        Checks.Require(WireProtocol.TryDecodeClanCommand(
            WireProtocol.EncodeClanCommand(accept),out parsed)&&parsed==accept,
            "clan invite accept command roundtrip failed");

        var malformed=WireProtocol.EncodeClanCommand(create);
        malformed[20]=9;
        Checks.Require(!WireProtocol.TryDecodeClanCommand(malformed,out _),
            "clan wire accepted invalid action");
        var clear=new ClanSnapshotWire(7,0,0,0,string.Empty,[]);
        Checks.Require(WireProtocol.TryDecodeClanSnapshot(
            WireProtocol.EncodeClanSnapshot(clear),out var empty)&&empty is not null&&
            empty.ClanId==0&&empty.Announcement==string.Empty,
            "empty clan snapshot did not roundtrip");
    }

    private static async Task Persistence()
    {
        string root=Path.Combine(Path.GetTempPath(),"sgtest_clan_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            long leaderId,memberId,thirdId,clanId;
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "clan_a","Clan-Account-Secret-2026!")).AccountId;
                long other=(await store.CreateAccountAsync(
                    "clan_b","Clan-Other-Secret-2026!")).AccountId;
                leaderId=(await store.CreateCharacterAsync(
                    account,0,"ClanLeader",new(15,100,100))).Character!.CharacterId;
                memberId=(await store.CreateCharacterAsync(
                    account,1,"ClanMember",new(15,110,100))).Character!.CharacterId;
                thirdId=(await store.CreateCharacterAsync(
                    other,0,"ClanThird",new(15,120,100))).Character!.CharacterId;

                var created=await store.CreateClanAsync(leaderId,"星門先遣隊");
                Checks.Require(created.Status==ClanStoreStatus.Success&&created.Clan is not null&&
                    created.Clan.LeaderCharacterId==leaderId&&created.Clan.Members.Count==1,
                    "clan create did not persist leader");
                clanId=created.Clan!.ClanId;
                Checks.Require((await store.CreateClanAsync(
                        memberId,"星門先遣隊")).Status==ClanStoreStatus.NameTaken,
                    "duplicate normalized clan name accepted");

                var joined=await store.JoinClanAsync(memberId,clanId);
                var third=await store.JoinClanAsync(thirdId,clanId);
                Checks.Require(joined.Status==ClanStoreStatus.Success&&
                    third.Status==ClanStoreStatus.Success&&third.Clan!.Members.Count==3,
                    "clan members did not join");

                Checks.Require((await store.SetClanAnnouncementAsync(
                        memberId,"越權公告")).Status==ClanStoreStatus.NotLeader,
                    "non-leader changed clan announcement");
                Checks.Require((await store.KickClanMemberAsync(
                        memberId,thirdId)).Status==ClanStoreStatus.NotLeader,
                    "non-leader kicked clan member");
                Checks.Require((await store.TransferClanLeadershipAsync(
                        memberId,thirdId)).Status==ClanStoreStatus.NotLeader,
                    "non-leader transferred clan leadership");

                var announced=await store.SetClanAnnouncementAsync(
                    leaderId,"今晚九點集合");
                Checks.Require(announced.Status==ClanStoreStatus.Success&&
                    announced.Clan!.Announcement=="今晚九點集合",
                    "leader announcement did not persist");

                var transferred=await store.TransferClanLeadershipAsync(leaderId,memberId);
                Checks.Require(transferred.Status==ClanStoreStatus.Success&&
                    transferred.Clan!.LeaderCharacterId==memberId&&
                    transferred.Clan.Members.Single(x=>x.CharacterId==memberId).Role==
                        ClanMemberRole.Leader&&
                    transferred.Clan.Members.Single(x=>x.CharacterId==leaderId).Role==
                        ClanMemberRole.Member,
                    "explicit clan leadership transfer failed");
                Checks.Require((await store.SetClanAnnouncementAsync(
                        leaderId,"舊團長越權")).Status==ClanStoreStatus.NotLeader,
                    "former leader retained announcement authority");

                var kicked=await store.KickClanMemberAsync(memberId,thirdId);
                Checks.Require(kicked.Status==ClanStoreStatus.Success&&
                    kicked.Clan!.Members.Count==2&&
                    !kicked.Clan.Members.Any(x=>x.CharacterId==thirdId)&&
                    await store.GetClanForCharacterAsync(thirdId) is null,
                    "leader kick did not atomically remove member");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var persisted=await reopened.GetClanForCharacterAsync(memberId);
                Checks.Require(persisted is not null&&persisted.ClanId==clanId&&
                    persisted.LeaderCharacterId==memberId&&
                    persisted.Announcement=="今晚九點集合"&&
                    persisted.Members.Count==2&&
                    persisted.Members.Any(x=>x.CharacterId==leaderId),
                    "clan management state did not survive store reopen");

                var oldLeaderLeave=await reopened.LeaveClanAsync(leaderId);
                Checks.Require(oldLeaderLeave.Status==ClanStoreStatus.Success&&
                    oldLeaderLeave.Clan!.LeaderCharacterId==memberId,
                    "ordinary member could not leave persisted clan");
                var lastLeave=await reopened.LeaveClanAsync(memberId);
                Checks.Require(lastLeave.Status==ClanStoreStatus.Success&&lastLeave.Clan is null&&
                    await reopened.GetClanForCharacterAsync(memberId) is null,
                    "last leader leave did not dissolve clan");
                Checks.Require((await reopened.JoinClanAsync(
                        leaderId,clanId)).Status==ClanStoreStatus.ClanNotFound,
                    "dissolved clan remained joinable");
            }
        }
        finally { Directory.Delete(root,true); }
    }
}
