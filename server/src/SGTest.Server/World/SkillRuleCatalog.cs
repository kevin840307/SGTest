using System.Text.Json;

namespace SGTest.Server.World;

public enum SkillUseStatus : byte
{
    Applied = 0,
    Miss = 1,
    TargetMissing = 2,
    TargetDead = 3,
    OutOfRange = 4,
    Blocked = 5,
    Cooldown = 6,
    CombatUnavailable = 7,
    OldMapEpoch = 8,
    ReplayedSequence = 9,
    NoRule = 10,
    InsufficientMp = 11,
    TooManyTargets = 12
}

public enum SkillTargetKind : byte
{
    SingleMonster = 0,
    SelfCenteredArea = 1
}

public readonly record struct VerifiedSkillRule(
    int SkillId, int FixedDamage, double Range, int CooldownTicks,
    int MpCost = 0,
    SkillTargetKind TargetKind = SkillTargetKind.SingleMonster,
    double AreaRadius = 0,
    bool RequireLineOfSight = true);

/// <summary>
/// Explicit operator-admitted fixed-damage skill rules. Single-monster rules
/// preserve the original V1 manifest. New self-centered-area rules require an
/// explicit radius and line-of-sight policy; the catalog never infers those
/// semantics from an effect name. A bounded MP cost is metadata only: callers
/// settle it durably between Prepare and Commit. SP/HP costs, buffs and
/// formula-derived damage remain rejected until their authoritative owners and
/// original rules are verified.
/// </summary>
public static class SkillRuleCatalog
{
    public static IReadOnlyDictionary<int,VerifiedSkillRule> LoadVerified(string path)
    {
        if(string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Verified skill rule manifest path required",nameof(path));
        byte[] content=File.ReadAllBytes(path);
        if(content.Length is <1 or >1_048_576)
            throw new InvalidDataException("Skill manifest size out of range");
        using var doc=JsonDocument.Parse(content);
        var root=doc.RootElement;
        string? manifestKind=Text(root,"kind");
        bool legacySingle=manifestKind=="verified_single_target_skill_rules";
        bool general=manifestKind=="verified_skill_rules";
        if(root.ValueKind!=JsonValueKind.Object ||
            (!legacySingle && !general) ||
            Number(root,"version")!=1 ||
            !Bool(root,"rules_verified") || !Bool(root,"gameplay_ready") ||
            string.IsNullOrWhiteSpace(Text(root,"source_reference")) ||
            Text(root,"source_reference")!.Length>256 ||
            !root.TryGetProperty("rules",out var rules) ||
            rules.ValueKind!=JsonValueKind.Array ||
            rules.GetArrayLength() is <1 or >64)
            throw new InvalidDataException("Skill rules require operator-verified provenance");

        var result=new Dictionary<int,VerifiedSkillRule>();
        foreach(var entry in rules.EnumerateArray())
        {
            if(entry.ValueKind!=JsonValueKind.Object ||
                Text(entry,"damage_kind")!="fixed")
                throw new InvalidDataException(
                    "Only verified fixed_damage skills are admitted");
            string? targetText=Text(entry,"target_kind");
            SkillTargetKind targetKind=targetText switch
            {
                "single_monster"=>SkillTargetKind.SingleMonster,
                "self_centered_area" when general=>SkillTargetKind.SelfCenteredArea,
                _=>throw new InvalidDataException(
                    "Skill target_kind is not admitted by this manifest")
            };
            int skill=Number(entry,"skill_id");
            int damage=Number(entry,"fixed_damage");
            int cooldown=Number(entry,"cooldown_ticks");
            int mpCost=Number(entry,"mp_cost");
            double range=Real(entry,"range");
            double areaRadius=targetKind==SkillTargetKind.SelfCenteredArea
                ? Real(entry,"area_radius") : 0;
            bool requireLos=true;
            if(targetKind==SkillTargetKind.SelfCenteredArea)
            {
                var los=OptionalBool(entry,"line_of_sight");
                if(los is null)
                    throw new InvalidDataException(
                        "Area skill requires explicit line_of_sight policy");
                requireLos=los.Value;
            }

            var rule=new VerifiedSkillRule(
                skill,damage,range,cooldown,mpCost,targetKind,
                areaRadius,requireLos);
            bool rangeValid=targetKind==SkillTargetKind.SingleMonster
                ? double.IsFinite(range) && range is >=1 and <=500
                : double.IsFinite(range) && Math.Abs(range)<=1e-9 &&
                  double.IsFinite(areaRadius) && areaRadius is >=1 and <=4096;
            if(skill is <1 or >100_000_000 ||
                damage is <1 or >100_000_000 ||
                !rangeValid ||
                cooldown is <0 or >3600 ||
                mpCost is <0 or >100_000_000 ||
                !result.TryAdd(skill,rule))
                throw new InvalidDataException("Skill rules contain invalid or duplicate definitions");
        }
        return result;
    }

    private static string? Text(JsonElement value,string name) =>
        value.TryGetProperty(name,out var property) &&
        property.ValueKind==JsonValueKind.String ? property.GetString() : null;

    private static int Number(JsonElement value,string name) =>
        value.TryGetProperty(name,out var property) &&
        property.TryGetInt32(out int number) ? number : int.MinValue;

    private static double Real(JsonElement value,string name) =>
        value.TryGetProperty(name,out var property) &&
        property.TryGetDouble(out double number) ? number : double.NaN;

    private static bool Bool(JsonElement value,string name) =>
        value.TryGetProperty(name,out var property) &&
        property.ValueKind==JsonValueKind.True;

    private static bool? OptionalBool(JsonElement value,string name)
    {
        if(!value.TryGetProperty(name,out var property))return null;
        return property.ValueKind switch
        {
            JsonValueKind.True=>true,
            JsonValueKind.False=>false,
            _=>null
        };
    }
}
