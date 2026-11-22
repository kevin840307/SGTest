using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;
using System.Text.Json;

internal static class CharacterCreationSettingsChecks
{
    private const string Password = "2468";
    private static readonly CharacterSpawn Spawn = new(15,100,100);
    private static readonly CharacterCreationAttributes Balanced = new(2,2,2,2);
    private static CharacterCreationSettings Settings(CharacterCreationAttributes? attrs=null) =>
        new(attrs??Balanced,Password,"synthetic/creation-settings/v1");

    public static void Verify()
    {
        Checks.Run("creation rules enforce full eight-point allocation Unicode name width and redacted four-character secret",Rules);
        Checks.Run("creation settings persist atomically with look wallet and reject initial combat divergence",
            ()=>Persistence().GetAwaiter().GetResult());
        Checks.Run("creation settings and wallet roll back together on failed creation",
            ()=>Rollback().GetAwaiter().GetResult());
        Checks.Run("creation slot races preserve winner settings and distinct salted credentials",
            ()=>Races().GetAwaiter().GetResult());
        Checks.Run("character password lockout is account-owned durable and atomic across stores",
            ()=>Credentials().GetAwaiter().GetResult());
        Checks.Run("v24 migration backs up and preserves unknown legacy creation settings",
            ()=>Migration().GetAwaiter().GetResult());
    }

    private static void Require(bool ok,string name)=>Checks.Require(ok,name);
    private static void Reject<T>(Action action) where T:Exception
    {
        try { action(); } catch(T) { return; }
        throw new Exception("Expected "+typeof(T).Name);
    }
    private static async Task RejectAsync<T>(Func<Task> action) where T:Exception
    {
        try { await action(); } catch(T) { return; }
        throw new Exception("Expected "+typeof(T).Name);
    }
    private static string Root()
    {
        string path=Path.Combine(Path.GetTempPath(),"sgtest_creation_settings_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    private static void Sql(string path,string sql)
    {
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());
        c.Open();using var q=c.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();
    }
    private static long Count(string path,string sql)
    {
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());
        c.Open();using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(q.ExecuteScalar());
    }

    private static void Rules()
    {
        int count=0;
        for(int a=1;a<=5;a++)for(int b=1;b<=5;b++)for(int c=1;c<=5;c++)for(int d=1;d<=5;d++)
        {
            if(a+b+c+d==8){ CharacterCreationRules.Validate(new(a,b,c,d));count++; }
            else Reject<ArgumentException>(()=>CharacterCreationRules.Validate(new(a,b,c,d)));
        }
        Require(count==35,"valid starting distributions missing");
        foreach(var value in new[]{new CharacterCreationAttributes(0,3,3,2),new(-1,3,3,3),new(int.MaxValue,int.MaxValue,1,1)})
            Reject<ArgumentException>(()=>CharacterCreationRules.Validate(value));
        CharacterCreationRules.ValidateName("123456789012");
        CharacterCreationRules.ValidateName("甲乙丙丁戊己");
        Reject<ArgumentException>(()=>CharacterCreationRules.ValidateName("甲乙丙丁戊己庚"));
        Reject<ArgumentException>(()=>CharacterCreationRules.ValidateName("1234567890123"));
        CharacterCreationRules.ValidatePassword("甲乙丙丁");
        foreach(string bad in new[]{"","123","12345"," 123","123 ","12\n3","\ud800123"})
            Reject<ArgumentException>(()=>CharacterCreationRules.ValidatePassword(bad));
        Require(!Settings().ToString().Contains(Password),"creation settings formatted plaintext credential");
        Reject<ArgumentException>(()=>new CharacterCreationSettings(Balanced,Password,""));
        Reject<ArgumentException>(()=>PasswordHasher.ValidateNewPassword(Password));
    }

