using SGTest.Server.World;

namespace SGTest.Server.Gateway;

public sealed record MonsterStateBatch(
    long Tick,
    IReadOnlyList<MonsterSnapshot> Changed,
    IReadOnlyList<MonsterWorldEvent>? Events = null);

public sealed record MonsterWorldCoordinatorStatistics(
    long ProcessedWorldBatches,
    long RuntimeTicks,
    long ChangedSnapshots,
    int TrackedActors,
    int Maps,
    int Monsters);

/// <summary>
/// Single-consumer bridge from authoritative World batches to map-local MonsterRuntime.
/// It keeps only player position/generation snapshots, performs no DB/socket work,
/// and coalesces multiple same-tick AI events for one monster to one final snapshot.
/// Player HP may be supplied by the separate combat-settlement owner; without one,
/// synthetic/unit callers retain the historical targetable=1 fallback. This coordinator
/// owns movement/AI event publication only and never performs SQLite settlement.
/// </summary>
public sealed class MonsterWorldCoordinator
{
    private readonly SortedDictionary<int, MonsterRuntime> _runtimes;
    private sealed class AttackCursor
    {
        public long ConnectionEpoch;
        public long MapEpoch;
        public uint LastSequence;
        public long LastAppliedTick = -1;
    }

    private readonly Dictionary<long, ActorSnapshot> _actors = [];
    private readonly Dictionary<long, AttackCursor> _attack = [];
    private readonly HashSet<int> _seededMaps = [];
    private long _lastTick;
    private long _batches, _runtimeTicks, _changed;

