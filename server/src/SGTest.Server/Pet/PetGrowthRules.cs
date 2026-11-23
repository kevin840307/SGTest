using System.Text.Json;

namespace SGTest.Server.Pet;

public sealed record PetGrowthCurve(
    int MaximumLevel,
    IReadOnlyList<long> LevelUpTotalExperience,
    string EvidenceSource)
{
    public int AdvanceLevel(int currentLevel,long totalExperience)
    {
        if(currentLevel is <1||currentLevel>MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(currentLevel));
        if(totalExperience<0)
            throw new ArgumentOutOfRangeException(nameof(totalExperience));

        int level=currentLevel;
        while(level<MaximumLevel&&
              totalExperience>=LevelUpTotalExperience[level-1])
            level++;
        return level;
    }

    public long LevelUpThreshold(int level)
    {
        if(level is <1||level>MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(level));
        return LevelUpTotalExperience[level-1];
    }
}

/// <summary>
/// Fail-closed loader for source-audited Pet total-EXP thresholds. The runtime
/// consumes a generated manifest rather than the private original CSV itself.
/// </summary>
public static class PetGrowthRuleCatalog
{
    private const long MaxDocumentBytes=1_048_576;
    private const long MaxPetExperience=9_000_000_000_000_000L;

    public static PetGrowthCurve LoadVerified(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file=new FileInfo(path);
        if(!file.Exists)
            throw new FileNotFoundException("Verified pet growth curve not found",path);
        if(file.Length is <2 or >MaxDocumentBytes)
            throw new InvalidDataException("Pet growth curve outside bounded document size");

        using var stream=file.OpenRead();
        using var document=JsonDocument.Parse(stream,new JsonDocumentOptions
        {
            MaxDepth=16,
            CommentHandling=JsonCommentHandling.Disallow,
            AllowTrailingCommas=false
        });
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           !string.Equals(ReadString(root,"schema"),"sgtest.pet_growth_curve/v1",
               StringComparison.Ordinal))
            throw new InvalidDataException("Pet growth curve schema is not verified v1");

        string source=ReadString(root,"source").Trim();
        int maximumLevel=ReadInt(root,"max_level");
        if(source.Length is <1 or >256||maximumLevel!=100)
            throw new InvalidDataException("Pet growth curve provenance/max level is invalid");

        var values=Require(root,"level_up_total_experience",JsonValueKind.Array);
        if(values.GetArrayLength()!=maximumLevel)
            throw new InvalidDataException("Pet growth curve must contain exactly 100 thresholds");

        var thresholds=new long[maximumLevel];
        long previous=0;
        int index=0;
        foreach(var item in values.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Number||!item.TryGetInt64(out long value)||
               value<=previous||value>MaxPetExperience)
                throw new InvalidDataException(
                    $"Pet growth threshold {index+1} is invalid/non-increasing");
            thresholds[index++]=value;
            previous=value;
        }
        return new(maximumLevel,thresholds,source);
    }

    private static JsonElement Require(
        JsonElement parent,string name,JsonValueKind kind)
    {
        if(!parent.TryGetProperty(name,out var value)||value.ValueKind!=kind)
            throw new InvalidDataException($"Pet growth curve missing/invalid {name}");
        return value;
    }

    private static int ReadInt(JsonElement parent,string name)=>
        Require(parent,name,JsonValueKind.Number).TryGetInt32(out int value)
            ?value:throw new InvalidDataException(
                $"Pet growth curve invalid integer {name}");

    private static string ReadString(JsonElement parent,string name)=>
        Require(parent,name,JsonValueKind.String).GetString()??"";
}
