using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace SGTest.Server.Identity;

public readonly record struct EquipmentRule(int ItemDefinitionId,int SlotId);

public static class EquipmentItemCatalog
{
    public static IReadOnlyDictionary<int,EquipmentRule> LoadVerified(string path)
    {
        if(string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Verified equipment rule manifest path required",nameof(path));
        var content=File.ReadAllBytes(path);
        if(content.Length is <1 or >1_048_576)
            throw new InvalidDataException("Equipment manifest size out of range");
        using var parsed=JsonDocument.Parse(content);
        var root=parsed.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           !root.TryGetProperty("kind",out var kind)||
           kind.GetString()!="verified_equipment_item_rules"||
           !root.TryGetProperty("version",out var version)||
           version.GetInt32()!=1||
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
                "Equipment rules require operator-verified provenance");
        var result=new Dictionary<int,EquipmentRule>();
        foreach(var rule in rules.EnumerateArray())
        {
            if(rule.ValueKind!=JsonValueKind.Object||
               !rule.TryGetProperty("item_definition_id",out var item)||
               !rule.TryGetProperty("slot_id",out var slot)||
               !item.TryGetInt32(out int definition)||
               !slot.TryGetInt32(out int slotId)||
               definition is <1 or >100_000_000||
               slotId is <1 or >255||
               !result.TryAdd(definition,new(definition,slotId)))
                throw new InvalidDataException(
                    "Equipment rules contain invalid or duplicate item definitions");
        }
        return result;
    }
}

public enum EquipmentTransferKind : byte { Equip = 1, Unequip = 2 }

public enum EquipmentTransferStatus : byte
{
    Applied = 0,
    AlreadyApplied = 1,
    NotOwned = 2,
    NotEquipped = 3,
    SlotOccupied = 4,
    InvalidRule = 5,
    StaleSequence = 6,
    NoRules = 7,
    StaleGeneration = 8
}

/// <summary>
/// Built by an authenticated server boundary. Equip rule fields come from a
/// verified item-definition manifest, never from raw client input.
/// </summary>
public readonly record struct TrustedEquipmentContext(
    long CharacterId,long ConnectionGeneration,long MapEpoch,uint ActionSequence);

public sealed record EquipmentTransferResult(
    EquipmentTransferStatus Status,EquipmentTransferKind Kind,
    long ItemUid,int ItemDefinitionId,int SlotId,long SourceDropId);

public sealed record EquippedItem(
    long ItemUid,long CharacterId,int SlotId,int ItemDefinitionId,long SourceDropId,
    int EnhancementLevel = 0,int? DurabilityRaw = null);

public sealed partial class SqliteIdentityStore
{
    public Task<EquipmentTransferResult> EquipInventoryItemAsync(
        TrustedEquipmentContext context,long itemUid,
        IReadOnlyDictionary<int,EquipmentRule> rules,
        CancellationToken cancellationToken=default) =>
        ExecuteEquipmentTransferAsync(
            context,EquipmentTransferKind.Equip,itemUid,rules,cancellationToken);

    public Task<EquipmentTransferResult> UnequipItemAsync(
        TrustedEquipmentContext context,long itemUid,
        CancellationToken cancellationToken=default) =>
        ExecuteEquipmentTransferAsync(
            context,EquipmentTransferKind.Unequip,itemUid,null,cancellationToken);

    public async Task<IReadOnlyList<EquippedItem>> ListEquippedItemsAsync(
        long characterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run<IReadOnlyList<EquippedItem>>(() =>
        {
            using var connection=Open();
            using var command=connection.CreateCommand();
            command.CommandText=@"SELECT item_uid,character_id,slot_id,
item_definition_id,COALESCE(source_drop_id,0),enhancement_level,durability_raw
FROM equipped_items WHERE character_id=$actor ORDER BY slot_id,item_uid;";
            command.Parameters.AddWithValue("$actor",characterId);
            using var reader=command.ExecuteReader();
            var result=new List<EquippedItem>();
            while(reader.Read())
                result.Add(new(reader.GetInt64(0),reader.GetInt64(1),
                    reader.GetInt32(2),reader.GetInt32(3),reader.GetInt64(4),
                    reader.GetInt32(5),
                    reader.IsDBNull(6)?null:reader.GetInt32(6)));
            return result;
        },cancellationToken).ConfigureAwait(false);
    }

