using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace SGTest.Server.Identity;

public enum EnhancementAccessStatus : byte
{
    Allowed=0, StaleGeneration=1, UnknownEnhancer=2, TooFar=3, PlayerDead=4
}

public enum EnhancementItemLocation : byte
{
    None=0, Inventory=1, Equipped=2
}

public enum EnhancementStatus : byte
{
    Applied=0,
    AlreadyApplied=1,
    UnknownEnhancer=2,
    TooFar=3,
    NotOwned=4,
    NoRule=5,
    MaxLevel=6,
    InsufficientMoney=7,
    StaleSequence=8,
    StaleGeneration=9,
    PlayerDead=10
}

public readonly record struct TrustedEnhancementContext(
    long CharacterId,long ConnectionGeneration,long MapEpoch,uint ActionSequence);

public readonly record struct TrustedEnhancementNpcRule(
    int NpcSpawnId,string SourceReference);

public sealed record EnhancementPreviewItem(
    long ItemUid,int ItemDefinitionId,EnhancementItemLocation Location,
    int CurrentLevel,int TargetLevel,int SuccessPpm,long MoneyCost,
    bool ConsumeMoneyOnFailure);

public sealed record EnhancementPreview(
    long WalletMoney,IReadOnlyList<EnhancementPreviewItem> Items);

public sealed record EnhancementResult(
    EnhancementStatus Status,int NpcSpawnId,long ItemUid,int ItemDefinitionId,
    EnhancementItemLocation Location,int PreviousLevel,int TargetLevel,
    int CurrentLevel,bool Succeeded,long MoneyCost,long MoneyCharged,
    long WalletMoney,int RollPpm);

