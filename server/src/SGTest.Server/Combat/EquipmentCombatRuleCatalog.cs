using System.Text.Json;
using SGTest.Server.Identity;

namespace SGTest.Server.Combat;

public sealed record EquipmentCombatRule(
    int ItemDefinitionId,
    int AttackBonus,
    int DefenseBonus,
    int HitBonus,
    int DodgeBonus,
    bool IsWeapon,
    double AttackRange,
    string SourceReference);

/// <summary>
/// Fail-closed combat modifiers emitted only when runtime item data agrees with
/// original HK item.itd and the Old EquipItem modifier semantics. Weapon ranges
/// additionally require the existing runtime/HK/Old Weapon.itd three-way bridge.
/// </summary>
public static class EquipmentCombatRuleCatalog
{
    public static IReadOnlyDictionary<int,EquipmentCombatRule> LoadVerified(string path)
    {
        if(string.IsNullOrWhiteSpace(path))
            throw new ArgumentException(
                "Verified equipment combat manifest path required",nameof(path));
        byte[] content=File.ReadAllBytes(path);
        if(content.Length is <1 or >2_097_152)
            throw new InvalidDataException(
                "Equipment combat manifest size out of range");
        using var parsed=JsonDocument.Parse(content,new JsonDocumentOptions
        {
            MaxDepth=20,AllowTrailingCommas=false,
            CommentHandling=JsonCommentHandling.Disallow
        });
        JsonElement root=parsed.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           !root.TryGetProperty("kind",out var kind)||
           kind.GetString()!="verified_equipment_combat_rules"||
           !root.TryGetProperty("version",out var version)||
           !version.TryGetInt32(out int v)||v!=1||
           !root.TryGetProperty("rules_verified",out var verified)||
           verified.ValueKind!=JsonValueKind.True||
           !root.TryGetProperty("gameplay_ready",out var ready)||
           ready.ValueKind!=JsonValueKind.True||
           !root.TryGetProperty("source_reference",out var sourceNode)||
           sourceNode.ValueKind!=JsonValueKind.String||
           string.IsNullOrWhiteSpace(sourceNode.GetString())||
           sourceNode.GetString()!.Length>512||
           !root.TryGetProperty("rules",out var rulesNode)||
           rulesNode.ValueKind!=JsonValueKind.Array||
           rulesNode.GetArrayLength() is <1 or >4096)
            throw new InvalidDataException(
                "Equipment combat rules require verified provenance");

        string source=sourceNode.GetString()!;
        var result=new Dictionary<int,EquipmentCombatRule>();
        foreach(JsonElement node in rulesNode.EnumerateArray())
        {
            if(node.ValueKind!=JsonValueKind.Object||
               !TryInt(node,"item_definition_id",out int definition)||
               !TryInt(node,"attack_bonus",out int attack)||
               !TryInt(node,"defense_bonus",out int defense)||
               !TryInt(node,"hit_bonus",out int hit)||
               !TryInt(node,"dodge_bonus",out int dodge)||
               !node.TryGetProperty("is_weapon",out var weaponNode)||
               weaponNode.ValueKind is not
                   (JsonValueKind.True or JsonValueKind.False)||
               !node.TryGetProperty("attack_range",out var rangeNode)||
               !rangeNode.TryGetDouble(out double range)||
               definition is <1 or >100_000_000||
               attack is <0 or >ushort.MaxValue||
               defense is <0 or >ushort.MaxValue||
               hit is <0 or >ushort.MaxValue||
               dodge is <0 or >ushort.MaxValue||
               !double.IsFinite(range)||range<0||range>100_000)
                throw new InvalidDataException(
                    "Invalid equipment combat rule");

            bool isWeapon=weaponNode.GetBoolean();
            if(isWeapon!=(range>0))
                throw new InvalidDataException(
                    "Weapon combat rules require exactly one positive range");
            if(!result.TryAdd(definition,new(
                definition,attack,defense,hit,dodge,isWeapon,range,
                source+$"#item={definition}")))
                throw new InvalidDataException(
                    "Duplicate equipment combat item definition");
        }
        return result;
    }

    private static bool TryInt(JsonElement node,string name,out int value)
    {
        value=0;
        return node.TryGetProperty(name,out var child)&&child.TryGetInt32(out value);
    }
}

public static class SourceVerifiedEquipmentCombatRules
{
    public static bool TryPromote(
        CharacterCombatProfileData profile,
        IReadOnlyList<EquippedItem> equipped,
        IReadOnlyDictionary<int,EquipmentCombatRule> rules,
        int worldTickHz,
        out CharacterCombatProfileData effective)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(equipped);
        ArgumentNullException.ThrowIfNull(rules);
        effective=profile;

        if(equipped.Count==0||
           profile.Trust!=CombatProfileTrust.ProfileOnlyVerified||
           profile.AttackRange!=0||profile.AttackCooldownTicks!=0||
           profile.Level is <1 or >ushort.MaxValue||
           profile.CurrentHp<0||profile.MaxHp<1||
           profile.CurrentHp>profile.MaxHp||
           profile.Attack is <0 or >ushort.MaxValue||
           profile.Defense is <0 or >ushort.MaxValue||
           profile.Hit is <0 or >ushort.MaxValue||
           profile.Dodge is <0 or >ushort.MaxValue)
            return false;

        long attack=profile.Attack;
        long defense=profile.Defense;
        long hit=profile.Hit;
        long dodge=profile.Dodge;
        double attackRange=SourceVerifiedUnarmedCombatRules.UnarmedAttackRange;
        int weaponCount=0;
        string? source=null;

        foreach(EquippedItem item in equipped)
        {
            // Enhancement stat formulas are not source-closed yet. Null
            // durability is the store's verified "full/default" representation;
            // positive raw is usable, while zero is explicitly broken.
            if(item.EnhancementLevel!=0||item.DurabilityRaw is <0 or 0||
               !rules.TryGetValue(item.ItemDefinitionId,out var rule))
                return false;

            attack+=rule.AttackBonus;
            defense+=rule.DefenseBonus;
            hit+=rule.HitBonus;
            dodge+=rule.DodgeBonus;
            source??=rule.SourceReference;
            if(rule.IsWeapon)
            {
                weaponCount++;
                if(weaponCount>1)return false;
                attackRange=rule.AttackRange;
            }
        }

        effective=profile with
        {
            Trust=CombatProfileTrust.Verified,
            SourceReference=
                profile.SourceReference+
                "|SGOEquip::EquipItem->EffAP/EffDP/EffHR/EffDR"+
                "|"+source,
            Attack=(int)Math.Min(ushort.MaxValue,attack),
            Defense=(int)Math.Min(ushort.MaxValue,defense),
            Hit=(int)Math.Min(ushort.MaxValue,hit),
            Dodge=(int)Math.Min(ushort.MaxValue,dodge),
            AttackRange=attackRange,
            AttackCooldownTicks=
                SourceVerifiedUnarmedCombatRules.AttackCooldownTicks(worldTickHz)
        };
        return true;
    }
}
