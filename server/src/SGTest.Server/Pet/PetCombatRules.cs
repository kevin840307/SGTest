using System.Text.Json;

namespace SGTest.Server.Pet;

public readonly record struct PetCombatRuleKey(
    int DefinitionId,int EvolutionId,int Level);

public sealed record VerifiedPetCombatRule(
    int DefinitionId,int EvolutionId,int Level,
    int FixedDamage,double AttackRange,long CooldownTicks,string EvidenceSource);

/// <summary>
/// Source-gated pet normal-attack rules. Pet damage, range and cadence must come
/// from audited original evidence; the client only selects a pet and target.
/// Exact level keys deliberately avoid inventing an unverified scaling formula.
/// </summary>
public static class PetCombatRuleCatalog
{
    public const int MaximumRules=4096;

    public static IReadOnlyDictionary<PetCombatRuleKey,VerifiedPetCombatRule> LoadVerified(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var document=JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object)
            throw new InvalidDataException("Pet combat manifest root must be an object");

        string source=root.TryGetProperty("source",out var sourceElement)
            ? sourceElement.GetString()?.Trim() ?? "" : "";
        if(source.Length is <3 or >512)
            throw new InvalidDataException("Pet combat manifest requires audited source");

        if(!root.TryGetProperty("rules",out var rulesElement) ||
           rulesElement.ValueKind!=JsonValueKind.Array)
            throw new InvalidDataException("Pet combat manifest requires rules array");
        if(rulesElement.GetArrayLength()>MaximumRules)
            throw new InvalidDataException("Pet combat manifest exceeds rule capacity");

        var rules=new Dictionary<PetCombatRuleKey,VerifiedPetCombatRule>();
        foreach(JsonElement item in rulesElement.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object)
                throw new InvalidDataException("Pet combat rule must be an object");
            int definition=RequiredInt(item,"definition_id");
            int evolution=RequiredInt(item,"evolution_id");
            int level=RequiredInt(item,"level");
            int damage=RequiredInt(item,"fixed_damage");
            double range=RequiredDouble(item,"attack_range");
            long cooldown=RequiredLong(item,"cooldown_ticks");
            if(definition is <=0 or >100_000_000 ||
               evolution is <0 or >65_535 ||
               level is <=0 or >1_000_000 ||
               damage is <=0 or >100_000_000 ||
               !double.IsFinite(range) || range is <=0 or >500 ||
               cooldown is <0 or >3_600)
                throw new InvalidDataException("Pet combat rule value is out of range");

            var key=new PetCombatRuleKey(definition,evolution,level);
            if(!rules.TryAdd(key,new(
                definition,evolution,level,damage,range,cooldown,source)))
                throw new InvalidDataException(
                    $"Duplicate pet combat rule {definition}/{evolution}/Lv{level}");
        }
        return rules;
    }

    private static int RequiredInt(JsonElement item,string name) =>
        item.TryGetProperty(name,out var value) && value.TryGetInt32(out int result)
            ? result : throw new InvalidDataException($"Pet combat rule missing {name}");

    private static long RequiredLong(JsonElement item,string name) =>
        item.TryGetProperty(name,out var value) && value.TryGetInt64(out long result)
            ? result : throw new InvalidDataException($"Pet combat rule missing {name}");

    private static double RequiredDouble(JsonElement item,string name) =>
        item.TryGetProperty(name,out var value) && value.TryGetDouble(out double result)
            ? result : throw new InvalidDataException($"Pet combat rule missing {name}");
}
