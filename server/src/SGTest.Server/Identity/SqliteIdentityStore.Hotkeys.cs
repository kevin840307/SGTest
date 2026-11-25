using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public sealed record CharacterHotkeyRecord(
    long CharacterId,int Slot,int DataType,long DataId,long UpdatedUnixMilliseconds);

public sealed partial class SqliteIdentityStore
{
    public async Task<IReadOnlyList<CharacterHotkeyRecord>> ListCharacterHotkeysAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run<IReadOnlyList<CharacterHotkeyRecord>>(() =>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT character_id,slot,data_type,data_id,updated_unix_ms
FROM character_hotkeys WHERE character_id=$actor ORDER BY slot;";
            command.Parameters.AddWithValue("$actor",characterId);
            using var reader=command.ExecuteReader();
            var result=new List<CharacterHotkeyRecord>();
            while(reader.Read())
                result.Add(new(reader.GetInt64(0),reader.GetInt32(1),reader.GetInt32(2),
                    reader.GetInt64(3),reader.GetInt64(4)));
            return result;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveCharacterHotkeyAsync(
        long characterId,int slot,int dataType,long dataId,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||slot is <0 or >=12||dataType is <0 or >1||dataId<=0)
            throw new ArgumentOutOfRangeException(nameof(characterId));
        await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            using var command=connection.CreateCommand();
            command.Transaction=tx;
            command.CommandText=@"INSERT INTO character_hotkeys(
character_id,slot,data_type,data_id,updated_unix_ms)
VALUES($actor,$slot,$type,$data,$now)
ON CONFLICT(character_id,slot) DO UPDATE SET
 data_type=excluded.data_type,
 data_id=excluded.data_id,
 updated_unix_ms=excluded.updated_unix_ms;";
            command.Parameters.AddWithValue("$actor",characterId);
            command.Parameters.AddWithValue("$slot",slot);
            command.Parameters.AddWithValue("$type",dataType);
            command.Parameters.AddWithValue("$data",dataId);
            command.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if(command.ExecuteNonQuery()!=1)
                throw new InvalidOperationException("Hotkey upsert failed");
            tx.Commit();
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveCharacterHotkeyAsync(
        long characterId,int slot,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||slot is <0 or >=12)
            throw new ArgumentOutOfRangeException(nameof(characterId));
        await Task.Run(() =>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"DELETE FROM character_hotkeys
WHERE character_id=$actor AND slot=$slot;";
            command.Parameters.AddWithValue("$actor",characterId);
            command.Parameters.AddWithValue("$slot",slot);
            command.ExecuteNonQuery();
        },cancellationToken).ConfigureAwait(false);
    }
}