    private static async Task Persistence()
    {
        string root=Root(),db=Path.Combine(root,"identity.db");long account,id;
        try
        {
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();account=(await store.CreateAccountAsync("creation_user","Creation-Account-123!")).AccountId;
                var appearance=new CharacterAppearance(1,3,7);
                var rules=new CharacterAppearanceRules("synthetic/creation-look",[appearance]);
                var created=await store.CreateCharacterWithSettingsAsync(account,0,"CreationHero",Spawn,Settings(),appearance,rules);
                Require(created.Status==CharacterCreateStatus.Created,"settings creation failed");
                var row=created.Character!;id=row.CharacterId;
                Require(row.Creation!.Attributes==Balanced && row.Appearance!.Value==appearance && row.RequiresCharacterPassword,"creation metadata lost choices");
                Require(!JsonSerializer.Serialize(row).Contains(Password),"public metadata exposed character password");
                var mismatched=new CharacterCombatProfileData(CombatProfileTrust.SyntheticTest,"synthetic/creation-combat",1,0,
                    1,1,1,1,1,100,100,10,10,100,1,128,15);
                await RejectAsync<InvalidOperationException>(()=>store.SetCharacterCombatProfileAsync(id,mismatched));
                await store.SetCharacterCombatProfileAsync(id,mismatched with{Strength=2,Dexterity=2,Constitution=2,Wisdom=2});
                await RejectAsync<ArgumentException>(()=>store.CreateCharacterWithSettingsAsync(account,1,"TooLongForName",Spawn,Settings()));
                Require(Count(db,"SELECT COUNT(*) FROM characters;")==1 && Count(db,"SELECT COUNT(*) FROM character_wallets;")==1,"invalid settings request created partial state");
            }
            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var auth=await reopened.AuthenticateAsync("creation_user","Creation-Account-123!");
                var row=auth.Account!.Characters.Single();
                Require(row.CharacterId==id && row.Creation==new CharacterCreationState(Balanced,"synthetic/creation-settings/v1") && row.RequiresCharacterPassword,"settings did not survive account reauthentication");
                Require(await reopened.VerifyCharacterPasswordAsync(account,id,Password)==CharacterPasswordStatus.Success,"stored credential failed after restart");
            }
        }
        finally { Directory.Delete(root,true); }
    }

    private static async Task Rollback()
    {
        string root=Root(),db=Path.Combine(root,"identity.db");
        try
        {
            using var store=new SqliteIdentityStore(db);await store.InitializeAsync();
            long account=(await store.CreateAccountAsync("creation_rollback","Creation-Account-123!")).AccountId;
            Sql(db,"CREATE TRIGGER fail_settings_wallet BEFORE INSERT ON character_wallets BEGIN SELECT RAISE(ABORT,'fixture failure'); END;");
            var look=new CharacterAppearance(0,1,0);var rules=new CharacterAppearanceRules("synthetic",[look]);
            await RejectAsync<SqliteException>(()=>store.CreateCharacterWithSettingsAsync(account,0,"AtomicHero",Spawn,Settings(),look,rules));
            foreach(string table in new[]{"characters","character_creation_settings","character_appearances","character_wallets"})
                Require(Count(db,"SELECT COUNT(*) FROM "+table+";")==0,"wallet failure left "+table);
            Sql(db,"DROP TRIGGER fail_settings_wallet;");
            Require((await store.CreateCharacterWithSettingsAsync(account,0,"AtomicHero",Spawn,Settings(),look,rules)).Status==CharacterCreateStatus.Created,"rollback retained slot/name");
        }
        finally { Directory.Delete(root,true); }
    }

    private static async Task Races()
    {
        string root=Root(),db=Path.Combine(root,"identity.db");
        try
        {
            using var hasher=new PasswordHasher();
            using var a=new SqliteIdentityStore(db,hasher);await a.InitializeAsync();
            using var b=new SqliteIdentityStore(db,hasher);
            long account=(await a.CreateAccountAsync("creation_race","Creation-Account-123!")).AccountId;
            var results=await Task.WhenAll(
                a.CreateCharacterWithSettingsAsync(account,0,"RaceOne",Spawn,Settings()),
                b.CreateCharacterWithSettingsAsync(account,0,"RaceTwo",Spawn,Settings(new(5,1,1,1))));
            Require(results.Count(x=>x.Status==CharacterCreateStatus.Created)==1 && results.Count(x=>x.Status==CharacterCreateStatus.SlotOccupied)==1,"slot race has multiple winners");
            var winner=(await a.ListCharactersAsync(account)).Single();
            Require(winner.Creation!.Attributes==(winner.Name=="RaceOne"?Balanced:new(5,1,1,1)),"loser overwrote winner attributes");
            Require((await a.CreateCharacterWithSettingsAsync(account,1,"OtherHero",Spawn,Settings())).Status==CharacterCreateStatus.Created,"second protected character failed");
            Require(Count(db,"SELECT COUNT(DISTINCT password_hash) FROM character_creation_settings;")==2,"same password reused salt/hash");
            Require((await a.CreateCharacterWithSettingsAsync(account,2,winner.Name,Spawn,Settings())).Status==CharacterCreateStatus.NameTaken,"duplicate name overwrote settings");
            Require(Count(db,"SELECT COUNT(*) FROM character_creation_settings;")==2,"conflict persisted extra settings");
        }
        finally { Directory.Delete(root,true); }
    }

    private sealed class Clock : TimeProvider
    {
        private long _now=1_790_000_000_000;
        public override DateTimeOffset GetUtcNow()=>DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref _now));
        public void Advance(int seconds)=>Interlocked.Add(ref _now,seconds*1000L);
    }
    private static async Task Credentials()
    {
        string root=Root(),db=Path.Combine(root,"identity.db");var clock=new Clock();
        try
        {
            using var hasher=new PasswordHasher();
            using var a=new SqliteIdentityStore(db,hasher,timeProvider:clock);await a.InitializeAsync();
            using var b=new SqliteIdentityStore(db,hasher,timeProvider:clock);
            long account=(await a.CreateAccountAsync("credential_owner","Creation-Account-123!")).AccountId;
            long other=(await a.CreateAccountAsync("credential_other","Creation-Account-123!")).AccountId;
            long id=(await a.CreateCharacterWithSettingsAsync(account,0,"SecretHero",Spawn,Settings())).Character!.CharacterId;
            long legacy=(await a.CreateCharacterAsync(account,1,"LegacyHero",Spawn)).Character!.CharacterId;
            Require(await a.VerifyCharacterPasswordAsync(account,legacy,null)==CharacterPasswordStatus.Success,"legacy character invented a credential");
            Require(await a.VerifyCharacterPasswordAsync(account,legacy,Password)==CharacterPasswordStatus.InvalidPassword,"legacy character silently ignored extraneous proof");
            Require(await a.VerifyCharacterPasswordAsync(other,id,Password)==CharacterPasswordStatus.NotOwned,"foreign account verified a character credential");
            Require(await a.VerifyCharacterPasswordAsync(account,id,null)==CharacterPasswordStatus.InvalidPassword,"missing protected credential accepted");
            Require(await a.VerifyCharacterPasswordAsync(account,id,Password)==CharacterPasswordStatus.Success,"correct credential rejected");
            var results=await Task.WhenAll(Enumerable.Range(0,8).Select(i=>(i%2==0?a:b).VerifyCharacterPasswordAsync(account,id,"9999")));
            Require(results.Count(x=>x==CharacterPasswordStatus.InvalidPassword)==4 && results.Count(x=>x==CharacterPasswordStatus.Locked)==4,"concurrent failures bypassed durable attempt limit");
            Require(Count(db,"SELECT failed_password_attempts FROM character_creation_settings;")==5,"attempt count escaped bound");
            using(var reopened=new SqliteIdentityStore(db,hasher,timeProvider:clock))
                Require(await reopened.VerifyCharacterPasswordAsync(account,id,Password)==CharacterPasswordStatus.Locked,"new Store bypassed persisted lockout");
            clock.Advance(CharacterCreationRules.PasswordLockoutSeconds+1);
            Require(await b.VerifyCharacterPasswordAsync(account,id,"9999")==CharacterPasswordStatus.InvalidPassword,"expired lockout retained full failure count");
            Require(Count(db,"SELECT failed_password_attempts FROM character_creation_settings;")==1,"first post-expiry failure did not restart counter");
            Require(await a.VerifyCharacterPasswordAsync(account,id,Password)==CharacterPasswordStatus.Success && Count(db,"SELECT failed_password_attempts FROM character_creation_settings;")==0,"successful proof failed to reset attempts");
            await a.SetDisabledAsync(account,true);
            Require(await b.VerifyCharacterPasswordAsync(account,id,Password)==CharacterPasswordStatus.NotOwned,"disabled account verified a protected character");
        }
        finally { Directory.Delete(root,true); }
    }

    private static async Task Migration()
    {
        string root=Root(),db=Path.Combine(root,"identity.db");long account;
        try
        {
            using(var a=new SqliteIdentityStore(db))
            {
                await a.InitializeAsync();account=(await a.CreateAccountAsync("migration_settings","Creation-Account-123!")).AccountId;
                await a.CreateCharacterAsync(account,0,"OldHero",Spawn);
            }
            Sql(db,"DROP TABLE character_creation_settings; UPDATE schema_version SET version=24;");
            using(var b=new SqliteIdentityStore(db))
            {
                await b.InitializeAsync();var row=(await b.ListCharactersAsync(account)).Single();
                Require(row.Creation is null && !row.RequiresCharacterPassword && row.Appearance is null,"migration fabricated creation choices or password");
                Require((await b.CreateCharacterWithSettingsAsync(account,1,"NewHero",Spawn,Settings())).Status==CharacterCreateStatus.Created,"upgraded Store cannot create protected characters");
            }
            string backup=Directory.GetFiles(root,"identity.db.pre-v24-to-v28.*.bak").Single();
            Require(Count(backup,"SELECT version FROM schema_version;")==24 && Count(backup,"SELECT COUNT(*) FROM sqlite_master WHERE name='character_creation_settings';")==0,"migration backup lost prior schema");
            Require(Count(db,"SELECT version FROM schema_version;")==28,"creation migration version wrong");
        }
        finally { Directory.Delete(root,true); }
    }
}
