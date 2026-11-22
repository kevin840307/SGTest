using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum CharacterManagementStatus
{
    Success, NotOwned, InvalidPassword, PasswordLocked, InvalidInput, Conflict, HasHistory
}

/// <summary>
/// Credential rotation and deletion occur only after account authentication.
/// SQLite owns atomic compare-and-swap and foreign key enforcement.
/// </summary>
public sealed partial class SqliteIdentityStore
{
    private static CharacterManagementStatus CredentialResult(CharacterPasswordStatus result) =>
        result switch
        {
            CharacterPasswordStatus.NotOwned => CharacterManagementStatus.NotOwned,
            CharacterPasswordStatus.Locked => CharacterManagementStatus.PasswordLocked,
            _ => CharacterManagementStatus.InvalidPassword
        };

    public async Task<CharacterManagementStatus> ChangeCharacterPasswordAsync(
        long accountId,long characterId,string? oldPassword,string newPassword,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(accountId<=0 || characterId<=0) return CharacterManagementStatus.InvalidInput;
        try { CharacterCreationRules.ValidatePassword(newPassword); }
        catch(ArgumentException) { return CharacterManagementStatus.InvalidInput; }

        var original=await Task.Run(() => {
            using var connection=Open();
            return ReadCharacterCredential(connection,null,accountId,characterId);
        },cancellationToken).ConfigureAwait(false);
        if(original is null) return CharacterManagementStatus.NotOwned;
        if(original.PasswordHash is null)
        {
            if(!string.IsNullOrEmpty(oldPassword)) return CharacterManagementStatus.InvalidInput;
        }
        else
        {
            if(oldPassword is null) return CharacterManagementStatus.InvalidPassword;
            var verified=await VerifyCharacterPasswordAsync(accountId,characterId,oldPassword,cancellationToken)
                .ConfigureAwait(false);
            if(verified!=CharacterPasswordStatus.Success) return CredentialResult(verified);
        }
        // Hash before acquiring a SQLite write transaction. Secret is purpose separated.
        string newHash=await _passwords.HashAsync(
            CharacterCreationRules.PasswordPurpose+newPassword,cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            var latest=ReadCharacterCredential(connection,tx,accountId,characterId);
            if(latest is null) return CharacterManagementStatus.NotOwned;
            if(latest.PasswordHash!=original.PasswordHash || latest.Override!=original.Override)
                return CharacterManagementStatus.Conflict;
            if(latest.LockedUntil>_timeProvider.GetUtcNow().ToUnixTimeMilliseconds())
                return CharacterManagementStatus.PasswordLocked;
            using var command=connection.CreateCommand();
            command.Transaction=tx;
            command.CommandText=@"INSERT INTO character_password_overrides(
character_id,password_hash,failed_password_attempts,password_locked_until_ms)
VALUES($id,$hash,0,0)
ON CONFLICT(character_id) DO UPDATE SET
password_hash=excluded.password_hash,failed_password_attempts=0,password_locked_until_ms=0;";
            command.Parameters.AddWithValue("$id",characterId);
            command.Parameters.AddWithValue("$hash",newHash);
            if(command.ExecuteNonQuery()!=1) return CharacterManagementStatus.Conflict;
            tx.Commit();
            return CharacterManagementStatus.Success;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<CharacterManagementStatus> DeleteCharacterWithPasswordAsync(
        long accountId,long characterId,string password,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(accountId<=0 || characterId<=0) return CharacterManagementStatus.InvalidInput;
        try { CharacterCreationRules.ValidatePassword(password); }
        catch(ArgumentException) { return CharacterManagementStatus.InvalidInput; }
        var original=await Task.Run(() => {
            using var connection=Open();
            return ReadCharacterCredential(connection,null,accountId,characterId);
        },cancellationToken).ConfigureAwait(false);
        if(original is null) return CharacterManagementStatus.NotOwned;
        if(original.PasswordHash is null) return CharacterManagementStatus.InvalidInput;
        var verified=await VerifyCharacterPasswordAsync(accountId,characterId,password,cancellationToken)
            .ConfigureAwait(false);
        if(verified!=CharacterPasswordStatus.Success) return CredentialResult(verified);
        return await Task.Run(() =>
        {
            try
            {
                using var connection=Open();
                using var tx=connection.BeginTransaction();
                var current=ReadCharacterCredential(connection,tx,accountId,characterId);
                if(current is null) return CharacterManagementStatus.NotOwned;
                if(current.PasswordHash!=original.PasswordHash || current.Override!=original.Override)
                    return CharacterManagementStatus.Conflict;
                if(current.LockedUntil>_timeProvider.GetUtcNow().ToUnixTimeMilliseconds())
                    return CharacterManagementStatus.PasswordLocked;

                // Delete only this character's transient receipts and inventory.
                // Do not rewrite trade, admin or player-shop settlement history; if
                // any restrictive reference remains, the whole transaction rolls back.
                foreach(var sql in new[] {
                    "DELETE FROM skill_heal_actions WHERE character_id=$id OR target_character_id=$id;",
                    "DELETE FROM recovery_use_actions WHERE character_id=$id;",
                    "DELETE FROM inventory_discard_actions WHERE character_id=$id;",
                    "DELETE FROM skill_mp_actions WHERE character_id=$id;",
                    "DELETE FROM item_instances WHERE character_id=$id;",
                    "DELETE FROM ground_drops WHERE picked_by_character_id=$id;",
                    "DELETE FROM characters WHERE character_id=$id AND account_id=$account;"
                })
                {
                    using var command=connection.CreateCommand();
                    command.Transaction=tx;
                    command.CommandText=sql;
                    command.Parameters.AddWithValue("$id",characterId);
                    command.Parameters.AddWithValue("$account",accountId);
                    int affected=command.ExecuteNonQuery();
                    if(sql.StartsWith("DELETE FROM characters ",StringComparison.Ordinal) && affected!=1)
                        return CharacterManagementStatus.Conflict;
                }
                tx.Commit();
                return CharacterManagementStatus.Success;
            }
            catch(SqliteException e) when(e.SqliteErrorCode==19)
            {
                // For durable economy/audit foreign keys, fail closed rather than
                // silently deleting another character's transaction history.
                return CharacterManagementStatus.HasHistory;
            }
        },cancellationToken).ConfigureAwait(false);
    }
}
