using Microsoft.Data.Sqlite;
using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Monster;
using SGTest.Server.Pet;
using SGTest.Server.Visibility;

internal static class CharacterExperienceChecks
{
    public static void Verify(string root)
    {
        Checks.Run("solo character EXP policy matches verified legacy level-difference brackets",
            Policy);
        Checks.Run("character EXP persistence is durable replay-safe and conflict-safe",
            ()=>Persistence(root).GetAwaiter().GetResult());
        Checks.Run("monster death settlement persists character EXP then active Pet half-EXP exactly once",
            ()=>DeathSettlement(root).GetAwaiter().GetResult());
        Checks.Run("party monster EXP uses damage pools legacy sharing and per-actor replay keys",
            ()=>PartyDeathSettlement(root).GetAwaiter().GetResult());
    }

    private static void Policy()
    {
        static long Settle(int difference)=>
            CharacterExperienceSettlementPolicy.SettleSolo(
                50,50-difference,1000);

        Checks.Require(Settle(-2)==1000&&Settle(0)==1000&&Settle(3)==1000,
            "equal/higher enemy or <=3 level gap changed solo EXP");
        Checks.Require(Settle(4)==800&&Settle(6)==800,
            "4-6 level solo EXP bracket is not 80 percent");
        Checks.Require(Settle(7)==600&&Settle(9)==600,
            "7-9 level solo EXP bracket is not 60 percent");
        Checks.Require(Settle(10)==400&&Settle(12)==400,
            "10-12 level solo EXP bracket is not 40 percent");
        Checks.Require(Settle(13)==200&&Settle(15)==200,
            "13-15 level solo EXP bracket is not one fifth");
        Checks.Require(Settle(16)==100&&Settle(18)==100,
            "16-18 level solo EXP bracket is not one tenth");
        Checks.Require(Settle(19)==0,
            "greater-than-18 solo level gap did not settle zero EXP");

        Checks.Require(
            CharacterExperienceSettlementPolicy.ProportionalPool(1000,300,1000)==300,
            "damage-proportional EXP pool changed legacy floor division");
        Checks.Require(
            CharacterExperienceSettlementPolicy.SettlePartyMember(10,10,100,2,10)==50,
            "equal-level two-member party did not split the group pool equally");
        Checks.Require(
            CharacterExperienceSettlementPolicy.SettlePartyMember(9,9,100,2,10)==69,
            "lower-level two-member party bonus/gap penalty changed");
        Checks.Require(
            CharacterExperienceSettlementPolicy.SettlePartyMember(1,1,100,2,10)==13&&
            CharacterExperienceSettlementPolicy.SettlePartyMember(1,1,100,2,11)==0,
            "party max-level gap quadratic penalty or ten-level cutoff changed");
        Checks.Require(
            CharacterExperienceSettlementPolicy.SettlePartyMember(9,9,600,6,10)==297&&
            CharacterExperienceSettlementPolicy.SettlePartyMember(10,10,600,6,10)==100,
            "six-member legacy bonus/equal-max split changed");
        Checks.Require(
            CharacterExperienceSettlementPolicy.SettlePartyMember(9,9,600,8,10)==74,
            "greater-than-six party fallback unexpectedly applied a legacy bonus");
        Checks.Require(
            CharacterExperienceSettlementPolicy.SettlePartyMember(10,5,100,2,11)==55,
            "enemy-level penalty was not applied after party sharing");
    }

    private static async Task Persistence(string root)
    {
        string folder=Path.Combine(root,"experience_persistence");
        Directory.CreateDirectory(folder);
        string db=Path.Combine(folder,"identity.db");
        long character;
        try
        {
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "exp_owner","Exp-Owner-Secret-2026!")).AccountId;
                character=(await store.CreateCharacterAsync(
                    account,0,"ExpOwner",new(15,100,100))).Character!.CharacterId;

                var initial=await store.GetCharacterExperienceAsync(character);
                Checks.Require(initial is { Experience:0 },
                    "new character did not expose zero durable EXP");

