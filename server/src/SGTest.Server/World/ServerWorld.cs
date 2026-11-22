using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Map;
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
    // MVP target is 20 concurrent players. Eight route queries caused
    // deterministic TickRouteBudgetExhausted receipts when all 20 moved on
    // the same tick. Keep bounded headroom without coupling this to monster
    // AI, which has its own independent per-step path budget.
    int MaxPathRequestsPerTick = 32,
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
    RouteBecameBlocked,
    CombatDead
}

public readonly record struct MoveReceipt(
    long ActorId, long ConnectionEpoch, long MapEpoch,
    uint MovementSequence, MoveReceiptStatus Status, long Tick);

public readonly record struct ActorSnapshot(
    long ActorId, int MapId, long ConnectionEpoch, long MapEpoch,
    uint LastMovementSequence, uint MovementActionSequence, double X, double Y,
    long EntityRevision, bool IsMoving, byte Facing = 1, bool CombatDead = false);

public enum PvpAttackGeometryStatus : byte
{
    Ready, StaleGeneration, SelfTarget, TargetMissing, DifferentMap,
    NonPvpMap, AttackerDead, TargetDead, TooFar, Blocked
}
public readonly record struct PvpAttackGeometry(
    PvpAttackGeometryStatus Status,long ServerTick,ActorSnapshot Attacker,ActorSnapshot Target);

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
        public uint LastPortalSequence; // per-MapEpoch authenticated NPC interaction replay guard
        public byte Facing = 1; // Initial spawn policy; movement owns subsequent facing.
        public long Revision = 1;
        public bool Closing;
        public bool CombatDead; // trusted persistence barrier; raw client can never set this.
        public IReadOnlyList<GridCell> Route = Array.Empty<GridCell>();
        public int RouteIndex;

        public ActorSnapshot Snapshot() => new(Id, Map, ConnectionEpoch, MapEpoch,
            LastSequence, MovementActionSequence, X, Y, Revision, RouteIndex < Route.Count, Facing, CombatDead);

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
    private readonly List<ActorSnapshot> _trustedChanged = new();
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

    /// <summary>
    /// Starts graceful session release on the single World writer. The actor stays
    /// registered (so a same-character login cannot race the SQLite save), but its
    /// route is frozen and all already-queued MoveTarget commands are rejected.
    /// The returned snapshot is the only position persistence may write.
    /// </summary>
    public bool TryTrustedPrepareDisconnect(long actorId, long connectionEpoch, long mapEpoch,
                                            out ActorSnapshot snapshot)
    {
        EnsureOwner();
        snapshot = default;
        if (!_actors.TryGetValue(actorId, out var actor) ||
            actor.ConnectionEpoch != connectionEpoch || actor.MapEpoch != mapEpoch ||
            actor.Closing)
            return false;
        actor.Closing = true;
        actor.ClearRoute();
        snapshot = actor.Snapshot();
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
    /// Joined/Left lifecycle is published on the next Tick for WorldReady/AOI;
    /// upstream gameplay portal permissions and client RPC are separate work.
    /// </summary>
    public bool TryTrustedTransfer(long actorId, long connectionEpoch, long mapEpoch,
                                   int newMapId, double x, double y, out ActorSnapshot snapshot)
    {
        EnsureOwner();
        snapshot = default;
        if (!_actors.TryGetValue(actorId, out var actor) ||
            actor.ConnectionEpoch != connectionEpoch || actor.MapEpoch != mapEpoch ||
            actor.Closing || !_maps.TryGetValue(newMapId, out var grid) ||
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

    public bool TryTrustedSetCombatDead(
        long actorId, long connectionEpoch, long mapEpoch,
        bool dead, out ActorSnapshot snapshot)
    {
        EnsureOwner();
        snapshot = default;
        if (!_actors.TryGetValue(actorId, out var actor) ||
            actor.ConnectionEpoch != connectionEpoch ||
            actor.MapEpoch != mapEpoch || actor.Closing)
            return false;
        if (actor.CombatDead == dead)
        {
            snapshot = actor.Snapshot();
            return true;
        }

        actor.CombatDead = dead;
        if (dead) actor.ClearRoute();
        actor.Revision = checked(actor.Revision + 1);
        snapshot = actor.Snapshot();
        _trustedChanged.Add(snapshot);
        return true;
    }

    /// <summary>
    /// Trusted authenticated NPC interaction; ALL decisions execute atomically
    /// on the single World writer. The request supplies only a map-scoped NPC
    /// SpawnId: actor identity, map and coordinates are server-owned.
    /// The source NPC must be within the configured interaction radius.
    /// Only cataloged audited static transfers are eligible. Never call this
    /// directly from a raw client packet outside a trusted Gateway binding.
    /// </summary>
    public NpcPortalOutcome TryTrustedNpcInteract(
        long actorId, long connectionEpoch, long mapEpoch,
        uint interactionSequence, int spawnId, PortalCatalog catalog)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(catalog);
        if (!_actors.TryGetValue(actorId, out var actor) ||
            actor.ConnectionEpoch != connectionEpoch ||
            actor.MapEpoch != mapEpoch || actor.Closing ||
            actorId <= 0 || interactionSequence == 0)
            return new(NpcPortalStatus.StaleGeneration, null);
        if (actor.CombatDead)
            return new(NpcPortalStatus.CombatDead, null);
        if (interactionSequence <= actor.LastPortalSequence)
            return new(NpcPortalStatus.ReplayedSequence, null);
        actor.LastPortalSequence = interactionSequence;

        if (!catalog.TryGet(actor.Map, spawnId, out var portal))
            return new(NpcPortalStatus.UnknownNpc, null);
        double deltaX = actor.X - portal.NpcX;
        double deltaY = actor.Y - portal.NpcY;
        double distance = catalog.MaximumInteractionDistance;
        if (deltaX * deltaX + deltaY * deltaY > distance * distance)
            return new(NpcPortalStatus.TooFar, null);

        if (!TryTrustedTransfer(actorId, connectionEpoch, mapEpoch,
                portal.DestinationMapId, portal.DestinationX,
                portal.DestinationY, out var transferred))
            return new(NpcPortalStatus.DestinationRejected, null);

        actor.LastPortalSequence = 0; // new MapEpoch starts new client sequence
        return new(NpcPortalStatus.Accepted, transferred);
    }

    public PvpAttackGeometry GetPvpAttackGeometry(
        long actorId,long connectionEpoch,long mapEpoch,long targetActorId,double attackRange)
    {
        EnsureOwner();
        if(actorId<=0||connectionEpoch<=0||mapEpoch<=0||targetActorId<=0||
           !double.IsFinite(attackRange)||attackRange<=0||attackRange>500)
            throw new ArgumentOutOfRangeException(nameof(attackRange));
        if(!_actors.TryGetValue(actorId,out var attacker)||attacker.ConnectionEpoch!=connectionEpoch||
           attacker.MapEpoch!=mapEpoch||attacker.Closing)
            return new(PvpAttackGeometryStatus.StaleGeneration,_tick,default,default);
        var a=attacker.Snapshot();
        if(targetActorId==actorId)return new(PvpAttackGeometryStatus.SelfTarget,_tick,a,default);
        if(!_actors.TryGetValue(targetActorId,out var target)||target.Closing)
            return new(PvpAttackGeometryStatus.TargetMissing,_tick,a,default);
        var t=target.Snapshot();
        if(target.Map!=attacker.Map)return new(PvpAttackGeometryStatus.DifferentMap,_tick,a,t);
        if(!PvpMapPolicy.AllowsPlayerPvp(attacker.Map))return new(PvpAttackGeometryStatus.NonPvpMap,_tick,a,t);
        if(attacker.CombatDead)return new(PvpAttackGeometryStatus.AttackerDead,_tick,a,t);
        if(target.CombatDead)return new(PvpAttackGeometryStatus.TargetDead,_tick,a,t);
        const double tolerance=24.0;
        double allowed=attackRange+tolerance,dx=attacker.X-target.X,dy=attacker.Y-target.Y;
        if(dx*dx+dy*dy>allowed*allowed)return new(PvpAttackGeometryStatus.TooFar,_tick,a,t);
        if(!_maps[attacker.Map].IsDirectClear(attacker.X,attacker.Y,target.X,target.Y))
            return new(PvpAttackGeometryStatus.Blocked,_tick,a,t);
        return new(PvpAttackGeometryStatus.Ready,_tick,a,t);
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
        var changed = new List<ActorSnapshot>(_trustedChanged);
        _trustedChanged.Clear();
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
            if (actor.CombatDead)
                actor.ClearRoute();
            else
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
        // A closing authenticated socket has been detached already. Preserve the
        // final persistence snapshot by refusing commands that were queued before EOF.
        if (actor.Closing)
            return MoveReceiptStatus.UnknownActor;
        if (actor.CombatDead)
            return MoveReceiptStatus.CombatDead;
        if (input.MovementSequence == 0 || input.MovementSequence <= actor.LastSequence)
            return MoveReceiptStatus.ReplayedSequence;

        // The command's sequence is consumed even when its requested destination
        // fails validation, preventing a later retry of the same stale command.
        actor.LastSequence = input.MovementSequence;
        var map = _maps[actor.Map];
        if (!map.TryWorldCell(input.TargetX, input.TargetY, out var goal))
            return MoveReceiptStatus.InvalidTarget;
        if (!map.IsWalkable(goal)) return MoveReceiptStatus.TargetBlocked;
        // Held-cursor updates can resolve to the existing final collision
        // cell. Acknowledge their fresh sequence, but do NOT restart A* or
        // the active route: doing so can cause a visible 16px turn/jitter.
        if (actor.RouteIndex < actor.Route.Count && actor.Route[^1] == goal)
        {
            // LastSequence already acknowledges this newer request. Preserve
            // the current walk action/revision; otherwise a harmless repeated
            // command still generates unnecessary AOI traffic.
            return MoveReceiptStatus.Accepted;
        }
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
        if (actor.RouteIndex >= actor.Route.Count) return;

        // Match Recovered player.gd: route is already complete/smoothed.
        // Each 30Hz tick performs ONE speed-clamped movement step. Waypoints
        // may be promoted and corner probes adjusted, but elapsed time is
        // never "caught up" with a faster visual move after pathfinding.
        double stepDistance = _limits.MovementPixelsPerTick;
        bool updated = false;

        while (actor.RouteIndex + 1 < actor.Route.Count)
        {
            var next = actor.Route[actor.RouteIndex + 1];
            double nextX = next.X * BlockingGrid.CellPixels + BlockingGrid.CellPixels / 2.0;
            double nextY = next.Y * BlockingGrid.CellPixels + BlockingGrid.CellPixels / 2.0;
            bool nextVisible = _maps[actor.Map].IsDirectClear(actor.X, actor.Y, nextX, nextY);
            // Recovered's close-to-waypoint promotion relies on CharacterBody2D
            // collision as a safety net. This authoritative coordinate writer
            // has no body collider, so early promotion requires proven LOS.
            if (!nextVisible) break;
            actor.RouteIndex++;
        }

        var waypoint = actor.Route[actor.RouteIndex];
        double finalX = waypoint.X * BlockingGrid.CellPixels + BlockingGrid.CellPixels / 2.0;
        double finalY = waypoint.Y * BlockingGrid.CellPixels + BlockingGrid.CellPixels / 2.0;
        double finalDx = finalX - actor.X, finalDy = finalY - actor.Y;
        double finalDistance = Math.Sqrt(finalDx * finalDx + finalDy * finalDy);

        // Preserve Recovered steering, but never use its ~10px final snap;
        // the last authoritative step is still bounded by normal tick speed.
        if (actor.RouteIndex + 1 >= actor.Route.Count && finalDistance <= stepDistance)
        {
            actor.X = finalX;
            actor.Y = finalY;
            actor.ClearRoute();
            updated = true;
        }
        else
        {
            double targetX = finalX, targetY = finalY;
            if (actor.RouteIndex + 1 < actor.Route.Count)
            {
                var next = actor.Route[actor.RouteIndex + 1];
                double nextX = next.X * BlockingGrid.CellPixels + BlockingGrid.CellPixels / 2.0;
                double nextY = next.Y * BlockingGrid.CellPixels + BlockingGrid.CellPixels / 2.0;
                // Recovered probes 3/4, 1/2, 1/4 of the next segment and
                // steers toward the furthest currently visible point.
                for (int sample = 3; sample >= 1; sample--)
                {
                    double ratio = sample / 4.0;
                    double probeX = finalX + (nextX - finalX) * ratio;
                    double probeY = finalY + (nextY - finalY) * ratio;
                    if (!_maps[actor.Map].IsDirectClear(actor.X, actor.Y, probeX, probeY))
                        continue;
                    targetX = probeX;
                    targetY = probeY;
                    break;
                }
            }

            double dx = targetX - actor.X, dy = targetY - actor.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > 1e-9)
            {
                double pixels = Math.Min(stepDistance, distance);
                double nx = actor.X + dx / distance * pixels;
                double ny = actor.Y + dy / distance * pixels;
                if (!_maps[actor.Map].IsStepClear(actor.X, actor.Y, nx, ny))
                {
                    actor.ClearRoute();
                    receipts.Add(new MoveReceipt(
                        actor.Id, actor.ConnectionEpoch, actor.MapEpoch, actor.LastSequence,
                        MoveReceiptStatus.RouteBecameBlocked, _tick));
                    updated = true;
                }
                else
                {
                    actor.Facing = FacingFromDisplacement(dx, dy, actor.Facing);
                    actor.X = nx;
                    actor.Y = ny;
                    updated = true;
                }
            }
        }

        if (updated)
        {
            actor.Revision = checked(actor.Revision + 1);
            changed.Add(actor.Snapshot());
        }
    }

    private static byte FacingFromDisplacement(double dx, double dy, byte previousFacing)
    {
        // Original world-space convention: E=6, SE=7, S=8, SW=1,
        // W=2, NW=3, N=4, NE=5. A* waypoint steering can move a few
        // degrees either side of a 22.5-degree sector boundary on adjacent
        // ticks. Retain the existing facing until the new heading is clearly
        // outside its sector: this is visual facing hysteresis, NOT steering.
        double heading = Math.Atan2(dy, dx);
        if (previousFacing is >= 1 and <= 8)
        {
            int oldSector = (previousFacing + 2) % 8;
            double oldHeading = oldSector * Math.PI / 4;
            double delta = Math.Atan2(Math.Sin(heading - oldHeading),
                                      Math.Cos(heading - oldHeading));
            const double margin = Math.PI / 24; // 7.5 degrees
            if (Math.Abs(delta) <= Math.PI / 8 + margin)
                return previousFacing;
        }
        int sector = (int)Math.Floor(heading / (Math.PI / 4) + 0.5);
        return (byte)(((sector + 5) % 8 + 8) % 8 + 1);
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
                .Append('/').Append(a.MovementActionSequence).Append('/').Append(a.Facing)
                .Append('/').Append(a.Revision)
                .Append('/').Append(a.CombatDead ? 1 : 0)
                .Append('/').Append(BitConverter.DoubleToInt64Bits(a.X))
                .Append('/').Append(BitConverter.DoubleToInt64Bits(a.Y))
                .Append('/').Append(a.RouteIndex);
            foreach (var c in a.Route)
                sb.Append('/').Append(c.X).Append(',').Append(c.Y);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
