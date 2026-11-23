namespace SGTest.Server.Identity;

public enum CharacterExperienceGrantStatus : byte
{
    Applied=0,
    AlreadyProcessed=1,
    CharacterNotFound=2,
    GrantConflict=3
}

public sealed record CharacterExperienceState(long CharacterId,long Experience);

public sealed record CharacterExperienceGrantOutcome(
    CharacterExperienceGrantStatus Status,long Amount,long Experience);

/// <summary>
/// Durable character EXP authority. Client packets never supply EXP amounts or
/// grant keys; trusted combat settlement owns both. Grant keys make lethal
/// monster rewards replay-safe across retries and process restarts.
/// </summary>
public sealed partial class SqliteIdentityStore
{
    public const long CharacterMaximumExperience=9_000_000_000_000_000L;

    public async Task<CharacterExperienceState?> GetCharacterExperienceAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run<CharacterExperienceState?>(()=>
        {
            using var connection=Open();
            using(var command=connection.CreateCommand())
            {
                command.CommandText=@"
SELECT experience FROM character_experience_state
WHERE character_id=$character;";
                command.Parameters.AddWithValue("$character",characterId);
                object? value=command.ExecuteScalar();
                if(value is not null and not DBNull)
                    return new(characterId,Convert.ToInt64(value));
            }
            using(var exists=connection.CreateCommand())
            {
                exists.CommandText=
                    "SELECT COUNT(*) FROM characters WHERE character_id=$character;";
                exists.Parameters.AddWithValue("$character",characterId);
                return Convert.ToInt64(exists.ExecuteScalar())==1
                    ?new CharacterExperienceState(characterId,0):null;
            }
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<CharacterExperienceGrantOutcome> ApplyCharacterExperienceAsync(
        long characterId,long amount,string grantKey,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        string key=(grantKey??"").Trim();
        if(characterId<=0||amount<0||amount>CharacterMaximumExperience||
           key.Length is <1 or >128||key.Any(char.IsControl))
            throw new ArgumentOutOfRangeException();

        return await Task.Run<CharacterExperienceGrantOutcome>(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            using(var replay=connection.CreateCommand())
            {
                replay.Transaction=tx;
                replay.CommandText=@"
SELECT amount,resulting_experience
FROM character_experience_grants
WHERE character_id=$character AND grant_key=$key;";
                replay.Parameters.AddWithValue("$character",characterId);
                replay.Parameters.AddWithValue("$key",key);
                using var reader=replay.ExecuteReader();
                if(reader.Read())
                {
                    long oldAmount=reader.GetInt64(0);
                    long oldExperience=reader.GetInt64(1);
                    return oldAmount==amount
                        ?new(CharacterExperienceGrantStatus.AlreadyProcessed,
                            oldAmount,oldExperience)
                        :new(CharacterExperienceGrantStatus.GrantConflict,
                            oldAmount,oldExperience);
                }
            }

            using(var exists=connection.CreateCommand())
            {
                exists.Transaction=tx;
                exists.CommandText=
                    "SELECT COUNT(*) FROM characters WHERE character_id=$character;";
                exists.Parameters.AddWithValue("$character",characterId);
                if(Convert.ToInt64(exists.ExecuteScalar())!=1)
                    return new(CharacterExperienceGrantStatus.CharacterNotFound,amount,0);
            }

            long now=_timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            using(var ensure=connection.CreateCommand())
            {
                ensure.Transaction=tx;
                ensure.CommandText=@"
INSERT INTO character_experience_state(character_id,experience,updated_unix_ms)
VALUES($character,0,$now)
ON CONFLICT(character_id) DO NOTHING;";
                ensure.Parameters.AddWithValue("$character",characterId);
                ensure.Parameters.AddWithValue("$now",now);
                ensure.ExecuteNonQuery();
            }

            long current;
            using(var read=connection.CreateCommand())
            {
                read.Transaction=tx;
                read.CommandText=@"
SELECT experience FROM character_experience_state
WHERE character_id=$character;";
                read.Parameters.AddWithValue("$character",characterId);
                current=Convert.ToInt64(read.ExecuteScalar());
            }

            long next;
            try { next=checked(current+amount); }
            catch(OverflowException error)
            {
                throw new InvalidOperationException("Character experience overflow",error);
            }
            if(next>CharacterMaximumExperience)
                throw new InvalidOperationException(
                    "Character experience exceeds durable cap");

            using(var update=connection.CreateCommand())
            {
                update.Transaction=tx;
                update.CommandText=@"
UPDATE character_experience_state
SET experience=$experience,updated_unix_ms=$now
WHERE character_id=$character;";
                update.Parameters.AddWithValue("$experience",next);
                update.Parameters.AddWithValue("$now",now);
                update.Parameters.AddWithValue("$character",characterId);
                if(update.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException(
                        "Character experience state disappeared during grant");
            }

            using(var audit=connection.CreateCommand())
            {
                audit.Transaction=tx;
                audit.CommandText=@"
INSERT INTO character_experience_grants(
    character_id,grant_key,amount,result_status,resulting_experience,created_unix_ms)
VALUES($character,$key,$amount,$status,$experience,$created);";
                audit.Parameters.AddWithValue("$character",characterId);
                audit.Parameters.AddWithValue("$key",key);
                audit.Parameters.AddWithValue("$amount",amount);
                audit.Parameters.AddWithValue(
                    "$status",(int)CharacterExperienceGrantStatus.Applied);
                audit.Parameters.AddWithValue("$experience",next);
                audit.Parameters.AddWithValue("$created",now);
                audit.ExecuteNonQuery();
            }

            tx.Commit();
            return new(CharacterExperienceGrantStatus.Applied,amount,next);
        },cancellationToken).ConfigureAwait(false);
    }
}
