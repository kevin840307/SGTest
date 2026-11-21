using SGTest.Server.Visibility;
using SGTest.Server.Monster;
using SGTest.Server.Map;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class NormalAttackChecks
{
    private static void Require(bool condition, string message) => Checks.Require(condition, message);

    public static void Verify(string root)
    {
        Checks.Run("normal attack wire has no client actor damage or range fields", WireRoundTrip);
        Checks.Run("normal attack gateway injects trusted actor and combat profile after Ready ACK", GatewayBoundary);
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
            AttackRange:48, AttackCooldownTicks:15);
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
        var coordinator=new MonsterWorldCoordinator([runtime]);
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
            AttackRange:48,AttackCooldownTicks:3);
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
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while(result is null || monster is null)
        {
            timeout.Token.ThrowIfCancellationRequested();
            while(gateway.TryDequeueReliable(out var frame))
            {
                if(WireProtocol.TryDecodeNormalAttackResult(frame,out var attack) &&
                    attack.AttackSequence==sequence)
                    result=attack;
                else if(WireProtocol.TryDecodeMonsterState(
                    frame,out var kind,out var state) &&
                    kind==WireOpcode.MonsterDelta)
                    monster=state;
            }
            if(result is null || monster is null)
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
