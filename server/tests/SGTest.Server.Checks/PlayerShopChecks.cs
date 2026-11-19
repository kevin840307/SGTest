using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

internal static class PlayerShopChecks
{
    public static void Verify(string root)
    {
        Checks.Run("player shop atomically transfers one whole ItemUid stack and money",
            ()=>AtomicPurchase(root).GetAwaiter().GetResult());
        Checks.Run("player shop stale listing cannot sell an ItemUid moved elsewhere",
            ()=>StaleListing(root).GetAwaiter().GetResult());
        Checks.Run("ten concurrent player-shop buyers yield exactly one sale",
            ()=>ConcurrentBuyers(root).GetAwaiter().GetResult());
    }

    private static async Task AtomicPurchase(string root)
    {
        string dir=Path.Combine(root,"player_shop_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        try
        {
            using var store=new SqliteIdentityStore(
                db,options:new IdentityStoreOptions(MaxCharactersPerAccount:16));
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "player_shop_a","Player-Shop-Password-123!")).AccountId;
            long seller=(await store.CreateCharacterAsync(
                account,0,"ShopSeller",new(15,8,8))).Character!.CharacterId;
            long buyer=(await store.CreateCharacterAsync(
                account,1,"ShopBuyer",new(15,10,8))).Character!.CharacterId;
            long other=(await store.CreateCharacterAsync(
                account,2,"ShopOther",new(15,12,8))).Character!.CharacterId;
            SeedWallet(db,seller,100);
            SeedWallet(db,buyer,1000);
            SeedWallet(db,other,1000);
            long generation=await store.AllocateLootWorldGenerationAsync();
            var first=await SeedItem(store,generation,seller,5101,2,1);
            var second=await SeedItem(store,generation,seller,5102,1,2);
            var rules=new Dictionary<int,TradeItemRule>
            {
                [5101]=new(5101,true,"synthetic:player-shop-5101"),
                [5102]=new(5102,false,"synthetic:player-shop-5102")
            };

            var created=await store.CreatePlayerShopListingAsync(
                seller,first.ItemUid,300,rules);
            Checks.Require(created.Status==PlayerShopCreateStatus.Created&&
                created.Listing is not null&&
                created.Listing.ItemUid==first.ItemUid&&
                created.Listing.ItemDefinitionId==5101&&
                created.Listing.Quantity==2&&
                created.Listing.SourceDropId==first.DropId&&
                created.Listing.Price==300&&
                created.Listing.State==PlayerShopListingState.Active,
                "player-shop listing snapshot mismatch");
            var listing=created.Listing
                ??throw new InvalidOperationException("player-shop listing missing");

            var duplicate=await store.CreatePlayerShopListingAsync(
                seller,second.ItemUid,1,rules);
            Checks.Require(duplicate.Status==PlayerShopCreateStatus.AlreadyOpen,
                "seller opened two simultaneous player-shop listings");

            var active=await store.GetActivePlayerShopListingAsync(seller);
            Checks.Require(active?.ListingId==listing.ListingId,
                "active player-shop listing lookup mismatch");

            var applied=await store.BuyPlayerShopListingAsync(
                buyer,listing.ListingId,rules);
            Checks.Require(applied.Status==PlayerShopBuyStatus.Applied&&
                applied.Listing is not null&&
                applied.Listing.State==PlayerShopListingState.Sold&&
                applied.Listing.BuyerCharacterId==buyer&&
                applied.Listing.SellerWalletAfter==400&&
                applied.Listing.BuyerWalletAfter==700,
                "player-shop sale receipt mismatch");
            Checks.Require(await store.GetCharacterMoneyAsync(seller)==400&&
                await store.GetCharacterMoneyAsync(buyer)==700,
                "player-shop sale money mismatch");
            await AssertOwnership(store,buyer,first.ItemUid,5101,2,first.DropId);
            Checks.Require((await store.ListInventoryItemsAsync(seller))
                .All(x=>x.ItemUid!=first.ItemUid),
                "sold player-shop UID remained with seller");

            var replay=await store.BuyPlayerShopListingAsync(
                buyer,listing.ListingId,rules);
            Checks.Require(replay.Status==PlayerShopBuyStatus.AlreadyApplied&&
                replay.Listing?.BuyerCharacterId==buyer&&
                await store.GetCharacterMoneyAsync(seller)==400&&
                await store.GetCharacterMoneyAsync(buyer)==700,
                "exact player-shop replay changed committed money");
            var lost=await store.BuyPlayerShopListingAsync(
                other,listing.ListingId,rules);
            Checks.Require(lost.Status==PlayerShopBuyStatus.Sold&&
                await store.GetCharacterMoneyAsync(other)==1000,
                "second buyer changed already-sold player-shop listing");

            var closedSold=await store.ClosePlayerShopListingAsync(
                seller,listing.ListingId);
            Checks.Require(closedSold.Status==PlayerShopCloseStatus.AlreadyClosed,
                "sold player-shop listing was closed as active");

            var blocked=await store.CreatePlayerShopListingAsync(
                seller,second.ItemUid,100,rules);
            Checks.Require(blocked.Status==PlayerShopCreateStatus.NotTradeable&&
                (await store.ListInventoryItemsAsync(seller))
                    .Any(x=>x.ItemUid==second.ItemUid),
                "unverified/non-tradeable player-shop item was admitted or lost");
        }
        finally{Directory.Delete(dir,true);}
    }

    private static async Task StaleListing(string root)
    {
        string dir=Path.Combine(root,"player_shop_stale_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        try
        {
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "player_shop_stale","Player-Shop-Stale-123!")).AccountId;
            long seller=(await store.CreateCharacterAsync(
                account,0,"StaleSeller",new(15,8,8))).Character!.CharacterId;
            long buyer=(await store.CreateCharacterAsync(
                account,1,"StaleBuyer",new(15,9,8))).Character!.CharacterId;
            long movedTo=(await store.CreateCharacterAsync(
                account,2,"StaleMoved",new(15,10,8))).Character!.CharacterId;
            SeedWallet(db,seller,50);
            SeedWallet(db,buyer,500);
            SeedWallet(db,movedTo,0);
            long generation=await store.AllocateLootWorldGenerationAsync();
            var item=await SeedItem(store,generation,seller,5201,1,1);
            var rules=new Dictionary<int,TradeItemRule>
            { [5201]=new(5201,true,"synthetic:player-shop-stale") };

            var created=await store.CreatePlayerShopListingAsync(
                seller,item.ItemUid,200,rules);
            Checks.Require(created.Status==PlayerShopCreateStatus.Created&&
                created.Listing is not null,"stale listing setup failed");
            var staleListing=created.Listing
                ??throw new InvalidOperationException("stale listing missing");

            var moved=await store.CommitInventoryTradeAsync(
                new(92001,seller,movedTo,0,0,[item.ItemUid],[]),rules);
            Checks.Require(moved.Status==TradeCommitStatus.Applied,
                "stale listing setup could not move UID");

            var rejected=await store.BuyPlayerShopListingAsync(
                buyer,staleListing.ListingId,rules);
            Checks.Require(rejected.Status==PlayerShopBuyStatus.StaleListing&&
                rejected.Listing?.State==PlayerShopListingState.Stale&&
                await store.GetCharacterMoneyAsync(seller)==50&&
                await store.GetCharacterMoneyAsync(buyer)==500,
                "stale player-shop listing mutated wallets");
            await AssertOwnership(store,movedTo,item.ItemUid,5201,1,item.DropId);
            Checks.Require(await store.GetActivePlayerShopListingAsync(seller) is null,
                "stale player-shop listing remained active");

            var freshItem=await SeedItem(store,generation,seller,5201,1,2);
            var fresh=await store.CreatePlayerShopListingAsync(
                seller,freshItem.ItemUid,100,rules);
            Checks.Require(fresh.Status==PlayerShopCreateStatus.Created&&
                fresh.Listing is not null,"fresh listing after stale failed");
            var freshListing=fresh.Listing
                ??throw new InvalidOperationException("fresh listing missing");
            var closed=await store.ClosePlayerShopListingAsync(
                seller,freshListing.ListingId);
            var afterClose=await store.BuyPlayerShopListingAsync(
                buyer,freshListing.ListingId,rules);
            Checks.Require(closed.Status==PlayerShopCloseStatus.Closed&&
                afterClose.Status==PlayerShopBuyStatus.Closed,
                "closed player-shop listing remained purchasable");
        }
        finally{Directory.Delete(dir,true);}
    }

    private static async Task ConcurrentBuyers(string root)
    {
        string dir=Path.Combine(root,"player_shop_race_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        try
        {
            using var store=new SqliteIdentityStore(
                db,options:new IdentityStoreOptions(
                    MaxCharactersPerAccount:16,BusyTimeoutSeconds:10));
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "player_shop_race","Player-Shop-Race-123!")).AccountId;
            long seller=(await store.CreateCharacterAsync(
                account,0,"RaceSeller",new(15,8,8))).Character!.CharacterId;
            var buyers=new List<long>();
            for(int i=0;i<10;i++)
                buyers.Add((await store.CreateCharacterAsync(
                    account,i+1,$"RaceBuyer{i}",new(15,10+i,8)))
                    .Character!.CharacterId);
            SeedWallet(db,seller,0);
            foreach(long buyer in buyers)SeedWallet(db,buyer,1000);
            long generation=await store.AllocateLootWorldGenerationAsync();
            var item=await SeedItem(store,generation,seller,5301,3,1);
            var rules=new Dictionary<int,TradeItemRule>
            { [5301]=new(5301,true,"synthetic:player-shop-race") };
            var created=await store.CreatePlayerShopListingAsync(
                seller,item.ItemUid,100,rules);
            Checks.Require(created.Listing is not null,
                "concurrent player-shop listing setup failed");
            var raceListing=created.Listing
                ??throw new InvalidOperationException("race listing missing");

            var results=await Task.WhenAll(buyers.Select(
                buyer=>store.BuyPlayerShopListingAsync(
                    buyer,raceListing.ListingId,rules)));
            Checks.Require(results.Count(x=>x.Status==PlayerShopBuyStatus.Applied)==1&&
                results.Count(x=>x.Status==PlayerShopBuyStatus.Sold)==9,
                "concurrent player-shop buyers did not yield exactly one winner");
            var winner=results.Single(x=>x.Status==PlayerShopBuyStatus.Applied)
                .Listing?.BuyerCharacterId??0;
            Checks.Require(winner>0&&await store.GetCharacterMoneyAsync(seller)==100,
                "concurrent player-shop winner/seller wallet mismatch");
            int owners=0;
            int debited=0;
            foreach(long buyer in buyers)
            {
                if((await store.ListInventoryItemsAsync(buyer))
                    .Any(x=>x.ItemUid==item.ItemUid))owners++;
                if(await store.GetCharacterMoneyAsync(buyer)==900)debited++;
            }
            Checks.Require(owners==1&&debited==1&&
                await store.GetCharacterMoneyAsync(winner)==900,
                "player-shop race duplicated UID or debited multiple buyers");
        }
        finally{Directory.Delete(dir,true);}
    }

    private static async Task<(long ItemUid,long DropId)> SeedItem(
        SqliteIdentityStore store,long generation,long characterId,
        int definition,int quantity,int slot)
    {
        var drop=await store.CreateTrustedGroundDropAsync(new(
            15,8+slot,8,950000+slot,
            SqliteIdentityStore.ComposeLootDeathRevision(generation,slot),
            0,definition,quantity,LootSourceTrust.SyntheticTest,
            "synthetic:player-shop-seed-"+slot));
        var grant=await store.ClaimGroundDropAsync(
            new TrustedPickupContext(characterId,500+slot,1,1,15,8+slot,8),
            drop.DropId);
        Checks.Require(grant.Status==PickupStatus.Granted&&grant.ItemUid>0,
            "player-shop seed pickup failed");
        return(grant.ItemUid,drop.DropId);
    }

    private static async Task AssertOwnership(
        SqliteIdentityStore store,long owner,long uid,int definition,int quantity,long dropId)
    {
        var item=(await store.ListInventoryItemsAsync(owner))
            .Single(x=>x.ItemUid==uid);
        Checks.Require(item.ItemDefinitionId==definition&&
            item.Quantity==quantity&&item.SourceDropId==dropId,
            "player-shop settlement lost UID/quantity/provenance");
    }

    private static void SeedWallet(string db,long characterId,long money)
    {
        using var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText=@"
INSERT INTO character_wallets(character_id,money) VALUES($id,$money)
ON CONFLICT(character_id) DO UPDATE SET money=excluded.money;";
        command.Parameters.AddWithValue("$id",characterId);
        command.Parameters.AddWithValue("$money",money);
        Checks.Require(command.ExecuteNonQuery()==1,
            "player-shop wallet seed failed");
    }
}
