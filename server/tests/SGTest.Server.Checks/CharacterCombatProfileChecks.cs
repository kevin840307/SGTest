using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

internal static class CharacterCombatProfileChecks
{
    public static void Verify(string root)
    {
        Checks.Run("identity schema v1 migrates to v2 combat profile table without changing characters",
            () => Migration(root).GetAwaiter().GetResult());
        Checks.Run("normal character has no combat stats until trusted profile is provisioned",
            () => TrustBoundary(root).GetAwaiter().GetResult());
        Checks.Run("trusted combat HP settlement is atomic persistent and absent-profile fail-closed",
            () => HpSettlement(root).GetAwaiter().GetResult());
        Checks.Run("schema v5 combat profiles migrate MP to zero without inventing a pool",
            () => V5MpMigration(root).GetAwaiter().GetResult());
        Checks.Run("trusted MP settlement is atomic persistent and fail-closed",
            () => MpSettlement(root).GetAwaiter().GetResult());
        Checks.Run("durable skill MP settlement is idempotent and sequence-bound",
            () => DurableSkillMpSettlement(root).GetAwaiter().GetResult());
    }

    private static async Task Migration(string root)
    {
        string dir=Path.Combine(root,"combat-migrate-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        string cs=new SqliteConnectionStringBuilder
        {
            DataSource=db,Pooling=false,ForeignKeys=true
        }.ToString();
        using(var connection=new SqliteConnection(cs))
        {
            connection.Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"
CREATE TABLE schema_version(version INTEGER NOT NULL);
INSERT INTO schema_version(version) VALUES(1);
CREATE TABLE accounts(
 account_id INTEGER PRIMARY KEY AUTOINCREMENT,
 username_key TEXT NOT NULL UNIQUE,
 username_display TEXT NOT NULL,
 password_hash TEXT NOT NULL,
 disabled INTEGER NOT NULL DEFAULT 0,
 created_unix_ms INTEGER NOT NULL);
CREATE TABLE characters(
 character_id INTEGER PRIMARY KEY AUTOINCREMENT,
 account_id INTEGER NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
 slot INTEGER NOT NULL,name TEXT NOT NULL,name_key TEXT NOT NULL UNIQUE,
 map_id INTEGER NOT NULL,x REAL NOT NULL,y REAL NOT NULL,
 created_unix_ms INTEGER NOT NULL,updated_unix_ms INTEGER NOT NULL,
 UNIQUE(account_id,slot));
INSERT INTO accounts(account_id,username_key,username_display,password_hash,disabled,created_unix_ms)
VALUES(1,'legacy','legacy','not-used',0,1);
INSERT INTO characters(character_id,account_id,slot,name,name_key,map_id,x,y,created_unix_ms,updated_unix_ms)
VALUES(7,1,0,'Legacy','legacy',15,24,24,1,1);";
            command.ExecuteNonQuery();
        }

        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            var characters=await store.ListCharactersAsync(1);
            Checks.Require(characters.Count==1 &&
                    characters[0].CharacterId==7 &&
                    await store.GetCharacterCombatProfileAsync(7) is null,
                "v1 character changed or received invented combat profile");
        }

        using(var connection=new SqliteConnection(cs))
        {
            connection.Open();
            using var command=connection.CreateCommand();
            command.CommandText="SELECT version FROM schema_version;";
            Checks.Require(Convert.ToInt32(command.ExecuteScalar())==
                           SqliteIdentityStore.SchemaVersion,
                "identity schema did not migrate to v2");
            command.CommandText=@"
SELECT COUNT(*) FROM sqlite_master
 WHERE type='table' AND name='character_combat_profiles';";
            Checks.Require(Convert.ToInt32(command.ExecuteScalar())==1,
                "combat profile table missing after migration");
        }
    }

    private static async Task TrustBoundary(string root)
    {
        string db=Path.Combine(root,"combat-trust-"+Guid.NewGuid().ToString("N")+".db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        long account=(await store.CreateAccountAsync(
            "combat_profile","Combat-Profile-Password-123!")).AccountId;
        long character=(await store.CreateCharacterAsync(account,0,"CombatHero",
            new CharacterSpawn(15,24,24))).Character!.CharacterId;
        Checks.Require(await store.GetCharacterCombatProfileAsync(character) is null,
            "ordinary character creation invented combat stats");

