using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum StorageTransferKind : byte { Deposit = 1, Withdraw = 2 }

public enum StorageTransferStatus : byte
{
    Applied = 0,
    AlreadyApplied = 1,
    NotOwned = 2,
    NotStored = 3,
    StaleSequence = 4,
    UnknownStorage = 5,
    TooFar = 6,
    StaleGeneration = 7,
    PlayerDead = 8
}

/// <summary>
/// Built only by an authenticated gateway/session owner. The raw client must
/// never provide CharacterId or ConnectionGeneration directly.
/// </summary>
public readonly record struct TrustedStorageContext(
    long CharacterId, long ConnectionGeneration, long MapEpoch, uint ActionSequence);

public sealed record StorageTransferResult(
    StorageTransferStatus Status, StorageTransferKind Kind,
    long ItemUid, int ItemDefinitionId, int Quantity, long SourceDropId);

public sealed record StoredItem(
    long ItemUid, long CharacterId, int ItemDefinitionId,
    int Quantity, long SourceDropId);

public sealed partial class SqliteIdentityStore
{
    public Task<StorageTransferResult> DepositInventoryItemAsync(
        TrustedStorageContext context, long itemUid,
        CancellationToken cancellationToken = default) =>
        ExecuteStorageTransferAsync(
            context, StorageTransferKind.Deposit, itemUid, cancellationToken);

    public Task<StorageTransferResult> WithdrawStorageItemAsync(
        TrustedStorageContext context, long itemUid,
        CancellationToken cancellationToken = default) =>
        ExecuteStorageTransferAsync(
            context, StorageTransferKind.Withdraw, itemUid, cancellationToken);