public sealed partial class SqliteIdentityStore
{
    public async Task<EnhancementPreview> ListEnhancementPreviewAsync(
        long characterId,TrustedEnhancementNpcRule npcRule,
        IReadOnlyDictionary<int,EquipmentEnhancementItemRule> itemRules,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ValidateEnhancementNpcRule(npcRule);
        ArgumentNullException.ThrowIfNull(itemRules);
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            EnsureWallet(connection,tx,characterId);
            long wallet=ReadMoney(connection,tx,characterId);
            var result=new List<EnhancementPreviewItem>();
            ReadEnhancementPreviewTable(connection,tx,"item_instances",
                EnhancementItemLocation.Inventory,characterId,itemRules,result);
            ReadEnhancementPreviewTable(connection,tx,"equipped_items",
                EnhancementItemLocation.Equipped,characterId,itemRules,result);
            result.Sort((a,b)=>a.ItemUid.CompareTo(b.ItemUid));
            if(result.Count>4095)
                throw new InvalidDataException(
                    "Enhancement preview exceeds bounded item count");
            tx.Commit();
            return new EnhancementPreview(wallet,result);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<EnhancementResult> EnhanceItemAsync(
        TrustedEnhancementContext context,EnhancementAccessStatus access,
        TrustedEnhancementNpcRule npcRule,
        IReadOnlyDictionary<int,EquipmentEnhancementItemRule> itemRules,
        long itemUid,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ValidateEnhancementRequest(context,npcRule,itemRules,itemUid);
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            EnsureWallet(connection,tx,context.CharacterId);

            using(var replay=connection.CreateCommand())
            {
                replay.Transaction=tx;
                replay.CommandText=@"
SELECT npc_spawn_id,item_uid,item_definition_id,item_location,
       level_before,target_level,level_after,success_ppm,roll_ppm,succeeded,
       money_cost,money_charged,wallet_after
FROM enhancement_actions
WHERE character_id=$actor AND connection_generation=$conn
  AND map_epoch=$epoch AND action_sequence=$seq;";
                replay.Parameters.AddWithValue("$actor",context.CharacterId);
                replay.Parameters.AddWithValue("$conn",context.ConnectionGeneration);
                replay.Parameters.AddWithValue("$epoch",context.MapEpoch);
                replay.Parameters.AddWithValue("$seq",(long)context.ActionSequence);
                using var reader=replay.ExecuteReader();
                if(reader.Read())
                {
                    int originalNpc=reader.GetInt32(0);
                    long originalUid=reader.GetInt64(1);
                    if(originalNpc!=npcRule.NpcSpawnId||originalUid!=itemUid)
                        return Reject(
                            EnhancementStatus.StaleSequence,npcRule.NpcSpawnId,
                            itemUid,ReadMoney(connection,tx,context.CharacterId));
                    return new(
                        EnhancementStatus.AlreadyApplied,originalNpc,originalUid,
                        reader.GetInt32(2),(EnhancementItemLocation)reader.GetInt32(3),
                        reader.GetInt32(4),reader.GetInt32(5),reader.GetInt32(6),
                        reader.GetInt32(9)!=0,reader.GetInt64(10),
                        reader.GetInt64(11),reader.GetInt64(12),reader.GetInt32(8));
                }
            }

            long last=ScalarLong(connection,@"
SELECT COALESCE(MAX(action_sequence),0) FROM enhancement_actions
WHERE character_id=$actor AND connection_generation=$conn AND map_epoch=$epoch;",tx,
                ("$actor",context.CharacterId),
                ("$conn",context.ConnectionGeneration),
                ("$epoch",context.MapEpoch));
            if(last>=context.ActionSequence)
                return Reject(
                    EnhancementStatus.StaleSequence,npcRule.NpcSpawnId,itemUid,
                    ReadMoney(connection,tx,context.CharacterId));

            var accessFailure=EnhancementAccessFailure(access);
            if(accessFailure is not null)
                return Reject(accessFailure.Value,npcRule.NpcSpawnId,itemUid,
                    ReadMoney(connection,tx,context.CharacterId));

            var owned=ReadEnhancementItem(
                connection,tx,context.CharacterId,itemUid);
            if(owned is null)
                return Reject(
                    EnhancementStatus.NotOwned,npcRule.NpcSpawnId,itemUid,
                    ReadMoney(connection,tx,context.CharacterId));

            if(!itemRules.TryGetValue(owned.ItemDefinitionId,out var itemRule))
                return new(
                    EnhancementStatus.NoRule,npcRule.NpcSpawnId,itemUid,
                    owned.ItemDefinitionId,owned.Location,owned.Level,0,owned.Level,
                    false,0,0,ReadMoney(connection,tx,context.CharacterId),0);

            if(!itemRule.Steps.TryGetValue(owned.Level,out var step))
            {
                int max=itemRule.Steps.Values.Max(x=>x.ToLevel);
                var status=owned.Level>=max
                    ?EnhancementStatus.MaxLevel:EnhancementStatus.NoRule;
                return new(
                    status,npcRule.NpcSpawnId,itemUid,owned.ItemDefinitionId,
                    owned.Location,owned.Level,0,owned.Level,false,0,0,
                    ReadMoney(connection,tx,context.CharacterId),0);
            }

            long walletBefore=ReadMoney(connection,tx,context.CharacterId);
            if(step.MoneyCost>walletBefore)
                return new(
                    EnhancementStatus.InsufficientMoney,npcRule.NpcSpawnId,itemUid,
                    owned.ItemDefinitionId,owned.Location,owned.Level,step.ToLevel,
                    owned.Level,false,step.MoneyCost,0,walletBefore,0);

            int roll=step.SuccessPpm switch
            {
                <=0=>0,
                >=EquipmentEnhancementCatalog.ProbabilityScale=>0,
                _=>RandomNumberGenerator.GetInt32(
                    EquipmentEnhancementCatalog.ProbabilityScale)
            };
            bool succeeded=step.SuccessPpm>0&&roll<step.SuccessPpm;
            long charged=succeeded||step.ConsumeMoneyOnFailure
                ?step.MoneyCost:0;
            long walletAfter=walletBefore-charged;
            int levelAfter=succeeded?step.ToLevel:owned.Level;

            if(succeeded)
            {
                string table=owned.Location==EnhancementItemLocation.Equipped
                    ?"equipped_items":"item_instances";
                using var update=connection.CreateCommand();
                update.Transaction=tx;
                update.CommandText=$@"
UPDATE {table} SET enhancement_level=$level
WHERE item_uid=$uid AND character_id=$actor AND enhancement_level=$before;";
                update.Parameters.AddWithValue("$level",levelAfter);
                update.Parameters.AddWithValue("$uid",itemUid);
                update.Parameters.AddWithValue("$actor",context.CharacterId);
                update.Parameters.AddWithValue("$before",owned.Level);
                if(update.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException(
                        "Enhancement item changed during transaction");
            }
            if(charged>0)
                UpdateMoney(connection,tx,context.CharacterId,walletAfter);

            using(var receipt=connection.CreateCommand())
            {
                receipt.Transaction=tx;
                receipt.CommandText=@"
INSERT INTO enhancement_actions(
    character_id,connection_generation,map_epoch,action_sequence,npc_spawn_id,
    item_uid,item_definition_id,item_location,level_before,target_level,level_after,
    success_ppm,roll_ppm,succeeded,money_cost,money_charged,wallet_after,
    source_reference,created_unix_ms)
VALUES($actor,$conn,$epoch,$seq,$npc,$uid,$item,$location,$before,$target,$after,
       $ppm,$roll,$success,$cost,$charged,$wallet,$source,$now);";
                receipt.Parameters.AddWithValue("$actor",context.CharacterId);
                receipt.Parameters.AddWithValue("$conn",context.ConnectionGeneration);
                receipt.Parameters.AddWithValue("$epoch",context.MapEpoch);
                receipt.Parameters.AddWithValue("$seq",(long)context.ActionSequence);
                receipt.Parameters.AddWithValue("$npc",npcRule.NpcSpawnId);
                receipt.Parameters.AddWithValue("$uid",itemUid);
                receipt.Parameters.AddWithValue("$item",owned.ItemDefinitionId);
                receipt.Parameters.AddWithValue("$location",(int)owned.Location);
                receipt.Parameters.AddWithValue("$before",owned.Level);
                receipt.Parameters.AddWithValue("$target",step.ToLevel);
                receipt.Parameters.AddWithValue("$after",levelAfter);
                receipt.Parameters.AddWithValue("$ppm",step.SuccessPpm);
                receipt.Parameters.AddWithValue("$roll",roll);
                receipt.Parameters.AddWithValue("$success",succeeded?1:0);
                receipt.Parameters.AddWithValue("$cost",step.MoneyCost);
                receipt.Parameters.AddWithValue("$charged",charged);
                receipt.Parameters.AddWithValue("$wallet",walletAfter);
                receipt.Parameters.AddWithValue("$source",step.SourceReference);
                receipt.Parameters.AddWithValue(
                    "$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if(receipt.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException(
                        "Enhancement durable receipt failed");
            }

            tx.Commit();
            return new(
                EnhancementStatus.Applied,npcRule.NpcSpawnId,itemUid,
                owned.ItemDefinitionId,owned.Location,owned.Level,step.ToLevel,
                levelAfter,succeeded,step.MoneyCost,charged,walletAfter,roll);
        },cancellationToken).ConfigureAwait(false);
    }

