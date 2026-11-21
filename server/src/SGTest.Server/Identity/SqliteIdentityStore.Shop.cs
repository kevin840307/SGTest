using SGTest.Server.Npc;
using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum ShopAccessStatus : byte
{
    Allowed = 0,
    StaleGeneration = 1,
    UnknownShop = 2,
    TooFar = 3,
    PlayerDead = 4
}

public enum ShopTransactionKind : byte { Buy = 1, Sell = 2 }

public enum ShopTransactionStatus : byte
{
    Applied = 0,
    AlreadyApplied = 1,
    UnknownShop = 2,
    TooFar = 3,
    UnknownItem = 4,
    InsufficientMoney = 5,
    NotOwned = 6,
    InsufficientQuantity = 7,
    StaleSequence = 8,
    StaleGeneration = 9,
    PlayerDead = 10,
    NotSellable = 11
}

public readonly record struct TrustedShopContext(
    long CharacterId, long ConnectionGeneration, long MapEpoch, uint ActionSequence);

public readonly record struct TrustedShopPrice(
    int NpcSpawnId, int ItemDefinitionId, long BuyPrice, long SellPrice,
    string SourceReference);

public sealed record ShopTransactionResult(
    ShopTransactionStatus Status, ShopTransactionKind Kind, int NpcSpawnId,
    long ItemUid, int ItemDefinitionId, int Quantity, int RemainingQuantity,
    long Money);

public sealed partial class SqliteIdentityStore
{
    public const long MaximumMoney = 9_000_000_000_000_000L;

