using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace SGTest.Server.Identity;

/// <summary>
/// Explicitly audited recovered-item effects. This source is admitted only by
/// a production operator; the presence of a client .tres does not itself prove
/// the original game's actual cooldown, multiplier or usage requirements.
/// </summary>
public readonly record struct RecoveryItemRule(
    int ItemDefinitionId, int RestoreHp, int RequiredLevel);

public static class RecoveryItemCatalog
{
    public static IReadOnlyDictionary<int,RecoveryItemRule> LoadVerified(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(
            "Verified recovery rule manifest path required", nameof(path));
        var content=File.ReadAllBytes(path);
        if (content.Length is < 1 or > 1_048_576)
            throw new InvalidDataException("Recovery manifest size out of range");
        using var parsed=JsonDocument.Parse(content);
        var root=parsed.RootElement;
        if (root.ValueKind!=JsonValueKind.Object ||
            !root.TryGetProperty("kind",out var kind) ||
            kind.GetString()!="verified_recovery_item_rules" ||
            !root.TryGetProperty("version",out var version) ||
            version.GetInt32()!=1 ||
            !root.TryGetProperty("rules_verified",out var verified) ||
            verified.ValueKind!=JsonValueKind.True ||
            !root.TryGetProperty("gameplay_ready",out var ready) ||
            ready.ValueKind!=JsonValueKind.True ||
            !root.TryGetProperty("source_reference",out var source) ||
            source.ValueKind!=JsonValueKind.String ||
            string.IsNullOrWhiteSpace(source.GetString()) ||
            source.GetString()!.Length>256 ||
            !root.TryGetProperty("rules",out var rules) ||
            rules.ValueKind!=JsonValueKind.Array ||
            rules.GetArrayLength() is <1 or >4096)
            throw new InvalidDataException("Recovery rules require operator-verified provenance");
        var result=new Dictionary<int,RecoveryItemRule>();
        foreach (var rule in rules.EnumerateArray())
        {
            if (rule.ValueKind!=JsonValueKind.Object ||
                !rule.TryGetProperty("item_definition_id",out var item) ||
                !rule.TryGetProperty("restore_hp",out var restore) ||
                !rule.TryGetProperty("required_level",out var level) ||
                !item.TryGetInt32(out int id) ||
                !restore.TryGetInt32(out int hp) ||
                !level.TryGetInt32(out int required) ||
                id is <1 or >100_000_000 || hp is <1 or >100_000_000 ||
                required is <1 or >1000 || !result.TryAdd(id,new(id,hp,required)))
                throw new InvalidDataException("Recovery rules contain invalid or duplicate item definitions");
        }
        return result;
    }
}

/// <summary>Trusted server-only item-use session. Client can select only UID
/// and request sequence; all other fields must originate from World, Combat
/// and the durable Server startup generation.</summary>
public readonly record struct TrustedRecoveryUseContext(
    long CharacterId, long ConnectionGeneration, long MapEpoch,
    uint ActionSequence, int ExpectedCommittedHp);

public enum RecoveryUseStatus : byte
{
    Granted=0, AlreadyGranted=1, Missing=2, NotOwned=3,
    NoRule=4, FullHp=5, Dead=6, CombatUnavailable=7,
    RequirementsNotMet=8, StaleSequence=9, CombatDiverged=10
}

public sealed record RecoveryUseResult(
    RecoveryUseStatus Status, long ItemUid,
    int ItemDefinitionId=0,int RemainingQuantity=0,
    int PreviousHp=0,int CurrentHp=0,int MaxHp=0);