    public MonsterWorldCoordinator(IEnumerable<MonsterRuntime> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        _runtimes = new SortedDictionary<int, MonsterRuntime>();
        foreach (var runtime in runtimes)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            if (!_runtimes.TryAdd(runtime.MapId, runtime))
                throw new ArgumentException(
                    $"Duplicate MonsterRuntime for map {runtime.MapId}", nameof(runtimes));
        }
        if (_runtimes.Count == 0)
            throw new ArgumentException("At least one MonsterRuntime is required", nameof(runtimes));
    }

    public long LastTick => _lastTick;

    public MonsterWorldCoordinatorStatistics Statistics =>
        new(Interlocked.Read(ref _batches), Interlocked.Read(ref _runtimeTicks),
            Interlocked.Read(ref _changed), _actors.Count, _runtimes.Count,
            _runtimes.Values.Sum(x => x.Count));

    public MonsterStateBatch Process(
        WorldChangedBatch batch,
        Func<ActorSnapshot, int>? currentHp = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Tick <= _lastTick)
            throw new InvalidOperationException(
                "Monster coordinator requires strictly ordered World batches");
        _lastTick = batch.Tick;

        if (batch.Lifecycle is not null)
        {
            foreach (var lifecycle in batch.Lifecycle)
            {
                if (lifecycle.Kind == ActorLifecycleKind.Joined)
                    Join(lifecycle.Actor);
                else if (lifecycle.Kind == ActorLifecycleKind.Left)
                    Leave(lifecycle.Actor);
                else
                    throw new InvalidOperationException("Unknown actor lifecycle");
            }
        }

        foreach (var actor in batch.Changed)
            Change(actor);

        var latest = new Dictionary<long, MonsterSnapshot>();
        var combatEvents = new List<MonsterWorldEvent>();
        foreach (var pair in _runtimes)
        {
            var runtime = pair.Value;

            // First observation publishes the current map baseline even if this
            // map has no player yet. AOI stores it spatially; no wire is sent
            // until an observer is actually in range.
            if (_seededMaps.Add(pair.Key))
                foreach (var monster in runtime.SnapshotAll())
                    latest[monster.EntityId] = monster;

            var targets = _actors.Values
                .Where(x => x.MapId == pair.Key)
                .OrderBy(x => x.ActorId)
                .Select(x => new MonsterCombatTarget(
                    x.ActorId, x.MapId, x.X, x.Y,
                    CurrentHp: Math.Max(0, currentHp?.Invoke(x) ?? 1)))
                .ToArray();
            if (targets.Length == 0)
                continue;

            var events = runtime.Tick(batch.Tick, targets);
            Interlocked.Increment(ref _runtimeTicks);
            foreach (var evt in events)
            {
                latest[evt.Monster.EntityId] = evt.Monster;
                combatEvents.Add(evt);
            }
        }

        var changed = latest.Values.OrderBy(x => x.EntityId).ToArray();
        Interlocked.Increment(ref _batches);
        Interlocked.Add(ref _changed, changed.Length);
        return new MonsterStateBatch(batch.Tick, changed, combatEvents.ToArray());
    }

    public bool TryGetActor(long actorId, out ActorSnapshot actor) =>
        _actors.TryGetValue(actorId, out actor);

    public NormalAttackOutcome ResolveNormalAttack(TrustedNormalAttack request)
    {
        if (_lastTick <= 0 || !_actors.TryGetValue(request.ActorId, out var actor) ||
            !_attack.TryGetValue(request.ActorId, out var cursor) ||
            actor.ConnectionEpoch != request.ConnectionEpoch ||
            cursor.ConnectionEpoch != request.ConnectionEpoch)
            return Empty(request, NormalAttackStatus.OldMapEpoch);

        if (request.RequestMapEpoch != actor.MapEpoch ||
            cursor.MapEpoch != actor.MapEpoch)
            return Empty(request, NormalAttackStatus.OldMapEpoch);

        if (request.AttackSequence <= cursor.LastSequence)
            return Empty(request, NormalAttackStatus.ReplayedSequence);
        cursor.LastSequence = request.AttackSequence;

        if (request.Profile is not { } profile ||
            !_runtimes.TryGetValue(actor.MapId, out var runtime))
            return Empty(request, NormalAttackStatus.CombatUnavailable);

        if (cursor.LastAppliedTick >= 0 &&
            _lastTick - cursor.LastAppliedTick < profile.AttackCooldownTicks)
            return Empty(request, NormalAttackStatus.Cooldown);

        var status = runtime.ValidateNormalAttackTarget(
            request.TargetEntityId, actor.X, actor.Y, profile.AttackRange,
            out var target);
        if (status != NormalAttackStatus.Applied)
            return FromTarget(request, status, target);

        cursor.LastAppliedTick = _lastTick;
        int damage = Math.Max(1, profile.Attack);
        var applied = runtime.ApplyTrustedDamage(
            request.TargetEntityId, request.ActorId, damage, _lastTick);
        if (!applied.Applied)
            return FromTarget(request, NormalAttackStatus.TargetDead, target);

        var changed = applied.Monster;
        return new(NormalAttackStatus.Applied, _lastTick, request.TargetEntityId,
            applied.Damage, changed.CurrentHp, changed.MaxHp,
            changed.EntityRevision, changed);
    }

    private NormalAttackOutcome Empty(
        TrustedNormalAttack request, NormalAttackStatus status) =>
        new(status, Math.Max(0, _lastTick), request.TargetEntityId, 0, 0, 0, 0);

    private NormalAttackOutcome FromTarget(
        TrustedNormalAttack request, NormalAttackStatus status, MonsterSnapshot target)
    {
        if (target.EntityId <= 0)
            return Empty(request, status);
        return new(status, Math.Max(0, _lastTick), request.TargetEntityId, 0,
            target.CurrentHp, target.MaxHp, target.EntityRevision, target);
    }

    private void Join(ActorSnapshot actor)
    {
        ValidateActor(actor);
        if (_actors.ContainsKey(actor.ActorId))
            throw new InvalidOperationException(
                "Monster coordinator received Join without prior Leave");
        _actors.Add(actor.ActorId, actor);
        _attack.Add(actor.ActorId, new AttackCursor
        {
            ConnectionEpoch = actor.ConnectionEpoch,
            MapEpoch = actor.MapEpoch
        });
    }

    private void Leave(ActorSnapshot actor)
    {
        if (!_actors.TryGetValue(actor.ActorId, out var current) ||
            current.ConnectionEpoch != actor.ConnectionEpoch ||
            current.MapEpoch != actor.MapEpoch ||
            current.MapId != actor.MapId)
            throw new InvalidOperationException("Monster coordinator received stale Leave");
        _actors.Remove(actor.ActorId);
        _attack.Remove(actor.ActorId);
    }

    private void Change(ActorSnapshot actor)
    {
        ValidateActor(actor);
        if (!_actors.TryGetValue(actor.ActorId, out var current))
            throw new InvalidOperationException("Monster coordinator delta before Join");
        if (current.ConnectionEpoch != actor.ConnectionEpoch ||
            current.MapEpoch != actor.MapEpoch || current.MapId != actor.MapId ||
            actor.EntityRevision < current.EntityRevision)
            throw new InvalidOperationException("Monster coordinator actor generation regressed");
        if (actor.EntityRevision > current.EntityRevision)
            _actors[actor.ActorId] = actor;
    }

    private static void ValidateActor(ActorSnapshot actor)
    {
        if (actor.ActorId <= 0 || actor.ConnectionEpoch <= 0 ||
            actor.MapEpoch <= 0 || actor.MapId <= 0 || actor.EntityRevision <= 0 ||
            !double.IsFinite(actor.X) || !double.IsFinite(actor.Y) ||
            actor.X < 0 || actor.Y < 0)
            throw new InvalidOperationException(
                "Invalid authoritative target snapshot for Monster coordinator");
    }
}
