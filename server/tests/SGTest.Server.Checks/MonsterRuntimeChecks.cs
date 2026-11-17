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
        Checks.Run("monster trusted damage owns death/respawn and AI budget is fair across 300 entities",
            () => DamageRespawnAndBudget(root));
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
        var kill=runtime.ApplyTrustedDamage(first.EntityId,99,50,0);
        Checks.Require(kill.Killed && kill.Monster.State==MonsterAiState.Dead &&
                       kill.Monster.CurrentHp==0,
                       "monster death not authoritative");

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
