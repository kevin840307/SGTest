using SGTest.Server.Monster;
using SGTest.Server.Map;
using System.Text.Json;
using SGTest.Server.World;

internal static class MonsterRuntimeChecks
{
    public static void Verify(string root)
    {
        Checks.Run("monster runtime rejects unverified content and blocked spawn",
            () => PopulationGate(root));
        Checks.Run("monster single-map authority chases around Blocking and attacks on bounded cadence",
            () => ChaseAndAttack(root));
        Checks.Run("monster loses live target when actor transfers to another map",
            () => CrossMapTargetLost(root));
        Checks.Run("monster returns to spawn before reacquiring a nearby player after target loss",
            () => TargetLostReturnsHome(root));
        Checks.Run("monster only publishes wandering animation when actually moving",
            () => WanderOnlyWhenMoving(root));
        Checks.Run("monster only publishes chasing animation when a legal step moves it",
            () => ChaseOnlyWhenMoving(root));
        Checks.Run("monster trusted damage owns death/respawn and AI budget is fair across 300 entities",
            () => DamageRespawnAndBudget(root));
        Checks.Run("monster respawn deadline is independent of AI cadence and budget",
            () => RespawnDeadlineIndependentOfAi(root));
    }

    private static void PopulationGate(string root)
    {
        string dir = Checks.Fixture(root, 211, 12, 12,
            (x,y) => (x,y)==(5,5) ? (byte)7 : (byte)0);
        var grid = BlockingGrid.Load(dir,211);
        string path = Path.Combine(dir,"population.json");
        File.WriteAllText(path, JsonSerializer.Serialize(Document(
            211, verified:false, [Spawn(0,1,24,24)], [Definition(1)])));
        MustReject(() => MonsterPopulationCatalog.LoadVerified(path,211,grid),
            "unverified population accepted");

        File.WriteAllText(path, JsonSerializer.Serialize(Document(
            211, verified:true, [Spawn(0,1,88,88)], [Definition(1)])));
        MustReject(() => MonsterPopulationCatalog.LoadVerified(path,211,grid),
            "blocked monster spawn accepted");

        File.WriteAllText(path, JsonSerializer.Serialize(Document(
            211, verified:true, [Spawn(0,1,24,24)], [Definition(1)])));
        var population = MonsterPopulationCatalog.LoadVerified(path,211,grid);
        Checks.Require(population.MapId==211 && population.Spawns.Count==1 &&
            population.Definitions[1].Stats.MaxHp==100,
            "verified population not loaded exactly");
    }

    private static void ChaseAndAttack(string root)
    {
        string dir = Checks.Fixture(root,212,10,10,
            (x,y) => x==4 && y!=6 ? (byte)7 : (byte)0);
        var grid = BlockingGrid.Load(dir,212);
        string path = Path.Combine(dir,"population.json");
        File.WriteAllText(path, JsonSerializer.Serialize(Document(
            212,true,[Spawn(0,1,24,24)],[Definition(1,
                moveStep:16,aggro:300,lose:500,leash:500,attackRange:28,
                attackCooldown:3,wanderRadius:0,wanderInterval:100)])));
        var population = MonsterPopulationCatalog.LoadVerified(path,212,grid);
        var runtime = new MonsterRuntime(grid,population,
            new(AiIntervalTicks:1,MaxAiPerStep:8,MaxPathQueriesPerStep:2,
                MaxPathExpandedCells:512));
        var target = new MonsterCombatTarget(10,212,136,24,1000);
        var previous = runtime.SnapshotAll()[0];
        bool crossedWall=false, attacked=false, acquired=false;
        for(long tick=1;tick<=80;tick++)
        {
            var events=runtime.Tick(tick,[target]);
            acquired |= events.Any(e=>e.Kind==MonsterWorldEventKind.TargetAcquired);
            attacked |= events.Any(e=>e.Kind==MonsterWorldEventKind.Attack &&
                                     e.TargetActorId==10 && e.Damage==6);
            var current=runtime.SnapshotAll()[0];
            Checks.Require(grid.TryWorldCell(current.X,current.Y,out var cell) &&
                           grid.IsWalkable(cell),
                           "monster entered blocked cell");
            if (current.X!=previous.X || current.Y!=previous.Y)
                Checks.Require(grid.IsDirectClear(previous.X,previous.Y,current.X,current.Y),
                    "monster tunneled through Blocking");
            if (current.X>72) crossedWall=true;
            previous=current;
            if(attacked) break;
        }
        Checks.Require(acquired && crossedWall && attacked,
            "monster did not path through door and attack target");

        var before=runtime.SnapshotAll()[0];
        var lostEvents=runtime.Tick(81,[target with { X=900, Y=900 }]);
        var after=runtime.SnapshotAll()[0];
        Checks.Require(lostEvents.Any(e=>e.Kind==MonsterWorldEventKind.TargetLost) &&
                       after.TargetActorId==0 &&
                       after.EntityRevision>before.EntityRevision,
                       "monster did not leave combat when target exceeded lose radius");
    }

