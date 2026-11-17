using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public sealed record IdentityStoreOptions(int MaxCharactersPerAccount = 8, int BusyTimeoutSeconds = 5);
public readonly record struct CharacterSpawn(int MapId, double X, double Y);
public readonly record struct CharacterPositionUpdate(long CharacterId, CharacterSpawn Spawn);
public sealed record CharacterRecord(long CharacterId, long AccountId, int Slot, string Name,
    int MapId, double X, double Y, long CreatedUnixMilliseconds);
public sealed record AccountRecord(long AccountId, string Username, bool Disabled,
    IReadOnlyList<CharacterRecord> Characters);

public enum AccountCreateStatus { Created, DuplicateUsername }
public sealed record AccountCreateResult(AccountCreateStatus Status, long AccountId = 0);
public enum AuthenticationStatus { Success, InvalidCredentials, Disabled }
public sealed record AuthenticationResult(AuthenticationStatus Status, AccountRecord? Account = null);
public enum CharacterCreateStatus { Created, UnknownAccount, SlotOccupied, NameTaken, LimitReached }
public sealed record CharacterCreateResult(CharacterCreateStatus Status, CharacterRecord? Character = null);

/// <summary>
/// SQLite identity persistence. This service is intentionally outside World/Tick;
/// callers must finish Account/Auth/CharacterChoose before TrustedRegisterAsync.
/// No network listener or client-controlled ActorId is exposed here.
/// </summary>
public sealed class SqliteIdentityStore : IDisposable
{
    public const int SchemaVersion = 1;
    private readonly string _connectionString;
    private readonly IdentityStoreOptions _options;
    private readonly PasswordHasher _passwords;
    private readonly bool _ownsPasswords;
    private readonly string _dummyHash;
    private bool _disposed;

