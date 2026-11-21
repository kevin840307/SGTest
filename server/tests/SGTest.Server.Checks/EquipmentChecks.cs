using SGTest.Server.Npc;
using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

internal static class EquipmentChecks
{
    public static void Verify(string root)
    {
        Checks.Run("equipment verified rule catalog fails closed",
            () => VerifiedCatalog(root));
        Checks.Run("equipment UID location is atomic slot-bound durable and replay-safe",
            () => WholeUidLifecycle(root).GetAwaiter().GetResult());
    }

    private static void VerifiedCatalog(string root)
    {
        string path=Path.Combine(root,"verified-equipment.json");
        File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new
        {
            kind="verified_equipment_item_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="ci:explicit-equipment-rule",
            rules=new[]
            {
                new{item_definition_id=3701,slot_id=4},
                new{item_definition_id=3702,slot_id=5}
            }
        }));
        var rules=EquipmentItemCatalog.LoadVerified(path);
        Checks.Require(rules.Count==2&&rules[3701].SlotId==4&&rules[3702].SlotId==5,
            "verified equipment catalog mismatch");

        File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new
        {
            kind="verified_equipment_item_rules",version=1,
            rules_verified=false,gameplay_ready=true,
            source_reference="ci:unverified",
            rules=new[]{new{item_definition_id=3701,slot_id=4}}
        }));
        bool rejected=false;
        try{EquipmentItemCatalog.LoadVerified(path);}
        catch(InvalidDataException){rejected=true;}
        Checks.Require(rejected,"unverified equipment rules were admitted");

        File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new
        {
            kind="verified_equipment_item_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="ci:duplicate",
            rules=new[]
            {
                new{item_definition_id=3701,slot_id=4},
                new{item_definition_id=3701,slot_id=5}
            }
        }));
        rejected=false;
        try{EquipmentItemCatalog.LoadVerified(path);}
        catch(InvalidDataException){rejected=true;}
        Checks.Require(rejected,"duplicate equipment definition rule was admitted");
    }

    private static async Task WholeUidLifecycle(string root)
    {
        string dir=Path.Combine(root,"equipment_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        long characterA;
        long characterB;
        long swordUid;
        long shieldUid;
        long swordDrop;

        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "equipment_user","Equipment-Password-123!")).AccountId;
            characterA=(await store.CreateCharacterAsync(
                account,0,"EquipA",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId??throw new Exception("equipment A missing");
            characterB=(await store.CreateCharacterAsync(
                account,1,"EquipB",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId??throw new Exception("equipment B missing");

            long generation=await store.AllocateLootWorldGenerationAsync();
            var swordDropRow=await store.CreateTrustedGroundDropAsync(new(
                15,8,8,8101,
                SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                0,3701,1,LootSourceTrust.SyntheticTest,"equipment-sword"));
            var swordGrant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
                characterA,SqliteIdentityStore.ComposePickupConnectionEpoch(generation,1),
                1,1,15,8,8),swordDropRow.DropId);
            Checks.Require(swordGrant.Status==PickupStatus.Granted&&
                swordGrant.Quantity==1,"equipment sword seed failed");
            swordUid=swordGrant.ItemUid;
            swordDrop=swordDropRow.DropId;

            var shieldDropRow=await store.CreateTrustedGroundDropAsync(new(
                15,8,8,8102,
                SqliteIdentityStore.ComposeLootDeathRevision(generation,2),
                0,3702,1,LootSourceTrust.SyntheticTest,"equipment-shield"));
            var shieldGrant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
                characterA,SqliteIdentityStore.ComposePickupConnectionEpoch(generation,1),
                1,2,15,8,8),shieldDropRow.DropId);
            Checks.Require(shieldGrant.Status==PickupStatus.Granted&&
                shieldGrant.Quantity==1,"equipment shield seed failed");
            shieldUid=shieldGrant.ItemUid;

            IReadOnlyDictionary<int,EquipmentRule> sameSlotRules=
                new Dictionary<int,EquipmentRule>
                {
                    [3701]=new(3701,4),
                    [3702]=new(3702,4)
                };
            IReadOnlyDictionary<int,EquipmentRule> swordOnlyRules=
                new Dictionary<int,EquipmentRule>
                {
                    [3701]=new(3701,4)
                };

            var equipContext=new TrustedEquipmentContext(characterA,301,1,1);
            var equipped=await store.EquipInventoryItemAsync(
                equipContext,swordUid,sameSlotRules);
            var replay=await store.EquipInventoryItemAsync(
                equipContext,swordUid,sameSlotRules);
            var equippedRows=await store.ListEquippedItemsAsync(characterA);
            var inventory=await store.ListInventoryItemsAsync(characterA);
            Checks.Require(
                equipped.Status==EquipmentTransferStatus.Applied&&
                replay.Status==EquipmentTransferStatus.AlreadyApplied&&
                equipped.ItemUid==swordUid&&equipped.ItemDefinitionId==3701&&
                equipped.SlotId==4&&equipped.SourceDropId==swordDrop&&
                equippedRows.Count==1&&equippedRows[0].ItemUid==swordUid&&
                equippedRows[0].SlotId==4&&equippedRows[0].SourceDropId==swordDrop&&
                inventory.Count==1&&inventory[0].ItemUid==shieldUid,
                "equipment equip/replay did not preserve exact UID");

            var occupied=await store.EquipInventoryItemAsync(
                new TrustedEquipmentContext(characterA,301,1,2),
                shieldUid,sameSlotRules);
            Checks.Require(
                occupied.Status==EquipmentTransferStatus.SlotOccupied&&
                (await store.ListEquippedItemsAsync(characterA)).Single().ItemUid==swordUid&&
                (await store.ListInventoryItemsAsync(characterA)).Single().ItemUid==shieldUid,
                "occupied equipment slot mutated inventory");

            var invalidRule=await store.EquipInventoryItemAsync(
                new TrustedEquipmentContext(characterA,301,1,3),
                shieldUid,swordOnlyRules);
            Checks.Require(
                invalidRule.Status==EquipmentTransferStatus.InvalidRule&&
                (await store.ListInventoryItemsAsync(characterA)).Single().ItemUid==shieldUid,
                "mismatched trusted equipment definition was accepted");

            var foreign=await store.UnequipItemAsync(
                new TrustedEquipmentContext(characterB,401,1,1),swordUid);
            Checks.Require(
                foreign.Status==EquipmentTransferStatus.NotEquipped&&
                (await store.ListEquippedItemsAsync(characterA)).Count==1,
                "foreign character unequipped another character item");

            var conflictingReplay=await store.UnequipItemAsync(
                equipContext,swordUid);
            Checks.Require(
                conflictingReplay.Status==EquipmentTransferStatus.StaleSequence&&
                (await store.ListEquippedItemsAsync(characterA)).Count==1,
                "same equipment sequence accepted different action");

            var unequipContext=new TrustedEquipmentContext(characterA,301,1,4);
            var unequipped=await store.UnequipItemAsync(unequipContext,swordUid);
            var unequipReplay=await store.UnequipItemAsync(unequipContext,swordUid);
            inventory=await store.ListInventoryItemsAsync(characterA);
            Checks.Require(
                unequipped.Status==EquipmentTransferStatus.Applied&&
                unequipReplay.Status==EquipmentTransferStatus.AlreadyApplied&&
                unequipped.SlotId==4&&unequipped.SourceDropId==swordDrop&&
                (await store.ListEquippedItemsAsync(characterA)).Count==0&&
                inventory.Count==2&&inventory.Any(x=>x.ItemUid==swordUid)&&
                inventory.Any(x=>x.ItemUid==shieldUid),
                "equipment unequip/replay did not restore exact inventory UID");

            bool overlapRejected=false;
            try
            {
                using var connection=new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false
                    }.ToString());
                connection.Open();
                using var command=connection.CreateCommand();
                command.CommandText=@"
INSERT INTO equipped_items(
    item_uid,character_id,slot_id,item_definition_id,quantity,source_drop_id,
    source_kind,source_reference,created_unix_ms)
SELECT item_uid,character_id,4,item_definition_id,quantity,source_drop_id,
       source_kind,source_reference,created_unix_ms
FROM item_instances WHERE item_uid=$uid;";
                command.Parameters.AddWithValue("$uid",swordUid);
                command.ExecuteNonQuery();
            }
            catch(SqliteException e) when(e.SqliteErrorCode==19)
            {
                overlapRejected=true;
            }
            Checks.Require(overlapRejected,
                "database allowed one Item UID in inventory and equipment");
        }

        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var inventory=await reopened.ListInventoryItemsAsync(characterA);
            Checks.Require(
                inventory.Count==2&&inventory.Any(x=>x.ItemUid==swordUid)&&
                inventory.Any(x=>x.ItemUid==shieldUid)&&
                (await reopened.ListEquippedItemsAsync(characterA)).Count==0,
                "equipment lifecycle did not survive database reopen");
        }
    }
}
