using Microsoft.Data.Sqlite;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Map;
using SGTest.Server.Monster;
using SGTest.Server.Pet;
using SGTest.Server.Wire;
using SGTest.Server.World;
using SGTest.Server.Visibility;

internal static class PetChecks
{
    public static void Verify(string root)
    {
        Checks.Run("pet wire roundtrips command roster AOI and server-owned combat",Wire);
        Checks.Run("pet growth manifest requires audited cumulative total-EXP thresholds",
            ()=>GrowthManifest(root));
        Checks.Run("pet combat manifest requires audited exact-level rules",
            ()=>CombatManifest(root));
        Checks.Run("pet attack authority owns active identity position range cooldown replay and damage",
            ()=>CombatAuthority(root));
        Checks.Run("pet summon recall replay and persistence are server authoritative",
            ()=>Persistence().GetAwaiter().GetResult());
        Checks.Run("pet EXP grants cross levels atomically and replay exactly once",
            ()=>GrowthPersistence().GetAwaiter().GetResult());
        Checks.Run("schema v30 pet rows migrate to explicit evolution zero without data loss",
            ()=>MigrationV30().GetAwaiter().GetResult());
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
            new PetSnapshotEntryWire(101,5001,2,4,88),
            new PetSnapshotEntryWire(102,5002,7,7,1234)
        ]);
        Checks.Require(WireProtocol.TryDecodePetSnapshot(
            WireProtocol.EncodePetSnapshot(snapshot),out var decodedSnapshot)&&
            decodedSnapshot is not null&&decodedSnapshot.ActivePetUid==102&&
            decodedSnapshot.Pets.Count==2&&decodedSnapshot.Pets[0].EvolutionId==2&&
            decodedSnapshot.Pets[1].EvolutionId==7&&decodedSnapshot.Pets[1].Experience==1234,
            "pet snapshot wire roundtrip failed");

        var spawn=new PetEntitySpawnWire(7,42,1001,11,4,102,5002,7,7,24f,36f);
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

        var attack=new PetAttackWire(7,3,102,1_001_300_001);
        var attackFrame=WireProtocol.EncodePetAttack(attack);
        Checks.Require(attackFrame.Length==WireProtocol.PetAttackFrameBytes&&
            WireProtocol.TryDecodePetAttack(attackFrame,out var decodedAttack)&&
            decodedAttack==attack,
            "pet attack wire roundtrip failed");
        var attackResult=new PetAttackResultWire(
            7,3,NormalAttackStatus.Applied,102,1_001_300_001,
            25,75,100,4,55);
        var attackResultFrame=WireProtocol.EncodePetAttackResult(attackResult);
        Checks.Require(attackResultFrame.Length==WireProtocol.PetAttackResultFrameBytes&&
            WireProtocol.TryDecodePetAttackResult(
                attackResultFrame,out var decodedAttackResult)&&
            decodedAttackResult==attackResult,
            "pet attack result wire roundtrip failed");

        var malformed=WireProtocol.EncodePetCommand(summon);
        malformed[20]=9;
        Checks.Require(!WireProtocol.TryDecodePetCommand(malformed,out _),
            "pet command accepted invalid action");

        bool rejected=false;
        try
        {
            WireProtocol.EncodePetSnapshot(new(7,999,[
                new PetSnapshotEntryWire(101,5001,0,1,0)
            ]));
        }
        catch(ArgumentOutOfRangeException){rejected=true;}
        Checks.Require(rejected,"pet snapshot accepted active uid outside roster");
    }

    private static void GrowthManifest(string root)
    {
        string valid=Path.Combine(root,"pet-growth-rules.json");
        File.WriteAllText(valid,JsonSerializer.Serialize(new
        {
            schema="sgtest.pet_growth_curve/v1",
            source="unit-test Old Pet curve",
            max_level=100,
            level_up_total_experience=Enumerable.Range(1,100)
                .Select(level=>(long)level*10).ToArray()
        }));
        var growth=PetGrowthRuleCatalog.LoadVerified(valid);
        Checks.Require(growth.MaximumLevel==100&&
            growth.LevelUpThreshold(1)==10&&growth.LevelUpThreshold(100)==1000&&
            growth.AdvanceLevel(1,9)==1&&growth.AdvanceLevel(1,10)==2&&
            growth.AdvanceLevel(1,25)==3&&growth.AdvanceLevel(99,5000)==100&&
            growth.EvidenceSource=="unit-test Old Pet curve",
            "verified Pet growth manifest did not preserve cumulative thresholds");

        string invalid=Path.Combine(root,"pet-growth-rules-invalid.json");
        File.WriteAllText(invalid,JsonSerializer.Serialize(new
        {
            schema="sgtest.pet_growth_curve/v1",
            source="unit-test",
            max_level=100,
            level_up_total_experience=Enumerable.Repeat(10L,100).ToArray()
        }));
        bool rejected=false;
        try { PetGrowthRuleCatalog.LoadVerified(invalid); }
        catch(InvalidDataException){rejected=true;}
        Checks.Require(rejected,
            "Pet growth manifest accepted non-increasing total-EXP thresholds");
    }

    private static void CombatManifest(string root)
    {
        string valid=Path.Combine(root,"pet-combat-rules.json");
        File.WriteAllText(valid,JsonSerializer.Serialize(new
        {
            source="unit-test audited fixture",
            rules=new[]
            {
                new
                {
                    definition_id=5002,evolution_id=7,level=7,
                    fixed_damage=25,attack_range=24.0,cooldown_ticks=3
                }
            }
        }));
        var rules=PetCombatRuleCatalog.LoadVerified(valid);
        Checks.Require(rules.Count==1&&
            rules.TryGetValue(new(5002,7,7),out var rule)&&
            rule.FixedDamage==25&&rule.AttackRange==24&&
            rule.CooldownTicks==3&&rule.EvidenceSource=="unit-test audited fixture",
            "verified pet combat manifest did not retain exact source-backed rule");

        string invalid=Path.Combine(root,"pet-combat-rules-invalid.json");
        File.WriteAllText(invalid,JsonSerializer.Serialize(new
        {
            source="",
            rules=Array.Empty<object>()
        }));
        bool rejected=false;
        try { PetCombatRuleCatalog.LoadVerified(invalid); }
        catch(InvalidDataException){rejected=true;}
        Checks.Require(rejected,
            "pet combat manifest accepted rules without audited source");
    }

    private static void CombatAuthority(string root)
    {
        const int mapId=215;
        string folder=Checks.Fixture(root,mapId,12,12,(_,_)=>0);
        var grid=BlockingGrid.Load(folder,mapId);
        string populationPath=Path.Combine(folder,"population-pet-combat.json");
        File.WriteAllText(populationPath,JsonSerializer.Serialize(new
        {
            version=4,kind="verified_monster_runtime",map_id=mapId,
            stats_verified=true,gameplay_ready=true,
            definitions=new[]
            {
                new
                {
                    id=1,name="PetCombatFixture",sprite="EN001A",
                    atlas="Enemy/EN001A/atlas.json",
                    stats=new
                    {
                        level=3,max_hp=100,attack_damage=1,move_step=8.0,
                        aggro_radius=1.0,lose_target_radius=2.0,
                        leash_radius=2.0,attack_range=1.0,
                        attack_cooldown_ticks=30,respawn_ticks=30,
                        wander_radius=0.0,wander_interval_ticks=100
                    }
                }
            },
            spawns=new[]
            {
                new { source_index=0,source_flags=7,definition=1,p=new[]{40,24} }
            }
        }));
        var population=MonsterPopulationCatalog.LoadVerified(
            populationPath,mapId,grid);
        var runtime=new MonsterRuntime(grid,population,
            new(AiIntervalTicks:1,MaxAiPerStep:4,MaxPathQueriesPerStep:1,
                MaxPathExpandedCells:128));
        var key=new PetCombatRuleKey(5002,7,7);
        var coordinator=new MonsterWorldCoordinator(
            [runtime],petRules:new Dictionary<PetCombatRuleKey,VerifiedPetCombatRule>
            {
                [key]=new(5002,7,7,25,24,3,"unit-test audited fixture")
            });
        var actor=new ActorSnapshot(
            1001,mapId,11,4,0,0,24,24,1,false);
        var baseline=coordinator.Process(new WorldChangedBatch(
            1,[],[new(ActorLifecycleKind.Joined,actor)]));
        var target=baseline.Changed.Single(x=>x.CurrentHp==100);
        var pet=new PetWorldPresence(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            102,5002,7,7,24,24);

        var first=coordinator.ResolvePetNormalAttack(
            new(actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
                1,pet.PetUid,target.EntityId),pet);
        Checks.Require(first.Status==NormalAttackStatus.Applied&&
            first.Damage==25&&first.CurrentHp==75&&first.MaxHp==100,
            "verified pet rule did not apply server-owned damage");

        var replay=coordinator.ResolvePetNormalAttack(
            new(actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
                1,pet.PetUid,target.EntityId),pet);
        Checks.Require(replay.Status==NormalAttackStatus.ReplayedSequence,
            "pet attack replay was not rejected");

        var cooldown=coordinator.ResolvePetNormalAttack(
            new(actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
                2,pet.PetUid,target.EntityId),pet);
        Checks.Require(cooldown.Status==NormalAttackStatus.Cooldown,
            "pet attack ignored server-owned cooldown");

        coordinator.Process(new WorldChangedBatch(2,[]));
        coordinator.Process(new WorldChangedBatch(3,[]));
        coordinator.Process(new WorldChangedBatch(4,[]));
        var second=coordinator.ResolvePetNormalAttack(
            new(actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
                3,pet.PetUid,target.EntityId),pet);
        Checks.Require(second.Status==NormalAttackStatus.Applied&&
            second.Damage==25&&second.CurrentHp==50,
            "pet attack did not resume after verified cooldown");

        var noRule=pet with {Level=8};
        var blocked=coordinator.ResolvePetNormalAttack(
            new(actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
                4,noRule.PetUid,target.EntityId),noRule);
        Checks.Require(blocked.Status==NormalAttackStatus.CombatUnavailable,
            "pet combat guessed damage for an unverified exact level");

        var gateway=new GatewayConnection(actor);
        Checks.Require(gateway.QueueAoi(
                new(AoiEventKind.WorldReady,10,actor.MapEpoch,actor))==
                GatewaySendStatus.Enqueued&&
            gateway.FeedClientBytes(
                WireProtocol.EncodeWorldReadyAck(new(actor.MapEpoch,10)),
                _=>true,out _)==WireFeedStatus.Accepted,
            "pet combat gateway Ready ACK failed");
        TrustedPetAttack captured=default;
        byte[] frame=WireProtocol.EncodePetAttack(
            new(actor.MapEpoch,1,pet.PetUid,target.EntityId));
        Checks.Require(gateway.FeedClientBytes(
                frame,_=>true,out var accepted,
                postPetAttack:value=>{captured=value;return true;})==
                WireFeedStatus.Accepted&&accepted==1&&
            captured.ActorId==actor.ActorId&&
            captured.ConnectionEpoch==actor.ConnectionEpoch&&
            captured.RequestMapEpoch==actor.MapEpoch&&
            captured.AttackSequence==1&&captured.PetUid==pet.PetUid&&
            captured.TargetEntityId==target.EntityId,
            "pet combat gateway did not stamp trusted actor generation");
    }

    private static async Task GrowthPersistence()
    {
        string root=Path.Combine(
            Path.GetTempPath(),"sgtest_pet_growth_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            string manifest=Path.Combine(root,"pet-growth.json");
            File.WriteAllText(manifest,JsonSerializer.Serialize(new
            {
                schema="sgtest.pet_growth_curve/v1",
                source="unit-test Old Pet curve",
                max_level=100,
                level_up_total_experience=Enumerable.Range(1,100)
                    .Select(level=>(long)level*10).ToArray()
            }));
            var growth=PetGrowthRuleCatalog.LoadVerified(manifest);

            long character,otherCharacter,petUid,foreignPet;
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "pet_growth","Pet-Growth-Secret-2026!")).AccountId;
                long other=(await store.CreateAccountAsync(
                    "pet_growth_other","Pet-Growth-Other-2026!")).AccountId;
                character=(await store.CreateCharacterAsync(
                    account,0,"PetGrowth",new(15,100,100))).Character!.CharacterId;
                otherCharacter=(await store.CreateCharacterAsync(
                    other,0,"PetGrowthOther",new(15,120,100))).Character!.CharacterId;
                petUid=await store.ProvisionPetAsync(character,5002,1,0,evolutionId:7);
                foreignPet=await store.ProvisionPetAsync(otherCharacter,6001,1,0);

                var first=await store.ApplyPetExperienceAsync(
                    character,petUid,25,"monster:215:99:death:1",growth);
                Checks.Require(first.Status==PetExperienceGrantStatus.Applied&&
                    first.PetUid==petUid&&first.Level==3&&first.Experience==25,
                    "Pet EXP grant did not cross multiple verified thresholds atomically");

                var replay=await store.ApplyPetExperienceAsync(
                    character,petUid,25,"monster:215:99:death:1",growth);
                Checks.Require(replay.Status==PetExperienceGrantStatus.AlreadyProcessed&&
                    replay.Level==3&&replay.Experience==25,
                    "Pet EXP exact replay was not idempotent");

                var conflict=await store.ApplyPetExperienceAsync(
                    character,petUid,26,"monster:215:99:death:1",growth);
                Checks.Require(conflict.Status==PetExperienceGrantStatus.GrantConflict&&
                    conflict.Level==3&&conflict.Experience==25,
                    "Pet EXP grant key accepted a changed payload");

                var foreign=await store.ApplyPetExperienceAsync(
                    character,foreignPet,10,"monster:215:100:death:1",growth);
                Checks.Require(foreign.Status==PetExperienceGrantStatus.PetNotOwned&&
                    foreign.Level==0&&foreign.Experience==0,
                    "Pet EXP grant accepted a foreign Pet UID");

                var second=await store.ApplyPetExperienceAsync(
                    character,petUid,100,"monster:215:101:death:1",growth);
                Checks.Require(second.Status==PetExperienceGrantStatus.Applied&&
                    second.Level==13&&second.Experience==125,
                    "Pet EXP second grant did not retain total EXP/cross-level semantics");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var pet=(await reopened.GetPetsAsync(character)).Single();
                Checks.Require(pet.PetUid==petUid&&pet.Level==13&&
                    pet.Experience==125,
                    "Pet EXP/level did not survive SQLite reopen");
                var replay=await reopened.ApplyPetExperienceAsync(
                    character,petUid,100,"monster:215:101:death:1",growth);
                Checks.Require(replay.Status==PetExperienceGrantStatus.AlreadyProcessed&&
                    replay.Level==13&&replay.Experience==125,
                    "Pet EXP grant replay identity did not survive SQLite reopen");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root,true);
        }
    }

    private static async Task MigrationV30()
    {
        string root=Path.Combine(Path.GetTempPath(),"sgtest_pet_v30_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            long character,petUid;
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "pet_migrate","Pet-Migrate-Secret-2026!")).AccountId;
                character=(await store.CreateCharacterAsync(
                    account,0,"PetMigrate",new(15,140,140))).Character!.CharacterId;
                petUid=await store.ProvisionPetAsync(
                    character,7001,9,4321,new byte[]{9,8,7});
                var summon=await store.ApplyPetActionAsync(
                    character,21,6,1,PetActionKind.Summon,petUid);
                Checks.Require(summon.Status==PetActionStatus.Applied,
                    "v30 pet migration fixture could not summon pet");
            }

            using(var connection=new SqliteConnection($"Data Source={db}"))
            {
                connection.Open();
                using var command=connection.CreateCommand();
                command.CommandText=@"ALTER TABLE pet_instances DROP COLUMN evolution_id;
UPDATE schema_version SET version=30;";
                command.ExecuteNonQuery();
            }

            using(var migrated=new SqliteIdentityStore(db))
            {
                await migrated.InitializeAsync();
                var pet=(await migrated.GetPetsAsync(character)).Single();
                Checks.Require(
                    pet.PetUid==petUid&&pet.DefinitionId==7001&&pet.EvolutionId==0&&
                    pet.Level==9&&pet.Experience==4321&&pet.IsActive&&
                    pet.StateBlob.SequenceEqual(new byte[]{9,8,7}),
                    "v30->v31 pet migration changed identity/state or did not default evolution to zero");
            }

            string backup=Directory.GetFiles(
                root,$"identity.db.pre-v30-to-v{SqliteIdentityStore.SchemaVersion}.*.bak").Single();
            using(var connection=new SqliteConnection($"Data Source={backup}"))
            {
                connection.Open();
                using var command=connection.CreateCommand();
                command.CommandText="SELECT version FROM schema_version LIMIT 1;";
                Checks.Require(Convert.ToInt32(command.ExecuteScalar())==30,
                    "pet migration backup did not preserve v30 schema");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root,true);
        }
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
                petA=await store.ProvisionPetAsync(character,5001,4,88,new byte[]{1,2,3},evolutionId:2);
                petB=await store.ProvisionPetAsync(character,5002,7,1234,evolutionId:7);
                foreignPet=await store.ProvisionPetAsync(otherCharacter,6001,evolutionId:1);

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
                    pets.Single(x=>x.PetUid==petB).EvolutionId==7&&
                    pets.Single(x=>x.PetUid==petA).EvolutionId==2&&
                    !pets.Single(x=>x.PetUid==petA).IsActive,
                    "active pet uniqueness/evolution identity not reflected in roster");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var persisted=await reopened.GetPetsAsync(character);
                Checks.Require(persisted.Count==2&&
                    persisted.Count(x=>x.IsActive)==1&&
                    persisted.Single(x=>x.IsActive).PetUid==petB&&
                    persisted.Single(x=>x.PetUid==petA).EvolutionId==2&&
                    persisted.Single(x=>x.PetUid==petB).EvolutionId==7&&
                    persisted.Single(x=>x.PetUid==petA).StateBlob.SequenceEqual(new byte[]{1,2,3}),
                    "pet roster/active state/evolution/blob did not survive reopen");

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
