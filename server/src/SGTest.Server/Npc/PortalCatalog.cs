using SGTest.Server.World;
using System.Text.Json;

namespace SGTest.Server.Npc;

/// <summary>
/// Trusted runtime index of NPC transfers compiled by the single original
/// asset converter (sgtest.npcs/v1). Only a verified static TS_Common
/// CanMove/confirm_transfer entry becomes a portal. Dynamic Quest/Lua scripts
/// deliberately remain unavailable until a separately audited rule exists.
/// </summary>
public sealed class PortalCatalog
{
    public readonly record struct Portal(
        int SourceMapId, int SpawnId, double NpcX, double NpcY,
        int DestinationMapId, double DestinationX, double DestinationY);

    private readonly Dictionary<(int MapId, int SpawnId), Portal> _entries;
    public int Count => _entries.Count;
    public double MaximumInteractionDistance { get; }

    public PortalCatalog(IEnumerable<Portal> entries, double maximumInteractionDistance = 96)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!double.IsFinite(maximumInteractionDistance) ||
            maximumInteractionDistance is < 16 or > 256)
            throw new ArgumentOutOfRangeException(nameof(maximumInteractionDistance));
        MaximumInteractionDistance = maximumInteractionDistance;
        _entries = new();
        foreach (var p in entries)
        {
            if (p.SourceMapId <= 0 || p.SpawnId <= 0 || p.DestinationMapId <= 0 ||
                !ValidPosition(p.NpcX, p.NpcY) ||
                !ValidPosition(p.DestinationX, p.DestinationY) ||
                !_entries.TryAdd((p.SourceMapId, p.SpawnId), p))
                throw new ArgumentException("Malformed/duplicate trusted NPC portal", nameof(entries));
        }
        if (_entries.Count > 10000)
            throw new ArgumentException("Portal index is unbounded", nameof(entries));
    }

    public bool TryGet(int mapId, int spawnId, out Portal portal) =>
        _entries.TryGetValue((mapId, spawnId), out portal);

    public static PortalCatalog LoadNpcDocuments(
        IEnumerable<string> npcJsonPaths, double maximumInteractionDistance = 96)
    {
        ArgumentNullException.ThrowIfNull(npcJsonPaths);
        var portals = new List<Portal>();
        var seenMaps = new HashSet<int>();
        foreach (string path in npcJsonPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("NPC document path missing", nameof(npcJsonPaths));
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is < 30 or > 2 * 1024 * 1024)
                throw new InvalidDataException("NPC content missing or exceeds bounded size");
            using var stream = file.OpenRead();
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                MaxDepth = 20, AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                ReadString(root, "schema") != "sgtest.npcs/v1" ||
                ReadString(root, "coordinate_space") != "world_pixels")
                throw new InvalidDataException("Unsupported private NPC document schema");
            int mapId = ReadPositive(root, "map_id");
            if (!seenMaps.Add(mapId) || !root.TryGetProperty("npcs", out var npcs) ||
                npcs.ValueKind != JsonValueKind.Array || npcs.GetArrayLength() > 10000)
                throw new InvalidDataException("NPC map is duplicate or has invalid entries");

            var seenSpawn = new HashSet<int>();
            foreach (var npc in npcs.EnumerateArray())
            {
                if (npc.ValueKind != JsonValueKind.Object ||
                    !npc.TryGetProperty("interaction", out var interact) ||
                    interact.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("NPC interaction metadata unavailable");
                int spawnId = ReadPositive(npc, "spawn_id");
                if (!seenSpawn.Add(spawnId))
                    throw new InvalidDataException("Duplicate NPC spawn ID");
                string mode = ReadString(interact, "mode");
                if (mode != "confirm_transfer")
                    continue; // never execute unknown/dynamic script as a portal
                if (ReadString(interact, "adapter") != "audited_TS_Common_v1" ||
                    ReadString(interact, "destination_status") != "walkable" ||
                    !interact.TryGetProperty("dependencies_complete", out var complete) ||
                    complete.ValueKind != JsonValueKind.True ||
                    !interact.TryGetProperty("choice", out var choice) ||
                    choice.ValueKind != JsonValueKind.Number ||
                    !choice.TryGetInt32(out int choiceValue) || choiceValue != 0 ||
                    !interact.TryGetProperty("destination", out var destination) ||
                    destination.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Unverified static transfer cannot be enabled");
                (double x, double y) = ReadPoint(npc, "p");
                (double dx, double dy) = ReadPoint(destination, "p");
                portals.Add(new Portal(mapId, spawnId, x, y,
                    ReadPositive(destination, "map_id"), dx, dy));
            }
        }
        return new PortalCatalog(portals, maximumInteractionDistance);
    }

    private static bool ValidPosition(double x, double y) =>
        double.IsFinite(x) && double.IsFinite(y) &&
        x >= 0 && y >= 0 && x <= int.MaxValue && y <= int.MaxValue;

    private static (double X, double Y) ReadPoint(JsonElement source, string property)
    {
        if (!source.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2 ||
            !value[0].TryGetDouble(out double x) ||
            !value[1].TryGetDouble(out double y) || !ValidPosition(x, y))
            throw new InvalidDataException("Invalid NPC position");
        return (x, y);
    }

    private static string ReadString(JsonElement source, string property) =>
        source.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : throw new InvalidDataException("Missing NPC string property: " + property);

    private static int ReadPositive(JsonElement source, string property)
    {
        if (!source.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out int number) || number <= 0)
            throw new InvalidDataException("Invalid NPC numeric property: " + property);
        return number;
    }
}

public enum NpcPortalStatus : byte
{
    Accepted = 0,
    StaleGeneration = 1,
    ReplayedSequence = 2,
    UnknownNpc = 3,
    TooFar = 4,
    DestinationRejected = 5,
    CombatDead = 6
}

public readonly record struct NpcPortalOutcome(
    NpcPortalStatus Status, ActorSnapshot? TransferredActor);
