namespace SGTest.Domain;

/// <summary>
/// Pure single-writer map simulation: no database, network, locks or file IO.
/// MapActor alone may mutate this class. Safe for deterministic replay/tests.
/// </summary>
public sealed class WorldSimulation
{
    public const int TickRate = 30;
    public const double MovementSpeed = 180.0;
    public const double Width = 1400.0;
    public const double Height = 900.0;
    public const long MapEpoch = 1;

    private readonly Dictionary<long, PlayerState> _players = [];
    public long Tick { get; private set; }
    public int PlayerCount => _players.Count;

    public bool TryJoin(long id, string name, out WorldEvent? spawned)
    {
        spawned = null;
        if (id <= 0 || _players.ContainsKey(id))
            return false;
        name = name.Trim();
        if (name.Length == 0)
            name = "Player";
        if (name.Length > 24)
            name = name[..24];
        var p = new PlayerState(id, name,
            new Point2(120.0 + (id % 17) * 42.0, 200.0 + ((id / 17) % 10) * 42.0));
        _players.Add(id, p);
        spawned = SpawnOf(p);
        return true;
    }

    /// <summary>Existing world snapshot including unfinished movement legs.</summary>
    public IReadOnlyList<WorldEvent> Snapshot()
    {
        var events = new List<WorldEvent>(_players.Count * 2);
        foreach (var p in _players.Values.OrderBy(p => p.Id))
        {
            events.Add(SpawnOf(p));
            if (p.Movement is { } leg)
            {
                var remaining = (int)(leg.StartTick + leg.DurationTicks - Tick);
                if (remaining > 0)
                {
                    events.Add(new WorldEvent
                    {
                        Type = "move", EntityId = p.Id, ServerTick = Tick,
                        MovementSeq = p.MovementSeq, Facing = (int)p.Facing,
                        StartX = p.Position.X, StartY = p.Position.Y,
                        EndX = leg.End.X, EndY = leg.End.Y,
                        StartTick = Tick, DurationTicks = remaining
                    });
                }
            }
        }
        return events;
    }

    public bool TryMove(long id, Point2 destination, out WorldEvent? moved)
    {
        moved = null;
        if (!_players.TryGetValue(id, out var p) || !InBounds(destination))
            return false;
        var distance = p.Position.Distance(destination);
        if (distance < 0.01)
            return TryStop(id, out moved);
        var duration = Math.Max(1, (int)Math.Ceiling(distance / MovementSpeed * TickRate));
        p.MovementSeq++;
        p.Facing = FacingMath.FromDelta(
            new Point2(destination.X - p.Position.X, destination.Y - p.Position.Y),
            p.Facing);
        p.Movement = new MoveSegment(p.Position, destination, Tick, duration);
        moved = new WorldEvent
        {
            Type = "move", EntityId = id, ServerTick = Tick,
            MovementSeq = p.MovementSeq, Facing = (int)p.Facing,
            StartX = p.Position.X, StartY = p.Position.Y,
            EndX = destination.X, EndY = destination.Y,
            StartTick = Tick, DurationTicks = duration
        };
        return true;
    }

    public bool TryStop(long id, out WorldEvent? stopped)
    {
        stopped = null;
        if (!_players.TryGetValue(id, out var p))
            return false;
        p.Movement = null;
        p.MovementSeq++;
        stopped = StopOf(p);
        return true;
    }

    public bool TryLeave(long id, out WorldEvent? despawned)
    {
        despawned = null;
        if (!_players.Remove(id, out var p))
            return false;
        despawned = new WorldEvent
        {
            Type = "despawn", EntityId = id, ServerTick = Tick,
            EntityRevision = p.Revision + 1
        };
        return true;
    }

    // Called exactly once by the MapActor per fixed server step.
    public IReadOnlyList<WorldEvent> Step()
    {
        Tick++;
        var completed = new List<WorldEvent>();
        foreach (var player in _players.Values)
        {
            if (player.Movement is not { } leg)
                continue;
            var t = Math.Clamp((double)(Tick - leg.StartTick) / leg.DurationTicks, 0, 1);
            player.Position = Point2.Lerp(leg.Start, leg.End, t);
            if (t >= 1.0)
            {
                player.Movement = null;
                player.MovementSeq++;
                completed.Add(StopOf(player));
            }
        }
        return completed;
    }

    public bool TryGetPosition(long id, out Point2 position)
    {
        if (_players.TryGetValue(id, out var player))
        {
            position = player.Position;
            return true;
        }
        position = default;
        return false;
    }

    private static bool InBounds(Point2 p)
        => p.IsFinite && p.X >= 0.0 && p.X <= Width && p.Y >= 0.0 && p.Y <= Height;

    private WorldEvent SpawnOf(PlayerState p) => new()
    {
        Type = "spawn", EntityId = p.Id, EntityRevision = p.Revision,
        ServerTick = Tick, Name = p.Name, X = p.Position.X,
        Y = p.Position.Y, Facing = (int)p.Facing
    };

    private WorldEvent StopOf(PlayerState p) => new()
    {
        Type = "stop", EntityId = p.Id, ServerTick = Tick,
        MovementSeq = p.MovementSeq, X = p.Position.X,
        Y = p.Position.Y, Facing = (int)p.Facing
    };
}
