namespace SGTest.Server.Map;

public enum PathStatus
{
    Found,
    InvalidCoordinate,
    StartBlocked,
    GoalBlocked,
    BudgetExceeded,
    NoRoute
}

public sealed record NavigationPath(PathStatus Status, IReadOnlyList<GridCell> Cells, int Expanded)
{
    public bool Found => Status == PathStatus.Found;
}

/// <summary>
/// Bounded, deterministic 8-neighbor A* on the ORIGINAL Blocking.dat.
/// Own one navigator per map; World simulation must authorize commands and
/// schedule a query budget separately. This is not a physics clock or AI tick.
/// </summary>
public sealed class MapNavigator
{
    private static readonly (int X, int Y, int Cost)[] Directions =
    [
        (1, 0, 10), (0, 1, 10), (-1, 0, 10), (0, -1, 10),
        (1, 1, 14), (-1, 1, 14), (-1, -1, 14), (1, -1, 14)
    ];

    private readonly BlockingGrid _map;

    public MapNavigator(BlockingGrid map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
    }

    /// <summary>
    /// A path is in COLLISION CELLS (16 pixels), not terrain tiles.
    /// It includes the start and goal cells. Every diagonal checks both
    /// orthogonals. If the bounded search cannot prove a route, fail closed.
    /// </summary>
    public NavigationPath FindPath(double fromX, double fromY, double toX, double toY,
                                   int maxExpanded = 4096)
    {
        if (maxExpanded is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(maxExpanded));

        if (!_map.TryWorldCell(fromX, fromY, out var start) ||
            !_map.TryWorldCell(toX, toY, out var goal))
            return Fail(PathStatus.InvalidCoordinate);
        if (!_map.IsWalkable(start)) return Fail(PathStatus.StartBlocked);
        if (!_map.IsWalkable(goal)) return Fail(PathStatus.GoalBlocked);
        if (start == goal) return new NavigationPath(PathStatus.Found, [start], 0);

        // Recovered first uses a verified direct segment. This avoids doing A*
        // just to recreate a straight route and keeps Client/Server steering
        // identical before an obstacle is involved.
        var goalPoint = Center(goal);
        if (_map.IsDirectClear(fromX, fromY, goalPoint.X, goalPoint.Y))
            return new NavigationPath(PathStatus.Found, [start, goal], 0);

        int initial = Index(start), target = Index(goal);
        // A* avoids scanning the full 960x960 grid for every player path request.
        var queue = new PriorityQueue<(int Index, int Cost), (int F, int H, int Index)>();
        var bestCost = new Dictionary<int, int> { [initial] = 0 };
        var parents = new Dictionary<int, int>();
        var closed = new HashSet<int>();
        int h0 = Heuristic(start, goal);
        queue.Enqueue((initial, 0), (h0, h0, initial));
        int expanded = 0;

        while (queue.TryDequeue(out var queued, out _))
        {
            if (!bestCost.TryGetValue(queued.Index, out var best) ||
                queued.Cost != best || !closed.Add(queued.Index))
                continue; // stale priority-queue entry
            if (expanded >= maxExpanded)
                return Fail(PathStatus.BudgetExceeded, expanded);
            expanded++;
            if (queued.Index == target)
            {
                var cells = new List<GridCell>();
                int cursor = target;
                while (cursor != initial)
                {
                    cells.Add(Cell(cursor));
                    cursor = parents[cursor];
                }
                cells.Add(start);
                cells.Reverse();
                return new NavigationPath(PathStatus.Found,
                    SmoothPath(cells, fromX, fromY), expanded);
            }
            var current = Cell(queued.Index);
            foreach (var (stepX, stepY, cost) in Directions)
            {
                var next = new GridCell(current.X + stepX, current.Y + stepY);
                if (!_map.IsWalkable(next) || (stepX != 0 && stepY != 0 &&
                    (!_map.IsWalkable(new GridCell(current.X + stepX, current.Y)) ||
                     !_map.IsWalkable(new GridCell(current.X, current.Y + stepY)))))
                    continue;
                int index = Index(next);
                if (closed.Contains(index)) continue;
                int tentative = best + cost;
                if (bestCost.TryGetValue(index, out int old) && tentative >= old) continue;
                bestCost[index] = tentative;
                parents[index] = queued.Index;
                int h = Heuristic(next, goal);
                queue.Enqueue((index, tentative), (tentative + h, h, index));
            }
        }
        return Fail(PathStatus.NoRoute, expanded);
    }

    private IReadOnlyList<GridCell> SmoothPath(IReadOnlyList<GridCell> raw,
                                                double startX, double startY)
    {
        if (raw.Count <= 2) return raw;
        var result = new List<GridCell> { raw[0] };
        double anchorX = startX, anchorY = startY;
        int index = 1;
        while (index < raw.Count)
        {
            int furthest = index;
            for (int candidate = index; candidate < raw.Count; candidate++)
            {
                var p = Center(raw[candidate]);
                if (!_map.IsDirectClear(anchorX, anchorY, p.X, p.Y))
                    break;
                furthest = candidate;
            }
            var selected = raw[furthest];
            result.Add(selected);
            var point = Center(selected);
            anchorX = point.X;
            anchorY = point.Y;
            index = furthest + 1;
        }
        return result;
    }

    private static (double X, double Y) Center(GridCell cell) =>
        (cell.X * BlockingGrid.CellPixels + BlockingGrid.CellPixels / 2.0,
         cell.Y * BlockingGrid.CellPixels + BlockingGrid.CellPixels / 2.0);

    private int Index(GridCell point) => checked(point.Y * _map.Width + point.X);

    private GridCell Cell(int index) => new(index % _map.Width, index / _map.Width);

    private static int Heuristic(GridCell a, GridCell b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return 14 * Math.Min(dx, dy) + 10 * Math.Abs(dx - dy);
    }

    private static NavigationPath Fail(PathStatus status, int expanded = 0) =>
        new(status, Array.Empty<GridCell>(), expanded);
}
