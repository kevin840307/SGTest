using SGTest.Server.World;
using System.Text.Json;

namespace SGTest.Server.Npc;

public enum RepairNpcAccessStatus : byte
{
    Allowed = 0,
    StaleGeneration = 1,
    UnknownRepair = 2,
    TooFar = 3,
    PlayerDead = 4
}

public sealed class NpcRepairCatalog
{
    public sealed record RepairPoint(
        int MapId,int NpcSpawnId,double NpcX,double NpcY,
        int RepairRate,string SourceReference);

    private readonly Dictionary<(int MapId,int SpawnId),RepairPoint> _points;
    public int Count=>_points.Count;
    public double MaximumInteractionDistance { get; }

    public NpcRepairCatalog(
        IEnumerable<RepairPoint> points,double maximumInteractionDistance=96)
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
               !double.IsFinite(point.NpcX)||!double.IsFinite(point.NpcY)||
               point.NpcX<0||point.NpcY<0||
               point.RepairRate is <1 or >255||
               string.IsNullOrWhiteSpace(point.SourceReference)||
               point.SourceReference.Length>512||
               !_points.TryAdd((point.MapId,point.NpcSpawnId),point))
                throw new ArgumentException("Malformed/duplicate repair NPC",nameof(points));
        }
        if(_points.Count>10000)
            throw new ArgumentException("Repair NPC index is unbounded",nameof(points));
    }

    public bool TryGet(int mapId,int spawnId,out RepairPoint point)=>
        _points.TryGetValue((mapId,spawnId),out point!);

    public RepairNpcAccessStatus Access(ActorSnapshot actor,int npcSpawnId)
    {
        if(!TryGet(actor.MapId,npcSpawnId,out var point))
            return RepairNpcAccessStatus.UnknownRepair;
        double dx=actor.X-point.NpcX,dy=actor.Y-point.NpcY;
        double d=MaximumInteractionDistance;
        return dx*dx+dy*dy<=d*d
            ? RepairNpcAccessStatus.Allowed:RepairNpcAccessStatus.TooFar;
    }

    public static NpcRepairCatalog LoadVerified(
        string manifestPath,IEnumerable<string> npcJsonPaths,
        double maximumInteractionDistance=96)
    {
        if(string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("Repair manifest path required",nameof(manifestPath));
        var positions=NpcShopCatalog.LoadNpcPositions(npcJsonPaths);
        var file=new FileInfo(manifestPath);
        if(!file.Exists||file.Length is <40 or >2*1024*1024)
            throw new InvalidDataException("Repair manifest missing or too large");
        using var stream=file.OpenRead();
        using var document=JsonDocument.Parse(stream,new JsonDocumentOptions
        {
            MaxDepth=20,AllowTrailingCommas=false,
            CommentHandling=JsonCommentHandling.Disallow
        });
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           ReadString(root,"kind")!="verified_npc_repair_rules"||
           ReadInt(root,"version")!=1||
           !ReadTrue(root,"rules_verified")||
           !ReadTrue(root,"gameplay_ready"))
            throw new InvalidDataException("Repair NPC rules require explicit verification");
        string source=ReadString(root,"source_reference");
        if(string.IsNullOrWhiteSpace(source)||source.Length>384)
            throw new InvalidDataException("Repair NPC source reference missing");
        if(!root.TryGetProperty("repairs",out var rows)||
           rows.ValueKind!=JsonValueKind.Array||
           rows.GetArrayLength() is <1 or >10000)
            throw new InvalidDataException("Invalid repair NPC list");

        var result=new List<RepairPoint>();
        var seen=new HashSet<(int,int)>();
        foreach(var row in rows.EnumerateArray())
        {
            int map=ReadPositive(row,"map_id");
            int spawn=ReadPositive(row,"npc_spawn_id");
            int rate=ReadPositive(row,"repair_rate_percent");
            if(rate>255||!seen.Add((map,spawn))||
               !positions.TryGetValue((map,spawn),out var pos))
                throw new InvalidDataException(
                    "Repair references missing/duplicate NPC or invalid rate");
            result.Add(new(map,spawn,pos.X,pos.Y,rate,
                source+$"#map={map};npc={spawn};rate={rate}"));
        }
        return new(result,maximumInteractionDistance);
    }

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
