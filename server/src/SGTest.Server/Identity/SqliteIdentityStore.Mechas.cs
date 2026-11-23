using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum MechaActionStatus : byte
{
    Applied=0,
    AlreadyProcessed=1,
    MechaNotOwned=2,
    AlreadyMounted=3,
    NotMounted=4,
    SequenceConflict=5,
    ItemNotOwned=6,
    InvalidEquipment=7,
    LevelTooLow=8,
    MountedLocked=9,
    NotEquipped=10,
    CorePartCannotUnequip=11,
    CardSlotsFull=12,
    RulesUnavailable=13
}

public enum MechaActionKind : byte
{
    Mount=1,
    Dismount=2,
    Refresh=3,
    Equip=4,
    Unequip=5
}

public sealed record MechaInstanceRecord(
    long MechaUid,long CharacterId,int DefinitionId,int Level,long Experience,
    bool IsMounted,byte[] PartsBlob);

public sealed record MechaActionOutcome(
    MechaActionStatus Status,long MountedMechaUid,long ItemUid=0,
    int SlotId=0,long ReplacedItemUid=0);

/// <summary>
/// Durable mecha ownership and mounted-state authority. Client requests identify
/// an owned instance only; definition, level, experience and parts remain server-owned.
/// Exact HK movement/stat/card effects are intentionally outside this baseline until
/// source-backed rules are available.
/// </summary>
public sealed partial class SqliteIdentityStore
{
    public const int MechaMaximumPerCharacter=64;
    public const int MechaMaximumPartsBlobBytes=4096;

    public async Task<long> ProvisionMechaAsync(
        long characterId,int definitionId,int level=1,long experience=0,
        ReadOnlyMemory<byte> partsBlob=default,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||definitionId is <=0 or >100000000||
           level is <=0 or >1000000||experience is <0 or >9000000000000000L||
           partsBlob.Length>MechaMaximumPartsBlobBytes)
            throw new ArgumentOutOfRangeException();

        byte[] parts=partsBlob.ToArray();
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
                count.CommandText="SELECT COUNT(*) FROM mecha_instances WHERE character_id=$id;";
                count.Parameters.AddWithValue("$id",characterId);
                if(Convert.ToInt64(count.ExecuteScalar())>=MechaMaximumPerCharacter)
                    throw new InvalidOperationException("Mecha capacity reached");
            }
            long now=_timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            using var command=connection.CreateCommand();
            command.Transaction=tx;
            command.CommandText=@"INSERT INTO mecha_instances(
