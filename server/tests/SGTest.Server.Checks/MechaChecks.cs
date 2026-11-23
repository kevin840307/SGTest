using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class MechaChecks
{
    public static void Verify()
    {
        Checks.Run("mecha wire roundtrips authoritative mount roster",Wire);
        Checks.Run("mecha mount dismount replay ownership and persistence are authoritative",
            ()=>Persistence().GetAwaiter().GetResult());
    }

    private static void Wire()
    {
        var mount=new MechaCommandWire(7,1,MechaCommandAction.Mount,101);
        Checks.Require(WireProtocol.TryDecodeMechaCommand(
            WireProtocol.EncodeMechaCommand(mount),out var decoded)&&decoded==mount,
            "mecha mount wire roundtrip failed");

        var refresh=new MechaCommandWire(7,2,MechaCommandAction.Refresh,0);
        Checks.Require(WireProtocol.TryDecodeMechaCommand(
            WireProtocol.EncodeMechaCommand(refresh),out decoded)&&decoded==refresh,
            "mecha refresh wire roundtrip failed");

        var result=new MechaResultWire(
            7,1,MechaCommandAction.Mount,MechaResultStatus.Applied,101,101);
        Checks.Require(WireProtocol.TryDecodeMechaResult(
            WireProtocol.EncodeMechaResult(result),out var decodedResult)&&
            decodedResult==result,"mecha result wire roundtrip failed");

        var snapshot=new MechaSnapshotWire(7,102,[
            new MechaSnapshotEntryWire(101,7001,4,88),
            new MechaSnapshotEntryWire(102,7002,7,1234)
        ]);
        Checks.Require(WireProtocol.TryDecodeMechaSnapshot(
            WireProtocol.EncodeMechaSnapshot(snapshot),out var decodedSnapshot)&&
            decodedSnapshot is not null&&decodedSnapshot.MountedMechaUid==102&&
            decodedSnapshot.Mechas.Count==2&&
            decodedSnapshot.Mechas[1].DefinitionId==7002&&
            decodedSnapshot.Mechas[1].Level==7&&
            decodedSnapshot.Mechas[1].Experience==1234,
            "mecha snapshot wire roundtrip failed");

        var presentation=new MechaPresentationWire(
            7,99,44,8,7,true,102,7002,7);
        Checks.Require(WireProtocol.TryDecodeMechaPresentation(
            WireProtocol.EncodeMechaPresentation(presentation),out var decodedPresentation)&&
            decodedPresentation==presentation,
            "mounted mecha presentation wire roundtrip failed");
        var cleared=presentation with {Mounted=false,MechaUid=0,DefinitionId=0,Level=0};
        Checks.Require(WireProtocol.TryDecodeMechaPresentation(
            WireProtocol.EncodeMechaPresentation(cleared),out decodedPresentation)&&
            decodedPresentation==cleared,
            "dismounted mecha presentation wire roundtrip failed");

        var malformed=WireProtocol.EncodeMechaCommand(mount);
        malformed[20]=9;
        Checks.Require(!WireProtocol.TryDecodeMechaCommand(malformed,out _),
            "mecha command accepted invalid action");

        bool rejected=false;
        try
        {
            WireProtocol.EncodeMechaSnapshot(new(7,999,[
                new MechaSnapshotEntryWire(101,7001,1,0)
            ]));
        }
        catch(ArgumentOutOfRangeException){rejected=true;}
        Checks.Require(rejected,"mecha snapshot accepted mounted uid outside roster");
    }

    private static async Task Persistence()
    {
        string root=Path.Combine(
            Path.GetTempPath(),"sgtest_mecha_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            long character,mechaA,mechaB;
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "mecha_a","Mecha-Account-Secret-2026!")).AccountId;
                long other=(await store.CreateAccountAsync(
                    "mecha_b","Mecha-Other-Secret-2026!")).AccountId;
                character=(await store.CreateCharacterAsync(
                    account,0,"MechaOwner",new(15,100,100))).Character!.CharacterId;
                long otherCharacter=(await store.CreateCharacterAsync(
                    other,0,"MechaOther",new(15,120,100))).Character!.CharacterId;
                mechaA=await store.ProvisionMechaAsync(
                    character,7001,4,88,new byte[]{1,2,3});
                mechaB=await store.ProvisionMechaAsync(character,7002,7,1234);
                long foreign=await store.ProvisionMechaAsync(otherCharacter,8001);

                var mount=await store.ApplyMechaActionAsync(
                    character,11,4,1,MechaActionKind.Mount,mechaA);
                Checks.Require(
                    mount.Status==MechaActionStatus.Applied&&
                    mount.MountedMechaUid==mechaA,
                    "first mount did not activate owned mecha");

                var replay=await store.ApplyMechaActionAsync(
                    character,11,4,1,MechaActionKind.Mount,mechaA);
                Checks.Require(
                    replay.Status==MechaActionStatus.AlreadyProcessed&&
                    replay.MountedMechaUid==mechaA,
                    "exact mecha retransmit was not idempotent");

                var conflict=await store.ApplyMechaActionAsync(
                    character,11,4,1,MechaActionKind.Mount,mechaB);
                Checks.Require(
                    conflict.Status==MechaActionStatus.SequenceConflict&&
                    conflict.MountedMechaUid==mechaA,
                    "same mecha sequence with different payload was not rejected");

                var second=await store.ApplyMechaActionAsync(
                    character,11,4,2,MechaActionKind.Mount,mechaB);
                Checks.Require(
                    second.Status==MechaActionStatus.Applied&&
                    second.MountedMechaUid==mechaB,
                    "second mount did not atomically replace mounted mecha");

                var foreignResult=await store.ApplyMechaActionAsync(
                    character,11,4,3,MechaActionKind.Mount,foreign);
                Checks.Require(
                    foreignResult.Status==MechaActionStatus.MechaNotOwned&&
                    foreignResult.MountedMechaUid==mechaB,
                    "foreign mecha ownership was not rejected");

                var roster=await store.GetMechasAsync(character);
                Checks.Require(roster.Count==2&&
                    roster.Count(x=>x.IsMounted)==1&&
                    roster.Single(x=>x.IsMounted).MechaUid==mechaB&&
                    roster.Single(x=>x.MechaUid==mechaA).PartsBlob
                        .SequenceEqual(new byte[]{1,2,3}),
                    "mounted uniqueness or server-owned parts were not retained");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var persisted=await reopened.GetMechasAsync(character);
                Checks.Require(persisted.Count==2&&
                    persisted.Count(x=>x.IsMounted)==1&&
                    persisted.Single(x=>x.IsMounted).MechaUid==mechaB&&
                    persisted.Single(x=>x.MechaUid==mechaA).PartsBlob
                        .SequenceEqual(new byte[]{1,2,3}),
                    "mecha roster/mounted state/parts did not survive reopen");

                var dismount=await reopened.ApplyMechaActionAsync(
                    character,12,4,1,MechaActionKind.Dismount,mechaB);
                Checks.Require(
                    dismount.Status==MechaActionStatus.Applied&&
                    dismount.MountedMechaUid==0,
                    "dismount did not clear mounted mecha");
                var after=await reopened.GetMechasAsync(character);
                Checks.Require(after.All(x=>!x.IsMounted),
                    "dismount left a mounted mecha row");
            }
        }
        finally { Directory.Delete(root,true); }
    }
}
