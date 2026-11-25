using SGTest.Server.Map;
using SGTest.Server.World;
using System.Text.Json;

namespace SGTest.Server.Monster;

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
    int WanderIntervalTicks,
    bool AiEnabled = true,
    int DefensePower = 0,
    int MagicDefensePower = 0,
    int DodgeRate = 0,
    bool TargetCombatVerified = false,
    bool RetaliationEnabled = false,
    int HitRate = 0);

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
/// Runtime monster content gate. Converter v3 visual data can share the map
/// directory, but only explicitly verified v4 AI data or fail-closed v5
/// source-backed target-combat data can create monster actors.
/// </summary>
public static class MonsterPopulationCatalog
{
    private const long MaxDocumentBytes = 16_777_216;

    public static MonsterPopulation LoadVerified(
        string path,
        int expectedMapId,
        BlockingGrid blocking,
        int maxSpawns = 10_000) =>
        Load(path, expectedMapId, blocking, maxSpawns, allowVisualOnly: false)!;

    public static MonsterPopulation? LoadForWorld(
        string path, int expectedMapId, BlockingGrid blocking) =>
        Load(path, expectedMapId, blocking, 10_000, allowVisualOnly: true);

    private static MonsterPopulation? Load(
        string path, int expectedMapId, BlockingGrid blocking, int maxSpawns,
        bool allowVisualOnly)
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
        if (allowVisualOnly && root.ValueKind == JsonValueKind.Object &&
            ReadInt(root, "version") == 3 &&
            ReadInt(root, "map_id") == expectedMapId &&
            ReadString(root, "kind") == "visual_spawn_content")
        {
            Require(root, "stats_verified", JsonValueKind.False);
            Require(root, "gameplay_ready", JsonValueKind.False);
            Require(root, "spawns", JsonValueKind.Array);
            foreach (var definition in Require(root, "definitions", JsonValueKind.Array).EnumerateArray())
            {
                if (definition.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Visual monster definition must be an object");
                Require(definition, "stats_verified", JsonValueKind.False);
                Require(definition, "stats", JsonValueKind.Null);
            }
            return null;
        }
        bool targetOnly =
            root.ValueKind == JsonValueKind.Object &&
            ReadInt(root, "version") == 5 &&
            ReadInt(root, "map_id") == expectedMapId &&
            ReadBool(root, "stats_verified") &&
            ReadBool(root, "target_combat_ready") &&
            Require(root, "ai_ready", JsonValueKind.False).ValueKind ==
                JsonValueKind.False &&
            string.Equals(ReadString(root, "kind"),
                "verified_monster_target_runtime", StringComparison.Ordinal);
        bool retaliationOnly =
            root.ValueKind == JsonValueKind.Object &&
            ReadInt(root, "version") == 6 &&
            ReadInt(root, "map_id") == expectedMapId &&
            ReadBool(root, "stats_verified") &&
            ReadBool(root, "target_combat_ready") &&
            ReadBool(root, "retaliation_ready") &&
            Require(root, "ai_ready", JsonValueKind.False).ValueKind ==
                JsonValueKind.False &&
            string.Equals(ReadString(root, "kind"),
                "verified_monster_retaliation_runtime", StringComparison.Ordinal);
        bool fullRuntime =
            root.ValueKind == JsonValueKind.Object &&
            ReadInt(root, "version") == 4 &&
            ReadInt(root, "map_id") == expectedMapId &&
            ReadBool(root, "stats_verified") &&
            ReadBool(root, "gameplay_ready") &&
            string.Equals(ReadString(root, "kind"), "verified_monster_runtime",
                StringComparison.Ordinal);
        if (!targetOnly && !retaliationOnly && !fullRuntime)
            throw new InvalidDataException(
                "Monster population is neither verified full AI, retaliation, nor target-combat content");

        var definitionsElement = Require(root, "definitions", JsonValueKind.Array);
        if (definitionsElement.GetArrayLength() is < 1 or > 4096)
            throw new InvalidDataException("Monster definition count outside bounds");

        var definitions = new Dictionary<int, MonsterDefinition>();
        foreach (var item in definitionsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Monster definition must be an object");
            int id = ReadInt(item, "id");
            string sprite = ReadString(item, "sprite").Trim().ToUpperInvariant();
            string name = targetOnly || retaliationOnly
                ? sprite
                : ReadString(item, "name").Trim();
            string atlas = ReadString(item, "atlas").Trim().Replace('\\', '/');
            if (id <= 0 || id > 10_000_000 || string.IsNullOrWhiteSpace(name) ||
                name.Length > 128 || !SafeSprite(sprite) || !SafeRelativePath(atlas))
                throw new InvalidDataException($"Invalid monster definition {id}");

            var statsElement = Require(item, "stats", JsonValueKind.Object);
            var stats = targetOnly
                ? new MonsterCombatStats(
                    ReadInt(statsElement, "level"),
                    ReadInt(statsElement, "max_hp"),
                    0, 0, 0, 0, 0, 0, 0,
                    ReadInt(statsElement, "respawn_ticks"),
                    0, 0, AiEnabled: false,
                    DefensePower: ReadInt(statsElement, "defense_power"),
                    MagicDefensePower: ReadInt(statsElement, "magic_defense_power"),
                    DodgeRate: ReadInt(statsElement, "dodge_rate"),
                    TargetCombatVerified: true)
                : retaliationOnly
                    ? new MonsterCombatStats(
                        ReadInt(statsElement, "level"),
                        ReadInt(statsElement, "max_hp"),
                        ReadInt(statsElement, "attack_damage"),
                        0, 0,
                        ReadDouble(statsElement, "lose_target_radius"),
                        0,
                        ReadDouble(statsElement, "attack_range"),
                        ReadInt(statsElement, "attack_cooldown_ticks"),
                        ReadInt(statsElement, "respawn_ticks"),
                        0, 0, AiEnabled: false,
                        DefensePower: ReadInt(statsElement, "defense_power"),
                        MagicDefensePower: ReadInt(statsElement, "magic_defense_power"),
                        DodgeRate: ReadInt(statsElement, "dodge_rate"),
                        TargetCombatVerified: true,
                        RetaliationEnabled: true,
                        HitRate: ReadInt(statsElement, "hit_rate"))
                    : new MonsterCombatStats(
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
            stats.RespawnTicks is < 1 or > 108_000 ||
            stats.DefensePower is < 0 or > ushort.MaxValue ||
            stats.MagicDefensePower is < 0 or > ushort.MaxValue ||
            stats.DodgeRate is < 0 or > ushort.MaxValue)
            throw new InvalidDataException($"Unsafe monster target stats for definition {id}");

        if (!stats.AiEnabled)
        {
            if (stats.RetaliationEnabled)
            {
                if (stats.AttackDamage is < 1 or > 100_000_000 ||
                    stats.HitRate is < 0 or > ushort.MaxValue ||
                    !FiniteRange(stats.LoseTargetRadius, 1, 4000) ||
                    !FiniteRange(stats.AttackRange, 1, 500) ||
                    stats.AttackRange > stats.LoseTargetRadius ||
                    stats.AttackCooldownTicks is < 1 or > 3600 ||
                    stats.MoveStep != 0 || stats.AggroRadius != 0 ||
                    stats.LeashRadius != 0 || stats.WanderRadius != 0 ||
                    stats.WanderIntervalTicks != 0)
                    throw new InvalidDataException(
                        $"Unsafe source-backed retaliation stats for definition {id}");
                return;
            }
            if (stats.AttackDamage != 0 || stats.HitRate != 0 ||
                stats.MoveStep != 0 || stats.AggroRadius != 0 ||
                stats.LoseTargetRadius != 0 || stats.LeashRadius != 0 ||
                stats.AttackRange != 0 || stats.AttackCooldownTicks != 0 ||
                stats.WanderRadius != 0 || stats.WanderIntervalTicks != 0)
                throw new InvalidDataException(
                    $"Target-only monster {id} contains unverified AI values");
            return;
        }

        if (stats.AttackDamage is < 1 or > 100_000_000 ||
            !FiniteRange(stats.MoveStep, 0.1, 64) ||
            !FiniteRange(stats.AggroRadius, 1, 2000) ||
            !FiniteRange(stats.LoseTargetRadius, stats.AggroRadius, 4000) ||
            !FiniteRange(stats.LeashRadius, stats.AggroRadius, 4000) ||
            !FiniteRange(stats.AttackRange, 1, 500) ||
            stats.AttackCooldownTicks is < 1 or > 3600 ||
            !FiniteRange(stats.WanderRadius, 0, 2000) ||
            stats.WanderIntervalTicks is < 1 or > 108_000)
            throw new InvalidDataException($"Unsafe monster AI stats for definition {id}");
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
