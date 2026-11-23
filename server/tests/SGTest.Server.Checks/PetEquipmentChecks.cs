using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class PetEquipmentChecks
{
    public static void Verify()
    {
        Checks.Run("pet equipment wire carries ItemUid slot and durable snapshot",Wire);
        Checks.Run("pet equipment manifest is explicit provenance-gated",Manifest);
        Checks.Run("pet equipment UID lifecycle is atomic durable replay-safe and fail-closed",
            ()=>Lifecycle().GetAwaiter().GetResult());
    }

    private static void Wire()
    {
        var command=new PetCommandWire(7,3,PetCommandAction.Equip,101,9001);
        Checks.Require(WireProtocol.TryDecodePetCommand(
            WireProtocol.EncodePetCommand(command),out var decoded)&&decoded==command,
            "pet equip command wire roundtrip failed");

        var result=new PetResultWire(
            7,3,PetCommandAction.Equip,PetResultStatus.Applied,101,0,9001,2,8999);
        Checks.Require(WireProtocol.TryDecodePetResult(
            WireProtocol.EncodePetResult(result),out var decodedResult)&&decodedResult==result,
            "pet equip result wire roundtrip failed");

        var snapshot=new PetEquipmentSnapshotWire(7,[
            new PetEquipmentEntryWire(101,9001,2,8101,501,4,80),
            new PetEquipmentEntryWire(102,9002,3,8201,0,0,-1)
        ]);
        Checks.Require(WireProtocol.TryDecodePetEquipmentSnapshot(
            WireProtocol.EncodePetEquipmentSnapshot(snapshot),out var decodedSnapshot)&&
            decodedSnapshot is not null&&decodedSnapshot.Items.Count==2&&
            decodedSnapshot.Items[0].PetUid==101&&
            decodedSnapshot.Items[1].SlotId==3,
            "pet equipment snapshot roundtrip failed");

        bool rejected=false;
        try
        {
            WireProtocol.EncodePetEquipmentSnapshot(new(7,[
                new PetEquipmentEntryWire(101,9001,2,8101,0,0,-1),
                new PetEquipmentEntryWire(101,9002,2,8102,0,0,-1)
            ]));
        }
        catch(ArgumentOutOfRangeException){rejected=true;}
        Checks.Require(rejected,
            "pet equipment snapshot accepted duplicate physical slot");
    }

    private static void Manifest()
    {
        string root=Path.Combine(
            Path.GetTempPath(),"sgtest_pet_equipment_manifest_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path=Path.Combine(root,"pet-equipment.json");
        try
        {
            File.WriteAllText(path,
                """
                {
                  "kind":"verified_pet_equipment_rules",
                  "version":1,
                  "rules_verified":true,
                  "gameplay_ready":true,
                  "source_reference":"Old SGOPetSys OnEquip/CSGOPet::Equip; exact HK slots audited separately",
                  "rules":[
                    {"item_definition_id":8101,"slot_id":2,"required_level":1},
                    {"item_definition_id":8201,"slot_id":3,"required_level":5}
                  ]
                }
                """);
            var rules=PetEquipmentCatalog.LoadVerified(path);
            Checks.Require(rules.Count==2&&rules[8101].SlotId==2&&
                rules[8201].RequiredLevel==5,
                "verified pet equipment catalog mismatch");

            File.WriteAllText(path,
                """
                {
                  "kind":"verified_pet_equipment_rules",
                  "version":1,
                  "rules_verified":false,
                  "gameplay_ready":true,
                  "source_reference":"not verified",
                  "rules":[{"item_definition_id":8101,"slot_id":2,"required_level":1}]
                }
                """);
            bool rejected=false;
            try{PetEquipmentCatalog.LoadVerified(path);}
            catch(InvalidDataException){rejected=true;}
            Checks.Require(rejected,"unverified pet equipment rules were admitted");
        }
        finally{Directory.Delete(root,true);}
    }

    private static async Task Lifecycle()
    {
        string root=Path.Combine(
            Path.GetTempPath(),"sgtest_pet_equipment_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        long character=0,pet=0,first=0,second=0;
        try
        {
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "pet_equipment","Pet-Equipment-Secret-2026!")).AccountId;
                character=(await store.CreateCharacterAsync(
                    account,0,"PetEquip",new(15,100,100))).Character!.CharacterId;
                pet=await store.ProvisionPetAsync(character,5001,5,0,evolutionId:2);

                long generation=await store.AllocateLootWorldGenerationAsync();
                uint pickup=1;
                async Task<long> Seed(int definition,int deathRevision)
                {
                    var drop=await store.CreateTrustedGroundDropAsync(new(
                        15,100,100,9100+deathRevision,
                        SqliteIdentityStore.ComposeLootDeathRevision(
                            generation,(uint)deathRevision),
                        0,definition,1,LootSourceTrust.SyntheticTest,
                        "pet-equipment-"+definition));
                    var grant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
                        character,
                        SqliteIdentityStore.ComposePickupConnectionEpoch(generation,1),
                        1,pickup++,15,100,100),drop.DropId);
                    Checks.Require(grant.Status==PickupStatus.Granted&&grant.Quantity==1,
                        "pet equipment seed failed for "+definition);
                    return grant.ItemUid;
                }

                first=await Seed(8101,1);
                second=await Seed(8102,2);
                IReadOnlyDictionary<int,PetEquipmentRule> rules=
                    new Dictionary<int,PetEquipmentRule>
                    {
                        [8101]=new(8101,2,1),
                        [8102]=new(8102,2,1)
                    };

                var equippedFirst=await store.ApplyPetEquipmentActionAsync(
                    character,31,6,1,PetActionKind.Equip,pet,first,rules);
                Checks.Require(equippedFirst.Status==PetActionStatus.Applied&&
                    equippedFirst.SlotId==2,
                    "first pet item did not equip");

                var replacement=await store.ApplyPetEquipmentActionAsync(
                    character,31,6,2,PetActionKind.Equip,pet,second,rules);
                var inventory=await store.ListInventoryItemsAsync(character);
                var equipment=await store.ListPetEquippedItemsAsync(character);
                Checks.Require(replacement.Status==PetActionStatus.Applied&&
                    replacement.ReplacedItemUid==first&&replacement.SlotId==2&&
                    equipment.Count==1&&equipment[0].ItemUid==second&&
                    inventory.Any(x=>x.ItemUid==first)&&
                    inventory.All(x=>x.ItemUid!=second),
                    "pet replacement did not atomically return old ItemUid");

                var replay=await store.ApplyPetEquipmentActionAsync(
                    character,31,6,2,PetActionKind.Equip,pet,second,rules);
                Checks.Require(replay.Status==PetActionStatus.AlreadyProcessed&&
                    replay.SlotId==2&&replay.ReplacedItemUid==first,
                    "exact pet equipment retransmit was not idempotent");

                var conflict=await store.ApplyPetEquipmentActionAsync(
                    character,31,6,2,PetActionKind.Equip,pet,first,rules);
                Checks.Require(conflict.Status==PetActionStatus.SequenceConflict,
                    "same pet equipment sequence accepted different ItemUid");

                var unequip=await store.ApplyPetEquipmentActionAsync(
                    character,31,6,3,PetActionKind.Unequip,pet,second,null);
                Checks.Require(unequip.Status==PetActionStatus.Applied&&
                    (await store.ListPetEquippedItemsAsync(character)).Count==0&&
                    (await store.ListInventoryItemsAsync(character))
                        .Any(x=>x.ItemUid==second),
                    "pet unequip did not return exact ItemUid to inventory");

                var unavailable=await store.ApplyPetEquipmentActionAsync(
                    character,31,6,4,PetActionKind.Equip,pet,first,null);
                Checks.Require(unavailable.Status==PetActionStatus.RulesUnavailable,
                    "pet equipment did not fail closed without verified rules");

                IReadOnlyDictionary<int,PetEquipmentRule> highLevel=
                    new Dictionary<int,PetEquipmentRule>{[8101]=new(8101,2,99)};
                var tooLow=await store.ApplyPetEquipmentActionAsync(
                    character,31,6,5,PetActionKind.Equip,pet,first,highLevel);
                Checks.Require(tooLow.Status==PetActionStatus.LevelTooLow,
                    "pet equipment level gate was bypassed");

                var finalEquip=await store.ApplyPetEquipmentActionAsync(
                    character,31,6,6,PetActionKind.Equip,pet,first,rules);
                equipment=await store.ListPetEquippedItemsAsync(character);
                inventory=await store.ListInventoryItemsAsync(character);
                Checks.Require(finalEquip.Status==PetActionStatus.Applied&&
                    equipment.Single().ItemUid==first&&
                    inventory.All(x=>x.ItemUid!=first)&&
                    !inventory.Select(x=>x.ItemUid)
                        .Intersect(equipment.Select(x=>x.ItemUid)).Any(),
                    "pet equipment/inventory physical UID locations overlap");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var persisted=await reopened.ListPetEquippedItemsAsync(character);
                Checks.Require(persisted.Count==1&&persisted[0].PetUid==pet&&
                    persisted[0].ItemUid==first&&persisted[0].SlotId==2,
                    "pet equipment did not survive database reopen");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root,true);
        }
    }
}
