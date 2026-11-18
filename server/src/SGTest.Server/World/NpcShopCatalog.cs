using System.Text.Json;
using SGTest.Server.Identity;

namespace SGTest.Server.World;

/// <summary>
/// Operator-admitted shop economics joined to NPC positions emitted by the
/// single audited NPC converter. The shop manifest never supplies coordinates:
/// proximity always uses the source-derived map-local NPC spawn position.
/// </summary>
public sealed class NpcShopCatalog
{
    public sealed record Item(
        int ItemDefinitionId,long BuyPrice,long SellPrice);
    public sealed record Shop(
        int MapId,int NpcSpawnId,double NpcX,double NpcY,
        string SourceReference,IReadOnlyDictionary<int,Item> Items);

    private readonly Dictionary<(int MapId,int SpawnId),Shop> _shops;
    public int Count=>_shops.Count;
    public double MaximumInteractionDistance{get;}

    public NpcShopCatalog(
        IEnumerable<Shop> shops,double maximumInteractionDistance=96)
    {
        ArgumentNullException.ThrowIfNull(shops);
        if(!double.IsFinite(maximumInteractionDistance)||
           maximumInteractionDistance is <16 or >256)
            throw new ArgumentOutOfRangeException(nameof(maximumInteractionDistance));
        MaximumInteractionDistance=maximumInteractionDistance;
        _shops=new();
        foreach(var shop in shops)
        {
            if(shop.MapId<=0||shop.NpcSpawnId<=0||
               !ValidPosition(shop.NpcX,shop.NpcY)||
               string.IsNullOrWhiteSpace(shop.SourceReference)||
               shop.SourceReference.Length>512||
               shop.Items is null||shop.Items.Count is <1 or >SGTest.Server.Wire.WireProtocol.ShopSnapshotMaxItems||
               !_shops.TryAdd((shop.MapId,shop.NpcSpawnId),shop))
                throw new ArgumentException("Malformed/duplicate NPC shop",nameof(shops));
            foreach(var item in shop.Items.Values)
                if(item.ItemDefinitionId is <1 or >100_000_000||
                   item.BuyPrice is <1 or >1_000_000_000_000L||
                   item.SellPrice<0||item.SellPrice>item.BuyPrice)
                    throw new ArgumentException("Invalid NPC shop item",nameof(shops));
        }
        if(_shops.Count>10000)
            throw new ArgumentException("Shop index is unbounded",nameof(shops));
    }

    public bool TryGet(int mapId,int spawnId,out Shop shop)=>
        _shops.TryGetValue((mapId,spawnId),out shop!);

    public ShopAccessStatus Access(ActorSnapshot actor,int npcSpawnId)
    {
        if(!TryGet(actor.MapId,npcSpawnId,out var shop))
            return ShopAccessStatus.UnknownShop;
        double dx=actor.X-shop.NpcX,dy=actor.Y-shop.NpcY;
        double distance=MaximumInteractionDistance;
        return dx*dx+dy*dy<=distance*distance
            ? ShopAccessStatus.Allowed : ShopAccessStatus.TooFar;
    }

