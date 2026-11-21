using SGTest.Server.Npc;
using System.Text.Json;
using SGTest.Server.World;

internal static class NpcBankCatalogChecks
{
    public static void Verify(string root)
    {
        Checks.Run("verified NPC bank catalog requires explicit scope and audited source position",
            () => Catalog(root));
    }

    private static void Catalog(string root)
    {
        string npc=Path.Combine(root,"bank_npcs_76.json");
        File.WriteAllText(npc,JsonSerializer.Serialize(new
        {
            schema="sgtest.npcs/v1",map_id=76,coordinate_space="world_pixels",
            npcs=new[]{new{spawn_id=77,p=new[]{8,8},
                interaction=new{mode="script_required"}}}
        }));

        string character=WriteManifest(root,77,true,"character");
        var characterCatalog=NpcBankCatalog.LoadVerified(character,[npc]);
        Checks.Require(characterCatalog.Count==1&&
            characterCatalog.TryGet(76,77,out var characterPoint)&&
            characterPoint.NpcX==8&&characterPoint.NpcY==8&&
            characterPoint.Scope==BankScopePolicy.Character,
            "verified character bank did not join audited NPC");

        string account=WriteManifest(root,77,true,"account");
        var accountCatalog=NpcBankCatalog.LoadVerified(account,[npc]);
        Checks.Require(accountCatalog.TryGet(76,77,out var accountPoint)&&
            accountPoint.Scope==BankScopePolicy.Account,
            "verified account bank scope mismatch");

        var near=new ActorSnapshot(1,76,1,1,0,0,8,8,1,false);
        var far=near with{X=500,Y=500};
        Checks.Require(
            characterCatalog.Access(near,77)==BankNpcAccessStatus.Allowed&&
            characterCatalog.Access(far,77)==BankNpcAccessStatus.TooFar&&
            characterCatalog.Access(near,78)==BankNpcAccessStatus.UnknownBank,
            "bank proximity or NPC identity policy mismatch");

        bool unverified=false,unknown=false,missingScope=false,badScope=false;
        try{NpcBankCatalog.LoadVerified(
            WriteManifest(root,77,false,"character"),[npc]);}
        catch(InvalidDataException){unverified=true;}
        try{NpcBankCatalog.LoadVerified(
            WriteManifest(root,78,true,"character"),[npc]);}
        catch(InvalidDataException){unknown=true;}
        try{NpcBankCatalog.LoadVerified(
            WriteManifest(root,77,true,null),[npc]);}
        catch(InvalidDataException){missingScope=true;}
        try{NpcBankCatalog.LoadVerified(
            WriteManifest(root,77,true,"global"),[npc]);}
        catch(InvalidDataException){badScope=true;}
        Checks.Require(unverified&&unknown&&missingScope&&badScope,
            "bank admitted unverified/missing-source/implicit scope rule");
    }

    private static string WriteManifest(
        string root,int spawn,bool verified,string? scope)
    {
        string safe=scope??"missing";
        string path=Path.Combine(root,$"bank_rules_{spawn}_{verified}_{safe}.json");
        object row=scope is null
            ? new{map_id=76,npc_spawn_id=spawn}
            : new{map_id=76,npc_spawn_id=spawn,balance_scope=scope};
        File.WriteAllText(path,JsonSerializer.Serialize(new
        {
            kind="verified_npc_bank_rules",version=1,
            rules_verified=verified,gameplay_ready=true,
            source_reference="synthetic:bank-catalog-check",
            banks=new[]{row}
        }));
        return path;
    }
}
