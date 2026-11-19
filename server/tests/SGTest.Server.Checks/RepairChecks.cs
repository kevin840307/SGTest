using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

internal static class RepairChecks
{
    public static void Verify(string root)
    {
        Checks.Run("Old repair formula and raw-50 durability are durable and replay-safe",
            () => Lifecycle(root).GetAwaiter().GetResult());
    }

    private static async Task Lifecycle(string root)
    {
        Checks.Require(SqliteIdentityStore.DisplayDurability(5000)==100&&
            SqliteIdentityStore.DisplayDurability(4999)==100&&
            SqliteIdentityStore.DisplayDurability(4950)==99&&
            SqliteIdentityStore.DisplayDurability(1)==1&&
            SqliteIdentityStore.DisplayDurability(0)==0,
            "Old raw durability display conversion mismatch");
        Checks.Require(
            SqliteIdentityStore.RepairCost(1000,100,80,10)==20&&
            SqliteIdentityStore.RepairCost(1000,100,100,10)==1&&
            SqliteIdentityStore.RepairCost(333,7,6,10)==5,
            "Old repair ceil/minimum-one formula mismatch");

        string dir=Path.Combine(root,"repair_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        long characterId;
        long uid;

        var itemRules=new Dictionary<int,RepairItemRule>
        {
            [3701]=new(3701,1000,100,"old:repair-item-3701")
        };
        var equipmentRules=new Dictionary<int,EquipmentRule>
        {
            [3701]=new(3701,4)
        };
        var npcRule=new TrustedRepairNpcRule(
            88,10,"old:Repair.txt:OpenRepair(10)");

        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "repair_user","Repair-Password-123!")).AccountId;
            characterId=(await store.CreateCharacterAsync(
                account,0,"RepairHero",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId??throw new Exception("repair character missing");
            SeedWallet(db,characterId,100);

            long generation=await store.AllocateLootWorldGenerationAsync();
            var drop=await store.CreateTrustedGroundDropAsync(new(
                15,8,8,9301,
                SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                0,3701,1,LootSourceTrust.SyntheticTest,"repair-seed"));
            var grant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
                characterId,SqliteIdentityStore.ComposePickupConnectionEpoch(generation,1),
                1,1,15,8,8),drop.DropId);
            Checks.Require(grant.Status==PickupStatus.Granted&&grant.Quantity==1,
                "repair item seed failed");
            uid=grant.ItemUid;

            var equipped=await store.EquipInventoryItemAsync(
                new TrustedEquipmentContext(characterId,301,1,1),
                uid,equipmentRules);
            Checks.Require(equipped.Status==EquipmentTransferStatus.Applied,
                "repair seed equip failed");

            var wear=await store.ApplyTrustedEquippedWearAsync(
                characterId,uid,itemRules,1001);
            Checks.Require(wear.Applied&&wear.PreviousRaw==5000&&
                wear.CurrentRaw==3999&&!wear.Broken,
                "repair wear did not preserve Old raw scale");

            var preview=await store.ListRepairPreviewAsync(
                characterId,npcRule,itemRules);
            var item=preview.Items.Single();
            Checks.Require(preview.WalletMoney==100&&item.ItemUid==uid&&
                item.Location==RepairItemLocation.Equipped&&
                item.CurrentDurability==80&&item.MaxDurability==100&&
                item.Cost==20,
                "repair preview formula/location mismatch");

            var context=new TrustedRepairContext(characterId,501,1,1);
            var repaired=await store.RepairItemAsync(
                context,RepairAccessStatus.Allowed,npcRule,itemRules,uid);
            var replay=await store.RepairItemAsync(
                context,RepairAccessStatus.Allowed,npcRule,itemRules,uid);
            Checks.Require(
                repaired.Status==RepairStatus.Applied&&
                repaired.CurrentDurability==100&&repaired.MaxDurability==100&&
                repaired.Cost==20&&repaired.WalletMoney==80&&
                replay.Status==RepairStatus.AlreadyApplied&&
                replay.Cost==20&&replay.WalletMoney==80,
                "repair apply/exact replay mismatch");

            var full=await store.RepairItemAsync(
                new TrustedRepairContext(characterId,501,1,2),
                RepairAccessStatus.Allowed,npcRule,itemRules,uid);
            Checks.Require(full.Status==RepairStatus.AlreadyFull&&
                full.Cost==0&&full.WalletMoney==80,
                "full item was charged for repair");

