using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class ClanChecks
{
    public static void Verify()
    {
        Checks.Run("clan wire roundtrips bounded commands results and snapshots",Wire);
        Checks.Run("durable clan create join leader transfer and dissolve",()=>Persistence().GetAwaiter().GetResult());
    }

    private static void Wire()
    {
        var create=new ClanCommandWire(7,1,ClanCommandAction.Create,0,"星門先遣隊");
        var encoded=WireProtocol.EncodeClanCommand(create);
        Checks.Require(WireProtocol.TryDecodeClanCommand(encoded,out var parsed)&&parsed==create,
            "clan create wire roundtrip failed");
        var join=new ClanCommandWire(7,2,ClanCommandAction.Join,44,string.Empty);
        Checks.Require(WireProtocol.TryDecodeClanCommand(
            WireProtocol.EncodeClanCommand(join),out parsed)&&parsed==join,
            "clan join wire roundtrip failed");
        var result=new ClanResultWire(7,2,ClanCommandAction.Join,ClanResultStatus.Success,44);
        Checks.Require(WireProtocol.TryDecodeClanResult(
            WireProtocol.EncodeClanResult(result),out var decodedResult)&&decodedResult==result,
            "clan result roundtrip failed");
        var snapshot=new ClanSnapshotWire(7,44,3,1001,"星門先遣隊",[
            new ClanMemberWire(1001,1,"團長"),
            new ClanMemberWire(1002,0,"成員")
        ]);
        Checks.Require(WireProtocol.TryDecodeClanSnapshot(
            WireProtocol.EncodeClanSnapshot(snapshot),out var decoded)&&decoded is not null&&
            decoded.ClanId==44&&decoded.Name=="星門先遣隊"&&decoded.Members.Count==2&&
            decoded.Members.Single(x=>x.CharacterId==1001).Role==1,
            "clan snapshot roundtrip failed");
        var malformed=WireProtocol.EncodeClanCommand(create);
        malformed[20]=9;
        Checks.Require(!WireProtocol.TryDecodeClanCommand(malformed,out _),
            "clan wire accepted invalid action");
        var clear=new ClanSnapshotWire(7,0,0,0,string.Empty,[]);
        Checks.Require(WireProtocol.TryDecodeClanSnapshot(
            WireProtocol.EncodeClanSnapshot(clear),out var empty)&&empty is not null&&empty.ClanId==0,
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
                long account=(await store.CreateAccountAsync("clan_a","Clan-Account-Secret-2026!")).AccountId;
                long other=(await store.CreateAccountAsync("clan_b","Clan-Other-Secret-2026!")).AccountId;
                leaderId=(await store.CreateCharacterAsync(account,0,"ClanLeader",new(15,100,100))).Character!.CharacterId;
                memberId=(await store.CreateCharacterAsync(account,1,"ClanMember",new(15,110,100))).Character!.CharacterId;
                thirdId=(await store.CreateCharacterAsync(other,0,"ClanThird",new(15,120,100))).Character!.CharacterId;

                var created=await store.CreateClanAsync(leaderId,"星門先遣隊");
                Checks.Require(created.Status==ClanStoreStatus.Success&&created.Clan is not null&&
                    created.Clan.LeaderCharacterId==leaderId&&created.Clan.Members.Count==1,
                    "clan create did not persist leader");
                clanId=created.Clan!.ClanId;
                Checks.Require((await store.CreateClanAsync(memberId,"星門先遣隊")).Status==ClanStoreStatus.NameTaken,
                    "duplicate normalized clan name accepted");
                var joined=await store.JoinClanAsync(memberId,clanId);
                Checks.Require(joined.Status==ClanStoreStatus.Success&&joined.Clan!.Members.Count==2,
                    "second character did not join clan");
                Checks.Require((await store.CreateClanAsync(memberId,"Other Clan")).Status==ClanStoreStatus.AlreadyInClan,
                    "member created second clan");
                var third=await store.JoinClanAsync(thirdId,clanId);
                Checks.Require(third.Status==ClanStoreStatus.Success&&third.Clan!.Members.Count==3,
                    "cross-account character could not join clan");

                var leave=await store.LeaveClanAsync(leaderId);
                Checks.Require(leave.Status==ClanStoreStatus.Success&&leave.Clan is not null&&
                    leave.Clan.LeaderCharacterId!=leaderId&&leave.Clan.Members.Count==2,
                    "leader leave did not transfer leadership");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var persisted=await reopened.GetClanForCharacterAsync(memberId);
                Checks.Require(persisted is not null&&persisted.ClanId==clanId&&
                    persisted.Members.Count==2&&persisted.Members.Any(x=>x.CharacterId==thirdId),
                    "clan membership did not survive store reopen");
                var memberLeave=await reopened.LeaveClanAsync(memberId);
                Checks.Require(memberLeave.Status==ClanStoreStatus.Success,
                    "member could not leave persisted clan");
                var lastLeave=await reopened.LeaveClanAsync(thirdId);
                Checks.Require(lastLeave.Status==ClanStoreStatus.Success&&lastLeave.Clan is null&&
                    await reopened.GetClanForCharacterAsync(thirdId) is null,
                    "last leader leave did not dissolve clan");
                Checks.Require((await reopened.JoinClanAsync(leaderId,clanId)).Status==ClanStoreStatus.ClanNotFound,
                    "dissolved clan remained joinable");
            }
        }
        finally { Directory.Delete(root,true); }
    }
}
