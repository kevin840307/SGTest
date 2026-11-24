using System.Text.Json;

namespace SGTest.Server.Combat;

public sealed record VerifiedWeaponAttackRangeRule(
    int ItemDefinitionId,int OldItemIndex,double AttackRange,
    int UseItem,string RuntimeSourceSha256);

/// <summary>
/// Loads only a complete three-way verified SGTest definition -> Old ItemIndex
/// attack-distance manifest. Unknown definitions are intentionally absent so
/// callers can keep combat fail-closed.
/// </summary>
public static class WeaponAttackRangeCatalog
{
    public static IReadOnlyDictionary<int,VerifiedWeaponAttackRangeRule>
        LoadVerified(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file=new FileInfo(path);
        if(!file.Exists)
            throw new FileNotFoundException("Verified weapon range manifest missing",path);
        if(file.Length is <2 or >1_048_576)
            throw new InvalidDataException("Verified weapon range manifest size out of bound");

        using var stream=file.OpenRead();
        using var document=JsonDocument.Parse(stream,new JsonDocumentOptions
        {
            MaxDepth=16,
            AllowTrailingCommas=false,
            CommentHandling=JsonCommentHandling.Disallow
        });
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           Text(root,"kind")!="verified_weapon_definition_attack_ranges"||
           Number(root,"version")!=1||
           !Bool(root,"rules_verified")||
           Text(root,"rule_scope")!="three_way_matches_only_fail_closed"||
           Bool(root,"partial")||
           Text(root,"status")!="pass")
            throw new InvalidDataException(
                "Weapon attack-range manifest is not complete verified gameplay content");

        var sources=Read(root,"source_reference",JsonValueKind.Object);
        foreach(string name in new[]{"runtime_items","hk_items","old_weapons"})
        {
            string source=Text(sources,name).Trim();
            if(source.Length is <1 or >256)
                throw new InvalidDataException(
                    "Weapon attack-range source reference invalid: "+name);
        }

        var rows=Read(root,"rules",JsonValueKind.Array);
        if(rows.GetArrayLength() is <1 or >4096)
            throw new InvalidDataException("Weapon attack-range rule count out of bounds");

        var byDefinition=new Dictionary<int,VerifiedWeaponAttackRangeRule>();
        var oldIds=new HashSet<int>();
        foreach(var row in rows.EnumerateArray())
        {
            if(row.ValueKind!=JsonValueKind.Object)
                throw new InvalidDataException("Invalid weapon attack-range rule");
            int definition=Number(row,"item_definition_id");
            int old=Number(row,"old_item_index");
            int range=Number(row,"attack_distance");
            int useItem=Number(row,"use_item");
            string sha=Text(row,"runtime_source_sha256");
            if(definition is <1 or >100_000_000||
               old is <1 or >ushort.MaxValue||
               range is <1 or >500||
               useItem is <0 or >ushort.MaxValue||
               !Sha256(sha)||
               byDefinition.ContainsKey(definition)||
               !oldIds.Add(old))
                throw new InvalidDataException(
                    "Unknown, duplicate or invalid weapon attack-range rule");
            byDefinition.Add(definition,new(
                definition,old,range,useItem,sha));
        }

        var audit=Read(root,"audit",JsonValueKind.Object);
        if(Number(audit,"verified_rule_count")!=byDefinition.Count||
           Number(audit,"old_weapon_count")!=byDefinition.Count)
            throw new InvalidDataException("Weapon attack-range audit count mismatch");
        var unresolved=Read(audit,"unresolved_old_item_indices",JsonValueKind.Array);
        if(unresolved.GetArrayLength()!=0)
            throw new InvalidDataException(
                "Weapon attack-range manifest still has unresolved Old weapons");

        return byDefinition;
    }

    private static bool Sha256(string value)=>
        value.Length==64&&value.All(c=>
            c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static JsonElement Read(
        JsonElement parent,string name,JsonValueKind kind)
    {
        if(!parent.TryGetProperty(name,out var value)||value.ValueKind!=kind)
            throw new InvalidDataException(
                "Weapon attack-range property invalid: "+name);
        return value;
    }

    private static int Number(JsonElement parent,string name)=>
        Read(parent,name,JsonValueKind.Number).TryGetInt32(out int value)
            ?value:throw new InvalidDataException(
                "Weapon attack-range integer invalid: "+name);

    private static string Text(JsonElement parent,string name)=>
        Read(parent,name,JsonValueKind.String).GetString()??"";

    private static bool Bool(JsonElement parent,string name)
    {
        if(!parent.TryGetProperty(name,out var value)||
           value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException(
                "Weapon attack-range boolean invalid: "+name);
        return value.GetBoolean();
    }
}
