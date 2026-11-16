using System.Text;
using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

internal static class IdentityStoreChecks
{
    public static void Verify()
    {
        Checks.Run("Argon2id PHC roundtrip unicode salt and wrong-password rejection",
            () => PasswordRoundTrip().GetAwaiter().GetResult());
        Checks.Run("SQLite account auth is case-stable and never stores plaintext password",
            () => AccountAuthentication().GetAwaiter().GetResult());
        Checks.Run("SQLite character slots names ownership and persistence survive reopen",
            () => CharacterPersistence().GetAwaiter().GetResult());
        Checks.Run("SQLite concurrent duplicate slot is serialized without duplicate character",
            () => ConcurrentSlot().GetAwaiter().GetResult());
        Checks.Run("disabled account fails closed after valid password verification",
            () => DisabledAccount().GetAwaiter().GetResult());
        Checks.Run("identity schema version mismatch fails closed",
            () => SchemaMismatch().GetAwaiter().GetResult());
    }

    private static async Task PasswordRoundTrip()
    {
        using var hasher = new PasswordHasher();
        const string password = "密碼-LongEnough-🔐-2026";
        string first = await hasher.HashAsync(password);
        string second = await hasher.HashAsync(password);
        Checks.Require(first.StartsWith("$argon2id$v=19$m=9216,t=4,p=1$", StringComparison.Ordinal),
            "unexpected Argon2id PHC policy");
        Checks.Require(first != second, "password salts repeated");
        Checks.Require(await hasher.VerifyAsync(password, first), "unicode password did not verify");
        Checks.Require(!await hasher.VerifyAsync(password + "x", first), "wrong password verified");
        Checks.Require(!await hasher.VerifyAsync(password, "$argon2id$v=19$m=999999,t=9,p=4$bad$bad"),
            "hostile KDF parameters accepted");
        Checks.Require(!hasher.NeedsRehash(first), "fresh password unexpectedly needs rehash");
    }

