using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.Identity;
using SGTest.Server.World;
using Microsoft.Data.Sqlite;

internal static class PvpModeChecks
{
    public static void Verify(string root)
    {
        Checks.Run("verified NonPK maps and durable blue-white PvP mode",
            ()=>VerifyAsync(root).GetAwaiter().GetResult());
        Checks.Run("authenticated PvP mode request replay and map epoch admission",
            VerifySequenceGate);
        Checks.Run("recovered PvP pink-red crime state persists and cannot be cleared by mode toggle",
            ()=>VerifyCrimeAsync(root).GetAwaiter().GetResult());
        Checks.Run("schema v16 PvP mode migrates to v17 crime columns",
            ()=>VerifyV16MigrationAsync(root).GetAwaiter().GetResult());
    }

    private static void VerifySequenceGate()
    {
        var gate = new PvpModeSequenceGate();
        var first = new TrustedPvpModeCommand(11,7,1,1,PvpModeCommandAction.SetWhite);
        Checks.Require(gate.Inspect(first)==PvpModeSequenceDecision.New,
            "first PvP mutation rejected");
        gate.Record(first,PvpModeResultStatus.Applied,PvpMode.EnabledWhite,true);
        Checks.Require(gate.Inspect(first)==PvpModeSequenceDecision.ExactReplay &&
            gate.LastReceipt==new PvpModeResultWire(
                1,1,PvpModeResultStatus.Applied,PvpMode.EnabledWhite,true),
            "exact retry failed to preserve original durable receipt");
        var forged=first with { Action=PvpModeCommandAction.SetBlue };
        Checks.Require(gate.Inspect(forged)==PvpModeSequenceDecision.Stale,
            "same-sequence conflicting PvP action accepted");
        bool zeroRejected=false;
        try { gate.Inspect(first with { PvpSequence=0 }); }
        catch(ArgumentOutOfRangeException) { zeroRejected=true; }
        Checks.Require(zeroRejected,"zero sequence passed PvP ingress guard");
        Checks.Require(gate.Inspect(first with { PvpSequence=2 })==
            PvpModeSequenceDecision.New,"increasing sequence rejected");
        var rejected=first with { PvpSequence=2,Action=PvpModeCommandAction.SetBlue };
        gate.Record(rejected,PvpModeResultStatus.PlayerDead,PvpMode.EnabledWhite,true);
        Checks.Require(gate.Inspect(rejected)==PvpModeSequenceDecision.ExactReplay &&
            gate.LastReceipt.Status==PvpModeResultStatus.PlayerDead,
            "denied command replay was incorrectly re-authorized");
        var transfer=rejected with { RequestMapEpoch=2,PvpSequence=1 };
        Checks.Require(gate.Inspect(transfer)==PvpModeSequenceDecision.New,
            "new trusted map generation did not reset sequence");
        gate.Record(transfer,PvpModeResultStatus.Ready,PvpMode.EnabledWhite,false);
        Checks.Require(gate.Inspect(rejected)==PvpModeSequenceDecision.Stale &&
            gate.LastReceipt.MapEpoch==2,
            "late old-map command rewound PvP sequence");
        var wire=WireProtocol.EncodePvpModeResult(new PvpModeResultWire(
            2,2,PvpModeResultStatus.StaleSequence,PvpMode.EnabledWhite,false));
        Checks.Require(WireProtocol.TryDecodePvpModeResult(wire,out var decoded) &&
            decoded.Status==PvpModeResultStatus.StaleSequence,
            "PvP stale-sequence result does not roundtrip");
    }


    private static async Task VerifyCrimeAsync(string root)
    {
        string dir=Path.Combine(root,"pvp_crime_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        long character;
        var now=DateTimeOffset.FromUnixTimeMilliseconds(2_000_000_000_000L);
        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "pvp_crime","Pvp-Crime-Password-123!")).AccountId;
            character=(await store.CreateCharacterAsync(
                account,0,"PvpCrime",new CharacterSpawn(13,8,8)))
                .Character?.CharacterId??throw new Exception("PvP crime character missing");

            var initial=await store.GetCharacterPvpStateAsync(character);
            Checks.Require(PvpCrimePolicy.NameState(initial,now)==PvpNameState.ProtectedBlue&&
                !PvpCrimePolicy.IsCombatEligible(initial,now),
                "fresh PvP crime state is not protected blue");

            await store.SetCharacterPvpModeAsync(character,PvpMode.EnabledWhite);
            var white=await store.GetCharacterPvpStateAsync(character);
            Checks.Require(PvpCrimePolicy.IsNormalWhite(white,now)&&
                PvpCrimePolicy.IsCombatEligible(white,now),
                "white mode is not normal PvP eligible");

            var pink=await store.MarkPvpHostileAttackAsync(character,true,now);
            Checks.Require(PvpCrimePolicy.NameState(pink,now)==PvpNameState.Pink&&
                pink.PinkUntilUnixMilliseconds==now.AddSeconds(30).ToUnixTimeMilliseconds()&&
                !PvpCrimePolicy.IsNormalWhite(pink,now),
                "hostile attack did not create 30-second pink state");
            Checks.Require(PvpCrimePolicy.NameState(
                pink,now.AddSeconds(27))==PvpNameState.PinkFlashing,
                "last three pink seconds are not flashing");

            // Ordinary player mode selection cannot launder an active crime state.
            await store.SetCharacterPvpModeAsync(character,PvpMode.ProtectedBlue);
            var toggled=await store.GetCharacterPvpStateAsync(character);
            Checks.Require(toggled.Mode==PvpMode.ProtectedBlue&&
                PvpCrimePolicy.NameState(toggled,now)==PvpNameState.Pink&&
                PvpCrimePolicy.IsCombatEligible(toggled,now),
                "blue preference incorrectly cleared active pink crime");

