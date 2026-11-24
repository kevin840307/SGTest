using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Monster;
using SGTest.Server.Map;
using SGTest.Server.Combat;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class PvpSkillChecks
{
    public static void Verify(string root)
    {
        Checks.Run("PvP player skill manifest requires explicit authoritative LOS",()=>Manifest(root));
        Checks.Run("PvP player skill compact wire and authenticated gateway intent",Wire);
        Checks.Run("monster and player target skills share one sequence cooldown cursor",()=>SharedCursor(root));
        Checks.Run("PvP player skill uses fixed rule damage durable HP and skill AOI",()=>SettlementAndVisual(root).GetAwaiter().GetResult());
    }

    private static void Manifest(string root)
    {
        string file=Path.Combine(root,"pvp-player-skill-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(file,JsonSerializer.Serialize(new{
            kind="verified_skill_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="ci:explicit-synthetic-player-skill",
            rules=new[]{new{
                skill_id=99003,target_kind="single_player",
                damage_kind="fixed",fixed_damage=60,range=48.0,
                line_of_sight=true,cooldown_ticks=3,mp_cost=1
            }}
        }));
        var rules=SkillRuleCatalog.LoadVerified(file);
        Checks.Require(rules[99003]==new VerifiedSkillRule(
            99003,60,48,3,1,SkillTargetKind.SinglePlayer,0,true),
            "single_player verified rule not retained");
        string noLos=file+".no-los";
        File.WriteAllText(noLos,File.ReadAllText(file).Replace(",\"line_of_sight\":true",""));
        bool missing=false;
        try{SkillRuleCatalog.LoadVerified(noLos);}catch(InvalidDataException){missing=true;}
        Checks.Require(missing,"player skill invented missing LOS policy");
        string falseLos=file+".false-los";
        File.WriteAllText(falseLos,File.ReadAllText(file).Replace(
            "\"line_of_sight\":true","\"line_of_sight\":false"));
        bool falseRejected=false;
        try{SkillRuleCatalog.LoadVerified(falseLos);}catch(InvalidDataException){falseRejected=true;}
        Checks.Require(falseRejected,"player PvP skill admitted wall-ignoring rule");
    }

    private static void Wire()
    {
        var request=new PvpSkillWire(3,7,99003,99);
        byte[] frame=WireProtocol.EncodePvpSkill(request);
        Checks.Require(frame.Length==32&&
            WireProtocol.TryDecodePvpSkill(frame,out var decoded)&&decoded==request,
            "PvP skill request wire mismatch");
        var result=new PvpSkillResultWire(
            3,7,SkillUseStatus.Applied,44,99003,99,60,false);
        byte[] resultFrame=WireProtocol.EncodePvpSkillResult(result);
        Checks.Require(resultFrame.Length==46&&
            WireProtocol.TryDecodePvpSkillResult(resultFrame,out var parsed)&&parsed==result,
            "PvP skill result wire mismatch");
        var visual=new PvpSkillVisualWire(
            3,44,88,5,7,99003,99,6,2,60,false,3,6);
        byte[] visualFrame=WireProtocol.EncodePvpSkillVisual(visual);
        Checks.Require(visualFrame.Length==75&&
            WireProtocol.TryDecodePvpSkillVisual(visualFrame,out var pv)&&pv==visual,
            "PvP skill visual wire mismatch");

        var actor=new ActorSnapshot(88,13,5,3,0,0,8,8,1,false,3,false);
        var profile=new TrustedCombatProfile(30,10,10,3,48,15,3);
        var gateway=new GatewayConnection(actor,combatProfile:profile);
        TrustedPvpSkill captured=default;
        Checks.Require(gateway.FeedClientBytes(
            frame,_=>true,out int accepted,
            postPvpSkill:value=>{captured=value;return true;})==WireFeedStatus.Accepted&&
            accepted==1&&captured.ActorId==88&&captured.ConnectionEpoch==5&&
            captured.SkillSequence==7&&captured.SkillId==99003&&
            captured.TargetActorId==99&&captured.Profile==profile,
            "Gateway did not inject authenticated PvP skill identity/profile");
        Checks.Require(gateway.QueuePvpSkillResult(captured,result)==GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var queued)&&
            WireProtocol.TryDecodePvpSkillResult(queued,out var qr)&&qr==result,
            "reliable PvP skill requester result missing");
    }

    private static void SharedCursor(string root)
    {
        string dir=Path.Combine(root,"pvp-skill-cursor-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string map=Checks.Fixture(dir,13,12,12,(_,_)=>0);
        var grid=BlockingGrid.Load(map,13);
        string populationPath=Path.Combine(map,"population-pvp-skill.json");
        File.WriteAllText(populationPath,JsonSerializer.Serialize(new{
            version=4,kind="verified_monster_runtime",map_id=13,
            stats_verified=true,gameplay_ready=true,
            definitions=new[]{new{
                id=1,name="CursorFixture",sprite="EN001A",
                atlas="Enemy/EN001A/atlas.json",
                stats=new{
                    level=1,max_hp=100,attack_damage=1,move_step=8.0,
                    aggro_radius=1.0,lose_target_radius=2.0,leash_radius=2.0,
                    attack_range=1.0,attack_cooldown_ticks=300,respawn_ticks=300,
                    wander_radius=0.0,wander_interval_ticks=100
                }}},
            spawns=new[]{new{source_index=0,source_flags=7,definition=1,p=new[]{40,24}}}
        }));
        var runtime=new MonsterRuntime(grid,
            MonsterPopulationCatalog.LoadVerified(populationPath,13,grid),
            new(AiIntervalTicks:1,MaxAiPerStep:4,MaxPathQueriesPerStep:1,MaxPathExpandedCells:128));
        var rules=new Dictionary<int,VerifiedSkillRule>{
            [99001]=new(99001,10,48,1),
            [99003]=new(99003,60,48,3,1,SkillTargetKind.SinglePlayer,0,true)
        };
        var coordinator=new MonsterWorldCoordinator([runtime],rules);
        var attacker=new ActorSnapshot(501,13,2,1,0,0,24,24,1,false,3,false);
        // Target and caster have independent per-character MapEpoch values;
        // a reconnect or return portal must not disable same-map player skills.
        var victim=new ActorSnapshot(502,13,3,7,0,0,56,24,1,false,6,false);
        coordinator.Process(new WorldChangedBatch(1,[],[
            new(ActorLifecycleKind.Joined,attacker),
            new(ActorLifecycleKind.Joined,victim)]));
        var profile=new TrustedCombatProfile(1,1,1,1,48,15,1);

        var p1=new TrustedSkillUse(
            attacker.ActorId,attacker.ConnectionEpoch,attacker.MapEpoch,
            1,99003,victim.ActorId,profile,SkillTargetKind.SinglePlayer);
        var prepared=coordinator.PrepareSkill(p1);
        Checks.Require(prepared.Ready,"player skill did not reserve shared cursor");
        coordinator.CancelPreparedSkill(prepared.Prepared!.Value,SkillUseStatus.CombatUnavailable);

        var monster=runtime.SnapshotAll().Single();
        var sameSequence=coordinator.PrepareSkill(new(
            attacker.ActorId,attacker.ConnectionEpoch,attacker.MapEpoch,
            1,99001,monster.EntityId,profile));
        Checks.Require(sameSequence.Rejected?.Status==SkillUseStatus.ReplayedSequence,
            "monster skill reused player-skill sequence namespace");

        var p2=coordinator.PrepareSkill(p1 with {SkillSequence=2});
        Checks.Require(p2.Ready,"second player skill preflight rejected");
        var committed=coordinator.CommitPreparedSkill(p2.Prepared!.Value);
        Checks.Require(committed.Status==SkillUseStatus.Applied&&
            committed.TargetEntityId==victim.ActorId&&committed.Damage==0&&
            runtime.SnapshotAll().Single().CurrentHp==100,
            "player skill cursor commit touched Monster HP");
        var cooldown=coordinator.PrepareSkill(p1 with {SkillSequence=3});
        Checks.Require(cooldown.Rejected?.Status==SkillUseStatus.Cooldown,
            "player skill did not share verified cooldown owner");

        // Moving the target to another map must still be rejected, even if
        // their private MapEpoch changes again. Rejoining the caster's map
        // with another distinct epoch must become targetable immediately.
        var otherMap=victim with {MapId=15,MapEpoch=8,EntityRevision=2};
        coordinator.Process(new WorldChangedBatch(10,[],[
            new(ActorLifecycleKind.Left,victim),
            new(ActorLifecycleKind.Joined,otherMap)]));
        var departed=coordinator.PrepareSkill(
            p1 with {SkillSequence=4});
        Checks.Require(departed.Rejected?.Status==SkillUseStatus.TargetMissing,
            "player skill targeted an actor on a different map");

        var returned=victim with {MapEpoch=9,EntityRevision=3};
        coordinator.Process(new WorldChangedBatch(11,[],[
            new(ActorLifecycleKind.Left,otherMap),
            new(ActorLifecycleKind.Joined,returned)]));
        var newGeneration=coordinator.PrepareSkill(
            p1 with {SkillSequence=5});
        Checks.Require(newGeneration.Ready&&
            coordinator.CommitPreparedSkill(newGeneration.Prepared!.Value).Status==
                SkillUseStatus.Applied,
            "same-map player skill rejected a returned target with independent MapEpoch");

        var staleCaster=coordinator.PrepareSkill(p1 with {
            SkillSequence=6,RequestMapEpoch=7});
        Checks.Require(staleCaster.Rejected?.Status==SkillUseStatus.OldMapEpoch,
            "independent target MapEpoch relaxed caster generation validation");
    }

    private static async Task SettlementAndVisual(string root)
    {
        string dir=Path.Combine(root,"pvp-skill-settlement-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var store=new SqliteIdentityStore(Path.Combine(dir,"identity.db"));
        await store.InitializeAsync();
        long account=(await store.CreateAccountAsync(
            "pvp_skill","Pvp-Skill-Password-123!")).AccountId;
        long aid=(await store.CreateCharacterAsync(
            account,0,"SkillCaster",new(13,8,8))).Character!.CharacterId;
        long tid=(await store.CreateCharacterAsync(
            account,1,"SkillVictim",new(13,24,8))).Character!.CharacterId;
        var ap=Profile(100,30,0);
        var tp=Profile(100,1,1000);
        await store.SetCharacterCombatProfileAsync(aid,ap);
        await store.SetCharacterCombatProfileAsync(tid,tp);

        var attacker=new ActorSnapshot(aid,13,2,1,0,0,8,8,1,false,3,false);
        var victim=new ActorSnapshot(tid,13,3,9,0,0,24,8,1,false,6,false);
        var victimGateway=new GatewayConnection(victim);
        var combat=new PlayerCombatSettlement(
            store,new(Capacity:8),victimGateway.QueuePlayerDamage);
        Checks.Require(combat.TryAttach(attacker,ap)&&combat.TryAttach(victim,tp)&&
            combat.TryResume(attacker)&&combat.TryResume(victim),"PvP skill combat attach");
        using var stop=new CancellationTokenSource();
        var worker=combat.RunAsync(stop.Token);

        Checks.Require(combat.TryPostPvpSkillHit(
            victim,attacker,7,99003,60,44,true,out var completion)==
            CombatPostStatus.Accepted&&completion is not null,
            "PvP skill fixed hit not accepted");
        var settled=await completion!.WaitAsync(TimeSpan.FromSeconds(5));
        Checks.Require(settled.SourceKind==PlayerDamageSourceKind.PlayerSkill&&
            settled.Damage==60&&settled.CurrentHp==40&&!settled.Died&&
            settled.AttackerSkillId==99003&&settled.AttackerSkillSequence==7&&
            settled.AttackerAttackSequence==0,
            "PvP skill damage used normal-attack defense formula or lost metadata");
        Checks.Require(victimGateway.TryDequeueReliable(out var damageFrame)&&
            WireProtocol.TryDecodePvpDamage(damageFrame,out var damage)&&
            damage.Damage==60&&damage.CurrentHp==40&&
            damage.AttackerAttackSequence==7,
            "victim did not receive authoritative skill-sourced PvP HP");

        var observer=new ActorSnapshot(900,13,4,1,0,0,40,8,1,false,1,false);
        var aoi=new WorldAoi();
        aoi.Apply(new WorldChangedBatch(50,[],[
            new(ActorLifecycleKind.Joined,attacker),
            new(ActorLifecycleKind.Joined,victim),
            new(ActorLifecycleKind.Joined,observer)]));
        foreach(var actor in new[]{attacker,victim,observer})
            while(aoi.TryDequeue(actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,out _)){}
        int fanout=aoi.EnqueuePlayerPvpSkillVisual(settled);
        Checks.Require(fanout==3,"PvP skill visual did not reach exact dual-interest set");
        foreach(var actor in new[]{attacker,victim,observer})
        {
            Checks.Require(aoi.TryDequeue(
                actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,out var evt)&&
                evt.Kind==AoiEventKind.PvpSkillVisual&&evt.PvpSkill is { } v&&
                v.SkillSequence==7&&v.SkillId==99003&&v.Damage==60&&
                v.VictimActorId==victim.ActorId,
                "PvP skill AOI visual mismatch");
        }

        Checks.Require(aoi.EnqueuePlayerPvpSkillVisual(
                settled with {AttackerMapEpoch=attacker.MapEpoch+1})==0,
            "stale attacker MapEpoch fanned out skill event");
        Checks.Require(aoi.EnqueuePlayerPvpSkillVisual(
                settled with {MapEpoch=victim.MapEpoch+1})==0,
            "stale victim MapEpoch fanned out skill event");
        stop.Cancel();
        try{await worker;}catch(OperationCanceledException){}
    }

    private static CharacterCombatProfileData Profile(int hp,int attack,int defense)=>
        new(CombatProfileTrust.SyntheticTest,"ci:pvp-player-skill",
            0,0,5,5,5,5,1,hp,hp,attack,defense,10,3,48,15,2,2);
}
