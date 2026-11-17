using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class PlayerCombatSettlementChecks
{
    public static void Verify(string root)
    {
        Checks.Run("monster hit reservation is nonblocking and emits only after SQLite commit",
            () => CommitBoundary(root).GetAwaiter().GetResult());
        Checks.Run("projected HP prevents same-tick overkill and death is committed exactly once",
            () => LethalReservation(root).GetAwaiter().GetResult());
        Checks.Run("combat suspend drains before transfer and resumes only on trusted generation",
            () => SuspendTransfer(root).GetAwaiter().GetResult());
        Checks.Run("monster coordinator consumes external HP owner without inventing alive state",
            () => CoordinatorHpOwner(root));
    }

    private static async Task CommitBoundary(string root)
    {
        string db=Path.Combine(root,"combat-settle-"+Guid.NewGuid().ToString("N")+".db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        var created=await Create(store,"settle_one","Settle-One-Password-123!",100,10);
        var actor=Actor(created.CharacterId,13,1,1);
        var settlement=new PlayerCombatSettlement(store,new(Capacity:8));
        Checks.Require(settlement.TryAttach(actor,created.Profile),"settlement attach");

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

    private static async Task LethalReservation(string root)
    {
        string db=Path.Combine(root,"combat-lethal-"+Guid.NewGuid().ToString("N")+".db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        var created=await Create(store,"settle_lethal","Settle-Lethal-Password-123!",20,0);
        var actor=Actor(created.CharacterId,13,1,1);
        var settlement=new PlayerCombatSettlement(store,new(Capacity:8));
        Checks.Require(settlement.TryAttach(actor,created.Profile),"lethal attach");

        Checks.Require(settlement.TryPostMonsterHit(actor,Attack(actor,20,15,2))==
                       CombatPostStatus.Accepted &&
                       settlement.TryPostMonsterHit(actor,Attack(actor,20,15,3))==
                       CombatPostStatus.Accepted &&
                       settlement.CurrentHp(actor)==0 &&
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
        Checks.Require(settlement.TryAttach(actor,created.Profile),"transfer attach");
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
