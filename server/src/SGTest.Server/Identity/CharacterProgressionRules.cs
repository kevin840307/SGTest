using System.Text.Json;

namespace SGTest.Server.Identity;

public enum CharacterAttributeKind : byte
{
    Strength = 1,
    Dexterity = 2,
    Constitution = 3,
    Wisdom = 4
}

public sealed record CharacterProgressionLevelRule(
    int Level,long RequiredExperience,int AttributePointsAwarded,
    int BaseHp,int BaseMp,int BaseAttack,int BaseDefense,int BaseHit,int BaseDodge);

public sealed record CharacterRaceProgressionRule(
    IReadOnlyList<int> StrengthAttack,
    IReadOnlyList<int> ConstitutionHp,
    IReadOnlyList<int> DexterityHit,
    IReadOnlyList<int> DexterityDodge,
    IReadOnlyList<int> WisdomMp);

/// <summary>
/// Source-audited character progression rules generated from private Old Server
/// Dat/Player tables. Runtime consumes the generated manifest, never the private
/// source files. The current combat contract intentionally exposes only fields
/// whose source formulas have been verified.
/// </summary>
public sealed class CharacterProgressionRules
{
    public const int MaximumLevel = 100;
    public const int MaximumAttribute = 200;

    private readonly CharacterProgressionLevelRule[] _levels;
    private readonly CharacterRaceProgressionRule[] _races;
    private readonly long[] _consumedBeforeLevel;

    public string EvidenceSource { get; }

    private CharacterProgressionRules(
        CharacterProgressionLevelRule[] levels,
        CharacterRaceProgressionRule[] races,
        string evidenceSource)
    {
        _levels = levels;
        _races = races;
        EvidenceSource = evidenceSource;
        _consumedBeforeLevel = new long[MaximumLevel + 1];
        long consumed = 0;
        for (int level = 2; level <= MaximumLevel; level++)
        {
            consumed = checked(consumed + _levels[level - 2].RequiredExperience);
            _consumedBeforeLevel[level] = consumed;
        }
    }

