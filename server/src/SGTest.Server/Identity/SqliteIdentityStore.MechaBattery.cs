using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace SGTest.Server.Identity;

public readonly record struct MechaBatteryRule(
    int MechaDefinitionId,int BatteryItemDefinitionId,int Capacity,
    int ChargeAmount,bool AllowWhileMounted);

/// <summary>
/// Operator-verified Mecha battery rules. Old SGOHorseEquipMgr confirms a
/// server-side OnChargeBattery path that validates the inventory item against
/// Mecha/battery data and mutates inventory only on success. Exact HK capacity,
/// charge amount and compatibility values stay outside code and fail closed.
/// </summary>
public sealed class MechaBatteryCatalog
{
    private readonly Dictionary<(int MechaDefinitionId,int BatteryItemDefinitionId),MechaBatteryRule> _rules;
    private readonly Dictionary<int,int> _capacities;

    private MechaBatteryCatalog(
        Dictionary<(int,int),MechaBatteryRule> rules,Dictionary<int,int> capacities)
    {
        _rules=rules;
        _capacities=capacities;
    }

    public int Count=>_rules.Count;

    public bool TryGet(int mechaDefinitionId,int itemDefinitionId,out MechaBatteryRule rule)=>
        _rules.TryGetValue((mechaDefinitionId,itemDefinitionId),out rule);

    public int CapacityFor(int mechaDefinitionId)=>
        _capacities.TryGetValue(mechaDefinitionId,out int capacity)?capacity:0;

    public static MechaBatteryCatalog LoadVerified(string path)
    {
        if(string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Verified Mecha battery manifest path required",nameof(path));
        byte[] content=File.ReadAllBytes(path);
        if(content.Length is <1 or >1_048_576)
            throw new InvalidDataException("Mecha battery manifest size out of range");
        using var parsed=JsonDocument.Parse(content);
        var root=parsed.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           !root.TryGetProperty("kind",out var kind)||
           kind.GetString()!="verified_mecha_battery_rules"||
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
                "Mecha battery rules require operator-verified provenance");

        var result=new Dictionary<(int,int),MechaBatteryRule>();
        var capacities=new Dictionary<int,int>();
        foreach(var entry in rules.EnumerateArray())
        {
            if(entry.ValueKind!=JsonValueKind.Object||
               !entry.TryGetProperty("mecha_definition_id",out var mecha)||
               !entry.TryGetProperty("battery_item_definition_id",out var item)||
               !entry.TryGetProperty("capacity",out var capacityValue)||
               !entry.TryGetProperty("charge_amount",out var amountValue)||
               !entry.TryGetProperty("allow_while_mounted",out var mountedValue)||
               !mecha.TryGetInt32(out int mechaDefinitionId)||
               !item.TryGetInt32(out int itemDefinitionId)||
               !capacityValue.TryGetInt32(out int capacity)||
               !amountValue.TryGetInt32(out int amount)||
               mountedValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)||
               mechaDefinitionId is <1 or >100_000_000||
               itemDefinitionId is <1 or >100_000_000||
               capacity<=0||amount<=0||amount>capacity)
                throw new InvalidDataException("Invalid Mecha battery rule");

            bool allowWhileMounted=mountedValue.GetBoolean();
            var rule=new MechaBatteryRule(
                mechaDefinitionId,itemDefinitionId,capacity,amount,allowWhileMounted);
            if(!result.TryAdd((mechaDefinitionId,itemDefinitionId),rule))
                throw new InvalidDataException("Duplicate Mecha battery compatibility rule");
            if(capacities.TryGetValue(mechaDefinitionId,out int existingCapacity))
            {
                if(existingCapacity!=capacity)
                    throw new InvalidDataException(
                        "Mecha definition has conflicting battery capacities");
            }
            else capacities.Add(mechaDefinitionId,capacity);
        }
        return new(result,capacities);
    }
}

