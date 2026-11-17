using System.Text.Json;

namespace SGTest.Server.World;

public sealed record MonsterCombatStats(
    int Level,
    int MaxHp,
    int AttackDamage,
    double MoveStep,
    double AggroRadius,
    double LoseTargetRadius,
    double LeashRadius,
    double AttackRange,
    int AttackCooldownTicks,
    int RespawnTicks,
    double WanderRadius,
    int WanderIntervalTicks);

public sealed record MonsterDefinition(
    int Id,
    string Name,
    string Sprite,
    string Atlas,
    MonsterCombatStats Stats);

public readonly record struct MonsterSpawn(
    int SourceIndex,
    int SourceFlags,
    int DefinitionId,
    double X,
    double Y);

public sealed class MonsterPopulation
{
    public int MapId { get; }
    public IReadOnlyDictionary<int, MonsterDefinition> Definitions { get; }
    public IReadOnlyList<MonsterSpawn> Spawns { get; }

    internal MonsterPopulation(int mapId,
        Dictionary<int, MonsterDefinition> definitions,
        MonsterSpawn[] spawns)
    {
        MapId = mapId;
        Definitions = definitions;
        Spawns = spawns;
    }
}

/// <summary>
/// Runtime monster content gate. The existing converter v3 population intentionally
/// says stats_verified=false/gameplay_ready=false and MUST NOT become combat input.
/// Only an explicitly verified v4 runtime document can cross this boundary.
/// </summary>
public static class MonsterPopulationCatalog
{
    private const long MaxDocumentBytes = 16_777_216;

    public static MonsterPopulation LoadVerified(
        string path,
        int expectedMapId,
        BlockingGrid blocking,
        int maxSpawns = 10_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(blocking);
        if (expectedMapId <= 0 || blocking.MapId != expectedMapId)
            throw new ArgumentOutOfRangeException(nameof(expectedMapId));
        if (maxSpawns is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(maxSpawns));

        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException("Verified monster population not found", path);
        if (file.Length is < 2 or > MaxDocumentBytes)
            throw new InvalidDataException("Monster population outside bounded document size");

        using var stream = file.OpenRead();
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            MaxDepth = 32,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            ReadInt(root, "version") != 4 ||
            ReadInt(root, "map_id") != expectedMapId ||
            !ReadBool(root, "stats_verified") ||
            !ReadBool(root, "gameplay_ready") ||
            !string.Equals(ReadString(root, "kind"), "verified_monster_runtime",
                StringComparison.Ordinal))
            throw new InvalidDataException(
                "Monster population is not an explicitly verified v4 gameplay document");

        var definitionsElement = Require(root, "definitions", JsonValueKind.Array);
        if (definitionsElement.GetArrayLength() is < 1 or > 4096)
            throw new InvalidDataException("Monster definition count outside bounds");

        var definitions = new Dictionary<int, MonsterDefinition>();
        foreach (var item in definitionsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Monster definition must be an object");
            int id = ReadInt(item, "id");
            string name = ReadString(item, "name").Trim();
            string sprite = ReadString(item, "sprite").Trim().ToUpperInvariant();
            string atlas = ReadString(item, "atlas").Trim().Replace('\\', '/');
            if (id <= 0 || id > 10_000_000 || string.IsNullOrWhiteSpace(name) ||
                name.Length > 128 || !SafeSprite(sprite) || !SafeRelativePath(atlas))
                throw new InvalidDataException($"Invalid monster definition {id}");

            var statsElement = Require(item, "stats", JsonValueKind.Object);
            var stats = new MonsterCombatStats(
                ReadInt(statsElement, "level"),
                ReadInt(statsElement, "max_hp"),
                ReadInt(statsElement, "attack_damage"),
                ReadDouble(statsElement, "move_step"),
                ReadDouble(statsElement, "aggro_radius"),
                ReadDouble(statsElement, "lose_target_radius"),
                ReadDouble(statsElement, "leash_radius"),
                ReadDouble(statsElement, "attack_range"),
                ReadInt(statsElement, "attack_cooldown_ticks"),
                ReadInt(statsElement, "respawn_ticks"),
                ReadDouble(statsElement, "wander_radius"),
                ReadInt(statsElement, "wander_interval_ticks"));
            ValidateStats(stats, id);

            if (!definitions.TryAdd(id, new(id, name, sprite, atlas, stats)))
                throw new InvalidDataException($"Duplicate monster definition {id}");
        }

