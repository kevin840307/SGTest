using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Monster;
using SGTest.Server.Map;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class SkillCombatChecks
{
    private static void Require(bool condition,string message) =>
        Checks.Require(condition,message);

    public static void Verify(string root)
    {
        Checks.Run("verified skill manifest admits bounded MP metadata but rejects AoE",
            ()=>Manifest(root));
        Checks.Run("skill wire contains only epoch sequence skill and target intent",
            Wire);
        Checks.Run("skill gateway injects authenticated actor and combat profile",
            Gateway);
        Checks.Run("skill authority owns rule damage range cooldown replay and monster death",
            ()=>Authority(root));
        Checks.Run("skill prepare is non-mutating and commit/cancel owns effect boundary",
            ()=>PrepareCommit(root));
        Checks.Run("self-centered area skill selects all bounded authoritative targets",
            ()=>AreaAuthority(root));
    }

    private static void Manifest(string root)
    {
        string valid=Path.Combine(root,"verified-skills.json");
        File.WriteAllText(valid,JsonSerializer.Serialize(new {
            kind="verified_single_target_skill_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="ci:explicit-synthetic-skill-rule",
            rules=new[]{new{skill_id=99001,target_kind="single_monster",
                damage_kind="fixed",fixed_damage=60,range=48.0,
                cooldown_ticks=3,mp_cost=0}}
        }));
        var rules=SkillRuleCatalog.LoadVerified(valid);
        Require(rules.Count==1 &&
            rules[99001]==new VerifiedSkillRule(99001,60,48,3),
            "verified skill rule rejected");

        string mp=Path.Combine(root,"verified-mp-skill.json");
        File.WriteAllText(mp,File.ReadAllText(valid).Replace(
            "\"mp_cost\":0","\"mp_cost\":1"));
        var mpRules=SkillRuleCatalog.LoadVerified(mp);
        Require(mpRules[99001]==new VerifiedSkillRule(99001,60,48,3,1),
            "verified MP cost metadata was not retained");

        string aoe=Path.Combine(root,"verified-area-skill.json");
        File.WriteAllText(aoe,JsonSerializer.Serialize(new {
            kind="verified_skill_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="ci:explicit-synthetic-area-rule",
            rules=new[]{new{skill_id=99001,target_kind="self_centered_area",
                damage_kind="fixed",fixed_damage=40,range=0.0,
                area_radius=900.0,line_of_sight=false,
                cooldown_ticks=0,mp_cost=1}}
        }));
        var areaRules=SkillRuleCatalog.LoadVerified(aoe);
        Require(areaRules[99001]==new VerifiedSkillRule(
                99001,40,0,0,1,SkillTargetKind.SelfCenteredArea,900,false),
            "verified area skill metadata not retained");

        string missingLos=Path.Combine(root,"unowned-area-los.json");
        File.WriteAllText(missingLos,File.ReadAllText(aoe)
            .Replace(",\"line_of_sight\":false",""));
        bool rejected=false;
        try{SkillRuleCatalog.LoadVerified(missingLos);}
        catch(InvalidDataException){rejected=true;}
        Require(rejected,"area skill invented line-of-sight semantics");
    }

    private static void Wire()
    {
        var request=new SkillUseWire(3,7,99001,1_021_400_001);
        byte[] frame=WireProtocol.EncodeSkillUse(request);
        Require(frame.Length==32 &&
            WireProtocol.TryDecodeSkillUse(frame,out var parsed) &&
            parsed==request,"skill request roundtrip/size");
        var result=new SkillUseResultWire(
            3,7,SkillUseStatus.Applied,42,99001,
            request.TargetEntityId,60,40,100,5);
        byte[] response=WireProtocol.EncodeSkillUseResult(result);
        Require(response.Length==61 &&
            WireProtocol.TryDecodeSkillUseResult(response,out var decoded) &&
            decoded==result,"skill result roundtrip/size");

        var areaRequest=new SkillUseWire(3,8,99001,0);
        var areaFrame=WireProtocol.EncodeSkillUse(areaRequest);
        Require(WireProtocol.TryDecodeSkillUse(areaFrame,out var decodedArea) &&
                decodedArea==areaRequest,
            "self-centered area target=0 wire rejected");
        var areaResult=new SkillUseResultWire(
            3,8,SkillUseStatus.Applied,43,99001,0,0,0,0,0);
        var areaResponse=WireProtocol.EncodeSkillUseResult(areaResult);
        Require(WireProtocol.TryDecodeSkillUseResult(
                    areaResponse,out var decodedAreaResult) &&
                decodedAreaResult==areaResult,
            "area skill summary wire rejected");
    }

    private static void Gateway()
    {
        var actor=new ActorSnapshot(11,13,9,3,0,0,24,24,1,false);
        var profile=new TrustedCombatProfile(
            Attack:30,Defense:4,Hit:10,Dodge:3,
            AttackRange:48,AttackCooldownTicks:15,Level:3);
        var gateway=new GatewayConnection(actor,combatProfile:profile);
        Require(gateway.QueueAoi(new(AoiEventKind.WorldReady,10,
                    actor.MapEpoch,actor))==GatewaySendStatus.Enqueued &&
            gateway.FeedClientBytes(
                WireProtocol.EncodeWorldReadyAck(new(actor.MapEpoch,10)),
                _=>true,out _)==WireFeedStatus.Accepted,
            "skill Ready ACK");
        TrustedSkillUse captured=default;
        byte[] frame=WireProtocol.EncodeSkillUse(
            new(actor.MapEpoch,1,99001,1_001_300_001));
        Require(gateway.FeedClientBytes(frame,_=>true,out var accepted,
                postSkill:value=>{captured=value;return true;})==
                WireFeedStatus.Accepted &&
            accepted==1 && captured.ActorId==actor.ActorId &&
            captured.ConnectionEpoch==actor.ConnectionEpoch &&
            captured.RequestMapEpoch==actor.MapEpoch &&
            captured.SkillSequence==1 && captured.SkillId==99001 &&
            captured.TargetEntityId==1_001_300_001 &&
            captured.Profile==profile,
            "Gateway did not inject trusted skill identity/profile");
    }

    private static void PrepareCommit(string root)
    {
        const int mapId=316;
        string folder=Checks.Fixture(root,mapId,12,12,(_,_)=>0);
        var grid=BlockingGrid.Load(folder,mapId);
        string populationPath=Path.Combine(folder,"population-skill-prepare.json");
        File.WriteAllText(populationPath,JsonSerializer.Serialize(new {
            version=4,kind="verified_monster_runtime",map_id=mapId,
            stats_verified=true,gameplay_ready=true,
            definitions=new[]{new{
                id=1,name="SkillPrepareFixture",sprite="EN001A",
                atlas="Enemy/EN001A/atlas.json",
                stats=new{
                    level=3,max_hp=100,attack_damage=1,move_step=8.0,
                    aggro_radius=1.0,lose_target_radius=2.0,
                    leash_radius=2.0,attack_range=1.0,
                    attack_cooldown_ticks=300,respawn_ticks=300,
                    wander_radius=0.0,wander_interval_ticks=100
                }}},
            spawns=new[]{new{source_index=0,source_flags=7,
                definition=1,p=new[]{40,24}}}
        }));
        var runtime=new MonsterRuntime(
            grid,MonsterPopulationCatalog.LoadVerified(populationPath,mapId,grid),
            new(AiIntervalTicks:1,MaxAiPerStep:4,
                MaxPathQueriesPerStep:1,MaxPathExpandedCells:128));
        var coordinator=new MonsterWorldCoordinator([runtime],
            new Dictionary<int,VerifiedSkillRule>{
                [23103]=new(23103,25,48,3,1)
            });
        var actor=new ActorSnapshot(
            88,mapId,2,1,0,0,24,24,1,false);
        coordinator.Process(new WorldChangedBatch(1,[],
            [new(ActorLifecycleKind.Joined,actor)]));
        var target=runtime.SnapshotAll().Single();
        var request=new TrustedSkillUse(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            1,23103,target.EntityId,
            new(1,1,1,1,48,15,1));

        var prepared=coordinator.PrepareSkill(request);
        Require(prepared.Ready &&
                prepared.Prepared?.Rule.MpCost==1 &&
                prepared.Prepared?.Rule.FixedDamage==25 &&
                runtime.SnapshotAll().Single().CurrentHp==100,
            "skill prepare mutated monster or lost MP metadata");
        var cancelled=coordinator.CancelPreparedSkill(
            prepared.Prepared!.Value,SkillUseStatus.CombatUnavailable);
        Require(cancelled.Status==SkillUseStatus.CombatUnavailable &&
                runtime.SnapshotAll().Single().CurrentHp==100,
            "cancelled prepared skill changed monster");

        var second=coordinator.PrepareSkill(request with {SkillSequence=2});
        Require(second.Ready &&
                runtime.SnapshotAll().Single().CurrentHp==100,
            "second skill prepare mutated monster");
        var applied=coordinator.CommitPreparedSkill(second.Prepared!.Value);
        Require(applied.Status==SkillUseStatus.Applied &&
                applied.Damage==25 && applied.CurrentHp==75 &&
                runtime.SnapshotAll().Single().CurrentHp==75,
            "prepared skill commit did not own exact effect boundary");

        var duplicateCommit=coordinator.CommitPreparedSkill(second.Prepared.Value);
        Require(duplicateCommit.Status==SkillUseStatus.OldMapEpoch &&
                runtime.SnapshotAll().Single().CurrentHp==75,
            "prepared skill token committed twice");
    }

    private static void AreaAuthority(string root)
    {
        const int mapId=317;
        string folder=Checks.Fixture(root,mapId,40,20,(_,_)=>0);
        var grid=BlockingGrid.Load(folder,mapId);
        string populationPath=Path.Combine(folder,"population-skill-area.json");
        File.WriteAllText(populationPath,JsonSerializer.Serialize(new {
            version=4,kind="verified_monster_runtime",map_id=mapId,
            stats_verified=true,gameplay_ready=true,
            definitions=new[]{new{
                id=1,name="AreaFixture",sprite="EN001A",
                atlas="Enemy/EN001A/atlas.json",
                stats=new{
                    level=3,max_hp=100,attack_damage=1,move_step=8.0,
                    aggro_radius=1.0,lose_target_radius=2.0,
                    leash_radius=2.0,attack_range=1.0,
                    attack_cooldown_ticks=300,respawn_ticks=300,
                    wander_radius=0.0,wander_interval_ticks=100
                }}},
            spawns=new[]{
                new{source_index=0,source_flags=7,definition=1,p=new[]{32,24}},
                new{source_index=1,source_flags=7,definition=1,p=new[]{56,24}},
                new{source_index=2,source_flags=7,definition=1,p=new[]{240,24}}
            }
        }));
        var runtime=new MonsterRuntime(
            grid,MonsterPopulationCatalog.LoadVerified(populationPath,mapId,grid),
            new(AiIntervalTicks:1,MaxAiPerStep:8,
                MaxPathQueriesPerStep:1,MaxPathExpandedCells:128));
        var rule=new VerifiedSkillRule(
            99001,40,0,0,1,SkillTargetKind.SelfCenteredArea,64,false);
        var coordinator=new MonsterWorldCoordinator([runtime],
            new Dictionary<int,VerifiedSkillRule>{{99001,rule}});
        var actor=new ActorSnapshot(99,mapId,2,1,0,0,24,24,1,false);
        coordinator.Process(new WorldChangedBatch(1,[],
            [new(ActorLifecycleKind.Joined,actor)]));
        var profile=new TrustedCombatProfile(1,1,1,1,48,15,1);
        var request=new TrustedSkillUse(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            1,99001,0,profile);
        var prepared=coordinator.PrepareSkill(request);
        Require(prepared.Ready &&
                runtime.SnapshotAll().All(x=>x.CurrentHp==100),
            "area preflight changed monsters or rejected target=0");
        var outcome=coordinator.CommitPreparedSkill(prepared.Prepared!.Value);
        var all=runtime.SnapshotAll().OrderBy(x=>x.SourceIndex).ToArray();
        Require(outcome.Status==SkillUseStatus.Applied &&
                outcome.TargetEntityId==0 && outcome.Damage==0 &&
                outcome.CurrentHp==0 && outcome.MaxHp==0 &&
                outcome.TargetRevision==0 &&
                outcome.Hits?.Count==2 &&
                outcome.Hits.All(x=>x.Damage==40) &&
                all[0].CurrentHp==60 && all[1].CurrentHp==60 &&
                all[2].CurrentHp==100,
            "area skill target selection/damage authority mismatch");

        var badTarget=coordinator.ResolveSkill(request with {
            SkillSequence=2,TargetEntityId=all[0].EntityId});
        Require(badTarget.Status==SkillUseStatus.TargetMissing &&
                all[0].CurrentHp==60,
            "self-centered area accepted client-selected target");
    }

    private static void Authority(string root)
    {
        const int mapId=315;
        string folder=Checks.Fixture(root,mapId,12,12,(_,_)=>0);
        var grid=BlockingGrid.Load(folder,mapId);
        string populationPath=Path.Combine(folder,"population-skill.json");
        File.WriteAllText(populationPath,JsonSerializer.Serialize(new {
            version=4,kind="verified_monster_runtime",map_id=mapId,
            stats_verified=true,gameplay_ready=true,
            definitions=new[]{new{
                id=1,name="SkillFixture",sprite="EN001A",
                atlas="Enemy/EN001A/atlas.json",
                stats=new{
                    level=3,max_hp=100,attack_damage=1,move_step=8.0,
                    aggro_radius=1.0,lose_target_radius=2.0,
                    leash_radius=2.0,attack_range=1.0,
                    attack_cooldown_ticks=300,respawn_ticks=300,
                    wander_radius=0.0,wander_interval_ticks=100
                }}},
            spawns=new[]{new{source_index=0,source_flags=7,
                definition=1,p=new[]{40,24}}}
        }));
        var population=MonsterPopulationCatalog.LoadVerified(
            populationPath,mapId,grid);
        var runtime=new MonsterRuntime(grid,population,
            new(AiIntervalTicks:1,MaxAiPerStep:4,
                MaxPathQueriesPerStep:1,MaxPathExpandedCells:128));
        var rules=new Dictionary<int,VerifiedSkillRule>{
            [99001]=new(99001,60,48,3),
            [99002]=new(99002,10,8,1)
        };
        var coordinator=new MonsterWorldCoordinator([runtime],rules);
        var actor=new ActorSnapshot(
            77,mapId,4,5,0,0,24,24,1,false);
        coordinator.Process(new WorldChangedBatch(1,[],
            [new(ActorLifecycleKind.Joined,actor)]));
        var target=runtime.SnapshotAll().Single();
        var profile=new TrustedCombatProfile(
            Attack:1,Defense:1,Hit:1,Dodge:1,
            AttackRange:1,AttackCooldownTicks:100,Level:1);

        var first=coordinator.ResolveSkill(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            1,99001,target.EntityId,profile));
        Require(first.Status==SkillUseStatus.Applied &&
            first.Damage==60 && first.CurrentHp==40 &&
            first.Monster?.CurrentHp==40,
            "verified fixed skill damage not authoritative");

        var replay=coordinator.ResolveSkill(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            1,99001,target.EntityId,profile));
        var cooldown=coordinator.ResolveSkill(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            2,99001,target.EntityId,profile));
        var outOfRange=coordinator.ResolveSkill(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            3,99002,target.EntityId,profile));
        var noRule=coordinator.ResolveSkill(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            4,12345,target.EntityId,profile));
        Require(replay.Status==SkillUseStatus.ReplayedSequence &&
            cooldown.Status==SkillUseStatus.Cooldown &&
            outOfRange.Status==SkillUseStatus.OutOfRange &&
            noRule.Status==SkillUseStatus.NoRule,
            "skill replay/cooldown/range/rule gate");

        coordinator.Process(new WorldChangedBatch(2,[]));
        coordinator.Process(new WorldChangedBatch(3,[]));
        coordinator.Process(new WorldChangedBatch(4,[]));
        var lethal=coordinator.ResolveSkill(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            5,99001,target.EntityId,profile));
        Require(lethal.Status==SkillUseStatus.Applied &&
            lethal.Damage==40 && lethal.CurrentHp==0 &&
            lethal.Monster is { } dead &&
            dead.State==MonsterAiState.Dead,
            "skill death did not reuse Monster authority");
    }
}
