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

    public async Task<bool> SwapCharacterHotkeysAsync(
        long characterId,int firstSlot,int secondSlot,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||firstSlot is <0 or >=12||
           secondSlot is <0 or >=12||firstSlot==secondSlot)
            throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            using var read=connection.CreateCommand();
            read.Transaction=tx;
            read.CommandText=@"SELECT slot,data_type,data_id
FROM character_hotkeys
WHERE character_id=$actor AND slot IN ($first,$second)
ORDER BY slot;";
            read.Parameters.AddWithValue("$actor",characterId);
            read.Parameters.AddWithValue("$first",firstSlot);
            read.Parameters.AddWithValue("$second",secondSlot);
            int? firstType=null,secondType=null;
            long? firstData=null,secondData=null;
            using(var reader=read.ExecuteReader())
            {
                while(reader.Read())
                {
                    int slot=reader.GetInt32(0);
                    if(slot==firstSlot)
                    {
                        firstType=reader.GetInt32(1);
                        firstData=reader.GetInt64(2);
                    }
                    else if(slot==secondSlot)
                    {
                        secondType=reader.GetInt32(1);
                        secondData=reader.GetInt64(2);
                    }
                }
            }
            if(firstType is null||firstData is null||
               secondType is null||secondData is null)
            {
                tx.Rollback();
                return false;
            }

            using var update=connection.CreateCommand();
            update.Transaction=tx;
            update.CommandText=@"UPDATE character_hotkeys SET
 data_type=CASE slot WHEN $first THEN $second_type ELSE $first_type END,
 data_id=CASE slot WHEN $first THEN $second_data ELSE $first_data END,
 updated_unix_ms=$now
WHERE character_id=$actor AND slot IN ($first,$second);";
            update.Parameters.AddWithValue("$actor",characterId);
            update.Parameters.AddWithValue("$first",firstSlot);
            update.Parameters.AddWithValue("$second",secondSlot);
            update.Parameters.AddWithValue("$first_type",firstType.Value);
            update.Parameters.AddWithValue("$first_data",firstData.Value);
            update.Parameters.AddWithValue("$second_type",secondType.Value);
            update.Parameters.AddWithValue("$second_data",secondData.Value);
            update.Parameters.AddWithValue(
                "$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if(update.ExecuteNonQuery()!=2)
            {
                tx.Rollback();
                return false;
            }
            tx.Commit();
            return true;
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