public sealed partial class SqliteIdentityStore
{
    /// <summary>
    /// Atomic one-unit recovery: single SQLite commit contains both item
    /// quantity (or stack removal) and combat current_hp plus replay receipt.
    /// Caller must suspend/drain the exact combat generation before calling.
    /// </summary>
    public async Task<RecoveryUseResult> UseVerifiedRecoveryItemAsync(
        TrustedRecoveryUseContext actor,long itemUid,
        IReadOnlyDictionary<int,RecoveryItemRule> rules,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rules);
        if(actor.CharacterId<=0||actor.ConnectionGeneration<=0||
            actor.MapEpoch<=0||actor.ActionSequence==0||
            actor.ExpectedCommittedHp<=0||itemUid<=0)
            throw new ArgumentOutOfRangeException(nameof(actor));
        return await Task.Run(()=>
        {
            using var conn=Open();
            using var tx=conn.BeginTransaction();
            using(var action=conn.CreateCommand())
            {
                action.Transaction=tx;
                action.CommandText=@"SELECT item_uid,item_definition_id,remaining_quantity,
previous_hp,current_hp,max_hp FROM recovery_use_actions
WHERE character_id=$actor AND connection_generation=$conn
AND map_epoch=$epoch AND action_sequence=$seq;";
                action.Parameters.AddWithValue("$actor",actor.CharacterId);
                action.Parameters.AddWithValue("$conn",actor.ConnectionGeneration);
                action.Parameters.AddWithValue("$epoch",actor.MapEpoch);
                action.Parameters.AddWithValue("$seq",(long)actor.ActionSequence);
                using var reader=action.ExecuteReader();
                if(reader.Read())
                {
                    long previousItemUid=reader.GetInt64(0);
                    return previousItemUid==itemUid
                        ? new RecoveryUseResult(RecoveryUseStatus.AlreadyGranted,
                            previousItemUid,reader.GetInt32(1),reader.GetInt32(2),
                            reader.GetInt32(3),reader.GetInt32(4),reader.GetInt32(5))
                        : new RecoveryUseResult(RecoveryUseStatus.StaleSequence,itemUid);
                }
            }
            long last=ScalarLong(conn,@"SELECT COALESCE(MAX(action_sequence),0)
FROM recovery_use_actions WHERE character_id=$actor AND connection_generation=$conn
AND map_epoch=$epoch;",tx,("$actor",actor.CharacterId),
                ("$conn",actor.ConnectionGeneration),("$epoch",actor.MapEpoch));
            if(last>=actor.ActionSequence)
                return new(RecoveryUseStatus.StaleSequence,itemUid);
            long owner;
            int itemId,quantity;
            using(var select=conn.CreateCommand())
            {
                select.Transaction=tx;
                select.CommandText=@"SELECT character_id,item_definition_id,quantity
FROM item_instances WHERE item_uid=$uid;";
                select.Parameters.AddWithValue("$uid",itemUid);
                using var reader=select.ExecuteReader();
                if(!reader.Read())return new(RecoveryUseStatus.Missing,itemUid);
                owner=reader.GetInt64(0);
                itemId=reader.GetInt32(1);
                quantity=reader.GetInt32(2);
            }
            if(owner!=actor.CharacterId)
                return new(RecoveryUseStatus.NotOwned,itemUid);
            // Old/imported/corrupt rows can violate the schema's quantity
            // CHECK. Never let a missing or invalid stack reach the debit and
            // heal transaction: it would throw rather than return a reliable
            // rejection to the player, or produce an invalid item count.
            if(quantity is <1 or >1_000_000)
                return new(RecoveryUseStatus.Missing,itemUid);
            if(!rules.TryGetValue(itemId,out var rule) ||
                rule.ItemDefinitionId!=itemId ||
                rule.RestoreHp is <1 or >100_000_000 ||
                rule.RequiredLevel is <1 or >1000)
                return new(RecoveryUseStatus.NoRule,itemUid);
            var combat=ReadCombatProfile(conn,actor.CharacterId,tx);
            if(combat is null)
                return new(RecoveryUseStatus.CombatUnavailable,itemUid);
            int hp=combat.Data.CurrentHp,max=combat.Data.MaxHp;
            if(hp!=actor.ExpectedCommittedHp)
                return new(RecoveryUseStatus.CombatDiverged,itemUid);
            if(hp<=0) return new(RecoveryUseStatus.Dead,itemUid);
            if(hp>=max) return new(RecoveryUseStatus.FullHp,itemUid);
            if(combat.Data.Level<rule.RequiredLevel)
                return new(RecoveryUseStatus.RequirementsNotMet,itemUid);
            int remaining=quantity-1;
            int healed=(int)Math.Min((long)max,(long)hp+rule.RestoreHp);
            using(var update=conn.CreateCommand())
            {
                update.Transaction=tx;
                update.CommandText=remaining==0
                    ? @"DELETE FROM item_instances WHERE item_uid=$uid AND character_id=$actor AND quantity=1;"
                    : @"UPDATE item_instances SET quantity=quantity-1
WHERE item_uid=$uid AND character_id=$actor AND quantity=$quantity;";
                update.Parameters.AddWithValue("$uid",itemUid);
                update.Parameters.AddWithValue("$actor",actor.CharacterId);
                if(remaining!=0)update.Parameters.AddWithValue("$quantity",quantity);
                if(update.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException("Inventory quantity raced during durable recovery");
            }
            using(var update=conn.CreateCommand())
            {
                update.Transaction=tx;
                update.CommandText=@"UPDATE character_combat_profiles
SET current_hp=$after,updated_unix_ms=$now
WHERE character_id=$actor AND current_hp=$before;";
                update.Parameters.AddWithValue("$after",healed);
                update.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                update.Parameters.AddWithValue("$actor",actor.CharacterId);
                update.Parameters.AddWithValue("$before",hp);
                if(update.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException("Combat HP raced during durable recovery");
            }
            using(var save=conn.CreateCommand())
            {
                save.Transaction=tx;
                save.CommandText=@"INSERT INTO recovery_use_actions(
character_id,connection_generation,map_epoch,action_sequence,item_uid,
item_definition_id,remaining_quantity,previous_hp,current_hp,max_hp,created_unix_ms)
VALUES($actor,$conn,$epoch,$seq,$uid,$item,$remaining,$before,$after,$max,$now);";
                save.Parameters.AddWithValue("$actor",actor.CharacterId);
                save.Parameters.AddWithValue("$conn",actor.ConnectionGeneration);
                save.Parameters.AddWithValue("$epoch",actor.MapEpoch);
                save.Parameters.AddWithValue("$seq",(long)actor.ActionSequence);
                save.Parameters.AddWithValue("$uid",itemUid);
                save.Parameters.AddWithValue("$item",itemId);
                save.Parameters.AddWithValue("$remaining",remaining);
                save.Parameters.AddWithValue("$before",hp);
                save.Parameters.AddWithValue("$after",healed);
                save.Parameters.AddWithValue("$max",max);
                save.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if(save.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException("Recovery receipt was not persisted");
            }
            tx.Commit();
            return new(RecoveryUseStatus.Granted,itemUid,itemId,remaining,hp,healed,max);
        },cancellationToken).ConfigureAwait(false);
    }
}
