using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

internal static class PlayerShopMainSeed
{
    private const string CharacterProof="2468";
    private const int DefinitionId=1001;

    internal static async Task<int> RunAsync(string database,string appearanceRulesPath)
    {
        try
        {
            string db=Path.GetFullPath(database);
            if(File.Exists(db))
                throw new InvalidOperationException("seed database must be new");
            Directory.CreateDirectory(Path.GetDirectoryName(db)!);
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync().ConfigureAwait(false);
            var appearanceRules=CharacterAppearanceRules.LoadVerified(
                Path.GetFullPath(appearanceRulesPath));
            var settings=new CharacterCreationSettings(
                new CharacterCreationAttributes(2,2,2,2),CharacterProof,
                "Old source-backed level-one creation + verified appearance rules");

            async Task<long> Create(string username,string name,double x)
            {
                long account=(await store.CreateAccountAsync(username,Program.Password)
                    .ConfigureAwait(false)).AccountId;
                var created=await store.CreateCharacterWithSettingsAsync(
                    account,0,name,new CharacterSpawn(15,x,800),settings,
                    new CharacterAppearance(0,0,0,0,0,0),appearanceRules,
                    initialProfileRules:CharacterInitialProfileRules.OldParadiseLevelOne)
                    .ConfigureAwait(false);
                return created.Character?.CharacterId
                    ??throw new InvalidOperationException("character create failed");
            }

            long seller=await Create("player_shop_seller","ShopSeller",840)
                .ConfigureAwait(false);
            long buyer=await Create("player_shop_buyer","ShopBuyer",880)
                .ConfigureAwait(false);
            SeedWallet(db,seller,100);
            SeedWallet(db,buyer,1000);

            long generation=await store.AllocateLootWorldGenerationAsync().ConfigureAwait(false);
            var drop=await store.CreateTrustedGroundDropAsync(new TrustedDropSpawn(
                15,840,800,1900001001,
                SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                0,DefinitionId,3,LootSourceTrust.Verified,
                "Recovered2 Items/recovery/1001.tres can_trade=1"))
                .ConfigureAwait(false);
            var grant=await store.ClaimGroundDropAsync(
                new TrustedPickupContext(
                    seller,SqliteIdentityStore.ComposePickupConnectionEpoch(generation,1),
                    1,1,15,840,800),drop.DropId).ConfigureAwait(false);
            if(grant.Status!=PickupStatus.Granted||grant.ItemUid<=0||grant.Quantity!=3)
                throw new InvalidOperationException("verified player-shop seed grant failed");

            Console.WriteLine(
                $"PLAYER_SHOP_MAIN_SEED_PASS seller={seller} buyer={buyer} "+
                $"item_uid={grant.ItemUid} drop_id={drop.DropId} "+
                $"definition={DefinitionId} quantity=3 seller_wallet=100 buyer_wallet=1000");
            return 0;
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("PLAYER_SHOP_MAIN_SEED_FAIL "+error);
            return 1;
        }
    }

    private static void SeedWallet(string database,long characterId,long money)
    {
        using var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource=database,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText=
            "UPDATE character_wallets SET money=$money WHERE character_id=$id;";
        command.Parameters.AddWithValue("$money",money);
        command.Parameters.AddWithValue("$id",characterId);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("player-shop wallet seed failed");
    }
}