            var red=await store.MarkPvpWhiteKillAsync(character,true,now.AddSeconds(1));
            Checks.Require(red.RedMan&&red.PinkUntilUnixMilliseconds==0&&
                PvpCrimePolicy.NameState(red,now.AddSeconds(1))==PvpNameState.Red,
                "white-player kill did not turn attacker red");

            var unchanged=await store.MarkPvpHostileAttackAsync(
                character,false,now.AddSeconds(2));
            unchanged=await store.MarkPvpWhiteKillAsync(
                character,false,now.AddSeconds(2));
            Checks.Require(unchanged.RedMan&&unchanged.UpdatedUnixMilliseconds==
                red.UpdatedUnixMilliseconds,
                "attacking/killing non-white target escalated or rewrote crime state");

            var gmWhite=await store.SetCharacterPvpNameStateForGmAsync(
                character,PvpNameState.EnabledWhite,now.AddSeconds(3));
            Checks.Require(PvpCrimePolicy.NameState(gmWhite,now.AddSeconds(3))==
                PvpNameState.EnabledWhite&&!gmWhite.RedMan&&gmWhite.PinkUntilUnixMilliseconds==0,
                "trusted GM white reset failed");
            var gmFlash=await store.SetCharacterPvpNameStateForGmAsync(
                character,PvpNameState.PinkFlashing,now.AddSeconds(4));
            Checks.Require(PvpCrimePolicy.NameState(gmFlash,now.AddSeconds(4))==
                PvpNameState.PinkFlashing,
                "trusted GM flashing pink state failed");
        }
        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var persisted=await reopened.GetCharacterPvpStateAsync(character);
            Checks.Require(persisted.PinkUntilUnixMilliseconds==
                now.AddSeconds(7).ToUnixTimeMilliseconds()&&
                PvpCrimePolicy.NameState(persisted,now.AddSeconds(4))==
                    PvpNameState.PinkFlashing,
                "PvP crime state did not survive SQLite reopen");
        }
    }

    private static async Task VerifyV16MigrationAsync(string root)
    {
        string db=Path.Combine(root,"pvp_v16_"+Guid.NewGuid().ToString("N")+".db");
        using(var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {DataSource=db,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString()))
        {
            connection.Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"
CREATE TABLE schema_version(version INTEGER NOT NULL);
INSERT INTO schema_version(version) VALUES(16);
CREATE TABLE character_pvp_modes(
 character_id INTEGER PRIMARY KEY,
 mode INTEGER NOT NULL DEFAULT 0 CHECK(mode IN (0,1)),
 updated_unix_ms INTEGER NOT NULL);";
            command.ExecuteNonQuery();
        }
        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            Checks.Require(SqliteIdentityStore.SchemaVersion==18,
                "v16 PvP migration did not target v18");
            long account=(await store.CreateAccountAsync(
                "pvp_migrate","Pvp-Migrate-Password-123!")).AccountId;
            long character=(await store.CreateCharacterAsync(
                account,0,"PvpMigrate",new CharacterSpawn(13,8,8)))
                .Character?.CharacterId??throw new Exception("migration character missing");
            var state=await store.GetCharacterPvpStateAsync(character);
            Checks.Require(state.Mode==PvpMode.ProtectedBlue&&!state.RedMan&&
                state.RedManKillLeft==0&&state.PinkUntilUnixMilliseconds==0,
                "v16 migration did not default new crime columns safely");
        }
    }

    private static async Task VerifyAsync(string root)
    {
        foreach(int map in new[]{7,9,15,17,22,25,38,40})
            Checks.Require(!PvpMapPolicy.AllowsPlayerPvp(map),
                $"Old NonPK Map{map} admitted player PvP");
        Checks.Require(PvpMapPolicy.AllowsPlayerPvp(13),
            "Map13 should be PvP-eligible by verified NonPK exclusion");
        Checks.Require(!PvpMapPolicy.AllowsPlayerPvp(0),
            "invalid map admitted PvP");

        string dir=Path.Combine(root,"pvp_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        long character;
        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            Checks.Require(SqliteIdentityStore.SchemaVersion==18,
                "PvP schema version is not v18");
            long account=(await store.CreateAccountAsync(
                "pvp_user","Pvp-Password-123!")).AccountId;
            character=(await store.CreateCharacterAsync(
                account,0,"PvpHero",new CharacterSpawn(13,8,8)))
                .Character?.CharacterId??throw new Exception("PvP character missing");
            var initial=await store.GetCharacterPvpModeAsync(character);
            Checks.Require(initial.Mode==PvpMode.ProtectedBlue&&
                initial.UpdatedUnixMilliseconds==0,
                "fresh character is not protected blue");
            var white=await store.SetCharacterPvpModeAsync(
                character,PvpMode.EnabledWhite);
            Checks.Require(white.Mode==PvpMode.EnabledWhite&&
                white.UpdatedUnixMilliseconds>0,
                "white PvP mode was not persisted");
        }
        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var persisted=await reopened.GetCharacterPvpModeAsync(character);
            Checks.Require(persisted.Mode==PvpMode.EnabledWhite,
                "white PvP mode did not survive reopen");
            var blue=await reopened.SetCharacterPvpModeAsync(
                character,PvpMode.ProtectedBlue);
            Checks.Require(blue.Mode==PvpMode.ProtectedBlue,
                "blue PvP mode update failed");
        }
        using(var third=new SqliteIdentityStore(db))
        {
            await third.InitializeAsync();
            Checks.Require((await third.GetCharacterPvpModeAsync(character)).Mode==
                PvpMode.ProtectedBlue,"blue PvP mode did not survive reopen");
        }
    }
}
