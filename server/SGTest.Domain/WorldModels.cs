namespace SGTest.Domain;

// Screen/world convention: +X right, +Y down. 8 directions start at East.
public readonly record struct Point2(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    public double Distance(Point2 other)
        => Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
    public static Point2 Lerp(Point2 a, Point2 b, double t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}

public enum Facing
{
    East = 0, SouthEast = 1, South = 2, SouthWest = 3,
    West = 4, NorthWest = 5, North = 6, NorthEast = 7
}

public static class FacingMath
{
    public static Facing FromDelta(Point2 delta, Facing fallback)
    {
        if (Math.Abs(delta.X) < 0.001 && Math.Abs(delta.Y) < 0.001)
            return fallback;
        var octant = (int)Math.Round(
            Math.Atan2(delta.Y, delta.X) / (Math.PI / 4.0),
            MidpointRounding.AwayFromZero);
        return (Facing)((octant + 8) % 8);
    }
}

public sealed record MoveSegment(
    Point2 Start, Point2 End, long StartTick, int DurationTicks);

public sealed class PlayerState(long id, string name, Point2 position)
{
    public long Id { get; } = id;
    public string Name { get; } = name;
    public Point2 Position { get; internal set; } = position;
    public Facing Facing { get; internal set; } = Facing.South;
    public long MovementSeq { get; internal set; }
    public MoveSegment? Movement { get; internal set; }
    public long Revision { get; } = 1;
}

// Wire DTO (demo JSON v0 only). Persistent IDs and asset events are NOT implemented.
public sealed record WorldEvent
{
    public required string Type { get; init; }
    public long EntityId { get; init; }
    public long MapEpoch { get; init; } = 1;
    public long EntityRevision { get; init; } = 1;
    public long ServerTick { get; init; }
    public long MovementSeq { get; init; }
    public string Name { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public int Facing { get; init; }
    public double StartX { get; init; }
    public double StartY { get; init; }
    public double EndX { get; init; }
    public double EndY { get; init; }
    public long StartTick { get; init; }
    public int DurationTicks { get; init; }
}
