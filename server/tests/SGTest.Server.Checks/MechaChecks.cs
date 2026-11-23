using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class MechaChecks
{
    public static void Verify()
    {
        Checks.Run("mecha wire roundtrips authoritative mount roster and equipment",Wire);
        Checks.Run("mecha verified equipment catalog fails closed",VerifiedCatalog);
        Checks.Run("mecha verified battery catalog fails closed",VerifiedBatteryCatalog);
        Checks.Run("mecha battery charge is atomic durable and replay-safe",
            ()=>BatteryLifecycle().GetAwaiter().GetResult());
        Checks.Run("mecha mount dismount replay ownership and persistence are authoritative",
            ()=>Persistence().GetAwaiter().GetResult());
        Checks.Run("mecha equipment UID lifecycle is atomic durable and replay-safe",
            ()=>EquipmentLifecycle().GetAwaiter().GetResult());
    }

    private static void Wire()
    {
        var mount=new MechaCommandWire(7,1,MechaCommandAction.Mount,101);
        Checks.Require(WireProtocol.TryDecodeMechaCommand(
            WireProtocol.EncodeMechaCommand(mount),out var decoded)&&decoded==mount,
            "mecha mount wire roundtrip failed");

        var refresh=new MechaCommandWire(7,2,MechaCommandAction.Refresh,0);
        Checks.Require(WireProtocol.TryDecodeMechaCommand(
            WireProtocol.EncodeMechaCommand(refresh),out decoded)&&decoded==refresh,
            "mecha refresh wire roundtrip failed");

        var equip=new MechaCommandWire(7,3,MechaCommandAction.Equip,101,9001);
        Checks.Require(WireProtocol.TryDecodeMechaCommand(
            WireProtocol.EncodeMechaCommand(equip),out decoded)&&decoded==equip,
            "mecha equip wire roundtrip failed");

        var result=new MechaResultWire(
            7,3,MechaCommandAction.Equip,MechaResultStatus.Applied,
            101,0,9001,1,9000);
        Checks.Require(WireProtocol.TryDecodeMechaResult(
            WireProtocol.EncodeMechaResult(result),out var decodedResult)&&
            decodedResult==result,"mecha result wire roundtrip failed");

        var snapshot=new MechaSnapshotWire(7,102,[
            new MechaSnapshotEntryWire(101,7001,4,88),
            new MechaSnapshotEntryWire(102,7002,7,1234)
        ]);
        Checks.Require(WireProtocol.TryDecodeMechaSnapshot(
            WireProtocol.EncodeMechaSnapshot(snapshot),out var decodedSnapshot)&&
            decodedSnapshot is not null&&decodedSnapshot.MountedMechaUid==102&&
            decodedSnapshot.Mechas.Count==2&&
            decodedSnapshot.Mechas[1].DefinitionId==7002&&
            decodedSnapshot.Mechas[1].Level==7&&
            decodedSnapshot.Mechas[1].Experience==1234,
            "mecha snapshot wire roundtrip failed");

        var equipment=new MechaEquipmentSnapshotWire(7,[
            new MechaEquipmentEntryWire(101,9001,1,7101,501,5,80),
            new MechaEquipmentEntryWire(101,9002,3,7301,502,2,-1)
        ]);
        Checks.Require(WireProtocol.TryDecodeMechaEquipmentSnapshot(
            WireProtocol.EncodeMechaEquipmentSnapshot(equipment),out var decodedEquipment)&&
            decodedEquipment is not null&&decodedEquipment.Items.Count==2&&
            decodedEquipment.Items[0].ItemUid==9001&&
            decodedEquipment.Items[1].SlotId==3&&
            decodedEquipment.Items[1].DurabilityRaw==-1,
            "mecha equipment snapshot roundtrip failed");

        var presentation=new MechaPresentationWire(
            7,99,44,8,7,true,102,7002,7);
        Checks.Require(WireProtocol.TryDecodeMechaPresentation(
            WireProtocol.EncodeMechaPresentation(presentation),out var decodedPresentation)&&
            decodedPresentation==presentation,
            "mounted mecha presentation wire roundtrip failed");
        var cleared=presentation with {Mounted=false,MechaUid=0,DefinitionId=0,Level=0};
        Checks.Require(WireProtocol.TryDecodeMechaPresentation(
            WireProtocol.EncodeMechaPresentation(cleared),out decodedPresentation)&&
            decodedPresentation==cleared,
            "dismounted mecha presentation wire roundtrip failed");

        var malformed=WireProtocol.EncodeMechaCommand(mount);
        malformed[20]=9;
        Checks.Require(!WireProtocol.TryDecodeMechaCommand(malformed,out _),
            "mecha command accepted invalid action");

        bool rejected=false;
        try
        {
            WireProtocol.EncodeMechaSnapshot(new(7,999,[
                new MechaSnapshotEntryWire(101,7001,1,0)
            ]));
        }
        catch(ArgumentOutOfRangeException){rejected=true;}
        Checks.Require(rejected,"mecha snapshot accepted mounted uid outside roster");

        rejected=false;
        try
        {
            WireProtocol.EncodeMechaEquipmentSnapshot(new(7,[
                new MechaEquipmentEntryWire(101,9001,3,7301,0,0,-1),
                new MechaEquipmentEntryWire(101,9002,3,7302,0,0,-1)
            ]));
        }
        catch(ArgumentOutOfRangeException){rejected=true;}
        Checks.Require(rejected,"mecha equipment snapshot accepted duplicate physical slot");
    }

    private static void VerifiedCatalog()
    {
        string root=Path.Combine(
            Path.GetTempPath(),"sgtest_mecha_rules_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path=Path.Combine(root,"verified-mecha-equipment.json");
        try
        {
            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new
            {
                kind="verified_mecha_equipment_rules",version=1,
                rules_verified=true,gameplay_ready=true,
                source_reference="ci:explicit-mecha-equipment-rule",
                rules=new[]
                {
                    new{item_definition_id=7101,equipment_class=1,required_level=1},
                    new{item_definition_id=7201,equipment_class=2,required_level=3},
                    new{item_definition_id=7301,equipment_class=3,required_level=1},
                    new{item_definition_id=7601,equipment_class=6,required_level=4}
                }
            }));
            var rules=MechaEquipmentCatalog.LoadVerified(path);
            Checks.Require(
                rules.Count==4&&rules[7101].EquipmentClass==1&&
                rules[7301].EquipmentClass==3&&rules[7601].RequiredLevel==4,
                "verified mecha equipment catalog mismatch");

            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new
            {
                kind="verified_mecha_equipment_rules",version=1,
                rules_verified=false,gameplay_ready=true,
                source_reference="ci:unverified",
                rules=new[]{new{item_definition_id=7101,equipment_class=1,required_level=1}}
            }));
            bool rejected=false;
            try{MechaEquipmentCatalog.LoadVerified(path);}
            catch(InvalidDataException){rejected=true;}
            Checks.Require(rejected,"unverified mecha equipment rules were admitted");

            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new
            {
                kind="verified_mecha_equipment_rules",version=1,
                rules_verified=true,gameplay_ready=true,
                source_reference="ci:invalid-class",
                rules=new[]{new{item_definition_id=7101,equipment_class=4,required_level=1}}
            }));
            rejected=false;
            try{MechaEquipmentCatalog.LoadVerified(path);}
            catch(InvalidDataException){rejected=true;}
            Checks.Require(rejected,"unsupported mecha equipment class was admitted");
        }
        finally { Directory.Delete(root,true); }
    }

    private static void VerifiedBatteryCatalog()
    {
        string root=Path.Combine(
            Path.GetTempPath(),"sgtest_mecha_battery_rules_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path=Path.Combine(root,"verified-mecha-battery.json");
        try
        {
            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new
            {
                kind="verified_mecha_battery_rules",version=1,
                rules_verified=true,gameplay_ready=true,
                source_reference="ci:synthetic-battery-rule-not-hk",
                rules=new[]
                {
                    new{mecha_definition_id=7001,battery_item_definition_id=7701,
                        capacity=100,charge_amount=40,allow_while_mounted=false},
                    new{mecha_definition_id=7001,battery_item_definition_id=7702,
                        capacity=100,charge_amount=100,allow_while_mounted=true}
                }
            }));
            var rules=MechaBatteryCatalog.LoadVerified(path);
            Checks.Require(rules.Count==2&&rules.CapacityFor(7001)==100&&
                rules.TryGet(7001,7701,out var rule)&&rule.ChargeAmount==40&&
                !rule.AllowWhileMounted,
                "verified mecha battery catalog mismatch");

            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new
            {
                kind="verified_mecha_battery_rules",version=1,
                rules_verified=false,gameplay_ready=true,
                source_reference="ci:unverified",
                rules=new[]{new{mecha_definition_id=7001,battery_item_definition_id=7701,
                    capacity=100,charge_amount=40,allow_while_mounted=false}}
            }));
            bool rejected=false;
            try{MechaBatteryCatalog.LoadVerified(path);}
            catch(InvalidDataException){rejected=true;}
            Checks.Require(rejected,"unverified mecha battery rules were admitted");

            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new
            {
                kind="verified_mecha_battery_rules",version=1,
                rules_verified=true,gameplay_ready=true,
                source_reference="ci:conflicting-capacity",
                rules=new[]
                {
                    new{mecha_definition_id=7001,battery_item_definition_id=7701,
                        capacity=100,charge_amount=40,allow_while_mounted=false},
                    new{mecha_definition_id=7001,battery_item_definition_id=7702,
                        capacity=120,charge_amount=40,allow_while_mounted=false}
                }
            }));
            rejected=false;
            try{MechaBatteryCatalog.LoadVerified(path);}
            catch(InvalidDataException){rejected=true;}
            Checks.Require(rejected,"conflicting mecha battery capacities were admitted");
        }
        finally { Directory.Delete(root,true); }
    }

    private static async Task BatteryLifecycle()
    {
        string root=Path.Combine(
            Path.GetTempPath(),"sgtest_mecha_battery_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        string manifest=Path.Combine(root,"verified-mecha-battery.json");
        try
        {
            File.WriteAllText(manifest,System.Text.Json.JsonSerializer.Serialize(new
            {
                kind="verified_mecha_battery_rules",version=1,
                rules_verified=true,gameplay_ready=true,
                source_reference="ci:synthetic-battery-rule-not-hk",
                rules=new[]
                {
                    new{mecha_definition_id=7001,battery_item_definition_id=7701,
                        capacity=100,charge_amount=40,allow_while_mounted=false},
                    new{mecha_definition_id=7001,battery_item_definition_id=7702,
                        capacity=100,charge_amount=100,allow_while_mounted=true}
                }
            }));
            var rules=MechaBatteryCatalog.LoadVerified(manifest);
            long character,mecha,batteryUid,incompatibleUid,mountedBatteryUid;
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "mecha_battery","Mecha-Battery-Secret-2026!")).AccountId;
                character=(await store.CreateCharacterAsync(
                    account,0,"MechaBattery",new(15,100,100))).Character!.CharacterId;
                mecha=await store.ProvisionMechaAsync(character,7001,5,0);

                long generation=await store.AllocateLootWorldGenerationAsync();
                uint pickup=1;
                async Task<long> Seed(int definition,int quantity,int revision)
                {
                    var drop=await store.CreateTrustedGroundDropAsync(new(
                        15,100,100,9700+revision,
                        SqliteIdentityStore.ComposeLootDeathRevision(generation,(uint)revision),
                        0,definition,quantity,LootSourceTrust.SyntheticTest,
                        "mecha-battery-"+definition));
                    var grant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
                        character,
                        SqliteIdentityStore.ComposePickupConnectionEpoch(generation,1),
                        1,pickup++,15,100,100),drop.DropId);
                    Checks.Require(grant.Status==PickupStatus.Granted&&grant.Quantity==quantity,
                        "mecha battery seed failed for "+definition);
                    return grant.ItemUid;
                }

                batteryUid=await Seed(7701,2,1);
                incompatibleUid=await Seed(7799,1,2);
                mountedBatteryUid=await Seed(7702,1,3);

                var unavailable=await store.ApplyMechaBatteryChargeAsync(
                    character,31,6,1,mecha,batteryUid,null);
                Checks.Require(unavailable.Status==MechaActionStatus.RulesUnavailable&&
                    (await store.ListInventoryItemsAsync(character))
                        .Single(x=>x.ItemUid==batteryUid).Quantity==2,
                    "missing battery rules did not fail closed");

                var incompatible=await store.ApplyMechaBatteryChargeAsync(
                    character,31,6,2,mecha,incompatibleUid,rules);
                Checks.Require(incompatible.Status==MechaActionStatus.BatteryIncompatible&&
                    (await store.ListInventoryItemsAsync(character)).Any(x=>x.ItemUid==incompatibleUid),
                    "incompatible battery consumed inventory");

                var first=await store.ApplyMechaBatteryChargeAsync(
                    character,31,6,3,mecha,batteryUid,rules);
                var roster=await store.GetMechasAsync(character);
                var inventory=await store.ListInventoryItemsAsync(character);
                Checks.Require(first.Status==MechaActionStatus.Applied&&
                    roster.Single(x=>x.MechaUid==mecha).BatteryCharge==40&&
                    inventory.Single(x=>x.ItemUid==batteryUid).Quantity==1,
                    "first battery charge was not atomic");

                var replay=await store.ApplyMechaBatteryChargeAsync(
                    character,31,6,3,mecha,batteryUid,rules);
                Checks.Require(replay.Status==MechaActionStatus.AlreadyProcessed&&
                    (await store.GetMechasAsync(character))
                        .Single(x=>x.MechaUid==mecha).BatteryCharge==40&&
                    (await store.ListInventoryItemsAsync(character))
                        .Single(x=>x.ItemUid==batteryUid).Quantity==1,
                    "battery retransmit was not idempotent");

                var second=await store.ApplyMechaBatteryChargeAsync(
                    character,31,6,4,mecha,batteryUid,rules);
                Checks.Require(second.Status==MechaActionStatus.Applied&&
                    (await store.GetMechasAsync(character))
                        .Single(x=>x.MechaUid==mecha).BatteryCharge==80&&
                    !(await store.ListInventoryItemsAsync(character)).Any(x=>x.ItemUid==batteryUid),
                    "second charge did not consume final stack unit");

                var mount=await store.ApplyMechaActionAsync(
                    character,31,6,5,MechaActionKind.Mount,mecha);
                Checks.Require(mount.Status==MechaActionStatus.Applied,
                    "battery test mount failed");
                var mountedAllowed=await store.ApplyMechaBatteryChargeAsync(
                    character,31,6,6,mecha,mountedBatteryUid,rules);
                Checks.Require(mountedAllowed.Status==MechaActionStatus.Applied&&
                    (await store.GetMechasAsync(character))
                        .Single(x=>x.MechaUid==mecha).BatteryCharge==100,
                    "verified mounted battery charge did not reach capacity");

                var fullBattery=await Seed(7702,1,4);
                var full=await store.ApplyMechaBatteryChargeAsync(
                    character,31,6,7,mecha,fullBattery,rules);
                Checks.Require(full.Status==MechaActionStatus.BatteryAlreadyFull&&
                    (await store.ListInventoryItemsAsync(character)).Any(x=>x.ItemUid==fullBattery),
                    "full battery attempt consumed inventory");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var persisted=await reopened.GetMechasAsync(character);
                Checks.Require(persisted.Single(x=>x.MechaUid==mecha).BatteryCharge==100,
                    "mecha battery charge did not survive reopen");
            }
        }
        finally { Directory.Delete(root,true); }
    }

    private static async Task Persistence()
    {
        string root=Path.Combine(
            Path.GetTempPath(),"sgtest_mecha_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            long character,mechaA,mechaB;
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "mecha_a","Mecha-Account-Secret-2026!")).AccountId;
                long other=(await store.CreateAccountAsync(
                    "mecha_b","Mecha-Other-Secret-2026!")).AccountId;
                character=(await store.CreateCharacterAsync(
                    account,0,"MechaOwner",new(15,100,100))).Character!.CharacterId;
                long otherCharacter=(await store.CreateCharacterAsync(
                    other,0,"MechaOther",new(15,120,100))).Character!.CharacterId;
                mechaA=await store.ProvisionMechaAsync(
                    character,7001,4,88,new byte[]{1,2,3});
                mechaB=await store.ProvisionMechaAsync(character,7002,7,1234);
                long foreign=await store.ProvisionMechaAsync(otherCharacter,8001);

                var mount=await store.ApplyMechaActionAsync(
                    character,11,4,1,MechaActionKind.Mount,mechaA);
                Checks.Require(
                    mount.Status==MechaActionStatus.Applied&&
                    mount.MountedMechaUid==mechaA,
                    "first mount did not activate owned mecha");

                var replay=await store.ApplyMechaActionAsync(
                    character,11,4,1,MechaActionKind.Mount,mechaA);
                Checks.Require(
                    replay.Status==MechaActionStatus.AlreadyProcessed&&
                    replay.MountedMechaUid==mechaA,
                    "exact mecha retransmit was not idempotent");

                var conflict=await store.ApplyMechaActionAsync(
                    character,11,4,1,MechaActionKind.Mount,mechaB);
                Checks.Require(
                    conflict.Status==MechaActionStatus.SequenceConflict&&
                    conflict.MountedMechaUid==mechaA,
                    "same mecha sequence with different payload was not rejected");

                var second=await store.ApplyMechaActionAsync(
                    character,11,4,2,MechaActionKind.Mount,mechaB);
                Checks.Require(
                    second.Status==MechaActionStatus.Applied&&
                    second.MountedMechaUid==mechaB,
                    "second mount did not atomically replace mounted mecha");

                var foreignResult=await store.ApplyMechaActionAsync(
                    character,11,4,3,MechaActionKind.Mount,foreign);
                Checks.Require(
                    foreignResult.Status==MechaActionStatus.MechaNotOwned&&
                    foreignResult.MountedMechaUid==mechaB,
                    "foreign mecha ownership was not rejected");

                var roster=await store.GetMechasAsync(character);
                Checks.Require(roster.Count==2&&
                    roster.Count(x=>x.IsMounted)==1&&
                    roster.Single(x=>x.IsMounted).MechaUid==mechaB&&
                    roster.Single(x=>x.MechaUid==mechaA).PartsBlob
                        .SequenceEqual(new byte[]{1,2,3}),
                    "mounted uniqueness or server-owned parts were not retained");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var persisted=await reopened.GetMechasAsync(character);
                Checks.Require(persisted.Count==2&&
                    persisted.Count(x=>x.IsMounted)==1&&
                    persisted.Single(x=>x.IsMounted).MechaUid==mechaB&&
                    persisted.Single(x=>x.MechaUid==mechaA).PartsBlob
                        .SequenceEqual(new byte[]{1,2,3}),
                    "mecha roster/mounted state/parts did not survive reopen");

                var dismount=await reopened.ApplyMechaActionAsync(
                    character,12,4,1,MechaActionKind.Dismount,mechaB);
                Checks.Require(
                    dismount.Status==MechaActionStatus.Applied&&
                    dismount.MountedMechaUid==0,
                    "dismount did not clear mounted mecha");
                var after=await reopened.GetMechasAsync(character);
                Checks.Require(after.All(x=>!x.IsMounted),
                    "dismount left a mounted mecha row");
            }
        }
        finally { Directory.Delete(root,true); }
    }

    private static async Task EquipmentLifecycle()
    {
        string root=Path.Combine(
            Path.GetTempPath(),"sgtest_mecha_equipment_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            long character,mecha;
            long weapon1,weapon2,engine,card1,card2,card3,card4,guardian;
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "mecha_equipment","Mecha-Equipment-Secret-2026!")).AccountId;
                character=(await store.CreateCharacterAsync(
                    account,0,"MechaEquip",new(15,100,100))).Character!.CharacterId;
                mecha=await store.ProvisionMechaAsync(character,7001,10,0);

                long generation=await store.AllocateLootWorldGenerationAsync();
                uint pickup=1;
                async Task<long> Seed(int definition,int deathRevision)
                {
                    var drop=await store.CreateTrustedGroundDropAsync(new(
                        15,100,100,9000+deathRevision,
                        SqliteIdentityStore.ComposeLootDeathRevision(generation,(uint)deathRevision),
                        0,definition,1,LootSourceTrust.SyntheticTest,
                        "mecha-equipment-"+definition));
                    var grant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
                        character,
                        SqliteIdentityStore.ComposePickupConnectionEpoch(generation,1),
                        1,pickup++,15,100,100),drop.DropId);
                    Checks.Require(grant.Status==PickupStatus.Granted&&grant.Quantity==1,
                        "mecha equipment seed failed for "+definition);
                    return grant.ItemUid;
                }

                weapon1=await Seed(7101,1);
                weapon2=await Seed(7102,2);
                engine=await Seed(7201,3);
                card1=await Seed(7301,4);
                card2=await Seed(7302,5);
                card3=await Seed(7303,6);
                card4=await Seed(7304,7);
                guardian=await Seed(7601,8);

                IReadOnlyDictionary<int,MechaEquipmentRule> rules=
                    new Dictionary<int,MechaEquipmentRule>
                    {
                        [7101]=new(7101,1,1),[7102]=new(7102,1,1),
                        [7201]=new(7201,2,3),
                        [7301]=new(7301,3,1),[7302]=new(7302,3,1),
                        [7303]=new(7303,3,1),[7304]=new(7304,3,1),
                        [7601]=new(7601,6,4)
                    };

                var first=await store.ApplyMechaEquipmentActionAsync(
                    character,21,5,1,MechaActionKind.Equip,mecha,weapon1,rules);
                Checks.Require(first.Status==MechaActionStatus.Applied&&first.SlotId==1,
                    "mecha weapon did not equip into slot 1");

                var replacement=await store.ApplyMechaEquipmentActionAsync(
                    character,21,5,2,MechaActionKind.Equip,mecha,weapon2,rules);
                var inventory=await store.ListInventoryItemsAsync(character);
                var equipped=await store.ListMechaEquippedItemsAsync(character);
                Checks.Require(
                    replacement.Status==MechaActionStatus.Applied&&replacement.SlotId==1&&
                    replacement.ReplacedItemUid==weapon1&&
                    equipped.Single(x=>x.SlotId==1).ItemUid==weapon2&&
                    inventory.Any(x=>x.ItemUid==weapon1)&&
                    inventory.All(x=>x.ItemUid!=weapon2),
                    "mecha core replacement did not atomically return old UID");

                var coreUnequip=await store.ApplyMechaEquipmentActionAsync(
                    character,21,5,3,MechaActionKind.Unequip,mecha,weapon2,null);
                Checks.Require(
                    coreUnequip.Status==MechaActionStatus.CorePartCannotUnequip&&
                    (await store.ListMechaEquippedItemsAsync(character))
                        .Single(x=>x.SlotId==1).ItemUid==weapon2,
                    "mecha core part was directly unequipped");

                var engineResult=await store.ApplyMechaEquipmentActionAsync(
                    character,21,5,4,MechaActionKind.Equip,mecha,engine,rules);
                Checks.Require(
                    engineResult.Status==MechaActionStatus.Applied&&engineResult.SlotId==2,
                    "mecha engine did not equip into slot 2");

                foreach(var pair in new[]{(card1,5,3),(card2,6,4),(card3,7,5)})
                {
                    var cardResult=await store.ApplyMechaEquipmentActionAsync(
                        character,21,5,(uint)pair.Item2,MechaActionKind.Equip,
                        mecha,pair.Item1,rules);
                    Checks.Require(
                        cardResult.Status==MechaActionStatus.Applied&&
                        cardResult.SlotId==pair.Item3,
                        "mecha card allocation did not fill runtime slots 3..5");
                }
                var full=await store.ApplyMechaEquipmentActionAsync(
                    character,21,5,8,MechaActionKind.Equip,mecha,card4,rules);
                Checks.Require(full.Status==MechaActionStatus.CardSlotsFull,
                    "fourth mecha card bypassed three-card capacity");

                var guardianResult=await store.ApplyMechaEquipmentActionAsync(
                    character,21,5,9,MechaActionKind.Equip,mecha,guardian,rules);
                Checks.Require(
                    guardianResult.Status==MechaActionStatus.Applied&&guardianResult.SlotId==6,
                    "mecha guardian stone did not equip into slot 6");

                var cardUnequip=await store.ApplyMechaEquipmentActionAsync(
                    character,21,5,10,MechaActionKind.Unequip,mecha,card2,null);
                Checks.Require(
                    cardUnequip.Status==MechaActionStatus.Applied&&cardUnequip.SlotId==4&&
                    (await store.ListInventoryItemsAsync(character))
                        .Any(x=>x.ItemUid==card2),
                    "mecha card unequip did not return exact UID to inventory");

                var mount=await store.ApplyMechaActionAsync(
                    character,21,5,11,MechaActionKind.Mount,mecha);
                Checks.Require(mount.Status==MechaActionStatus.Applied,
                    "mecha equipment test mount failed");
                var locked=await store.ApplyMechaEquipmentActionAsync(
                    character,21,5,12,MechaActionKind.Equip,mecha,card4,rules);
                Checks.Require(locked.Status==MechaActionStatus.MountedLocked,
                    "mounted mecha accepted equipment mutation");
                var replay=await store.ApplyMechaEquipmentActionAsync(
                    character,21,5,12,MechaActionKind.Equip,mecha,card4,rules);
                Checks.Require(replay.Status==MechaActionStatus.AlreadyProcessed,
                    "exact mecha equipment retransmit was not idempotent");
                var conflict=await store.ApplyMechaEquipmentActionAsync(
                    character,21,5,12,MechaActionKind.Equip,mecha,weapon1,rules);
                Checks.Require(conflict.Status==MechaActionStatus.SequenceConflict,
                    "same mecha equipment sequence accepted different ItemUid");

                equipped=await store.ListMechaEquippedItemsAsync(character);
                inventory=await store.ListInventoryItemsAsync(character);
                Checks.Require(
                    equipped.Count==5&&
                    equipped.Select(x=>x.ItemUid).Distinct().Count()==5&&
                    inventory.Select(x=>x.ItemUid).Intersect(
                        equipped.Select(x=>x.ItemUid)).Any()==false,
                    "mecha equipment/inventory physical UID locations overlap");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var persisted=await reopened.ListMechaEquippedItemsAsync(character);
                Checks.Require(
                    persisted.Count==5&&
                    persisted.Single(x=>x.SlotId==1).ItemUid==weapon2&&
                    persisted.Single(x=>x.SlotId==2).ItemUid==engine&&
                    persisted.Single(x=>x.SlotId==6).ItemUid==guardian&&
                    persisted.Any(x=>x.ItemUid==card1&&x.SlotId==3)&&
                    persisted.Any(x=>x.ItemUid==card3&&x.SlotId==5),
                    "mecha equipment did not survive database reopen");
            }
        }
        finally { Directory.Delete(root,true); }
    }
}