        var spawnElement = Require(root, "spawns", JsonValueKind.Array);
        if (spawnElement.GetArrayLength() > maxSpawns)
            throw new InvalidDataException("Monster spawn count exceeds configured bound");

        var sourceIndices = new HashSet<int>();
        var spawns = new List<MonsterSpawn>(spawnElement.GetArrayLength());
        foreach (var item in spawnElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Monster spawn must be an object");
            int sourceIndex = ReadInt(item, "source_index");
            int flags = ReadInt(item, "source_flags");
            int definitionId = ReadInt(item, "definition");
            var point = Require(item, "p", JsonValueKind.Array);
            if (point.GetArrayLength() != 2)
                throw new InvalidDataException("Monster spawn point must contain exactly two values");
            double x = point[0].GetDouble();
            double y = point[1].GetDouble();
            if (sourceIndex is < 0 or >= 100_000 || !sourceIndices.Add(sourceIndex) ||
                !definitions.ContainsKey(definitionId) ||
                !double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 ||
                !blocking.TryWorldCell(x, y, out var cell) || !blocking.IsWalkable(cell))
                throw new InvalidDataException($"Invalid/unwalkable monster spawn source_index={sourceIndex}");
            spawns.Add(new(sourceIndex, flags, definitionId, x, y));
        }

        return new MonsterPopulation(expectedMapId, definitions,
            spawns.OrderBy(x => x.SourceIndex).ToArray());
    }

    private static void ValidateStats(MonsterCombatStats stats, int id)
    {
        if (stats.Level is < 1 or > 255 ||
            stats.MaxHp is < 1 or > 2_000_000_000 ||
            stats.AttackDamage is < 1 or > 100_000_000 ||
            !FiniteRange(stats.MoveStep, 0.1, 64) ||
            !FiniteRange(stats.AggroRadius, 1, 2000) ||
            !FiniteRange(stats.LoseTargetRadius, stats.AggroRadius, 4000) ||
            !FiniteRange(stats.LeashRadius, stats.AggroRadius, 4000) ||
            !FiniteRange(stats.AttackRange, 1, 500) ||
            stats.AttackCooldownTicks is < 1 or > 3600 ||
            stats.RespawnTicks is < 1 or > 108_000 ||
            !FiniteRange(stats.WanderRadius, 0, 2000) ||
            stats.WanderIntervalTicks is < 1 or > 108_000)
            throw new InvalidDataException($"Unsafe monster combat stats for definition {id}");
    }

    private static bool FiniteRange(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;

    private static bool SafeSprite(string value) =>
        value.Length is > 0 and <= 64 &&
        value.All(c => c <= 0x7f && char.IsLetterOrDigit(c));

    private static bool SafeRelativePath(string value)
    {
        if (value.Length is < 1 or > 256 || value.StartsWith('/') ||
            value.Contains(':', StringComparison.Ordinal))
            return false;
        var segments = value.Split('/', StringSplitOptions.None);
        return segments.All(segment =>
            segment.Length > 0 && segment is not "." and not "..");
    }

    private static JsonElement Require(JsonElement parent, string name, JsonValueKind kind)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != kind)
            throw new InvalidDataException($"Monster population missing/invalid {name}");
        return value;
    }

    private static int ReadInt(JsonElement parent, string name) =>
        Require(parent, name, JsonValueKind.Number).TryGetInt32(out var value)
            ? value : throw new InvalidDataException($"Monster population invalid integer {name}");

    private static double ReadDouble(JsonElement parent, string name)
    {
        var value = Require(parent, name, JsonValueKind.Number).GetDouble();
        if (!double.IsFinite(value))
            throw new InvalidDataException($"Monster population invalid number {name}");
        return value;
    }

    private static bool ReadBool(JsonElement parent, string name) =>
        Require(parent, name, JsonValueKind.True) .GetBoolean();

    private static string ReadString(JsonElement parent, string name) =>
        Require(parent, name, JsonValueKind.String).GetString() ?? "";
}
