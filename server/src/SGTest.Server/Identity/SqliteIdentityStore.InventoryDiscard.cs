namespace SGTest.Server.Identity;

/// <summary>
/// Authenticated inventory mutation context. Raw clients may choose an owned
/// ItemUid and quantity only; character/session/map identity comes from the
/// trusted World/Gateway binding and durable server boot generation.
/// </summary>
public readonly record struct TrustedInventoryDiscardContext(
    long CharacterId,long ConnectionGeneration,long MapEpoch,uint ActionSequence);

public enum InventoryDiscardStatus : byte
{
    Applied=0, AlreadyApplied=1, Missing=2, NotOwned=3,
    InvalidQuantity=4, StaleSequence=5, StaleGeneration=6
}

public sealed record InventoryDiscardResult(
    InventoryDiscardStatus Status,long ItemUid,
    int ItemDefinitionId=0,int DiscardedQuantity=0,int RemainingQuantity=0);

public sealed partial class SqliteIdentityStore
{
    /// <summary>
    /// Atomically destroys an explicitly requested quantity from exactly one
    /// owned inventory UID. This does NOT spawn a ground item. A committed
    /// receipt survives deletion of the last stack unit and makes an identical
    /// authenticated retry idempotent across DB reopen/process restart.
    /// </summary>
    public async Task<InventoryDiscardResult> DiscardInventoryItemAsync(
        TrustedInventoryDiscardContext actor,long itemUid,int quantity,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(actor.CharacterId<=0||actor.ConnectionGeneration<=0||
            actor.MapEpoch<=0||actor.ActionSequence==0||itemUid<=0||
            quantity is <1 or >1_000_000)
            throw new ArgumentOutOfRangeException(nameof(actor));
        return await Task.Run(()=>
        {
            using var conn=Open();
            using var tx=conn.BeginTransaction();
            using(var action=conn.CreateCommand())
            {
                action.Transaction=tx;
                action.CommandText=@"SELECT item_uid,item_definition_id,
discarded_quantity,remaining_quantity FROM inventory_discard_actions
WHERE character_id=$actor AND connection_generation=$conn
AND map_epoch=$epoch AND action_sequence=$seq;";
                action.Parameters.AddWithValue("$actor",actor.CharacterId);
                action.Parameters.AddWithValue("$conn",actor.ConnectionGeneration);
                action.Parameters.AddWithValue("$epoch",actor.MapEpoch);
                action.Parameters.AddWithValue("$seq",(long)actor.ActionSequence);
                using var reader=action.ExecuteReader();
                if(reader.Read())
                {
                    long previousUid=reader.GetInt64(0);
                    int previousQuantity=reader.GetInt32(2);
                    return previousUid==itemUid && previousQuantity==quantity
                        ? new InventoryDiscardResult(
                            InventoryDiscardStatus.AlreadyApplied,previousUid,
                            reader.GetInt32(1),previousQuantity,reader.GetInt32(3))
                        : new InventoryDiscardResult(
                            InventoryDiscardStatus.StaleSequence,itemUid);
                }
            }

            long last=ScalarLong(conn,@"SELECT COALESCE(MAX(action_sequence),0)
FROM inventory_discard_actions
WHERE character_id=$actor AND connection_generation=$conn AND map_epoch=$epoch;",
                tx,("$actor",actor.CharacterId),("$conn",actor.ConnectionGeneration),
                ("$epoch",actor.MapEpoch));
            if(last>=actor.ActionSequence)
                return new(InventoryDiscardStatus.StaleSequence,itemUid);

            long owner;
            int itemId,ownedQuantity;
            using(var select=conn.CreateCommand())
            {
                select.Transaction=tx;
                select.CommandText=@"SELECT character_id,item_definition_id,quantity
FROM item_instances WHERE item_uid=$uid;";
                select.Parameters.AddWithValue("$uid",itemUid);
                using var reader=select.ExecuteReader();
                if(!reader.Read())
                    return new(InventoryDiscardStatus.Missing,itemUid);
                owner=reader.GetInt64(0);
                itemId=reader.GetInt32(1);
                ownedQuantity=reader.GetInt32(2);
            }
            if(owner!=actor.CharacterId)
                return new(InventoryDiscardStatus.NotOwned,itemUid);
            if(quantity>ownedQuantity)
                return new(InventoryDiscardStatus.InvalidQuantity,itemUid);

            int remaining=ownedQuantity-quantity;
            using(var update=conn.CreateCommand())
            {
                update.Transaction=tx;
                update.CommandText=remaining==0
                    ? @"DELETE FROM item_instances
WHERE item_uid=$uid AND character_id=$actor AND quantity=$owned;"
                    : @"UPDATE item_instances SET quantity=$remaining
WHERE item_uid=$uid AND character_id=$actor AND quantity=$owned;";
                update.Parameters.AddWithValue("$uid",itemUid);
                update.Parameters.AddWithValue("$actor",actor.CharacterId);
                update.Parameters.AddWithValue("$owned",ownedQuantity);
                if(remaining!=0) update.Parameters.AddWithValue("$remaining",remaining);
                if(update.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException(
                        "Inventory quantity raced during durable discard");
            }
            using(var save=conn.CreateCommand())
            {
                save.Transaction=tx;
                save.CommandText=@"INSERT INTO inventory_discard_actions(
character_id,connection_generation,map_epoch,action_sequence,item_uid,
item_definition_id,discarded_quantity,remaining_quantity,created_unix_ms)
VALUES($actor,$conn,$epoch,$seq,$uid,$item,$discarded,$remaining,$now);";
                save.Parameters.AddWithValue("$actor",actor.CharacterId);
                save.Parameters.AddWithValue("$conn",actor.ConnectionGeneration);
                save.Parameters.AddWithValue("$epoch",actor.MapEpoch);
                save.Parameters.AddWithValue("$seq",(long)actor.ActionSequence);
                save.Parameters.AddWithValue("$uid",itemUid);
                save.Parameters.AddWithValue("$item",itemId);
                save.Parameters.AddWithValue("$discarded",quantity);
                save.Parameters.AddWithValue("$remaining",remaining);
                save.Parameters.AddWithValue("$now",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if(save.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException(
                        "Inventory discard receipt was not persisted");
            }
            tx.Commit();
            return new(InventoryDiscardStatus.Applied,itemUid,itemId,quantity,remaining);
        },cancellationToken).ConfigureAwait(false);
    }
}
