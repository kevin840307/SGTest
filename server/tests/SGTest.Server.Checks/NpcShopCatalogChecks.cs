using SGTest.Server.Npc;
using System.Text.Json;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class NpcShopCatalogChecks
{
    public static void Verify(string root)
    {
        Checks.Run("verified NPC shop catalog joins source NPC position and fails closed",
            () => Catalog(root));
    }

    private static void Catalog(string root)
    {
        string npc = Path.Combine(root, "shop_npcs_76.json");
        File.WriteAllText(npc, JsonSerializer.Serialize(new
        {
            schema = "sgtest.npcs/v1",
            map_id = 76,
            coordinate_space = "world_pixels",
            npcs = new[]
            {
                new { spawn_id = 99, p = new[] { 8, 8 },
                    interaction = new { mode = "script_required" } }
            }
        }));
        string valid = WriteManifest(root, 99, rulesVerified:true,
            buy:10, sell:5);
        var catalog = NpcShopCatalog.LoadVerified(valid, [npc]);
        Checks.Require(catalog.Count == 1 &&
            catalog.TryGet(76, 99, out _),
            "verified shop did not join source NPC position");
        if (!catalog.TryGet(76, 99, out var shop))
            throw new Exception("verified shop missing after catalog validation");
        Checks.Require(shop.NpcX == 8 && shop.NpcY == 8 &&
            shop.Items.Count == 1 &&
            shop.Items[1001] == new NpcShopCatalog.Item(1001,10,5),
            "verified shop source position or item mismatch");

        var near = new ActorSnapshot(1,76,1,1,0,0,8,8,1,false);
        var far = near with { X = 500, Y = 500 };
        Checks.Require(catalog.Access(near,99)==ShopAccessStatus.Allowed &&
            catalog.Access(far,99)==ShopAccessStatus.TooFar &&
            catalog.Access(near,100)==ShopAccessStatus.UnknownShop,
            "shop proximity or NPC identity policy mismatch");

        var price = catalog.Price(shop,1001);
        var missing = catalog.Price(shop,2002);
        Checks.Require(price.BuyPrice==10 && price.SellPrice==5 &&
            missing.BuyPrice==0 && missing.SellPrice==0,
            "shop price authority mismatch");

        bool unverified=false, unknownNpc=false, badPrice=false;
        try
        {
            NpcShopCatalog.LoadVerified(
                WriteManifest(root,99,rulesVerified:false,buy:10,sell:5),[npc]);
        }
        catch(InvalidDataException){unverified=true;}
        try
        {
            NpcShopCatalog.LoadVerified(
                WriteManifest(root,100,rulesVerified:true,buy:10,sell:5),[npc]);
        }
        catch(InvalidDataException){unknownNpc=true;}
        try
        {
            NpcShopCatalog.LoadVerified(
                WriteManifest(root,99,rulesVerified:true,buy:5,sell:10),[npc]);
        }
        catch(InvalidDataException){badPrice=true;}
        Checks.Require(unverified&&unknownNpc&&badPrice,
            "unverified, unknown-NPC or inverted shop prices were admitted");
    }

    private static string WriteManifest(
        string root,int npcSpawnId,bool rulesVerified,long buy,long sell)
    {
        string path = Path.Combine(root,
            $"shop_rules_{npcSpawnId}_{rulesVerified}_{buy}_{sell}.json");
        File.WriteAllText(path,JsonSerializer.Serialize(new
        {
            kind="verified_npc_shop_rules",
            version=1,
            rules_verified=rulesVerified,
            gameplay_ready=true,
            source_reference="synthetic:shop-catalog-check",
            shops=new[]
            {
                new
                {
                    map_id=76,
                    npc_spawn_id=npcSpawnId,
                    items=new[]
                    {
                        new
                        {
                            item_definition_id=1001,
                            buy_price=buy,
                            sell_price=sell
                        }
                    }
                }
            }
        }));
        return path;
    }
}