    private static void CrossMapTargetLost(string root)
    {
        string dir = Checks.Fixture(root, 214, 12, 12, (_, _) => 0);
        var grid = BlockingGrid.Load(dir, 214);
        string path = Path.Combine(dir, "population.json");
        File.WriteAllText(path, JsonSerializer.Serialize(Document(
            214, true, [Spawn(0, 1, 24, 24)], [Definition(1,
                aggro: 200, lose: 250, leash: 250, attackRange: 28,
                attackCooldown: 1, wanderRadius: 0, wanderInterval: 100)])));
        var population = MonsterPopulationCatalog.LoadVerified(path, 214, grid);
        var runtime = new MonsterRuntime(grid, population,
            new(AiIntervalTicks: 1, MaxAiPerStep: 8, MaxPathQueriesPerStep: 2));
        var nearbyPlayer = new MonsterCombatTarget(99, 214, 40, 24, 100);
        var acquired = runtime.Tick(1, [nearbyPlayer]);
        Checks.Require(acquired.Any(e => e.Kind == MonsterWorldEventKind.TargetAcquired) &&
                       acquired.Any(e => e.Kind == MonsterWorldEventKind.Attack &&
                                         e.TargetActorId == nearbyPlayer.ActorId),
            "same-map live target was not acquired and attacked");

        // Same actor, same coordinates, still alive; only the trusted MapId changes.
        // The old attack target must be dropped before any cooldown/attack processing.
        var transferred = runtime.Tick(2, [nearbyPlayer with { MapId = 215 }]);
        var current = runtime.SnapshotAll()[0];
        Checks.Require(transferred.Any(e => e.Kind == MonsterWorldEventKind.TargetLost &&
                                             e.TargetActorId == nearbyPlayer.ActorId) &&
                       transferred.All(e => e.Kind != MonsterWorldEventKind.Attack) &&
                       current.TargetActorId == 0 &&
                       current.State != MonsterAiState.Attack,
            "monster attacked or retained the player across a map transfer");

        var returned = runtime.Tick(3, [nearbyPlayer]);
        Checks.Require(returned.Any(e => e.Kind == MonsterWorldEventKind.TargetAcquired) &&
                       runtime.SnapshotAll()[0].TargetActorId == nearbyPlayer.ActorId,
            "returning to the same map did not acquire a fresh target");
    }

