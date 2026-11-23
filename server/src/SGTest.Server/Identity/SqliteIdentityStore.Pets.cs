using Microsoft.Data.Sqlite;
using SGTest.Server.Pet;

namespace SGTest.Server.Identity;

public enum PetActionStatus : byte
{
    Applied=0,
    AlreadyProcessed=1,
    PetNotOwned=2,
    AlreadyActive=3,
    NotActive=4,
    SequenceConflict=5,
    ItemNotOwned=6,
    InvalidEquipment=7,
    LevelTooLow=8,
    NotEquipped=9,
    RulesUnavailable=10
}

public enum PetActionKind : byte { Summon=1, Recall=2, Refresh=3, Equip=4, Unequip=5 }

public sealed record PetInstanceRecord(
    long PetUid,long CharacterId,int DefinitionId,int EvolutionId,int Level,long Experience,
    bool IsActive,byte[] StateBlob);

public sealed record PetActionOutcome(
    PetActionStatus Status,long ActivePetUid,long ItemUid=0,int SlotId=0,
    long ReplacedItemUid=0);

public enum PetExperienceGrantStatus : byte
{
    Applied=0,
    AlreadyProcessed=1,
    PetNotOwned=2,
    GrantConflict=3
}

public sealed record PetExperienceGrantOutcome(
    PetExperienceGrantStatus Status,long PetUid,int Level,long Experience);

/// <summary>
/// Durable pet ownership and active-pet state. SQLite owns the truth; no client
/// payload can set definition, level, experience or state_blob.
/// </summary>
public sealed partial class SqliteIdentityStore
{
    public const int PetMaximumPerCharacter=64;
    public const int PetMaximumStateBlobBytes=4096;

    public async Task<long> ProvisionPetAsync(
        long characterId,int definitionId,int level=1,long experience=0,
        ReadOnlyMemory<byte> stateBlob=default,int evolutionId=0,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||definitionId is <=0 or >100000000||
           evolutionId is <0 or >65535||level is <=0 or >1000000||
           experience is <0 or >9000000000000000L||
           stateBlob.Length>PetMaximumStateBlobBytes)
            throw new ArgumentOutOfRangeException();

        byte[] state=stateBlob.ToArray();
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            using(var exists=connection.CreateCommand())
            {
                exists.Transaction=tx;
                exists.CommandText="SELECT COUNT(*) FROM characters WHERE character_id=$id;";
                exists.Parameters.AddWithValue("$id",characterId);
                if(Convert.ToInt64(exists.ExecuteScalar())!=1)
                    throw new InvalidOperationException("Character does not exist");
            }
            using(var count=connection.CreateCommand())
            {
                count.Transaction=tx;
                count.CommandText="SELECT COUNT(*) FROM pet_instances WHERE character_id=$id;";
                count.Parameters.AddWithValue("$id",characterId);
                if(Convert.ToInt64(count.ExecuteScalar())>=PetMaximumPerCharacter)
                    throw new InvalidOperationException("Pet capacity reached");
            }
            long now=_timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            using var command=connection.CreateCommand();
            command.Transaction=tx;
            command.CommandText=@"INSERT INTO pet_instances(
character_id,definition_id,evolution_id,level,experience,is_active,state_blob,created_unix_ms,updated_unix_ms)
VALUES($character,$definition,$evolution,$level,$experience,0,$state,$now,$now);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$character",characterId);
            command.Parameters.AddWithValue("$definition",definitionId);
            command.Parameters.AddWithValue("$evolution",evolutionId);
            command.Parameters.AddWithValue("$level",level);
            command.Parameters.AddWithValue("$experience",experience);
            command.Parameters.Add("$state",SqliteType.Blob).Value=state;
            command.Parameters.AddWithValue("$now",now);
            long uid=Convert.ToInt64(command.ExecuteScalar());
            if(uid<=0)throw new InvalidOperationException("Pet insert failed");
            tx.Commit();
            return uid;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PetInstanceRecord>> GetPetsAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT pet_uid,character_id,definition_id,evolution_id,level,experience,is_active,state_blob