    public async Task<IReadOnlyList<StoredItem>> ListStorageItemsAsync(
        long characterId, int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (characterId <= 0 || limit is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run<IReadOnlyList<StoredItem>>(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT item_uid,character_id,item_definition_id,
quantity,COALESCE(source_drop_id,0) FROM storage_items
WHERE character_id=$actor ORDER BY item_uid LIMIT $limit;";
            command.Parameters.AddWithValue("$actor", characterId);
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var result = new List<StoredItem>();
            while (reader.Read())
                result.Add(new(reader.GetInt64(0), reader.GetInt64(1),
                    reader.GetInt32(2), reader.GetInt32(3), reader.GetInt64(4)));
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StorageTransferResult> ExecuteStorageTransferAsync(
        TrustedStorageContext context, StorageTransferKind kind, long itemUid,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateStorageRequest(context, kind, itemUid);
        return await Task.Run<StorageTransferResult>(() =>
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();

            using (var replay = connection.CreateCommand())
            {
                replay.Transaction = tx;
                replay.CommandText = @"
SELECT action_kind,item_uid,item_definition_id,quantity,COALESCE(source_drop_id,0)
FROM storage_actions
WHERE character_id=$actor AND connection_generation=$conn
  AND map_epoch=$epoch AND action_sequence=$seq;";
                replay.Parameters.AddWithValue("$actor", context.CharacterId);
                replay.Parameters.AddWithValue("$conn", context.ConnectionGeneration);
                replay.Parameters.AddWithValue("$epoch", context.MapEpoch);
                replay.Parameters.AddWithValue("$seq", (long)context.ActionSequence);
                using var reader = replay.ExecuteReader();
                if (reader.Read())
                {
                    var originalKind = (StorageTransferKind)reader.GetInt32(0);
                    long originalUid = reader.GetInt64(1);
                    int originalItem = reader.GetInt32(2);
                    int originalQuantity = reader.GetInt32(3);
                    long originalSourceDrop = reader.GetInt64(4);
                    return originalKind == kind && originalUid == itemUid
                        ? new(StorageTransferStatus.AlreadyApplied, kind,
                            originalUid, originalItem, originalQuantity, originalSourceDrop)
                        : new(StorageTransferStatus.StaleSequence, kind,
                            itemUid, 0, 0, 0);
                }
            }

            long last = ScalarLong(connection, @"
SELECT COALESCE(MAX(action_sequence),0) FROM storage_actions
WHERE character_id=$actor AND connection_generation=$conn AND map_epoch=$epoch;", tx,
                ("$actor", context.CharacterId),
                ("$conn", context.ConnectionGeneration),
                ("$epoch", context.MapEpoch));
            if (last >= context.ActionSequence)
                return new(StorageTransferStatus.StaleSequence, kind, itemUid, 0, 0, 0);

            string sourceTable = kind == StorageTransferKind.Deposit
                ? "item_instances" : "storage_items";
            string destinationTable = kind == StorageTransferKind.Deposit
                ? "storage_items" : "item_instances";
            var row = ReadStorageMoveItem(
                connection, tx, sourceTable, context.CharacterId, itemUid);
            if (row is null)
                return new(
                    kind == StorageTransferKind.Deposit
                        ? StorageTransferStatus.NotOwned
                        : StorageTransferStatus.NotStored,
                    kind, itemUid, 0, 0, 0);

            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = tx;
                delete.CommandText =
                    $"DELETE FROM {sourceTable} WHERE item_uid=$uid AND character_id=$actor;";
                delete.Parameters.AddWithValue("$uid", itemUid);
                delete.Parameters.AddWithValue("$actor", context.CharacterId);
                if (delete.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Storage source item changed during transfer");
            }

            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = $@"
INSERT INTO {destinationTable}(
    item_uid,character_id,item_definition_id,quantity,source_drop_id,
    source_kind,source_reference,created_unix_ms)
VALUES($uid,$actor,$item,$quantity,$drop,$source_kind,$source,$created);";
                insert.Parameters.AddWithValue("$uid", row.ItemUid);
                insert.Parameters.AddWithValue("$actor", row.CharacterId);
                insert.Parameters.AddWithValue("$item", row.ItemDefinitionId);
                insert.Parameters.AddWithValue("$quantity", row.Quantity);
                insert.Parameters.AddWithValue(
                    "$drop", row.SourceDropId is null ? DBNull.Value : row.SourceDropId.Value);
                insert.Parameters.AddWithValue("$source_kind", row.SourceKind);
                insert.Parameters.AddWithValue("$source", row.SourceReference);
                insert.Parameters.AddWithValue("$created", row.CreatedUnixMilliseconds);
                if (insert.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Storage destination insert failed");
            }

            using (var action = connection.CreateCommand())
            {
                action.Transaction = tx;
                action.CommandText = @"
INSERT INTO storage_actions(
    character_id,connection_generation,map_epoch,action_sequence,
    action_kind,item_uid,item_definition_id,quantity,source_drop_id,created_unix_ms)
VALUES($actor,$conn,$epoch,$seq,$kind,$uid,$item,$quantity,$drop,$now);";
                action.Parameters.AddWithValue("$actor", context.CharacterId);
                action.Parameters.AddWithValue("$conn", context.ConnectionGeneration);
                action.Parameters.AddWithValue("$epoch", context.MapEpoch);
                action.Parameters.AddWithValue("$seq", (long)context.ActionSequence);
                action.Parameters.AddWithValue("$kind", (int)kind);
                action.Parameters.AddWithValue("$uid", row.ItemUid);
                action.Parameters.AddWithValue("$item", row.ItemDefinitionId);
                action.Parameters.AddWithValue("$quantity", row.Quantity);
                action.Parameters.AddWithValue("$drop", row.SourceDropId is null ? DBNull.Value : row.SourceDropId.Value);
                action.Parameters.AddWithValue(
                    "$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (action.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Storage action receipt failed");
            }

            tx.Commit();
            return new(StorageTransferStatus.Applied, kind,
                row.ItemUid, row.ItemDefinitionId, row.Quantity, row.SourceDropId ?? 0);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static StorageMoveItem? ReadStorageMoveItem(
        SqliteConnection connection, SqliteTransaction tx, string table,
        long characterId, long itemUid)
    {
        if (table is not ("item_instances" or "storage_items"))
            throw new ArgumentOutOfRangeException(nameof(table));
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = $@"SELECT item_uid,character_id,item_definition_id,
quantity,source_drop_id,source_kind,source_reference,created_unix_ms
FROM {table} WHERE item_uid=$uid AND character_id=$actor;";
        command.Parameters.AddWithValue("$uid", itemUid);
        command.Parameters.AddWithValue("$actor", characterId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.GetInt32(5), reader.GetString(6), reader.GetInt64(7));
    }

    private static void ValidateStorageRequest(
        TrustedStorageContext context, StorageTransferKind kind, long itemUid)
    {
        if (context.CharacterId <= 0 || context.ConnectionGeneration <= 0 ||
            context.MapEpoch <= 0 || context.ActionSequence == 0 ||
            kind is not (StorageTransferKind.Deposit or StorageTransferKind.Withdraw) ||
            itemUid <= 0)
            throw new ArgumentOutOfRangeException(nameof(context));
    }

    private sealed record StorageMoveItem(
        long ItemUid, long CharacterId, int ItemDefinitionId, int Quantity,
        long? SourceDropId, int SourceKind, string SourceReference,
        long CreatedUnixMilliseconds);
}
