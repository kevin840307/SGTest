using SGTest.Server.Npc;
﻿using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;


/// <summary>
/// SQLite identity persistence. This service is intentionally outside World/Tick;
/// callers must finish Account/Auth/CharacterChoose before TrustedRegisterAsync.
/// No network listener or client-controlled ActorId is exposed here.
/// </summary>
public sealed partial class SqliteIdentityStore : IDisposable
{
    public const int SchemaVersion = 46;
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly IdentityStoreOptions _options;
    private readonly PasswordHasher _passwords;
    private readonly bool _ownsPasswords;
    private readonly string _dummyHash;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public SqliteIdentityStore(string databasePath, PasswordHasher? passwords = null,
        IdentityStoreOptions? options = null, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path required", nameof(databasePath));
        _options = options ?? new IdentityStoreOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (_options.MaxCharactersPerAccount is < 1 or > 32 || _options.BusyTimeoutSeconds is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(options));
        _ownsPasswords = passwords is null;
        _passwords = passwords ?? new PasswordHasher();
        _dummyHash = _passwords.CreateDeterministicDummyHash();
        string full = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        _databasePath = full;
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

    public Task<CharacterCreateResult> CreateCharacterAsync(long accountId, int slot,
        string name, CharacterSpawn spawn, CancellationToken cancellationToken = default)
        => CreateCharacterCoreAsync(accountId, slot, name, spawn, null, cancellationToken);

    public Task<CharacterCreateResult> CreateCharacterAsync(long accountId, int slot,
        string name, CharacterSpawn spawn, CharacterAppearance appearance,
        CharacterAppearanceRules appearanceRules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appearanceRules);
        return CreateCharacterCoreAsync(accountId, slot, name, spawn,
            appearanceRules.Resolve(appearance), cancellationToken);
    }

    private async Task<CharacterCreateResult> CreateCharacterCoreAsync(long accountId, int slot,
        string name, CharacterSpawn spawn, CharacterAppearanceState? appearance,
        CancellationToken cancellationToken, PreparedCharacterCreation? creation = null)
    {
        ThrowIfDisposed();
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (slot < 0 || slot >= _options.MaxCharactersPerAccount)
            throw new ArgumentOutOfRangeException(nameof(slot));
        var characterName = NormalizeCharacterName(name);
        ValidateSpawn(spawn);
        return await Task.Run(() => CreateCharacter(accountId, slot, characterName, spawn, appearance, creation), cancellationToken)
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
        (string Display, string Key) name, CharacterSpawn spawn, CharacterAppearanceState? appearance,
        PreparedCharacterCreation? creation = null)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        // Re-check the account inside the same character-create transaction.
        // Authentication happened earlier, but a local operator may disable the
        // account between Auth and Create; creation must not bypass that state.
        if (ScalarLong(connection,
            "SELECT COUNT(*) FROM accounts WHERE account_id=$id AND disabled=0;", tx,
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
            if (appearance is not null)
            {
                using var look = connection.CreateCommand();
                look.Transaction = tx;
                look.CommandText = @"INSERT INTO character_appearances(
character_id,race,hair_index,hair_color,body_index,leg_index,sleeve_color,source_ref)
VALUES($id,$race,$hair,$color,$body,$leg,$sleeve,$source);";
                look.Parameters.AddWithValue("$id", characterId);
                look.Parameters.AddWithValue("$race", appearance.Value.Race);
                look.Parameters.AddWithValue("$hair", appearance.Value.HairIndex);
                look.Parameters.AddWithValue("$color", appearance.Value.HairColor);
                look.Parameters.AddWithValue("$body", appearance.Value.BodyIndex);
                look.Parameters.AddWithValue("$leg", appearance.Value.LegIndex);
                look.Parameters.AddWithValue("$sleeve", appearance.Value.SleeveColor);
                look.Parameters.AddWithValue("$source", appearance.SourceReference);
                if (look.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Character appearance creation failed");
            }
            if (creation is not null)
            {
                InsertCharacterCreation(connection, tx, characterId, creation);
                if (creation.InitialProfile is not null)
                    InsertInitialCombatProfile(connection, tx, characterId, creation.InitialProfile, now);
            }
            using (var wallet = connection.CreateCommand())
            {
                wallet.Transaction = tx;
                wallet.CommandText = @"INSERT INTO character_wallets(character_id,money)
VALUES($character,0);";
                wallet.Parameters.AddWithValue("$character", characterId);
                if (wallet.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Character wallet creation failed");
            }
            tx.Commit();
            return new CharacterCreateResult(CharacterCreateStatus.Created,
                new CharacterRecord(characterId, accountId, slot, name.Display,
                    spawn.MapId, spawn.X, spawn.Y, now, appearance, creation?.State));
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19 &&
            e.SqliteExtendedErrorCode is 1555 or 2067)
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
        command.CommandText = @"SELECT c.character_id,c.account_id,c.slot,c.name,c.map_id,c.x,c.y,c.created_unix_ms,
a.race,a.hair_index,a.hair_color,a.body_index,a.leg_index,a.sleeve_color,a.source_ref,
s.strength,s.dexterity,s.constitution,s.wisdom,s.source_ref,p.character_id
FROM characters c LEFT JOIN character_appearances a ON a.character_id=c.character_id
LEFT JOIN character_creation_settings s ON s.character_id=c.character_id
LEFT JOIN character_password_overrides p ON p.character_id=c.character_id
WHERE c.account_id=$account ORDER BY c.slot;";
        command.Parameters.AddWithValue("$account", accountId);
        using var reader = command.ExecuteReader();
        var rows = new List<CharacterRecord>();
        while (reader.Read())
            rows.Add(new CharacterRecord(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2),
                reader.GetString(3), reader.GetInt32(4), reader.GetDouble(5), reader.GetDouble(6),
                reader.GetInt64(7), reader.IsDBNull(8) ? null : new CharacterAppearanceState(
                    new(reader.GetInt32(8),reader.GetInt32(9),reader.GetInt32(10),
                        reader.GetInt32(11),reader.GetInt32(12),reader.GetInt32(13)),reader.GetString(14)),
                reader.IsDBNull(15) ? null : new CharacterCreationState(
                    new(reader.GetInt32(15),reader.GetInt32(16),reader.GetInt32(17),reader.GetInt32(18)),
                    reader.GetString(19)), !reader.IsDBNull(20)));
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

    private static bool ColumnExists(
        SqliteConnection connection, string table, string column,
        SqliteTransaction? transaction = null)
    {
        // This helper is intentionally limited to the trusted internal schema
        // identifiers supplied by migrations; no client/user value is interpolated.
        if (table is not ("character_combat_profiles" or
                "item_instances" or "storage_items" or "equipped_items" or
                "trade_commit_items" or "player_shop_listings" or "clans" or
                "character_pvp_modes" or "character_appearances" or "pet_actions" or
                "mecha_instances") ||
            string.IsNullOrWhiteSpace(column))
            throw new ArgumentOutOfRangeException(nameof(table));
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
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