    private static void ReadEnhancementPreviewTable(
        SqliteConnection connection,SqliteTransaction tx,string table,
        EnhancementItemLocation location,long characterId,
        IReadOnlyDictionary<int,EquipmentEnhancementItemRule> itemRules,
        List<EnhancementPreviewItem> result)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=$@"SELECT item_uid,item_definition_id,quantity,enhancement_level
FROM {table} WHERE character_id=$actor ORDER BY item_uid;";
        command.Parameters.AddWithValue("$actor",characterId);
        using var reader=command.ExecuteReader();
        while(reader.Read())
        {
            long uid=reader.GetInt64(0);
            int definition=reader.GetInt32(1);
            int quantity=reader.GetInt32(2);
            int level=reader.GetInt32(3);
            if(quantity!=1||uid<=0||
               !itemRules.TryGetValue(definition,out var itemRule)||
               !itemRule.Steps.TryGetValue(level,out var step))
                continue;
            result.Add(new(uid,definition,location,level,step.ToLevel,
                step.SuccessPpm,step.MoneyCost,step.ConsumeMoneyOnFailure));
        }
    }

    private static EnhancementOwnedItem? ReadEnhancementItem(
        SqliteConnection connection,SqliteTransaction tx,long characterId,long itemUid)
    {
        foreach(var (table,location) in new[]
        {
            ("item_instances",EnhancementItemLocation.Inventory),
            ("equipped_items",EnhancementItemLocation.Equipped)
        })
        {
            using var command=connection.CreateCommand();
            command.Transaction=tx;
            command.CommandText=$@"
SELECT item_definition_id,quantity,enhancement_level
FROM {table} WHERE item_uid=$uid AND character_id=$actor;";
            command.Parameters.AddWithValue("$uid",itemUid);
            command.Parameters.AddWithValue("$actor",characterId);
            using var reader=command.ExecuteReader();
            if(!reader.Read())continue;
            int quantity=reader.GetInt32(1);
            if(quantity!=1)
                return null;
            return new(
                reader.GetInt32(0),(EnhancementItemLocation)location,
                reader.GetInt32(2));
        }
        return null;
    }

    private static EnhancementResult Reject(
        EnhancementStatus status,int npc,long itemUid,long wallet)=>
        new(status,npc,itemUid,0,EnhancementItemLocation.None,0,0,0,
            false,0,0,wallet,0);

    private static EnhancementStatus? EnhancementAccessFailure(
        EnhancementAccessStatus access)=>
        access switch
        {
            EnhancementAccessStatus.Allowed=>null,
            EnhancementAccessStatus.StaleGeneration=>EnhancementStatus.StaleGeneration,
            EnhancementAccessStatus.UnknownEnhancer=>EnhancementStatus.UnknownEnhancer,
            EnhancementAccessStatus.TooFar=>EnhancementStatus.TooFar,
            EnhancementAccessStatus.PlayerDead=>EnhancementStatus.PlayerDead,
            _=>EnhancementStatus.StaleGeneration
        };

    private static void ValidateEnhancementNpcRule(
        TrustedEnhancementNpcRule npcRule)
    {
        if(npcRule.NpcSpawnId<=0||
           string.IsNullOrWhiteSpace(npcRule.SourceReference)||
           npcRule.SourceReference.Length>512)
            throw new ArgumentOutOfRangeException(nameof(npcRule));
    }

    private static void ValidateEnhancementRequest(
        TrustedEnhancementContext context,TrustedEnhancementNpcRule npcRule,
        IReadOnlyDictionary<int,EquipmentEnhancementItemRule> itemRules,long itemUid)
    {
        ArgumentNullException.ThrowIfNull(itemRules);
        ValidateEnhancementNpcRule(npcRule);
        if(context.CharacterId<=0||context.ConnectionGeneration<=0||
           context.MapEpoch<=0||context.ActionSequence==0||itemUid<=0)
            throw new ArgumentOutOfRangeException(nameof(context));
    }

    private sealed record EnhancementOwnedItem(
        int ItemDefinitionId,EnhancementItemLocation Location,int Level);
}