public sealed partial class SqliteIdentityStore
{
    public async Task<MechaActionOutcome> ApplyMechaBatteryChargeAsync(
        long characterId,long connectionGeneration,long mapEpoch,uint actionSequence,
        long mechaUid,long itemUid,MechaBatteryCatalog? rules,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(characterId<=0||connectionGeneration<=0||mapEpoch<=0||actionSequence==0||
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
                bool exact=replay.Value.Action==MechaActionKind.ChargeBattery&&
                    replay.Value.MechaUid==mechaUid&&replay.Value.ItemUid==itemUid;
                return exact
                    ?new MechaActionOutcome(
                        MechaActionStatus.AlreadyProcessed,replay.Value.MountedMechaUid,itemUid)
                    :new MechaActionOutcome(
                        MechaActionStatus.SequenceConflict,replay.Value.MountedMechaUid,itemUid);
            }

            bool owned=false;
            bool mounted=false;
            int definitionId=0;
            int currentCharge=0;
            using(var command=connection.CreateCommand())
            {
                command.Transaction=tx;
                command.CommandText=@"SELECT definition_id,is_mounted,battery_charge
FROM mecha_instances WHERE mecha_uid=$mecha AND character_id=$character;";
                command.Parameters.AddWithValue("$mecha",mechaUid);
                command.Parameters.AddWithValue("$character",characterId);
                using var reader=command.ExecuteReader();
                if(reader.Read())
                {
                    owned=true;
                    definitionId=reader.GetInt32(0);
                    mounted=reader.GetInt64(1)!=0;
                    currentCharge=reader.GetInt32(2);
                }
            }

            long mountedUid=ReadMountedMechaUid(connection,tx,characterId);
            MechaActionStatus status;
            int remainingQuantity=-1;
            if(!owned)
                status=MechaActionStatus.MechaNotOwned;
            else if(rules is null)
                status=MechaActionStatus.RulesUnavailable;
            else
            {
                PortableMechaItem? item=ReadInventoryItem(connection,tx,characterId,itemUid);
                if(item is null)
                    status=MechaActionStatus.ItemNotOwned;
                else if(!rules.TryGet(definitionId,item.ItemDefinitionId,out var rule))
                    status=MechaActionStatus.BatteryIncompatible;
                else if(mounted&&!rule.AllowWhileMounted)
                    status=MechaActionStatus.MountedLocked;
                else if(currentCharge>=rule.Capacity)
                    status=MechaActionStatus.BatteryAlreadyFull;
                else
                {
                    int nextCharge=(int)Math.Min(
                        (long)rule.Capacity,(long)currentCharge+rule.ChargeAmount);
                    using(var consume=connection.CreateCommand())
                    {
                        consume.Transaction=tx;
                        consume.CommandText=item.Quantity>1
                            ?"UPDATE item_instances SET quantity=quantity-1 WHERE item_uid=$item AND character_id=$character AND quantity=$quantity;"
                            :"DELETE FROM item_instances WHERE item_uid=$item AND character_id=$character AND quantity=1;";
                        consume.Parameters.AddWithValue("$item",itemUid);
                        consume.Parameters.AddWithValue("$character",characterId);
                        if(item.Quantity>1)
                            consume.Parameters.AddWithValue("$quantity",item.Quantity);
                        if(consume.ExecuteNonQuery()!=1)
                            throw new InvalidOperationException("Mecha battery inventory item disappeared");
                    }
                    using(var update=connection.CreateCommand())
                    {
                        update.Transaction=tx;
                        update.CommandText=@"UPDATE mecha_instances
SET battery_charge=$charge,updated_unix_ms=$updated
WHERE mecha_uid=$mecha AND character_id=$character AND battery_charge=$previous;";
                        update.Parameters.AddWithValue("$charge",nextCharge);
                        update.Parameters.AddWithValue("$updated",
                            _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                        update.Parameters.AddWithValue("$mecha",mechaUid);
                        update.Parameters.AddWithValue("$character",characterId);
                        update.Parameters.AddWithValue("$previous",currentCharge);
                        if(update.ExecuteNonQuery()!=1)
                            throw new InvalidOperationException("Mecha battery state changed concurrently");
                    }
                    remainingQuantity=item.Quantity-1;
                    status=MechaActionStatus.Applied;
                }
            }

            InsertMechaActionReceipt(
                connection,tx,characterId,connectionGeneration,mapEpoch,actionSequence,
                MechaActionKind.ChargeBattery,mechaUid,itemUid,status,mountedUid,0,0);
            tx.Commit();
            return new MechaActionOutcome(
                status,mountedUid,itemUid,0,0,remainingQuantity);
        },cancellationToken).ConfigureAwait(false);
    }
}
