using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

internal static class EnhancementMetadataChecks
{
    public static void Verify(string root)
    {
        Checks.Run("item enhancement metadata survives storage and equipment locations",
            () => Lifecycle(root).GetAwaiter().GetResult());
        Checks.Run("schema v19 item locations migrate to enhancement level zero",
            () => MigrateV19(root).GetAwaiter().GetResult());
        Checks.Run("schema v20 trade and player-shop receipts migrate enhancement metadata",
            () => MigrateV20Receipts(root).GetAwaiter().GetResult());
    }

    private static async Task Lifecycle(string root)
    {
        string dir=Path.Combine(root,"enhancement_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        long actor;
        long uid;

        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            Checks.Require(SqliteIdentityStore.SchemaVersion==27,
                "enhancement metadata requires schema v24");
            long account=(await store.CreateAccountAsync(
                "enhance_user","Enhance-Password-123!")).AccountId;
            actor=(await store.CreateCharacterAsync(
                account,0,"EnhanceHero",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId
                ??throw new Exception("enhancement character missing");

            long generation=await store.AllocateLootWorldGenerationAsync();
            var drop=await store.CreateTrustedGroundDropAsync(new(
                15,8,8,990001,
                SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                0,4701,1,LootSourceTrust.SyntheticTest,
                "ci:enhancement-metadata-seed"));
            var grant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
                actor,SqliteIdentityStore.ComposePickupConnectionEpoch(generation,1),
                1,1,15,8,8),drop.DropId);
            Checks.Require(grant.Status==PickupStatus.Granted&&grant.ItemUid>0,
                "enhancement seed item missing");
            uid=grant.ItemUid;

            SetLevel(db,"item_instances",uid,7);
            Checks.Require(ReadLevel(db,"item_instances",uid)==7,
                "inventory enhancement seed mismatch");

            var deposited=await store.DepositInventoryItemAsync(
                new TrustedStorageContext(actor,501,1,1),uid);
            Checks.Require(
                deposited.Status==StorageTransferStatus.Applied&&
                ReadLevel(db,"storage_items",uid)==7&&
                !Exists(db,"item_instances",uid),
                "storage deposit reset enhancement level");

            var withdrawn=await store.WithdrawStorageItemAsync(
                new TrustedStorageContext(actor,501,1,2),uid);
            Checks.Require(
                withdrawn.Status==StorageTransferStatus.Applied&&
                ReadLevel(db,"item_instances",uid)==7&&
                !Exists(db,"storage_items",uid),
                "storage withdraw reset enhancement level");

            IReadOnlyDictionary<int,EquipmentRule> rules=
                new Dictionary<int,EquipmentRule>{[4701]=new(4701,4)};
            var equipped=await store.EquipInventoryItemAsync(
                new TrustedEquipmentContext(actor,601,1,1),uid,rules);
            Checks.Require(
                equipped.Status==EquipmentTransferStatus.Applied&&
                ReadLevel(db,"equipped_items",uid)==7&&
                !Exists(db,"item_instances",uid),
                "equip reset enhancement level");

            var unequipped=await store.UnequipItemAsync(
                new TrustedEquipmentContext(actor,601,1,2),uid);
            Checks.Require(
                unequipped.Status==EquipmentTransferStatus.Applied&&
                ReadLevel(db,"item_instances",uid)==7&&
                !Exists(db,"equipped_items",uid),
                "unequip reset enhancement level");

            Checks.Require(ConstraintRejects(db,uid,-1)&&
                ConstraintRejects(db,uid,256)&&
                ReadLevel(db,"item_instances",uid)==7,
                "enhancement level bounds were not enforced");
        }

        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            Checks.Require(
                ReadLevel(db,"item_instances",uid)==7&&
                (await reopened.ListInventoryItemsAsync(actor)).Single().ItemUid==uid,
                "enhancement metadata did not survive database reopen");
        }
    }

    private static async Task MigrateV19(string root)
    {
        string dir=Path.Combine(root,"enhancement_migrate_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        long actor;
        long inventoryUid;
        long storageUid;
        long equippedUid;

        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "enhance_migrate","Enhance-Migrate-123!")).AccountId;
            actor=(await store.CreateCharacterAsync(
                account,0,"EnhanceMigrate",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId
                ??throw new Exception("enhancement migration character missing");
            long generation=await store.AllocateLootWorldGenerationAsync();

            inventoryUid=await SeedOne(store,actor,generation,1,4801);
            storageUid=await SeedOne(store,actor,generation,2,4802);
            equippedUid=await SeedOne(store,actor,generation,3,4803);

            var stored=await store.DepositInventoryItemAsync(
                new TrustedStorageContext(actor,701,1,1),storageUid);
            Checks.Require(stored.Status==StorageTransferStatus.Applied,
                "enhancement migration storage seed failed");
            var equipped=await store.EquipInventoryItemAsync(
                new TrustedEquipmentContext(actor,702,1,1),equippedUid,
                new Dictionary<int,EquipmentRule>{[4803]=new(4803,4)});
            Checks.Require(equipped.Status==EquipmentTransferStatus.Applied,
                "enhancement migration equipment seed failed");
        }

        using(var connection=Open(db))
        {
            using(var command=connection.CreateCommand())
            {
                command.CommandText="UPDATE schema_version SET version=19;";
                command.ExecuteNonQuery();
            }
            foreach(string table in new[]{"item_instances","storage_items","equipped_items"})
            {
                using var drop=connection.CreateCommand();
                drop.CommandText=$"ALTER TABLE {table} DROP COLUMN enhancement_level;";
                drop.ExecuteNonQuery();
            }
        }

        using(var migrated=new SqliteIdentityStore(db))
        {
            await migrated.InitializeAsync();
            Checks.Require(
                SqliteIdentityStore.SchemaVersion==27&&
                ReadLevel(db,"item_instances",inventoryUid)==0&&
                ReadLevel(db,"storage_items",storageUid)==0&&
                ReadLevel(db,"equipped_items",equippedUid)==0&&
                (await migrated.ListInventoryItemsAsync(actor)).Any(x=>x.ItemUid==inventoryUid)&&
                (await migrated.ListStorageItemsAsync(actor)).Single().ItemUid==storageUid&&
                (await migrated.ListEquippedItemsAsync(actor)).Single().ItemUid==equippedUid,
                "v19 enhancement migration changed UID/location or nonzero default");
        }
    }

    private static async Task MigrateV20Receipts(string root)
    {
        string dir=Path.Combine(root,"enhancement_receipt_migrate_"+
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        using(var store=new SqliteIdentityStore(db))
            await store.InitializeAsync();

        using(var connection=Open(db))
        {
            using(var version=connection.CreateCommand())
            {
                version.CommandText="UPDATE schema_version SET version=20;";
                version.ExecuteNonQuery();
            }
            foreach(string table in new[]{"trade_commit_items","player_shop_listings"})
            {
                using var drop=connection.CreateCommand();
                drop.CommandText=$"ALTER TABLE {table} DROP COLUMN enhancement_level;";
                drop.ExecuteNonQuery();
                Checks.Require(!HasColumn(connection,table,"enhancement_level"),
                    "v20 receipt fixture still had enhancement_level");
            }
        }

        using(var migrated=new SqliteIdentityStore(db))
        {
            await migrated.InitializeAsync();
            using var connection=Open(db);
            Checks.Require(
                SqliteIdentityStore.SchemaVersion==27&&
                HasColumn(connection,"trade_commit_items","enhancement_level")&&
                HasColumn(connection,"player_shop_listings","enhancement_level"),
                "v20 receipt migration did not restore enhancement metadata");
        }
    }

    private static bool HasColumn(
        SqliteConnection connection,string table,string column)
    {
        if(table is not ("trade_commit_items" or "player_shop_listings"))
            throw new ArgumentOutOfRangeException(nameof(table));
        using var command=connection.CreateCommand();
        command.CommandText=$"PRAGMA table_info({table});";
        using var reader=command.ExecuteReader();
        while(reader.Read())
            if(string.Equals(reader.GetString(1),column,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static async Task<long> SeedOne(
        SqliteIdentityStore store,long actor,long generation,int slot,int definition)
    {
        var drop=await store.CreateTrustedGroundDropAsync(new(
            15,8,8,991000+slot,
            SqliteIdentityStore.ComposeLootDeathRevision(generation,slot),
            0,definition,1,LootSourceTrust.SyntheticTest,
            "ci:enhancement-migration-seed-"+slot));
        var grant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
            actor,SqliteIdentityStore.ComposePickupConnectionEpoch(generation,slot),
            1,(uint)slot,15,8,8),drop.DropId);
        if(grant.Status!=PickupStatus.Granted||grant.ItemUid<=0)
            throw new InvalidOperationException("enhancement migration item seed failed");
        return grant.ItemUid;
    }

    private static bool ConstraintRejects(string db,long uid,int level)
    {
        try
        {
            SetLevel(db,"item_instances",uid,level);
            return false;
        }
        catch(SqliteException error) when(error.SqliteErrorCode==19)
        {
            return true;
        }
    }

    private static void SetLevel(string db,string table,long uid,int level)
    {
        ValidateTable(table);
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=$"UPDATE {table} SET enhancement_level=$level WHERE item_uid=$uid;";
        command.Parameters.AddWithValue("$level",level);
        command.Parameters.AddWithValue("$uid",uid);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("enhancement item row missing");
    }

    private static int ReadLevel(string db,string table,long uid)
    {
        ValidateTable(table);
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=$"SELECT enhancement_level FROM {table} WHERE item_uid=$uid;";
        command.Parameters.AddWithValue("$uid",uid);
        object? value=command.ExecuteScalar();
        if(value is null)
            throw new InvalidOperationException("enhancement item row missing");
        return Convert.ToInt32(value);
    }

    private static bool Exists(string db,string table,long uid)
    {
        ValidateTable(table);
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=$"SELECT COUNT(*) FROM {table} WHERE item_uid=$uid;";
        command.Parameters.AddWithValue("$uid",uid);
        return Convert.ToInt64(command.ExecuteScalar())!=0;
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

    private static void ValidateTable(string table)
    {
        if(table is not ("item_instances" or "storage_items" or "equipped_items"))
            throw new ArgumentOutOfRangeException(nameof(table));
    }
}
