using SGTest.Server.Npc;
using System.Text.Json;
using SGTest.Server.World;

internal static class EnhancementCatalogChecks
{
    public static void Verify(string root)
    {
        Checks.Run("verified enhancement NPC rules fail closed",
            () => VerifyCatalog(root));
    }

    private static void VerifyCatalog(string root)
    {
        string npc=Path.Combine(root,"enhancement-npc.json");
        File.WriteAllText(npc,JsonSerializer.Serialize(new
        {
            schema="sgtest.npcs/v1",map_id=76,coordinate_space="world_pixels",
            npcs=new[]{new{spawn_id=99,p=new[]{8,8},
                interaction=new{mode="script_required"}}}
        }));
        string manifest=Path.Combine(root,"verified-enhancement-npcs.json");
        File.WriteAllText(manifest,JsonSerializer.Serialize(new
        {
            kind="verified_npc_enhancement_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="old:verified-enhancer",
            enhancers=new[]{new{map_id=76,npc_spawn_id=99}}
        }));
        var catalog=NpcEnhancementCatalog.LoadVerified(manifest,[npc]);
        Checks.Require(catalog.Count==1&&
            catalog.TryGet(76,99,out var point)&&
            point.NpcX==8&&point.NpcY==8&&
            point.SourceReference.Contains("npc=99",StringComparison.Ordinal),
            "verified enhancement NPC rule mismatch");

        var near=new ActorSnapshot(1,76,1,1,0,0,8,8,1,false);
        var far=near with{X=1000,Y=1000};
        Checks.Require(
            catalog.Access(near,99)==EnhancementNpcAccessStatus.Allowed&&
            catalog.Access(far,99)==EnhancementNpcAccessStatus.TooFar&&
            catalog.Access(near,100)==EnhancementNpcAccessStatus.UnknownEnhancer,
            "enhancement NPC access gate mismatch");

        bool unverified=false,missingNpc=false;
        File.WriteAllText(manifest,JsonSerializer.Serialize(new
        {
            kind="verified_npc_enhancement_rules",version=1,
            rules_verified=false,gameplay_ready=true,
            source_reference="bad",
            enhancers=new[]{new{map_id=76,npc_spawn_id=99}}
        }));
        try{NpcEnhancementCatalog.LoadVerified(manifest,[npc]);}
        catch(InvalidDataException){unverified=true;}

        File.WriteAllText(manifest,JsonSerializer.Serialize(new
        {
            kind="verified_npc_enhancement_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="bad",
            enhancers=new[]{new{map_id=76,npc_spawn_id=100}}
        }));
        try{NpcEnhancementCatalog.LoadVerified(manifest,[npc]);}
        catch(InvalidDataException){missingNpc=true;}

        Checks.Require(unverified&&missingNpc,
            "enhancement catalog admitted unverified or missing NPC");
    }
}
