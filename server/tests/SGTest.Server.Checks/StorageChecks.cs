using SGTest.Server.Npc;
using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

internal static class StorageChecks
{
    public static void Verify(string root)
    {
        Checks.Run("storage whole-stack UID transfer is atomic durable and replay-safe",
            () => WholeStackTransfer(root).GetAwaiter().GetResult());
    }

    private static async Task WholeStackTransfer(string root)
    {
        string dir = Path.Combine(root, "storage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db = Path.Combine(dir, "identity.db");
        long characterA;
        long characterB;
        long itemUid;
        long sourceDropId;

        using (var store = new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            long account = (await store.CreateAccountAsync(
                "storage_user", "Storage-Password-123!")).AccountId;
            characterA = (await store.CreateCharacterAsync(
                account, 0, "StorageA", new CharacterSpawn(15, 8, 8)))
                .Character?.CharacterId ?? throw new Exception("storage character A missing");
            characterB = (await store.CreateCharacterAsync(
                account, 1, "StorageB", new CharacterSpawn(15, 8, 8)))
                .Character?.CharacterId ?? throw new Exception("storage character B missing");

            long generation = await store.AllocateLootWorldGenerationAsync();
            var drop = await store.CreateTrustedGroundDropAsync(new(
                15, 8, 8, 7001,
                SqliteIdentityStore.ComposeLootDeathRevision(generation, 1),
                0, 2701, 3, LootSourceTrust.SyntheticTest, "storage-seed"));
            sourceDropId = drop.DropId;
            var grant = await store.ClaimGroundDropAsync(new TrustedPickupContext(
                characterA,
                SqliteIdentityStore.ComposePickupConnectionEpoch(generation, 1),
                1, 1, 15, 8, 8), drop.DropId);
            Checks.Require(grant.Status == PickupStatus.Granted &&
                grant.ItemUid > 0 && grant.Quantity == 3,
                "storage seed inventory grant failed");
            itemUid = grant.ItemUid;
            using (var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = db,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false
                }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "UPDATE item_instances SET enhancement_level=5,durability_raw=80 WHERE item_uid=$uid;";
                command.Parameters.AddWithValue("$uid", itemUid);
                Checks.Require(command.ExecuteNonQuery() == 1,
                    "storage enhancement-level seed failed");
            }

            var depositContext = new TrustedStorageContext(characterA, 101, 1, 1);
            var deposited = await store.DepositInventoryItemAsync(
                depositContext, itemUid);
            var depositReplay = await store.DepositInventoryItemAsync(
                depositContext, itemUid);
            var stored = await store.ListStorageItemsAsync(characterA);
            var inventory = await store.ListInventoryItemsAsync(characterA);
            Checks.Require(
                deposited.Status == StorageTransferStatus.Applied &&
                depositReplay.Status == StorageTransferStatus.AlreadyApplied &&
                deposited.ItemUid == itemUid &&
                deposited.ItemDefinitionId == 2701 &&
                deposited.Quantity == 3 &&
                inventory.Count == 0 &&
                stored.Count == 1 &&
                stored[0].ItemUid == itemUid &&
                stored[0].Quantity == 3 &&
                stored[0].SourceDropId == sourceDropId &&
                stored[0].EnhancementLevel == 5 &&
                stored[0].DurabilityRaw == 80,
                "storage deposit/replay did not preserve exact UID stack metadata");

            var foreign = await store.WithdrawStorageItemAsync(
                new TrustedStorageContext(characterB, 201, 1, 1), itemUid);
            Checks.Require(
                foreign.Status == StorageTransferStatus.NotStored &&
                (await store.ListStorageItemsAsync(characterA)).Single().ItemUid == itemUid &&
                (await store.ListInventoryItemsAsync(characterB)).Count == 0,
                "foreign character accessed stored UID");

            var conflictingReplay = await store.WithdrawStorageItemAsync(
                depositContext, itemUid);
            Checks.Require(
                conflictingReplay.Status == StorageTransferStatus.StaleSequence &&
                (await store.ListStorageItemsAsync(characterA)).Count == 1,
                "same storage sequence accepted a different action");

            var withdrawContext = new TrustedStorageContext(characterA, 101, 1, 2);
            var withdrawn = await store.WithdrawStorageItemAsync(
                withdrawContext, itemUid);
            var withdrawReplay = await store.WithdrawStorageItemAsync(
                withdrawContext, itemUid);
            var restored = await store.ListInventoryItemsAsync(characterA);
            Checks.Require(
                withdrawn.Status == StorageTransferStatus.Applied &&
                withdrawReplay.Status == StorageTransferStatus.AlreadyApplied &&
                (await store.ListStorageItemsAsync(characterA)).Count == 0 &&
                restored.Count == 1 &&
                restored[0].ItemUid == itemUid &&
                restored[0].ItemDefinitionId == 2701 &&
                restored[0].Quantity == 3 &&
                restored[0].SourceDropId == sourceDropId &&
                restored[0].EnhancementLevel == 5 &&
                restored[0].DurabilityRaw == 80,
                "storage withdraw/replay did not restore exact inventory UID metadata");

            bool overlapRejected = false;
            try
            {
                using var connection = new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = db,
                        Mode = SqliteOpenMode.ReadWrite,
                        Pooling = false
                    }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
INSERT INTO storage_items(
    item_uid,character_id,item_definition_id,quantity,source_drop_id,
    source_kind,source_reference,created_unix_ms)
SELECT item_uid,character_id,item_definition_id,quantity,source_drop_id,
       source_kind,source_reference,created_unix_ms
FROM item_instances WHERE item_uid=$uid;";
                command.Parameters.AddWithValue("$uid", itemUid);
                command.ExecuteNonQuery();
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19)
            {
                overlapRejected = true;
            }
            Checks.Require(overlapRejected,
                "database allowed one Item UID in inventory and storage simultaneously");
        }

        using (var reopened = new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var inventory = await reopened.ListInventoryItemsAsync(characterA);
            Checks.Require(
                inventory.Count == 1 &&
                inventory[0].ItemUid == itemUid &&
                inventory[0].Quantity == 3 &&
                inventory[0].EnhancementLevel == 5 &&
                inventory[0].DurabilityRaw == 80 &&
                (await reopened.ListStorageItemsAsync(characterA)).Count == 0,
                "storage transfer did not survive database reopen");
        }
    }
}
