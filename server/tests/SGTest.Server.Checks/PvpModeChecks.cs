using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class PvpModeChecks
{
    public static void Verify(string root)
    {
        Checks.Run("verified NonPK maps and durable blue-white PvP mode",
            ()=>VerifyAsync(root).GetAwaiter().GetResult());
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
            Checks.Require(SqliteIdentityStore.SchemaVersion==16,
                "PvP schema version is not v16");
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