    public async Task<long> GetCharacterMoneyAsync(
        long characterId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(() =>
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            EnsureWallet(connection, tx, characterId);
            long money = ScalarLong(connection,
                "SELECT money FROM character_wallets WHERE character_id=$id;", tx,
                ("$id", characterId));
            tx.Commit();
            return money;
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<ShopTransactionResult> BuyFromShopAsync(
        TrustedShopContext context, ShopAccessStatus access,
        TrustedShopPrice rule, int quantity,
        CancellationToken cancellationToken = default) =>
        ExecuteShopAsync(context, access, ShopTransactionKind.Buy, rule,
            0, quantity, cancellationToken);

    public Task<ShopTransactionResult> SellToShopAsync(
        TrustedShopContext context, ShopAccessStatus access,
        TrustedShopPrice rule, long itemUid, int quantity,
        CancellationToken cancellationToken = default) =>
        ExecuteShopAsync(context, access, ShopTransactionKind.Sell, rule,
            itemUid, quantity, cancellationToken);

    private async Task<ShopTransactionResult> ExecuteShopAsync(
        TrustedShopContext context, ShopAccessStatus access,
        ShopTransactionKind kind, TrustedShopPrice rule,
        long requestedItemUid, int quantity, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateShopRequest(context, kind, rule, requestedItemUid, quantity);
        return await Task.Run(() =>
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            EnsureWallet(connection, tx, context.CharacterId);

            using (var old = connection.CreateCommand())
            {
                old.Transaction = tx;
                old.CommandText = @"
SELECT action_kind,npc_spawn_id,item_uid,item_definition_id,quantity,
       unit_price,remaining_quantity,money_after,source_reference
FROM shop_actions
WHERE character_id=$actor AND connection_generation=$conn
  AND map_epoch=$epoch AND action_sequence=$seq;";
                old.Parameters.AddWithValue("$actor", context.CharacterId);
                old.Parameters.AddWithValue("$conn", context.ConnectionGeneration);
                old.Parameters.AddWithValue("$epoch", context.MapEpoch);
                old.Parameters.AddWithValue("$seq", (long)context.ActionSequence);
                using var reader = old.ExecuteReader();
                if (reader.Read())
                {
                    var originalKind = (ShopTransactionKind)reader.GetInt32(0);
                    int originalNpc = reader.GetInt32(1);
                    long originalUid = reader.GetInt64(2);
                    int originalItem = reader.GetInt32(3);
                    int originalQuantity = reader.GetInt32(4);
                    long originalPrice = reader.GetInt64(5);
                    int originalRemaining = reader.GetInt32(6);
                    long money = reader.GetInt64(7);
                    string source = reader.GetString(8);
                    bool exact = originalKind == kind &&
                        originalNpc == rule.NpcSpawnId &&
                        originalItem == rule.ItemDefinitionId &&
                        originalQuantity == quantity &&
                        originalPrice == UnitPrice(kind, rule) &&
                        source == rule.SourceReference &&
                        (kind == ShopTransactionKind.Buy ||
                         originalUid == requestedItemUid);
                    return exact
                        ? new(ShopTransactionStatus.AlreadyApplied, kind,
                            originalNpc, originalUid, originalItem,
                            originalQuantity, originalRemaining, money)
                        : Reject(ShopTransactionStatus.StaleSequence, kind,
                            rule.NpcSpawnId, requestedItemUid,
                            rule.ItemDefinitionId, quantity,
                            ReadMoney(connection, tx, context.CharacterId));
                }
            }

            long last = ScalarLong(connection, @"
SELECT COALESCE(MAX(action_sequence),0) FROM shop_actions
WHERE character_id=$actor AND connection_generation=$conn AND map_epoch=$epoch;", tx,
                ("$actor", context.CharacterId), ("$conn", context.ConnectionGeneration),
                ("$epoch", context.MapEpoch));
            if (last >= context.ActionSequence)
                return Reject(ShopTransactionStatus.StaleSequence, kind,
                    rule.NpcSpawnId, requestedItemUid, rule.ItemDefinitionId,
                    quantity, ReadMoney(connection, tx, context.CharacterId));

            var accessFailure = AccessFailure(access);
            if (accessFailure is not null)
                return Reject(accessFailure.Value, kind, rule.NpcSpawnId,
                    requestedItemUid, rule.ItemDefinitionId, quantity,
                    ReadMoney(connection, tx, context.CharacterId));

            long unitPrice = UnitPrice(kind, rule);
            if (kind == ShopTransactionKind.Sell && unitPrice == 0)
                return Reject(ShopTransactionStatus.NotSellable, kind,
                    rule.NpcSpawnId, requestedItemUid, rule.ItemDefinitionId,
                    quantity, ReadMoney(connection, tx, context.CharacterId));
            if (unitPrice <= 0)
                return Reject(ShopTransactionStatus.UnknownItem, kind,
                    rule.NpcSpawnId, requestedItemUid, rule.ItemDefinitionId,
                    quantity, ReadMoney(connection, tx, context.CharacterId));

            long total;
            try { total = checked(unitPrice * quantity); }
            catch (OverflowException)
            {
                return Reject(kind == ShopTransactionKind.Buy
                    ? ShopTransactionStatus.InsufficientMoney
                    : ShopTransactionStatus.UnknownItem, kind, rule.NpcSpawnId,
                    requestedItemUid, rule.ItemDefinitionId, quantity,
                    ReadMoney(connection, tx, context.CharacterId));
            }

            long moneyBefore = ReadMoney(connection, tx, context.CharacterId);
            long itemUid;
            int remaining;
            long moneyAfter;

            if (kind == ShopTransactionKind.Buy)
            {
                if (total > moneyBefore)
                    return Reject(ShopTransactionStatus.InsufficientMoney, kind,
                        rule.NpcSpawnId, 0, rule.ItemDefinitionId, quantity, moneyBefore);
                moneyAfter = moneyBefore - total;
                UpdateMoney(connection, tx, context.CharacterId, moneyAfter);
                using var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = @"
INSERT INTO item_instances(
    character_id,item_definition_id,quantity,source_drop_id,
    source_kind,source_reference,created_unix_ms)
VALUES($actor,$item,$quantity,NULL,2,$source,$now);";
                insert.Parameters.AddWithValue("$actor", context.CharacterId);
                insert.Parameters.AddWithValue("$item", rule.ItemDefinitionId);
                insert.Parameters.AddWithValue("$quantity", quantity);
                insert.Parameters.AddWithValue("$source", rule.SourceReference);
                insert.Parameters.AddWithValue("$now",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (insert.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Shop inventory grant failed");
                itemUid = ScalarLong(connection, "SELECT last_insert_rowid();", tx);
                remaining = quantity;
            }
            else
            {
                using var owned = connection.CreateCommand();
                owned.Transaction = tx;
                owned.CommandText = @"
SELECT item_definition_id,quantity FROM item_instances
WHERE item_uid=$uid AND character_id=$actor;";
                owned.Parameters.AddWithValue("$uid", requestedItemUid);
                owned.Parameters.AddWithValue("$actor", context.CharacterId);
                using var reader = owned.ExecuteReader();
                if (!reader.Read())
                    return Reject(ShopTransactionStatus.NotOwned, kind,
                        rule.NpcSpawnId, requestedItemUid, rule.ItemDefinitionId,
                        quantity, moneyBefore);
                int actualDefinition = reader.GetInt32(0);
                int actualQuantity = reader.GetInt32(1);
                if (actualDefinition != rule.ItemDefinitionId)
                    return Reject(ShopTransactionStatus.UnknownItem, kind,
                        rule.NpcSpawnId, requestedItemUid, rule.ItemDefinitionId,
                        quantity, moneyBefore);
                if (actualQuantity < quantity)
                    return Reject(ShopTransactionStatus.InsufficientQuantity, kind,
                        rule.NpcSpawnId, requestedItemUid, rule.ItemDefinitionId,
                        quantity, moneyBefore);
                remaining = actualQuantity - quantity;
                moneyAfter = checked(moneyBefore + total);
                if (moneyAfter > MaximumMoney)
                    throw new InvalidOperationException("Character money overflow");
                UpdateMoney(connection, tx, context.CharacterId, moneyAfter);
                using var update = connection.CreateCommand();
                update.Transaction = tx;
                update.CommandText = remaining == 0
                    ? "DELETE FROM item_instances WHERE item_uid=$uid AND character_id=$actor;"
                    : "UPDATE item_instances SET quantity=$remaining WHERE item_uid=$uid AND character_id=$actor;";
                update.Parameters.AddWithValue("$uid", requestedItemUid);
                update.Parameters.AddWithValue("$actor", context.CharacterId);
                if (remaining != 0)
                    update.Parameters.AddWithValue("$remaining", remaining);
                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Shop sell lost inventory ownership");
                itemUid = requestedItemUid;
            }

            using (var action = connection.CreateCommand())
            {
                action.Transaction = tx;
                action.CommandText = @"
INSERT INTO shop_actions(
    character_id,connection_generation,map_epoch,action_sequence,action_kind,
    npc_spawn_id,item_uid,item_definition_id,quantity,unit_price,
    remaining_quantity,money_after,source_reference,created_unix_ms)
VALUES($actor,$conn,$epoch,$seq,$kind,$npc,$uid,$item,$quantity,$price,
       $remaining,$money,$source,$now);";
                action.Parameters.AddWithValue("$actor", context.CharacterId);
                action.Parameters.AddWithValue("$conn", context.ConnectionGeneration);
                action.Parameters.AddWithValue("$epoch", context.MapEpoch);
                action.Parameters.AddWithValue("$seq", (long)context.ActionSequence);
                action.Parameters.AddWithValue("$kind", (int)kind);
                action.Parameters.AddWithValue("$npc", rule.NpcSpawnId);
                action.Parameters.AddWithValue("$uid", itemUid);
                action.Parameters.AddWithValue("$item", rule.ItemDefinitionId);
                action.Parameters.AddWithValue("$quantity", quantity);
                action.Parameters.AddWithValue("$price", unitPrice);
                action.Parameters.AddWithValue("$remaining", remaining);
                action.Parameters.AddWithValue("$money", moneyAfter);
                action.Parameters.AddWithValue("$source", rule.SourceReference);
                action.Parameters.AddWithValue("$now",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (action.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Shop action receipt failed");
            }

            tx.Commit();
            return new(ShopTransactionStatus.Applied, kind, rule.NpcSpawnId,
                itemUid, rule.ItemDefinitionId, quantity, remaining, moneyAfter);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ShopTransactionResult Reject(
        ShopTransactionStatus status, ShopTransactionKind kind, int npcSpawnId,
        long itemUid, int itemDefinitionId, int quantity, long money) =>
        new(status, kind, npcSpawnId, Math.Max(0, itemUid),
            itemDefinitionId, quantity, 0, money);

    private static ShopTransactionStatus? AccessFailure(ShopAccessStatus access) =>
        access switch
        {
            ShopAccessStatus.Allowed => null,
            ShopAccessStatus.StaleGeneration => ShopTransactionStatus.StaleGeneration,
            ShopAccessStatus.UnknownShop => ShopTransactionStatus.UnknownShop,
            ShopAccessStatus.TooFar => ShopTransactionStatus.TooFar,
            ShopAccessStatus.PlayerDead => ShopTransactionStatus.PlayerDead,
            _ => ShopTransactionStatus.StaleGeneration
        };

    private static long UnitPrice(ShopTransactionKind kind, TrustedShopPrice rule) =>
        kind == ShopTransactionKind.Buy ? rule.BuyPrice : rule.SellPrice;

    private static void ValidateShopRequest(
        TrustedShopContext context, ShopTransactionKind kind,
        TrustedShopPrice rule, long itemUid, int quantity)
    {
        if (context.CharacterId <= 0 || context.ConnectionGeneration <= 0 ||
            context.MapEpoch <= 0 || context.ActionSequence == 0 ||
            kind is not (ShopTransactionKind.Buy or ShopTransactionKind.Sell) ||
            rule.NpcSpawnId <= 0 ||
            rule.ItemDefinitionId is < 1 or > 100_000_000 ||
            rule.BuyPrice is < 0 or > 1_000_000_000_000L ||
            rule.SellPrice is < 0 or > 1_000_000_000_000L ||
            rule.SellPrice > rule.BuyPrice ||
            string.IsNullOrWhiteSpace(rule.SourceReference) ||
            rule.SourceReference.Length > 512 ||
            quantity is < 1 or > 1_000_000 ||
            (kind == ShopTransactionKind.Buy && itemUid != 0) ||
            (kind == ShopTransactionKind.Sell && itemUid <= 0))
            throw new ArgumentOutOfRangeException(nameof(context));
    }

    private static void EnsureWallet(
        SqliteConnection connection, SqliteTransaction tx, long characterId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"
INSERT OR IGNORE INTO character_wallets(character_id,money)
SELECT character_id,0 FROM characters WHERE character_id=$id;";
        command.Parameters.AddWithValue("$id", characterId);
        command.ExecuteNonQuery();
        if (ScalarLong(connection,
                "SELECT COUNT(*) FROM character_wallets WHERE character_id=$id;", tx,
                ("$id", characterId)) != 1)
            throw new KeyNotFoundException("Unknown character");
    }

    private static long ReadMoney(
        SqliteConnection connection, SqliteTransaction tx, long characterId) =>
        ScalarLong(connection,
            "SELECT money FROM character_wallets WHERE character_id=$id;", tx,
            ("$id", characterId));

    private static void UpdateMoney(
        SqliteConnection connection, SqliteTransaction tx,
        long characterId, long money)
    {
        if (money < 0 || money > MaximumMoney)
            throw new ArgumentOutOfRangeException(nameof(money));
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            "UPDATE character_wallets SET money=$money WHERE character_id=$id;";
        command.Parameters.AddWithValue("$money", money);
        command.Parameters.AddWithValue("$id", characterId);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Character wallet update failed");
    }
}
