using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace SGTest.Server.Identity;

public readonly record struct MechaEquipmentRule(
    int ItemDefinitionId,int EquipmentClass,int RequiredLevel);

public static class MechaEquipmentCatalog
{
    public static IReadOnlyDictionary<int,MechaEquipmentRule> LoadVerified(string path)
    {
        if(string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Verified Mecha equipment manifest path required",nameof(path));
        byte[] content=File.ReadAllBytes(path);
        if(content.Length is <1 or >1_048_576)
            throw new InvalidDataException("Mecha equipment manifest size out of range");
        using var parsed=JsonDocument.Parse(content);
        var root=parsed.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           !root.TryGetProperty("kind",out var kind)||
           kind.GetString()!="verified_mecha_equipment_rules"||
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
                "Mecha equipment rules require operator-verified provenance");

        var result=new Dictionary<int,MechaEquipmentRule>();
        foreach(var rule in rules.EnumerateArray())
        {
            if(rule.ValueKind!=JsonValueKind.Object||
               !rule.TryGetProperty("item_definition_id",out var item)||
               !rule.TryGetProperty("equipment_class",out var equipmentClass)||
               !rule.TryGetProperty("required_level",out var requiredLevel)||
               !item.TryGetInt32(out int definition)||
               !equipmentClass.TryGetInt32(out int equipmentClassId)||
               !requiredLevel.TryGetInt32(out int level)||
               definition is <1 or >100_000_000||
               equipmentClassId is not (1 or 2 or 3 or 6)||
               level is <1 or >1_000_000||
               !result.TryAdd(definition,new(definition,equipmentClassId,level)))
                throw new InvalidDataException(
                    "Mecha equipment rules contain invalid or duplicate definitions");
        }
        return result;
    }
}

public sealed record MechaEquippedItem(
    long ItemUid,long CharacterId,long MechaUid,int SlotId,int ItemDefinitionId,
    long SourceDropId,int EnhancementLevel,int? DurabilityRaw);

public sealed partial class SqliteIdentityStore
{
    private sealed record PortableMechaItem(
        long ItemUid,long CharacterId,int ItemDefinitionId,int Quantity,
        long? SourceDropId,int? DurabilityRaw,int EnhancementLevel,int SourceKind,
        string SourceReference,long CreatedUnixMs);

    public async Task<IReadOnlyList<MechaEquippedItem>> ListMechaEquippedItemsAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run<IReadOnlyList<MechaEquippedItem>>(()=>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT item_uid,character_id,mecha_uid,slot_id,
item_definition_id,COALESCE(source_drop_id,0),enhancement_level,durability_raw
FROM mecha_equipped_items
WHERE character_id=$character
ORDER BY mecha_uid,slot_id,item_uid;";
            command.Parameters.AddWithValue("$character",characterId);
            using var reader=command.ExecuteReader();
            var result=new List<MechaEquippedItem>();
            while(reader.Read())
                result.Add(new(
                    reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),
                    reader.GetInt32(3),reader.GetInt32(4),reader.GetInt64(5),
                    reader.GetInt32(6),reader.IsDBNull(7)?null:reader.GetInt32(7)));
            return result;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<MechaActionOutcome> ApplyMechaEquipmentActionAsync(
        long characterId,long connectionGeneration,long mapEpoch,uint actionSequence,
        MechaActionKind action,long mechaUid,long itemUid,
        IReadOnlyDictionary<int,MechaEquipmentRule>? rules,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||connectionGeneration<=0||mapEpoch<=0||actionSequence==0||
           action is not (MechaActionKind.Equip or MechaActionKind.Unequip)||
           mechaUid<=0||itemUid<=0)
            throw new ArgumentOutOfRangeException();

