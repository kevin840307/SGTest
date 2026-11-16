using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SGTest.Server.World;

/// <summary>
/// Policy limits only. MovementPixelsPerTick=8 is a TEST BASELINE, NOT verified legacy speed.
/// Tick() is advanced explicitly by a future host; no socket, sleep, render FPS or database.
/// </summary>
public sealed record WorldLimits(
    int MaxActors = 128,
    int MaxKnownActorIds = 4096,
    int MaxPendingCommands = 512,
    int MaxCommandsPerTick = 64,
    int MaxPathRequestsPerTick = 8,
    int MaxPathExpandedCells = 2048,
    double MovementPixelsPerTick = 8.0);

public readonly record struct MoveTargetCommand(
    long ActorId, long ConnectionEpoch, long MapEpoch,
    uint MovementSequence, double TargetX, double TargetY);

public enum MoveReceiptStatus
{
    Accepted,
    UnknownActor,
    OldConnectionEpoch,
    OldMapEpoch,
    ReplayedSequence,
    InvalidTarget,
    TargetBlocked,
    PathBudgetExhausted,
    PathNotFound,
    TickRouteBudgetExhausted,
    RouteBecameBlocked
}

public readonly record struct MoveReceipt(
    long ActorId, long ConnectionEpoch, long MapEpoch,
    uint MovementSequence, MoveReceiptStatus Status, long Tick);

public readonly record struct ActorSnapshot(
    long ActorId, int MapId, long ConnectionEpoch, long MapEpoch,
    uint LastMovementSequence, uint MovementActionSequence, double X, double Y,
    long EntityRevision, bool IsMoving);

public enum ActorLifecycleKind { Joined, Left }
public readonly record struct ActorLifecycleEvent(ActorLifecycleKind Kind, ActorSnapshot Actor);

public sealed record WorldTickResult(
    long Tick, IReadOnlyList<MoveReceipt> Receipts, IReadOnlyList<ActorSnapshot> Changed,
    IReadOnlyList<ActorLifecycleEvent>? Lifecycle = null);

/// <summary>
/// Single-writer authoritative state, with one bounded MPSC command ingress.
/// Trusted session/transfer methods MUST be called only after upstream auth and
/// server-side portal authorization; they must never be wired to raw client commands.
/// All mutable world state is owned by one thread. TryPostMove is the ONLY
/// thread-safe operation. The owner advances Tick, not the client render loop.
/// </summary>
public sealed class ServerWorld
{
    private sealed class Actor
    {
        public required long Id;
        public required int Map;
        public required long ConnectionEpoch;
        public required long MapEpoch;
        public required double X;
        public required double Y;
        public uint LastSequence; // highest client command sequence consumed
        public uint MovementActionSequence; // last accepted authoritative movement/stop
        public long Revision = 1;
        public IReadOnlyList<GridCell> Route = Array.Empty<GridCell>();
        public int RouteIndex;

        public ActorSnapshot Snapshot() => new(Id, Map, ConnectionEpoch, MapEpoch,
            LastSequence, MovementActionSequence, X, Y, Revision, RouteIndex < Route.Count);

        public void ClearRoute()
        {
            Route = Array.Empty<GridCell>();
            RouteIndex = 0;
        }
    }

    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly WorldLimits _limits;
    private readonly Dictionary<int, BlockingGrid> _maps;
    private readonly Dictionary<int, MapNavigator> _paths;
    private readonly Dictionary<long, Actor> _actors = new();
    // Tombstones retained until process restart; bounded to MaxKnownActorIds.
    private readonly Dictionary<long, (long Connection, long Map)> _epochs = new();
    private readonly ConcurrentQueue<MoveTargetCommand> _incoming = new();
    // Trusted registration, disconnect and transfer are ordered on this writer.
    // They must reach AOI even when the actor never sends a MoveTarget.
    private readonly List<ActorLifecycleEvent> _lifecycle = new();
    private int _reserved;
    private long _tick;

    public long TickIndex { get { EnsureOwner(); return _tick; } }
    public int ActorCount { get { EnsureOwner(); return _actors.Count; } }
    public int PendingCommands => Volatile.Read(ref _reserved);

