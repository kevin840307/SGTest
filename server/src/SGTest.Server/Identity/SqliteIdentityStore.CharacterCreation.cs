using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

/// <summary>Durable starting attributes and purpose-separated character credentials.</summary>
public sealed partial class SqliteIdentityStore
{
    private sealed record PreparedCharacterCreation(CharacterCreationState State, string PasswordHash);
    private sealed record CharacterCredential(string? PasswordHash, int Failures, long LockedUntil, bool Override);

    public async Task<CharacterCreateResult> CreateCharacterWithSettingsAsync(
        long accountId, int slot, string name, CharacterSpawn spawn, CharacterCreationSettings settings,
        CharacterAppearance? appearance = null, CharacterAppearanceRules? appearanceRules = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (slot < 0 || slot >= _options.MaxCharactersPerAccount) throw new ArgumentOutOfRangeException(nameof(slot));
        CharacterCreationRules.ValidateName(NormalizeCharacterName(name).Display);
        ValidateSpawn(spawn);
        if (appearance.HasValue != (appearanceRules is not null))
            throw new ArgumentException("Appearance and verified rules must be provided together");
        var look = appearance is { } value ? appearanceRules!.Resolve(value) : null;
        // Reuse the existing bounded Argon2id engine without weakening account-password rules.
        string hash = await _passwords.HashAsync(settings.PasswordForHashing, cancellationToken).ConfigureAwait(false);
        var prepared = new PreparedCharacterCreation(new(settings.Attributes,settings.SourceReference),hash);
        return await CreateCharacterCoreAsync(accountId,slot,name,spawn,look,cancellationToken,prepared)
            .ConfigureAwait(false);
    }

    private static void InsertCharacterCreation(SqliteConnection connection, SqliteTransaction tx,
        long characterId, PreparedCharacterCreation value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"INSERT INTO character_creation_settings(
character_id,strength,dexterity,constitution,wisdom,source_ref,password_hash)
VALUES($id,$str,$dex,$con,$wis,$source,$hash);";
        command.Parameters.AddWithValue("$id",characterId);
        command.Parameters.AddWithValue("$str",value.State.Attributes.Strength);
        command.Parameters.AddWithValue("$dex",value.State.Attributes.Dexterity);
        command.Parameters.AddWithValue("$con",value.State.Attributes.Constitution);
        command.Parameters.AddWithValue("$wis",value.State.Attributes.Wisdom);
        command.Parameters.AddWithValue("$source",value.State.SourceReference);
        command.Parameters.AddWithValue("$hash",value.PasswordHash);
        if (command.ExecuteNonQuery()!=1) throw new InvalidOperationException("Character settings creation failed");
    }

    public async Task<CharacterPasswordStatus> VerifyCharacterPasswordAsync(long accountId, long characterId,
        string? password, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (accountId<=0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (characterId<=0) throw new ArgumentOutOfRangeException(nameof(characterId));
        var before = await Task.Run(() => {
            using var connection = Open();
            return ReadCharacterCredential(connection,null,accountId,characterId);
        },cancellationToken).ConfigureAwait(false);
        if (before is null) return CharacterPasswordStatus.NotOwned;
        if (before.PasswordHash is null)
            return string.IsNullOrEmpty(password) ? CharacterPasswordStatus.Success : CharacterPasswordStatus.InvalidPassword;
        if (before.LockedUntil>_timeProvider.GetUtcNow().ToUnixTimeMilliseconds())
            return CharacterPasswordStatus.Locked;
        bool valid;
        try { CharacterCreationRules.ValidatePassword(password!); valid=true; }
        catch (ArgumentException) { valid=false; }
        bool verified = valid && await _passwords.VerifyAsync(
            CharacterCreationRules.PasswordPurpose+password,before.PasswordHash,cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => SettleCharacterPassword(accountId,characterId,before.PasswordHash,verified),
            cancellationToken).ConfigureAwait(false);
    }

    private CharacterPasswordStatus SettleCharacterPassword(long accountId, long characterId,
        string expectedHash, bool verified)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        var current = ReadCharacterCredential(connection,tx,accountId,characterId);
        if (current is null) return CharacterPasswordStatus.NotOwned;
        if (current.PasswordHash!=expectedHash) return CharacterPasswordStatus.InvalidPassword;
        long now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        if (current.LockedUntil>now) return CharacterPasswordStatus.Locked;
        // Read/check/update in the same SQLite transaction, including across Store instances.
        int failures = verified ? 0 : (current.LockedUntil>0 ? 0 : current.Failures)+1;
        long lockedUntil = failures>=CharacterCreationRules.PasswordFailureLimit
            ? now+CharacterCreationRules.PasswordLockoutSeconds*1000L : 0;
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = current.Override ? @"UPDATE character_password_overrides
SET failed_password_attempts=$failures,password_locked_until_ms=$locked WHERE character_id=$id;" : @"UPDATE character_creation_settings
SET failed_password_attempts=$failures,password_locked_until_ms=$locked WHERE character_id=$id;";
        command.Parameters.AddWithValue("$failures",failures);
        command.Parameters.AddWithValue("$locked",lockedUntil);
        command.Parameters.AddWithValue("$id",characterId);
        if(command.ExecuteNonQuery()!=1) throw new InvalidOperationException("Character credential changed during verification");
        tx.Commit();
        return lockedUntil>now ? CharacterPasswordStatus.Locked :
            verified ? CharacterPasswordStatus.Success : CharacterPasswordStatus.InvalidPassword;
    }

    private static CharacterCredential? ReadCharacterCredential(SqliteConnection connection, SqliteTransaction? tx,
        long accountId, long characterId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"SELECT COALESCE(p.password_hash,s.password_hash),
COALESCE(p.failed_password_attempts,s.failed_password_attempts,0),
COALESCE(p.password_locked_until_ms,s.password_locked_until_ms,0),
p.character_id
FROM characters c JOIN accounts a ON a.account_id=c.account_id
LEFT JOIN character_creation_settings s ON s.character_id=c.character_id
LEFT JOIN character_password_overrides p ON p.character_id=c.character_id
WHERE c.character_id=$character AND c.account_id=$account AND a.disabled=0;";
        command.Parameters.AddWithValue("$character",characterId);
        command.Parameters.AddWithValue("$account",accountId);
        using var reader = command.ExecuteReader();
        return !reader.Read() ? null : reader.IsDBNull(0) ? new(null,0,0,false) :
            new(reader.GetString(0),reader.GetInt32(1),reader.GetInt64(2),!reader.IsDBNull(3));
    }
}