    private static async Task AccountAuthentication()
    {
        string root = TempRoot();
        string db = Path.Combine(root, "identity.db");
        const string secret = "NeverStoreThis-密碼-123!";
        try
        {
            using (var store = new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                var created = await store.CreateAccountAsync("Kevin_01", secret);
                Checks.Require(created.Status == AccountCreateStatus.Created && created.AccountId > 0,
                    "account create failed");
                var duplicate = await store.CreateAccountAsync("kEvIn_01", "Different-Password-123!");
                Checks.Require(duplicate.Status == AccountCreateStatus.DuplicateUsername,
                    "case-insensitive account identity duplicated");
                var auth = await store.AuthenticateAsync("KEVIN_01", secret);
                Checks.Require(auth.Status == AuthenticationStatus.Success &&
                    auth.Account?.AccountId == created.AccountId && auth.Account.Characters.Count == 0,
                    "valid account auth failed");
                Checks.Require((await store.AuthenticateAsync("Kevin_01", "wrong-password")).Status ==
                    AuthenticationStatus.InvalidCredentials, "wrong password not rejected");
                Checks.Require((await store.AuthenticateAsync("missing_user", secret)).Status ==
                    AuthenticationStatus.InvalidCredentials, "missing account not rejected");
            }

            byte[] needle = Encoding.UTF8.GetBytes(secret);
            foreach (string file in Directory.GetFiles(root, "identity.db*"))
            {
                byte[] raw = File.ReadAllBytes(file);
                Checks.Require(!Contains(raw, needle), "plaintext password present in SQLite files");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task CharacterPersistence()
    {
        string root = TempRoot();
        string db = Path.Combine(root, "identity.db");
        long accountId;
        long characterId;
        try
        {
            using (var store = new SqliteIdentityStore(db, options: new IdentityStoreOptions(MaxCharactersPerAccount: 2)))
            {
                await store.InitializeAsync();
                accountId = (await store.CreateAccountAsync("player1", "Player-Password-123!")).AccountId;
                var first = await store.CreateCharacterAsync(accountId, 0, "Jos\u00E9", new CharacterSpawn(15, 7872, 11296));
                Checks.Require(first.Status == CharacterCreateStatus.Created && first.Character is not null,
                    "character create failed");
                var firstCharacter = first.Character ?? throw new Exception("missing created character");
                characterId = firstCharacter.CharacterId;
                Checks.Require(firstCharacter.MapId == 15 && firstCharacter.X == 7872 && firstCharacter.Y == 11296,
                    "server spawn not persisted");
                Checks.Require((await store.CreateCharacterAsync(accountId, 0, "Other", new CharacterSpawn(15, 1, 1))).Status ==
                    CharacterCreateStatus.SlotOccupied, "duplicate slot accepted");
                Checks.Require((await store.CreateCharacterAsync(accountId, 1, "Jose\u0301", new CharacterSpawn(13, 2560, 2880))).Status ==
                    CharacterCreateStatus.NameTaken, "unicode-normalized duplicate name accepted");
                Checks.Require((await store.CreateCharacterAsync(accountId, 1, "Second", new CharacterSpawn(13, 2560, 2880))).Status ==
                    CharacterCreateStatus.Created, "second character create failed");
                bool limited = false;
                try { await store.CreateCharacterAsync(accountId, 2, "Third", new CharacterSpawn(13, 1, 1)); }
                catch (ArgumentOutOfRangeException) { limited = true; }
                Checks.Require(limited, "slot above configured character limit accepted");
            }

            using var reopened = new SqliteIdentityStore(db, options: new IdentityStoreOptions(MaxCharactersPerAccount: 2));
            await reopened.InitializeAsync();
            var auth = await reopened.AuthenticateAsync("PLAYER1", "Player-Password-123!");
            Checks.Require(auth.Status == AuthenticationStatus.Success && auth.Account is not null,
                "reopened auth failed");
            var account = auth.Account ?? throw new Exception("missing reopened account");
            Checks.Require(account.Characters.Count == 2 && account.Characters[0].CharacterId == characterId &&
                account.Characters[0].Slot == 0 && account.Characters[1].Slot == 1,
                "character persistence/order failed");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task ConcurrentSlot()
    {
        string root = TempRoot();
        string db = Path.Combine(root, "identity.db");
        try
        {
            using var store = new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long id = (await store.CreateAccountAsync("race_user", "Race-Password-123!")).AccountId;
            var a = store.CreateCharacterAsync(id, 0, "RaceA", new CharacterSpawn(15, 100, 100));
            var b = store.CreateCharacterAsync(id, 0, "RaceB", new CharacterSpawn(15, 100, 100));
            var results = await Task.WhenAll(a, b);
            Checks.Require(results.Count(x => x.Status == CharacterCreateStatus.Created) == 1 &&
                results.Count(x => x.Status == CharacterCreateStatus.SlotOccupied) == 1,
                "concurrent same-slot result not exactly one winner");
            Checks.Require((await store.ListCharactersAsync(id)).Count == 1,
                "concurrent same-slot created duplicate rows");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task DisabledAccount()
    {
        string root = TempRoot();
        try
        {
            using var store = new SqliteIdentityStore(Path.Combine(root, "identity.db"));
            await store.InitializeAsync();
            long id = (await store.CreateAccountAsync("disabled_user", "Disabled-Password-123!")).AccountId;
            await store.SetDisabledAsync(id, true);
            Checks.Require((await store.AuthenticateAsync("disabled_user", "Disabled-Password-123!")).Status ==
                AuthenticationStatus.Disabled, "disabled account authenticated");
            Checks.Require((await store.AuthenticateAsync("disabled_user", "wrong-password")).Status ==
                AuthenticationStatus.InvalidCredentials, "disabled account leaked status to wrong password");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task SchemaMismatch()
    {
        string root = TempRoot();
        string db = Path.Combine(root, "identity.db");
        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString();
            using (var connection = new SqliteConnection(cs))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE schema_version(version INTEGER NOT NULL); INSERT INTO schema_version VALUES(99);";
                command.ExecuteNonQuery();
            }
            using var store = new SqliteIdentityStore(db);
            bool rejected = false;
            try { await store.InitializeAsync(); }
            catch (InvalidDataException) { rejected = true; }
            Checks.Require(rejected, "unknown identity schema accepted");
        }
        finally { Directory.Delete(root, true); }
    }

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "sgtest_identity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length) return false;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return true;
        }
        return false;
    }
}
