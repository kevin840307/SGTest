using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

internal static class EnhancementChecks
{
    public static void Verify(string root)
    {
        Checks.Run("verified enhancement catalog fails closed",
            () => VerifiedCatalog(root));
        Checks.Run("enhancement attempt is atomic replay-safe and location-stable",
            () => Lifecycle(root).GetAwaiter().GetResult());
    }

    private static void VerifiedCatalog(string root)
    {
        string path=Path.Combine(root,"verified-enhancement.json");
        WriteManifest(path,true,"no_change");
        var rules=EquipmentEnhancementCatalog.LoadVerified(path);
        Checks.Require(
            rules.Count==3&&
            rules[5701].Steps[0].MoneyCost==100&&
            rules[5701].Steps[0].SuccessPpm==1_000_000&&
            rules[5702].Steps[0].SuccessPpm==0&&
            rules[5702].Steps[0].ConsumeMoneyOnFailure,
            "verified enhancement catalog mismatch");

        WriteManifest(path,false,"no_change");
        bool rejected=false;
        try{EquipmentEnhancementCatalog.LoadVerified(path);}
        catch(InvalidDataException){rejected=true;}
        Checks.Require(rejected,"unverified enhancement manifest admitted");

        WriteManifest(path,true,"downgrade");
        rejected=false;
        try{EquipmentEnhancementCatalog.LoadVerified(path);}
        catch(InvalidDataException){rejected=true;}
        Checks.Require(rejected,
            "unsupported enhancement failure mode was silently admitted");
    }

