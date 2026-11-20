using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

internal static class EnhancementMetadataChecks
{
    public static void Verify(string root)
    {
        Checks.Run("item enhancement metadata survives storage and equipment locations",
            () => Lifecycle(root).GetAwaiter().GetResult());
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
            Checks.Require(SqliteIdentityStore.SchemaVersion==20,
                "enhancement metadata requires schema v20");
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
