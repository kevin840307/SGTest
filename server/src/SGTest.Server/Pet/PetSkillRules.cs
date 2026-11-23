using System.Text.Json;

namespace SGTest.Server.Pet;

public readonly record struct PetSkillRuleKey(
    int DefinitionId,int EvolutionId,int Level,int SkillId);

public sealed record VerifiedPetSkillRule(
    int DefinitionId,int EvolutionId,int Level,int SkillId,
    int FixedDamage,double Range,long CooldownTicks,string EvidenceSource);

/// <summary>
/// Source-gated V1 pet skill rules. Only exact pet/evolution/level/skill keys are
/// admitted. Missing or ambiguous original evidence stays unavailable rather than
/// inheriting player skill formulas or inventing pet scaling.
/// </summary>
public static class PetSkillRuleCatalog
{
    public const int MaximumRules=4096;
    public const string Schema="sgtest.pet_skill_rules/v1";

    public static IReadOnlyDictionary<PetSkillRuleKey,VerifiedPetSkillRule> LoadVerified(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var document=JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           !root.TryGetProperty("schema",out var schemaElement)||
           schemaElement.GetString()!=Schema)
            throw new InvalidDataException("Pet skill manifest schema is invalid");

        string source=root.TryGetProperty("source",out var sourceElement)
            ?sourceElement.GetString()?.Trim()??"": "";
        if(source.Length is <3 or >512)
            throw new InvalidDataException("Pet skill manifest requires audited source");

        if(!root.TryGetProperty("rules",out var rulesElement)||
           rulesElement.ValueKind!=JsonValueKind.Array)
            throw new InvalidDataException("Pet skill manifest requires rules array");
        if(rulesElement.GetArrayLength()>MaximumRules)
            throw new InvalidDataException("Pet skill manifest exceeds rule capacity");

        var rules=new Dictionary<PetSkillRuleKey,VerifiedPetSkillRule>();
        foreach(JsonElement item in rulesElement.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object)
                throw new InvalidDataException("Pet skill rule must be an object");
            int definition=RequiredInt(item,"definition_id");
            int evolution=RequiredInt(item,"evolution_id");
            int level=RequiredInt(item,"level");
            int skillId=RequiredInt(item,"skill_id");
            int damage=RequiredInt(item,"fixed_damage");
            double range=RequiredDouble(item,"range");
            long cooldown=RequiredLong(item,"cooldown_ticks");
            if(definition is <=0 or >100_000_000||
               evolution is <0 or >65_535||
               level is <=0 or >1_000_000||
               skillId is <=0 or >100_000_000||
               damage is <=0 or >100_000_000||
               !double.IsFinite(range)||range is <=0 or >500||
               cooldown is <0 or >3_600)
                throw new InvalidDataException("Pet skill rule value is out of range");

            var key=new PetSkillRuleKey(definition,evolution,level,skillId);
            if(!rules.TryAdd(key,new(
                definition,evolution,level,skillId,damage,range,cooldown,source)))
                throw new InvalidDataException(
                    $"Duplicate pet skill rule {definition}/{evolution}/Lv{level}/Skill{skillId}");
        }
        return rules;
    }

    private static int RequiredInt(JsonElement item,string name)=>
        item.TryGetProperty(name,out var value)&&value.TryGetInt32(out int result)
            ?result:throw new InvalidDataException($"Pet skill rule missing {name}");

    private static long RequiredLong(JsonElement item,string name)=>
        item.TryGetProperty(name,out var value)&&value.TryGetInt64(out long result)
            ?result:throw new InvalidDataException($"Pet skill rule missing {name}");

    private static double RequiredDouble(JsonElement item,string name)=>
        item.TryGetProperty(name,out var value)&&value.TryGetDouble(out double result)
            ?result:throw new InvalidDataException($"Pet skill rule missing {name}");
}
