using SGTest.Server.Monster;
using SGTest.Server.Map;
using SGTest.Server.Combat;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class PlayerCombatSettlementChecks
{
    public static void Verify(string root)
    {
        Checks.Run("player combat baseline and committed damage wire are exact and generation-safe",
            WireContract);
        Checks.Run("profile-only actor without settlement slot is not treated as dead",
            ProfileOnlyActorIsNotDead);
        Checks.Run("committed damage is queued reliably before combat drain completes",
            () => ReliableDelivery(root).GetAwaiter().GetResult());
        Checks.Run("monster hit reservation is nonblocking and emits only after SQLite commit",
            () => CommitBoundary(root).GetAwaiter().GetResult());
        Checks.Run("source-backed monster retaliation uses Old physical hit reduction before durable HP commit",
            () => SourceBackedMonsterDamage(root).GetAwaiter().GetResult());
        Checks.Run("source-backed monster MISS keeps HP durable and does not gap the next damage sequence",
            () => SourceBackedMonsterMiss(root).GetAwaiter().GetResult());
        Checks.Run("projected HP prevents same-tick overkill and death is committed exactly once",
            () => LethalReservation(root).GetAwaiter().GetResult());
        Checks.Run("combat suspend drains before transfer and resumes only on trusted generation",
            () => SuspendTransfer(root).GetAwaiter().GetResult());
        Checks.Run("monster coordinator consumes external HP owner without inventing alive state",
            () => CoordinatorHpOwner(root));
    }


    private static void WireContract()
    {
        var baseline = new PlayerCombatStateWire(3, 82, 100, 3, 5, 7, 9);
        var baselineBytes = WireProtocol.EncodePlayerCombatState(baseline);
        Checks.Require(baselineBytes.Length == WireProtocol.PlayerCombatStateFrameBytes &&
                       WireProtocol.TryDecodePlayerCombatState(
                           baselineBytes, out var parsedBaseline) &&
                       parsedBaseline == baseline &&
                       parsedBaseline.CombatEnabled,
            "player combat baseline wire");
        var vitalsOnly = baseline with { CombatEnabled = false };
        var vitalsBytes = WireProtocol.EncodePlayerCombatState(vitalsOnly);
        Checks.Require(vitalsBytes.Length == WireProtocol.PlayerCombatStateFrameBytes &&
                       WireProtocol.TryDecodePlayerCombatState(
                           vitalsBytes, out var parsedVitals) &&
                       parsedVitals == vitalsOnly &&
                       !parsedVitals.CombatEnabled,
            "profile-only vitals wire");

        var damage = new PlayerDamageWire(
            3, 2, 42, 1_000_000_001, 7,
            18, 64, 100, false);
        var damageBytes = WireProtocol.EncodePlayerDamage(damage);
        Checks.Require(damageBytes.Length == 57 &&
                       WireProtocol.TryDecodePlayerDamage(
                           damageBytes, out var parsedDamage) &&
                       parsedDamage == damage,
            "player damage wire");

        bool rejected=false;
        try
        {
            WireProtocol.EncodePlayerDamage(
                damage with { CurrentHp=0, Died=false });
        }
        catch(ArgumentOutOfRangeException) { rejected=true; }
        Checks.Require(rejected,
            "zero HP without explicit death was accepted");
    }

    private static void ProfileOnlyActorIsNotDead()
    {
        string db=Path.Combine(Path.GetTempPath(),
            "combat-profile-only-"+Guid.NewGuid().ToString("N")+".db");
        try
        {
            using var store=new SqliteIdentityStore(db);
            var settlement=new PlayerCombatSettlement(store,new(Capacity:8));
            var actor=Actor(91,15,7,3);
            Checks.Require(settlement.CurrentHp(actor)==0,
                "legacy CurrentHp sentinel changed for missing combat slot");
            Checks.Require(!settlement.IsIncapacitated(actor),
                "missing combat slot was incorrectly interpreted as death");
            Checks.Require(settlement.IsIncapacitated(actor with { CombatDead=true }),
                "authoritative World CombatDead no longer gates gameplay");
        }
        finally
        {
            // Close SQLite before deleting the isolated regression fixture.
            foreach(var suffix in new[]{"","-wal","-shm"})
                try{File.Delete(db+suffix);}catch{}
        }
    }

    private static async Task ReliableDelivery(string root)
    {
        string db=Path.Combine(root,"combat-delivery-"+Guid.NewGuid().ToString("N")+".db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        var created=await Create(store,"settle_delivery","Settle-Delivery-Password-123!",100,10);
        var actor=Actor(created.CharacterId,13,1,1);
        var gateway=new GatewayConnection(actor);
        var settlement=new PlayerCombatSettlement(
            store,new(Capacity:8),gateway.QueuePlayerDamage);
        Checks.Require(settlement.TryAttach(actor,created.Profile) &&
                       settlement.TryResume(actor),
            "delivery settlement attach/activate");
        Checks.Require(settlement.TryPostMonsterHit(
                actor,Attack(actor,41,20,2))==CombatPostStatus.Accepted,
            "delivery hit enqueue");

        var drain=settlement.SuspendAndDrainAsync(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch);
        Checks.Require(!drain.IsCompleted && gateway.ReliableFrames==0,
            "delivery became visible before worker/commit");

        using var stop=new CancellationTokenSource();
        var worker=settlement.RunAsync(stop.Token);
        await drain.WaitAsync(TimeSpan.FromSeconds(3));
        Checks.Require(gateway.TryDequeueReliable(out var frame) &&
                       WireProtocol.TryDecodePlayerDamage(frame,out var delivered) &&
                       delivered.MapEpoch==actor.MapEpoch &&
                       delivered.DamageSequence==1 &&
                       delivered.AttackerMonsterEntityId==41 &&
                       delivered.Damage==18 &&
                       delivered.CurrentHp==82 &&
                       delivered.MaxHp==100 &&
                       !delivered.Died,
            "committed player damage was not queued before drain completion");
        Checks.Require(!settlement.Results.TryRead(out _),
            "delivery-mode worker duplicated result into unused side channel");

        stop.Cancel();
        await worker.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task CommitBoundary(string root)
    {
        string db=Path.Combine(root,"combat-settle-"+Guid.NewGuid().ToString("N")+".db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        var created=await Create(store,"settle_one","Settle-One-Password-123!",100,10);
        var actor=Actor(created.CharacterId,13,1,1);
        var settlement=new PlayerCombatSettlement(store,new(Capacity:8));
        Checks.Require(settlement.TryAttach(actor,created.Profile) &&
                       settlement.TryResume(actor),"settlement attach/activate");

        var hit=Attack(actor,10,20,revision:2);
        Checks.Require(settlement.TryPostMonsterHit(actor,hit)==CombatPostStatus.Accepted &&
                       settlement.CurrentHp(actor)==82 &&
                       !settlement.Results.TryRead(out _),
            "hit was not only reserved before worker");
        var before=await store.GetCharacterCombatProfileAsync(actor.ActorId);
        Checks.Require(before?.Data.CurrentHp==100,
            "TryPostMonsterHit performed SQLite I/O synchronously");

        using var stop=new CancellationTokenSource();
        var worker=settlement.RunAsync(stop.Token);
        var result=await settlement.Results.ReadAsync(
            new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
        await settlement.SuspendAndDrainAsync(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch);
        Checks.Require(result.Damage==18 && result.CurrentHp==82 &&
                       !result.Died && result.DamageSequence==1,
            "committed result mismatch");
        var persisted=await store.GetCharacterCombatProfileAsync(actor.ActorId);
        Checks.Require(persisted?.Data.CurrentHp==82 &&
                       settlement.Statistics.HitsCommitted==1 &&
                       settlement.Statistics.DamageCommitted==18,
            "damage was not durable before result completion");
        stop.Cancel();
        await worker.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task SourceBackedMonsterDamage(string root)
    {
        string db=Path.Combine(root,"combat-source-monster-"+Guid.NewGuid().ToString("N")+".db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        var created=await Create(store,"settle_source","Settle-Source-Password-123!",100,10);
        var actor=Actor(created.CharacterId,13,1,1);
        var settlement=new PlayerCombatSettlement(
            store,new(Capacity:8),monsterDamageRoll:_=>0,monsterHitRoll:_=>0);
        Checks.Require(settlement.TryAttach(actor,created.Profile)&&
                       settlement.TryResume(actor),
            "source-backed settlement attach/activate");

        var sourceAttack=Attack(actor,12,17,2) with
        {
            AttackerLevel=3,
            AttackerHit=30
        };
        Checks.Require(settlement.TryPostMonsterHit(actor,sourceAttack)==
                       CombatPostStatus.Accepted,
            "source-backed retaliation enqueue");
        int expected=NormalAttackDamageRules.CalculatePhysicalHit(
            17,3,10,1,_=>0);
        Checks.Require(expected==8&&settlement.CurrentHp(actor)==100-expected,
            "source-backed retaliation did not reserve Old physical damage");

        using var stop=new CancellationTokenSource();
        var worker=settlement.RunAsync(stop.Token);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result=await settlement.Results.ReadAsync(timeout.Token);
        await settlement.SuspendAndDrainAsync(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch);
        var persisted=await store.GetCharacterCombatProfileAsync(actor.ActorId);
        Checks.Require(result.Damage==expected&&result.CurrentHp==100-expected&&
                       persisted?.Data.CurrentHp==100-expected,
            "source-backed retaliation was not durably committed with Old damage");
        stop.Cancel();
        await worker.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task SourceBackedMonsterMiss(string root)
    {
        string db=Path.Combine(root,"combat-source-monster-miss-"+Guid.NewGuid().ToString("N")+".db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        var created=await Create(store,"settle_source_miss",
            "Settle-Source-Miss-Password-123!",100,10);
        var actor=Actor(created.CharacterId,13,1,1);
        int hitRollIndex=0;
        int[] hitRolls=[99,0];
        var settlement=new PlayerCombatSettlement(
            store,new(Capacity:8),monsterDamageRoll:_=>0,
            monsterHitRoll:_=>hitRolls[Math.Min(hitRollIndex++,hitRolls.Length-1)]);
        Checks.Require(settlement.TryAttach(actor,created.Profile)&&
                       settlement.TryResume(actor),
            "source-backed MISS settlement attach/activate");

        var sourceAttack=Attack(actor,13,17,2) with
        {
            AttackerLevel=1,
            AttackerHit=0
        };
        Checks.Require(settlement.TryPostMonsterHit(actor,sourceAttack)==
                       CombatPostStatus.Accepted &&
                       settlement.CurrentHp(actor)==100,
            "MISS changed projected HP");
        Checks.Require(settlement.TryPostMonsterHit(actor,sourceAttack with
                       { Monster=sourceAttack.Monster with { EntityRevision=3 } })==
                       CombatPostStatus.Accepted &&
                       settlement.CurrentHp(actor)==92,
            "post-MISS hit did not reserve source-backed damage");

        using var stop=new CancellationTokenSource();
        var worker=settlement.RunAsync(stop.Token);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var miss=await settlement.Results.ReadAsync(timeout.Token);
        var hit=await settlement.Results.ReadAsync(timeout.Token);
        await settlement.SuspendAndDrainAsync(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch);
        var persisted=await store.GetCharacterCombatProfileAsync(actor.ActorId);
        Checks.Require(miss.Status==NormalAttackStatus.Miss &&
                       miss.Damage==0 && miss.DamageSequence==0 &&
                       miss.VisualSequence==1 && miss.CurrentHp==100 &&
                       !miss.Died,
            "source-backed monster MISS result mismatch");
        Checks.Require(hit.Status==NormalAttackStatus.Applied &&
                       hit.Damage==8 && hit.DamageSequence==1 &&
                       hit.VisualSequence==2 && hit.CurrentHp==92 &&
                       persisted?.Data.CurrentHp==92,
            "MISS created a durable sequence gap or mutated HP");
        Checks.Require(settlement.Statistics.HitsAccepted==2 &&
                       settlement.Statistics.HitsCommitted==1 &&
                       settlement.Statistics.DamageCommitted==8,
            "MISS was incorrectly counted as committed damage");
        stop.Cancel();
        await worker.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task LethalReservation(string root)
    {
        string db=Path.Combine(root,"combat-lethal-"+Guid.NewGuid().ToString("N")+".db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        var created=await Create(store,"settle_lethal","Settle-Lethal-Password-123!",20,0);
        var actor=Actor(created.CharacterId,13,1,1);
        var settlement=new PlayerCombatSettlement(store,new(Capacity:8));
        Checks.Require(settlement.TryAttach(actor,created.Profile) &&
                       settlement.TryResume(actor),"lethal attach/activate");

        Checks.Require(settlement.TryPostMonsterHit(actor,Attack(actor,20,15,2))==
                       CombatPostStatus.Accepted &&
                       settlement.TryPostMonsterHit(actor,Attack(actor,20,15,3))==
                       CombatPostStatus.Accepted &&
                       settlement.CurrentHp(actor)==0 &&
                       settlement.IsIncapacitated(actor) &&
                       settlement.TryPostMonsterHit(actor,Attack(actor,20,15,4))==
                       CombatPostStatus.TargetUnavailable,
            "projected HP did not stop post-lethal attack reservation");

        using var stop=new CancellationTokenSource();
        var worker=settlement.RunAsync(stop.Token);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var first=await settlement.Results.ReadAsync(timeout.Token);
        var second=await settlement.Results.ReadAsync(timeout.Token);
        await settlement.SuspendAndDrainAsync(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch);
        Checks.Require(first.DamageSequence==1 && first.Damage==15 &&
                       first.CurrentHp==5 && !first.Died &&
                       second.DamageSequence==2 && second.Damage==5 &&
                       second.CurrentHp==0 && second.Died,
            "ordered lethal reservation/commit mismatch");
        var persisted=await store.GetCharacterCombatProfileAsync(actor.ActorId);
        Checks.Require(persisted?.Data.CurrentHp==0 &&
                       settlement.Statistics.DeathsCommitted==1 &&
                       settlement.Statistics.HitsCommitted==2,
            "lethal damage durability/accounting");
        stop.Cancel();
        await worker.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task SuspendTransfer(string root)
    {
        string db=Path.Combine(root,"combat-transfer-"+Guid.NewGuid().ToString("N")+".db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        var created=await Create(store,"settle_transfer","Settle-Transfer-Password-123!",100,10);
        var actor=Actor(created.CharacterId,15,1,1);
        var settlement=new PlayerCombatSettlement(store,new(Capacity:8));
        Checks.Require(settlement.TryAttach(actor,created.Profile) &&
                       settlement.TryResume(actor),"transfer attach/activate");
        Checks.Require(settlement.TryPostMonsterHit(actor,Attack(actor,30,20,2))==
                       CombatPostStatus.Accepted,"transfer first hit");
        var drain=settlement.SuspendAndDrainAsync(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch);
        Checks.Require(!drain.IsCompleted &&
                       settlement.TryPostMonsterHit(actor,Attack(actor,30,20,3))==
                       CombatPostStatus.TargetUnavailable &&
                       settlement.CurrentHp(actor)==0,
            "suspend did not close AI target admission while pending");

        using var stop=new CancellationTokenSource();
        var worker=settlement.RunAsync(stop.Token);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var committed=await settlement.Results.ReadAsync(timeout.Token);
        await drain.WaitAsync(TimeSpan.FromSeconds(2));
        var transferred=actor with { MapId=13, MapEpoch=2 };
        Checks.Require(committed.CurrentHp==82 &&
                       settlement.TryResume(transferred) &&
                       settlement.CurrentHp(transferred)==82 &&
                       settlement.TryPostMonsterHit(
                           transferred,Attack(transferred,31,20,4))==
                       CombatPostStatus.Accepted,
            "trusted transfer did not resume committed combat timeline");
        var second=await settlement.Results.ReadAsync(timeout.Token);
        await settlement.SuspendAndDrainAsync(
            transferred.ActorId,transferred.ConnectionEpoch,transferred.MapEpoch);
        Checks.Require(second.MapEpoch==2 && second.CurrentHp==64,
            "post-transfer damage used stale map generation");
        stop.Cancel();
        await worker.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static void CoordinatorHpOwner(string root)
    {
        const int mapId=216;
        string folder=Checks.Fixture(root,mapId,8,8,(_,_)=>0);
        var grid=BlockingGrid.Load(folder,mapId);
        string path=Path.Combine(folder,"population.json");
        File.WriteAllText(path,JsonSerializer.Serialize(new
        {
            version=4,kind="verified_monster_runtime",map_id=mapId,
            stats_verified=true,gameplay_ready=true,
            definitions=new[]
            {
                new
                {
                    id=1,name="HpOwner",sprite="EN001A",
                    atlas="Enemy/EN001A/atlas.json",
                    stats=new
                    {
                        level=1,max_hp=100,attack_damage=10,move_step=8.0,
                        aggro_radius=100.0,lose_target_radius=150.0,
                        leash_radius=150.0,attack_range=100.0,
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
        var population=MonsterPopulationCatalog.LoadVerified(path,mapId,grid);
        var runtime=new MonsterRuntime(grid,population,
            new(AiIntervalTicks:1,MaxAiPerStep:4,MaxPathQueriesPerStep:1,
                MaxPathExpandedCells:64));
        var coordinator=new MonsterWorldCoordinator([runtime]);
        var actor=Actor(777,mapId,1,1);
        var dead=coordinator.Process(new WorldChangedBatch(
            1,Array.Empty<ActorSnapshot>(),
            [new ActorLifecycleEvent(ActorLifecycleKind.Joined,actor)]),_=>0);
        Checks.Require(dead.Events is { Count:0 },
            "HP owner said dead but monster emitted combat");
        var alive=coordinator.Process(new WorldChangedBatch(
            2,Array.Empty<ActorSnapshot>()),_=>100);
        Checks.Require(alive.Events?.Any(x=>
                x.Kind==MonsterWorldEventKind.Attack &&
                x.TargetActorId==actor.ActorId &&
                x.Damage==10)==true &&
                coordinator.TryGetActor(actor.ActorId,out var tracked) &&
                tracked==actor,
            "external HP owner did not enable authoritative monster attack event");
    }

    private static async Task<(long CharacterId,CharacterCombatProfileData Profile)> Create(
        SqliteIdentityStore store,string user,string password,int hp,int defense)
    {
        long account=(await store.CreateAccountAsync(user,password)).AccountId;
        long character=(await store.CreateCharacterAsync(account,0,user+"Hero",
            new CharacterSpawn(13,24,24))).Character!.CharacterId;
        var profile=new CharacterCombatProfileData(
            CombatProfileTrust.SyntheticTest,"ci:player-combat-settlement",
            Race:0,Career:0,Strength:5,Dexterity:5,Constitution:5,Wisdom:5,
            Level:1,CurrentHp:hp,MaxHp:hp,Attack:20,Defense:defense,
            Hit:10,Dodge:3,AttackRange:48,AttackCooldownTicks:15);
        await store.SetCharacterCombatProfileAsync(character,profile);
        return (character,profile);
    }

    private static ActorSnapshot Actor(long id,int map,long connection,long mapEpoch) =>
        new(id,map,connection,mapEpoch,0,0,24,24,1,false);

    private static MonsterWorldEvent Attack(
        ActorSnapshot target,long monsterId,int rawDamage,long revision) =>
        new(MonsterWorldEventKind.Attack,10,
            new MonsterSnapshot(
                monsterId,1,0,target.MapId,40,24,40,24,
                100,100,MonsterAiState.Attack,target.ActorId,revision,2),
            target.ActorId,rawDamage);
}
