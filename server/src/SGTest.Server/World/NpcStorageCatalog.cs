using System.Text.Json;

namespace SGTest.Server.World;

public enum StorageAccessStatus : byte
{
    Allowed = 0,
    StaleGeneration = 1,
    UnknownStorage = 2,
    TooFar = 3,
    PlayerDead = 4
}

public sealed class NpcStorageCatalog
{
    public sealed record StoragePoint(
        int MapId,int NpcSpawnId,double NpcX,double NpcY,string SourceReference);

    private readonly Dictionary<(int MapId,int SpawnId),StoragePoint> _points;
    public int Count=>_points.Count;
    public double MaximumInteractionDistance { get; }

    public NpcStorageCatalog(
        IEnumerable<StoragePoint> points,double maximumInteractionDistance=96)
    {
        ArgumentNullException.ThrowIfNull(points);
        if(!double.IsFinite(maximumInteractionDistance)||
           maximumInteractionDistance is <16 or >256)
            throw new ArgumentOutOfRangeException(nameof(maximumInteractionDistance));
        MaximumInteractionDistance=maximumInteractionDistance;
        _points=new();
        foreach(var point in points)
        {
            if(point.MapId<=0||point.NpcSpawnId<=0||
               !ValidPosition(point.NpcX,point.NpcY)||
               string.IsNullOrWhiteSpace(point.SourceReference)||
               point.SourceReference.Length>512||
               !_points.TryAdd((point.MapId,point.NpcSpawnId),point))
                throw new ArgumentException("Malformed/duplicate storage NPC",nameof(points));
        }
        if(_points.Count>10000)
            throw new ArgumentException("Storage NPC index is unbounded",nameof(points));
    }

    public bool TryGet(int mapId,int spawnId,out StoragePoint point)=>
        _points.TryGetValue((mapId,spawnId),out point!);

    public StorageAccessStatus Access(ActorSnapshot actor,int npcSpawnId)
    {
        if(!TryGet(actor.MapId,npcSpawnId,out var point))
            return StorageAccessStatus.UnknownStorage;
        double dx=actor.X-point.NpcX,dy=actor.Y-point.NpcY;
        double distance=MaximumInteractionDistance;
        return dx*dx+dy*dy<=distance*distance
            ? StorageAccessStatus.Allowed:StorageAccessStatus.TooFar;
    }

    public static NpcStorageCatalog LoadVerified(
        string manifestPath,IEnumerable<string> npcJsonPaths,
        double maximumInteractionDistance=96)
    {
        if(string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("Storage manifest path required",nameof(manifestPath));
        var positions=NpcShopCatalog.LoadNpcPositions(npcJsonPaths);
        var file=new FileInfo(manifestPath);
        if(!file.Exists||file.Length is <40 or >2*1024*1024)
            throw new InvalidDataException("Storage manifest missing or exceeds bounded size");
        using var stream=file.OpenRead();
        using var document=JsonDocument.Parse(stream,new JsonDocumentOptions{
            MaxDepth=20,AllowTrailingCommas=false,
            CommentHandling=JsonCommentHandling.Disallow});
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           ReadString(root,"kind")!="verified_npc_storage_rules"||
           ReadInt(root,"version")!=1||
           !ReadTrue(root,"rules_verified")||
           !ReadTrue(root,"gameplay_ready"))
            throw new InvalidDataException("Storage manifest is not explicitly verified");
        string source=ReadString(root,"source_reference");
        if(string.IsNullOrWhiteSpace(source)||source.Length>384)
            throw new InvalidDataException("Invalid storage source reference");
        if(!root.TryGetProperty("storages",out var rows)||
           rows.ValueKind!=JsonValueKind.Array||
           rows.GetArrayLength() is <1 or >10000)
            throw new InvalidDataException("Invalid storage NPC list");

        var result=new List<StoragePoint>();
        var seen=new HashSet<(int,int)>();
        foreach(var row in rows.EnumerateArray())
        {
            int map=ReadPositive(row,"map_id");
            int spawn=ReadPositive(row,"npc_spawn_id");
            if(!seen.Add((map,spawn))||
               !positions.TryGetValue((map,spawn),out var pos))
                throw new InvalidDataException(
                    "Storage references missing/duplicate source NPC");
            result.Add(new(map,spawn,pos.X,pos.Y,
                source+$"#map={map};npc={spawn}"));
        }
        return new(result,maximumInteractionDistance);
    }

    private static bool ValidPosition(double x,double y)=>
        double.IsFinite(x)&&double.IsFinite(y)&&x>=0&&y>=0&&
        x<=int.MaxValue&&y<=int.MaxValue;
    private static string ReadString(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String
            ? v.GetString()??string.Empty:string.Empty;
    private static int ReadInt(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.TryGetInt32(out int n)?n:0;
    private static int ReadPositive(JsonElement e,string name)
    {
        int value=ReadInt(e,name);
        if(value<=0)throw new InvalidDataException("Expected positive "+name);
        return value;
    }
    private static bool ReadTrue(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.True;
}
