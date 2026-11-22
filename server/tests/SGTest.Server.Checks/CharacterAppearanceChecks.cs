using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;
using System.Text.Json;

internal static class CharacterAppearanceChecks
{
    private static readonly CharacterSpawn Spawn = new(15,100,100);
    private static readonly CharacterAppearance Boy = new(0,0,0);
    private static readonly CharacterAppearance Girl = new(1,3,7,2,1);

    public static void Verify()
    {
        Checks.Run("character appearance allowlist is bounded immutable and rejects invalid source data", Rules);
        Checks.Run("character appearance manifest requires bounded explicit verified fields", Manifest);
        Checks.Run("character appearance atomic creation persists exact choice and rejects race divergence",
            () => Persistence().GetAwaiter().GetResult());
        Checks.Run("character appearance and wallet roll back together on failed creation",
            () => Rollback().GetAwaiter().GetResult());
        Checks.Run("v23 appearance migration preserves unknown legacy looks and pre-upgrade backup",
            () => Migration().GetAwaiter().GetResult());
    }

    private static CharacterAppearanceRules Ruleset() => new("synthetic/appearance-creation/v1",[Boy,Girl]);

    private static void Manifest()
    {
        string root=TempRoot(),path=Path.Combine(root,"appearance.json");
        try
        {
            string valid=JsonSerializer.Serialize(new {
                kind="verified_character_appearance_rules",version=1,rules_verified=true,
                source_reference="synthetic/manifest/v1",
                choices=new[]{
                    new{race=0,hair_index=0,hair_color=0,body_index=0,leg_index=0},
                    new{race=1,hair_index=3,hair_color=7,body_index=2,leg_index=1}}
            });
            File.WriteAllText(path,valid);
            var loaded=CharacterAppearanceRules.LoadVerified(path);
            Checks.Require(loaded.Allows(Girl) && loaded.Choices.Count==2 && loaded.SourceReference=="synthetic/manifest/v1",
                "appearance manifest lost choices/source");
            foreach(string invalid in new[]{valid.Replace("\"rules_verified\":true","\"rules_verified\":false"),
                valid.Replace("\"version\":1","\"version\":2"),valid.Replace("\"race\":1","\"race\":5"),
                valid.Replace("\"hair_color\":7","\"hair_color\":8"),valid.Replace("\"hair_index\":3","\"hair_index\":\"3\""),
                valid.Replace("\"body_index\":2","\"body_index\":256"),valid.Replace("\"leg_index\":1","\"leg_index\":-1"),
                valid.Replace("\"race\":1","\"race\":1,\"race\":0"),valid.Replace("\"hair_index\":3,",""),
                valid.Replace("\"rules_verified\":true,",""),"{}","[]","broken json",new string('x',1024*1024+1)})
            {
                File.WriteAllText(path,invalid);
                Reject<InvalidDataException>(()=>CharacterAppearanceRules.LoadVerified(path));
            }
        }
        finally { Directory.Delete(root,true); }
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("appearance operation should reject "+typeof(T).Name);
    }

    private static void Rules()
    {
        var choices = new List<CharacterAppearance> { Boy,Girl };
        var rules = new CharacterAppearanceRules("synthetic/appearance-creation/v1",choices);
        choices.Clear();
        Checks.Require(rules.Choices.SequenceEqual(new[]{Boy,Girl}),"caller mutated appearance rules");
        Reject<NotSupportedException>(() => ((IList<CharacterAppearance>)rules.Choices)[0]=new(4,0,0));
        foreach (var invalid in new[]{new CharacterAppearance(-1,0,0),new(5,0,0),new(0,-1,0),
                     new(0,65536,0),new(0,0,-1),new(0,0,8),new(0,0,0,-1,0),
                     new(0,0,0,256,0),new(0,0,0,0,-1),new(0,0,0,0,256)})
            Reject<ArgumentException>(() => new CharacterAppearanceRules("synthetic",[invalid]));
        Reject<ArgumentException>(() => new CharacterAppearanceRules("",[Boy]));
        Reject<ArgumentException>(() => new CharacterAppearanceRules("bad\nsource",[Boy]));
        Reject<ArgumentException>(() => new CharacterAppearanceRules("synthetic",[]));
        Reject<ArgumentException>(() => new CharacterAppearanceRules("synthetic",[Boy,Boy]));
        Reject<ArgumentException>(() => new CharacterAppearanceRules("synthetic",
            Enumerable.Range(0,4097).Select(hair => new CharacterAppearance(0,hair,0))));
    }