    private static void TargetLostReturnsHome(string root)
    {
        string dir = Checks.Fixture(root, 217, 12, 12,
            (x, y) => x == 4 && y != 6 ? (byte)7 : (byte)0);
        var grid = BlockingGrid.Load(dir, 217);
        string path = Path.Combine(dir, "population.json");
        File.WriteAllText(path, JsonSerializer.Serialize(Document(
            217, true, [Spawn(0, 1, 24, 24)], [Definition(1,
                moveStep: 16, aggro: 300, lose: 500, leash: 500,
                attackRange: 28, attackCooldown: 3,
                wanderRadius: 0, wanderInterval: 100)])));
        var runtime = new MonsterRuntime(grid,
            MonsterPopulationCatalog.LoadVerified(path, 217, grid),
            new(AiIntervalTicks: 1, MaxAiPerStep: 8,
                MaxPathQueriesPerStep: 2, MaxPathExpandedCells: 512));
        var initialTarget = new MonsterCombatTarget(10, 217, 136, 24, 1000);
        long tick = 0;
        bool attacked = false;
        for (; tick < 100 && !attacked;)
        {
            var events = runtime.Tick(++tick, [initialTarget]);
            attacked = events.Any(e => e.Kind == MonsterWorldEventKind.Attack &&
                                       e.TargetActorId == initialTarget.ActorId);
        }
        var pulled = runtime.SnapshotAll()[0];
        Checks.Require(attacked && Math.Sqrt(Math.Pow(pulled.X - pulled.SpawnX, 2) +
                                            Math.Pow(pulled.Y - pulled.SpawnY, 2)) > 64,
            "monster did not leave spawn and attack past the obstacle");

        // A different nearby player must not cancel the retreat just because
        // the returning monster has reentered its leash boundary.
        var lure = new MonsterCombatTarget(20, 217, 136, 80, 1000);
        var lost = runtime.Tick(++tick,
            [initialTarget with { X = 900, Y = 900 }, lure]);
        Checks.Require(lost.Any(e => e.Kind == MonsterWorldEventKind.TargetLost &&
                                     e.TargetActorId == initialTarget.ActorId) &&
                       lost.All(e => e.Kind != MonsterWorldEventKind.TargetAcquired &&
                                     e.Kind != MonsterWorldEventKind.Attack),
            "lost target was immediately replaced instead of returning home");
        var previous = runtime.SnapshotAll()[0];
        bool reachedHome = false;
        for (int i = 0; i < 100 && !reachedHome; i++)
        {
            var events = runtime.Tick(++tick, [lure]);
            var current = runtime.SnapshotAll()[0];
            Checks.Require(current.TargetActorId == 0 &&
                           events.All(e => e.Kind != MonsterWorldEventKind.TargetAcquired &&
                                           e.Kind != MonsterWorldEventKind.Attack),
                "monster reacquired or attacked a player during its return");
            Checks.Require(grid.IsDirectClear(previous.X, previous.Y, current.X, current.Y),
                "return-to-spawn movement clipped through Blocking");
            reachedHome = Math.Sqrt(Math.Pow(current.X - current.SpawnX, 2) +
                                    Math.Pow(current.Y - current.SpawnY, 2)) < 0.001;
            if (reachedHome)
                Checks.Require(current.State == MonsterAiState.Idle,
                    "monster failed to enter idle state at its original spawn");
            previous = current;
        }
        Checks.Require(reachedHome, "lost monster never returned to its original spawn");
        var reacquire = runtime.Tick(++tick, [lure]);
        Checks.Require(reacquire.Any(e => e.Kind == MonsterWorldEventKind.TargetAcquired &&
                                          e.TargetActorId == lure.ActorId),
            "monster did not resume normal aggro after reaching spawn");
    }

