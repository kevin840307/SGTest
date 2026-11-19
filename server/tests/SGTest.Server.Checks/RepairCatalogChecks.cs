using System.Text.Json;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class RepairCatalogChecks
{
    public static void Verify(string root)
    {
        Checks.Run("verified repair item and NPC rules fail closed",
            () => VerifyCatalogs(root));
    }

    private static void VerifyCatalogs(string root)
    {
        string items=Path.Combine(root,"verified-repair-items.json");
        File.WriteAllText(items,JsonSerializer.Serialize(new
        {
            kind="verified_repair_item_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="old:SGORepair+item-data",
            rules=new[]{new
            {
                item_definition_id=3701,price=1000,
                max_durability=100,can_repair=true
            }}
        }));
        var itemRules=RepairItemCatalog.LoadVerified(items);
        Checks.Require(itemRules.Count==1&&
            itemRules[3701].BasePrice==1000&&
            itemRules[3701].MaxDurability==100&&
            itemRules[3701].FullRawDurability==5000,
            "verified repair item rule mismatch");

        string npc=Path.Combine(root,"repair-npc.json");
        File.WriteAllText(npc,JsonSerializer.Serialize(new
        {
            schema="sgtest.npcs/v1",map_id=76,coordinate_space="world_pixels",
            npcs=new[]{new{spawn_id=77,p=new[]{8,8},
                interaction=new{mode="script_required"}}}
        }));
        string repairs=Path.Combine(root,"verified-repair-npcs.json");
        File.WriteAllText(repairs,JsonSerializer.Serialize(new
        {
            kind="verified_npc_repair_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="old:Repair.txt:OpenRepair(10)",
            repairs=new[]{new
            {
                map_id=76,npc_spawn_id=77,repair_rate_percent=10
            }}
        }));
        var npcRules=NpcRepairCatalog.LoadVerified(repairs,[npc]);
        Checks.Require(npcRules.Count==1&&
            npcRules.TryGet(76,77,out var point)&&
            point.RepairRate==10&&point.NpcX==8&&point.NpcY==8,
            "verified repair NPC rule mismatch");

        bool unverifiedItem=false,notRepairable=false,badNpc=false,badRate=false;
        File.WriteAllText(items,JsonSerializer.Serialize(new
        {
            kind="verified_repair_item_rules",version=1,
            rules_verified=false,gameplay_ready=true,
            source_reference="bad",
            rules=new[]{new
            {
                item_definition_id=3701,price=1000,
                max_durability=100,can_repair=true
            }}
        }));
        try{RepairItemCatalog.LoadVerified(items);}
        catch(InvalidDataException){unverifiedItem=true;}

        File.WriteAllText(items,JsonSerializer.Serialize(new
        {
            kind="verified_repair_item_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="bad",
            rules=new[]{new
            {
                item_definition_id=3701,price=1000,
                max_durability=100,can_repair=false
            }}
        }));
        try{RepairItemCatalog.LoadVerified(items);}
        catch(InvalidDataException){notRepairable=true;}

        File.WriteAllText(repairs,JsonSerializer.Serialize(new
        {
            kind="verified_npc_repair_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="bad",
            repairs=new[]{new
            {
                map_id=76,npc_spawn_id=78,repair_rate_percent=10
            }}
        }));
        try{NpcRepairCatalog.LoadVerified(repairs,[npc]);}
        catch(InvalidDataException){badNpc=true;}

        File.WriteAllText(repairs,JsonSerializer.Serialize(new
        {
            kind="verified_npc_repair_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="bad",
            repairs=new[]{new
            {
                map_id=76,npc_spawn_id=77,repair_rate_percent=256
            }}
        }));
        try{NpcRepairCatalog.LoadVerified(repairs,[npc]);}
        catch(InvalidDataException){badRate=true;}

        Checks.Require(unverifiedItem&&notRepairable&&badNpc&&badRate,
            "repair catalogs admitted unverified or impossible rules");
    }
}