    public static NpcShopCatalog LoadVerified(
        string manifestPath,IEnumerable<string> npcJsonPaths,
        double maximumInteractionDistance=96)
    {
        if(string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("Shop manifest path required",nameof(manifestPath));
        var positions=LoadNpcPositions(npcJsonPaths);
        var file=new FileInfo(manifestPath);
        if(!file.Exists||file.Length is <40 or >2*1024*1024)
            throw new InvalidDataException("Shop manifest missing or exceeds bounded size");
        using var stream=file.OpenRead();
        using var document=JsonDocument.Parse(stream,new JsonDocumentOptions{
            MaxDepth=20,AllowTrailingCommas=false,
            CommentHandling=JsonCommentHandling.Disallow});
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           ReadString(root,"kind")!="verified_npc_shop_rules"||
           ReadInt(root,"version")!=1||
           !ReadTrue(root,"rules_verified")||
           !ReadTrue(root,"gameplay_ready"))
            throw new InvalidDataException("Shop manifest is not explicitly verified");
        string source=ReadString(root,"source_reference");
        if(string.IsNullOrWhiteSpace(source)||source.Length>400)
            throw new InvalidDataException("Invalid shop source reference");
        if(!root.TryGetProperty("shops",out var shops)||
           shops.ValueKind!=JsonValueKind.Array||
           shops.GetArrayLength() is <1 or >10000)
            throw new InvalidDataException("Invalid shop list");

        var result=new List<Shop>();
        var seen=new HashSet<(int,int)>();
        foreach(var row in shops.EnumerateArray())
        {
            int map=ReadPositive(row,"map_id");
            int spawn=ReadPositive(row,"npc_spawn_id");
            if(!seen.Add((map,spawn))||
               !positions.TryGetValue((map,spawn),out var pos))
                throw new InvalidDataException(
                    "Shop references missing/duplicate source NPC");
            if(!row.TryGetProperty("items",out var items)||
               items.ValueKind!=JsonValueKind.Array||
               items.GetArrayLength() is <1 or >SGTest.Server.Wire.WireProtocol.ShopSnapshotMaxItems)
                throw new InvalidDataException("Invalid shop item list");
            var dictionary=new Dictionary<int,Item>();
            foreach(var entry in items.EnumerateArray())
            {
                int item=ReadPositive(entry,"item_definition_id");
                long buy=ReadLong(entry,"buy_price");
                long sell=ReadLong(entry,"sell_price");
                if(item>100_000_000||buy is <1 or >1_000_000_000_000L||
                   sell<0||sell>buy||
                   !dictionary.TryAdd(item,new(item,buy,sell)))
                    throw new InvalidDataException("Invalid/duplicate shop item");
            }
            result.Add(new(map,spawn,pos.X,pos.Y,
                source+$"#map={map};npc={spawn}",dictionary));
        }
        return new(result,maximumInteractionDistance);
    }

    public TrustedShopPrice Price(Shop shop,int itemDefinitionId)
    {
        if(!shop.Items.TryGetValue(itemDefinitionId,out var item))
            return new(shop.NpcSpawnId,itemDefinitionId,0,0,shop.SourceReference);
        return new(shop.NpcSpawnId,item.ItemDefinitionId,
            item.BuyPrice,item.SellPrice,shop.SourceReference);
    }

    private static Dictionary<(int,int),(double X,double Y)> LoadNpcPositions(
        IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var result=new Dictionary<(int,int),(double,double)>();
        var seenMaps=new HashSet<int>();
        foreach(string path in paths)
        {
            var file=new FileInfo(path);
            if(!file.Exists||file.Length is <30 or >2*1024*1024)
                throw new InvalidDataException("NPC content missing or exceeds bounded size");
            using var stream=file.OpenRead();
            using var doc=JsonDocument.Parse(stream,new JsonDocumentOptions{
                MaxDepth=20,AllowTrailingCommas=false,
                CommentHandling=JsonCommentHandling.Disallow});
            var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||
               ReadString(root,"schema")!="sgtest.npcs/v1"||
               ReadString(root,"coordinate_space")!="world_pixels")
                throw new InvalidDataException("Unsupported private NPC document schema");
            int map=ReadPositive(root,"map_id");
            if(!seenMaps.Add(map)||!root.TryGetProperty("npcs",out var npcs)||
               npcs.ValueKind!=JsonValueKind.Array||npcs.GetArrayLength()>10000)
                throw new InvalidDataException("Invalid NPC map document");
            foreach(var npc in npcs.EnumerateArray())
            {
                int spawn=ReadPositive(npc,"spawn_id");
                if(!npc.TryGetProperty("p",out var p)||p.ValueKind!=JsonValueKind.Array||
                   p.GetArrayLength()!=2||!p[0].TryGetDouble(out double x)||
                   !p[1].TryGetDouble(out double y)||!ValidPosition(x,y)||
                   !result.TryAdd((map,spawn),(x,y)))
                    throw new InvalidDataException("Invalid/duplicate NPC source position");
            }
        }
        return result;
    }

    private static bool ValidPosition(double x,double y)=>
        double.IsFinite(x)&&double.IsFinite(y)&&x>=0&&y>=0&&
        x<=int.MaxValue&&y<=int.MaxValue;
    private static string ReadString(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String
            ? v.GetString()??string.Empty
            : throw new InvalidDataException("Missing string: "+name);
    private static bool ReadTrue(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.True;
    private static int ReadInt(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.TryGetInt32(out int x)
            ? x:throw new InvalidDataException("Invalid integer: "+name);
    private static int ReadPositive(JsonElement e,string name)
    {
        int x=ReadInt(e,name);
        if(x<=0)throw new InvalidDataException("Invalid positive integer: "+name);
        return x;
    }
    private static long ReadLong(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.TryGetInt64(out long x)
            ? x:throw new InvalidDataException("Invalid integer: "+name);
}
