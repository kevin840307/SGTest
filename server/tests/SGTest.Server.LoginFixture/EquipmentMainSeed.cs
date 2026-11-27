using SGTest.Server.Identity;

internal static class EquipmentMainSeed
{
    private const string CharacterProof="2468";
    private const int DefinitionId=18501;

    internal static async Task<int> RunAsync(string database,string appearanceRulesPath)
    {
        try
        {
            string db=Path.GetFullPath(database);
            if(File.Exists(db))
                throw new InvalidOperationException("seed database must be new");
            Directory.CreateDirectory(Path.GetDirectoryName(db)!);
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync().ConfigureAwait(false);
            var appearanceRules=CharacterAppearanceRules.LoadVerified(
                Path.GetFullPath(appearanceRulesPath));
            var settings=new CharacterCreationSettings(
                new CharacterCreationAttributes(2,2,2,2),CharacterProof,
                "Old source-backed level-one creation + verified appearance rules");

            async Task<long> Create(string username,string name,double x)
            {
                long account=(await store.CreateAccountAsync(username,Program.Password)
                    .ConfigureAwait(false)).AccountId;
                var created=await store.CreateCharacterWithSettingsAsync(
                    account,0,name,new CharacterSpawn(15,x,800),settings,
                    new CharacterAppearance(0,0,0,0,0,0),appearanceRules,
                    initialProfileRules:CharacterInitialProfileRules.OldParadiseLevelOne)
                    .ConfigureAwait(false);
                return created.Character?.CharacterId
                    ??throw new InvalidOperationException("character create failed");
            }

            long actor=await Create("equipment_actor","EqActor",840).ConfigureAwait(false);
            long observer=await Create("equipment_observer","EqWatch",880).ConfigureAwait(false);
            long generation=await store.AllocateLootWorldGenerationAsync().ConfigureAwait(false);
            var drop=await store.CreateTrustedGroundDropAsync(new TrustedDropSpawn(
                15,840,800,1900018501,
                SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                0,DefinitionId,1,LootSourceTrust.Verified,
                "HK item.itd + Recovered2 equip/18501.tres verified equipment gate"))
                .ConfigureAwait(false);
            var grant=await store.ClaimGroundDropAsync(
                new TrustedPickupContext(
                    actor,SqliteIdentityStore.ComposePickupConnectionEpoch(generation,1),
                    1,1,15,840,800),drop.DropId).ConfigureAwait(false);
            if(grant.Status!=PickupStatus.Granted||grant.ItemUid<=0||grant.Quantity!=1)
                throw new InvalidOperationException("verified equipment seed grant failed");

            Console.WriteLine(
                $"EQUIPMENT_MAIN_SEED_PASS actor={actor} observer={observer} "+
                $"item_uid={grant.ItemUid} definition={DefinitionId} "+
                "baseline_defense=3 expected_equipped_defense=9");
            return 0;
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("EQUIPMENT_MAIN_SEED_FAIL "+error);
            return 1;
        }
    }
}
