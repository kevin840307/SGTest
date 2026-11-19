using Microsoft.Data.Sqlite;
using SGTest.Server.World;

namespace SGTest.Server.Identity;

public sealed record CharacterPvpMode(
    long CharacterId,PvpMode Mode,long UpdatedUnixMilliseconds);

public sealed partial class SqliteIdentityStore
{
    public async Task<CharacterPvpMode> GetCharacterPvpModeAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var exists=connection.CreateCommand();
            exists.CommandText="SELECT COUNT(*) FROM characters WHERE character_id=$id;";
            exists.Parameters.AddWithValue("$id",characterId);
            if(Convert.ToInt64(exists.ExecuteScalar())!=1)
                throw new InvalidOperationException("Unknown character PvP state");
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT mode,updated_unix_ms FROM character_pvp_modes
WHERE character_id=$id;";
            command.Parameters.AddWithValue("$id",characterId);
            using var reader=command.ExecuteReader();
            if(!reader.Read())return new(characterId,PvpMode.ProtectedBlue,0);
            var mode=(PvpMode)reader.GetInt32(0);
            if(mode is not (PvpMode.ProtectedBlue or PvpMode.EnabledWhite))
                throw new InvalidDataException("Invalid persisted PvP mode");
            return new(characterId,mode,reader.GetInt64(1));
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<CharacterPvpMode> SetCharacterPvpModeAsync(
        long characterId,PvpMode mode,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||mode is not (PvpMode.ProtectedBlue or PvpMode.EnabledWhite))
            throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(()=>
        {
            using var connection=Open();
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using var command=connection.CreateCommand();
            command.CommandText=@"INSERT INTO character_pvp_modes(
character_id,mode,updated_unix_ms)
SELECT character_id,$mode,$now FROM characters WHERE character_id=$id
ON CONFLICT(character_id) DO UPDATE SET mode=excluded.mode,updated_unix_ms=excluded.updated_unix_ms
RETURNING mode,updated_unix_ms;";
            command.Parameters.AddWithValue("$id",characterId);
            command.Parameters.AddWithValue("$mode",(int)mode);
            command.Parameters.AddWithValue("$now",now);
            using var reader=command.ExecuteReader();
            if(!reader.Read())throw new InvalidOperationException("Unknown character PvP state");
            return new CharacterPvpMode(characterId,(PvpMode)reader.GetInt32(0),reader.GetInt64(1));
        },cancellationToken).ConfigureAwait(false);
    }
}