        return await Task.Run(()=>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            var replay=ReadMechaActionReceipt(
                connection,tx,characterId,connectionGeneration,mapEpoch,actionSequence);
            if(replay is not null)
            {
                bool exact=replay.Value.Action==action&&
                    replay.Value.MechaUid==mechaUid&&replay.Value.ItemUid==itemUid;
                return exact
                    ?new MechaActionOutcome(
                        MechaActionStatus.AlreadyProcessed,
                        replay.Value.MountedMechaUid,itemUid,
                        replay.Value.SlotId,replay.Value.ReplacedItemUid)
                    :new MechaActionOutcome(
                        MechaActionStatus.SequenceConflict,
                        replay.Value.MountedMechaUid,itemUid);
            }

            int mechaLevel=0;
            bool mounted=false;
            bool owned=false;
            using(var mecha=connection.CreateCommand())
            {
                mecha.Transaction=tx;
                mecha.CommandText=@"SELECT level,is_mounted
FROM mecha_instances WHERE mecha_uid=$mecha AND character_id=$character;";
                mecha.Parameters.AddWithValue("$mecha",mechaUid);
                mecha.Parameters.AddWithValue("$character",characterId);
                using var reader=mecha.ExecuteReader();
                if(reader.Read())
                {
                    owned=true;
                    mechaLevel=reader.GetInt32(0);
                    mounted=reader.GetInt64(1)!=0;
                }
            }

            long mountedUid=ReadMountedMechaUid(connection,tx,characterId);
            MechaActionStatus status;
            int slotId=0;
            long replacedItemUid=0;

            if(!owned)
                status=MechaActionStatus.MechaNotOwned;
            else if(mounted)
                status=MechaActionStatus.MountedLocked;
            else if(action==MechaActionKind.Equip)
            {
                if(rules is null)
                    status=MechaActionStatus.RulesUnavailable;
                else
                {
                    PortableMechaItem? item=ReadInventoryItem(
                        connection,tx,characterId,itemUid);
                    if(item is null||item.Quantity!=1)
                        status=MechaActionStatus.ItemNotOwned;
                    else if(!rules.TryGetValue(item.ItemDefinitionId,out var rule))
                        status=MechaActionStatus.InvalidEquipment;
                    else if(mechaLevel<rule.RequiredLevel)
                        status=MechaActionStatus.LevelTooLow;
                    else
                    {
                        slotId=ResolveMechaEquipmentSlot(
                            connection,tx,mechaUid,rule.EquipmentClass);
                        if(slotId==0)
                            status=MechaActionStatus.CardSlotsFull;
                        else
                        {
                            PortableMechaItem? displaced=ReadMechaEquippedItemAtSlot(
                                connection,tx,characterId,mechaUid,slotId);
                            if(displaced is not null)
                            {
                                replacedItemUid=displaced.ItemUid;
                                DeleteMechaEquippedItem(connection,tx,displaced.ItemUid);
                            }

                            DeleteInventoryItem(connection,tx,item.ItemUid);
                            if(displaced is not null)
                                InsertInventoryItem(connection,tx,displaced);
                            InsertMechaEquippedItem(
                                connection,tx,item,mechaUid,slotId);
                            status=MechaActionStatus.Applied;
                        }
                    }
                }
            }
            else
            {
                PortableMechaItem? equipped=ReadMechaEquippedItem(
                    connection,tx,characterId,mechaUid,itemUid,out slotId);
                if(equipped is null)
                    status=MechaActionStatus.NotEquipped;
                else if(slotId is 1 or 2)
                    status=MechaActionStatus.CorePartCannotUnequip;
                else
                {
                    DeleteMechaEquippedItem(connection,tx,itemUid);
                    InsertInventoryItem(connection,tx,equipped);
                    status=MechaActionStatus.Applied;
                }
            }

            InsertMechaActionReceipt(
                connection,tx,characterId,connectionGeneration,mapEpoch,
                actionSequence,action,mechaUid,itemUid,status,mountedUid,
                slotId,replacedItemUid);
            tx.Commit();
            return new MechaActionOutcome(
                status,mountedUid,itemUid,slotId,replacedItemUid);
        },cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct MechaActionReceipt(
        MechaActionKind Action,long MechaUid,long ItemUid,
        MechaActionStatus Status,long MountedMechaUid,int SlotId,long ReplacedItemUid);