character_id,definition_id,level,experience,is_mounted,parts_blob,created_unix_ms,updated_unix_ms)
VALUES($character,$definition,$level,$experience,0,$parts,$now,$now);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$character",characterId);
            command.Parameters.AddWithValue("$definition",definitionId);
            command.Parameters.AddWithValue("$level",level);
            command.Parameters.AddWithValue("$experience",experience);
            command.Parameters.Add("$parts",SqliteType.Blob).Value=parts;
            command.Parameters.AddWithValue("$now",now);
            long uid=Convert.ToInt64(command.ExecuteScalar());
            if(uid<=0)throw new InvalidOperationException("Mecha insert failed");
            tx.Commit();
            return uid;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MechaInstanceRecord>> GetMechasAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(()=>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT mecha_uid,character_id,definition_id,level,experience,is_mounted,parts_blob
FROM mecha_instances WHERE character_id=$character ORDER BY mecha_uid;";
            command.Parameters.AddWithValue("$character",characterId);
            using var reader=command.ExecuteReader();
            var mechas=new List<MechaInstanceRecord>();
            while(reader.Read())
                mechas.Add(new(
                    reader.GetInt64(0),reader.GetInt64(1),reader.GetInt32(2),
                    reader.GetInt32(3),reader.GetInt64(4),reader.GetInt64(5)!=0,
                    (byte[])reader[6]));
            return (IReadOnlyList<MechaInstanceRecord>)mechas;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<MechaActionOutcome> ApplyMechaActionAsync(
        long characterId,long connectionGeneration,long mapEpoch,uint actionSequence,
        MechaActionKind action,long mechaUid,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||connectionGeneration<=0||mapEpoch<=0||actionSequence==0||
           action is not (MechaActionKind.Mount or MechaActionKind.Dismount)||mechaUid<=0)
            throw new ArgumentOutOfRangeException();

        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            using(var replay=connection.CreateCommand())
            {
                replay.Transaction=tx;
                replay.CommandText=@"SELECT action,mecha_uid,resulting_mounted_uid
FROM mecha_actions
WHERE character_id=$character AND connection_generation=$generation
AND map_epoch=$epoch AND action_sequence=$sequence;";
                replay.Parameters.AddWithValue("$character",characterId);
                replay.Parameters.AddWithValue("$generation",connectionGeneration);
                replay.Parameters.AddWithValue("$epoch",mapEpoch);
                replay.Parameters.AddWithValue("$sequence",(long)actionSequence);
                using var reader=replay.ExecuteReader();
                if(reader.Read())
                {
                    var oldAction=(MechaActionKind)reader.GetInt32(0);
                    long oldUid=reader.GetInt64(1);
                    long oldMounted=reader.GetInt64(2);
                    return oldAction==action&&oldUid==mechaUid
                        ?new MechaActionOutcome(MechaActionStatus.AlreadyProcessed,oldMounted)
                        :new MechaActionOutcome(MechaActionStatus.SequenceConflict,oldMounted);
                }
            }

            long mounted=ReadMountedMechaUid(connection,tx,characterId);
            bool owned;
            using(var ownedCommand=connection.CreateCommand())
            {
                ownedCommand.Transaction=tx;
                ownedCommand.CommandText=@"SELECT COUNT(*) FROM mecha_instances
WHERE mecha_uid=$mecha AND character_id=$character;";
                ownedCommand.Parameters.AddWithValue("$mecha",mechaUid);
                ownedCommand.Parameters.AddWithValue("$character",characterId);
                owned=Convert.ToInt64(ownedCommand.ExecuteScalar())==1;
            }

            MechaActionStatus status;
            if(!owned)
                status=MechaActionStatus.MechaNotOwned;
            else if(action==MechaActionKind.Mount&&mounted==mechaUid)
                status=MechaActionStatus.AlreadyMounted;
            else if(action==MechaActionKind.Dismount&&mounted!=mechaUid)
                status=MechaActionStatus.NotMounted;
            else
            {
                long now=_timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
                if(action==MechaActionKind.Mount)
                {
                    using var clear=connection.CreateCommand();
                    clear.Transaction=tx;
                    clear.CommandText=@"UPDATE mecha_instances SET is_mounted=0,updated_unix_ms=$now
WHERE character_id=$character AND is_mounted=1;";
                    clear.Parameters.AddWithValue("$now",now);
                    clear.Parameters.AddWithValue("$character",characterId);
                    clear.ExecuteNonQuery();

                    using var mount=connection.CreateCommand();
                    mount.Transaction=tx;
                    mount.CommandText=@"UPDATE mecha_instances SET is_mounted=1,updated_unix_ms=$now
WHERE character_id=$character AND mecha_uid=$mecha;";
                    mount.Parameters.AddWithValue("$now",now);
                    mount.Parameters.AddWithValue("$character",characterId);
                    mount.Parameters.AddWithValue("$mecha",mechaUid);
                    if(mount.ExecuteNonQuery()!=1)
                        throw new InvalidOperationException("Owned mecha disappeared during mount");
                    mounted=mechaUid;
                }
                else
                {
                    using var dismount=connection.CreateCommand();
                    dismount.Transaction=tx;
                    dismount.CommandText=@"UPDATE mecha_instances SET is_mounted=0,updated_unix_ms=$now
WHERE character_id=$character AND mecha_uid=$mecha AND is_mounted=1;";
                    dismount.Parameters.AddWithValue("$now",now);
                    dismount.Parameters.AddWithValue("$character",characterId);
                    dismount.Parameters.AddWithValue("$mecha",mechaUid);
                    if(dismount.ExecuteNonQuery()!=1)
                        throw new InvalidOperationException("Mounted mecha disappeared during dismount");
                    mounted=0;
                }
                status=MechaActionStatus.Applied;
            }

            using(var audit=connection.CreateCommand())
            {
                audit.Transaction=tx;
                audit.CommandText=@"INSERT INTO mecha_actions(
character_id,connection_generation,map_epoch,action_sequence,action,mecha_uid,
result_status,resulting_mounted_uid,created_unix_ms)
VALUES($character,$generation,$epoch,$sequence,$action,$mecha,$status,$mounted,$created);";
                audit.Parameters.AddWithValue("$character",characterId);
                audit.Parameters.AddWithValue("$generation",connectionGeneration);
                audit.Parameters.AddWithValue("$epoch",mapEpoch);
                audit.Parameters.AddWithValue("$sequence",(long)actionSequence);
                audit.Parameters.AddWithValue("$action",(int)action);
                audit.Parameters.AddWithValue("$mecha",mechaUid);
                audit.Parameters.AddWithValue("$status",(int)status);
                audit.Parameters.AddWithValue("$mounted",mounted);
                audit.Parameters.AddWithValue(
                    "$created",_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                audit.ExecuteNonQuery();
            }
            tx.Commit();
            return new MechaActionOutcome(status,mounted);
        },cancellationToken).ConfigureAwait(false);
    }

    private static long ReadMountedMechaUid(
        SqliteConnection connection,SqliteTransaction tx,long characterId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"SELECT mecha_uid FROM mecha_instances
WHERE character_id=$character AND is_mounted=1 LIMIT 1;";
        command.Parameters.AddWithValue("$character",characterId);
        object? value=command.ExecuteScalar();
        return value is null or DBNull?0:Convert.ToInt64(value);
    }
}
