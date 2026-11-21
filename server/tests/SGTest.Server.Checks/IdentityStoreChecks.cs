using System.Security.Cryptography;
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
        Checks.Run("identity backup captures one consistent WAL-aware snapshot without overwrite",
            () => BackupConsistency().GetAwaiter().GetResult());
        Checks.Run("identity migration creates a verified pre-upgrade backup before schema version changes",
            () => MigrationBackup().GetAwaiter().GetResult());
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
                await store.UpdateCharacterSpawnAsync(characterId, new CharacterSpawn(13, 1234.5, 2345.5));
                bool invalidSpawnRejected = false;
                try { await store.UpdateCharacterSpawnAsync(characterId, new CharacterSpawn(13, double.NaN, 1)); }
                catch (ArgumentOutOfRangeException) { invalidSpawnRejected = true; }
                Checks.Require(invalidSpawnRejected, "non-finite persisted position accepted");
                bool missingRejected = false;
                try { await store.UpdateCharacterSpawnAsync(long.MaxValue, new CharacterSpawn(13, 1, 1)); }
                catch (KeyNotFoundException) { missingRejected = true; }
                Checks.Require(missingRejected, "unknown character position update silently succeeded");
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
            Checks.Require(account.Characters[0].MapId == 13 &&
                account.Characters[0].X == 1234.5 && account.Characters[0].Y == 2345.5,
                "updated authoritative spawn did not survive reopen");
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


    private static async Task BackupConsistency()
    {
        string root = TempRoot();
        string db = Path.Combine(root, "identity.db");
        string backup = Path.Combine(root, "manual-backup.db");
        try
        {
            using var store = new SqliteIdentityStore(db);
            await store.InitializeAsync();
            Checks.Require((await store.CreateAccountAsync(
                "backup_one", "Backup-Password-123!")).Status ==
                AccountCreateStatus.Created, "backup fixture account failed");

            var result = await store.BackupAsync(backup);
            Checks.Require(result.SchemaVersion == SqliteIdentityStore.SchemaVersion &&
                result.ByteLength > 0 && File.Exists(result.DestinationPath) &&
                string.Equals(Path.GetFullPath(backup), result.DestinationPath,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal),
                "backup metadata/path invalid");
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(backup)));
            Checks.Require(string.Equals(hash, result.Sha256, StringComparison.Ordinal),
                "backup SHA-256 metadata does not match bytes");

            Checks.Require((await store.CreateAccountAsync(
                "backup_two", "Backup-Password-456!")).Status ==
                AccountCreateStatus.Created, "post-backup source write failed");

            using (var snapshot = OpenReadOnly(backup))
            {
                Checks.Require(ReadSchemaVersion(snapshot) == SqliteIdentityStore.SchemaVersion,
                    "manual backup schema version mismatch");
                Checks.Require(Scalar(snapshot, "SELECT COUNT(*) FROM accounts;") == 1,
                    "manual backup was not a point-in-time snapshot");
                Checks.Require(string.Equals(
                    Convert.ToString(ScalarObject(snapshot, "PRAGMA integrity_check;")),
                    "ok", StringComparison.OrdinalIgnoreCase),
                    "manual backup integrity_check failed");
            }
            using (var source = OpenReadOnly(db))
                Checks.Require(Scalar(source, "SELECT COUNT(*) FROM accounts;") == 2,
                    "backup operation mutated or lost later source state");

            bool overwriteRejected = false;
            try { await store.BackupAsync(backup); }
            catch (IOException) { overwriteRejected = true; }
            Checks.Require(overwriteRejected,
                "manual backup silently overwrote an existing destination");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task MigrationBackup()
    {
        string root = TempRoot();
        string db = Path.Combine(root, "identity.db");
        const string password = "Migration-Backup-Password-123!";
        try
        {
            using (var store = new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                Checks.Require((await store.CreateAccountAsync("migration_user", password)).Status ==
                    AccountCreateStatus.Created, "migration backup fixture account failed");
            }

            using (var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = db, Mode = SqliteOpenMode.ReadWrite,
                    Cache = SqliteCacheMode.Private, Pooling = false
                }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE schema_version SET version=20;";
                Checks.Require(command.ExecuteNonQuery() == 1,
                    "could not stage prior-schema migration fixture");
            }

            using (var migrated = new SqliteIdentityStore(db))
            {
                await migrated.InitializeAsync();
                var auth = await migrated.AuthenticateAsync("migration_user", password);
                Checks.Require(auth.Status == AuthenticationStatus.Success,
                    "migration changed persisted account data");
            }

            string[] backups = Directory.GetFiles(root,
                $"identity.db.pre-v20-to-v{SqliteIdentityStore.SchemaVersion}.*.bak", SearchOption.TopDirectoryOnly);
            Checks.Require(backups.Length == 1,
                "migration did not create exactly one pre-upgrade backup");
            using (var snapshot = OpenReadOnly(backups[0]))
            {
                Checks.Require(ReadSchemaVersion(snapshot) == 20,
                    "pre-migration backup did not preserve source schema version");
                Checks.Require(Scalar(snapshot, "SELECT COUNT(*) FROM accounts;") == 1,
                    "pre-migration backup lost persisted account rows");
                Checks.Require(string.Equals(
                    Convert.ToString(ScalarObject(snapshot, "PRAGMA integrity_check;")),
                    "ok", StringComparison.OrdinalIgnoreCase),
                    "pre-migration backup integrity_check failed");
            }
            using (var source = OpenReadOnly(db))
                Checks.Require(ReadSchemaVersion(source) == SqliteIdentityStore.SchemaVersion,
                    "source schema did not complete migration after backup");
        }
        finally { Directory.Delete(root, true); }
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private, Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static long ReadSchemaVersion(SqliteConnection connection) =>
        Scalar(connection, "SELECT version FROM schema_version LIMIT 1;");

    private static long Scalar(SqliteConnection connection, string sql) =>
        Convert.ToInt64(ScalarObject(connection, sql));

    private static object ScalarObject(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() ?? throw new InvalidDataException(
            "SQLite scalar query returned null");
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