    private static MechaActionReceipt? ReadMechaActionReceipt(
        SqliteConnection connection,SqliteTransaction tx,long characterId,
        long connectionGeneration,long mapEpoch,uint actionSequence)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"SELECT action,mecha_uid,item_uid,result_status,
resulting_mounted_uid,resulting_slot_id,replaced_item_uid
FROM mecha_actions
WHERE character_id=$character AND connection_generation=$generation
AND map_epoch=$epoch AND action_sequence=$sequence;";
        command.Parameters.AddWithValue("$character",characterId);
        command.Parameters.AddWithValue("$generation",connectionGeneration);
        command.Parameters.AddWithValue("$epoch",mapEpoch);
        command.Parameters.AddWithValue("$sequence",(long)actionSequence);
        using var reader=command.ExecuteReader();
        if(!reader.Read())return null;
        return new(
            (MechaActionKind)reader.GetInt32(0),reader.GetInt64(1),reader.GetInt64(2),
            (MechaActionStatus)reader.GetInt32(3),reader.GetInt64(4),
            reader.GetInt32(5),reader.GetInt64(6));
    }

    private void InsertMechaActionReceipt(
        SqliteConnection connection,SqliteTransaction tx,long characterId,
        long connectionGeneration,long mapEpoch,uint actionSequence,
        MechaActionKind action,long mechaUid,long itemUid,MechaActionStatus status,
        long mountedUid,int slotId,long replacedItemUid)
    {
        using var audit=connection.CreateCommand();
        audit.Transaction=tx;
        audit.CommandText=@"INSERT INTO mecha_actions(
character_id,connection_generation,map_epoch,action_sequence,action,mecha_uid,item_uid,
result_status,resulting_mounted_uid,resulting_slot_id,replaced_item_uid,created_unix_ms)
VALUES($character,$generation,$epoch,$sequence,$action,$mecha,$item,$status,
$mounted,$slot,$replaced,$created);";
        audit.Parameters.AddWithValue("$character",characterId);
        audit.Parameters.AddWithValue("$generation",connectionGeneration);
        audit.Parameters.AddWithValue("$epoch",mapEpoch);
        audit.Parameters.AddWithValue("$sequence",(long)actionSequence);
        audit.Parameters.AddWithValue("$action",(int)action);
        audit.Parameters.AddWithValue("$mecha",mechaUid);
        audit.Parameters.AddWithValue("$item",itemUid);
        audit.Parameters.AddWithValue("$status",(int)status);
        audit.Parameters.AddWithValue("$mounted",mountedUid);
        audit.Parameters.AddWithValue("$slot",slotId);
        audit.Parameters.AddWithValue("$replaced",replacedItemUid);
        audit.Parameters.AddWithValue(
            "$created",_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        audit.ExecuteNonQuery();
    }

    private static PortableMechaItem? ReadInventoryItem(
        SqliteConnection connection,SqliteTransaction tx,long characterId,long itemUid)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"SELECT item_uid,character_id,item_definition_id,quantity,
