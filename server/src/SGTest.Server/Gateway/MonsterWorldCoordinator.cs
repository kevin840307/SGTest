using SGTest.Server.World;

namespace SGTest.Server.Gateway;

public sealed record MonsterStateBatch(long Tick, IReadOnlyList<MonsterSnapshot> Changed);

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
/// Player HP is intentionally not invented here: until the combat health owner is
/// integrated, a registered actor is treated as targetable/alive. This coordinator
/// owns movement/AI publication only, not player damage settlement.
/// </summary>
public sealed class MonsterWorldCoordinator
{
    private readonly SortedDictionary<int, MonsterRuntime> _runtimes;
    private readonly Dictionary<long, ActorSnapshot> _actors = [];
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

    public MonsterWorldCoordinatorStatistics Statistics =>
        new(Interlocked.Read(ref _batches), Interlocked.Read(ref _runtimeTicks),
            Interlocked.Read(ref _changed), _actors.Count, _runtimes.Count,
            _runtimes.Values.Sum(x => x.Count));

    public MonsterStateBatch Process(WorldChangedBatch batch)
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
                    x.ActorId, x.MapId, x.X, x.Y, CurrentHp: 1))
                .ToArray();
            if (targets.Length == 0)
                continue;

            var events = runtime.Tick(batch.Tick, targets);
            Interlocked.Increment(ref _runtimeTicks);
            foreach (var evt in events)
                latest[evt.Monster.EntityId] = evt.Monster;
        }

        var changed = latest.Values.OrderBy(x => x.EntityId).ToArray();
        Interlocked.Increment(ref _batches);
        Interlocked.Add(ref _changed, changed.Length);
        return new MonsterStateBatch(batch.Tick, changed);
    }

    private void Join(ActorSnapshot actor)
    {
        ValidateActor(actor);
        if (_actors.ContainsKey(actor.ActorId))
            throw new InvalidOperationException(
                "Monster coordinator received Join without prior Leave");
        _actors.Add(actor.ActorId, actor);
    }

    private void Leave(ActorSnapshot actor)
    {
        if (!_actors.TryGetValue(actor.ActorId, out var current) ||
            current.ConnectionEpoch != actor.ConnectionEpoch ||
            current.MapEpoch != actor.MapEpoch ||
            current.MapId != actor.MapId)
            throw new InvalidOperationException("Monster coordinator received stale Leave");
        _actors.Remove(actor.ActorId);
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