    private static string TempRoot()
    {
        string root=Path.Combine(Path.GetTempPath(),"sgtest-appearance-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static SqliteConnection Open(string db)
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource=db,Pooling=false,ForeignKeys=true }.ToString());
        connection.Open();
        return connection;
    }

    private static void Sql(string db,string sql)
    {
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(string db,string sql)
    {
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static CharacterCombatProfileData Profile(int race) => new(
        CombatProfileTrust.SyntheticTest,"synthetic/appearance-combat/v1",race,0,
        1,1,1,1,1,100,100,10,10,100,1,128,15);

    private static async Task Persistence()
    {
        string root=TempRoot(),db=Path.Combine(root,"identity.db");
        long account;
        try
        {
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                account=(await store.CreateAccountAsync("appearance_user","Appearance-Password-123!")).AccountId;
                var legacy=await store.CreateCharacterAsync(account,0,"UnknownLook",Spawn);
                Checks.Require(legacy.Character!.Appearance is null,"legacy API fabricated appearance");
                var rules=Ruleset();
                var created=await store.CreateCharacterAsync(account,1,"ChosenLook",Spawn,Girl,rules);
                Checks.Require(created.Status==CharacterCreateStatus.Created &&
                    created.Character!.Appearance==new CharacterAppearanceState(Girl,rules.SourceReference),
                    "create receipt lost validated appearance");
                bool rejected=false;
                try { await store.CreateCharacterAsync(account,2,"InvalidLook",Spawn,new(2,3,7),rules); }
                catch(ArgumentException) { rejected=true; }
                Checks.Require(rejected && (await store.ListCharactersAsync(account)).Count==2,
                    "unlisted appearance mutated identity data");
                var race=await Task.WhenAll(
                    store.CreateCharacterAsync(account,2,"SlotRaceBoy",Spawn,Boy,rules),
                    store.CreateCharacterAsync(account,2,"SlotRaceGirl",Spawn,Girl,rules));
                Checks.Require(race.Count(r=>r.Status==CharacterCreateStatus.Created)==1 &&
                    race.Count(r=>r.Status==CharacterCreateStatus.SlotOccupied)==1,"appearance slot race has multiple winners");
                var winner=(await store.ListCharactersAsync(account)).Single(c=>c.Slot==2);
                Checks.Require(winner.Appearance!.Value==(winner.Name=="SlotRaceBoy"?Boy:Girl),
                    "losing appearance overwrote winner");
                Checks.Require((await store.CreateCharacterAsync(account,3,"ChosenLook",Spawn,Boy,rules)).Status==
                    CharacterCreateStatus.NameTaken,"duplicate name did not reject");
                await store.SetCharacterCombatProfileAsync(created.Character!.CharacterId,Profile(1));
                rejected=false;
                try { await store.SetCharacterCombatProfileAsync(created.Character.CharacterId,Profile(0)); }
                catch(InvalidOperationException) { rejected=true; }
                Checks.Require(rejected && (await store.GetCharacterCombatProfileAsync(created.Character.CharacterId))!.Data.Race==1,
                    "combat provisioning diverged from appearance race");
                Checks.Require(Scalar(db,"SELECT COUNT(*) FROM characters;")==3 &&
                    Scalar(db,"SELECT COUNT(*) FROM character_wallets;")==3 &&
                    Scalar(db,"SELECT COUNT(*) FROM character_appearances;")==2,
                    "conflict left partial or orphaned character data");
            }
            using var reopened=new SqliteIdentityStore(db);
            await reopened.InitializeAsync();
            var auth=await reopened.AuthenticateAsync("appearance_user","Appearance-Password-123!");
            var list=auth.Account!.Characters;
            Checks.Require(list[0].Appearance is null && list[1].Appearance==
                new CharacterAppearanceState(Girl,Ruleset().SourceReference),"appearance did not survive reopen and login");
        }
        finally { Directory.Delete(root,true); }
    }

    private static async Task Rollback()
    {
        string root=TempRoot(),db=Path.Combine(root,"identity.db");
        try
        {
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync("rollback_user","Appearance-Password-123!")).AccountId;
            Sql(db,@"CREATE TRIGGER fail_wallet BEFORE INSERT ON character_wallets
BEGIN SELECT RAISE(ABORT,'synthetic wallet failure'); END;");
            bool rejected=false;
            try { await store.CreateCharacterAsync(account,0,"AtomicLook",Spawn,Girl,Ruleset()); }
            catch(SqliteException) { rejected=true; }
            Checks.Require(rejected && Scalar(db,"SELECT COUNT(*) FROM characters;")==0 &&
                Scalar(db,"SELECT COUNT(*) FROM character_appearances;")==0 &&
                Scalar(db,"SELECT COUNT(*) FROM character_wallets;")==0,"failed wallet left a character or appearance");
            Sql(db,"DROP TRIGGER fail_wallet;");
            Checks.Require((await store.CreateCharacterAsync(account,0,"AtomicLook",Spawn,Girl,Ruleset())).Status==
                CharacterCreateStatus.Created,"rolled back slot/name remained occupied");
        }
        finally { Directory.Delete(root,true); }
    }

    private static async Task Migration()
    {
        string root=TempRoot(),db=Path.Combine(root,"identity.db");
        long account;
        try
        {
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                account=(await store.CreateAccountAsync("migration_look","Appearance-Password-123!")).AccountId;
                var character=await store.CreateCharacterAsync(account,0,"LegacyLook",Spawn);
                await store.SetCharacterCombatProfileAsync(character.Character!.CharacterId,Profile(2));
            }
            Sql(db,"DROP TABLE character_appearances; UPDATE schema_version SET version=23;");
            using(var migrated=new SqliteIdentityStore(db))
            {
                await migrated.InitializeAsync();
                var legacy=(await migrated.ListCharactersAsync(account)).Single();
                Checks.Require(legacy.Appearance is null && legacy.MapId==15 && legacy.X==100 &&
                    (await migrated.GetCharacterCombatProfileAsync(legacy.CharacterId))!.Data.Race==2,
                    "migration fabricated look or changed legacy character/profile");
                Checks.Require((await migrated.CreateCharacterAsync(account,1,"MigratedNew",Spawn,Girl,Ruleset())).Status==
                    CharacterCreateStatus.Created,"migrated database cannot persist appearance");
            }
            string backup=Directory.GetFiles(root,"identity.db.pre-v23-to-v26.*.bak").Single();
            Checks.Require(Scalar(backup,"SELECT version FROM schema_version;")==23 &&
                Scalar(backup,"SELECT COUNT(*) FROM sqlite_master WHERE name='character_appearances';")==0 &&
                Scalar(backup,"SELECT COUNT(*) FROM characters;")==1,"backup did not preserve pre-upgrade schema/data");
            Checks.Require(Scalar(db,"SELECT version FROM schema_version;")==26,"appearance migration version wrong");
        }
        finally { Directory.Delete(root,true); }
    }
}
