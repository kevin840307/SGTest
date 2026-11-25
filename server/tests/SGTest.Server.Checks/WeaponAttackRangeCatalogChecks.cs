using SGTest.Server.Combat;
using System.Text.Json;

static class WeaponAttackRangeCatalogChecks
{
    internal static void Verify(string root)
    {
        Checks.Run("weapon range manifest accepts complete three-way verified rules", () =>
        {
            string path=Fixture(root,partial:false,unresolved:Array.Empty<int>());
            var rules=WeaponAttackRangeCatalog.LoadVerified(path,2);
            Checks.Require(rules.Count==2,"weapon range count");
            Checks.Require(rules[31548].OldItemIndex==1587 &&
                           rules[31548].AttackRange==140,
                           "weapon range definition bridge");
            Checks.Require(rules[31571].OldItemIndex==31225 &&
                           rules[31571].AttackRange==60,
                           "weapon range second definition bridge");
        });

        Checks.Run("weapon range manifest rejects partial unresolved authority", () =>
        {
            string path=Fixture(root,partial:true,unresolved:new[]{1587});
            bool rejected=false;
            try { WeaponAttackRangeCatalog.LoadVerified(path,2); }
            catch(InvalidDataException) { rejected=true; }
            Checks.Require(rejected,"partial weapon range accepted");
        });

        Checks.Run("weapon range manifest rejects duplicate Old identity", () =>
        {
            string path=Fixture(root,partial:false,unresolved:Array.Empty<int>(),
                duplicateOld:true);
            bool rejected=false;
            try { WeaponAttackRangeCatalog.LoadVerified(path,2); }
            catch(InvalidDataException) { rejected=true; }
            Checks.Require(rejected,"duplicate Old weapon identity accepted");
        });

        Checks.Run("weapon range manifest rejects truncated official Old corpus", () =>
        {
            string path=Fixture(root,partial:false,unresolved:Array.Empty<int>());
            bool rejected=false;
            try { WeaponAttackRangeCatalog.LoadVerified(path); }
            catch(InvalidDataException) { rejected=true; }
            Checks.Require(rejected,"two-rule manifest misrepresented complete 73-rule corpus");
        });

        Checks.Run("weapon range manifest rejects inconsistent attack-distance audit", () =>
        {
            string path=Fixture(root,partial:false,unresolved:Array.Empty<int>(),
                corruptDistances:true);
            bool rejected=false;
            try { WeaponAttackRangeCatalog.LoadVerified(path,2); }
            catch(InvalidDataException) { rejected=true; }
            Checks.Require(rejected,"corrupt attack-distance audit accepted");
        });
    }

    private static string Fixture(
        string root,bool partial,int[] unresolved,bool duplicateOld=false,
        bool corruptDistances=false)
    {
        string path=Path.Combine(root,"weapon_definition_ranges_"+
            Guid.NewGuid().ToString("N")+".json");
        object[] rules=
        {
            new
            {
                item_definition_id=31548,
                old_item_index=1587,
                attack_distance=140,
                use_item=0,
                runtime_source_sha256=new string('a',64)
            },
            new
            {
                item_definition_id=31571,
                old_item_index=duplicateOld?1587:31225,
                attack_distance=60,
                use_item=0,
                runtime_source_sha256=new string('b',64)
            }
        };
        var document=new
        {
            kind="verified_weapon_definition_attack_ranges",
            version=1,
            rules_verified=true,
            rule_scope="three_way_matches_only_fail_closed",
            source_reference=new
            {
                runtime_items="runtime fixture",
                hk_items="HK fixture",
                old_weapons="Old fixture"
            },
            rules,
            audit=new
            {
                runtime_item_count=10,
                runtime_weapon_count=2,
                hk_unique_name_matches=2,
                hk_weapon_matches=2,
                old_weapon_count=2,
                verified_rule_count=2,
                unresolved_old_item_indices=unresolved,
                matched_attack_distance_values=corruptDistances
                    ?new[]{60,120}:new[]{60,140},
                hk_source_sha256=new string('c',64),
                old_source_sha256=new string('d',64),
                old_record_bytes=1247
            },
            partial,
            status="pass"
        };
        File.WriteAllText(path,JsonSerializer.Serialize(document));
        return path;
    }
}