    public ServerWorld(IEnumerable<BlockingGrid> originalMaps, WorldLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(originalMaps);
        _limits = limits ?? new WorldLimits();
        if (_limits.MaxActors is < 1 or > 10000 ||
            _limits.MaxKnownActorIds < _limits.MaxActors || _limits.MaxKnownActorIds > 100000 ||
            _limits.MaxPendingCommands is < 1 or > 65536 ||
            _limits.MaxCommandsPerTick is < 1 or > 65536 ||
            _limits.MaxPathRequestsPerTick is < 1 or > 65536 ||
            _limits.MaxPathExpandedCells is < 1 or > 65536 ||
            !double.IsFinite(_limits.MovementPixelsPerTick) ||
            _limits.MovementPixelsPerTick <= 0 || _limits.MovementPixelsPerTick > 16)
            throw new ArgumentOutOfRangeException(nameof(limits), "World limits exceed safe bounds");
        _maps = new Dictionary<int, BlockingGrid>();
        foreach (var map in originalMaps)
        {
            ArgumentNullException.ThrowIfNull(map);
            if (!_maps.TryAdd(map.MapId, map))
                throw new ArgumentException("Duplicate source map ID", nameof(originalMaps));
        }
        if (_maps.Count == 0) throw new ArgumentException("At least one source map required", nameof(originalMaps));
        _paths = _maps.ToDictionary(entry => entry.Key, entry => new MapNavigator(entry.Value));
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("ServerWorld state has a single writer; only TryPostMove is MPSC");
    }

    /// <summary>Trusted bootstrap only; authentication and spawn authorization are upstream work.</summary>
    public bool TryTrustedRegister(long actorId, int mapId, double x, double y, out ActorSnapshot snapshot)
    {
        EnsureOwner();
        snapshot = default;
        if (actorId <= 0 || _actors.ContainsKey(actorId) ||
            _actors.Count >= _limits.MaxActors ||
            (!_epochs.ContainsKey(actorId) && _epochs.Count >= _limits.MaxKnownActorIds) ||
            !_maps.TryGetValue(mapId, out var grid) ||
            !grid.TryWorldCell(x, y, out var cell) || !grid.IsWalkable(cell))
            return false;

        var previous = _epochs.GetValueOrDefault(actorId);
        if (previous.Connection == long.MaxValue || previous.Map == long.MaxValue)
            return false; // Never wrap generations.
        var conn = previous.Connection + 1;
        var mapEpoch = previous.Map + 1;
        var actor = new Actor
        {
            Id = actorId, Map = mapId, X = x, Y = y,
            ConnectionEpoch = conn, MapEpoch = mapEpoch
        };
        _actors.Add(actorId, actor);
        _epochs[actorId] = (conn, mapEpoch);
        snapshot = actor.Snapshot();
        _lifecycle.Add(new(ActorLifecycleKind.Joined, snapshot));
        return true;
    }

    /// <summary>Trusted lifecycle call; old disconnects cannot remove a new connection/map.</summary>
    public bool TryTrustedDisconnect(long actorId, long connectionEpoch, long mapEpoch)
    {
        EnsureOwner();
        if (!_actors.TryGetValue(actorId, out var actor) ||
            actor.ConnectionEpoch != connectionEpoch || actor.MapEpoch != mapEpoch)
            return false;
        _lifecycle.Add(new(ActorLifecycleKind.Left, actor.Snapshot()));
        return _actors.Remove(actorId);
    }

