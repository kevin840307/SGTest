using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace SGTest.Server.Map;

/// <summary>One original 16-pixel Blocking.dat cell. These are NOT terrain-tile coordinates.</summary>
public readonly record struct GridCell(int X, int Y);

/// <summary>
/// Immutable collision data for one original map. No textures, Godot references,
/// world simulation state or old prototype code are used here.
/// </summary>
public sealed class BlockingGrid
{
    public const int CellPixels = 16;
    private const long MaxCells = 16_777_216;
    private readonly byte[] _bytes; // original header + row-major cell flags; owned and never exposed

    public int MapId { get; }
    public int Width { get; }
    public int Height { get; }
    public int CellCount => Width * Height;
    public int WorldWidth => Width * CellPixels;
    public int WorldHeight => Height * CellPixels;
    public string SourceSha256 { get; }

    private BlockingGrid(int mapId, int width, int height, byte[] bytes, string sha)
    {
        MapId = mapId;
        Width = width;
        Height = height;
        _bytes = bytes;
        SourceSha256 = sha;
    }

    /// <summary>
    /// Load exactly the same version-3 world.json and original blocking.dat
    /// produced by the ONE asset converter, refusing stale or modified maps.
    /// The caller chooses a private directory such as client/assets/maps/13;
    /// it is not committed to Git and should be copied into server/content at deployment.
    /// </summary>
    public static BlockingGrid Load(string mapDirectory, int expectedMapId)
    {
        if (expectedMapId is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(expectedMapId));
        ArgumentException.ThrowIfNullOrWhiteSpace(mapDirectory);

        string worldPath = Path.Combine(mapDirectory, "world.json");
        string blockingPath = Path.Combine(mapDirectory, "blocking.dat");
        using var worldStream = File.OpenRead(worldPath);
        if (worldStream.Length > 16_777_216)
            throw new InvalidDataException("world.json exceeds configured metadata limit");
        using var document = JsonDocument.Parse(worldStream, new JsonDocumentOptions { MaxDepth = 48 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            root.GetProperty("version").GetInt32() != 3 ||
            root.GetProperty("map_id").GetInt32() != expectedMapId ||
            root.GetProperty("collision_cell").GetInt32() != CellPixels)
            throw new InvalidDataException("Incompatible map/Blocking.dat schema");

        var extent = root.GetProperty("size");
        if (extent.ValueKind != JsonValueKind.Array || extent.GetArrayLength() != 2)
            throw new InvalidDataException("Invalid world pixel extent");
        int worldWidth = extent[0].GetInt32(), worldHeight = extent[1].GetInt32();
        string expectedSha = root.GetProperty("collision_sha256").GetString() ?? "";
        if (expectedSha.Length != 64 || !expectedSha.All(Uri.IsHexDigit))
            throw new InvalidDataException("Missing original Blocking.dat SHA-256");

        var file = new FileInfo(blockingPath);
        if (file.Length is < 9 or > MaxCells + 8)
            throw new InvalidDataException("Blocking.dat outside supported size range");
        byte[] raw = File.ReadAllBytes(blockingPath);
        if (raw.Length < 8)
            throw new InvalidDataException("Truncated Blocking.dat header");
        uint width = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0, 4));
        uint height = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4, 4));
        long cells = (long)width * height;
        if (width == 0 || height == 0 || cells is < 1 or > MaxCells ||
            raw.Length != 8 + cells ||
            (long)width * CellPixels != worldWidth ||
            (long)height * CellPixels != worldHeight)
            throw new InvalidDataException("Blocking dimensions, bytes or world coordinates differ");

        string actualSha = Convert.ToHexString(SHA256.HashData(raw));
        if (!actualSha.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original Blocking.dat SHA-256 mismatch");
        return new BlockingGrid(expectedMapId, checked((int)width), checked((int)height), raw, actualSha);
    }

    public bool Contains(GridCell cell) =>
        (uint)cell.X < (uint)Width && (uint)cell.Y < (uint)Height;

    /// <summary>Raw original flag; null for outside world, NEVER silently wrap/clamp.</summary>
    public byte? FlagAt(GridCell cell) =>
        Contains(cell) ? _bytes[8 + cell.Y * Width + cell.X] : null;

    /// <summary>
    /// Conservative initial policy: only raw 0 is traversable.
    /// Map13/15 observed values are 0, 6, 7. Special flag permissions are
    /// NOT inferred from the previous prototype and must be researched later.
    /// </summary>
    public bool IsWalkable(GridCell cell) => FlagAt(cell) == 0;

    public bool TryWorldCell(double x, double y, out GridCell cell)
    {
        cell = default;
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            x < 0 || y < 0 || x >= WorldWidth || y >= WorldHeight)
            return false;
        cell = new GridCell((int)Math.Floor(x / CellPixels), (int)Math.Floor(y / CellPixels));
        return true;
    }

    /// <summary>
    /// Conservative 2D grid supercover traversal: both orthogonal neighbors
    /// must be open at an exact diagonal cell corner. Checks the WHOLE segment,
    /// never accepts a chord through a wall just because an A* detour exists.
    /// </summary>
    public bool IsDirectClear(double fromX, double fromY, double toX, double toY)
    {
        if (!TryWorldCell(fromX, fromY, out var start) ||
            !TryWorldCell(toX, toY, out var goal) ||
            !IsWalkable(start) || !IsWalkable(goal))
            return false;
        int x = start.X, y = start.Y;
        if (start == goal) return true;
        double dx = toX - fromX, dy = toY - fromY;
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        double tx = sx == 0 ? double.PositiveInfinity
            : ((sx > 0 ? (x + 1) * CellPixels : x * CellPixels) - fromX) / dx;
        double ty = sy == 0 ? double.PositiveInfinity
            : ((sy > 0 ? (y + 1) * CellPixels : y * CellPixels) - fromY) / dy;
        double stepX = sx == 0 ? double.PositiveInfinity : CellPixels / Math.Abs(dx);
        double stepY = sy == 0 ? double.PositiveInfinity : CellPixels / Math.Abs(dy);
        int limit = Math.Abs(goal.X - x) + Math.Abs(goal.Y - y) + 2;
        while ((x != goal.X || y != goal.Y) && limit-- > 0)
        {
            if (Math.Abs(tx - ty) <= 1e-12)
            {
                if (!IsWalkable(new GridCell(x + sx, y)) ||
                    !IsWalkable(new GridCell(x, y + sy)))
                    return false;
                x += sx;
                y += sy;
                tx += stepX;
                ty += stepY;
            }
            else if (tx < ty)
            {
                x += sx;
                tx += stepX;
            }
            else
            {
                y += sy;
                ty += stepY;
            }
            if (!IsWalkable(new GridCell(x, y))) return false;
        }
        return x == goal.X && y == goal.Y;
    }
}