                var first=await store.ApplyCharacterExperienceAsync(
                    character,80,"monsterdeath:1:10");
                Checks.Require(first.Status==CharacterExperienceGrantStatus.Applied&&
                    first.Experience==80,
                    "first trusted character EXP grant was not applied");

                var replay=await store.ApplyCharacterExperienceAsync(
                    character,80,"monsterdeath:1:10");
                Checks.Require(
                    replay.Status==CharacterExperienceGrantStatus.AlreadyProcessed&&
                    replay.Experience==80,
                    "exact character EXP replay was not idempotent");

                var conflict=await store.ApplyCharacterExperienceAsync(
                    character,81,"monsterdeath:1:10");
                Checks.Require(
                    conflict.Status==CharacterExperienceGrantStatus.GrantConflict&&
                    conflict.Experience==80,
                    "same character EXP grant key with changed amount was not rejected");

                var zero=await store.ApplyCharacterExperienceAsync(
                    character,0,"monsterdeath:1:11");
                Checks.Require(zero.Status==CharacterExperienceGrantStatus.Applied&&
                    zero.Experience==80,
                    "zero settled EXP was not durably acknowledged");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var state=await reopened.GetCharacterExperienceAsync(character);
                Checks.Require(state is { Experience:80 },
                    "character EXP did not survive store restart");
                var replay=await reopened.ApplyCharacterExperienceAsync(
                    character,80,"monsterdeath:1:10");
                Checks.Require(
                    replay.Status==CharacterExperienceGrantStatus.AlreadyProcessed&&
                    replay.Experience==80,
                    "character EXP replay key did not survive restart");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private static async Task DeathSettlement(string root)
    {
        string folder=Path.Combine(root,"experience_death");
        Directory.CreateDirectory(folder);
        string db=Path.Combine(folder,"identity.db");
        try
        {
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "exp_killer","Exp-Killer-Secret-2026!")).AccountId;
            long character=(await store.CreateCharacterAsync(
                account,0,"ExpKiller",new(15,120,120))).Character!.CharacterId;
            await store.SetCharacterCombatProfileAsync(character,new(
                CombatProfileTrust.SyntheticTest,"character-exp-check",
                Race:0,Career:0,Strength:10,Dexterity:10,Constitution:10,Wisdom:10,
                Level:10,CurrentHp:100,MaxHp:100,Attack:50,Defense:5,Hit:10,Dodge:5,
                AttackRange:64,AttackCooldownTicks:1));

            long petUid=await store.ProvisionPetAsync(character,5001);
            var summoned=await store.ApplyPetActionAsync(
                character,11,4,1,PetActionKind.Summon,petUid);
            Checks.Require(summoned.Status==PetActionStatus.Applied,
                "EXP settlement fixture could not activate Pet");

            var router=new WorldResultRouter();
            var growth=new PetGrowthCurve(
                2,new long[]{100,200},"synthetic character EXP check");
            var pets=new PetCoordinator(store,growth);
            var rule=new CharacterExperienceRewardRule(
                15,7001,6,100,CharacterExperienceRewardTrust.SyntheticTest,
                "synthetic character EXP check");
            var binding=new ConnectionBinding(character,11,4);
            var dead=new MonsterSnapshot(
                9001,7001,0,15,140,140,140,140,0,100,
                MonsterAiState.Dead,character,7,0);

            var settlement=new CharacterExperienceSettlement(
                store,router,3,[rule],pets,capacity:2);
            Checks.Require(
                settlement.TryPostSoloDeath(binding,dead,petUid)==
                    CharacterExperienceDeathPostStatus.Enqueued,
                "verified solo death was not queued");
            var worker=settlement.RunAsync();
            settlement.Complete();
            await worker;

            var state=await store.GetCharacterExperienceAsync(character);
            var pet=(await store.GetPetsAsync(character)).Single(x=>x.PetUid==petUid);
            Checks.Require(state is { Experience:80 }&&
                pet.Experience==40&&pet.Level==1&&
                settlement.Statistics.GrantsApplied==1&&
                settlement.Statistics.PetGrantsApplied==1&&
                settlement.Statistics.Pending==0,
                "death settlement did not apply 80 percent character EXP then half Pet EXP");

