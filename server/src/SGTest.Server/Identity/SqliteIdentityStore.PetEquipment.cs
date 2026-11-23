using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace SGTest.Server.Identity;

public readonly record struct PetEquipmentRule(
    int ItemDefinitionId,int SlotId,int RequiredLevel);

public static class PetEquipmentCatalog
{
    public static IReadOnlyDictionary<int,PetEquipmentRule> LoadVerified(string path)
    {
        if(string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Verified Pet equipment manifest path required",nameof(path));
        byte[] content=File.ReadAllBytes(path);
        if(content.Length is <1 or >1_048_576)
            throw new InvalidDataException("Pet equipment manifest size out of range");
        using var parsed=JsonDocument.Parse(content);
        var root=parsed.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           !root.TryGetProperty("kind",out var kind)||
           kind.GetString()!="verified_pet_equipment_rules"||
           !root.TryGetProperty("version",out var version)||version.GetInt32()!=1||
           !root.TryGetProperty("rules_verified",out var verified)||
           verified.ValueKind!=JsonValueKind.True||
           !root.TryGetProperty("gameplay_ready",out var ready)||
           ready.ValueKind!=JsonValueKind.True||
           !root.TryGetProperty("source_reference",out var source)||
           source.ValueKind!=JsonValueKind.String||
           string.IsNullOrWhiteSpace(source.GetString())||
           source.GetString()!.Length>256||
           !root.TryGetProperty("rules",out var rules)||
           rules.ValueKind!=JsonValueKind.Array||
           rules.GetArrayLength() is <1 or >4096)
            throw new InvalidDataException(
                "Pet equipment rules require operator-verified provenance");

        var result=new Dictionary<int,PetEquipmentRule>();
        foreach(var rule in rules.EnumerateArray())
        {
            if(rule.ValueKind!=JsonValueKind.Object||
               !rule.TryGetProperty("item_definition_id",out var item)||
               !rule.TryGetProperty("slot_id",out var slot)||
               !rule.TryGetProperty("required_level",out var requiredLevel)||
               !item.TryGetInt32(out int definition)||
               !slot.TryGetInt32(out int slotId)||
               !requiredLevel.TryGetInt32(out int level)||
               definition is <1 or >100_000_000||
               slotId is <1 or >16||
               level is <1 or >1_000_000||
               !result.TryAdd(definition,new(definition,slotId,level)))
                throw new InvalidDataException(
                    "Pet equipment rules contain invalid or duplicate definitions");
        }
        return result;
    }
}

public sealed record PetEquippedItem(
    long ItemUid,long CharacterId,long PetUid,int SlotId,int ItemDefinitionId,
    long SourceDropId,int EnhancementLevel,int? DurabilityRaw);

public sealed partial class SqliteIdentityStore
{
    public async Task<IReadOnlyList<PetEquippedItem>> ListPetEquippedItemsAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run<IReadOnlyList<PetEquippedItem>>(()=>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT item_uid,character_id,pet_uid,slot_id,
item_definition_id,COALESCE(source_drop_id,0),enhancement_level,durability_raw
FROM pet_equipped_items
WHERE character_id=$character
ORDER BY pet_uid,slot_id,item_uid;";
            command.Parameters.AddWithValue("$character",characterId);
            using var reader=command.ExecuteReader();
            var result=new List<PetEquippedItem>();
            while(reader.Read())
                result.Add(new(
                    reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),
                    reader.GetInt32(3),reader.GetInt32(4),reader.GetInt64(5),
                    reader.GetInt32(6),reader.IsDBNull(7)?null:reader.GetInt32(7)));
            return result;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<PetActionOutcome> ApplyPetEquipmentActionAsync(
        long characterId,long connectionGeneration,long mapEpoch,uint actionSequence,
        PetActionKind action,long petUid,long itemUid,
        IReadOnlyDictionary<int,PetEquipmentRule>? rules,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||connectionGeneration<=0||mapEpoch<=0||actionSequence==0||
           action is not (PetActionKind.Equip or PetActionKind.Unequip)||
           petUid<=0||itemUid<=0)
            throw new ArgumentOutOfRangeException();

        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            var replay=ReadPetEquipmentActionReceipt(
                connection,tx,characterId,connectionGeneration,mapEpoch,actionSequence);
            if(replay is not null)
            {
                bool exact=replay.Value.Action==action&&
                    replay.Value.PetUid==petUid&&replay.Value.ItemUid==itemUid;
                return exact
                    ?new PetActionOutcome(
                        PetActionStatus.AlreadyProcessed,replay.Value.ActivePetUid,
                        itemUid,replay.Value.SlotId,replay.Value.ReplacedItemUid)
                    :new PetActionOutcome(
                        PetActionStatus.SequenceConflict,replay.Value.ActivePetUid,itemUid);
            }

