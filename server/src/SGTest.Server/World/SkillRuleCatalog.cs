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
    NoRule = 10
}

public readonly record struct VerifiedSkillRule(
    int SkillId, int FixedDamage, double Range, int CooldownTicks);

/// <summary>
/// Explicit operator-admitted first-slice skill rules. This intentionally
/// supports only single-monster fixed damage with zero MP cost. MP/SP, AoE,
/// buffs and formula-derived damage are rejected until their authoritative
/// state owners and original rules are verified.
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
        if(root.ValueKind!=JsonValueKind.Object ||
            Text(root,"kind")!="verified_single_target_skill_rules" ||
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
                Text(entry,"target_kind")!="single_monster" ||
                Text(entry,"damage_kind")!="fixed" ||
                Number(entry,"mp_cost")!=0)
                throw new InvalidDataException(
                    "Only verified single_monster fixed_damage zero-MP skills are admitted");
            int skill=Number(entry,"skill_id");
            int damage=Number(entry,"fixed_damage");
            int cooldown=Number(entry,"cooldown_ticks");
            double range=Real(entry,"range");
            var rule=new VerifiedSkillRule(skill,damage,range,cooldown);
            if(skill is <1 or >100_000_000 ||
                damage is <1 or >100_000_000 ||
                !double.IsFinite(range) || range is <1 or >500 ||
                cooldown is <1 or >3600 ||
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
}