            var replaySettlement=new CharacterExperienceSettlement(
                store,router,3,[rule],pets,capacity:2);
            Checks.Require(
                replaySettlement.TryPostSoloDeath(binding,dead,petUid)==
                    CharacterExperienceDeathPostStatus.Enqueued,
                "replay death was not accepted for idempotency check");
            var replayWorker=replaySettlement.RunAsync();
            replaySettlement.Complete();
            await replayWorker;
            state=await store.GetCharacterExperienceAsync(character);
            pet=(await store.GetPetsAsync(character)).Single(x=>x.PetUid==petUid);
            Checks.Require(state is { Experience:80 }&&pet.Experience==40&&
                replaySettlement.Statistics.Replays==1&&
                replaySettlement.Statistics.PetReplays==1,
                "replayed death duplicated character or Pet EXP");

            var bounded=new CharacterExperienceSettlement(
                store,router,3,[rule],pets,capacity:1);
            Checks.Require(
                bounded.TryPostSoloDeath(
                    binding,dead with {EntityRevision=8},petUid)==
                    CharacterExperienceDeathPostStatus.Enqueued&&
                bounded.TryPostSoloDeath(
                    binding,dead with {EntityRevision=9},petUid)==
                    CharacterExperienceDeathPostStatus.Backpressured&&
                bounded.Statistics.Overflows==1,
                "character EXP queue did not fail closed on overflow");
            var boundedWorker=bounded.RunAsync();
            bounded.Complete();
            await boundedWorker;
            state=await store.GetCharacterExperienceAsync(character);
            pet=(await store.GetPetsAsync(character)).Single(x=>x.PetUid==petUid);
            Checks.Require(state is { Experience:160 }&&pet.Experience==80,
                "bounded accepted death did not settle exactly once");

