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

    private static CharacterCombatProfileData Profile(
        CombatProfileTrust trust,string source,int hp,int attack) =>
        new(trust,source,Race:0,Career:0,Strength:5,Dexterity:5,
            Constitution:5,Wisdom:5,Level:1,CurrentHp:hp,MaxHp:hp,
            Attack:attack,Defense:4,Hit:10,Dodge:3,AttackRange:48,
            AttackCooldownTicks:15);
}