source_drop_id,durability_raw,enhancement_level,source_kind,source_reference,created_unix_ms
FROM item_instances WHERE item_uid=$item AND character_id=$character;";
        command.Parameters.AddWithValue("$item",itemUid);
        command.Parameters.AddWithValue("$character",characterId);
        using var reader=command.ExecuteReader();
        return reader.Read()?ReadPortableItem(reader):null;
    }

    private static PortableMechaItem? ReadMechaEquippedItemAtSlot(
        SqliteConnection connection,SqliteTransaction tx,long characterId,long mechaUid,int slotId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"SELECT item_uid,character_id,item_definition_id,quantity,
source_drop_id,durability_raw,enhancement_level,source_kind,source_reference,created_unix_ms
FROM mecha_equipped_items
WHERE character_id=$character AND mecha_uid=$mecha AND slot_id=$slot;";
        command.Parameters.AddWithValue("$character",characterId);
        command.Parameters.AddWithValue("$mecha",mechaUid);
        command.Parameters.AddWithValue("$slot",slotId);
        using var reader=command.ExecuteReader();
        return reader.Read()?ReadPortableItem(reader):null;
    }

    private static PortableMechaItem? ReadMechaEquippedItem(
        SqliteConnection connection,SqliteTransaction tx,long characterId,long mechaUid,
        long itemUid,out int slotId)
    {
        slotId=0;
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"SELECT item_uid,character_id,item_definition_id,quantity,
source_drop_id,durability_raw,enhancement_level,source_kind,source_reference,created_unix_ms,slot_id
FROM mecha_equipped_items
WHERE character_id=$character AND mecha_uid=$mecha AND item_uid=$item;";
        command.Parameters.AddWithValue("$character",characterId);
        command.Parameters.AddWithValue("$mecha",mechaUid);
        command.Parameters.AddWithValue("$item",itemUid);
        using var reader=command.ExecuteReader();
        if(!reader.Read())return null;
        var result=ReadPortableItem(reader);
        slotId=reader.GetInt32(10);
        return result;
    }

    private static PortableMechaItem ReadPortableItem(SqliteDataReader reader)=>new(
        reader.GetInt64(0),reader.GetInt64(1),reader.GetInt32(2),reader.GetInt32(3),
        reader.IsDBNull(4)?null:reader.GetInt64(4),
        reader.IsDBNull(5)?null:reader.GetInt32(5),
        reader.GetInt32(6),reader.GetInt32(7),reader.GetString(8),reader.GetInt64(9));

    private static int ResolveMechaEquipmentSlot(
        SqliteConnection connection,SqliteTransaction tx,long mechaUid,int equipmentClass)
    {
        if(equipmentClass is 1 or 2 or 6)return equipmentClass;
        if(equipmentClass!=3)return 0;
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"SELECT slot_id FROM mecha_equipped_items
WHERE mecha_uid=$mecha AND slot_id BETWEEN 3 AND 5 ORDER BY slot_id;";
        command.Parameters.AddWithValue("$mecha",mechaUid);
        using var reader=command.ExecuteReader();
        bool[] used=new bool[3];
        while(reader.Read())
        {
            int slot=reader.GetInt32(0);
            if(slot is >=3 and <=5)used[slot-3]=true;
        }
        for(int i=0;i<used.Length;i++)
            if(!used[i])return i+3;
        return 0;
    }

    private static void DeleteInventoryItem(
        SqliteConnection connection,SqliteTransaction tx,long itemUid)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText="DELETE FROM item_instances WHERE item_uid=$item;";
        command.Parameters.AddWithValue("$item",itemUid);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("Mecha equipment inventory item disappeared");
    }

    private static void DeleteMechaEquippedItem(
        SqliteConnection connection,SqliteTransaction tx,long itemUid)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText="DELETE FROM mecha_equipped_items WHERE item_uid=$item;";
        command.Parameters.AddWithValue("$item",itemUid);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("Mecha equipped item disappeared");
    }

    private static void InsertInventoryItem(
        SqliteConnection connection,SqliteTransaction tx,PortableMechaItem item)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"INSERT INTO item_instances(
item_uid,character_id,item_definition_id,quantity,source_drop_id,durability_raw,
enhancement_level,source_kind,source_reference,created_unix_ms)
VALUES($item,$character,$definition,$quantity,$drop,$durability,
$enhancement,$source_kind,$reference,$created);";
        BindPortableItem(command,item);
        command.ExecuteNonQuery();
    }

    private static void InsertMechaEquippedItem(
        SqliteConnection connection,SqliteTransaction tx,PortableMechaItem item,
        long mechaUid,int slotId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"INSERT INTO mecha_equipped_items(
item_uid,character_id,mecha_uid,slot_id,item_definition_id,quantity,source_drop_id,
durability_raw,enhancement_level,source_kind,source_reference,created_unix_ms)
VALUES($item,$character,$mecha,$slot,$definition,$quantity,$drop,
$durability,$enhancement,$source_kind,$reference,$created);";
        BindPortableItem(command,item);
        command.Parameters.AddWithValue("$mecha",mechaUid);
        command.Parameters.AddWithValue("$slot",slotId);
        command.ExecuteNonQuery();
    }

    private static void BindPortableItem(SqliteCommand command,PortableMechaItem item)
    {
        command.Parameters.AddWithValue("$item",item.ItemUid);
        command.Parameters.AddWithValue("$character",item.CharacterId);
        command.Parameters.AddWithValue("$definition",item.ItemDefinitionId);
        command.Parameters.AddWithValue("$quantity",item.Quantity);
        command.Parameters.AddWithValue(
            "$drop",item.SourceDropId is null?DBNull.Value:item.SourceDropId.Value);
        command.Parameters.AddWithValue(
            "$durability",item.DurabilityRaw is null?DBNull.Value:item.DurabilityRaw.Value);
        command.Parameters.AddWithValue("$enhancement",item.EnhancementLevel);
        command.Parameters.AddWithValue("$source_kind",item.SourceKind);
        command.Parameters.AddWithValue("$reference",item.SourceReference);
        command.Parameters.AddWithValue("$created",item.CreatedUnixMs);
    }
}