    /// <summary>
    /// Trusted transfer ONLY after NPC/portal validation performed elsewhere.
    /// Client may NOT request arbitrary map/position via a MoveTargetCommand.
    /// Clearing its route atomically also invalidates all queued old-map commands.
    /// This does NOT implement WorldReady, AOI or gameplay portal permissions.
    /// </summary>
    public bool TryTrustedTransfer(long actorId, long connectionEpoch, long mapEpoch,
                                   int newMapId, double x, double y, out ActorSnapshot snapshot)
    {
        EnsureOwner();
        snapshot = default;
        if (!_actors.TryGetValue(actorId, out var actor) ||
            actor.ConnectionEpoch != connectionEpoch || actor.MapEpoch != mapEpoch ||
            !_maps.TryGetValue(newMapId, out var grid) ||
            !grid.TryWorldCell(x, y, out var cell) || !grid.IsWalkable(cell) ||
            actor.MapEpoch == long.MaxValue || actor.Revision == long.MaxValue)
            return false;
        _lifecycle.Add(new(ActorLifecycleKind.Left, actor.Snapshot()));
        actor.Map = newMapId;
        actor.MapEpoch++;
        actor.X = x;
        actor.Y = y;
        actor.LastSequence = 0;
        actor.MovementActionSequence = 0;
        actor.Revision++;
        actor.ClearRoute();
        _epochs[actorId] = (actor.ConnectionEpoch, actor.MapEpoch);
        snapshot = actor.Snapshot();
        _lifecycle.Add(new(ActorLifecycleKind.Joined, snapshot));
        return true;
    }

    public ActorSnapshot? GetActor(long actorId)
    {
        EnsureOwner();
        return _actors.TryGetValue(actorId, out var actor) ? actor.Snapshot() : null;
    }

    /// <summary>
    /// Nonblocking multi-producer enqueue. CAS reserves a capacity token before
    /// publishing the item. A concurrent Tick may see a reserved-but-not-yet-
    /// published item and process it next tick; it never blocks waiting for it.
    /// A false result MUST be reported to the upstream connection/command layer.
    /// </summary>
    public bool TryPostMove(MoveTargetCommand command)
    {
        while (true)
        {
            int n = Volatile.Read(ref _reserved);
            if (n >= _limits.MaxPendingCommands) return false;
            if (Interlocked.CompareExchange(ref _reserved, n + 1, n) != n) continue;
            _incoming.Enqueue(command);
            return true;
        }
    }

    public WorldTickResult Tick()
    {
        EnsureOwner();
        _tick = checked(_tick + 1);
        var lifecycle = _lifecycle.ToArray();
        _lifecycle.Clear();
        var receipts = new List<MoveReceipt>();
        var changed = new List<ActorSnapshot>();
        var changedCommands = new HashSet<long>();
        int routeQueries = 0;

        for (int i = 0; i < _limits.MaxCommandsPerTick; i++)
        {
            if (!_incoming.TryDequeue(out var input)) break;
            Interlocked.Decrement(ref _reserved);
            var status = ApplyMove(input, ref routeQueries, changedCommands);
            receipts.Add(new MoveReceipt(
                input.ActorId, input.ConnectionEpoch, input.MapEpoch,
                input.MovementSequence, status, _tick));
        }

        // Stable order, independent of inbound thread scheduling and render FPS.
        foreach (var actor in _actors.Values.OrderBy(a => a.Id))
        {
            int priorCount = changed.Count;
            AdvanceMovement(actor, changed, receipts);
            // Accepted Move/Stop with zero displacement still needs an entity
            // revision/delta. Otherwise other clients never observe the Stop.
            if (changed.Count == priorCount && changedCommands.Contains(actor.Id))
            {
                actor.Revision = checked(actor.Revision + 1);
                changed.Add(actor.Snapshot());
            }
        }
        return new WorldTickResult(_tick, receipts.ToArray(), changed.ToArray(), lifecycle);
    }

