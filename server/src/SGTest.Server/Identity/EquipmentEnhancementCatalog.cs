using System.Text.Json;

namespace SGTest.Server.Identity;

public readonly record struct EquipmentEnhancementStepRule(
    int FromLevel,int ToLevel,long MoneyCost,int SuccessPpm,
    bool ConsumeMoneyOnFailure,string SourceReference);

public sealed record EquipmentEnhancementItemRule(
    int ItemDefinitionId,
    IReadOnlyDictionary<int,EquipmentEnhancementStepRule> Steps,
    string SourceReference);

/// <summary>
/// Enhancement V1 is intentionally source-driven. It supports only explicitly
/// verified no-change failures. Downgrade, break/destroy, materials, protection
/// items and stat formulas are NOT inferred by this loader.
/// </summary>
public static class EquipmentEnhancementCatalog
{
    public const int ProbabilityScale=1_000_000;

    public static IReadOnlyDictionary<int,EquipmentEnhancementItemRule>
        LoadVerified(string path)
    {
        if(string.IsNullOrWhiteSpace(path))
            throw new ArgumentException(
                "Verified enhancement rule manifest path required",nameof(path));
        byte[] content=File.ReadAllBytes(path);
        if(content.Length is <1 or >2_097_152)
            throw new InvalidDataException("Enhancement manifest size out of range");
        using var parsed=JsonDocument.Parse(content,new JsonDocumentOptions
        {
            MaxDepth=24,AllowTrailingCommas=false,
            CommentHandling=JsonCommentHandling.Disallow
        });
        var root=parsed.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           !root.TryGetProperty("kind",out var kind)||
           kind.GetString()!="verified_equipment_enhancement_rules"||
           !root.TryGetProperty("version",out var version)||
           !version.TryGetInt32(out int v)||v!=1||
           !root.TryGetProperty("rules_verified",out var verified)||
           verified.ValueKind!=JsonValueKind.True||
           !root.TryGetProperty("gameplay_ready",out var ready)||
           ready.ValueKind!=JsonValueKind.True||
           !root.TryGetProperty("source_reference",out var source)||
           source.ValueKind!=JsonValueKind.String||
           string.IsNullOrWhiteSpace(source.GetString())||
           source.GetString()!.Length>384||
           !root.TryGetProperty("rules",out var rules)||
           rules.ValueKind!=JsonValueKind.Array||
           rules.GetArrayLength() is <1 or >4096)
            throw new InvalidDataException(
                "Enhancement rules require operator-verified provenance");

        string sourceRef=source.GetString()!;
        var result=new Dictionary<int,EquipmentEnhancementItemRule>();
        foreach(var item in rules.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object||
               !item.TryGetProperty("item_definition_id",out var definitionNode)||
               !definitionNode.TryGetInt32(out int definition)||
               definition is <1 or >100_000_000||
               !item.TryGetProperty("steps",out var stepsNode)||
               stepsNode.ValueKind!=JsonValueKind.Array||
               stepsNode.GetArrayLength() is <1 or >255)
                throw new InvalidDataException("Invalid enhancement item rule");

            var steps=new Dictionary<int,EquipmentEnhancementStepRule>();
            foreach(var step in stepsNode.EnumerateArray())
            {
                if(step.ValueKind!=JsonValueKind.Object||
                   !step.TryGetProperty("from_level",out var fromNode)||
                   !fromNode.TryGetInt32(out int from)||
                   !step.TryGetProperty("to_level",out var toNode)||
                   !toNode.TryGetInt32(out int to)||
                   !step.TryGetProperty("money_cost",out var moneyNode)||
                   !moneyNode.TryGetInt64(out long money)||
                   !step.TryGetProperty("success_ppm",out var ppmNode)||
                   !ppmNode.TryGetInt32(out int ppm)||
                   !step.TryGetProperty("consume_money_on_failure",out var consumeNode)||
                   consumeNode.ValueKind is not
                       (JsonValueKind.True or JsonValueKind.False)||
                   !step.TryGetProperty("failure_mode",out var failureNode)||
                   failureNode.ValueKind!=JsonValueKind.String||
                   failureNode.GetString()!="no_change"||
                   from is <0 or >254||to is <1 or >255||to<=from||
                   money is <0 or >SqliteIdentityStore.MaximumMoney||
                   ppm is <0 or >ProbabilityScale||
                   !steps.TryAdd(from,new(
                       from,to,money,ppm,consumeNode.GetBoolean(),
                       sourceRef+$"#item={definition};from={from};to={to}")))
                    throw new InvalidDataException(
                        "Invalid/duplicate enhancement step rule");
            }
            if(!result.TryAdd(definition,new(definition,steps,sourceRef)))
                throw new InvalidDataException(
                    "Duplicate enhancement item definition");
        }
        return result;
    }
}
