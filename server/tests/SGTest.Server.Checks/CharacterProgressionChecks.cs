using System.Text.Json;
using Microsoft.Data.Sqlite;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class CharacterProgressionChecks
{
    public static void Verify(string root)
    {
        Checks.Run("character level-up and attribute allocation are durable server authority",
            ()=>Progression(root).GetAwaiter().GetResult());
    }

    private static async Task Progression(string root)
    {
        string folder=Path.Combine(root,"character_progression");
        Directory.CreateDirectory(folder);
        string manifest=Path.Combine(folder,"progression.json");
        string db=Path.Combine(folder,"identity.db");
        WriteManifest(manifest);
        var rules=CharacterProgressionRules.LoadVerified(manifest);

        try
        {
            long character;
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "progress_owner","Progress-Owner-Secret-2026!")).AccountId;
                character=(await store.CreateCharacterAsync(
                    account,0,"ProgressOwner",new(15,100,100))).Character!.CharacterId;
                await store.SetCharacterCombatProfileAsync(character,new(
                    CombatProfileTrust.ProfileOnlyVerified,"progression-check",
                    Race:0,Career:0,Strength:2,Dexterity:2,Constitution:2,Wisdom:2,
                    Level:1,CurrentHp:151,MaxHp:151,Attack:6,Defense:3,Hit:8,Dodge:8,
                    AttackRange:0,AttackCooldownTicks:0,CurrentMp:121,MaxMp:121,
                    AvailableAttributePoints:0));
                var initial=await store.GetCharacterCombatProfileAsync(character);
                Checks.Require(
                    rules.TryBackfillLegacyMissingWill(initial!.Data,out var wpBackfill)&&
                    wpBackfill.CurrentWp==101&&wpBackfill.MaxWp==101&&
                    wpBackfill.AvailableAttributePoints==initial.Data.AvailableAttributePoints,
                    "legacy v46 0/0 WP did not source-backfill without shifting positional points");
                await store.SetCharacterCombatProfileAsync(character,wpBackfill);
                initial=await store.GetCharacterCombatProfileAsync(character);
                var initialWire=CharacterProfileProgressionHandler.BuildState(
                    initial!.Data,character,0,rules);
                Checks.Require(
                    initialWire.VerifiedStats==
                        (PlayerProfileStatVerification.Attack |
                         PlayerProfileStatVerification.Hit |
                         PlayerProfileStatVerification.Dodge)&&
                    initialWire.Experience==0&&initialWire.ExperiencePercent==0,
                    "profile baseline verification or EXP percent was not source-backed");

                var first=await store.ApplyCharacterProgressionExperienceAsync(
                    character,1,"monsterdeath:1:1",rules);
                var firstWire=CharacterProfileProgressionHandler.BuildState(
                    first.Profile!.Data,character,1,rules);
                Checks.Require(
                    first.Status==CharacterExperienceGrantStatus.Applied&&
                    first.Profile.Data.Level==1&&first.CurrentExperience==1&&
                    first.Profile.Data.AvailableAttributePoints==0&&
                    firstWire.Experience==1&&firstWire.ExperiencePercent==50,
                    "sub-threshold EXP changed level/points or did not publish original EXP percent");

                var level=await store.ApplyCharacterProgressionExperienceAsync(
                    character,1,"monsterdeath:1:2",rules);
                var levelWire=CharacterProfileProgressionHandler.BuildState(
                    level.Profile!.Data,character,2,rules);
                Checks.Require(
                    level.Status==CharacterExperienceGrantStatus.Applied&&
                    level.Leveled&&level.Profile.Data.Level==2&&
                    level.CurrentExperience==0&&levelWire.ExperiencePercent==0&&
                    level.Profile.Data.AvailableAttributePoints==2&&
                    level.Profile!.Data.CurrentHp==level.Profile!.Data.MaxHp&&
                    level.Profile!.Data.CurrentMp==level.Profile!.Data.MaxMp&&
                    level.Profile!.Data.CurrentWp==level.Profile!.Data.MaxWp&&
                    level.Profile!.Data.MaxWp==101,
                    "level-up did not consume threshold refill vitals and award two points");

                var replay=await store.ApplyCharacterProgressionExperienceAsync(
                    character,1,"monsterdeath:1:2",rules);
                Checks.Require(
                    replay.Status==CharacterExperienceGrantStatus.AlreadyProcessed&&
                    replay.Profile?.Data.Level==2&&
                    replay.Profile.Data.AvailableAttributePoints==2,
                    "replayed death duplicated level or attribute points");

                int attackBefore=level.Profile!.Data.Attack;
                var strength=await store.AllocateCharacterAttributeAsync(
                    character,CharacterAttributeKind.Strength,rules);
                Checks.Require(
                    strength.Status==CharacterAttributeAllocationStatus.Applied&&
                    strength.Profile?.Data.Strength==3&&
                    strength.Profile!.Data.AvailableAttributePoints==1&&
                    strength.Profile!.Data.Attack>attackBefore,
                    "strength allocation did not spend one point and recompute profile");

                int maxHpBefore=strength.Profile!.Data.MaxHp;
                var constitution=await store.AllocateCharacterAttributeAsync(
                    character,CharacterAttributeKind.Constitution,rules);
                Checks.Require(
                    constitution.Status==CharacterAttributeAllocationStatus.Applied&&
                    constitution.Profile?.Data.Constitution==3&&
                    constitution.Profile.Data.AvailableAttributePoints==0&&
                    constitution.Profile.Data.MaxHp>maxHpBefore,
                    "constitution allocation did not update authoritative max HP");

                var exhausted=await store.AllocateCharacterAttributeAsync(
                    character,CharacterAttributeKind.Wisdom,rules);
                Checks.Require(
                    exhausted.Status==CharacterAttributeAllocationStatus.NoAvailablePoints&&
                    exhausted.Profile?.Data.Wisdom==2,
                    "attribute allocation did not fail closed at zero points");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var profile=await reopened.GetCharacterCombatProfileAsync(character);
                var experience=await reopened.GetCharacterExperienceAsync(character);
                Checks.Require(
                    profile?.Data.Level==2&&profile.Data.Strength==3&&
                    profile.Data.Constitution==3&&profile.Data.Wisdom==2&&
                    profile.Data.AvailableAttributePoints==0&&
                    profile.Data.CurrentWp==101&&profile.Data.MaxWp==101&&
                    experience is {Experience:2},
                    "level/attributes/points/experience did not survive restart");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    internal static void WriteManifest(string path)
    {
        var levels=Enumerable.Range(1,100).Select(level=>new
        {
            level,
            required_experience=level==1?2L:level==2?32L:1000L+level,
            attribute_points_awarded=2,
            base_hp=100+level,
            base_mp=100+level,
            base_attack=4,
            base_defense=3,
            base_magic_attack=4,
            base_magic_defense=3,
            base_hit=6*level,
            base_dodge=6*level,
            base_will=101
        }).ToArray();
        int[] ramp=Enumerable.Range(1,200).Select(v=>v*2).ToArray();
        int[] hp=Enumerable.Range(1,200).Select(v=>40+v*3).ToArray();
        int[] hit=Enumerable.Range(1,200).Select(v=>v).ToArray();
        int[] mp=Enumerable.Range(1,200).Select(v=>20+v).ToArray();
        var race=new
        {
            strength_attack=ramp,
            constitution_hp=hp,
            dexterity_hit=hit,
            dexterity_dodge=hit,
            wisdom_mp=mp,
            wisdom_magic_attack=hit
        };
        var doc=new
        {
            schema="sgtest.character_progression_rules/v1",
            status="pass",
            rules_verified=true,
            gameplay_ready=true,
            source="synthetic progression check preserving verified manifest shape",
            max_level=100,
            max_attribute=200,
            levels,
            races=new {boy=race,girl=race,migu=race,robot=race}
        };
        File.WriteAllText(path,JsonSerializer.Serialize(doc));
    }
}