    public static CharacterProgressionRules LoadVerified(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException("Verified character progression rules not found",path);
        if (file.Length is < 2 or > 8 * 1024 * 1024)
            throw new InvalidDataException("Character progression rules outside bounded document size");

        using var stream = file.OpenRead();
        using var document = JsonDocument.Parse(stream,new JsonDocumentOptions
        {
            MaxDepth = 24,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !string.Equals(ReadString(root,"schema"),
                "sgtest.character_progression_rules/v1",StringComparison.Ordinal) ||
            !ReadBool(root,"rules_verified") || !ReadBool(root,"gameplay_ready") ||
            ReadInt(root,"max_level") != MaximumLevel ||
            ReadInt(root,"max_attribute") != MaximumAttribute)
            throw new InvalidDataException("Character progression rules schema/trust is invalid");

        string source = ReadString(root,"source").Trim();
        if (source.Length is < 1 or > 256)
            throw new InvalidDataException("Character progression provenance is invalid");

        var levelArray = Require(root,"levels",JsonValueKind.Array);
        if (levelArray.GetArrayLength() != MaximumLevel)
            throw new InvalidDataException("Character progression must contain 100 level rows");
        var levels = new CharacterProgressionLevelRule[MaximumLevel];
        int levelIndex = 0;
        foreach (var item in levelArray.EnumerateArray())
        {
            int expected = levelIndex + 1;
            int level = ReadInt(item,"level");
            long required = ReadLong(item,"required_experience");
            int points = ReadInt(item,"attribute_points_awarded");
            if (level != expected || required is < 1 or > 9_000_000_000_000_000L ||
                points is < 0 or > 1000)
                throw new InvalidDataException($"Character progression level {expected} is invalid");
            levels[levelIndex++] = new(
                level,required,points,
                ReadBounded(item,"base_hp",1,2_000_000_000),
                ReadBounded(item,"base_mp",0,2_000_000_000),
                ReadBounded(item,"base_attack",0,100_000_000),
                ReadBounded(item,"base_defense",0,100_000_000),
                ReadBounded(item,"base_hit",0,100_000_000),
                ReadBounded(item,"base_dodge",0,100_000_000));
        }

        var raceRoot = Require(root,"races",JsonValueKind.Object);
        string[] names = ["boy","girl","migu","robot"];
        var races = new CharacterRaceProgressionRule[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            var race = Require(raceRoot,names[i],JsonValueKind.Object);
            races[i] = new(
                ReadArray(race,"strength_attack"),
                ReadArray(race,"constitution_hp"),
                ReadArray(race,"dexterity_hit"),
                ReadArray(race,"dexterity_dodge"),
                ReadArray(race,"wisdom_mp"));
        }
        return new(levels,races,source);
    }

    public int AdvanceLevel(int currentLevel,long totalExperience)
    {
        ValidateLevel(currentLevel);
        if (totalExperience < 0)
            throw new ArgumentOutOfRangeException(nameof(totalExperience));
        int level = currentLevel;
        while (level < MaximumLevel)
        {
            long nextRequiredTotal = checked(
                _consumedBeforeLevel[level] + _levels[level - 1].RequiredExperience);
            if (totalExperience < nextRequiredTotal)
                break;
            level++;
        }
        return level;
    }

    public long CurrentExperience(int level,long totalExperience)
    {
        ValidateLevel(level);
        if (totalExperience < 0)
            throw new ArgumentOutOfRangeException(nameof(totalExperience));
        long residual = totalExperience - _consumedBeforeLevel[level];
        return Math.Max(0,residual);
    }

    /// <summary>
    /// Original CCharInfoLiteItf::CalEXPBar reads PLAYERLVLUP[level-1] and
    /// displays floor(current-level EXP * 100 / required EXP), clamped when
    /// current EXP reaches the threshold. Keep the calculation on the trusted
    /// progression table instead of reconstructing thresholds in the client.
    /// </summary>
    public int ExperiencePercent(int level,long totalExperience)
    {
        long current=CurrentExperience(level,totalExperience);
        long required=_levels[level-1].RequiredExperience;
        if(current>=required)
            return 100;
        return (int)(current*100/required);
    }

    public int AwardedPointsForAdvance(int oldLevel,int newLevel)
    {
        ValidateLevel(oldLevel);
        ValidateLevel(newLevel);
        if (newLevel < oldLevel)
            throw new ArgumentOutOfRangeException(nameof(newLevel));
        int total = 0;
        for (int level = oldLevel; level < newLevel; level++)
            total = checked(total + _levels[level - 1].AttributePointsAwarded);
        return total;
    }

    public CharacterCombatProfileData BuildProfile(
        CharacterCombatProfileData current,
        int level,
        int strength,int dexterity,int constitution,int wisdom,
        int availableAttributePoints,
        bool refillVitals)
    {
        ArgumentNullException.ThrowIfNull(current);
        ValidateLevel(level);
        if (current.Race is < 0 or > 3)
            throw new InvalidOperationException("Character race has no verified progression rule");
        ValidateAttribute(strength);
        ValidateAttribute(dexterity);
        ValidateAttribute(constitution);
        ValidateAttribute(wisdom);
        if (availableAttributePoints is < 0 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(availableAttributePoints));

        var baseRule = _levels[level - 1];
        var race = _races[current.Race];
        int maxHp = checked(baseRule.BaseHp + race.ConstitutionHp[constitution - 1]);
        int maxMp = checked(baseRule.BaseMp + race.WisdomMp[wisdom - 1]);
        int attack = checked(baseRule.BaseAttack + race.StrengthAttack[strength - 1]);
        int hit = checked(baseRule.BaseHit + race.DexterityHit[dexterity - 1]);
        int dodge = checked(baseRule.BaseDodge + race.DexterityDodge[dexterity - 1]);
        int currentHp = refillVitals ? maxHp : Math.Min(current.CurrentHp,maxHp);
        int currentMp = refillVitals ? maxMp : Math.Min(current.CurrentMp,maxMp);
        return current with
        {
            Trust = CombatProfileTrust.ProfileOnlyVerified,
            SourceReference = EvidenceSource,
            Strength = strength,
            Dexterity = dexterity,
            Constitution = constitution,
            Wisdom = wisdom,
            Level = level,
            CurrentHp = currentHp,
            MaxHp = maxHp,
            CurrentMp = currentMp,
            MaxMp = maxMp,
            Attack = attack,
            Defense = baseRule.BaseDefense,
            Hit = hit,
            Dodge = dodge,
            AttackRange = 0,
            AttackCooldownTicks = 0,
            AvailableAttributePoints = availableAttributePoints
        };
    }

    public int MaximumAllocatableAttribute => MaximumAttribute;

    private static void ValidateLevel(int level)
    {
        if (level is < 1 or > MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(level));
    }

    private static void ValidateAttribute(int value)
    {
        if (value is < 1 or > MaximumAttribute)
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static IReadOnlyList<int> ReadArray(JsonElement parent,string name)
    {
        var array = Require(parent,name,JsonValueKind.Array);
        if (array.GetArrayLength() != MaximumAttribute)
            throw new InvalidDataException(
                $"Character progression {name} must contain {MaximumAttribute} rows");
        var values = new int[MaximumAttribute];
        int index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out int value) ||
                value < 0 || value > 2_000_000_000)
                throw new InvalidDataException($"Character progression {name}[{index}] is invalid");
            values[index++] = value;
        }
        return values;
    }

    private static JsonElement Require(JsonElement parent,string name,JsonValueKind kind)
    {
        if (!parent.TryGetProperty(name,out var value) || value.ValueKind != kind)
            throw new InvalidDataException($"Character progression missing/invalid {name}");
        return value;
    }

    private static string ReadString(JsonElement parent,string name) =>
        Require(parent,name,JsonValueKind.String).GetString() ?? "";

    private static bool ReadBool(JsonElement parent,string name)
    {
        var value = Require(parent,name,JsonValueKind.True);
        return value.GetBoolean();
    }

    private static int ReadInt(JsonElement parent,string name) =>
        Require(parent,name,JsonValueKind.Number).TryGetInt32(out int value)
            ? value : throw new InvalidDataException($"Character progression invalid integer {name}");

    private static long ReadLong(JsonElement parent,string name) =>
        Require(parent,name,JsonValueKind.Number).TryGetInt64(out long value)
            ? value : throw new InvalidDataException($"Character progression invalid integer {name}");

    private static int ReadBounded(JsonElement parent,string name,int minimum,int maximum)
    {
        int value = ReadInt(parent,name);
        if (value < minimum || value > maximum)
            throw new InvalidDataException($"Character progression {name} outside bounds");
        return value;
    }
}
