using SGTest.Server.Map;
using SGTest.Server.World;
namespace SGTest.Server.Monster;

public enum NormalAttackStatus : byte
{
    Applied = 0,
    Miss = 1,
    TargetMissing = 2,
    TargetDead = 3,
    OutOfRange = 4,
    Blocked = 5,
    Cooldown = 6,
    CombatUnavailable = 7,
    OldMapEpoch = 8,
    ReplayedSequence = 9
}

public enum MonsterAiState : byte
{
    Idle,
    Wander,
    Chase,
    Attack,
    Dead
}

public enum MonsterWorldEventKind : byte
{
    TargetAcquired,
    TargetLost,
    Move,
    State,
    Attack,
    Death,
    Respawn
}

public readonly record struct MonsterCombatTarget(
    long ActorId,
    int MapId,
    double X,
    double Y,
    int CurrentHp);

public readonly record struct MonsterSnapshot(
    long EntityId,
    int EnemyId,
    int SourceIndex,
    int MapId,
    double X,
    double Y,
    double SpawnX,
    double SpawnY,
    int CurrentHp,
    int MaxHp,
    MonsterAiState State,
    long TargetActorId,
    long EntityRevision,
    byte Facing);

public readonly record struct MonsterWorldEvent(
    MonsterWorldEventKind Kind,
    long Tick,
    MonsterSnapshot Monster,
    long TargetActorId = 0,
    int Damage = 0);

public readonly record struct MonsterDamageResult(
    bool Applied,
    bool Killed,
    int Damage,
    MonsterSnapshot Monster);

public readonly record struct MonsterDamageContribution(
    long ActorId,
    long AppliedDamage);

public readonly record struct AreaSkillTargetSelection(
    bool Overloaded,
    IReadOnlyList<MonsterSnapshot> Targets);

public sealed record MonsterRuntimeOptions(
    int AiIntervalTicks = 8,
    int MaxAiPerStep = 128,
    int MaxPathQueriesPerStep = 32,
    int MaxPathExpandedCells = 2048);

public sealed record MonsterRuntimeStatistics(
    long AiSteps,
    long MonstersProcessed,
    long PathQueries,
    long Attacks,
    int MaxProcessedInStep,
    int MonsterCount);

/// <summary>
/// One map-local, single-writer monster authority. It never reads sockets, SQLite
/// or Godot state. Player inputs enter only as already-authoritative target snapshots;
/// damage accepted by ApplyTrustedDamage must be computed by a trusted server combat
/// reducer, never supplied by a raw client.
/// </summary>
public sealed class MonsterRuntime
{
    private sealed class Monster
    {
        public required long EntityId;
        public required MonsterDefinition Definition;
        public required MonsterSpawn Spawn;
        public double X;
        public double Y;
        public int CurrentHp;
        public MonsterAiState State;
        public long TargetActorId;
        // Once aggro is lost, finish the return path before passive reacquisition.
        public bool ReturningHome;
        public long Revision = 1;
        public byte Facing = 1;
        public long LastAttackTick = -1;
        public long RespawnAtTick = -1;
        public long LastRespawnTick = -1;
        public long NextWanderTick;
        public uint WanderSequence;
        public readonly Dictionary<long,long> DamageByActor = [];

        public MonsterSnapshot Snapshot(int mapId) => new(
            EntityId, Definition.Id, Spawn.SourceIndex, mapId,
            X, Y, Spawn.X, Spawn.Y, CurrentHp, Definition.Stats.MaxHp,
            State, TargetActorId, Revision, Facing);
    }

    private readonly BlockingGrid _grid;
    private readonly MapNavigator _navigation;
    private readonly MonsterRuntimeOptions _options;
    private readonly Dictionary<long, Monster> _monsters = [];
    // Respawns are gameplay deadlines, not AI work. Order equal deadlines by
    // stable entity ID so a limited AI budget cannot delay or reorder them.
    private readonly PriorityQueue<(long EntityId, long Deadline), (long Tick, long EntityId)> _respawns = new();
    private readonly long[] _order;
    private int _cursor;
    private long _lastWorldTick = -1;
    private long _aiSteps, _processed, _pathQueries, _attacks;
    private int _maxProcessed;

    public int MapId => _grid.MapId;
    public int Count => _order.Length;

