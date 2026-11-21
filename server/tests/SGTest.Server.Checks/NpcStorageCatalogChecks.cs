using SGTest.Server.Npc;
using System.Text.Json;
using SGTest.Server.World;

internal static class NpcStorageCatalogChecks
{
    public static void Verify(string root)
    {
        Checks.Run("verified NPC storage catalog joins audited source position and fails closed",
            () => Catalog(root));
    }

    private static void Catalog(string root)
    {
        string npc=Path.Combine(root,"storage_npcs_76.json");
        File.WriteAllText(npc,JsonSerializer.Serialize(new
        {
            schema="sgtest.npcs/v1",map_id=76,coordinate_space="world_pixels",
            npcs=new[]{new{spawn_id=77,p=new[]{8,8},
                interaction=new{mode="script_required"}}}
        }));
        string valid=WriteManifest(root,77,true);
        var catalog=NpcStorageCatalog.LoadVerified(valid,[npc]);
        Checks.Require(catalog.Count==1&&catalog.TryGet(76,77,out var point)&&
            point.NpcX==8&&point.NpcY==8,
            "verified storage did not join source NPC position");
        var near=new ActorSnapshot(1,76,1,1,0,0,8,8,1,false);
        var far=near with{X=500,Y=500};
        Checks.Require(catalog.Access(near,77)==StorageAccessStatus.Allowed&&
            catalog.Access(far,77)==StorageAccessStatus.TooFar&&
            catalog.Access(near,78)==StorageAccessStatus.UnknownStorage,
            "storage proximity or NPC identity policy mismatch");

        bool unverified=false,unknown=false;
        try{NpcStorageCatalog.LoadVerified(WriteManifest(root,77,false),[npc]);}
        catch(InvalidDataException){unverified=true;}
        try{NpcStorageCatalog.LoadVerified(WriteManifest(root,78,true),[npc]);}
        catch(InvalidDataException){unknown=true;}
        Checks.Require(unverified&&unknown,
            "unverified or missing-source storage NPC was admitted");
    }

    private static string WriteManifest(string root,int spawn,bool verified)
    {
        string path=Path.Combine(root,$"storage_rules_{spawn}_{verified}.json");
        File.WriteAllText(path,JsonSerializer.Serialize(new
        {
            kind="verified_npc_storage_rules",version=1,
            rules_verified=verified,gameplay_ready=true,
            source_reference="synthetic:storage-catalog-check",
            storages=new[]{new{map_id=76,npc_spawn_id=spawn}}
        }));
        return path;
    }
}
