using SGTest.Server.Identity;

internal static class MechaMainSeed
{
    private const string CharacterProof="2468";
    internal const int MechaDefinitionId=50018;
    internal const int MechaLevel=65;
    internal const int WeaponDefinitionId=50703;
    internal const int EngineDefinitionId=50702;
    internal const int BatteryDefinitionId=50103;

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

            long actor=await Create("mecha_actor","MechaActor",840).ConfigureAwait(false);
            long observer=await Create("mecha_observer","MechaWatch",880).ConfigureAwait(false);
            long mechaUid=await store.ProvisionMechaAsync(
                actor,MechaDefinitionId,MechaLevel,0).ConfigureAwait(false);

            long generation=await store.AllocateLootWorldGenerationAsync().ConfigureAwait(false);
            async Task<long> Grant(int definition,long sourceActor,int revision)
            {
                var drop=await store.CreateTrustedGroundDropAsync(new TrustedDropSpawn(
                    15,840,800,sourceActor,
                    SqliteIdentityStore.ComposeLootDeathRevision(generation,revision),
                    0,definition,1,LootSourceTrust.Verified,
                    "Recovered2 converted Items source-backed mecha formal gate"))
                    .ConfigureAwait(false);
                var grant=await store.ClaimGroundDropAsync(
                    new TrustedPickupContext(
                        actor,SqliteIdentityStore.ComposePickupConnectionEpoch(generation,revision),
                        1,(uint)revision,15,840,800),drop.DropId).ConfigureAwait(false);
                if(grant.Status!=PickupStatus.Granted||grant.ItemUid<=0||grant.Quantity!=1)
                    throw new InvalidOperationException(
                        $"verified mecha seed grant failed definition={definition} status={grant.Status}");
                return grant.ItemUid;
            }

            long weaponUid=await Grant(WeaponDefinitionId,1900050703,1).ConfigureAwait(false);
            long engineUid=await Grant(EngineDefinitionId,1900050702,2).ConfigureAwait(false);
            long batteryUid=await Grant(BatteryDefinitionId,1900050103,3).ConfigureAwait(false);

            Console.WriteLine(
                $"MECHA_MAIN_SEED_PASS actor={actor} observer={observer} mecha_uid={mechaUid} "+
                $"definition={MechaDefinitionId} level={MechaLevel} weapon_uid={weaponUid} "+
                $"engine_uid={engineUid} battery_uid={batteryUid}");
            return 0;
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("MECHA_MAIN_SEED_FAIL "+error);
            return 1;
        }
    }
}