    private static void ChaseOnlyWhenMoving(string root)
    {
        string dir = Checks.Fixture(root, 220, 12, 12,
            (x, y) => x == 4 && y != 6 ? (byte)7 : (byte)0);
        var grid = BlockingGrid.Load(dir, 220);
        string path = Path.Combine(dir, "population.json");
        File.WriteAllText(path, JsonSerializer.Serialize(Document(
            220, true, [Spawn(0, 1, 24, 24)], [Definition(1,
                moveStep: 12, aggro: 240, lose: 420, leash: 420,
                attackRange: 28, wanderRadius: 0, wanderInterval: 100)])));
        var runtime = new MonsterRuntime(grid,
            MonsterPopulationCatalog.LoadVerified(path, 220, grid),
            new(AiIntervalTicks: 1, MaxAiPerStep: 8,
                MaxPathQueriesPerStep: 0));
        var blockedTarget = new MonsterCombatTarget(42, 220, 136, 24, 100);
        for (long tick = 1; tick <= 2; tick++)
        {
            var events = runtime.Tick(tick, [blockedTarget]);
            var current = runtime.SnapshotAll()[0];
            Checks.Require(current.TargetActorId == 42 &&
                           current.X == 24 && current.Y == 24 &&
                           current.State == MonsterAiState.Idle &&
                           events.All(e => e.Kind != MonsterWorldEventKind.Move &&
                                           !(e.Kind == MonsterWorldEventKind.State &&
                                             e.Monster.State == MonsterAiState.Chase)) &&
                           events.Where(e => e.Kind == MonsterWorldEventKind.TargetAcquired)
                               .All(e => e.Monster.State == MonsterAiState.Idle),
                "blocked chase made a stationary monster play a running animation");
        }
        var nearby = blockedTarget with { X = 56 };
        var moved = runtime.Tick(3, [nearby]);
        Checks.Require(moved.Any(e => e.Kind == MonsterWorldEventKind.Move) &&
                       runtime.SnapshotAll()[0].State == MonsterAiState.Chase &&
                       runtime.SnapshotAll()[0].X > 24,
            "reachable target did not restore the chase and movement state");
        var stopped = runtime.Tick(4, [blockedTarget]);
        var after = runtime.SnapshotAll()[0];
        Checks.Require(after.TargetActorId == 42 &&
                       after.State == MonsterAiState.Idle &&
                       stopped.All(e => e.Kind != MonsterWorldEventKind.Move),
            "blocked target retained a stale chase animation after moving");
    }

    private static void WanderOnlyWhenMoving(string root)
    {
        // An isolated spawn surrounded by blocked cells may select a distant
        // walkable waypoint, but cannot make a legal step through the ring.
        string trappedDir = Checks.Fixture(root, 218, 12, 12,
            (x, y) => Math.Abs(x - 5) <= 1 && Math.Abs(y - 5) <= 1 &&
                      (x != 5 || y != 5) ? (byte)7 : (byte)0);
        var trappedGrid = BlockingGrid.Load(trappedDir, 218);
        string trappedPath = Path.Combine(trappedDir, "population.json");
        File.WriteAllText(trappedPath, JsonSerializer.Serialize(Document(
            218, true, [Spawn(0, 1, 88, 88)],
            [Definition(1, wanderRadius: 64, wanderInterval: 1,
                moveStep: 16)])));
        var trapped = new MonsterRuntime(trappedGrid,
            MonsterPopulationCatalog.LoadVerified(trappedPath, 218, trappedGrid),
            new(AiIntervalTicks: 1, MaxAiPerStep: 8,
                MaxPathQueriesPerStep: 0));
        for (long tick = 1; tick <= 10; tick++)
        {
            var events = trapped.Tick(tick, []);
            var snapshot = trapped.SnapshotAll()[0];
            Checks.Require(snapshot.X == 88 && snapshot.Y == 88 &&
                           snapshot.State == MonsterAiState.Idle &&
                           events.All(e => e.Kind != MonsterWorldEventKind.Move &&
                                           !(e.Kind == MonsterWorldEventKind.State &&
                                             e.Monster.State == MonsterAiState.Wander)),
                "blocked patrol waypoint made a stationary monster appear to walk");
        }

        string openDir = Checks.Fixture(root, 219, 12, 12, (_, _) => 0);
        var openGrid = BlockingGrid.Load(openDir, 219);
        string openPath = Path.Combine(openDir, "population.json");
        File.WriteAllText(openPath, JsonSerializer.Serialize(Document(
            219, true, [Spawn(0, 1, 88, 88)],
            [Definition(1, wanderRadius: 64, wanderInterval: 6,
                moveStep: 16)])));
        var open = new MonsterRuntime(openGrid,
            MonsterPopulationCatalog.LoadVerified(openPath, 219, openGrid),
            new(AiIntervalTicks: 1, MaxAiPerStep: 8));
        long firstMoveTick = -1;
        for (long tick = 1; tick <= 10; tick++)
        {
            var events = open.Tick(tick, []);
            if (events.Any(e => e.Kind == MonsterWorldEventKind.Move))
            {
                firstMoveTick = tick;
                Checks.Require(open.SnapshotAll()[0].State == MonsterAiState.Wander,
                    "successful patrol move had no wandering state");
                break;
            }
        }
        Checks.Require(firstMoveTick > 0, "open map never produced a patrol step");
        var paused = open.Tick(firstMoveTick + 1, []);
        Checks.Require(open.SnapshotAll()[0].State == MonsterAiState.Idle &&
                       paused.All(e => e.Kind != MonsterWorldEventKind.Move),
            "wandering animation remained active after the patrol step stopped");
    }