        var synthetic=Profile(
            CombatProfileTrust.SyntheticTest,"ci:test-only",hp:120,attack:30);
        await store.SetCharacterCombatProfileAsync(character,synthetic);
        var saved=await store.GetCharacterCombatProfileAsync(character);
        Checks.Require(saved is not null &&
                saved.Data==synthetic &&
                saved.Data.Trust==CombatProfileTrust.SyntheticTest,
            "trusted synthetic profile did not roundtrip");

        bool rejected=false;
        try
        {
            await store.SetCharacterCombatProfileAsync(character,
                synthetic with { SourceReference="" });
        }
        catch(ArgumentOutOfRangeException) { rejected=true; }
        Checks.Require(rejected,"profile without source provenance accepted");

        rejected=false;
        try
        {
            await store.SetCharacterCombatProfileAsync(999999,synthetic);
        }
        catch(KeyNotFoundException) { rejected=true; }
        Checks.Require(rejected,"combat profile accepted for unknown character");
    }

    private static async Task HpSettlement(string root)
    {
        string db=Path.Combine(root,"combat-hp-"+Guid.NewGuid().ToString("N")+".db");
        long character;
        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "combat_hp","Combat-Profile-Password-456!")).AccountId;
            character=(await store.CreateCharacterAsync(account,0,"HpHero",
                new CharacterSpawn(13,24,24))).Character!.CharacterId;
            Checks.Require(await store.ApplyCharacterDamageAsync(character,1) is null,
                "missing combat profile invented HP during damage");
            await store.SetCharacterCombatProfileAsync(character,
                Profile(CombatProfileTrust.Verified,
                    "recovered:player-profile-fixture",hp:100,attack:20));
            var first=await store.ApplyCharacterDamageAsync(character,35);
            Checks.Require(first?.Data.CurrentHp==65 &&
                           first.Data.MaxHp==100 &&
                           first.Data.Trust==CombatProfileTrust.Verified,
                "atomic trusted damage wrong");
            var second=await store.ApplyCharacterDamageAsync(character,1000);
            Checks.Require(second?.Data.CurrentHp==0,
                "trusted damage did not clamp at zero");
        }
        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var persisted=await reopened.GetCharacterCombatProfileAsync(character);
            Checks.Require(persisted?.Data.CurrentHp==0 &&
                           persisted.Data.SourceReference==
                           "recovered:player-profile-fixture",
                "combat HP/provenance did not persist");
        }
    }

    private static async Task V5MpMigration(string root)
    {
        string db=Path.Combine(root,"combat-v5-mp-"+Guid.NewGuid().ToString("N")+".db");
        string cs=new SqliteConnectionStringBuilder
        {
            DataSource=db,Pooling=false,ForeignKeys=true
        }.ToString();
        using(var connection=new SqliteConnection(cs))
        {
            connection.Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"
CREATE TABLE schema_version(version INTEGER NOT NULL);
INSERT INTO schema_version(version) VALUES(5);
CREATE TABLE accounts(
 account_id INTEGER PRIMARY KEY AUTOINCREMENT,
 username_key TEXT NOT NULL UNIQUE,username_display TEXT NOT NULL,
 password_hash TEXT NOT NULL,disabled INTEGER NOT NULL DEFAULT 0,
 created_unix_ms INTEGER NOT NULL);
CREATE TABLE characters(
 character_id INTEGER PRIMARY KEY AUTOINCREMENT,
 account_id INTEGER NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
 slot INTEGER NOT NULL,name TEXT NOT NULL,name_key TEXT NOT NULL UNIQUE,
 map_id INTEGER NOT NULL,x REAL NOT NULL,y REAL NOT NULL,
 created_unix_ms INTEGER NOT NULL,updated_unix_ms INTEGER NOT NULL,
 UNIQUE(account_id,slot));
CREATE TABLE character_combat_profiles(
 character_id INTEGER PRIMARY KEY REFERENCES characters(character_id) ON DELETE CASCADE,
 trust INTEGER NOT NULL,source_ref TEXT NOT NULL,race INTEGER NOT NULL,
 career INTEGER NOT NULL,strength INTEGER NOT NULL,dexterity INTEGER NOT NULL,
 constitution INTEGER NOT NULL,wisdom INTEGER NOT NULL,level INTEGER NOT NULL,
 current_hp INTEGER NOT NULL,max_hp INTEGER NOT NULL,attack INTEGER NOT NULL,
 defense INTEGER NOT NULL,hit INTEGER NOT NULL,dodge INTEGER NOT NULL,
 attack_range REAL NOT NULL,attack_cooldown_ticks INTEGER NOT NULL,
 updated_unix_ms INTEGER NOT NULL);
INSERT INTO accounts VALUES(1,'v5','v5','unused',0,1);
INSERT INTO characters VALUES(7,1,0,'V5','v5hero',15,24,24,1,1);
INSERT INTO character_combat_profiles
VALUES(7,2,'ci:v5',0,0,5,5,5,5,1,80,100,20,4,10,3,48,15,1);";
            command.ExecuteNonQuery();
        }
        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            var migrated=await store.GetCharacterCombatProfileAsync(7);
            Checks.Require(migrated?.Data.CurrentHp==80 &&
                           migrated.Data.CurrentMp==0 &&
                           migrated.Data.MaxMp==0 &&
                           migrated.Data.Attack==20,
                "v5 profile migration invented MP or changed combat data");
        }
    }

    private static async Task MpSettlement(string root)
    {
        string db=Path.Combine(root,"combat-mp-"+Guid.NewGuid().ToString("N")+".db");
        long character;
        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "combat_mp","Combat-MP-Password-123!")).AccountId;
            character=(await store.CreateCharacterAsync(account,0,"MpHero",
                new CharacterSpawn(13,24,24))).Character!.CharacterId;
            var missing=await store.ConsumeCharacterMpAsync(character,1);
            Checks.Require(missing.Status==CharacterMpConsumeStatus.MissingProfile,
                "missing combat profile invented MP");

            var mpProfile=Profile(
                CombatProfileTrust.Verified,"client-skilldata:mp-fixture",
                hp:100,attack:20) with { CurrentMp=3,MaxMp=5 };
            await store.SetCharacterCombatProfileAsync(character,mpProfile);
            var first=await store.ConsumeCharacterMpAsync(character,1);
            Checks.Require(first.Status==CharacterMpConsumeStatus.Consumed &&
                           first.MpConsumed==1 &&
                           first.Profile?.Data.CurrentMp==2 &&
                           first.Profile.Data.MaxMp==5,
                "atomic MP consume wrong");
            var insufficient=await store.ConsumeCharacterMpAsync(character,3);
            Checks.Require(insufficient.Status==CharacterMpConsumeStatus.InsufficientMp &&
                           insufficient.Profile?.Data.CurrentMp==2,
                "insufficient MP mutated profile");
            await store.ApplyCharacterDamageAsync(character,1000);
            var dead=await store.ConsumeCharacterMpAsync(character,1);
            Checks.Require(dead.Status==CharacterMpConsumeStatus.Dead &&
                           dead.Profile?.Data.CurrentMp==2,
                "dead character spent MP");
        }
        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var saved=await reopened.GetCharacterCombatProfileAsync(character);
            Checks.Require(saved?.Data.CurrentMp==2 && saved.Data.MaxMp==5,
                "MP settlement did not persist");
        }
    }

    private static async Task DurableSkillMpSettlement(string root)
    {
        string db=Path.Combine(root,"combat-skill-mp-"+Guid.NewGuid().ToString("N")+".db");
        long character;
        const long generation=9001;
        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "skill_mp","Skill-MP-Password-123!")).AccountId;
            character=(await store.CreateCharacterAsync(account,0,"SkillMpHero",
                new CharacterSpawn(13,24,24))).Character!.CharacterId;

            var missing=await store.ConsumeSkillMpAsync(
                new(character,generation,1,1,23103),1);
            Checks.Require(missing.Status==SkillMpConsumeStatus.MissingProfile,
                "missing profile invented durable skill MP");

            var profile=Profile(
                CombatProfileTrust.Verified,"current-client:Skills/23103.tres",
                hp:100,attack:20) with {CurrentMp=3,MaxMp=5};
            await store.SetCharacterCombatProfileAsync(character,profile);

            var first=await store.ConsumeSkillMpAsync(
                new(character,generation,1,1,23103),1);
            Checks.Require(first.Status==SkillMpConsumeStatus.Consumed &&
                           first.SkillId==23103 && first.MpCost==1 &&
                           first.PreviousMp==3 && first.CurrentMp==2 &&
                           first.MaxMp==5,
                "durable skill MP first consume wrong");

            var replay=await store.ConsumeSkillMpAsync(
                new(character,generation,1,1,23103),1);
            Checks.Require(replay.Status==SkillMpConsumeStatus.AlreadyConsumed &&
                           replay.CurrentMp==2 && replay.PreviousMp==3,
                "exact skill MP replay spent resource twice");

            var conflictingSkill=await store.ConsumeSkillMpAsync(
                new(character,generation,1,1,99999),1);
            var conflictingCost=await store.ConsumeSkillMpAsync(
                new(character,generation,1,1,23103),2);
            Checks.Require(conflictingSkill.Status==SkillMpConsumeStatus.StaleSequence &&
                           conflictingCost.Status==SkillMpConsumeStatus.StaleSequence,
                "skill MP receipt allowed sequence payload substitution");

            var insufficient=await store.ConsumeSkillMpAsync(
                new(character,generation,1,2,23103),3);
            Checks.Require(insufficient.Status==SkillMpConsumeStatus.InsufficientMp &&
                           insufficient.CurrentMp==2,
                "insufficient durable skill MP mutated profile");

            var second=await store.ConsumeSkillMpAsync(
                new(character,generation,1,2,23103),2);
            Checks.Require(second.Status==SkillMpConsumeStatus.Consumed &&
                           second.PreviousMp==2 && second.CurrentMp==0,
                "retry after non-mutating insufficient result did not settle");

            var older=await store.ConsumeSkillMpAsync(
                new(character,generation,1,1,23103),1);
            Checks.Require(older.Status==SkillMpConsumeStatus.AlreadyConsumed &&
                           older.CurrentMp==2,
                "committed older skill MP receipt disappeared");
        }

        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var saved=await reopened.GetCharacterCombatProfileAsync(character);
            Checks.Require(saved?.Data.CurrentMp==0 && saved.Data.MaxMp==5,
                "durable skill MP did not persist");
            var replay=await reopened.ConsumeSkillMpAsync(
                new(character,generation,1,2,23103),2);
            Checks.Require(replay.Status==SkillMpConsumeStatus.AlreadyConsumed &&
                           replay.PreviousMp==2 && replay.CurrentMp==0,
                "skill MP receipt did not survive DB reopen");
            var stale=await reopened.ConsumeSkillMpAsync(
                new(character,generation,1,1,23104),1);
            Checks.Require(stale.Status==SkillMpConsumeStatus.StaleSequence,
                "old skill sequence was reusable after DB reopen");
        }

        string deadDb=Path.Combine(root,
            "combat-skill-mp-dead-"+Guid.NewGuid().ToString("N")+".db");
        using(var deadStore=new SqliteIdentityStore(deadDb))
        {
            await deadStore.InitializeAsync();
            long account=(await deadStore.CreateAccountAsync(
                "skill_mp_dead","Skill-MP-Dead-Password-123!")).AccountId;
            long deadCharacter=(await deadStore.CreateCharacterAsync(
                account,0,"SkillMpDeadHero",new CharacterSpawn(13,24,24)))
                .Character!.CharacterId;
            await deadStore.SetCharacterCombatProfileAsync(deadCharacter,
                Profile(CombatProfileTrust.Verified,
                    "current-client:Skills/23103.tres",hp:100,attack:20)
                    with {CurrentMp=2,MaxMp=2});
            await deadStore.ApplyCharacterDamageAsync(deadCharacter,1000);
            var dead=await deadStore.ConsumeSkillMpAsync(
                new(deadCharacter,1,1,1,23103),1);
            Checks.Require(dead.Status==SkillMpConsumeStatus.Dead &&
                           dead.CurrentMp==2,
                "dead character spent durable skill MP");
        }
    }

    private static CharacterCombatProfileData Profile(
        CombatProfileTrust trust,string source,int hp,int attack) =>
        new(trust,source,Race:0,Career:0,Strength:5,Dexterity:5,
            Constitution:5,Wisdom:5,Level:1,CurrentHp:hp,MaxHp:hp,
            Attack:attack,Defense:4,Hit:10,Dodge:3,AttackRange:48,
            AttackCooldownTicks:15);
}
