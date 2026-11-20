using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

internal static class TradeChecks
{
    public static void Verify(string root)
    {
        Checks.Run("trade commit atomically swaps whole ItemUid stacks and money",
            () => AtomicTrade(root).GetAwaiter().GetResult());
        Checks.Run("concurrent trades cannot double-spend one ItemUid",
            () => ConcurrentUidRace(root).GetAwaiter().GetResult());
    }

    private static async Task AtomicTrade(string root)
    {
        string dir=Path.Combine(root,"trade_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        long first,second,itemA,itemB,dropA,dropB;
        var rules=new Dictionary<int,TradeItemRule>
        {
            [4103]=new(4103,true,"synthetic:trade-4103"),
            [4104]=new(4104,true,"synthetic:trade-4104")
        };
        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            long a=(await store.CreateAccountAsync("trade_a","Trade-A-Password-123!")).AccountId;
            long b=(await store.CreateAccountAsync("trade_b","Trade-B-Password-123!")).AccountId;
            first=(await store.CreateCharacterAsync(a,0,"TradeA",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId??throw new Exception("trade A missing");
            second=(await store.CreateCharacterAsync(b,0,"TradeB",new CharacterSpawn(15,9,8)))
                .Character?.CharacterId??throw new Exception("trade B missing");
            SeedWallet(db,first,1000);
            SeedWallet(db,second,500);
            long generation=await store.AllocateLootWorldGenerationAsync();
            (itemA,dropA)=await SeedItem(store,generation,first,4103,2,1);
            (itemB,dropB)=await SeedItem(store,generation,second,4104,1,2);
            SeedEnhancementLevel(db,itemA,5);
            SeedEnhancementLevel(db,itemB,7);
            SeedDurability(db,itemA,80);
            SeedDurability(db,itemB,60);

            var request=new TradeCommitRequest(
                9001,first,second,200,50,[itemA],[itemB]);
            var applied=await store.CommitInventoryTradeAsync(request,rules);
            Checks.Require(applied.Status==TradeCommitStatus.Applied&&
                applied.FirstWalletMoney==850&&applied.SecondWalletMoney==650&&
                applied.FirstSentItems.Single().ItemUid==itemA&&
                applied.FirstSentItems.Single().SourceDropId==dropA&&
                applied.FirstSentItems.Single().EnhancementLevel==5&&
                applied.FirstSentItems.Single().DurabilityRaw==80&&
                applied.SecondSentItems.Single().ItemUid==itemB&&
                applied.SecondSentItems.Single().SourceDropId==dropB&&
                applied.SecondSentItems.Single().EnhancementLevel==7&&
                applied.SecondSentItems.Single().DurabilityRaw==60,
                "trade atomic settlement receipt mismatch");
            await AssertOwnership(store,first,itemB,4104,1,dropB,7,60);
            await AssertOwnership(store,second,itemA,4103,2,dropA,5,80);

            var replay=await store.CommitInventoryTradeAsync(request,rules);
            Checks.Require(replay.Status==TradeCommitStatus.AlreadyApplied&&
                replay.FirstWalletMoney==850&&replay.SecondWalletMoney==650&&
                replay.FirstSentItems.Single().ItemUid==itemA&&
                replay.FirstSentItems.Single().EnhancementLevel==5&&
                replay.FirstSentItems.Single().DurabilityRaw==80&&
                replay.SecondSentItems.Single().ItemUid==itemB&&
                replay.SecondSentItems.Single().EnhancementLevel==7&&
                replay.SecondSentItems.Single().DurabilityRaw==60,
                "trade exact replay was not idempotent");

            var conflict=await store.CommitInventoryTradeAsync(
                request with{FirstMoneyOffer=201},rules);
            Checks.Require(conflict.Status==TradeCommitStatus.StaleTrade&&
                await store.GetCharacterMoneyAsync(first)==850&&
                await store.GetCharacterMoneyAsync(second)==650,
                "conflicting trade replay mutated money");

            var wrongOwner=await store.CommitInventoryTradeAsync(
                new(9002,first,second,0,0,[itemA],[]),rules);
            Checks.Require(wrongOwner.Status==TradeCommitStatus.NotOwned,
                "trade admitted item no longer owned by sender");

            var blockedRules=new Dictionary<int,TradeItemRule>
            {
                [4104]=new(4104,false,"synthetic:not-tradeable")
            };
            var blocked=await store.CommitInventoryTradeAsync(
                new(9003,first,second,0,0,[itemB],[]),blockedRules);
            Checks.Require(blocked.Status==TradeCommitStatus.NotTradeable&&
                (await store.ListInventoryItemsAsync(first)).Any(x=>x.ItemUid==itemB),
                "trade admitted source-gated non-tradeable item");

            var poor=await store.CommitInventoryTradeAsync(
                new(9004,first,second,851,0,[],[]),rules);
            Checks.Require(poor.Status==TradeCommitStatus.InsufficientMoney&&
                await store.GetCharacterMoneyAsync(first)==850,
                "insufficient trade money mutated wallet");
        }

        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var replay=await reopened.CommitInventoryTradeAsync(
                new(9001,first,second,200,50,[itemA],[itemB]),rules);
            Checks.Require(replay.Status==TradeCommitStatus.AlreadyApplied&&
                replay.FirstWalletMoney==850&&replay.SecondWalletMoney==650,
                "trade receipt did not survive database reopen");
            Checks.Require(
                replay.FirstSentItems.Single().EnhancementLevel==5&&
                replay.FirstSentItems.Single().DurabilityRaw==80&&
                replay.SecondSentItems.Single().EnhancementLevel==7&&
                replay.SecondSentItems.Single().DurabilityRaw==60,
                "trade durable replay lost enhancement/durability");
            await AssertOwnership(reopened,first,itemB,4104,1,dropB,7,60);
            await AssertOwnership(reopened,second,itemA,4103,2,dropA,5,80);
        }
    }

    private static async Task ConcurrentUidRace(string root)
    {
        string dir=Path.Combine(root,"trade_race_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        long a=(await store.CreateAccountAsync("trade_race_a","Trade-Race-A-123!")).AccountId;
        long b=(await store.CreateAccountAsync("trade_race_b","Trade-Race-B-123!")).AccountId;
        long c=(await store.CreateAccountAsync("trade_race_c","Trade-Race-C-123!")).AccountId;
        long ca=(await store.CreateCharacterAsync(a,0,"TradeRaceA",new CharacterSpawn(15,8,8)))
            .Character!.CharacterId;
        long cb=(await store.CreateCharacterAsync(b,0,"TradeRaceB",new CharacterSpawn(15,9,8)))
            .Character!.CharacterId;
        long cc=(await store.CreateCharacterAsync(c,0,"TradeRaceC",new CharacterSpawn(15,10,8)))
            .Character!.CharacterId;
        long generation=await store.AllocateLootWorldGenerationAsync();
        var seeded=await SeedItem(store,generation,ca,4103,1,1);
        var rules=new Dictionary<int,TradeItemRule>
        { [4103]=new(4103,true,"synthetic:trade-race") };
        var t1=store.CommitInventoryTradeAsync(new(9101,ca,cb,0,0,[seeded.ItemUid],[]),rules);
        var t2=store.CommitInventoryTradeAsync(new(9102,ca,cc,0,0,[seeded.ItemUid],[]),rules);
        var results=await Task.WhenAll(t1,t2);
        Checks.Require(results.Count(x=>x.Status==TradeCommitStatus.Applied)==1&&
            results.Count(x=>x.Status==TradeCommitStatus.NotOwned)==1,
            "concurrent trade race did not have exactly one owner");
        int owners=0;
        foreach(long id in new[]{ca,cb,cc})
            if((await store.ListInventoryItemsAsync(id)).Any(x=>x.ItemUid==seeded.ItemUid))
                owners++;
        Checks.Require(owners==1,"trade race duplicated or lost ItemUid");
    }

    private static async Task<(long ItemUid,long DropId)> SeedItem(
        SqliteIdentityStore store,long generation,long characterId,
        int definition,int quantity,int slot)
    {
        var drop=await store.CreateTrustedGroundDropAsync(new(
            15,8+slot,8,900000+slot,
            SqliteIdentityStore.ComposeLootDeathRevision(generation,slot),
            0,definition,quantity,LootSourceTrust.SyntheticTest,
            "synthetic:trade-seed-"+slot));
        var grant=await store.ClaimGroundDropAsync(
            new TrustedPickupContext(characterId,100+slot,1,1,15,8+slot,8),
            drop.DropId);
        Checks.Require(grant.Status==PickupStatus.Granted&&grant.ItemUid>0,
            "trade seed pickup failed");
        return(grant.ItemUid,drop.DropId);
    }

    private static async Task AssertOwnership(
        SqliteIdentityStore store,long owner,long uid,int definition,int quantity,long dropId,
        int enhancementLevel=0,int? durabilityRaw=null)
    {
        var item=(await store.ListInventoryItemsAsync(owner)).Single(x=>x.ItemUid==uid);
        Checks.Require(item.ItemDefinitionId==definition&&item.Quantity==quantity&&
            item.SourceDropId==dropId&&item.EnhancementLevel==enhancementLevel&&
            item.DurabilityRaw==durabilityRaw,
            "trade did not preserve UID/provenance/enhancement/durability");
    }

    private static void SeedEnhancementLevel(string db,long itemUid,int level)
    {
        using var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText=
            "UPDATE item_instances SET enhancement_level=$level WHERE item_uid=$uid;";
        command.Parameters.AddWithValue("$level",level);
        command.Parameters.AddWithValue("$uid",itemUid);
        Checks.Require(command.ExecuteNonQuery()==1,
            "trade enhancement-level seed failed");
    }

    private static void SeedDurability(string db,long itemUid,int durabilityRaw)
    {
        using var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText=
            "UPDATE item_instances SET durability_raw=$value WHERE item_uid=$uid;";
        command.Parameters.AddWithValue("$value",durabilityRaw);
        command.Parameters.AddWithValue("$uid",itemUid);
        Checks.Require(command.ExecuteNonQuery()==1,
            "trade durability seed failed");
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
        Checks.Require(command.ExecuteNonQuery()==1,"trade wallet seed failed");
    }
}
