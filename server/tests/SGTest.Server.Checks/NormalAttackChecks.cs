using SGTest.Server.Visibility;
using SGTest.Server.Monster;
using SGTest.Server.Map;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Combat;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class NormalAttackChecks
{
    private static void Require(bool condition, string message) => Checks.Require(condition, message);

    public static void Verify(string root)
    {
        Checks.Run("normal attack wire has no client actor damage or range fields", WireRoundTrip);
        Checks.Run("normal attack gateway injects trusted actor and combat profile after Ready ACK", GatewayBoundary);
        Checks.Run("normal attack hit gate reproduces Old IsHit_PerHit",HitFormula);
        Checks.Run("normal attack MISS consumes cadence and preserves monster HP",()=>MissAuthority(root));
        Checks.Run("normal attack rejects range cadence dead and cross-map targets without damage",
            ()=>InvalidAttackAuthority(root));
        Checks.Run("normal attack authority owns range cooldown replay HP death and shared AOI",
            () => LivePipeline(root).GetAwaiter().GetResult());
    }

    private static void WireRoundTrip()
    {
        var request = new NormalAttackWire(3, 7, 1_021_400_001);
        var frame = WireProtocol.EncodeNormalAttack(request);
        Require(frame.Length == 28 &&
                WireProtocol.TryDecodeNormalAttack(frame, out var parsed) &&
                parsed == request, "normal attack request roundtrip");
        var result = new NormalAttackResultWire(
            3, 7, NormalAttackStatus.Applied, 42, request.TargetEntityId,
            30, 70, 100, 5);
        var response = WireProtocol.EncodeNormalAttackResult(result);
        Require(response.Length == 57 &&
                WireProtocol.TryDecodeNormalAttackResult(response, out var decoded) &&
                decoded == result, "normal attack result roundtrip");
    }

    private static void GatewayBoundary()
    {
        var actor = new ActorSnapshot(11, 13, 9, 3, 0, 0, 24, 24, 1, false);
        var profile = new TrustedCombatProfile(
            Attack:30, Defense:4, Hit:10, Dodge:3,
            AttackRange:48, AttackCooldownTicks:15, Level:3);
        var attack = WireProtocol.EncodeNormalAttack(
            new(actor.MapEpoch, 1, 1_001_300_001));

        var gated = new GatewayConnection(actor, combatProfile:profile);
        Require(gated.QueueAoi(new(AoiEventKind.WorldReady, 10, actor.MapEpoch, actor)) ==
                GatewaySendStatus.Enqueued &&
                gated.FeedClientBytes(attack, _ => true, out _,
                    postAttack:_ => true) == WireFeedStatus.ProtocolViolation,
            "attack bypassed WorldReady ACK");

        var gateway = new GatewayConnection(actor, combatProfile:profile);
        Require(gateway.QueueAoi(new(AoiEventKind.WorldReady, 10, actor.MapEpoch, actor)) ==
                GatewaySendStatus.Enqueued &&
                gateway.FeedClientBytes(
                    WireProtocol.EncodeWorldReadyAck(new(actor.MapEpoch,10)),
                    _ => true, out _) == WireFeedStatus.Accepted,
            "combat Ready ACK");
        TrustedNormalAttack captured=default;
        Require(gateway.FeedClientBytes(attack, _=>true, out var accepted,
                    postAttack:value => { captured=value; return true; }) ==
                WireFeedStatus.Accepted &&
                accepted==1 &&
                captured.ActorId==actor.ActorId &&
                captured.ConnectionEpoch==actor.ConnectionEpoch &&
                captured.RequestMapEpoch==actor.MapEpoch &&
                captured.AttackSequence==1 &&
                captured.TargetEntityId==1_001_300_001 &&
                captured.Profile==profile,
            "Gateway did not inject server combat identity/profile");
    }

    private static void HitFormula()
    {
        Require(NormalAttackHitRules.HitThreshold(3,10,3,5)==65,
            "Old hit threshold arithmetic drifted");
        Require(NormalAttackHitRules.HitThreshold(1,0,20,100)==30,
            "Old minimum hit threshold drifted");
        Require(NormalAttackHitRules.IsHit(1,0,20,100,30) &&
                !NormalAttackHitRules.IsHit(1,0,20,100,31),
            "Old inclusive >= rand()%100 boundary drifted");
    }

    private static void MissAuthority(string root)
    {
        const int mapId=215;
        string folder=Checks.Fixture(root,mapId,12,12,(_,_)=>0);
        var grid=BlockingGrid.Load(folder,mapId);
        string populationPath=Path.Combine(folder,"population-miss.json");
        File.WriteAllText(populationPath,JsonSerializer.Serialize(new
        {
            version=4,kind="verified_monster_runtime",map_id=mapId,
            stats_verified=true,gameplay_ready=true,
            definitions=new[] { new {
                id=1,name="MissFixture",sprite="EN001A",
                atlas="Enemy/EN001A/atlas.json",
                stats=new {
                    level=3,max_hp=100,attack_damage=6,move_step=8.0,
                    aggro_radius=1.0,lose_target_radius=2.0,
                    leash_radius=2.0,attack_range=1.0,
                    attack_cooldown_ticks=30,respawn_ticks=30,
                    wander_radius=0.0,wander_interval_ticks=100
                }}},
            spawns=new[]{new{source_index=0,source_flags=7,
                definition=1,p=new[]{40,24}}}
        }));
        var runtime=new MonsterRuntime(
            grid,MonsterPopulationCatalog.LoadVerified(populationPath,mapId,grid));
        var hitRule=new CharacterExperienceRewardRule(
            mapId,1,3,5,CharacterExperienceRewardTrust.SyntheticTest,
            "ci:enemy-dr-distinct-from-r-exp",EnemyDodge:100);
        var coordinator=new MonsterWorldCoordinator([runtime],
            normalAttackHitRules:[hitRule],normalAttackRoll:()=>31);
        var actor=new ActorSnapshot(88,mapId,2,1,0,0,24,24,1,false);
        coordinator.Process(new WorldChangedBatch(1,[],
            [new(ActorLifecycleKind.Joined,actor)]));
        var before=runtime.SnapshotAll().Single();
        var profile=new TrustedCombatProfile(
            Attack:60,Defense:4,Hit:10,Dodge:3,
            AttackRange:48,AttackCooldownTicks:3,Level:3);
        var miss=coordinator.ResolveNormalAttack(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            1,before.EntityId,profile));
        var after=runtime.SnapshotAll().Single();
        Require(miss.Status==NormalAttackStatus.Miss&&miss.Damage==0&&
                miss.CurrentHp==100&&miss.TargetRevision==before.EntityRevision&&
                after.CurrentHp==100&&after.EntityRevision==before.EntityRevision,
            "MISS mutated authoritative monster state");
        var cooldown=coordinator.ResolveNormalAttack(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            2,before.EntityId,profile));
        Require(cooldown.Status==NormalAttackStatus.Cooldown,
            "MISS did not consume normal attack cadence");
    }

    private static void InvalidAttackAuthority(string root)
    {
        const int mapA=216;
        const int mapB=217;
        string folderA=Checks.Fixture(root,mapA,16,16,(_,_)=>0);
        string folderB=Checks.Fixture(root,mapB,16,16,(_,_)=>0);
        var gridA=BlockingGrid.Load(folderA,mapA);
        var gridB=BlockingGrid.Load(folderB,mapB);
        string popA=Path.Combine(folderA,"population-invalid-a.json");
        string popB=Path.Combine(folderB,"population-invalid-b.json");
        static object Population(int mapId,int x)=>new
        {
            version=4,kind="verified_monster_runtime",map_id=mapId,
            stats_verified=true,gameplay_ready=true,
            definitions=new[] { new {
                id=1,name="InvalidAttackFixture",sprite="EN001A",
                atlas="Enemy/EN001A/atlas.json",
                stats=new {
                    level=3,max_hp=100,attack_damage=6,move_step=8.0,
                    aggro_radius=1.0,lose_target_radius=2.0,
                    leash_radius=2.0,attack_range=1.0,
                    attack_cooldown_ticks=300,respawn_ticks=300,
                    wander_radius=0.0,wander_interval_ticks=100
                }}},
            spawns=new[]{new{source_index=0,source_flags=7,
                definition=1,p=new[]{x,24}}}
        };
        File.WriteAllText(popA,JsonSerializer.Serialize(Population(mapA,40)));
        File.WriteAllText(popB,JsonSerializer.Serialize(Population(mapB,40)));
        var runtimeA=new MonsterRuntime(
            gridA,MonsterPopulationCatalog.LoadVerified(popA,mapA,gridA));
        var runtimeB=new MonsterRuntime(
            gridB,MonsterPopulationCatalog.LoadVerified(popB,mapB,gridB));
        var hitRules=new[]
        {
            new CharacterExperienceRewardRule(
                mapA,1,3,99,CharacterExperienceRewardTrust.SyntheticTest,
                "ci:invalid-attack-a",EnemyDodge:0),
            new CharacterExperienceRewardRule(
                mapB,1,3,99,CharacterExperienceRewardTrust.SyntheticTest,
                "ci:invalid-attack-b",EnemyDodge:0)
        };
        var coordinator=new MonsterWorldCoordinator(
            [runtimeA,runtimeB],normalAttackHitRules:hitRules,
            normalAttackRoll:()=>0);
        var actor=new ActorSnapshot(91,mapA,5,1,0,0,24,24,1,false);
        coordinator.Process(new WorldChangedBatch(1,[],
            [new(ActorLifecycleKind.Joined,actor)]));
        var targetA=runtimeA.SnapshotAll().Single();
        var targetB=runtimeB.SnapshotAll().Single();

        var shortRange=new TrustedCombatProfile(
            Attack:10,Defense:4,Hit:10,Dodge:3,
            AttackRange:10,AttackCooldownTicks:3,Level:3);
        var before=runtimeA.SnapshotAll().Single();
        var far=coordinator.ResolveNormalAttack(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            1,targetA.EntityId,shortRange));
        var afterFar=runtimeA.SnapshotAll().Single();
        Require(far.Status==NormalAttackStatus.OutOfRange &&
                far.Damage==0 &&
                afterFar.CurrentHp==before.CurrentHp &&
                afterFar.EntityRevision==before.EntityRevision,
            "out-of-range attack mutated authoritative target");

        var profile=shortRange with {AttackRange=48};
        var wrongMap=coordinator.ResolveNormalAttack(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            2,targetB.EntityId,profile));
        var afterWrongMap=runtimeB.SnapshotAll().Single();
        Require(wrongMap.Status==NormalAttackStatus.TargetMissing &&
                wrongMap.Damage==0 &&
                afterWrongMap.CurrentHp==targetB.CurrentHp &&
                afterWrongMap.EntityRevision==targetB.EntityRevision,
            "cross-map target changed authoritative state");

        var first=coordinator.ResolveNormalAttack(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            3,targetA.EntityId,profile));
        Require(first.Status==NormalAttackStatus.Applied && first.Damage==10 &&
                first.CurrentHp==90,"valid control attack did not apply");
        var afterFirst=runtimeA.SnapshotAll().Single();
        var tooFast=coordinator.ResolveNormalAttack(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            4,targetA.EntityId,profile));
        var afterFast=runtimeA.SnapshotAll().Single();
        Require(tooFast.Status==NormalAttackStatus.Cooldown &&
                tooFast.Damage==0 &&
                afterFast.CurrentHp==afterFirst.CurrentHp &&
                afterFast.EntityRevision==afterFirst.EntityRevision,
            "too-fast attack mutated authoritative target");

        var killed=runtimeA.ApplyTrustedDamage(
            targetA.EntityId,actor.ActorId,100,coordinator.LastTick);
        Require(killed.Applied&&killed.Killed,"dead-target fixture kill");
        coordinator.Process(new WorldChangedBatch(4,[],[]));
        var deadBefore=runtimeA.SnapshotAll().Single();
        var dead=coordinator.ResolveNormalAttack(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            5,targetA.EntityId,profile));
        var deadAfter=runtimeA.SnapshotAll().Single();
        Require(dead.Status==NormalAttackStatus.TargetDead &&
                dead.Damage==0 &&
                deadAfter.CurrentHp==0 &&
                deadAfter.EntityRevision==deadBefore.EntityRevision,
            "dead-target attack mutated authoritative target");
    }

    private static async Task LivePipeline(string root)
    {
        const int mapId=214;
        string folder=Checks.Fixture(root,mapId,12,12,(_,_)=>0);
        var grid=BlockingGrid.Load(folder,mapId);
        string populationPath=Path.Combine(folder,"population-combat.json");
        File.WriteAllText(populationPath,JsonSerializer.Serialize(new
        {
            version=4,kind="verified_monster_runtime",map_id=mapId,
            stats_verified=true,gameplay_ready=true,
            definitions=new[]
            {
                new
                {
                    id=1,name="CombatFixture",sprite="EN001A",
                    atlas="Enemy/EN001A/atlas.json",
                    stats=new
                    {
                        level=3,max_hp=100,attack_damage=6,move_step=8.0,
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
        var population=MonsterPopulationCatalog.LoadVerified(populationPath,mapId,grid);
        var runtime=new MonsterRuntime(grid,population,
            new(AiIntervalTicks:1,MaxAiPerStep:4,MaxPathQueriesPerStep:1,
                MaxPathExpandedCells:128));
        var hitRule=new CharacterExperienceRewardRule(
            mapId,1,3,99,CharacterExperienceRewardTrust.SyntheticTest,
            "ci:enemy-dr-distinct-from-r-exp",EnemyDodge:5);
        var coordinator=new MonsterWorldCoordinator([runtime],
            normalAttackHitRules:[hitRule],normalAttackRoll:()=>0);
        using var lootStore=new SqliteIdentityStore(Path.Combine(root,
            "normal-attack-loot-"+Guid.NewGuid().ToString("N")+".db"));
        await lootStore.InitializeAsync();
        long accountId=(await lootStore.CreateAccountAsync(
            "loot_pvp_"+Guid.NewGuid().ToString("N")[..12],"Fixture-Password-123!")).AccountId;
        long characterId=(await lootStore.CreateCharacterAsync(accountId,0,
            "LootFighter",new CharacterSpawn(mapId,24,24))).Character!.CharacterId;
        long generation=await lootStore.AllocateLootWorldGenerationAsync();
        var loot=new MonsterLootSettlement(lootStore,generation,
            [new MonsterDropRule(mapId,1,0,2701,2,
                LootSourceTrust.SyntheticTest,"ci:explicit-synthetic")]);

        await using var host=new ServerWorldHost(
            [grid],options:new WorldHostOptions(PublishIdleTicks:true));
        await host.StartAsync();
        var actor=await host.TrustedRegisterAsync(characterId,mapId,24,24)
            ?? throw new Exception("combat actor register");
        var profile=new TrustedCombatProfile(
            Attack:60,Defense:4,Hit:10,Dodge:3,
            AttackRange:48,AttackCooldownTicks:3,Level:3);
        var gateway=new GatewayConnection(actor,combatProfile:profile);
        var router=new WorldResultRouter(
            new WorldResultRouterOptions(AttackRequestCapacity:8));
        Require(router.TryAttach(gateway),"combat gateway attach");
        var pump=new WorldAoiPump(router,monsters:coordinator,loot:loot);
        using var stop=new CancellationTokenSource();
        var routing=router.RunAsync(host.Results);
        var pumping=pump.RunAsync(stop.Token);
        var looting=loot.RunAsync();
        try
        {
            await WaitFor(gateway,2);
            Require(gateway.TryDequeueReliable(out var ready),
                "combat WorldReady frame");
            Require(WireProtocol.TryDecodeWorldReady(ready,out var readyValue),
                "combat WorldReady decode");
            Require(gateway.TryDequeueReliable(out var spawn),
                "combat MonsterSpawn frame");
            Require(WireProtocol.TryDecodeMonsterState(
                        spawn,out var spawnKind,out var initial) &&
                    spawnKind==WireOpcode.MonsterSpawn && initial.CurrentHp==100,
                "combat MonsterSpawn decode");
            Require(gateway.FeedClientBytes(
                    WireProtocol.EncodeWorldReadyAck(
                        new(actor.MapEpoch,readyValue.ServerTick)),
                    _=>true,out _) == WireFeedStatus.Accepted,
                "combat ACK");

            byte[] first=WireProtocol.EncodeNormalAttack(
                new(actor.MapEpoch,1,initial.EntityId));
            Require(gateway.FeedClientBytes(first,_=>true,out var count,
                    postAttack:router.TryRequestNormalAttack)==WireFeedStatus.Accepted &&
                    count==1,"first attack ingress");
            var firstFrames=await DrainUntilAttack(gateway,1);
            var r=firstFrames.Result ??
                throw new Exception("first attack result missing");
            var d=firstFrames.Monster ??
                throw new Exception("first attack monster delta missing");
            Require(r.Status==NormalAttackStatus.Applied &&
                    r.Damage==60 && r.CurrentHp==40 && r.MaxHp==100 &&
                    d.CurrentHp==40 && d.Revision==r.TargetRevision,
                "first damage/result AOI mismatch");

            Require(gateway.FeedClientBytes(first,_=>true,out _,
                    postAttack:router.TryRequestNormalAttack)==WireFeedStatus.Accepted,
                "replay ingress");
            byte[] second=WireProtocol.EncodeNormalAttack(
                new(actor.MapEpoch,2,initial.EntityId));
            Require(gateway.FeedClientBytes(second,_=>true,out _,
                    postAttack:router.TryRequestNormalAttack)==WireFeedStatus.Accepted,
                "cooldown ingress");
            var statuses=new List<NormalAttackStatus>();
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                while(statuses.Count<2)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    while(gateway.TryDequeueReliable(out var frame))
                        if(WireProtocol.TryDecodeNormalAttackResult(frame,out var value))
                            statuses.Add(value.Status);
                    if(statuses.Count<2) await Task.Delay(5,timeout.Token);
                }
            }
            Require(statuses.Contains(NormalAttackStatus.ReplayedSequence) &&
                    statuses.Contains(NormalAttackStatus.Cooldown),
                "attack replay/cooldown status");

            long waitTick=r.ServerTick+profile.AttackCooldownTicks;
            while(coordinator.LastTick<waitTick)
                await Task.Delay(5);
            byte[] third=WireProtocol.EncodeNormalAttack(
                new(actor.MapEpoch,3,initial.EntityId));
            Require(gateway.FeedClientBytes(third,_=>true,out _,
                    postAttack:router.TryRequestNormalAttack)==WireFeedStatus.Accepted,
                "lethal attack ingress");
            var lethal=await DrainUntilAttack(gateway,3);
            Require(lethal.Result is { } lr &&
                    lr.Status==NormalAttackStatus.Applied &&
                    lr.Damage==40 && lr.CurrentHp==0 &&
                    lethal.Monster is { } dead &&
                    dead.CurrentHp==0 && dead.State==MonsterAiState.Dead,
                "authoritative death did not fan out");
            long lethalRevision = lethal.Result?.TargetRevision ??
                throw new Exception("Lethal result missing death revision");
            // Real authenticated normal attack->WorldAoiPump->bounded
            // settlement->SQLite; the test never seeds drops directly.
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4)))
            {
                while((await lootStore.ListActiveGroundDropsAsync(mapId)).Count!=1)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    await Task.Delay(10,timeout.Token);
                }
            }
            // Ground AOI notification is independent of DB latency. It must
            // reach this authenticated visible client with the persisted UID.
            GroundDropWire? groundWire=null;
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4)))
            {
                while(groundWire is null)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    while(gateway.TryDequeueReliable(out var frame))
                        if(WireProtocol.TryDecodeGroundDropSpawn(frame,out var visible))
                            groundWire=visible;
                    if(groundWire is null) await Task.Delay(10,timeout.Token);
                }
            }
            var drop=(await lootStore.ListActiveGroundDropsAsync(mapId)).Single();
            Require(drop.MonsterEntityId==initial.EntityId &&
                drop.DeathRevision==
                   SqliteIdentityStore.ComposeLootDeathRevision(
                       generation,lethalRevision) &&
                drop.ItemDefinitionId==2701 && drop.Quantity==2 &&
                drop.Trust==LootSourceTrust.SyntheticTest &&
                loot.Statistics.DeathsCommitted==1 &&
                loot.Statistics.DropsCommitted==1 &&
                groundWire is { } seen &&
                seen.DropId==drop.DropId && seen.MapId==mapId &&
                seen.ItemDefinitionId==drop.ItemDefinitionId &&
                seen.Quantity==drop.Quantity,
                "lethal attack failed to publish durable ground UID to authenticated AOI");
            // Authenticated Client intent carries ONLY epoch/seq/drop UID.
            // World supplies live position, DB owns the one-time inventory grant,
            // router removes from AOI only after the committed transaction.
            TrustedGroundPickup trustedPickup=default;
            int pickupCommands=0;
            var pickupRequest=WireProtocol.EncodeGroundPickup(
                new(actor.MapEpoch,1,drop.DropId));
            Require(gateway.FeedClientBytes(pickupRequest,_=>true,out var accepted,
                postPickup:request=>
                {
                    trustedPickup=request;
                    pickupCommands++;
                    return true;
                })==WireFeedStatus.Accepted &&
                accepted==1 && pickupCommands==1,
                "pickup did not enter trusted Gateway boundary");
            ActorSnapshot? current=await host.TrustedGetActorAsync(
                trustedPickup.ActorId);
            Require(current is { } observer &&
                observer.ConnectionEpoch==trustedPickup.ConnectionEpoch &&
                observer.MapEpoch==trustedPickup.RequestMapEpoch,
                "pickup lost trusted live World actor");
            var actorNow=current!.Value;
            var claimed=await lootStore.ClaimGroundDropAsync(new(
                actorNow.ActorId,actorNow.ConnectionEpoch,actorNow.MapEpoch,
                trustedPickup.PickupSequence,actorNow.MapId,
                actorNow.X,actorNow.Y),trustedPickup.DropId);
            Require(claimed.Status==PickupStatus.Granted && claimed.ItemUid>0 &&
                router.QueueGroundPickupResult(trustedPickup,claimed)==
                    GatewaySendStatus.Enqueued &&
                router.TryPostGroundDropClaimed(claimed.DropId),
                "committed pickup result was not routed through shared Server AOI");
            bool gotReceipt=false,gotRemoval=false;
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4)))
            {
                while(!gotReceipt||!gotRemoval)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    while(gateway.TryDequeueReliable(out var pickupFrame))
                    {
                        if(WireProtocol.TryDecodeGroundPickupResult(
                            pickupFrame,out var acceptedPickup))
                            gotReceipt=acceptedPickup.ItemUid==claimed.ItemUid &&
                                acceptedPickup.Status==PickupStatus.Granted;
                        if(WireProtocol.TryDecodeGroundDropDespawn(
                            pickupFrame,out var removed))
                            gotRemoval=removed.DropId==drop.DropId;
                    }
                    if(!gotReceipt||!gotRemoval)
                        await Task.Delay(10,timeout.Token);
                }
            }
            var durableItem=(await lootStore.ListInventoryItemsAsync(characterId)).Single();
            Require(durableItem.ItemUid==claimed.ItemUid &&
                durableItem.SourceDropId==drop.DropId &&
                (await lootStore.ListActiveGroundDropsAsync(mapId)).Count==0 &&
                pump.Aoi.GroundDropCount==0,
                "ground pickup did not atomically grant inventory and remove shared AOI");
        }
        finally
        {
            stop.Cancel();
            await pumping.WaitAsync(TimeSpan.FromSeconds(3));
            await host.StopAsync();
            await routing.WaitAsync(TimeSpan.FromSeconds(3));
            loot.Complete();
            await looting.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private sealed record AttackFrames(
        NormalAttackResultWire? Result, MonsterStateWire? Monster);

    private static async Task<AttackFrames> DrainUntilAttack(
        GatewayConnection gateway,uint sequence)
    {
        NormalAttackResultWire? result=null;
        MonsterStateWire? monster=null;
        static bool Correlates(
            NormalAttackResultWire? attack,MonsterStateWire? state)=>
            attack is { } expected&&state is { } actual&&
            actual.EntityId==expected.TargetEntityId&&
            actual.Revision==expected.TargetRevision;

        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while(!Correlates(result,monster))
        {
            timeout.Token.ThrowIfCancellationRequested();
            while(gateway.TryDequeueReliable(out var frame))
            {
                if(WireProtocol.TryDecodeNormalAttackResult(frame,out var attack) &&
                    attack.AttackSequence==sequence)
                {
                    result=attack;
                    if(monster is { } prior&&
                       (prior.EntityId!=attack.TargetEntityId||
                        prior.Revision!=attack.TargetRevision))
                        monster=null;
                }
                else if(WireProtocol.TryDecodeMonsterState(
                    frame,out var kind,out var state) &&
                    kind==WireOpcode.MonsterDelta&&
                    (result is not { } expected||
                     (state.EntityId==expected.TargetEntityId&&
                      state.Revision==expected.TargetRevision)))
                    monster=state;
            }
            if(!Correlates(result,monster))
                await Task.Delay(5,timeout.Token);
        }
        return new(result,monster);
    }

    private static async Task WaitFor(GatewayConnection gateway,int count)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while(gateway.ReliableFrames<count)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(5,timeout.Token);
        }
    }
}