            int petLevel=0;
            bool owned=false;
            using(var pet=connection.CreateCommand())
            {
                pet.Transaction=tx;
                pet.CommandText=@"SELECT level FROM pet_instances
WHERE pet_uid=$pet AND character_id=$character;";
                pet.Parameters.AddWithValue("$pet",petUid);
                pet.Parameters.AddWithValue("$character",characterId);
                using var reader=pet.ExecuteReader();
                if(reader.Read())
                {
                    owned=true;
                    petLevel=reader.GetInt32(0);
                }
            }

            long activeUid=ReadActivePetUid(connection,tx,characterId);
            PetActionStatus status;
            int slotId=0;
            long replacedItemUid=0;

            if(!owned)
                status=PetActionStatus.PetNotOwned;
            else if(action==PetActionKind.Equip)
            {
                if(rules is null)
                    status=PetActionStatus.RulesUnavailable;
                else
                {
                    PortableMechaItem? item=ReadInventoryItem(
                        connection,tx,characterId,itemUid);
                    if(item is null||item.Quantity!=1)
                        status=PetActionStatus.ItemNotOwned;
                    else if(!rules.TryGetValue(item.ItemDefinitionId,out var rule))
                        status=PetActionStatus.InvalidEquipment;
                    else if(petLevel<rule.RequiredLevel)
                        status=PetActionStatus.LevelTooLow;
                    else
                    {
                        slotId=rule.SlotId;
                        PortableMechaItem? displaced=ReadPetEquippedItemAtSlot(
                            connection,tx,characterId,petUid,slotId);
                        if(displaced is not null)
                        {
                            replacedItemUid=displaced.ItemUid;
                            DeletePetEquippedItem(connection,tx,displaced.ItemUid);
                        }

                        DeleteInventoryItem(connection,tx,item.ItemUid);
                        if(displaced is not null)
                            InsertInventoryItem(connection,tx,displaced);
                        InsertPetEquippedItem(connection,tx,item,petUid,slotId);
                        status=PetActionStatus.Applied;
                    }
                }
            }
            else
            {
                PortableMechaItem? equipped=ReadPetEquippedItem(
                    connection,tx,characterId,petUid,itemUid,out slotId);
                if(equipped is null)
                    status=PetActionStatus.NotEquipped;
                else
                {
                    DeletePetEquippedItem(connection,tx,itemUid);
                    InsertInventoryItem(connection,tx,equipped);
                    status=PetActionStatus.Applied;
                }
            }

            InsertPetEquipmentActionReceipt(
                connection,tx,characterId,connectionGeneration,mapEpoch,actionSequence,
                action,petUid,itemUid,status,activeUid,slotId,replacedItemUid);
            tx.Commit();
            return new PetActionOutcome(
                status,activeUid,itemUid,slotId,replacedItemUid);
        },cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct PetEquipmentActionReceipt(
        PetActionKind Action,long PetUid,long ItemUid,PetActionStatus Status,
        long ActivePetUid,int SlotId,long ReplacedItemUid);

    private static PetEquipmentActionReceipt? ReadPetEquipmentActionReceipt(
        SqliteConnection connection,SqliteTransaction tx,long characterId,
        long connectionGeneration,long mapEpoch,uint actionSequence)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"SELECT action,pet_uid,item_uid,result_status,