    public SqliteIdentityStore(string databasePath, PasswordHasher? passwords = null,
        IdentityStoreOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path required", nameof(databasePath));
        _options = options ?? new IdentityStoreOptions();
        if (_options.MaxCharactersPerAccount is < 1 or > 32 || _options.BusyTimeoutSeconds is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(options));
        _ownsPasswords = passwords is null;
        _passwords = passwords ?? new PasswordHasher();
        _dummyHash = _passwords.CreateDeterministicDummyHash();
        string full = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = full,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            // Identity traffic is not a World/Tick hot path. Disabling pooling keeps
            // DB file ownership deterministic across shutdown/restart and tests.
            Pooling = false,
            DefaultTimeout = _options.BusyTimeoutSeconds
        };
        _connectionString = builder.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await Task.Run(() =>
        {
            using var connection = Open();
            Execute(connection, "PRAGMA journal_mode=WAL;");
            Execute(connection, "PRAGMA synchronous=NORMAL;");
            Execute(connection, $"PRAGMA busy_timeout={_options.BusyTimeoutSeconds * 1000};");
            using var tx = connection.BeginTransaction();
            Execute(connection, "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);", tx);
            long count = ScalarLong(connection, "SELECT COUNT(*) FROM schema_version;", tx);
            if (count == 0)
                Execute(connection, "INSERT INTO schema_version(version) VALUES (1);", tx);
            else if (count != 1 || ScalarLong(connection, "SELECT version FROM schema_version LIMIT 1;", tx) != SchemaVersion)
                throw new InvalidDataException("Unsupported identity schema version");

            Execute(connection, @"
CREATE TABLE IF NOT EXISTS accounts (
    account_id INTEGER PRIMARY KEY AUTOINCREMENT,
    username_key TEXT NOT NULL UNIQUE,
    username_display TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    disabled INTEGER NOT NULL DEFAULT 0 CHECK(disabled IN (0,1)),
    created_unix_ms INTEGER NOT NULL
);", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS characters (
    character_id INTEGER PRIMARY KEY AUTOINCREMENT,
    account_id INTEGER NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
    slot INTEGER NOT NULL CHECK(slot BETWEEN 0 AND 31),
    name TEXT NOT NULL,
    name_key TEXT NOT NULL UNIQUE,
    map_id INTEGER NOT NULL CHECK(map_id BETWEEN 1 AND 65535),
    x REAL NOT NULL,
    y REAL NOT NULL,
    created_unix_ms INTEGER NOT NULL,
    updated_unix_ms INTEGER NOT NULL,
    UNIQUE(account_id, slot)
);", tx);
            Execute(connection, "CREATE INDEX IF NOT EXISTS ix_characters_account ON characters(account_id, slot);", tx);
            tx.Commit();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountCreateResult> CreateAccountAsync(string username, string password,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var identity = NormalizeUsername(username);
        PasswordHasher.ValidateNewPassword(password);
        string passwordHash = await _passwords.HashAsync(password, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO accounts(username_key,username_display,password_hash,created_unix_ms)
VALUES($key,$display,$hash,$created); SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$key", identity.Key);
            command.Parameters.AddWithValue("$display", identity.Display);
            command.Parameters.AddWithValue("$hash", passwordHash);
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            try
            {
                long id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                return new AccountCreateResult(AccountCreateStatus.Created, id);
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19)
            {
                return new AccountCreateResult(AccountCreateStatus.DuplicateUsername);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string? key = TryNormalizeUsername(username, out var normalized) ? normalized.Key : null;
        AccountRow? row = await Task.Run(() => key is null ? null : ReadAccount(key), cancellationToken)
            .ConfigureAwait(false);
        string suppliedPassword = password ?? string.Empty;
        string hash = row?.PasswordHash ?? _dummyHash;
        bool verified = await _passwords.VerifyAsync(suppliedPassword, hash, cancellationToken)
            .ConfigureAwait(false);
        if (row is null || !verified)
            return new AuthenticationResult(AuthenticationStatus.InvalidCredentials);
        if (row.Disabled)
            return new AuthenticationResult(AuthenticationStatus.Disabled);

        if (_passwords.NeedsRehash(row.PasswordHash))
        {
            string upgraded = await _passwords.HashAsync(suppliedPassword, cancellationToken).ConfigureAwait(false);
            await Task.Run(() => UpdatePasswordHash(row.AccountId, row.PasswordHash, upgraded), cancellationToken)
                .ConfigureAwait(false);
        }
        var characters = await Task.Run(() => ReadCharacters(row.AccountId), cancellationToken)
            .ConfigureAwait(false);
        return new AuthenticationResult(AuthenticationStatus.Success,
            new AccountRecord(row.AccountId, row.UsernameDisplay, false, characters));
    }

    public async Task<CharacterCreateResult> CreateCharacterAsync(long accountId, int slot,
        string name, CharacterSpawn spawn, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (slot < 0 || slot >= _options.MaxCharactersPerAccount)
            throw new ArgumentOutOfRangeException(nameof(slot));
        var characterName = NormalizeCharacterName(name);
        ValidateSpawn(spawn);
        return await Task.Run(() => CreateCharacter(accountId, slot, characterName, spawn), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CharacterRecord>> ListCharactersAsync(long accountId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        return await Task.Run(() => ReadCharacters(accountId), cancellationToken).ConfigureAwait(false);
    }

    public Task UpdateCharacterSpawnAsync(long characterId, CharacterSpawn spawn,
        CancellationToken cancellationToken = default) =>
        UpdateCharacterSpawnsAsync([new CharacterPositionUpdate(characterId, spawn)], cancellationToken);

    public async Task UpdateCharacterSpawnsAsync(
        IReadOnlyList<CharacterPositionUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(updates));
        var seen = new HashSet<long>();
        foreach (var update in updates)
        {
            if (update.CharacterId <= 0 || !seen.Add(update.CharacterId))
                throw new ArgumentException("Character position batch has invalid/duplicate ID", nameof(updates));
            ValidateSpawn(update.Spawn);
        }

        await Task.Run(() =>
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var update in updates)
            {
                using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = @"UPDATE characters
SET map_id=$map,x=$x,y=$y,updated_unix_ms=$updated
WHERE character_id=$id;";
                command.Parameters.AddWithValue("$map", update.Spawn.MapId);
                command.Parameters.AddWithValue("$x", update.Spawn.X);
                command.Parameters.AddWithValue("$y", update.Spawn.Y);
                command.Parameters.AddWithValue("$updated", now);
                command.Parameters.AddWithValue("$id", update.CharacterId);
                if (command.ExecuteNonQuery() != 1)
                    throw new KeyNotFoundException("Unknown character");
            }
            tx.Commit();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetDisabledAsync(long accountId, bool disabled,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        await Task.Run(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE accounts SET disabled=$disabled WHERE account_id=$id;";
            command.Parameters.AddWithValue("$disabled", disabled ? 1 : 0);
            command.Parameters.AddWithValue("$id", accountId);
            if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException("Unknown account");
        }, cancellationToken).ConfigureAwait(false);
    }

    private CharacterCreateResult CreateCharacter(long accountId, int slot,
        (string Display, string Key) name, CharacterSpawn spawn)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        if (ScalarLong(connection, "SELECT COUNT(*) FROM accounts WHERE account_id=$id;", tx,
            ("$id", accountId)) != 1)
            return new CharacterCreateResult(CharacterCreateStatus.UnknownAccount);
        if (ScalarLong(connection, "SELECT COUNT(*) FROM characters WHERE account_id=$id;", tx,
            ("$id", accountId)) >= _options.MaxCharactersPerAccount)
            return new CharacterCreateResult(CharacterCreateStatus.LimitReached);

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"INSERT INTO characters(account_id,slot,name,name_key,map_id,x,y,created_unix_ms,updated_unix_ms)
VALUES($account,$slot,$name,$key,$map,$x,$y,$created,$updated); SELECT last_insert_rowid();";
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$slot", slot);
        command.Parameters.AddWithValue("$name", name.Display);
        command.Parameters.AddWithValue("$key", name.Key);
        command.Parameters.AddWithValue("$map", spawn.MapId);
        command.Parameters.AddWithValue("$x", spawn.X);
        command.Parameters.AddWithValue("$y", spawn.Y);
        command.Parameters.AddWithValue("$created", now);
        command.Parameters.AddWithValue("$updated", now);
        try
        {
            long characterId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            tx.Commit();
            return new CharacterCreateResult(CharacterCreateStatus.Created,
                new CharacterRecord(characterId, accountId, slot, name.Display,
                    spawn.MapId, spawn.X, spawn.Y, now));
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            bool slotTaken = ScalarLong(connection,
                "SELECT COUNT(*) FROM characters WHERE account_id=$id AND slot=$slot;", tx,
                ("$id", accountId), ("$slot", slot)) > 0;
            tx.Rollback();
            return new CharacterCreateResult(slotTaken
                ? CharacterCreateStatus.SlotOccupied : CharacterCreateStatus.NameTaken);
        }
    }

    private AccountRow? ReadAccount(string usernameKey)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT account_id,username_display,password_hash,disabled
FROM accounts WHERE username_key=$key LIMIT 1;";
        command.Parameters.AddWithValue("$key", usernameKey);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new AccountRow(reader.GetInt64(0), reader.GetString(1),
            reader.GetString(2), reader.GetInt64(3) != 0) : null;
    }

    private IReadOnlyList<CharacterRecord> ReadCharacters(long accountId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT character_id,account_id,slot,name,map_id,x,y,created_unix_ms
FROM characters WHERE account_id=$account ORDER BY slot;";
        command.Parameters.AddWithValue("$account", accountId);
        using var reader = command.ExecuteReader();
        var rows = new List<CharacterRecord>();
        while (reader.Read())
            rows.Add(new CharacterRecord(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2),
                reader.GetString(3), reader.GetInt32(4), reader.GetDouble(5), reader.GetDouble(6),
                reader.GetInt64(7)));
        return rows;
    }

    private void UpdatePasswordHash(long accountId, string oldHash, string newHash)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"UPDATE accounts SET password_hash=$new
WHERE account_id=$id AND password_hash=$old;";
        command.Parameters.AddWithValue("$new", newHash);
        command.Parameters.AddWithValue("$old", oldHash);
        command.Parameters.AddWithValue("$id", accountId);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnection connection, string sql,
        SqliteTransaction? transaction = null, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var p in parameters) command.Parameters.AddWithValue(p.Name, p.Value);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static (string Display, string Key) NormalizeUsername(string username)
    {
        if (!TryNormalizeUsername(username, out var result))
            throw new ArgumentException("Username must be 3..32 ASCII letters, digits or underscore", nameof(username));
        return result;
    }

    private static bool TryNormalizeUsername(string? username, out (string Display, string Key) result)
    {
        result = default;
        if (username is null || username.Length is < 3 or > 32) return false;
        foreach (char c in username)
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
                return false;
        result = (username, username.ToLowerInvariant());
        return true;
    }

    private static (string Display, string Key) NormalizeCharacterName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string normalized = name.Normalize(NormalizationForm.FormC).Trim();
        if (normalized.Length is < 1 or > 24 || normalized.Any(char.IsControl))
            throw new ArgumentException("Character name must be 1..24 non-control characters", nameof(name));
        return (normalized, normalized);
    }

    private static void ValidateSpawn(CharacterSpawn spawn)
    {
        if (spawn.MapId is < 1 or > 65535 || !double.IsFinite(spawn.X) || !double.IsFinite(spawn.Y) ||
            spawn.X < 0 || spawn.Y < 0)
            throw new ArgumentOutOfRangeException(nameof(spawn), "Spawn must be a finite server-authorized location");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private sealed record AccountRow(long AccountId, string UsernameDisplay, string PasswordHash, bool Disabled);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsPasswords) _passwords.Dispose();
    }
}