    private async Task<EquipmentTransferResult> ExecuteEquipmentTransferAsync(
        TrustedEquipmentContext context,EquipmentTransferKind kind,long itemUid,
        IReadOnlyDictionary<int,EquipmentRule>? rules,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateEquipmentRequest(context,kind,itemUid);
        if(kind==EquipmentTransferKind.Equip)
            ArgumentNullException.ThrowIfNull(rules);
        return await Task.Run<EquipmentTransferResult>(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            using(var replay=connection.CreateCommand())
            {
                replay.Transaction=tx;
                replay.CommandText=@"
SELECT action_kind,item_uid,item_definition_id,slot_id,
       COALESCE(source_drop_id,0)
FROM equipment_actions
WHERE character_id=$actor AND connection_generation=$conn
  AND map_epoch=$epoch AND action_sequence=$seq;";
                replay.Parameters.AddWithValue("$actor",context.CharacterId);
                replay.Parameters.AddWithValue("$conn",context.ConnectionGeneration);
                replay.Parameters.AddWithValue("$epoch",context.MapEpoch);
                replay.Parameters.AddWithValue("$seq",(long)context.ActionSequence);
                using var reader=replay.ExecuteReader();
                if(reader.Read())
                {
                    var originalKind=(EquipmentTransferKind)reader.GetInt32(0);
                    long originalUid=reader.GetInt64(1);
                    int originalDefinition=reader.GetInt32(2);
                    int originalSlot=reader.GetInt32(3);
                    long originalDrop=reader.GetInt64(4);
                    bool exact=originalKind==kind&&originalUid==itemUid;
                    return exact
                        ? new(EquipmentTransferStatus.AlreadyApplied,kind,
                            originalUid,originalDefinition,originalSlot,originalDrop)
                        : new(EquipmentTransferStatus.StaleSequence,kind,
                            itemUid,0,0,0);
                }
            }

            long last=ScalarLong(connection,@"
SELECT COALESCE(MAX(action_sequence),0) FROM equipment_actions
WHERE character_id=$actor AND connection_generation=$conn AND map_epoch=$epoch;",tx,
                ("$actor",context.CharacterId),
                ("$conn",context.ConnectionGeneration),
                ("$epoch",context.MapEpoch));
            if(last>=context.ActionSequence)
                return new(EquipmentTransferStatus.StaleSequence,kind,itemUid,0,0,0);

            if(kind==EquipmentTransferKind.Equip)
            {
                var row=ReadEquipmentMoveItem(
                    connection,tx,"item_instances",context.CharacterId,itemUid);
                if(row is null)
                    return new(EquipmentTransferStatus.NotOwned,kind,itemUid,0,0,0);
                if(row.Quantity!=1||
                   !rules!.TryGetValue(row.ItemDefinitionId,out var rule))
                    return new(EquipmentTransferStatus.InvalidRule,kind,itemUid,0,0,0);
                int trustedSlotId=rule.SlotId;
                if(ScalarLong(connection,@"
SELECT COUNT(*) FROM equipped_items
WHERE character_id=$actor AND slot_id=$slot;",tx,
                    ("$actor",context.CharacterId),("$slot",trustedSlotId))!=0)
                    return new(EquipmentTransferStatus.SlotOccupied,kind,itemUid,0,0,0);

                MoveEquipmentRow(connection,tx,row,"item_instances","equipped_items",
                    trustedSlotId,context.CharacterId);
                InsertEquipmentAction(connection,tx,context,kind,row.ItemUid,
                    row.ItemDefinitionId,trustedSlotId,row.SourceDropId);
                tx.Commit();
                return new(EquipmentTransferStatus.Applied,kind,row.ItemUid,
                    row.ItemDefinitionId,trustedSlotId,row.SourceDropId??0);
            }
            else
            {
                var row=ReadEquipmentMoveItem(
                    connection,tx,"equipped_items",context.CharacterId,itemUid);
                if(row is null)
                    return new(EquipmentTransferStatus.NotEquipped,kind,itemUid,0,0,0);
                int slot=row.SlotId;
                MoveEquipmentRow(connection,tx,row,"equipped_items","item_instances",
                    0,context.CharacterId);
                InsertEquipmentAction(connection,tx,context,kind,row.ItemUid,
                    row.ItemDefinitionId,slot,row.SourceDropId);
                tx.Commit();
                return new(EquipmentTransferStatus.Applied,kind,row.ItemUid,
                    row.ItemDefinitionId,slot,row.SourceDropId??0);
            }
        },cancellationToken).ConfigureAwait(false);
    }

    private static EquipmentMoveItem? ReadEquipmentMoveItem(
        SqliteConnection connection,SqliteTransaction tx,string table,
        long characterId,long itemUid)
    {
        if(table is not ("item_instances" or "equipped_items"))
            throw new ArgumentOutOfRangeException(nameof(table));
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=table=="equipped_items"
            ? @"SELECT item_uid,character_id,item_definition_id,quantity,
source_drop_id,durability_raw,enhancement_level,source_kind,source_reference,created_unix_ms,slot_id
FROM equipped_items WHERE item_uid=$uid AND character_id=$actor;"
            : @"SELECT item_uid,character_id,item_definition_id,quantity,
source_drop_id,durability_raw,enhancement_level,source_kind,source_reference,created_unix_ms,0
FROM item_instances WHERE item_uid=$uid AND character_id=$actor;";
        command.Parameters.AddWithValue("$uid",itemUid);
        command.Parameters.AddWithValue("$actor",characterId);
        using var reader=command.ExecuteReader();
        if(!reader.Read())return null;
        return new(
            reader.GetInt64(0),reader.GetInt64(1),reader.GetInt32(2),
            reader.GetInt32(3),reader.IsDBNull(4)?null:reader.GetInt64(4),
            reader.IsDBNull(5)?null:reader.GetInt32(5),
            reader.GetInt32(6),reader.GetInt32(7),reader.GetString(8),
            reader.GetInt64(9),reader.GetInt32(10));
    }

    private static void MoveEquipmentRow(
        SqliteConnection connection,SqliteTransaction tx,EquipmentMoveItem row,
        string sourceTable,string destinationTable,int destinationSlot,long characterId)
    {
        using(var delete=connection.CreateCommand())
        {
            delete.Transaction=tx;
            delete.CommandText=$"DELETE FROM {sourceTable} WHERE item_uid=$uid AND character_id=$actor;";
            delete.Parameters.AddWithValue("$uid",row.ItemUid);
            delete.Parameters.AddWithValue("$actor",characterId);
            if(delete.ExecuteNonQuery()!=1)
                throw new InvalidOperationException("Equipment source item changed during transfer");
        }
        using(var insert=connection.CreateCommand())
        {
            insert.Transaction=tx;
            if(destinationTable=="equipped_items")
            {
                insert.CommandText=@"
INSERT INTO equipped_items(
    item_uid,character_id,slot_id,item_definition_id,quantity,source_drop_id,durability_raw,
    enhancement_level,source_kind,source_reference,created_unix_ms)
VALUES($uid,$actor,$slot,$item,$quantity,$drop,$durability,$enhancement,$source_kind,$source,$created);";
                insert.Parameters.AddWithValue("$slot",destinationSlot);
            }
            else if(destinationTable=="item_instances")
            {
                insert.CommandText=@"
INSERT INTO item_instances(
    item_uid,character_id,item_definition_id,quantity,source_drop_id,durability_raw,
    enhancement_level,source_kind,source_reference,created_unix_ms)
VALUES($uid,$actor,$item,$quantity,$drop,$durability,$enhancement,$source_kind,$source,$created);";
            }
            else throw new ArgumentOutOfRangeException(nameof(destinationTable));

            insert.Parameters.AddWithValue("$uid",row.ItemUid);
            insert.Parameters.AddWithValue("$actor",row.CharacterId);
            insert.Parameters.AddWithValue("$item",row.ItemDefinitionId);
            insert.Parameters.AddWithValue("$quantity",row.Quantity);
            insert.Parameters.AddWithValue(
                "$drop",row.SourceDropId is null?DBNull.Value:row.SourceDropId.Value);
            insert.Parameters.AddWithValue(
                "$durability",row.DurabilityRaw is null?DBNull.Value:row.DurabilityRaw.Value);
            insert.Parameters.AddWithValue("$enhancement",row.EnhancementLevel);
            insert.Parameters.AddWithValue("$source_kind",row.SourceKind);
            insert.Parameters.AddWithValue("$source",row.SourceReference);
            insert.Parameters.AddWithValue("$created",row.CreatedUnixMilliseconds);
            if(insert.ExecuteNonQuery()!=1)
                throw new InvalidOperationException("Equipment destination insert failed");
        }
    }

    private static void InsertEquipmentAction(
        SqliteConnection connection,SqliteTransaction tx,
        TrustedEquipmentContext context,EquipmentTransferKind kind,
        long itemUid,int itemDefinitionId,int slotId,long? sourceDropId)
    {
        using var action=connection.CreateCommand();
        action.Transaction=tx;
        action.CommandText=@"
INSERT INTO equipment_actions(
    character_id,connection_generation,map_epoch,action_sequence,
    action_kind,item_uid,item_definition_id,slot_id,source_drop_id,created_unix_ms)
VALUES($actor,$conn,$epoch,$seq,$kind,$uid,$item,$slot,$drop,$now);";
        action.Parameters.AddWithValue("$actor",context.CharacterId);
        action.Parameters.AddWithValue("$conn",context.ConnectionGeneration);
        action.Parameters.AddWithValue("$epoch",context.MapEpoch);
        action.Parameters.AddWithValue("$seq",(long)context.ActionSequence);
        action.Parameters.AddWithValue("$kind",(int)kind);
        action.Parameters.AddWithValue("$uid",itemUid);
        action.Parameters.AddWithValue("$item",itemDefinitionId);
        action.Parameters.AddWithValue("$slot",slotId);
        action.Parameters.AddWithValue("$drop",
            sourceDropId is null?DBNull.Value:sourceDropId.Value);
        action.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if(action.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("Equipment action receipt failed");
    }

    private static void ValidateEquipmentRequest(
        TrustedEquipmentContext context,EquipmentTransferKind kind,long itemUid)
    {
        if(context.CharacterId<=0||context.ConnectionGeneration<=0||
           context.MapEpoch<=0||context.ActionSequence==0||itemUid<=0||
           kind is not (EquipmentTransferKind.Equip or EquipmentTransferKind.Unequip))
            throw new ArgumentOutOfRangeException(nameof(context));
    }

    private sealed record EquipmentMoveItem(
        long ItemUid,long CharacterId,int ItemDefinitionId,int Quantity,
        long? SourceDropId,int? DurabilityRaw,int EnhancementLevel,int SourceKind,
        string SourceReference,long CreatedUnixMilliseconds,int SlotId);
}