resulting_active_uid,resulting_slot_id,replaced_item_uid
FROM pet_actions
WHERE character_id=$character AND connection_generation=$generation
AND map_epoch=$epoch AND action_sequence=$sequence;";
        command.Parameters.AddWithValue("$character",characterId);
        command.Parameters.AddWithValue("$generation",connectionGeneration);
        command.Parameters.AddWithValue("$epoch",mapEpoch);
        command.Parameters.AddWithValue("$sequence",(long)actionSequence);
        using var reader=command.ExecuteReader();
        if(!reader.Read())return null;
        return new(
            (PetActionKind)reader.GetInt32(0),reader.GetInt64(1),reader.GetInt64(2),
            (PetActionStatus)reader.GetInt32(3),reader.GetInt64(4),
            reader.GetInt32(5),reader.GetInt64(6));
    }

    private void InsertPetEquipmentActionReceipt(
        SqliteConnection connection,SqliteTransaction tx,long characterId,
        long connectionGeneration,long mapEpoch,uint actionSequence,
        PetActionKind action,long petUid,long itemUid,PetActionStatus status,
        long activeUid,int slotId,long replacedItemUid)
    {
        using var audit=connection.CreateCommand();
        audit.Transaction=tx;
        audit.CommandText=@"INSERT INTO pet_actions(
character_id,connection_generation,map_epoch,action_sequence,action,pet_uid,item_uid,
result_status,resulting_active_uid,resulting_slot_id,replaced_item_uid,created_unix_ms)
VALUES($character,$generation,$epoch,$sequence,$action,$pet,$item,$status,
$active,$slot,$replaced,$created);";
        audit.Parameters.AddWithValue("$character",characterId);
        audit.Parameters.AddWithValue("$generation",connectionGeneration);
        audit.Parameters.AddWithValue("$epoch",mapEpoch);
        audit.Parameters.AddWithValue("$sequence",(long)actionSequence);
        audit.Parameters.AddWithValue("$action",(int)action);
        audit.Parameters.AddWithValue("$pet",petUid);
        audit.Parameters.AddWithValue("$item",itemUid);
        audit.Parameters.AddWithValue("$status",(int)status);
        audit.Parameters.AddWithValue("$active",activeUid);
        audit.Parameters.AddWithValue("$slot",slotId);
        audit.Parameters.AddWithValue("$replaced",replacedItemUid);
        audit.Parameters.AddWithValue(
            "$created",_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        audit.ExecuteNonQuery();
    }

    private static PortableMechaItem? ReadPetEquippedItemAtSlot(
        SqliteConnection connection,SqliteTransaction tx,long characterId,long petUid,int slotId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"SELECT item_uid,character_id,item_definition_id,quantity,
source_drop_id,durability_raw,enhancement_level,source_kind,source_reference,created_unix_ms
FROM pet_equipped_items
WHERE character_id=$character AND pet_uid=$pet AND slot_id=$slot;";
        command.Parameters.AddWithValue("$character",characterId);
        command.Parameters.AddWithValue("$pet",petUid);
        command.Parameters.AddWithValue("$slot",slotId);
        using var reader=command.ExecuteReader();
        return reader.Read()?ReadPortableItem(reader):null;
    }

    private static PortableMechaItem? ReadPetEquippedItem(
        SqliteConnection connection,SqliteTransaction tx,long characterId,long petUid,
        long itemUid,out int slotId)
    {
        slotId=0;
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"SELECT item_uid,character_id,item_definition_id,quantity,
source_drop_id,durability_raw,enhancement_level,source_kind,source_reference,created_unix_ms,slot_id
FROM pet_equipped_items
WHERE character_id=$character AND pet_uid=$pet AND item_uid=$item;";
        command.Parameters.AddWithValue("$character",characterId);
        command.Parameters.AddWithValue("$pet",petUid);
        command.Parameters.AddWithValue("$item",itemUid);
        using var reader=command.ExecuteReader();
        if(!reader.Read())return null;
        var result=ReadPortableItem(reader);
        slotId=reader.GetInt32(10);
        return result;
    }

    private static void DeletePetEquippedItem(
        SqliteConnection connection,SqliteTransaction tx,long itemUid)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText="DELETE FROM pet_equipped_items WHERE item_uid=$item;";
        command.Parameters.AddWithValue("$item",itemUid);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("Pet equipped item disappeared");
    }

    private static void InsertPetEquippedItem(
        SqliteConnection connection,SqliteTransaction tx,PortableMechaItem item,
        long petUid,int slotId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"INSERT INTO pet_equipped_items(
item_uid,character_id,pet_uid,slot_id,item_definition_id,quantity,source_drop_id,
durability_raw,enhancement_level,source_kind,source_reference,created_unix_ms)
VALUES($item,$character,$pet,$slot,$definition,$quantity,$drop,
$durability,$enhancement,$source_kind,$reference,$created);";
        BindPortableItem(command,item);
        command.Parameters.AddWithValue("$pet",petUid);
        command.Parameters.AddWithValue("$slot",slotId);
        command.ExecuteNonQuery();
    }
}
