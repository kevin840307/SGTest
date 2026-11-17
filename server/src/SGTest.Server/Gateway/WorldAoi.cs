using SGTest.Server.World;

namespace SGTest.Server.Gateway;

public sealed record WorldAoiOptions(
    int EnterHalfWidth = 640,
    int EnterHalfHeight = 400,
    int ExitMargin = 96,
    int BucketSize = 256,
    int MaxEntities = 10000,
    int MaxObservers = 256,
    int MaxVisiblePerObserver = 2048,
    int MaxPendingEventsPerObserver = 4096);

public enum AoiEventKind : byte { WorldReady, Spawn, Delta, Despawn, SelfState }

/// <summary>
/// WorldReady includes the trusted viewer snapshot. Spawn/Delta include the
/// authoritative entity snapshot; Despawn identifies the previous entity
/// generation. ReceiverMapEpoch guards transfer/reconnect before wire encoding.
/// </summary>
public readonly record struct AoiEvent(
    AoiEventKind Kind, long Tick, long ReceiverMapEpoch,
    ActorSnapshot? Entity = null, long EntityActorId = 0, long EntityConnectionEpoch = 0);

public sealed class AoiBackpressureException : Exception
{
    public AoiBackpressureException(string message) : base(message) { }
}

/// <summary>
/// Single-writer, transport-independent AOI for authoritative player snapshots.
/// Spatial buckets are per-map; only candidate buckets around a moving viewer
/// are scanned. Each entity update examines observers on its own map, not every
/// entity in the World. No DB, socket, TLS or renderer runs here.
///
/// Call Apply in WorldResultRouter.ChangedBatches order; DO NOT feed unordered
/// events or stale generations. WorldReady/Spawn/Delta/Despawn are retained in
/// bounded per-observer FIFO queues for the next reliable wire delivery slice.
/// </summary>
public sealed class WorldAoi
{
    private readonly WorldAoiOptions _limits;
    private readonly Dictionary<long, ActorSnapshot> _entities = new();
    private readonly Dictionary<(int Map, int X, int Y), HashSet<long>> _buckets = new();
    private readonly Dictionary<long, Observer> _observers = new();
    private long _lastTick;

    private readonly record struct Seen(long ConnectionEpoch, long MapEpoch, long Revision);

    private sealed class Observer
    {
        public ActorSnapshot Self;
        public readonly Dictionary<long, Seen> Visible = new();
        public readonly Queue<AoiEvent> Pending = new();

        public Observer(ActorSnapshot actor) => Self = actor;
    }