    public bool IsDirectClear(double fromX,double fromY,double toX,double toY)
    {
        if(!double.IsFinite(fromX)||!double.IsFinite(fromY)||
           !double.IsFinite(toX)||!double.IsFinite(toY)||
           fromX<0||fromY<0||toX<0||toY<0)
            return false;
        return _grid.IsDirectClear(fromX,fromY,toX,toY);
    }

    public MonsterRuntimeStatistics Statistics =>
        new(_aiSteps, _processed, _pathQueries, _attacks, _maxProcessed, Count);

    public MonsterRuntime(BlockingGrid grid, MonsterPopulation population,
        MonsterRuntimeOptions? options = null)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        ArgumentNullException.ThrowIfNull(population);
        if (population.MapId != grid.MapId)
            throw new ArgumentException("Monster population/map mismatch", nameof(population));
        _options = options ?? new();
        if (_options.AiIntervalTicks is < 1 or > 300 ||
            _options.MaxAiPerStep is < 1 or > 4096 ||
            _options.MaxPathQueriesPerStep is < 0 or > 4096 ||
            _options.MaxPathExpandedCells is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(options));

        _navigation = new MapNavigator(grid);
        foreach (var spawn in population.Spawns)
        {
            var definition = population.Definitions[spawn.DefinitionId];
            long entityId = checked(1_000_000_000L + (long)grid.MapId * 100_000L +
                                    spawn.SourceIndex + 1L);
            if (_monsters.ContainsKey(entityId))
                throw new InvalidOperationException("Monster entity ID collision");
            _monsters.Add(entityId, new Monster
            {
                EntityId = entityId,
                Definition = definition,
                Spawn = spawn,
                X = spawn.X,
                Y = spawn.Y,
                CurrentHp = definition.Stats.MaxHp,
                State = MonsterAiState.Idle,
                NextWanderTick = definition.Stats.AiEnabled
                    ? 1 + PositiveModulo(entityId, definition.Stats.WanderIntervalTicks)
                    : long.MaxValue
            });
        }
        _order = _monsters.Keys.OrderBy(x => x).ToArray();
    }

    public IReadOnlyList<MonsterSnapshot> SnapshotAll() =>
        _order.Select(id => _monsters[id].Snapshot(MapId)).ToArray();

    public MonsterSnapshot? Find(long entityId) =>
        _monsters.TryGetValue(entityId, out var monster)
            ? monster.Snapshot(MapId) : null;

    public IReadOnlyList<MonsterDamageContribution> DamageContributions(long entityId)
    {
        if(!_monsters.TryGetValue(entityId,out var monster))
            return Array.Empty<MonsterDamageContribution>();
        return monster.DamageByActor
            .OrderBy(x=>x.Key)
            .Select(x=>new MonsterDamageContribution(x.Key,x.Value))
            .ToArray();
    }

    // An unobserved map still advances authoritative respawn deadlines,
    // but need not spend AI/path budget on monsters no player can see.
    public IReadOnlyList<MonsterWorldEvent> Tick(
        long worldTick,
        IReadOnlyList<MonsterCombatTarget> targets,
        bool skipAi = false)
    {
        if (worldTick <= _lastWorldTick)
            throw new InvalidOperationException("Monster runtime Tick must be strictly monotonic");
        ArgumentNullException.ThrowIfNull(targets);
        bool aiDue = !skipAi && _order.Length != 0 &&
            worldTick % _options.AiIntervalTicks == 0;
        // Do not advance the clock or publish respawns for an invalid AI input.
        var targetMap = aiDue ? ValidateTargets(targets) : null;
        _lastWorldTick = worldTick;
        if (!aiDue &&
            (!_respawns.TryPeek(out _, out var next) || next.Tick > worldTick))
            return Array.Empty<MonsterWorldEvent>();
        var events = new List<MonsterWorldEvent>();
        ProcessDueRespawns(worldTick, events);
        if (!aiDue)
            return events;

        int count = Math.Min(_options.MaxAiPerStep, _order.Length);
        int pathBudget = _options.MaxPathQueriesPerStep;
        for (int i = 0; i < count; i++)
        {
            int index = (_cursor + i) % _order.Length;
            Process(_monsters[_order[index]], worldTick, targetMap!, ref pathBudget, events);
        }
        _cursor = (_cursor + count) % _order.Length;
        _aiSteps++;
        _processed += count;
        _maxProcessed = Math.Max(_maxProcessed, count);
        return events;
    }

    /// <summary>
    /// Trusted server settlement only. Raw clients may identify a target, but may
    /// never supply this damage value. Death is authoritative and schedules respawn.
    /// </summary>
    public NormalAttackStatus ValidateNormalAttackTarget(
        long entityId, double attackerX, double attackerY, double attackRange,
        out MonsterSnapshot target)
    {
        target = default;
        if (!double.IsFinite(attackerX) || !double.IsFinite(attackerY) ||
            !double.IsFinite(attackRange) || attackerX < 0 || attackerY < 0 ||
            attackRange <= 0 || attackRange > 500)
            throw new ArgumentOutOfRangeException(nameof(attackRange));
        if (!_monsters.TryGetValue(entityId, out var monster))
            return NormalAttackStatus.TargetMissing;
        target = monster.Snapshot(MapId);
        if (monster.State == MonsterAiState.Dead || monster.CurrentHp <= 0)
            return NormalAttackStatus.TargetDead;
        double dx = attackerX - monster.X;
        double dy = attackerY - monster.Y;
        if (dx * dx + dy * dy > attackRange * attackRange)
            return NormalAttackStatus.OutOfRange;
        if (!_grid.IsDirectClear(attackerX, attackerY, monster.X, monster.Y))
            return NormalAttackStatus.Blocked;
        return NormalAttackStatus.Applied;
    }

    public bool TryGetCombatStats(long entityId,out MonsterCombatStats stats)
    {
        if(_monsters.TryGetValue(entityId,out var monster))
        {
            stats=monster.Definition.Stats;
            return true;
        }
        stats=default!;
        return false;
    }

    /// <summary>
    /// Deterministic same-map area selection from authoritative actor position.
    /// Overflow rejects the whole selection instead of silently truncating a
    /// gameplay effect. Optional LOS is an explicit verified rule property.
    /// </summary>
    public AreaSkillTargetSelection SelectAreaSkillTargets(
        double originX,double originY,double radius,bool requireLineOfSight,
        int maxTargets=1024)
    {
        if(!double.IsFinite(originX)||!double.IsFinite(originY)||
            !double.IsFinite(radius)||originX<0||originY<0||
            radius is <1 or >4096 || maxTargets is <1 or >4096)
            throw new ArgumentOutOfRangeException(nameof(radius));
        double squaredRadius=radius*radius;
        var targets=new List<MonsterSnapshot>(
            Math.Min(maxTargets,_monsters.Count));
        foreach(long id in _order)
        {
            var monster=_monsters[id];
            if(monster.State==MonsterAiState.Dead||monster.CurrentHp<=0)
                continue;
            double dx=monster.X-originX;
            double dy=monster.Y-originY;
            if(dx*dx+dy*dy>squaredRadius)continue;
            if(requireLineOfSight &&
                !_grid.IsDirectClear(originX,originY,monster.X,monster.Y))
                continue;
            if(targets.Count==maxTargets)
                return new(true,Array.Empty<MonsterSnapshot>());
            targets.Add(monster.Snapshot(MapId));
        }
        return new(false,targets);
    }

    public MonsterDamageResult ApplyTrustedDamage(
        long entityId, long attackerActorId, int damage, long worldTick)
    {
        if (attackerActorId <= 0 || damage is < 1 or > 100_000_000 ||
            worldTick < 0 || worldTick < _lastWorldTick)
            throw new ArgumentOutOfRangeException(nameof(damage));
        if (!_monsters.TryGetValue(entityId, out var monster) ||
            monster.State == MonsterAiState.Dead)
            return new(false, false, 0, default);

        int applied = Math.Min(damage, monster.CurrentHp);
        monster.CurrentHp -= applied;
        monster.DamageByActor[attackerActorId]=checked(
            monster.DamageByActor.GetValueOrDefault(attackerActorId)+applied);
        monster.Revision = checked(monster.Revision + 1);
        bool killed = monster.CurrentHp == 0;
        if (killed)
        {
            monster.State = MonsterAiState.Dead;
            monster.TargetActorId = 0;
            monster.ReturningHome = false;
            monster.RespawnAtTick = checked(worldTick + monster.Definition.Stats.RespawnTicks);
            _respawns.Enqueue((monster.EntityId, monster.RespawnAtTick),
                (monster.RespawnAtTick, monster.EntityId));
        }
        else if (monster.TargetActorId == 0 && monster.Definition.Stats.AiEnabled)
        {
            monster.TargetActorId = attackerActorId;
            monster.ReturningHome = false;
            // Damage can acquire aggro immediately, but only a successful
            // movement step may publish the Chase/run animation.
            monster.State = MonsterAiState.Idle;
        }
        return new(true, killed, applied, monster.Snapshot(MapId));
    }

    private void ProcessDueRespawns(long tick, List<MonsterWorldEvent> events)
    {
        while (_respawns.TryPeek(out var due, out var priority) &&
               priority.Tick <= tick)
        {
            _respawns.Dequeue();
            var monster = _monsters[due.EntityId];
            // A stale timer must not resurrect a later monster life.
            if (monster.State != MonsterAiState.Dead ||
                monster.RespawnAtTick != due.Deadline)
                continue;
            var stats = monster.Definition.Stats;
            monster.CurrentHp = stats.MaxHp;
            monster.X = monster.Spawn.X;
            monster.Y = monster.Spawn.Y;
            monster.State = MonsterAiState.Idle;
            monster.TargetActorId = 0;
            monster.ReturningHome = false;
            monster.LastAttackTick = -1;
            monster.RespawnAtTick = -1;
            monster.LastRespawnTick = tick;
            monster.DamageByActor.Clear();
            monster.NextWanderTick = checked(tick + stats.WanderIntervalTicks);
            Bump(monster);
            events.Add(new(MonsterWorldEventKind.Respawn, tick,
                monster.Snapshot(MapId)));
        }
    }

    private void Process(Monster monster, long tick,
        IReadOnlyDictionary<long, MonsterCombatTarget> targets,
        ref int pathBudget, List<MonsterWorldEvent> events)
    {
        // Keep the old idle-on-respawn-tick behavior even when that tick
        // happens to be an AI step.
        if (monster.State == MonsterAiState.Dead ||
            monster.LastRespawnTick == tick)
            return;
        var stats = monster.Definition.Stats;
        if (!stats.AiEnabled)
            return;

        MonsterCombatTarget target = default;
        bool hasTarget = monster.TargetActorId > 0 &&
            targets.TryGetValue(monster.TargetActorId, out target) &&
            target.CurrentHp > 0 && target.MapId == MapId;
        double leashDistance = Distance(
            monster.Spawn.X, monster.Spawn.Y, monster.X, monster.Y);

        if (hasTarget)
        {
            double targetDistance = Distance(monster.X, monster.Y, target.X, target.Y);
            if (targetDistance > stats.LoseTargetRadius ||
                leashDistance > stats.LeashRadius)
            {
                long old = monster.TargetActorId;
                monster.TargetActorId = 0;
                monster.ReturningHome = true;
                monster.State = MonsterAiState.Idle;
                Bump(monster);
                events.Add(new(MonsterWorldEventKind.TargetLost, tick,
                    monster.Snapshot(MapId), old));
                hasTarget = false;
            }
        }
        else if (monster.TargetActorId > 0)
        {
            long old = monster.TargetActorId;
            monster.TargetActorId = 0;
            monster.ReturningHome = true;
            monster.State = MonsterAiState.Idle;
            Bump(monster);
            events.Add(new(MonsterWorldEventKind.TargetLost, tick,
                monster.Snapshot(MapId), old));
        }

        // Target loss latches a full return-to-spawn, even inside the leash.
        // Merely stepping back within the leash allowed nearby players to
        // reacquire the monster before it ever got home.
        leashDistance = Distance(
            monster.Spawn.X, monster.Spawn.Y, monster.X, monster.Y);
        if (!hasTarget && (monster.ReturningHome ||
                           leashDistance > stats.LeashRadius))
        {
            monster.ReturningHome = true;
            if (leashDistance > 1e-6)
            {
                if (TryStep(monster, monster.Spawn.X, monster.Spawn.Y, stats.MoveStep,
                            ref pathBudget, out var homeX, out var homeY))
                {
                    SetState(monster, MonsterAiState.Chase, tick, events);
                    Move(monster, homeX, homeY, tick, events);
                }
                else
                    SetState(monster, MonsterAiState.Idle, tick, events);
            }
            if (Distance(monster.Spawn.X, monster.Spawn.Y,
                         monster.X, monster.Y) <= 1e-6)
            {
                monster.ReturningHome = false;
                SetState(monster, MonsterAiState.Idle, tick, events);
            }
            return;
        }

        if (!hasTarget)
        {
            var acquired = NearestTarget(monster, targets.Values, stats.AggroRadius);
            if (acquired is { } value)
            {
                target = value;
                hasTarget = true;
                monster.TargetActorId = value.ActorId;
                // Acquiring a target is not itself movement. Only a legal step
                // may transition to Chase, otherwise the client runs in place.
                monster.State = MonsterAiState.Idle;
                Bump(monster);
                events.Add(new(MonsterWorldEventKind.TargetAcquired, tick,
                    monster.Snapshot(MapId), value.ActorId));
            }
        }

        if (hasTarget)
        {
            double distance = Distance(monster.X, monster.Y, target.X, target.Y);
            bool direct = _grid.IsDirectClear(monster.X, monster.Y, target.X, target.Y);
            if (distance <= stats.AttackRange && direct)
            {
                SetState(monster, MonsterAiState.Attack, tick, events);
                if (monster.LastAttackTick < 0 ||
                    tick - monster.LastAttackTick >= stats.AttackCooldownTicks)
                {
                    monster.LastAttackTick = tick;
                    monster.Facing = Facing(target.X - monster.X, target.Y - monster.Y,
                        monster.Facing);
                    Bump(monster);
                    _attacks++;
                    events.Add(new(MonsterWorldEventKind.Attack, tick,
                        monster.Snapshot(MapId), target.ActorId, stats.AttackDamage));
                }
                return;
            }

            if (TryStep(monster, target.X, target.Y, stats.MoveStep,
                        ref pathBudget, out var nextX, out var nextY))
            {
                SetState(monster, MonsterAiState.Chase, tick, events);
                Move(monster, nextX, nextY, tick, events);
            }
            else
                SetState(monster, MonsterAiState.Idle, tick, events);
            return;
        }

        if (tick < monster.NextWanderTick || stats.WanderRadius <= 0)
        {
            // Wander is a one-step presentation state, not an idle animation
            // between patrol attempts.
            if (monster.State == MonsterAiState.Wander)
                SetState(monster, MonsterAiState.Idle, tick, events);
            return;
        }

        monster.NextWanderTick = checked(tick + stats.WanderIntervalTicks);
        monster.WanderSequence++;
        double angle = Unit01(monster.EntityId, monster.WanderSequence) * Math.PI * 2.0;
        double radius = Math.Min(stats.WanderRadius, Math.Max(stats.MoveStep, stats.MoveStep * 3));
        double desiredX = monster.Spawn.X + Math.Cos(angle) * radius;
        double desiredY = monster.Spawn.Y + Math.Sin(angle) * radius;
        if (!_grid.TryWorldCell(desiredX, desiredY, out var desiredCell) ||
            !_grid.IsWalkable(desiredCell))
        {
            if (monster.State == MonsterAiState.Wander)
                SetState(monster, MonsterAiState.Idle, tick, events);
            return;
        }

        // Never announce a walk if Blocking or path budget prevented movement.
        if (TryStep(monster, desiredX, desiredY, stats.MoveStep,
                    ref pathBudget, out var wanderX, out var wanderY))
        {
            SetState(monster, MonsterAiState.Wander, tick, events);
            Move(monster, wanderX, wanderY, tick, events);
        }
        else if (monster.State == MonsterAiState.Wander)
            SetState(monster, MonsterAiState.Idle, tick, events);
    }

    private bool TryStep(Monster monster, double targetX, double targetY, double step,
        ref int pathBudget, out double nextX, out double nextY)
    {
        nextX = monster.X;
        nextY = monster.Y;
        double desiredX = targetX, desiredY = targetY;

        if (!_grid.IsDirectClear(monster.X, monster.Y, targetX, targetY))
        {
            if (pathBudget <= 0) return false;
            pathBudget--;
            _pathQueries++;
            var path = _navigation.FindPath(monster.X, monster.Y, targetX, targetY,
                _options.MaxPathExpandedCells);
            if (!path.Found || path.Cells.Count < 2) return false;
            var cell = path.Cells[1];
            desiredX = cell.X * BlockingGrid.CellPixels + BlockingGrid.CellPixels / 2.0;
            desiredY = cell.Y * BlockingGrid.CellPixels + BlockingGrid.CellPixels / 2.0;
        }

        double dx = desiredX - monster.X;
        double dy = desiredY - monster.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance <= 1e-9) return false;
        double amount = Math.Min(step, distance);
        nextX = monster.X + dx / distance * amount;
        nextY = monster.Y + dy / distance * amount;
        if (!_grid.TryWorldCell(nextX, nextY, out var cellCheck) ||
            !_grid.IsWalkable(cellCheck) ||
            !_grid.IsDirectClear(monster.X, monster.Y, nextX, nextY))
        {
            nextX = monster.X;
            nextY = monster.Y;
            return false;
        }
        return true;
    }

    private static Dictionary<long, MonsterCombatTarget> ValidateTargets(
        IReadOnlyList<MonsterCombatTarget> targets)
    {
        var result = new Dictionary<long, MonsterCombatTarget>();
        foreach (var target in targets)
        {
            if (target.ActorId <= 0 || target.MapId <= 0 ||
                !double.IsFinite(target.X) || !double.IsFinite(target.Y) ||
                target.X < 0 || target.Y < 0 || target.CurrentHp < 0 ||
                !result.TryAdd(target.ActorId, target))
                throw new InvalidOperationException("Invalid authoritative monster target snapshot");
        }
        return result;
    }

    private MonsterCombatTarget? NearestTarget(Monster monster,
        IEnumerable<MonsterCombatTarget> targets, double radius)
    {
        double bestSquared = radius * radius;
        MonsterCombatTarget? best = null;
        foreach (var target in targets)
        {
            if (target.MapId != MapId || target.CurrentHp <= 0) continue;
            double dx = target.X - monster.X;
            double dy = target.Y - monster.Y;
            double squared = dx * dx + dy * dy;
            if (squared > bestSquared) continue;
            if (best is null || squared < bestSquared - 1e-9 ||
                (Math.Abs(squared - bestSquared) <= 1e-9 &&
                 target.ActorId < best.Value.ActorId))
            {
                bestSquared = squared;
                best = target;
            }
        }
        return best;
    }

    private void Move(Monster monster, double x, double y, long tick,
        List<MonsterWorldEvent> events)
    {
        monster.Facing = Facing(x - monster.X, y - monster.Y, monster.Facing);
        monster.X = x;
        monster.Y = y;
        Bump(monster);
        events.Add(new(MonsterWorldEventKind.Move, tick,
            monster.Snapshot(MapId), monster.TargetActorId));
    }

    private void SetState(Monster monster, MonsterAiState state, long tick,
        List<MonsterWorldEvent> events)
    {
        if (monster.State == state) return;
        monster.State = state;
        Bump(monster);
        events.Add(new(MonsterWorldEventKind.State, tick,
            monster.Snapshot(MapId), monster.TargetActorId));
    }

    private static void Bump(Monster monster) =>
        monster.Revision = checked(monster.Revision + 1);

    private static double Distance(double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static int PositiveModulo(long value, int divisor)
    {
        long result = value % divisor;
        return (int)(result < 0 ? result + divisor : result);
    }

    private static double Unit01(long entityId, uint sequence)
    {
        unchecked
        {
            ulong x = (ulong)entityId ^ ((ulong)sequence << 32) ^ 0x9E3779B97F4A7C15UL;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            x *= 2685821657736338717UL;
            return (x >> 11) * (1.0 / (1UL << 53));
        }
    }

    private static byte Facing(double dx, double dy, byte current)
    {
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return current;
        double degrees = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (degrees < 0) degrees += 360;
        byte[] facing = [6, 7, 8, 1, 2, 3, 4, 5];
        int sector = (int)Math.Floor((degrees + 22.5) / 45.0) & 7;
        return facing[sector];
    }
}
