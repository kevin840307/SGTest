using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class PetChecks
{
    public static void Verify()
    {
        Checks.Run("pet wire roundtrips bounded command result and roster snapshot",Wire);
        Checks.Run("pet summon recall replay and persistence are server authoritative",
            ()=>Persistence().GetAwaiter().GetResult());
    }

    private static void Wire()
    {
        var summon=new PetCommandWire(7,1,PetCommandAction.Summon,101);
        Checks.Require(WireProtocol.TryDecodePetCommand(
            WireProtocol.EncodePetCommand(summon),out var decoded)&&decoded==summon,
            "pet summon wire roundtrip failed");

        var refresh=new PetCommandWire(7,2,PetCommandAction.Refresh,0);
        Checks.Require(WireProtocol.TryDecodePetCommand(
            WireProtocol.EncodePetCommand(refresh),out decoded)&&decoded==refresh,
            "pet refresh wire roundtrip failed");

        var result=new PetResultWire(
            7,1,PetCommandAction.Summon,PetResultStatus.Applied,101,101);
        Checks.Require(WireProtocol.TryDecodePetResult(
            WireProtocol.EncodePetResult(result),out var decodedResult)&&decodedResult==result,
            "pet result wire roundtrip failed");

        var snapshot=new PetSnapshotWire(7,102,[
            new PetSnapshotEntryWire(101,5001,4,88),
            new PetSnapshotEntryWire(102,5002,7,1234)
        ]);
        Checks.Require(WireProtocol.TryDecodePetSnapshot(
            WireProtocol.EncodePetSnapshot(snapshot),out var decodedSnapshot)&&
            decodedSnapshot is not null&&decodedSnapshot.ActivePetUid==102&&
            decodedSnapshot.Pets.Count==2&&decodedSnapshot.Pets[1].Experience==1234,
            "pet snapshot wire roundtrip failed");

        var spawn=new PetEntitySpawnWire(7,42,1001,11,4,102,5002,7,24f,36f);
        Checks.Require(WireProtocol.TryDecodePetEntitySpawn(
            WireProtocol.EncodePetEntitySpawn(spawn),out var decodedSpawn)&&
            decodedSpawn==spawn,
            "pet AOI spawn wire roundtrip failed");
        bool invalidPositionRejected=false;
        try
        {
            WireProtocol.EncodePetEntitySpawn(spawn with {X=float.NaN});
        }
        catch(ArgumentOutOfRangeException){invalidPositionRejected=true;}
        Checks.Require(invalidPositionRejected,
            "pet AOI spawn accepted non-finite authoritative position");
        var delta=new PetEntityDeltaWire(7,43,1001,11,4,102,40f,36f);
        Checks.Require(WireProtocol.TryDecodePetEntityDelta(
            WireProtocol.EncodePetEntityDelta(delta),out var decodedDelta)&&
            decodedDelta==delta,
            "pet AOI movement delta wire roundtrip failed");
        bool invalidDeltaRejected=false;
        try
        {
            WireProtocol.EncodePetEntityDelta(delta with {Y=float.PositiveInfinity});
        }
        catch(ArgumentOutOfRangeException){invalidDeltaRejected=true;}
        Checks.Require(invalidDeltaRejected,
            "pet AOI movement delta accepted non-finite authoritative position");
        var despawn=new PetEntityDespawnWire(7,44,1001,11,4,102);
        Checks.Require(WireProtocol.TryDecodePetEntityDespawn(
            WireProtocol.EncodePetEntityDespawn(despawn),out var decodedDespawn)&&
            decodedDespawn==despawn,
            "pet AOI despawn wire roundtrip failed");

        var malformed=WireProtocol.EncodePetCommand(summon);
        malformed[20]=9;
        Checks.Require(!WireProtocol.TryDecodePetCommand(malformed,out _),
            "pet command accepted invalid action");

        bool rejected=false;
        try
        {
            WireProtocol.EncodePetSnapshot(new(7,999,[
                new PetSnapshotEntryWire(101,5001,1,0)
            ]));
        }
        catch(ArgumentOutOfRangeException){rejected=true;}
        Checks.Require(rejected,"pet snapshot accepted active uid outside roster");
    }

    private static async Task Persistence()
    {
        string root=Path.Combine(Path.GetTempPath(),"sgtest_pet_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            long character,otherCharacter,petA,petB,foreignPet;
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "pet_a","Pet-Account-Secret-2026!")).AccountId;
                long other=(await store.CreateAccountAsync(
                    "pet_b","Pet-Other-Secret-2026!")).AccountId;
                character=(await store.CreateCharacterAsync(
                    account,0,"PetOwner",new(15,100,100))).Character!.CharacterId;
                otherCharacter=(await store.CreateCharacterAsync(
                    other,0,"PetOther",new(15,120,100))).Character!.CharacterId;
                petA=await store.ProvisionPetAsync(character,5001,4,88,new byte[]{1,2,3});
                petB=await store.ProvisionPetAsync(character,5002,7,1234);
                foreignPet=await store.ProvisionPetAsync(otherCharacter,6001);

                var summon=await store.ApplyPetActionAsync(
                    character,11,4,1,PetActionKind.Summon,petA);
                Checks.Require(summon.Status==PetActionStatus.Applied&&summon.ActivePetUid==petA,
                    "first summon did not activate owned pet");

                var replay=await store.ApplyPetActionAsync(
                    character,11,4,1,PetActionKind.Summon,petA);
                Checks.Require(replay.Status==PetActionStatus.AlreadyProcessed&&
                    replay.ActivePetUid==petA,
                    "exact retransmit was not idempotent");

                var conflict=await store.ApplyPetActionAsync(
                    character,11,4,1,PetActionKind.Summon,petB);
                Checks.Require(conflict.Status==PetActionStatus.SequenceConflict&&
                    conflict.ActivePetUid==petA,
                    "same sequence with different payload was not rejected");

                var second=await store.ApplyPetActionAsync(
                    character,11,4,2,PetActionKind.Summon,petB);
                Checks.Require(second.Status==PetActionStatus.Applied&&second.ActivePetUid==petB,
                    "second summon did not atomically replace active pet");

                var foreign=await store.ApplyPetActionAsync(
                    character,11,4,3,PetActionKind.Summon,foreignPet);
                Checks.Require(foreign.Status==PetActionStatus.PetNotOwned&&
                    foreign.ActivePetUid==petB,
                    "foreign pet ownership was not rejected");

                var pets=await store.GetPetsAsync(character);
                Checks.Require(pets.Count==2&&pets.Single(x=>x.PetUid==petB).IsActive&&
                    !pets.Single(x=>x.PetUid==petA).IsActive,
                    "active pet uniqueness not reflected in roster");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var persisted=await reopened.GetPetsAsync(character);
                Checks.Require(persisted.Count==2&&
                    persisted.Count(x=>x.IsActive)==1&&
                    persisted.Single(x=>x.IsActive).PetUid==petB&&
                    persisted.Single(x=>x.PetUid==petA).StateBlob.SequenceEqual(new byte[]{1,2,3}),
                    "pet roster/active state/blob did not survive reopen");

                var recall=await reopened.ApplyPetActionAsync(
                    character,12,4,1,PetActionKind.Recall,petB);
                Checks.Require(recall.Status==PetActionStatus.Applied&&recall.ActivePetUid==0,
                    "recall did not clear active pet");
                var recalled=await reopened.GetPetsAsync(character);
                Checks.Require(recalled.All(x=>!x.IsActive),
                    "recall left an active pet row");
            }
        }
        finally { Directory.Delete(root,true); }
    }
}
