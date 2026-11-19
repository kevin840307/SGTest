using System.Text.Json;

namespace SGTest.Server.Identity;

public readonly record struct RepairItemRule(
    int ItemDefinitionId,long BasePrice,int MaxDurability,string SourceReference)
{
    public int FullRawDurability => checked(MaxDurability * 50);
}

public static class RepairItemCatalog
{
    public static IReadOnlyDictionary<int,RepairItemRule> LoadVerified(string path)
    {
        if(string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Verified repair item manifest path required",nameof(path));
        var bytes=File.ReadAllBytes(path);
        if(bytes.Length is <1 or >2*1024*1024)
            throw new InvalidDataException("Repair item manifest size out of range");
        using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions
        {
            MaxDepth=16,AllowTrailingCommas=false,
            CommentHandling=JsonCommentHandling.Disallow
        });
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           ReadString(root,"kind")!="verified_repair_item_rules"||
           ReadInt(root,"version")!=1||
           !ReadTrue(root,"rules_verified")||
           !ReadTrue(root,"gameplay_ready"))
            throw new InvalidDataException("Repair item rules require explicit verification");
        string source=ReadString(root,"source_reference");
        if(string.IsNullOrWhiteSpace(source)||source.Length>384)
            throw new InvalidDataException("Repair item source reference missing");
        if(!root.TryGetProperty("rules",out var rows)||
           rows.ValueKind!=JsonValueKind.Array||
           rows.GetArrayLength() is <1 or >10000)
            throw new InvalidDataException("Repair item rule list invalid");

        var result=new Dictionary<int,RepairItemRule>();
        foreach(var row in rows.EnumerateArray())
        {
            int item=ReadPositiveInt(row,"item_definition_id");
            long price=ReadPositiveLong(row,"price");
            int max=ReadPositiveInt(row,"max_durability");
            if(price>1_000_000_000_000L||max>42_949_672||
               !ReadTrue(row,"can_repair")||
               !result.TryAdd(item,new(item,price,max,
                   source+$"#item={item};price={price};max={max};raw_scale=50")))
                throw new InvalidDataException(
                    "Repair item rule invalid/duplicate or not explicitly repairable");
        }
        return result;
    }

    private static string ReadString(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String
            ? v.GetString()??string.Empty:string.Empty;
    private static int ReadInt(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.TryGetInt32(out int n)?n:0;
    private static long ReadLong(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.TryGetInt64(out long n)?n:0;
    private static int ReadPositiveInt(JsonElement e,string name)
    {
        int value=ReadInt(e,name);
        if(value<=0)throw new InvalidDataException("Expected positive "+name);
        return value;
    }
    private static long ReadPositiveLong(JsonElement e,string name)
    {
        long value=ReadLong(e,name);
        if(value<=0)throw new InvalidDataException("Expected positive "+name);
        return value;
    }
    private static bool ReadTrue(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.True;
}