            wear=await store.ApplyTrustedEquippedWearAsync(
                characterId,uid,itemRules,1);
            preview=await store.ListRepairPreviewAsync(
                characterId,npcRule,itemRules);
            item=preview.Items.Single();
            Checks.Require(wear.CurrentRaw==4999&&
                item.CurrentDurability==100&&item.Cost==1,
                "sub-display raw wear did not retain minimum-one repair cost");

            var minRepair=await store.RepairItemAsync(
                new TrustedRepairContext(characterId,501,1,3),
                RepairAccessStatus.Allowed,npcRule,itemRules,uid);
            Checks.Require(minRepair.Status==RepairStatus.Applied&&
                minRepair.Cost==1&&minRepair.WalletMoney==79,
                "minimum-one repair settlement mismatch");

            // Durability follows the UID through equipment -> inventory -> storage -> inventory.
            wear=await store.ApplyTrustedEquippedWearAsync(
                characterId,uid,itemRules,50);
            Checks.Require(wear.CurrentRaw==4950,"second wear mismatch");
            var unequip=await store.UnequipItemAsync(
                new TrustedEquipmentContext(characterId,302,1,1),uid);
            Checks.Require(unequip.Status==EquipmentTransferStatus.Applied,
                "repair durability unequip failed");
            var stored=await store.DepositInventoryItemAsync(
                new TrustedStorageContext(characterId,601,1,1),uid);
            Checks.Require(stored.Status==StorageTransferStatus.Applied,
                "repair durability storage deposit failed");
            Checks.Require(ReadDurability(db,"storage_items",uid)==4950,
                "storage move lost raw durability");
            var withdrawn=await store.WithdrawStorageItemAsync(
                new TrustedStorageContext(characterId,601,1,2),uid);
            Checks.Require(withdrawn.Status==StorageTransferStatus.Applied&&
                ReadDurability(db,"item_instances",uid)==4950,
                "storage withdraw lost raw durability");

            preview=await store.ListRepairPreviewAsync(
                characterId,npcRule,itemRules);
            item=preview.Items.Single();
            Checks.Require(item.Location==RepairItemLocation.Inventory&&
                item.CurrentDurability==99&&item.Cost==1,
                "inventory repair preview after storage mismatch");

            SeedWallet(db,characterId,0);
            var insufficient=await store.RepairItemAsync(
                new TrustedRepairContext(characterId,501,1,4),
                RepairAccessStatus.Allowed,npcRule,itemRules,uid);
            Checks.Require(insufficient.Status==RepairStatus.InsufficientMoney&&
                insufficient.Cost==1&&insufficient.WalletMoney==0&&
                ReadDurability(db,"item_instances",uid)==4950,
                "insufficient money mutated durability");

            var denied=await store.RepairItemAsync(
                new TrustedRepairContext(characterId,501,1,5),
                RepairAccessStatus.TooFar,npcRule,itemRules,uid);
            Checks.Require(denied.Status==RepairStatus.TooFar&&
                ReadDurability(db,"item_instances",uid)==4950,
                "repair access denial mutated item");
        }

        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var preview=await reopened.ListRepairPreviewAsync(
                characterId,npcRule,itemRules);
            var item=preview.Items.Single();
            Checks.Require(item.ItemUid==uid&&item.CurrentDurability==99&&
                item.MaxDurability==100&&item.Cost==1,
                "repair durability did not survive DB reopen");
        }
    }

    private static void SeedWallet(string db,long characterId,long money)
    {
        using var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText=@"
INSERT INTO character_wallets(character_id,money) VALUES($id,$money)
ON CONFLICT(character_id) DO UPDATE SET money=excluded.money;";
        command.Parameters.AddWithValue("$id",characterId);
        command.Parameters.AddWithValue("$money",money);
        Checks.Require(command.ExecuteNonQuery()==1,"repair wallet seed failed");
    }

    private static int? ReadDurability(string db,string table,long uid)
    {
        if(table is not ("item_instances" or "storage_items" or "equipped_items"))
            throw new ArgumentOutOfRangeException(nameof(table));
        using var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText=$"SELECT durability_raw FROM {table} WHERE item_uid=$uid;";
        command.Parameters.AddWithValue("$uid",uid);
        object? value=command.ExecuteScalar();
        return value is null or DBNull?null:Convert.ToInt32(value);
    }
}