    private static void DamageRespawnAndBudget(string root)
    {
        string dir = Checks.Fixture(root,213,40,40,(_,_)=>0);
        var grid = BlockingGrid.Load(dir,213);
        var defs = new[] { Definition(1,moveStep:8,aggro:1200,lose:1600,leash:1600,
            attackRange:24,attackCooldown:5,respawn:3,wanderRadius:0,wanderInterval:100) };
        var spawns = new List<object>();
        for(int i=0;i<300;i++)
        {
            int x=24+(i%20)*16;
            int y=24+(i/20)*16;
            spawns.Add(Spawn(i,1,x,y));
        }
        string path=Path.Combine(dir,"population.json");
        File.WriteAllText(path,JsonSerializer.Serialize(Document(213,true,spawns,defs)));
        var population=MonsterPopulationCatalog.LoadVerified(path,213,grid,maxSpawns:300);
        var runtime=new MonsterRuntime(grid,population,
            new(AiIntervalTicks:1,MaxAiPerStep:64,MaxPathQueriesPerStep:0,
                MaxPathExpandedCells:128));
        var first=runtime.SnapshotAll()[0];
        var hit=runtime.ApplyTrustedDamage(first.EntityId,99,60,0);
        Checks.Require(hit.Applied && !hit.Killed && hit.Monster.CurrentHp==40 &&
                       hit.Monster.TargetActorId==99,
                       "trusted damage did not mutate authoritative HP/aggro");
        var kill=runtime.ApplyTrustedDamage(first.EntityId,100,50,0);
        Checks.Require(kill.Killed && kill.Monster.State==MonsterAiState.Dead &&
                       kill.Monster.CurrentHp==0,
                       "monster death not authoritative");
        var contributions=runtime.DamageContributions(first.EntityId);
        Checks.Require(contributions.Count==2&&
                       contributions[0]==new MonsterDamageContribution(99,60)&&
                       contributions[1]==new MonsterDamageContribution(100,40),
                       "monster damage contribution ledger did not keep actual capped damage");

        var target=new MonsterCombatTarget(99,213,200,200,1000);
        bool respawnObserved=false;
        for(long tick=1;tick<=5;tick++)
        {
            var events=runtime.Tick(tick,[target]);
            respawnObserved |= events.Any(e=>
                e.Kind==MonsterWorldEventKind.Respawn &&
                e.Monster.EntityId==first.EntityId);
        }
        var stats=runtime.Statistics;
        Checks.Require(stats.AiSteps==5 && stats.MonstersProcessed==320 &&
                       stats.MaxProcessedInStep==64 && stats.PathQueries==0,
                       "300-monster AI budget/fair cursor wrong");
        Checks.Require(runtime.SnapshotAll().All(x=>
            x.EntityId==first.EntityId || x.EntityRevision>1),
            "fair cursor failed to process every live monster");

        var revived=runtime.Find(first.EntityId)!.Value;
        Checks.Require(respawnObserved && revived.State!=MonsterAiState.Dead &&
                       revived.CurrentHp==100,
                       "dead monster did not respawn when its bounded AI slot returned");
        Checks.Require(runtime.DamageContributions(first.EntityId).Count==0,
            "respawn did not clear prior-life damage contributors");
    }

