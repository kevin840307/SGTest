using SGTest.Server.Gateway;
using SGTest.Server.World;

namespace SGTest.Server.Identity;

public sealed record CharacterPositionCheckpointOptions(
    int IntervalMilliseconds = 5000,
    int MaxTrackedCharacters = 100000);

public sealed record CharacterPositionCheckpointStatistics(
    long ObservedSnapshots,
    long PeriodicFlushes,
    long PeriodicRowsWritten,
    long FinalRowsWritten,
    int TrackedCharacters,
    int DirtyCharacters);

public sealed class CharacterPositionCheckpointException : Exception
{
    public CharacterPositionCheckpointException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Process-local ordering barrier for non-gameplay-critical position checkpoints.
/// Observe() is memory-only and is called off the 30Hz World owner thread by the
/// result router. Periodic SQLite writes are coalesced. FinalizeAsync shares the
/// same write gate and leaves a generation/revision tombstone so an already
/// captured stale checkpoint can never overwrite the final disconnect position.
/// </summary>
public sealed class CharacterPositionCheckpoint
{
    private sealed class Entry
    {
        public ActorSnapshot Latest;
        public bool Dirty;
        public bool Finalized;
        public Entry(ActorSnapshot snapshot, bool dirty)
        {
            Latest = snapshot; Dirty = dirty;
        }
    }

    private readonly SqliteIdentityStore _store;
    private readonly CharacterPositionCheckpointOptions _options;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Dictionary<long, Entry> _entries = [];
    private long _observed, _flushes, _periodicRows, _finalRows;
    private int _started;

    public CharacterPositionCheckpoint(SqliteIdentityStore store,
        CharacterPositionCheckpointOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? new();
        if (_options.IntervalMilliseconds is < 500 or > 60000 ||
            _options.MaxTrackedCharacters is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public CharacterPositionCheckpointStatistics Statistics
    {
        get
        {
            lock (_gate)
                return new(Interlocked.Read(ref _observed), Interlocked.Read(ref _flushes),
                    Interlocked.Read(ref _periodicRows), Interlocked.Read(ref _finalRows),
                    _entries.Count, _entries.Values.Count(x => x.Dirty && !x.Finalized));
        }
    }

    public void Observe(WorldChangedBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (_gate)
        {
            if (batch.Lifecycle is not null)
                foreach (var lifecycle in batch.Lifecycle)
                    if (lifecycle.Kind == ActorLifecycleKind.Joined)
                        ObserveLocked(lifecycle.Actor);
            foreach (var actor in batch.Changed)
                ObserveLocked(actor);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("Position checkpoint pump can start only once");
        try
        {
            while (true)
            {
                await Task.Delay(_options.IntervalMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<ActorSnapshot> captured;
            lock (_gate)
                captured = _entries.Values
                    .Where(x => x.Dirty && !x.Finalized)
                    .Select(x => x.Latest).ToList();
            if (captured.Count == 0) return;

            // Recheck AFTER acquiring the shared writer gate. A final disconnect
            // may have finalized an entry while this flush was waiting.
            lock (_gate)
                captured = captured.Where(x =>
                    _entries.TryGetValue(x.ActorId, out var entry) &&
                    !entry.Finalized && entry.Dirty &&
                    SameVersion(entry.Latest, x)).ToList();
            if (captured.Count == 0) return;

            await _store.UpdateCharacterSpawnsAsync(captured.Select(ToUpdate).ToArray(),
                cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                foreach (var saved in captured)
                    if (_entries.TryGetValue(saved.ActorId, out var entry) &&
                        !entry.Finalized && SameVersion(entry.Latest, saved))
                        entry.Dirty = false;
            }
            Interlocked.Increment(ref _flushes);
            Interlocked.Add(ref _periodicRows, captured.Count);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new CharacterPositionCheckpointException(
                "Periodic authoritative position checkpoint failed", error);
        }
        finally { _writeGate.Release(); }
    }

    public async Task FinalizeAsync(ActorSnapshot final,
        CancellationToken cancellationToken = default)
    {
        Validate(final);
        lock (_gate)
        {
            if (!_entries.TryGetValue(final.ActorId, out var entry))
            {
                EnsureCapacity();
                _entries.Add(final.ActorId, entry = new Entry(final, true));
            }
            else if (Compare(final, entry.Latest) < 0)
                throw new CharacterPositionCheckpointException(
                    "Final position regressed an observed authoritative generation");
            else
            {
                entry.Latest = final;
                entry.Dirty = true;
            }
            entry.Finalized = true; // blocks periodic writes before waiting on DB gate
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(final.ActorId, out var current) ||
                    !current.Finalized || !SameVersion(current.Latest, final))
                    throw new CharacterPositionCheckpointException(
                        "Final position generation changed before persistence");
            }
            await _store.UpdateCharacterSpawnAsync(final.ActorId,
                new(final.MapId, final.X, final.Y), cancellationToken).ConfigureAwait(false);
            lock (_gate)
                if (_entries.TryGetValue(final.ActorId, out var current) &&
                    current.Finalized && SameVersion(current.Latest, final))
                    current.Dirty = false; // retain tombstone until a newer session generation
            Interlocked.Increment(ref _finalRows);
        }
        catch (Exception error) when (error is not OperationCanceledException &&
                                      error is not CharacterPositionCheckpointException)
        {
            throw new CharacterPositionCheckpointException(
                "Final authoritative position persistence failed", error);
        }
        finally { _writeGate.Release(); }
    }

    private void ObserveLocked(ActorSnapshot actor)
    {
        Validate(actor);
        Interlocked.Increment(ref _observed);
        if (!_entries.TryGetValue(actor.ActorId, out var entry))
        {
            EnsureCapacity();
            _entries.Add(actor.ActorId, new Entry(actor, true));
            return;
        }
        int order = Compare(actor, entry.Latest);
        if (order <= 0) return;
        entry.Latest = actor;
        entry.Dirty = true;
        entry.Finalized = false; // strictly newer connection/map/revision starts new live generation
    }

    private void EnsureCapacity()
    {
        if (_entries.Count >= _options.MaxTrackedCharacters)
            throw new CharacterPositionCheckpointException(
                "Position checkpoint tracked-character capacity exceeded");
    }

    private static CharacterPositionUpdate ToUpdate(ActorSnapshot value) =>
        new(value.ActorId, new CharacterSpawn(value.MapId, value.X, value.Y));

    private static bool SameVersion(ActorSnapshot a, ActorSnapshot b) =>
        a.ActorId == b.ActorId && a.ConnectionEpoch == b.ConnectionEpoch &&
        a.MapEpoch == b.MapEpoch && a.EntityRevision == b.EntityRevision &&
        a.MapId == b.MapId && a.X == b.X && a.Y == b.Y;

    private static int Compare(ActorSnapshot a, ActorSnapshot b)
    {
        int value = a.ConnectionEpoch.CompareTo(b.ConnectionEpoch);
        if (value != 0) return value;
        value = a.MapEpoch.CompareTo(b.MapEpoch);
        if (value != 0) return value;
        return a.EntityRevision.CompareTo(b.EntityRevision);
    }

    private static void Validate(ActorSnapshot actor)
    {
        if (actor.ActorId <= 0 || actor.ConnectionEpoch <= 0 || actor.MapEpoch <= 0 ||
            actor.EntityRevision <= 0 || actor.MapId <= 0 ||
            !double.IsFinite(actor.X) || !double.IsFinite(actor.Y) ||
            actor.X < 0 || actor.Y < 0)
            throw new CharacterPositionCheckpointException(
                "Invalid authoritative actor snapshot for persistence");
    }
}