    private MoveReceiptStatus ApplyMove(MoveTargetCommand input, ref int routeQueries,
                                        HashSet<long> changedCommands)
    {
        if (!_actors.TryGetValue(input.ActorId, out var actor))
            return MoveReceiptStatus.UnknownActor;
        if (actor.ConnectionEpoch != input.ConnectionEpoch)
            return MoveReceiptStatus.OldConnectionEpoch;
        if (actor.MapEpoch != input.MapEpoch)
            return MoveReceiptStatus.OldMapEpoch;
        if (input.MovementSequence == 0 || input.MovementSequence <= actor.LastSequence)
            return MoveReceiptStatus.ReplayedSequence;

        // The command's sequence is consumed even when its requested destination
        // fails validation, preventing a later retry of the same stale command.
        actor.LastSequence = input.MovementSequence;
        var map = _maps[actor.Map];
        if (!map.TryWorldCell(input.TargetX, input.TargetY, out var goal))
            return MoveReceiptStatus.InvalidTarget;
        if (!map.IsWalkable(goal)) return MoveReceiptStatus.TargetBlocked;
        if (routeQueries >= _limits.MaxPathRequestsPerTick)
            return MoveReceiptStatus.TickRouteBudgetExhausted;

        routeQueries++;
        var route = _paths[actor.Map].FindPath(actor.X, actor.Y,
            input.TargetX, input.TargetY, _limits.MaxPathExpandedCells);
        if (!route.Found)
            return route.Status == PathStatus.BudgetExceeded
                ? MoveReceiptStatus.PathBudgetExhausted : MoveReceiptStatus.PathNotFound;
        actor.Route = route.Cells.Skip(1).ToArray();
        actor.RouteIndex = 0;
        actor.MovementActionSequence = input.MovementSequence;
        changedCommands.Add(actor.Id);
        return MoveReceiptStatus.Accepted;
    }

    private void AdvanceMovement(Actor actor, List<ActorSnapshot> changed,
                                 List<MoveReceipt> receipts)
    {
        double remaining = _limits.MovementPixelsPerTick;
        bool updated = false;
        // At <=16px per tick no valid grid path can consume more than 3 segments.
        for (int step = 0; step < 4 && remaining > 1e-9 &&
             actor.RouteIndex < actor.Route.Count; step++)
        {
            var waypoint = actor.Route[actor.RouteIndex];
            double tx = waypoint.X * BlockingGrid.CellPixels + BlockingGrid.CellPixels / 2.0;
            double ty = waypoint.Y * BlockingGrid.CellPixels + BlockingGrid.CellPixels / 2.0;
            double dx = tx - actor.X, dy = ty - actor.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= 1e-9)
            {
                actor.RouteIndex++;
                continue;
            }
            double pixels = Math.Min(distance, remaining);
            double nx = actor.X + dx / distance * pixels;
            double ny = actor.Y + dy / distance * pixels;
            if (!_maps[actor.Map].IsDirectClear(actor.X, actor.Y, nx, ny))
            {
                actor.ClearRoute();
                receipts.Add(new MoveReceipt(
                    actor.Id, actor.ConnectionEpoch, actor.MapEpoch, actor.LastSequence,
                    MoveReceiptStatus.RouteBecameBlocked, _tick));
                updated = true;
                break;
            }
            actor.X = nx;
            actor.Y = ny;
            remaining -= pixels;
            updated = true;
            if (pixels >= distance - 1e-9)
            {
                actor.X = tx;
                actor.Y = ty;
                actor.RouteIndex++;
            }
        }
        if (actor.RouteIndex >= actor.Route.Count && actor.Route.Count != 0)
        {
            actor.ClearRoute();
            updated = true;
        }
        if (updated)
        {
            actor.Revision = checked(actor.Revision + 1);
            changed.Add(actor.Snapshot());
        }
    }

    /// <summary>Deterministic single-writer test trace, NOT network state encoding.</summary>
    public string StateHash()
    {
        EnsureOwner();
        var sb = new StringBuilder().Append(_tick.ToString(CultureInfo.InvariantCulture));
        foreach (var a in _actors.Values.OrderBy(a => a.Id))
        {
            sb.Append('|').Append(a.Id).Append('/').Append(a.Map)
                .Append('/').Append(a.ConnectionEpoch).Append('/').Append(a.MapEpoch)
                .Append('/').Append(a.LastSequence)
                .Append('/').Append(a.MovementActionSequence)
                .Append('/').Append(a.Revision)
                .Append('/').Append(BitConverter.DoubleToInt64Bits(a.X))
                .Append('/').Append(BitConverter.DoubleToInt64Bits(a.Y))
                .Append('/').Append(a.RouteIndex);
            foreach (var c in a.Route)
                sb.Append('/').Append(c.X).Append(',').Append(c.Y);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