    private static void RespawnDeadlineIndependentOfAi(string root)
    {
        string dir = Checks.Fixture(root, 216, 12, 12, (_, _) => 0);
        var grid = BlockingGrid.Load(dir, 216);
        string path = Path.Combine(dir, "population.json");
        File.WriteAllText(path, JsonSerializer.Serialize(Document(
            216, true, [Spawn(0, 1, 24, 24), Spawn(1, 1, 40, 24)],
            [Definition(1, respawn: 3, wanderRadius: 0, wanderInterval: 100)])));
        var runtime = new MonsterRuntime(grid,
            MonsterPopulationCatalog.LoadVerified(path, 216, grid),
            new(AiIntervalTicks: 8, MaxAiPerStep: 1, MaxPathQueriesPerStep: 0));

        var monsters = runtime.SnapshotAll();
        foreach (var monster in monsters)
        {
            var death = runtime.ApplyTrustedDamage(monster.EntityId, 99, 100, 0);
            Checks.Require(death.Killed &&
                           death.Monster.State == MonsterAiState.Dead,
                "setup failed to kill both monsters");
        }

        Checks.Require(runtime.Tick(1, []).Count == 0 &&
                       runtime.Tick(2, []).Count == 0,
            "monster respawned before its scheduled deadline");
        var due = runtime.Tick(3, []);
        Checks.Require(due.Count == 2 &&
                       due.All(e => e.Kind == MonsterWorldEventKind.Respawn &&
                                    e.Tick == 3) &&
                       due.Select(e => e.Monster.EntityId).SequenceEqual(
                           monsters.Select(m => m.EntityId)) &&
                       due.All(e => e.Monster.State == MonsterAiState.Idle &&
                                    e.Monster.CurrentHp == e.Monster.MaxHp &&
                                    e.Monster.TargetActorId == 0) &&
                       monsters.All(m => runtime.DamageContributions(m.EntityId).Count == 0),
            "scheduled respawn waited for round-robin AI or lost ordering/state");
        Checks.Require(runtime.Tick(4, []).Count == 0 &&
                       runtime.Tick(8, []).All(e =>
                           e.Kind != MonsterWorldEventKind.Respawn),
            "one death emitted duplicate respawn events");
    }

    private static object Document(int mapId,bool verified,
        IEnumerable<object> spawns,IEnumerable<object> definitions) => new
    {
        version=verified ? 4 : 3,
        kind=verified ? "verified_monster_runtime" : "visual_spawn_content",
        map_id=mapId,
        stats_verified=verified,
        gameplay_ready=verified,
        definitions,
        spawns
    };

    private static object Definition(int id,int hp=100,int level=3,int damage=6,
        double moveStep=12,double aggro=240,double lose=420,double leash=420,
        double attackRange=28,int attackCooldown=4,int respawn=6,
        double wanderRadius=80,int wanderInterval=20) => new
    {
        id,
        name=$"Enemy{id}",
        sprite=$"EN{id:000}A",
        atlas=$"Enemy/EN{id:000}A/atlas.json",
        stats=new
        {
            level,
            max_hp=hp,
            attack_damage=damage,
            move_step=moveStep,
            aggro_radius=aggro,
            lose_target_radius=lose,
            leash_radius=leash,
            attack_range=attackRange,
            attack_cooldown_ticks=attackCooldown,
            respawn_ticks=respawn,
            wander_radius=wanderRadius,
            wander_interval_ticks=wanderInterval
        }
    };

    private static object Spawn(int index,int definition,int x,int y) => new
    {
        source_index=index,
        source_flags=7,
        definition,
        p=new[]{x,y}
    };

    private static void MustReject(Action action,string message)
    {
        bool rejected=false;
        try { action(); }
        catch(InvalidDataException) { rejected=true; }
        Checks.Require(rejected,message);
    }
}
