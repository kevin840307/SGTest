using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public sealed record CharacterSkillRecord(
    long CharacterId,int SkillId,int Level,string SourceReference,
    long UpdatedUnixMilliseconds);

public sealed partial class SqliteIdentityStore
{
    public async Task SetCharacterSkillLevelAsync(
        long characterId,int skillId,int level,string sourceReference,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||skillId is <1 or >65535||level is <1 or >255||
           string.IsNullOrWhiteSpace(sourceReference)||sourceReference.Length>256)
            throw new ArgumentOutOfRangeException(nameof(characterId));
        await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            if(ScalarLong(connection,
                "SELECT COUNT(*) FROM characters WHERE character_id=$id;",tx,
                ("$id",characterId))!=1)
                throw new KeyNotFoundException("Unknown character skill owner");
            using var command=connection.CreateCommand();
            command.Transaction=tx;
            command.CommandText=@"INSERT INTO character_skills(
character_id,skill_id,level,source_reference,updated_unix_ms)
VALUES($actor,$skill,$level,$source,$now)
ON CONFLICT(character_id,skill_id) DO UPDATE SET
 level=excluded.level,
 source_reference=excluded.source_reference,
 updated_unix_ms=excluded.updated_unix_ms;";
            command.Parameters.AddWithValue("$actor",characterId);
            command.Parameters.AddWithValue("$skill",skillId);
            command.Parameters.AddWithValue("$level",level);
            command.Parameters.AddWithValue("$source",sourceReference);
            command.Parameters.AddWithValue(
                "$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if(command.ExecuteNonQuery()!=1)
                throw new InvalidOperationException("Skill book upsert failed");
            tx.Commit();
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CharacterSkillRecord>> ListCharacterSkillsAsync(
        long characterId,int limit=1000,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||limit is <1 or >10000)
            throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run<IReadOnlyList<CharacterSkillRecord>>(() =>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT character_id,skill_id,level,
source_reference,updated_unix_ms
FROM character_skills
WHERE character_id=$actor
ORDER BY skill_id
LIMIT $limit;";
            command.Parameters.AddWithValue("$actor",characterId);
            command.Parameters.AddWithValue("$limit",limit);
            using var reader=command.ExecuteReader();
            var result=new List<CharacterSkillRecord>();
            while(reader.Read())
                result.Add(new(
                    reader.GetInt64(0),reader.GetInt32(1),reader.GetInt32(2),
                    reader.GetString(3),reader.GetInt64(4)));
            return result;
        },cancellationToken).ConfigureAwait(false);
    }
}