    public WorldAoi(WorldAoiOptions? options = null)
    {
        _limits = options ?? new WorldAoiOptions();
        if (_limits.EnterHalfWidth is < 32 or > 4096 ||
            _limits.EnterHalfHeight is < 32 or > 4096 ||
            _limits.ExitMargin is < 0 or > 2048 ||
            _limits.BucketSize is < 64 or > 1024 ||
            _limits.MaxEntities is < 1 or > 100000 ||
            _limits.MaxObservers is < 1 or > 10000 ||
            _limits.MaxVisiblePerObserver is < 1 or > 10000 ||
            _limits.MaxPendingEventsPerObserver is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public int EntityCount => _entities.Count;
    public int ObserverCount => _observers.Count;
    public long LastTick => _lastTick;

    public int PendingEvents(long actorId) =>
        _observers.TryGetValue(actorId, out var observer) ? observer.Pending.Count : 0;

    public bool TryDequeue(long actorId, long connectionEpoch, long mapEpoch, out AoiEvent result)
    {
        result = default;
        if (!_observers.TryGetValue(actorId, out var observer) ||
            observer.Self.ConnectionEpoch != connectionEpoch ||
            observer.Self.MapEpoch != mapEpoch ||
            observer.Pending.Count == 0)
            return false;
        result = observer.Pending.Dequeue();
        return true;
    }

    /// <summary>
    /// Peek/confirm is intentionally split: a reliable AOI event is removed
    /// only AFTER the authenticated Gateway accepted it into bounded egress.
    /// These methods share the same single AOI consumer thread as Apply.
    /// </summary>
    public bool TryPeek(long actorId, long connectionEpoch, long mapEpoch, out AoiEvent result)
    {
        result = default;
        return _observers.TryGetValue(actorId, out var observer) &&
               observer.Self.ConnectionEpoch == connectionEpoch &&
               observer.Self.MapEpoch == mapEpoch &&
               observer.Pending.TryPeek(out result);
    }

    public bool TryConfirmQueued(long actorId, long connectionEpoch, long mapEpoch)
    {
        if (!_observers.TryGetValue(actorId, out var observer) ||
            observer.Self.ConnectionEpoch != connectionEpoch ||
            observer.Self.MapEpoch != mapEpoch ||
            observer.Pending.Count == 0)
            return false;
        observer.Pending.Dequeue();
        return true;
    }

    public bool TryResync(TrustedWorldResyncRequest request)
    {
        if (!_observers.TryGetValue(request.ActorId, out var observer) ||
            !_entities.TryGetValue(request.ActorId, out var current) ||
            observer.Self.ConnectionEpoch != request.ConnectionEpoch ||
            observer.Self.MapEpoch != request.MapEpoch ||
            current.ConnectionEpoch != request.ConnectionEpoch ||
            current.MapEpoch != request.MapEpoch)
            return false;
        observer.Pending.Clear();
        observer.Visible.Clear();
        observer.Self = current;
        Enqueue(observer, new(AoiEventKind.WorldReady, _lastTick, current.MapEpoch, current));
        Enqueue(observer, new(AoiEventKind.SelfState, _lastTick, current.MapEpoch, current));
        RefreshObserver(observer, _lastTick);
        return true;
    }

    public void Apply(WorldChangedBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Tick <= _lastTick)
            throw new InvalidOperationException("AOI batches must be strictly ordered by World Tick");
        _lastTick = batch.Tick;
        if (batch.Lifecycle is not null)
        {
            // Preserve trusted registration/disconnect/transfer ordering, even
            // when several operations occur between two 30Hz ticks.
            foreach (var lifecycle in batch.Lifecycle)
            {
                if (lifecycle.Kind == ActorLifecycleKind.Joined)
                    Joined(lifecycle.Actor, batch.Tick);
                else if (lifecycle.Kind == ActorLifecycleKind.Left)
                    Left(lifecycle.Actor, batch.Tick);
                else
                    throw new InvalidOperationException("Unknown trusted lifecycle event");
            }
        }
        foreach (var changed in batch.Changed)
            Changed(changed, batch.Tick);
    }

    private void Joined(ActorSnapshot actor, long tick)
    {
        ValidateActor(actor);
        if (_entities.ContainsKey(actor.ActorId))
            throw new InvalidOperationException("AOI received Join without preceding trusted Leave");
        if (_entities.Count >= _limits.MaxEntities || _observers.Count >= _limits.MaxObservers)
            throw new AoiBackpressureException("AOI entity/observer capacity exceeded");

        Store(actor);
        foreach (var observer in _observers.Values)
            if (observer.Self.MapId == actor.MapId)
                RefreshEntity(observer, actor, tick);

        var viewer = new Observer(actor);
        _observers.Add(actor.ActorId, viewer);
        Enqueue(viewer, new(AoiEventKind.WorldReady, tick, actor.MapEpoch, actor));
        RefreshObserver(viewer, tick);
    }

    private void Left(ActorSnapshot actor, long tick)
    {
        if (!_entities.TryGetValue(actor.ActorId, out var old) ||
            old.ConnectionEpoch != actor.ConnectionEpoch ||
            old.MapEpoch != actor.MapEpoch ||
            old.MapId != actor.MapId)
            throw new InvalidOperationException("Stale or untracked AOI Leave");

        RemoveBucket(old);
        _entities.Remove(actor.ActorId);
        // After disconnect or a trusted map transfer, the old observer queue is
        // no longer deliverable. New map gets a fresh WorldReady and generation.
        _observers.Remove(actor.ActorId);
        foreach (var observer in _observers.Values)
        {
            if (observer.Visible.Remove(actor.ActorId))
                Enqueue(observer, new(AoiEventKind.Despawn, tick,
                    observer.Self.MapEpoch, null, old.ActorId, old.ConnectionEpoch));
        }
    }

    private void Changed(ActorSnapshot actor, long tick)
    {
        ValidateActor(actor);
        if (!_entities.TryGetValue(actor.ActorId, out var previous))
            throw new InvalidOperationException("AOI delta before trusted Join");
        if (actor.ConnectionEpoch != previous.ConnectionEpoch ||
            actor.MapEpoch != previous.MapEpoch || actor.MapId != previous.MapId)
            throw new InvalidOperationException("AOI map/connection changed without trusted lifecycle");
        if (actor.EntityRevision < previous.EntityRevision)
            throw new InvalidOperationException("AOI revision regressed");
        if (actor.EntityRevision == previous.EntityRevision)
            return; // same-tick replay of identical revision
        RemoveBucket(previous);
        Store(actor);
        foreach (var observer in _observers.Values)
            if (observer.Self.MapId == actor.MapId)
                RefreshEntity(observer, actor, tick);
        if (_observers.TryGetValue(actor.ActorId, out var moving))
        {
            moving.Self = actor;
            Enqueue(moving, new(AoiEventKind.SelfState, tick, actor.MapEpoch, actor));
            RefreshObserver(moving, tick);
        }
    }

    private void RefreshObserver(Observer observer, long tick)
    {
        var viewer = observer.Self;
        foreach (var entry in observer.Visible.ToArray())
        {
            if (!_entities.TryGetValue(entry.Key, out var actor) || !Within(viewer, actor, true))
            {
                observer.Visible.Remove(entry.Key);
                Enqueue(observer, new(AoiEventKind.Despawn, tick,
                    viewer.MapEpoch, null, entry.Key, entry.Value.ConnectionEpoch));
            }
        }

        // Scan only the bounded map-specific spatial buckets surrounding this
        // observer, not the entire entity dictionary each movement tick.
        int minX = Bucket(viewer.X - _limits.EnterHalfWidth);
        int maxX = Bucket(viewer.X + _limits.EnterHalfWidth);
        int minY = Bucket(viewer.Y - _limits.EnterHalfHeight);
        int maxY = Bucket(viewer.Y + _limits.EnterHalfHeight);
        var candidates = new HashSet<long>();
        for (int bx = minX; bx <= maxX; bx++)
            for (int by = minY; by <= maxY; by++)
                if (_buckets.TryGetValue((viewer.MapId, bx, by), out var ids))
                    candidates.UnionWith(ids);
        foreach (long id in candidates.OrderBy(id => id))
            if (_entities.TryGetValue(id, out var actor))
                RefreshEntity(observer, actor, tick);
    }

    private void RefreshEntity(Observer observer, ActorSnapshot actor, long tick)
    {
        if (observer.Self.ActorId == actor.ActorId) return;
        bool visible = observer.Visible.TryGetValue(actor.ActorId, out var seen);
        if (!Within(observer.Self, actor, visible))
        {
            if (visible)
            {
                observer.Visible.Remove(actor.ActorId);
                Enqueue(observer, new(AoiEventKind.Despawn, tick,
                    observer.Self.MapEpoch, null, actor.ActorId, seen.ConnectionEpoch));
            }
            return;
        }
        if (!visible)
        {
            if (observer.Visible.Count >= _limits.MaxVisiblePerObserver)
                throw new AoiBackpressureException("AOI visible entity cap exceeded");
            observer.Visible.Add(actor.ActorId,
                new(actor.ConnectionEpoch, actor.MapEpoch, actor.EntityRevision));
            Enqueue(observer, new(AoiEventKind.Spawn, tick, observer.Self.MapEpoch, actor));
        }
        else if (seen.ConnectionEpoch != actor.ConnectionEpoch || seen.MapEpoch != actor.MapEpoch)
        {
            observer.Visible[actor.ActorId] =
                new(actor.ConnectionEpoch, actor.MapEpoch, actor.EntityRevision);
            Enqueue(observer, new(AoiEventKind.Despawn, tick,
                observer.Self.MapEpoch, null, actor.ActorId, seen.ConnectionEpoch));
            Enqueue(observer, new(AoiEventKind.Spawn, tick, observer.Self.MapEpoch, actor));
        }
        else if (actor.EntityRevision > seen.Revision)
        {
            observer.Visible[actor.ActorId] =
                new(actor.ConnectionEpoch, actor.MapEpoch, actor.EntityRevision);
            Enqueue(observer, new(AoiEventKind.Delta, tick, observer.Self.MapEpoch, actor));
        }
    }

    private bool Within(ActorSnapshot viewer, ActorSnapshot actor, bool alreadyVisible)
    {
        if (viewer.MapId != actor.MapId) return false;
        int margin = alreadyVisible ? _limits.ExitMargin : 0;
        return Math.Abs(viewer.X - actor.X) <= _limits.EnterHalfWidth + margin &&
               Math.Abs(viewer.Y - actor.Y) <= _limits.EnterHalfHeight + margin;
    }

    private static void ValidateActor(ActorSnapshot actor)
    {
        if (actor.ActorId <= 0 || actor.ConnectionEpoch <= 0 || actor.MapEpoch <= 0 ||
            actor.MapId <= 0 || actor.EntityRevision <= 0 || actor.Facing is < 1 or > 8 ||
            !double.IsFinite(actor.X) || !double.IsFinite(actor.Y) ||
            actor.X < 0 || actor.Y < 0)
            throw new InvalidOperationException("Invalid authoritative AOI actor snapshot");
    }

    private int Bucket(double value) => (int)Math.Floor(value / _limits.BucketSize);

    private void Store(ActorSnapshot actor)
    {
        _entities[actor.ActorId] = actor;
        var key = (actor.MapId, Bucket(actor.X), Bucket(actor.Y));
        if (!_buckets.TryGetValue(key, out var ids))
            _buckets.Add(key, ids = new HashSet<long>());
        ids.Add(actor.ActorId);
    }

    private void RemoveBucket(ActorSnapshot actor)
    {
        var key = (actor.MapId, Bucket(actor.X), Bucket(actor.Y));
        if (_buckets.TryGetValue(key, out var ids))
        {
            ids.Remove(actor.ActorId);
            if (ids.Count == 0) _buckets.Remove(key);
        }
    }

    private void Enqueue(Observer observer, AoiEvent value)
    {
        if (observer.Pending.Count >= _limits.MaxPendingEventsPerObserver)
            throw new AoiBackpressureException("AOI reliable event backlog exceeded");
        observer.Pending.Enqueue(value);
    }
}