    private static async Task Lifecycle(string root)
    {
        string dir=Path.Combine(root,"enhance_action_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        string manifest=Path.Combine(dir,"rules.json");
        WriteManifest(manifest,true,"no_change");
        var rules=EquipmentEnhancementCatalog.LoadVerified(manifest);
        long actor;
        long successUid;
        long failureUid;
        long equippedUid;

        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "enhance_action","Enhance-Action-123!")).AccountId;
            actor=(await store.CreateCharacterAsync(
                account,0,"EnhanceAction",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId
                ??throw new Exception("enhancement action character missing");
            SetWallet(db,actor,1000);

            long generation=await store.AllocateLootWorldGenerationAsync();
            successUid=await SeedOne(store,actor,generation,1,5701);
            failureUid=await SeedOne(store,actor,generation,2,5702);
            equippedUid=await SeedOne(store,actor,generation,3,5703);

            var npc=new TrustedEnhancementNpcRule(99,"ci:enhancer-npc");
            var firstContext=new TrustedEnhancementContext(actor,801,1,1);
            var success=await store.EnhanceItemAsync(
                firstContext,EnhancementAccessStatus.Allowed,npc,rules,successUid);
            var successReplay=await store.EnhanceItemAsync(
                firstContext,EnhancementAccessStatus.Allowed,npc,rules,successUid);
            Checks.Require(
                success.Status==EnhancementStatus.Applied&&success.Succeeded&&
                success.PreviousLevel==0&&success.TargetLevel==1&&
                success.CurrentLevel==1&&success.MoneyCost==100&&
                success.MoneyCharged==100&&success.WalletMoney==900&&
                successReplay.Status==EnhancementStatus.AlreadyApplied&&
                successReplay.Succeeded&&successReplay.CurrentLevel==1&&
                successReplay.WalletMoney==900&&
                ReadLevel(db,"item_instances",successUid)==1&&
                ReadWallet(db,actor)==900,
                "successful enhancement/replay mismatch");

            var conflicting=await store.EnhanceItemAsync(
                firstContext,EnhancementAccessStatus.Allowed,npc,rules,failureUid);
            Checks.Require(
                conflicting.Status==EnhancementStatus.StaleSequence&&
                ReadWallet(db,actor)==900&&
                ReadLevel(db,"item_instances",failureUid)==0,
                "same enhancement sequence accepted another item");

            var failContext=new TrustedEnhancementContext(actor,801,1,2);
            var failed=await store.EnhanceItemAsync(
                failContext,EnhancementAccessStatus.Allowed,npc,rules,failureUid);
            var failedReplay=await store.EnhanceItemAsync(
                failContext,EnhancementAccessStatus.Allowed,npc,rules,failureUid);
            Checks.Require(
                failed.Status==EnhancementStatus.Applied&&!failed.Succeeded&&
                failed.PreviousLevel==0&&failed.TargetLevel==1&&
                failed.CurrentLevel==0&&failed.MoneyCost==50&&
                failed.MoneyCharged==50&&failed.WalletMoney==850&&
                failed.RollPpm==0&&
                failedReplay.Status==EnhancementStatus.AlreadyApplied&&
                !failedReplay.Succeeded&&failedReplay.CurrentLevel==0&&
                failedReplay.WalletMoney==850&&
                ReadLevel(db,"item_instances",failureUid)==0&&
                ReadWallet(db,actor)==850,
                "failed enhancement was not durable/replay-safe");

            var maxed=await store.EnhanceItemAsync(
                new TrustedEnhancementContext(actor,801,1,3),
                EnhancementAccessStatus.Allowed,npc,rules,successUid);
            Checks.Require(
                maxed.Status==EnhancementStatus.MaxLevel&&
                ReadLevel(db,"item_instances",successUid)==1&&
                ReadWallet(db,actor)==850,
                "max-level enhancement mutated item/wallet");

            var equip=await store.EquipInventoryItemAsync(
                new TrustedEquipmentContext(actor,802,1,1),equippedUid,
                new Dictionary<int,EquipmentRule>{[5703]=new(5703,4)});
            Checks.Require(equip.Status==EquipmentTransferStatus.Applied,
                "enhancement equipped seed failed");
            var equippedResult=await store.EnhanceItemAsync(
                new TrustedEnhancementContext(actor,801,1,4),
                EnhancementAccessStatus.Allowed,npc,rules,equippedUid);
            Checks.Require(
                equippedResult.Status==EnhancementStatus.Applied&&
                equippedResult.Succeeded&&
                equippedResult.Location==EnhancementItemLocation.Equipped&&
                equippedResult.CurrentLevel==1&&
                ReadLevel(db,"equipped_items",equippedUid)==1&&
                ReadWallet(db,actor)==825,
                "equipped enhancement did not mutate exact UID");

            var denied=await store.EnhanceItemAsync(
                new TrustedEnhancementContext(actor,801,1,5),
                EnhancementAccessStatus.TooFar,npc,rules,failureUid);
            Checks.Require(
                denied.Status==EnhancementStatus.TooFar&&
                ReadWallet(db,actor)==825&&
                ReadLevel(db,"item_instances",failureUid)==0,
                "enhancement access denial mutated durable state");
        }

        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var replay=await reopened.EnhanceItemAsync(
                new TrustedEnhancementContext(actor,801,1,2),
                EnhancementAccessStatus.Allowed,
                new TrustedEnhancementNpcRule(99,"ci:enhancer-npc"),
                rules,failureUid);
            Checks.Require(
                replay.Status==EnhancementStatus.AlreadyApplied&&
                !replay.Succeeded&&replay.CurrentLevel==0&&
                replay.WalletMoney==850&&
                ReadLevel(db,"item_instances",successUid)==1&&
                ReadLevel(db,"equipped_items",equippedUid)==1&&
                ReadWallet(db,actor)==825,
                "enhancement receipt/item state did not survive reopen");
        }
    }

    private static void WriteManifest(string path,bool verified,string failureMode)
    {
        File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new
        {
            kind="verified_equipment_enhancement_rules",
            version=1,
            rules_verified=verified,
            gameplay_ready=true,
            source_reference="ci:explicit-enhancement-rules",
            rules=new object[]
            {
                new
                {
                    item_definition_id=5701,
                    steps=new[]{new
                    {
                        from_level=0,to_level=1,money_cost=100L,
                        success_ppm=1_000_000,
                        consume_money_on_failure=true,
                        failure_mode=failureMode
                    }}
                },
                new
                {
                    item_definition_id=5702,
                    steps=new[]{new
                    {
                        from_level=0,to_level=1,money_cost=50L,
                        success_ppm=0,
                        consume_money_on_failure=true,
                        failure_mode=failureMode
                    }}
                },
                new
                {
                    item_definition_id=5703,
                    steps=new[]{new
                    {
                        from_level=0,to_level=1,money_cost=25L,
                        success_ppm=1_000_000,
                        consume_money_on_failure=false,
                        failure_mode=failureMode
                    }}
                }
            }
        }));
    }

    private static async Task<long> SeedOne(
        SqliteIdentityStore store,long actor,long generation,int slot,int definition)
    {
        var drop=await store.CreateTrustedGroundDropAsync(new(
            15,8,8,992000+slot,
            SqliteIdentityStore.ComposeLootDeathRevision(generation,slot),
            0,definition,1,LootSourceTrust.SyntheticTest,
            "ci:enhancement-action-seed-"+slot));
        var grant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
            actor,SqliteIdentityStore.ComposePickupConnectionEpoch(generation,slot),
            1,(uint)slot,15,8,8),drop.DropId);
        if(grant.Status!=PickupStatus.Granted||grant.ItemUid<=0)
            throw new InvalidOperationException("enhancement action seed failed");
        return grant.ItemUid;
    }

    private static void SetWallet(string db,long actor,long money)
    {
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=
            "UPDATE character_wallets SET money=$money WHERE character_id=$actor;";
        command.Parameters.AddWithValue("$money",money);
        command.Parameters.AddWithValue("$actor",actor);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("enhancement wallet seed missing");
    }

    private static long ReadWallet(string db,long actor)
    {
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=
            "SELECT money FROM character_wallets WHERE character_id=$actor;";
        command.Parameters.AddWithValue("$actor",actor);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static int ReadLevel(string db,string table,long uid)
    {
        if(table is not ("item_instances" or "equipped_items"))
            throw new ArgumentOutOfRangeException(nameof(table));
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=
            $"SELECT enhancement_level FROM {table} WHERE item_uid=$uid;";
        command.Parameters.AddWithValue("$uid",uid);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static SqliteConnection Open(string db)
    {
        var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString());
        connection.Open();
        return connection;
    }
}