FROM pet_instances WHERE character_id=$character ORDER BY pet_uid;";
            command.Parameters.AddWithValue("$character",characterId);
            using var reader=command.ExecuteReader();
            var pets=new List<PetInstanceRecord>();
            while(reader.Read())
                pets.Add(new(
                    reader.GetInt64(0),reader.GetInt64(1),reader.GetInt32(2),reader.GetInt32(3),
                    reader.GetInt32(4),reader.GetInt64(5),reader.GetInt64(6)!=0,
                    (byte[])reader[7]));
            return (IReadOnlyList<PetInstanceRecord>)pets;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<PetActionOutcome> ApplyPetActionAsync(
        long characterId,long connectionGeneration,long mapEpoch,uint actionSequence,
        PetActionKind action,long petUid,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||connectionGeneration<=0||mapEpoch<=0||actionSequence==0||
           action is not (PetActionKind.Summon or PetActionKind.Recall)||petUid<=0)
            throw new ArgumentOutOfRangeException();

        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            using(var replay=connection.CreateCommand())
            {
                replay.Transaction=tx;
                replay.CommandText=@"SELECT action,pet_uid,resulting_active_uid
FROM pet_actions
WHERE character_id=$character AND connection_generation=$generation
AND map_epoch=$epoch AND action_sequence=$sequence;";
                replay.Parameters.AddWithValue("$character",characterId);
                replay.Parameters.AddWithValue("$generation",connectionGeneration);
                replay.Parameters.AddWithValue("$epoch",mapEpoch);
                replay.Parameters.AddWithValue("$sequence",(long)actionSequence);
                using var reader=replay.ExecuteReader();
                if(reader.Read())
                {
                    var oldAction=(PetActionKind)reader.GetInt32(0);
                    long oldUid=reader.GetInt64(1);
                    long oldActive=reader.GetInt64(2);
                    return oldAction==action&&oldUid==petUid
                        ?new PetActionOutcome(PetActionStatus.AlreadyProcessed,oldActive)
                        :new PetActionOutcome(PetActionStatus.SequenceConflict,oldActive);
                }
            }

            long active=ReadActivePetUid(connection,tx,characterId);
            bool owned;
            using(var ownedCommand=connection.CreateCommand())
            {
                ownedCommand.Transaction=tx;
                ownedCommand.CommandText=@"SELECT COUNT(*) FROM pet_instances
WHERE pet_uid=$pet AND character_id=$character;";
                ownedCommand.Parameters.AddWithValue("$pet",petUid);
                ownedCommand.Parameters.AddWithValue("$character",characterId);
                owned=Convert.ToInt64(ownedCommand.ExecuteScalar())==1;
            }

            PetActionStatus status;
            if(!owned)
                status=PetActionStatus.PetNotOwned;
            else if(action==PetActionKind.Summon&&active==petUid)
                status=PetActionStatus.AlreadyActive;
            else if(action==PetActionKind.Recall&&active!=petUid)
                status=PetActionStatus.NotActive;
            else
            {
                long now=_timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
                if(action==PetActionKind.Summon)
                {
                    using var clear=connection.CreateCommand();
                    clear.Transaction=tx;
                    clear.CommandText=@"UPDATE pet_instances SET is_active=0,updated_unix_ms=$now
WHERE character_id=$character AND is_active=1;";
                    clear.Parameters.AddWithValue("$now",now);
                    clear.Parameters.AddWithValue("$character",characterId);
                    clear.ExecuteNonQuery();

                    using var activate=connection.CreateCommand();
                    activate.Transaction=tx;
                    activate.CommandText=@"UPDATE pet_instances SET is_active=1,updated_unix_ms=$now
WHERE character_id=$character AND pet_uid=$pet;";
                    activate.Parameters.AddWithValue("$now",now);
                    activate.Parameters.AddWithValue("$character",characterId);
                    activate.Parameters.AddWithValue("$pet",petUid);
                    if(activate.ExecuteNonQuery()!=1)
                        throw new InvalidOperationException("Owned pet disappeared during summon");
                    active=petUid;
                }
                else
                {
                    using var recall=connection.CreateCommand();
                    recall.Transaction=tx;
                    recall.CommandText=@"UPDATE pet_instances SET is_active=0,updated_unix_ms=$now
WHERE character_id=$character AND pet_uid=$pet AND is_active=1;";
                    recall.Parameters.AddWithValue("$now",now);
                    recall.Parameters.AddWithValue("$character",characterId);
                    recall.Parameters.AddWithValue("$pet",petUid);
                    if(recall.ExecuteNonQuery()!=1)
                        throw new InvalidOperationException("Active pet disappeared during recall");
                    active=0;
                }
                status=PetActionStatus.Applied;
            }

            using(var audit=connection.CreateCommand())
            {
                audit.Transaction=tx;
                audit.CommandText=@"INSERT INTO pet_actions(
character_id,connection_generation,map_epoch,action_sequence,action,pet_uid,
result_status,resulting_active_uid,created_unix_ms)
VALUES($character,$generation,$epoch,$sequence,$action,$pet,$status,$active,$created);";
                audit.Parameters.AddWithValue("$character",characterId);
                audit.Parameters.AddWithValue("$generation",connectionGeneration);
                audit.Parameters.AddWithValue("$epoch",mapEpoch);
                audit.Parameters.AddWithValue("$sequence",(long)actionSequence);
                audit.Parameters.AddWithValue("$action",(int)action);
                audit.Parameters.AddWithValue("$pet",petUid);
                audit.Parameters.AddWithValue("$status",(int)status);
                audit.Parameters.AddWithValue("$active",active);
                audit.Parameters.AddWithValue("$created",_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                audit.ExecuteNonQuery();
            }
            tx.Commit();
            return new PetActionOutcome(status,active);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<PetExperienceGrantOutcome> ApplyPetExperienceAsync(
        long characterId,long petUid,long amount,string grantKey,
        PetGrowthCurve growth,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(growth);
        string key=(grantKey??"").Trim();
        if(characterId<=0||petUid<=0||amount<=0||
           amount>9_000_000_000_000_000L||
           key.Length is <1 or >128||key.Any(char.IsControl))
            throw new ArgumentOutOfRangeException();

        return await Task.Run<PetExperienceGrantOutcome>(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            using(var replay=connection.CreateCommand())
            {
                replay.Transaction=tx;
                replay.CommandText=@"SELECT pet_uid,amount,result_status,resulting_level,resulting_experience
FROM pet_experience_grants
WHERE character_id=$character AND grant_key=$key;";
                replay.Parameters.AddWithValue("$character",characterId);
                replay.Parameters.AddWithValue("$key",key);
                using var reader=replay.ExecuteReader();
                if(reader.Read())
                {
                    long oldPet=reader.GetInt64(0);
                    long oldAmount=reader.GetInt64(1);
                    int oldLevel=reader.GetInt32(3);
                    long oldExperience=reader.GetInt64(4);
                    if(oldPet!=petUid||oldAmount!=amount)
                        return new(PetExperienceGrantStatus.GrantConflict,
                            oldPet,oldLevel,oldExperience);
                    return new(PetExperienceGrantStatus.AlreadyProcessed,
                        oldPet,oldLevel,oldExperience);
                }
            }

            int level=0;
            long experience=0;
            bool owned;
            using(var pet=connection.CreateCommand())
            {
                pet.Transaction=tx;
                pet.CommandText=@"SELECT level,experience FROM pet_instances
WHERE pet_uid=$pet AND character_id=$character;";
                pet.Parameters.AddWithValue("$pet",petUid);
                pet.Parameters.AddWithValue("$character",characterId);
                using var reader=pet.ExecuteReader();
                owned=reader.Read();
                if(owned)
                {
                    level=reader.GetInt32(0);
                    experience=reader.GetInt64(1);
                }
            }

            PetExperienceGrantStatus status;
            if(!owned)
                status=PetExperienceGrantStatus.PetNotOwned;
            else
            {
                if(level>growth.MaximumLevel)
                    throw new InvalidDataException(
                        "Persisted pet level exceeds verified growth curve");
                long nextExperience;
                try { nextExperience=checked(experience+amount); }
                catch(OverflowException error)
                {
                    throw new InvalidOperationException("Pet experience overflow",error);
                }
                if(nextExperience>9_000_000_000_000_000L)
                    throw new InvalidOperationException("Pet experience exceeds durable cap");
                int nextLevel=growth.AdvanceLevel(level,nextExperience);
                long now=_timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
                using var update=connection.CreateCommand();
                update.Transaction=tx;
                update.CommandText=@"UPDATE pet_instances
SET level=$level,experience=$experience,updated_unix_ms=$now
WHERE pet_uid=$pet AND character_id=$character;";
                update.Parameters.AddWithValue("$level",nextLevel);
                update.Parameters.AddWithValue("$experience",nextExperience);
                update.Parameters.AddWithValue("$now",now);
                update.Parameters.AddWithValue("$pet",petUid);
                update.Parameters.AddWithValue("$character",characterId);
                if(update.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException(
                        "Owned pet disappeared during experience grant");
                level=nextLevel;
                experience=nextExperience;
                status=PetExperienceGrantStatus.Applied;
            }

            using(var audit=connection.CreateCommand())
            {
                audit.Transaction=tx;
                audit.CommandText=@"INSERT INTO pet_experience_grants(
character_id,grant_key,pet_uid,amount,result_status,resulting_level,
resulting_experience,created_unix_ms)
VALUES($character,$key,$pet,$amount,$status,$level,$experience,$created);";
                audit.Parameters.AddWithValue("$character",characterId);
                audit.Parameters.AddWithValue("$key",key);
                audit.Parameters.AddWithValue("$pet",petUid);
                audit.Parameters.AddWithValue("$amount",amount);
                audit.Parameters.AddWithValue("$status",(int)status);
                audit.Parameters.AddWithValue("$level",level);
                audit.Parameters.AddWithValue("$experience",experience);
                audit.Parameters.AddWithValue(
                    "$created",_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                audit.ExecuteNonQuery();
            }
            tx.Commit();
            return new(status,petUid,level,experience);
        },cancellationToken).ConfigureAwait(false);
    }

    private static long ReadActivePetUid(
        SqliteConnection connection,SqliteTransaction tx,long characterId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"SELECT pet_uid FROM pet_instances
WHERE character_id=$character AND is_active=1 LIMIT 1;";
        command.Parameters.AddWithValue("$character",characterId);
        object? value=command.ExecuteScalar();
        return value is null or DBNull?0:Convert.ToInt64(value);
    }
}