            await store.SetCharacterCombatProfileAsync(character,new(
                CombatProfileTrust.SyntheticTest,"character-exp-check-zero",
                Race:0,Career:0,Strength:10,Dexterity:10,Constitution:10,Wisdom:10,
                Level:25,CurrentHp:100,MaxHp:100,Attack:50,Defense:5,Hit:10,Dodge:5,
                AttackRange:64,AttackCooldownTicks:1));
            var zeroSettlement=new CharacterExperienceSettlement(
                store,router,3,[rule],pets);
            Checks.Require(
                zeroSettlement.TryPostSoloDeath(
                    binding,dead with {EntityRevision=10},petUid)==
                    CharacterExperienceDeathPostStatus.Enqueued,
                "zero-EXP death was not queued for durable acknowledgement");
            var zeroWorker=zeroSettlement.RunAsync();
            zeroSettlement.Complete();
            await zeroWorker;
            state=await store.GetCharacterExperienceAsync(character);
            pet=(await store.GetPetsAsync(character)).Single(x=>x.PetUid==petUid);
            Checks.Require(state is { Experience:160 }&&pet.Experience==80&&
                zeroSettlement.Statistics.ZeroAwards==1,
                "greater-than-18 level gap did not settle durable zero without Pet EXP");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private static async Task PartyDeathSettlement(string root)
    {
        string folder=Path.Combine(root,"experience_party_death");
        Directory.CreateDirectory(folder);
        string db=Path.Combine(folder,"identity.db");
        try
        {
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync();

            long accountA=(await store.CreateAccountAsync(
                "exp_party_a","Exp-Party-A-Secret-2026!")).AccountId;
            long actorA=(await store.CreateCharacterAsync(
                accountA,0,"ExpPartyA",new(15,100,100))).Character!.CharacterId;
            long accountB=(await store.CreateAccountAsync(
                "exp_party_b","Exp-Party-B-Secret-2026!")).AccountId;
            long actorB=(await store.CreateCharacterAsync(
                accountB,0,"ExpPartyB",new(15,100,100))).Character!.CharacterId;

            await store.SetCharacterCombatProfileAsync(actorA,new(
                CombatProfileTrust.SyntheticTest,"party-exp-a",
                Race:0,Career:0,Strength:10,Dexterity:10,Constitution:10,Wisdom:10,
                Level:10,CurrentHp:100,MaxHp:100,Attack:50,Defense:5,Hit:10,Dodge:5,
                AttackRange:64,AttackCooldownTicks:1));
            await store.SetCharacterCombatProfileAsync(actorB,new(
                CombatProfileTrust.SyntheticTest,"party-exp-b",
                Race:0,Career:0,Strength:10,Dexterity:10,Constitution:10,Wisdom:10,
                Level:9,CurrentHp:100,MaxHp:100,Attack:50,Defense:5,Hit:10,Dodge:5,
                AttackRange:64,AttackCooldownTicks:1));

            var router=new WorldResultRouter();
            var rule=new CharacterExperienceRewardRule(
                15,7002,9,100,CharacterExperienceRewardTrust.SyntheticTest,
                "synthetic party character EXP check");
            var bindingA=new ConnectionBinding(actorA,21,8);
            var bindingB=new ConnectionBinding(actorB,22,8);
            var dead=new MonsterSnapshot(
                9002,7002,0,15,140,140,140,140,0,100,
                MonsterAiState.Dead,actorA,11,0);

            var partyGroup=new CharacterExperienceDeathGroup(100,
            [
                new CharacterExperienceDeathParticipant(bindingA,60),
                new CharacterExperienceDeathParticipant(bindingB,40)
            ]);
            var settlement=new CharacterExperienceSettlement(
                store,router,5,[rule]);
            Checks.Require(
                settlement.TryPostDeath(dead,100,[partyGroup])==
                    CharacterExperienceDeathPostStatus.Enqueued,
                "party EXP death plan was not accepted");
            var worker=settlement.RunAsync();
            settlement.Complete();
            await worker;

            var stateA=await store.GetCharacterExperienceAsync(actorA);
            var stateB=await store.GetCharacterExperienceAsync(actorB);
            Checks.Require(
                stateA is { Experience:50 }&&stateB is { Experience:69 }&&
                settlement.Statistics.GrantsApplied==2,
                "party EXP did not apply equal-max split plus lower-level legacy bonus");

            var replay=new CharacterExperienceSettlement(
                store,router,5,[rule]);
            Checks.Require(
                replay.TryPostDeath(dead,100,[partyGroup])==
                    CharacterExperienceDeathPostStatus.Enqueued,
                "party EXP replay was not queued");
            var replayWorker=replay.RunAsync();
            replay.Complete();
            await replayWorker;
            stateA=await store.GetCharacterExperienceAsync(actorA);
            stateB=await store.GetCharacterExperienceAsync(actorB);
            Checks.Require(
                stateA is { Experience:50 }&&stateB is { Experience:69 }&&
                replay.Statistics.Replays==2,
                "party EXP per-actor grant keys were not replay-safe");

            var proportional=new CharacterExperienceSettlement(
                store,router,5,[rule]);
            var nextDeath=dead with { EntityRevision=12 };
            Checks.Require(
                proportional.TryPostDeath(nextDeath,100,
                [
                    new CharacterExperienceDeathGroup(75,
                    [
                        new CharacterExperienceDeathParticipant(bindingA,75)
                    ]),
                    new CharacterExperienceDeathGroup(25,
                    [
                        new CharacterExperienceDeathParticipant(bindingB,25)
                    ])
                ])==CharacterExperienceDeathPostStatus.Enqueued,
                "multi-group damage-proportional EXP death plan was not accepted");
            var proportionalWorker=proportional.RunAsync();
            proportional.Complete();
            await proportionalWorker;
            stateA=await store.GetCharacterExperienceAsync(actorA);
            stateB=await store.GetCharacterExperienceAsync(actorB);
            Checks.Require(
                stateA is { Experience:125 }&&stateB is { Experience:94 },
                "separate contributor groups did not receive proportional EXP pools");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }
}
